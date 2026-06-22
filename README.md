# MR Workplace Assistant — System Infrastructure

This document describes **how the runtime system is built and how its parts work together**. It is the
technical companion to `study_note.md` (which covers the experimental design). It does not walk through
source code — it explains the architecture, the data flow, and how each subsystem behaves at runtime.

The application is a **Meta Quest 3 mixed-reality** experience: a voice-driven AI assistant ("Sam") is
rendered as a Character Creator avatar that appears, in passthrough, standing on or seated at the
participant's real desk. The participant talks to it; it talks back with synchronized lip movement,
facial expression, gaze, and body gesture, while a SWOT brief is shown on a panel beside it.

---

## 1. Platform & dependencies

| Layer | Technology |
|---|---|
| Engine | Unity, **Built-in Render Pipeline (BiRP)** |
| XR runtime | Meta XR SDK (Core + Interaction), **MR Utility Kit (MRUK)** for scene understanding, **Depth API / EnvironmentDepthManager** for occlusion |
| Hardware | Meta Quest 3, **passthrough** (camera see-through) MR |
| Avatar | Character Creator 4 character (`Young_cami`), **Humanoid** rig, Mixamo + custom animation clips |
| Lip-sync | SALSA (amplitude-driven viseme/mouth animation) |
| Conversational AI | **ElevenLabs Conversational AI** (streaming voice agent), per-trial task injection via dynamic variables |

Because the build ships as a static APK, **any change to shaders, materials, prefabs, or animator
assets requires a rebuild and redeploy** to be visible on the headset.

---

## 2. High-level data flow

```
                            ┌──────────────────────────────────────────────┐
                            │                 Meta Quest 3                  │
                            │            (passthrough MR scene)             │
                            └──────────────────────────────────────────────┘
   Participant speaks ─► Microphone ─► ElevenLabs Conversational AI ─► streamed reply
                                                  │                    (audio + transcript)
                                                  ▼
        ┌─────────────────────────────────────────────────────────────────────────┐
        │  Reply audio is played through a spatial AudioSource on the avatar, and   │
        │  fans out to four consumers in parallel:                                  │
        │                                                                           │
        │   (a) SALSA   ── live amplitude ─►  mouth / lip-sync                       │
        │   (b) Emote bridge ── parses [tags] in transcript ─► facial expression    │
        │   (c) Body-gesture driver ── responds to agent events ─► wave / gesture    │
        │   (d) Spatial audio ── 3D-positioned voice from the avatar's location      │
        └─────────────────────────────────────────────────────────────────────────┘

   Room placement:  MRUK detects the real desk ─► avatar is spawned on it, scaled,
                    and turned to face the participant.
   Side panel:      A SWOT brief for the current task is shown on a world-space panel.
   Control:         The experimenter advances phases / trials (semi-Wizard-of-Oz).
```

---

## 3. Scene placement & anchoring

On startup the system waits for **MRUK** to finish loading the participant's scanned room, then searches
the room's anchors for a **TABLE**. The avatar is instantiated at that desk anchor (plus a configurable
local offset), uniformly **scaled** to the current condition, and rotated to **face the participant's
head** (yaw only, so it stays upright).

The desk anchor is the single spatial reference for the whole experience: the avatar, its voice source,
and the SWOT panel are all positioned relative to where the participant actually sits, so the scene is
consistent regardless of room layout. If no table is found (room not scanned), placement is skipped and
a warning is logged — i.e. the desk scan is a hard prerequisite.

---

## 4. Conversational AI / voice pipeline

The agent is hosted by **ElevenLabs Conversational AI**; the app holds a live streaming connection to it.

- **Turn-taking is full-duplex (barge-in enabled).** The microphone keeps streaming even while the agent
  is talking, and interruption events are honored, so the participant can cut in naturally.
  > **Operational requirement:** the participant **must wear headphones**. On open speakers the agent
  > hears its own playback and interrupts itself (echo).
