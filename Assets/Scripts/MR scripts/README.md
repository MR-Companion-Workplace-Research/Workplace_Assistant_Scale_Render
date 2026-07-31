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

```
AvatarPlacer (entry point)
 ├── HeadLookAt              (gaze tracking)
 ├── SimpleAutoBlinker        (eye blink animation)
 ├── GestureTrigger           (upper-body gestures)
 └── AgentVoiceController (mic, playback, lip sync)
      ├── ElevenLabsConnection   (voice backend option A)
      └── RealtimeAPIConnection  (voice backend option B)

ExperimenterRemoteTrigger (UDP remote control from experimenter PC)
```

## Scripts

### StudySetup.cs — **the one object the researcher edits**

Sits on `STUDY SETUP` in Launch_Scene. Holds a `StudyConfig` (participant id, condition label,
avatar variant, scale condition, all placement fields) and publishes it to `StudySession` in
`Awake`. Its custom editor draws the fields flat, so the nesting stays an implementation detail.

Per participant, this is the only thing you change.

---

### StudyConfig.cs

The `[Serializable]` field list itself, declared **once** and shared by `StudySetup`,
`StudySession` and `StudyControlPanel`. Three hand-maintained copies of the same twelve fields
would drift as fields are added; copying one object cannot.

---

### StudySession.cs

Static carrier across the scene load, holding two independent things: the researcher's config
(from `StudySetup`) and the participant's task (from `LaunchMenu`).

A static rather than a `DontDestroyOnLoad` object so that `MR_Scene` stays openable on its own
in the editor: `HasConfig`/`HasTask` are simply false and `StudyControlPanel` falls back to its
own inspector values.

---

### LaunchMenu.cs

The participant-facing passthrough-MR menu. Builds its world-space canvas at runtime (same
approach as `SwotPanel`), world-locks it in front of the participant, and hit-tests controller
rays against the button rects directly.

**Why no EventSystem / OVRInputModule / OVRRaycaster:** that stack needs four things wired
together and silently does nothing if any one is wrong. This is a participant-facing critical
path where a dead menu ends the session, so one ray drives the drawn laser, the cursor dot, the
hover tint and the click — what is highlighted is always exactly what will be pressed.

**Pointer ray:** a visible laser is drawn from the controller (`showPointerRay`, on by default).
This is not decoration — the controller models are hidden in the MR scenes, so with only a cursor
dot there is nothing on screen at all until the participant's aim happens to cross the panel, and
pointing becomes trial and error. The ray stops at the panel when it hits and extends
`rayMaxLength` when it does not, and brightens on hover. It is deliberately **not** drawn for the
head-gaze fallback, where a line from the eye is just a smear at the centre of view.

**Input:** either index trigger or A/X to click; **B/Y re-centres** the panel if the
participant has turned away. Falls back to a head-gaze ray if no controller is tracked, and to
keyboard `1`–`8` + `Return` in the editor. Only *connected* controllers are cast from — a hand
anchor holds its last pose after its controller sleeps, which would otherwise let a stale pose
steal the hover.

---

### AvatarPlacer.cs

The main entry point. When MRUK finishes scanning the room, this script finds the configured scene anchor (a `TABLE`/desk by default, but `COUCH`, `BED`, `FLOOR`, etc. can be selected) and spawns the avatar prefab on it.

**Responsibilities:**
- Listens for MRUK scene-loaded callback
- Searches room anchors for the configured surface type (`spawnAnchorLabel`)
- Instantiates the avatar at the anchor position with a configurable offset and scale
- Rotates the avatar to face the user (via `OVRCameraRig`)
- Attaches `HeadLookAt` for gaze tracking
- Locates the face mesh (by name or by blendshape search) and wires up `AgentVoiceController` with an `AudioSource`, face mesh, and mouth blendshape index
- Auto-connects to the selected voice backend (ElevenLabs or OpenAI)
- Exposes runtime control: `SetGazeTracking()`, `UpdateFacing()`, `SetScale()`

