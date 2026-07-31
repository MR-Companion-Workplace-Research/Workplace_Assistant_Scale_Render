# Scripts Overview

This directory contains the core scripts for the MR agent project — a Mixed Reality application that spawns an AI-powered avatar on the user's desk using Meta Quest's Scene Understanding (MRUK).

## Session flow

The app boots into **Launch_Scene**, not the study scene:

```
Launch_Scene                                          MR_Scene
  STUDY SETUP   ── config ──┐                           STUDY CONTROL
  (researcher edits THIS)   ├─► StudySession ─────────► (receives, then
  LAUNCH MENU   ── task ────┘    (static carrier)         pushes as before)
  (participant picks, START)
```

**What the researcher sets, and when:**

| Value | Where | How often |
|---|---|---|
| Participant ID | `STUDY SETUP` in Launch_Scene | once per participant, before the build |
| Scale / Render Style / placement | `STUDY SETUP` in Launch_Scene | once per participant, before the build |
| Task (A–H) | the in-headset launch menu | every run, by the participant |

Risk Level is within-subjects, so each participant runs several tasks. Selecting the task at
runtime is what removes the Android rebuild that would otherwise be needed between trials.

**STUDY SETUP lives in Launch_Scene, not MR_Scene**, because the menu displays the participant
id — and while the menu is running MR_Scene isn't loaded, so its STUDY CONTROL object doesn't
exist yet to be read from. Putting the config in the scene that boots first gives one edit point
*and* a menu that can show it, with no second copy to drift.

The launch menu shows the participant id (`受試者ID：P07`) and eight bare `任務 A`…`任務 H`
buttons. It shows **neither the condition nor the task names** — either would reveal the
manipulation (see the class header on `LaunchMenu.cs`).

The menu is in Traditional Chinese, matching `SwotPanel`, and uses the same `Assets/Fonts/msjh
SDF` asset (wired automatically by the scene generator). That atlas is **static**: characters
not baked into it do not render and do not fall back. If you reword `instruction`, verify the
new characters actually appear on the headset. The task *letters* stay Latin so they match what
the researcher says aloud and what the task keys and log files use.

Regenerate the scene with **Tools > Study > Create or Refresh Launch Scene**.

Opening `MR_Scene` directly still works: with no session to read from, it runs entirely off
`StudyControlPanel`'s own inspector values. Those fields are the **dev fallback** — in a real
session every one of them is overwritten at `Awake`, so don't configure a participant there.

### Practice app

The practice APK mirrors the same two-scene flow, so participants rehearse the *whole* process
rather than just the SWOT button:

```
DemoLaunch_Scene  ──►  DemoScene
(from Launch_Scene)    (from MR_Scene)
```

Its menu shows `練習模式` instead of a participant id (the APK is built once and sideloaded to
every headset), has its `STUDY SETUP` object **deleted** — so no participant's identity or
condition is compiled into it — and loads `DemoScene`. The A–H buttons are kept
and stay bare letters — practising the choice reveals no task content. `SwotPanel` runs with
`demoBlankContent`, so the sheet shows its headers with empty quadrants.

Regenerate both with **Tools > Study > Create or Refresh Demo Scene** (needs `Launch_Scene` to
exist first).

## Architecture

In `MR_Scene`, one **STUDY CONTROL** object (`StudyControlPanel`) applies the per-trial config to
`AvatarPlacer` and `ElevenLabsConnection` in `Awake`. `AvatarPlacer` then spawns the avatar on an
MRUK anchor and wires everything to the freshly-spawned instance at runtime:

```
StudyControlPanel  ("STUDY CONTROL")           ← pulls config from StudySession, pushes it out
 ├── AvatarPlacer                               spawns the avatar variant on an MR anchor, then wires:
 │     ├── SALSA  (Salsa / Emoter / Eyes)         lip-sync + facial emotes + gaze   (live on the prefab)
 │     ├── SalsaExternalAudioFeed                 feeds live playback amplitude into SALSA
 │     └── EmoterController → animDriver          facial-emote + body-gesture animator hooks
 ├── AgentVoiceController                        mic capture, PCM streaming, audio playback
 │     ├── ElevenLabsConnection                    voice backend — PRIMARY
 │     └── RealtimeAPIConnection                   voice backend — OpenAI (legacy/alt)
 ├── ElevenLabsEmoteBridge                       parses [emotion tags] in transcript → facial emote
 ├── AvatarBodyGestures                          connection/response events → wave / gesture / head-nod
 ├── ConversationLogger                          on-device JSONL transcript + timing log
 └── SwotPanel                                   world-space SWOT sheet, controller-triggered
```

