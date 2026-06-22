# Scripts Overview

This directory contains the core scripts for the MR agent project — a Mixed Reality application that spawns an AI-powered avatar on the user's desk using Meta Quest's Scene Understanding (MRUK).

## Architecture

```
AvatarDeskPlacer (entry point)
 ├── HeadLookAt              (gaze tracking)
 ├── SimpleAutoBlinker        (eye blink animation)
 ├── GestureTrigger           (upper-body gestures)
 └── AgentVoiceController (mic, playback, lip sync)
      ├── ElevenLabsConnection   (voice backend option A)
      └── RealtimeAPIConnection  (voice backend option B)

ExperimenterRemoteTrigger (UDP remote control from experimenter PC)
```

## Scripts

### AvatarDeskPlacer.cs

The main entry point. When MRUK finishes scanning the room, this script finds a `TABLE` anchor and spawns the avatar prefab on it.

**Responsibilities:**
- Listens for MRUK scene-loaded callback
- Searches room anchors for a table/desk
- Instantiates the avatar at the anchor position with a configurable offset and scale
- Rotates the avatar to face the user (via `OVRCameraRig`)
- Attaches `HeadLookAt` for gaze tracking
- Locates the face mesh (by name or by blendshape search) and wires up `AgentVoiceController` with an `AudioSource`, face mesh, and mouth blendshape index
- Auto-connects to the selected voice backend (ElevenLabs or OpenAI)
- Exposes runtime control: `SetGazeTracking()`, `UpdateFacing()`, `SetScale()`

**Inspector fields:** `avatarPrefab`, `cameraRig`, `positionOffset`, `avatarScale`, `faceMeshName`, `mouthBlendShapeName`

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