**Inspector fields:** `avatarPrefab`, `cameraRig`, `spawnAnchorLabel`, `positionOffset`, `avatarScale`, `faceMeshName`, `mouthBlendShapeName`

---

### HeadLookAt.cs

Rotates the avatar's head bone toward a target (the user's head) in `LateUpdate`, blending on top of whatever animation is playing.

**Key features:**
- Auto-detects the head bone from the `Animator` (`HumanBodyBones.Head`)
- Weighted blend (`weight` 0-1) so the head only partially turns
- Horizontal and vertical angle limits to prevent unnatural over-rotation
- Smooth interpolation via `Quaternion.Slerp` at a configurable `smoothSpeed`
- Graceful blend-out when disabled — the head smoothly returns to animation control instead of snapping

**Public API:** `SetEnabled(bool)` — enables/disables tracking with smooth transitions

---

### AgentVoiceController.cs

Central controller for voice interaction. Handles microphone capture, audio playback, and lip sync. Works with either `ElevenLabsConnection` or `RealtimeAPIConnection`.

**Microphone capture:**
- Captures audio from the Quest's mic as a ring buffer (`Microphone.Start`)
- Converts float PCM to PCM16 base64 and sends it to the active voice backend
- Configurable `micGain` (amplification) and `noiseGateThreshold` (silence filtering)
- Suppresses mic input while the AI is speaking to avoid echo/self-interruption
- `isMicMuted` flag allows the experimenter to mute the mic without stopping hardware recording

**Audio playback:**
- Receives base64 PCM16 audio chunks from the voice API
- Streams audio via a `ConcurrentQueue<float>` fed into an `AudioClip` PCM read callback
- Pre-buffers a configurable number of samples (`preBufferSamples`) before starting playback to avoid stuttering
- Adds silent padding (`drainDelaySeconds`) after the AI finishes speaking so hardware DSP doesn't cut off the last word
- Handles interruptions by clearing the queue and stopping playback immediately

**Lip sync:**
- Reads `AudioSource.GetOutputData` RMS amplitude each frame
- Maps amplitude to a mouth-open blendshape weight via `mouthSensitivity`
- Smoothly interpolates mouth movement with `mouthSmoothSpeed`

**Public API:** `Initialize()`, `StartListening()`, `StopListening()`, `SetMicMuted(bool)`

**Inspector fields:** `backend` (OpenAI / ElevenLabs), `sampleRate`, `micGain`, `noiseGateThreshold`, `preBufferSamples`, `drainDelaySeconds`, `mouthSensitivity`, `mouthSmoothSpeed`

---

### ElevenLabsConnection.cs