- **Per-trial task injection.** The agent runs **one fixed persona/system prompt** for every condition.
  The only thing that changes per trial is the **task context**, passed in as a dynamic variable. A
  **task key** (e.g. `low_A` … `high_H`) identifies which of the eight tasks is active and carries the
  within-subject **Risk** manipulation. The avatar's appearance (scale, render style) is never sent to
  the agent — it is visual-only.
- **Reply handling.** Incoming audio is played through a **spatialized AudioSource on the avatar** so the
  voice comes from its location in the room. The connection also raises events the rest of the system
  listens to: *connected* (drives the greeting), *new response started* (drives gesture choice), and
  *transcript finished* (drives facial emotion).

This component is the spine of the experience: lip-sync, emotion, and gesture are all reactions to its
audio stream and events.

---

## 5. Lip-sync

Mouth movement is produced by **SALSA**, reading the **live amplitude of the playing voice**. Because the
agent's audio arrives as a real-time stream (not a pre-loaded clip), SALSA cannot sample a finished clip;
instead it is switched into **external-analysis mode** and fed the output amplitude of the playback
AudioSource each frame. The result is mouth animation that tracks whatever the agent is currently saying,
with no dependence on having the full utterance in advance.

The lip-sync system **owns the mouth**; the app's fallback RMS lip-sync is disabled so the two never fight
over the same blendshape.

---

## 6. Facial expression (emotes)

The agent is instructed (in its dashboard prompt) to lead lines with an **emotion tag** such as
`[happy]`, `[sad]`, `[empathetic]`, or `[thoughtful]`. Two things happen with those tags:

1. ElevenLabs delivers the line with the matching **emotional vocal delivery**.
2. The tag also appears in the transcript the app receives, where an **emote bridge** parses the first
   recognized tag and fires the corresponding **facial expression** on the avatar — mapped into four
   buckets: *positive, negative, neutral, thinking*.

The facial expressions are driven through the avatar's expression controller (the same controller exposes
the body-gesture hooks used below). Lip-sync and facial emotes operate on different channels, so the
avatar can talk and emote at the same time.

---

## 7. Body animation & gesture

The avatar is a **Humanoid** rig, so all clips are **retargeted** through Unity's humanoid muscle system —
clips authored on other characters (Mixamo, or custom seated clips) play correctly regardless of bone
naming.

**Gesture triggering.** A body-gesture driver reacts to the agent's connection/response events:

- on **connect**, it plays a one-time **greeting wave**;
- on each **new agent response**, it has a chance to play a **random conversational gesture** (or nothing),
  with cooldowns so gestures don't stack on top of the greeting or repeat back-to-back.

Gestures are fired as **animation triggers**, kept separate from the facial-emote channel.

**Two animator setups, one per Scale condition:**

- **Standing controller** (miniature condition) — a single layer: a standing idle plus wave / gesture /
  head-nod states reached by the triggers.
- **Seated controller** (life-sized condition) — built so the avatar **stays seated while it gestures**.
  It uses **two layers**:
  - a **base layer** that always plays the seated idle (holds the lower body in the chair), and
  - a **masked "upper-body" override layer** that plays the gestures through an **avatar mask** limited to
    the **arms and head**, so the legs/torso stay in the seated pose.

  > Note on masking: in the humanoid rig the **shoulder belongs to the "arm" group**, so arm gestures
  > still move the shoulders. Truly natural seated motion comes from **seated-authored gesture clips**
  > rather than from masking standing clips.

**Gaze.** The lip-sync asset's eye module is pointed at the participant's head at runtime, so the avatar
**looks at the participant** during the conversation. (This deliberately replaces an older head-tracking
component that fought the rig for control of the head bone.) Gaze can be toggled by the experimenter.

---

## 8. The SWOT side panel

Each trial's **SWOT brief** is presented on a **world-space panel beside the avatar**. The panel reads the
**active task key** from the voice connection and shows the matching task's Strengths / Weaknesses /
Opportunities / Threats. The experimenter reveals it via a **controller button**, and it **auto-hides**
after a short interval. The panel is scale-independent — it reads the same whether the avatar is miniature
or life-sized — because the **SWOT belongs to the participant, not the agent** (the agent never receives
the SWOT text; it elicits that content from the participant).

