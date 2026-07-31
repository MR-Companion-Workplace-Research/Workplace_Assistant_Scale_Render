# MR Workplace Assistant — Avatar Scale & Render Style Study

A mixed-reality study examining how an embodied AI assistant's **visual presentation** shapes user
perception during a collaborative workplace decision task. Built on, and adapting, Schlesener (2025,
Clemson) Study 3. Targeting CHI.

---

## What we're studying

When a person works alongside an AI agent rendered as an avatar in their real room (Quest 3 passthrough
MR), do the avatar's **physical size** and **visual style** change how much they trust it, how credible
and competent it seems, and how eerie or comfortable it feels? We also vary the **stakes of the task**
to see whether these effects depend on how much is on the line.

The agent ("Sam") is a voice-based workplace assistant that helps the participant draft the problem
statement for a budget/justification report, working from a SWOT brief shown on a panel beside it.

---

## Design

A **2 × 2 × 2 mixed factorial**:

| Factor | Type | Levels |
|---|---|---|
| **Avatar Scale** | Between-subjects | Miniature / Life-sized |
| **Render Style** | Between-subjects | PBR-realistic / Toon-shaded |
| **Task Risk Level** | Within-subjects | Low-risk / High-risk |

Four between-subjects visual cells (Scale × Render Style); each participant completes both risk levels.

**Mapping to the precedent:** Scale replaces Schlesener's animation-fidelity factor (ABIF); Render Style
*is* her visual-fidelity factor (VIF: realistic vs. toon shading); Risk Level and the SWOT decision task
come from her Study 3.

---

## Measures (DVs)

Trust Questionnaire · Source Credibility Scale · Godspeed · VHPQ · NASA-TLX · UEQ · Uncanny Valley
Questionnaire. Plus a realism/manipulation-check item.

## Theoretical framing

Expectancy Violation Theory (Burgoon) · CASA / Media Equation (Nass & Moon) · Proteus Effect
(Yee & Bailenson) · social facilitation (Zajonc).

---

## The task & agent

- Participant role-plays a mid-level project manager writing a report for company leadership.
- Each trial presents a **SWOT brief** (Strengths / Weaknesses / Opportunities / Threats) on an MR panel.
  Low-risk tasks are small routine purchases; high-risk tasks are large justification investments — the
  **dollar amount + stakes language carry the Risk manipulation**.
- **Sam** guides the participant through the four SWOT points, asks 3–5 short follow-ups, and helps
  complete the problem statement (≤5 min/trial).
- **Interaction model is an adaptation, not a replication:** Schlesener's Sam was an open-ended
  collaborator that often drafted and confabulated; ours is a **structured elicitor** that draws the
  facts from the participant.

---

## Key design-validity rules

These are load-bearing — breaking them confounds the manipulation:

1. **One agent, one fixed system prompt.** The persona/tone/guardrails are byte-identical across all four
   visual cells. The *only* per-trial variation is the task, injected via the `{{task_context}}` dynamic
   variable (this carries the within-subject Risk manipulation).
2. **Scale and Render Style never appear in the prompt** — they are visual-only. A guardrail forbids Sam
   from commenting on its own size, appearance, or shading.
3. **The SWOT lives on the participant's panel, not in the agent prompt.** Sam receives only the task
   paraphrase and elicits SWOT content from the participant.
4. The 5-min cap and follow-up count are applied identically across all cells, so session length never
   correlates with condition.

---

## Tech stack

- **Engine:** Unity (Built-in Render Pipeline / BiRP), Meta XR SDK, SALSA LipSync
- **Hardware:** Meta Quest 3 (passthrough MR); participant-facing MR panels
- **Avatars:** Character Creator 4, Mixamo animations, lip-sync asset
- **Conversational AI:** ElevenLabs Conversational AI — per-trial task injection via dynamic variables
  (`{{task_context}}`)
- **Experiment control:** the participant selects the run's task on an in-headset launch menu; session
  pacing (follow-up count + 5-min cap) is held identical across conditions, so timing never cues the
  manipulation

---

## Notes / open items

- Reduced task set under consideration (≈2 low + 2 high) to manage MR headset fatigue; full 8-task pool is
  available.
- Agent interaction change (structured-elicitor model + SWOT-interpretation fix) pending team sign-off;
  consider piloting both models.
- Known stimulus quirks to clean up: Task F dollar-amount inconsistency ($3M vs $2.5M), Task B naming
  ("printer supply cabinet" vs "Printer Toner").

## Precedent & collaboration

Adapts **Schlesener (2025)**, *Examining the Roles of Embodiment and Theory of Mind in Shaping User
Perceptions of LLM-driven Conversational Agents* (Clemson), Study 3. International collaboration with
Prof. Sabarish V. Babu (Clemson), Prof. Roshan Venkatakrishnan (UCF), and Prof. Rohith Venkatakrishnan
(UCF).