WebSocket client for the [ElevenLabs Conversational AI](https://elevenlabs.io) agent API. The agent's personality, voice, language, and first message are configured in the ElevenLabs dashboard — this script just connects and streams audio.

**Connection flow:**
1. Opens a WebSocket to `wss://api.elevenlabs.io/v1/convai/conversation?agent_id=...`
2. Sends `conversation_initiation_client_data` to use dashboard defaults
3. Enters a receive loop, dispatching events to the main thread via `ConcurrentQueue<Action>`

**Server events handled:** `conversation_initiation_metadata`, `audio`, `agent_response`, `user_transcript`, `interruption`, `agent_response_end`, `mode_change`, `ping`

**Public API:**
- `Connect()` / `Disconnect()` — lifecycle
- `SendAudio(base64)` — stream mic audio to the agent
- `SendTextMessage(text)` — send text as if the user spoke (triggers a response)
- `SendContextualUpdate(text)` — silently inform the agent without triggering a response
- `SendActivityPing()` — prevent session timeout during passive phases
- `TriggerAgentInitiation()` — two-step initiation: contextual update + user message to make the agent start talking naturally

**Inspector fields:** `agentId`, `apiKey` (optional, for private agents), `inputSampleRate`, `outputSampleRate`

---

### RealtimeAPIConnection.cs

WebSocket client for the [OpenAI Realtime API](https://platform.openai.com/docs/guides/realtime). Alternative voice backend to ElevenLabs.

**Connection flow:**
1. Opens a WebSocket to `wss://api.openai.com/v1/realtime?model=...` with Bearer auth
2. Sends a `session.update` event configuring voice, audio format (PCM16), Whisper transcription, and server-side VAD
3. Enters a receive loop

**Server events handled:** `session.created`, `session.updated`, `response.audio.delta`, `response.audio.done`, `response.audio_transcript.delta/done`, `conversation.item.input_audio_transcription.completed`, `error`, `input_audio_buffer.speech_started/stopped`

**Public API:**
- `Connect()` / `Disconnect()` — lifecycle
- `SendEvent(object)` — send any JSON event
- `SendAudio(base64)` — stream mic audio
- `MakeAISpeak(prompt)` — inject a user message and trigger a response (for agent-initiated conversation)

**Inspector fields:** `apiKey`, `model`, `voice`, `instructions`

---

### ExperimenterRemoteTrigger.cs

UDP server that listens for commands from the experimenter's PC/laptop, enabling remote control of the agent during study sessions. Both devices must be on the same WiFi network.

**Supported commands:**

| Command | Description |
|---|---|
| `INITIATE` | Trigger agent-initiated conversation (default prompt) |
| `INITIATE:custom text` | Trigger with custom context |
| `CONTEXT:some info` | Silent contextual update (no response) |
| `PING` | Manual activity ping |
| `END_CONVERSATION` | AI wraps up and says goodbye |
| `STATUS` | Returns current connection and mic status |
| `MIC:ON` / `MIC:OFF` | Mute/unmute user microphone |
| `GAZE:ON` / `GAZE:OFF` | Enable/disable avatar gaze tracking |

**Network:** Listens on UDP port `9100` (configurable). The Quest's IP is logged to the console on startup. Replies are sent back to the experimenter's address.

**Auto keep-alive:** Optionally sends `user_activity` pings every 25 seconds to prevent ElevenLabs session timeout during passive co-presence phases.

---

### SimpleAutoBlinker.cs

Coroutine-based auto-blink animation. Drives a blink blendshape through close-hold-open cycles at random intervals.

**Key features:**
- Searches child objects for the face mesh by name, with fallback to any mesh containing the specified blendshape
- Supports both standard (0=open, 100=closed) and inverted blendshape ranges
- Auto-corrects `openValue` at runtime if the Animator has already set a non-zero rest pose
- Configurable timing: `minInterval`/`maxInterval` between blinks, `closeSeconds`, `closedHoldTime`, `openSeconds`

**Inspector fields:** `faceMeshName`, `blinkShapeName`, `openValue`, `closedValue`, timing parameters

---

### GestureTrigger.cs

Plays a random upper-body gesture animation each time the AI starts speaking. Uses an Animator `Int` parameter (`GestureIndex`) instead of triggers to avoid Unity's known issue with trigger consumption.

**How it works:**
1. Detects the rising edge of `AgentVoiceController.IsAISpeaking`
2. Picks a random gesture index (avoiding repeats)
3. Sets `GestureIndex` on the Animator
4. Waits for the Animator to leave `GestureIdle`, then resets `GestureIndex` to -1
5. The gesture plays once (non-looping) and returns to idle via exit-time transitions

**Animator requirements:**
- Int parameter `GestureIndex` (default -1)
- Gesture layer (index 1) with an upper-body avatar mask
- Default state `GestureIdle` with transitions to `Gesture1`, `Gesture2`, `Gesture3` based on `GestureIndex` value
- Return transitions from each gesture back to `GestureIdle` using Has Exit Time

**Inspector fields:** `animator`, `voiceController`, `gestureCount`, `gestureLayerIndex`

## Dependencies

- **Meta XR SDK** — `OVRCameraRig`, MRUK (`Meta.XR.MRUtilityKit`)
- **Newtonsoft JSON** (`com.unity.nuget.newtonsoft-json`) — WebSocket message serialization
- **Unity Input System** — required by Meta XR SDK