---

## 9. Rendering for passthrough MR

Two rendering problems are specific to passthrough MR, and **every avatar surface is built to solve both**
so that they never differ between conditions:

1. **Real-world occlusion (Depth API).** With `EnvironmentDepthManager` active, each avatar surface shader
   samples the headset's **environment depth** and discards any fragment that is **behind real geometry**.
   The effect: a real object in front of the avatar correctly hides it, instead of the avatar drawing on
   top of the room.
2. **Passthrough opacity.** Avatar shaders force **fully opaque output** so passthrough video does not
   bleed through the character (a transparent-looking, "ghosted" avatar otherwise).

These two fixes are present in **both** the realistic and toon shader families, so the only thing that
actually differs between render-style conditions is the **shading** — not occlusion or transparency.

---

## 10. The two visual independent variables, at the system level

The 2 × 2 visual design (Scale × Render Style) is produced by **swapping assets/settings, never by
changing the agent**.

### Render Style — realistic vs. toon

- **Two prefabs** share the **same mesh, blendshapes, rig, and behavioural wiring**; they differ only in
  their **material/shader set**:
  - **Realistic:** physically-based skin/eye/hair shaders (with the occlusion + opacity fixes).
  - **Toon:** a cel/flat shading family (`QuestToon/Lit` and `QuestToon/Hair`) with a high-key flat look,
    a soft warm shadow step, and an **inverted-hull outline** — carrying the **same** occlusion + opacity
    fixes so the conditions aren't confounded.
- A small **editor tool ("Toonify")** generates the toon material set from the realistic originals,
  copying each surface's texture/tint across automatically, so the two prefabs stay matched and the toon
  set can be regenerated/repaired in one step.

### Scale — miniature vs. life-sized

- Scale is applied as a **uniform transform scale** on the spawned avatar.
- The **toon outline thickness is fed the current scale** so the outline stays **proportional** at every
  size (a fixed-width outline looked correct at full size but flooded the face at miniature size).
- The two **animator controllers** above are paired with scale: **miniature → standing**, **life-sized →
  seated at the desk**. The seated/standing prefab is selected per condition.

The result is **four between-subjects visual cells** from two prefabs (render style) × two scales, while
**Risk** varies **within** subjects purely through the injected task context.

---

## 11. Runtime sequence of a session

1. **Launch** on Quest; MRUK loads the scanned room.
2. **Desk detected** (TABLE anchor); the **condition's prefab** is spawned there, **scaled**, and **turned
   to face** the participant.
3. **Wiring** happens automatically on spawn: spatial AudioSource, lip-sync feed, facial-emote bridge,
   body-gesture driver, and eye-gaze target are all connected to the freshly spawned avatar; the outline
   scale is set from the avatar's scale.
4. **Connect** to ElevenLabs; on connect the avatar plays its **greeting wave**.
5. **Conversation loop:** the participant speaks → the agent replies → the avatar **lip-syncs**, **emotes**,
   and occasionally **gestures**; the experimenter shows the **SWOT panel** on demand.
6. **Trial / risk progression** is driven by the experimenter (semi-Wizard-of-Oz): the **task key** selects
   the next task, which updates both the agent's injected task context and the SWOT panel.
7. **End** of session.

---

## 12. Experiment control

Phase and trial transitions are **experimenter-driven** (semi-Wizard-of-Oz) via controller-button input —
showing the SWOT panel, advancing tasks, toggling gaze. This keeps **session pacing identical across
conditions** (same follow-up count, same time cap), so timing never correlates with the manipulation.

---

## 13. Operational notes

- **Rebuild after asset changes.** Shader/material/prefab/animator edits only appear after an APK rebuild.
- **Headphones are mandatory** on the headset to prevent the agent self-interrupting via echo.
- **Desk scan required.** The room must be scanned (table present) or the avatar won't place.
- **Keep the agent prompt fixed.** Only the per-trial task context may change; scale/render-style must
  never enter the prompt. (See the design-validity rules in `study_note.md`.)