Lip-sync, gaze, and blinking all live in **SALSA** on the avatar prefab (rebound to each new CC avatar
with **Tools > Study > Bind SALSA To New Avatar** — see `../../Editor/SalsaAvatarBinder.md`).
`AvatarPlacer` only points SALSA's audio and look-target at the right runtime objects; it no longer
attaches the older `HeadLookAt` / `SimpleAutoBlinker` components (see **Legacy** below).

## Scripts

### Study configuration

#### StudyControlPanel.cs — the single per-trial control surface

Sits on `STUDY CONTROL` in `MR_Scene`. In `Awake` (with an early execution order) it **pulls** the
researcher's config and the participant's task from `StudySession`, then **pushes** every per-trial knob
(avatar variant, scale condition + value, placement, surface contact) into `AvatarPlacer` and the task
key into `ElevenLabsConnection`. `ConversationLogger` reads participant id / condition label straight off
this panel. Its inspector fields are the **dev fallback** used when `MR_Scene` is opened directly.

#### StudySetup.cs / StudyConfig.cs / StudySession.cs

`StudySetup` sits on `STUDY SETUP` in Launch_Scene and is **the one object the researcher edits per
participant**; it holds a `StudyConfig` and publishes it to `StudySession` in `Awake`. `StudyConfig` is
the `[Serializable]` field list declared **once** and shared by `StudySetup`, `StudySession`, and
`StudyControlPanel`, so the same fields can't drift across three copies. `StudySession` is a **static
carrier** (not `DontDestroyOnLoad`, so `MR_Scene` stays openable on its own) holding the config and the
picked task; `HasConfig` / `HasTask` are false when a scene is opened directly.

#### LaunchMenu.cs

The participant-facing passthrough-MR menu. Builds its world-space canvas at runtime, world-locks it in
front of the participant, and hit-tests controller rays against the button rects directly (no
EventSystem/OVRRaycaster stack, so a mis-wire can't silently kill this critical path). Draws a visible
pointer laser (the controller models are hidden in MR), clicks with trigger or A/X, re-centres with B/Y,
and falls back to head-gaze, then to keyboard `1`–`8` + `Return` in the editor.

### Avatar placement & animation

#### AvatarPlacer.cs — the entry point

When MRUK finishes scanning the room, this finds the configured scene anchor (`spawnAnchorLabel` — a
`TABLE`/desk by default, but `COUCH`, `BED`, `FLOOR`, … can be selected) and spawns the avatar there.

**Responsibilities:**
- Resolves which of the four **avatar-variant prefabs** to spawn (Female/Male × Real/Toon), with a legacy
  single-prefab fallback.
- Instantiates the avatar, applies the uniform **scale**, and snaps its **contact point** to the surface —
  *feet on surface* (standing/miniature) or *hips on cushion* (seated/life-sized) — measured after scale +
  pose so placement is scale-independent.
- Attaches the **Scale-condition Animator Controller** (`humanSizedAnimator` / `miniatureAnimator`) and
  warns if it lacks the body-gesture triggers or if scale looks inconsistent with the condition.
- Rotates the avatar to face the user (via `OVRCameraRig`).
- Sets two shader globals for the toon look: `_OutlineScale` (outline thickness ∝ avatar scale) and
  `_ToonKeyDir` (a virtual key light from `toonKeyLocalDirection`, kept glued to the avatar's facing).
- Wires the runtime plumbing: points **SALSA Eyes** at the user for gaze, feeds the playback audio to
  **SALSA** for lip-sync (via `SalsaExternalAudioFeed`), links `ElevenLabsEmoteBridge` to the avatar's
  `EmoterController`, sets up the spatial `AudioSource`, and auto-connects the voice backend.
- Exposes runtime control: `SetGazeTracking()`, `UpdateFacing()`, `SetScale()`, `SetScaleCondition()`.

When `useSalsaLipSync` is on (the default for the CC prefab), SALSA owns the mouth and
`AgentVoiceController`'s built-in RMS lip-sync is disabled so the two never fight.

#### AvatarBodyGestures.cs

Drives the avatar's **body** animations from ElevenLabs events, through `EmoterController → animDriver →
Animator` triggers (`WaveTrigger` / `GestureTrigger` / `HeadNodTrigger`):

- **Greeting wave** once each time a conversation connects (`OnConnected`).
- On each **new agent response** (`OnNewResponse`) there is a configurable chance (`gestureChance`) to play
  a gesture at all; `headNodShare` then splits it between a head-nod and the talking gestures. It only
  fires when the avatar is resting in an idle it can gesture out of, and drops stale triggers so a gesture
  can't fire late (after the agent has gone quiet). Adapts to whichever Scale-condition controller is
  attached (e.g. falls back if the controller has no `Gesture2Trigger`).

The `EmoterController` is resolved lazily from `AvatarPlacer`'s spawned instance — no manual wiring.
(This replaces the deleted `GestureTrigger.cs`, which was incompatible with the ElevenLabs event model.)

### Voice pipeline

#### AgentVoiceController.cs

Central controller for voice interaction: microphone capture, streamed audio playback, and (fallback)
lip-sync. Works with either `ElevenLabsConnection` or `RealtimeAPIConnection`.

- **Mic:** captures the Quest mic as a ring buffer, converts float PCM → PCM16 base64, and streams it to
  the active backend. `micGain` / `noiseGateThreshold` tune input; `SetMicMuted()` can suppress sending
  without stopping the hardware. While the agent speaks the mic is normally muted (half-duplex), but with
  **barge-in** enabled on the connection it keeps streaming so the user can interrupt.
- **Playback:** streams received PCM16 chunks through a `ConcurrentQueue<float>` into a read-callback
  `AudioClip`, pre-buffering `preBufferSamples` to avoid stutter and padding `drainDelaySeconds` of silence
  so the last word isn't clipped; clears the queue on interruption.
- **Lip-sync (fallback only):** an RMS-amplitude mouth blendshape driver, **used only when
  `AvatarPlacer.useSalsaLipSync` is off**. In the study build SALSA owns the mouth, so this is disabled
  (face mesh passed as null).

**Public API:** `Initialize()`, `StartListening()`, `StopListening()`, `SetMicMuted(bool)`.

#### ElevenLabsConnection.cs

WebSocket client for the [ElevenLabs Conversational AI](https://elevenlabs.io) agent API. The agent's
persona, voice, language, and first message live in the ElevenLabs dashboard; this script connects,
streams audio, and injects the per-trial task.

- **Task injection.** `taskKey` (`low_A` … `high_H`) selects a scenario from the built-in `TASK_BLOCKS`
  table (Traditional Chinese, sourced from `swot_chinese.md`); the resolved `taskContext` is sent as the
  `{{task_context}}` dynamic variable at conversation initiation. `conversation_config_override` is kept
  **empty on purpose** — the persona must be byte-identical across every visual cell. `RestartWithTask()`
  applies a new task to a live session by reconnecting (dynamic variables are read only at init).
- **Barge-in.** `allowInterruption` (default on) keeps the mic streaming during agent speech and honours
  `interruption` events. **Requires headphones on Quest** or the open mic hears the avatar and self-
  interrupts.
- **Events consumed by the rest of the system:** `OnConnected` (greeting), `OnNewResponse` (gesture
  choice), `OnTranscriptDone` (facial emote + log), `OnUserTranscript`, `OnAudioDelta/Done`,
  `OnInterruption`, `OnError`, `OnAnyServerEvent`.
- **Server events handled:** `conversation_initiation_metadata`, `audio`, `agent_response`,
  `agent_response_correction`, `user_transcript`, `interruption`, `agent_response_end`, `mode_change`,
  `ping`.
- **Public API:** `Connect()` / `Disconnect()`, `SendAudio()`, `SendTextMessage()`,
  `SendContextualUpdate()`, `SendActivityPing()`, `TriggerAgentInitiation()`, `SetTaskByKey()`,
  `RestartWithTask()`.

#### RealtimeAPIConnection.cs

WebSocket client for the [OpenAI Realtime API](https://platform.openai.com/docs/guides/realtime). The
alternative voice backend, kept for comparison; the study runs on ElevenLabs. Configures voice, PCM16
audio, Whisper transcription, and server-side VAD, and exposes `Connect()`/`Disconnect()`, `SendAudio()`,
`SendEvent()`, and `MakeAISpeak()`.

#### SalsaExternalAudioFeed.cs

Bridges streamed playback into SALSA. `AgentVoiceController` plays the agent's voice through a read-
callback `AudioClip` that SALSA can't sample directly, so SALSA is switched to **external analysis** and
fed the live output amplitude of the playback `AudioSource` each frame — mouth animation that tracks
whatever the agent is currently saying, with no need for the finished clip.

### Emotes, gestures & gaze

#### ElevenLabsEmoteBridge.cs

Parses the agent transcript for a leading Eleven v3 emotion tag (`[happy]`, `[sad]`, `[empathetic]`,
`[thoughtful]`, …) and fires the matching facial emote on the avatar's `EmoterController`, bucketed into
*Positive / Negative / Neutral / Thinking* (with a configurable fallback when no tag is present). With the
"Eleven v3 Conversational" model and "strip audio tags" off, those tags are both spoken with the matching
delivery and still arrive in the transcript for parsing. `AvatarPlacer` links it to the avatar via
`SetEmoter()`.

> Facial emotes (this script) and body gestures (`AvatarBodyGestures`) run on separate channels, so the
> avatar can talk, emote, and gesture at once.

### UI & logging

#### SwotPanel.cs

The participant's world-space **SWOT sheet**, rendered with TextMeshPro (SDF) so it stays crisp at any
scale. It reads `ElevenLabsConnection.taskKey` at show-time so panel and agent can't drift onto different
tasks, and is revealed by a **Controller Buttons Mapper** (B/Y) wired to `ShowSwot()`, then auto-hides.
The canvas is its **own root object** sized in absolute meters, so it's identical across the Scale/Render
conditions (it's a measurement instrument). Default anchor mode rides the participant's controller like a
held sheet of paper; a Head anchor mode world-locks it in front of the participant as a fallback. Content
is zh-TW and requires a CJK TMP font in `fontOverride`; `demoBlankContent` blanks the quadrants for the
practice build.

#### ConversationLogger.cs

Writes a per-session log to a **file on the headset**
(`Application.persistentDataPath/ConversationLogs/<participant>/…jsonl`) so you get the full transcript +
timing from a standalone APK. Subscribes to `ElevenLabsConnection` events and writes **JSONL** (one record
per line): session start/end, connect/disconnect, conversation-init metadata (conversation id, audio
formats, task key), user/agent messages, the agent's precise speaking interval, interruptions/corrections,
and errors — each stamped with wall-clock ISO-8601 time and ms-since-start. Can also mirror the whole Unity
console to a companion `.log`. Identity is read from `StudyControlPanel`. Retrieve with `adb pull` (or
`Tools/pull-logs.ps1`) over USB — no Quest Link needed.

### Supporting / legacy scripts

- **EmoterController.cs / animDriver.cs** (`../ECA/emoter files/`) — the avatar-side emote/gesture layer
  the bridges and `AvatarBodyGestures` invoke; expose the `emoterEvent*` / `waveAnim` / `gestureAnim` /
  `headnodAnim` hooks and drive the Animator through `animDriver`.
- **IdleVariantSwitcher.cs** — swaps between idle variants (e.g. hands-on-thigh) on the gesture layer.
- **SceneDepthOccluder.cs** — helper for the Depth-API occlusion setup.
- **HeadImageTag.cs** — the older baked-PNG info tag, superseded by `SwotPanel` (kept for reference).
- **EmoteDebugger.cs** — on-screen readout of what `ElevenLabsEmoteBridge` detected, for diagnosis.
- **HeadLookAt.cs** — *legacy.* Head-bone gaze that predated SALSA Eyes; no longer wired by `AvatarPlacer`
  because it fought SALSA over the head bone. Gaze now comes from SALSA's Eyes module.
- **SimpleAutoBlinker.cs** — *legacy.* Coroutine blink driver; blinking is now handled by SALSA's rebuilt
  eyelid expressions (both eyes), so this is not attached in the study prefabs.

## Dependencies

- **Meta XR SDK** — `OVRCameraRig`, MRUK (`Meta.XR.MRUtilityKit`) for scene understanding, Depth API
  (`EnvironmentDepthManager`) for occlusion.
- **SALSA LipSync** (Crazy Minnow Studio) — `Salsa` / `Emoter` / `Eyes` for lip-sync, facial emotes,
  gaze, and blink.
- **Newtonsoft JSON** (`com.unity.nuget.newtonsoft-json`) — WebSocket message + log serialization.
- **Unity Input System** — required by Meta XR SDK.
- **Built-in Render Pipeline (BiRP)** — the avatar's toon/occlusion shaders target BiRP (see
  `Assets/Toon Shaders/README.md`).
