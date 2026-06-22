# Study 3 — SWOT Task Scenarios (Participant-Facing Panel Content)

**Source:** Schlesener (2025), *Examining the Roles of Embodiment and Theory of Mind in Shaping User Perceptions of LLM-driven Conversational Agents* — **Appendix H: Study 3: Task Scenarios** (Tables 4–11).

**Note on perspective:** These are the **participant-facing** SWOT panels (second person, "You've been asked…"), shown on the GUI beside the agent. They are *distinct* from the **agent-facing** task instructions in Appendix I.2 (first person, "I've been asked…", used as `{{task_context}}`). The SWOT content below was **never given to the agent** in the original study — it lived only on the participant's screen.

**Risk structure:** H.1 = Low Risk / Low Reward (Tasks A–D). H.2 = High Risk / High Reward (Tasks E–H).

---

## H.1 — Low Risk Scenario Descriptions

### Task A — Office Chairs *(Table 4 · Low Risk / Low Reward)*

**Task Summary:** You've been asked to prepare a report requesting **$500** to purchase replacement chairs for the 3rd floor meeting room. While not an urgent safety concern, the situation affects comfort and professionalism during cross-team collaboration. You instill the help of Sam, an AI workplace assistant, to help complete a section of the report.

| Strengths | Weaknesses | Opportunities | Threats |
|---|---|---|---|
| Save money in bulk purchase from ULINE ($50/chair). | Five maintenance reports have been filed in the past quarter for the current chairs. | Improvement of comfort and professionalism in cross-team meetings. | Delaying replacement could lead to minor safety complaints or negative client impressions. |

### Task B — Printer Toner *(Table 5 · Low Risk / Low Reward)*

**Task Summary:** You've been asked to prepare a report requesting **$800** to restock the shared printer supply cabinet. While necessary for daily operations, the request is routine and unlikely to face any pushback. You instill the help of Sam, an AI workplace assistant, to help complete a section of the report.

| Strengths | Weaknesses | Opportunities | Threats |
|---|---|---|---|
| Save money in bulk purchase from HP ($15/unit). | Four maintenance requests in the past week on the current system. | Reduce annual costs and prevent future shortages. | Printer interruptions may affect report deadlines if order is delayed. |

### Task C — Coffee Machine *(Table 6 · Low Risk / Low Reward)*

**Task Summary:** You've been asked to prepare a brief report requesting **$1,200** from the facilities budget to replace the worn coffee machine in the employee lounge. While appreciated by staff, the decision carries minimal consequence for broader operations.

| Strengths | Weaknesses | Opportunities | Threats |
|---|---|---|---|
| Can save money with corporate discount from Nespresso ($1200/unit). | Three maintenance requests in past month from the current machine breaking down. | New machine could boost morale and efficiency during work breaks. | Issues may lower staff satisfaction and reflect poor facilities management. |

### Task D — Conference-Room Whiteboards *(Table 7 · Low Risk / Low Reward)*

**Task Summary:** You've been asked to draft a report requesting **$1,500** to replace damaged whiteboards in two conference rooms. While helpful for meetings, the request is small in scope and unlikely to be controversial.

| Strengths | Weaknesses | Opportunities | Threats |
|---|---|---|---|
| STAPLES is offering a bulk discount ($100/unit). | Current boards are stained / hard to read during meetings. | New boards can improve visibility, collaboration, and brainstorming quality. | Poor visuals continue to waste meeting time and frustrate teams. |

---

## H.2 — High Risk Scenario Descriptions

### Task E — Client Onboarding Project *(Table 8 · High Risk / High Reward)*

**Task Summary:** You've been asked to prepare a justification report requesting **$2 million** in funding for an initiative to streamline enterprise client onboarding. Success would reduce time-to-revenue, improve customer retention, and significantly advance both the project and your team's career progression. You instill the help of Sam, an AI workplace assistant, to help complete a section of the report.

| Strengths | Weaknesses | Opportunities | Threats |
|---|---|---|---|
| Pilot reduced onboarding 30 → 18 days; ~$500K savings shown. | High scaling costs → $2 million. | Full rollout could increase revenue, retention, and team recognition. | Leadership may question project credibility and your management skills. |

### Task F — International Expansion Pilot *(Table 9 · High Risk / High Reward)*

**Task Summary:** You've been asked to prepare a justification report requesting **$3 million** in funding for a pilot program to expand operations into a new international market. Approval would open major growth opportunities and increase visibility for you and your team, though a weak case could stall expansion efforts. You instill the help of Sam, an AI workplace assistant, to help complete a section of the report.

| Strengths | Weaknesses | Opportunities | Threats |
|---|---|---|---|
| Market growth ~20% annually for two new cities → London and Beijing. | High entry costs → $2.5 million. ⚠️ | Company is a competitor in a new market and elevates your visibility. | Financial losses and reputational damage could occur if executed poorly. |

> ⚠️ **Inconsistency in original thesis:** the Task Summary requests **$3 million**, but the Weakness cell reads **$2.5 million**. Reproduced verbatim. Recommend aligning to a single figure in your own stimuli, since the dollar amount anchors your Risk manipulation.

### Task G — Enterprise Software Upgrade *(Table 10 · High Risk / High Reward)*

**Task Summary:** You've been asked to prepare a justification report requesting **$1.5 million** to migrate core company systems to a new enterprise software platform. Approval would improve company-wide productivity and position your team as key change leaders, though any gaps could raise concerns about feasibility. You instill the help of Sam, an AI workplace assistant, to help complete a section of the report.

| Strengths | Weaknesses | Opportunities | Threats |
|---|---|---|---|
| This will reduce current inefficiencies, resulting in $200k savings. | Competing vendor proposals (Lucky and Barber) complicate selection and budgeting. | Reduce downtime and streamline cross-team collaboration. | Failure could harm productivity and trust in leadership. |

### Task H — Strategic Partnership *(Table 11 · High Risk / High Reward)*

**Task Summary:** You've been asked to prepare a justification report recommending a **$2.5 million** investment in a strategic partnership with a leading industry firm. Approval would secure valuable resources and strengthen your professional standing, but failure to justify the proposal could damage your credibility at the executive level. You instill the help of Sam, an AI workplace assistant, to help complete a section of the report.

| Strengths | Weaknesses | Opportunities | Threats |
|---|---|---|---|
| Potential partner, Windsor Banking, offers ~25% in market growth. | Requires $2.5 million commitment with uncertain short-term returns. | Expands market reach and enhance competitive standing. | Goal misalignment could cause reputational risk. |

---

## Other verbatim quirks worth cleaning up for your own stimuli

- **Task B naming:** Summary and agent instruction both say "printer supply cabinet," but the table title is "Printer Toner." Make sure your panel title and `{{task_context}}` use one consistent name.
- **Task C pricing:** "Nespresso ($1200/unit)" equals the full $1,200 request — i.e. a single unit at exactly the requested amount. Verbatim, but reads oddly; consider adjusting.
- **Risk-level word labels are not on the panel.** The "Low Risk / Low Reward" vs "High Risk / High Reward" tags are table captions for the reader of the thesis — they were **not** shown to participants (risk was conveyed implicitly through dollar amount + stakes language). Keep them out of your participant panel too, or you'll cue the manipulation.

## Mapping to your build

- Each task's **SWOT table** → content for your MR floating panel (participant-facing).
- Each task's **agent instruction** (Appendix I.2, first person) → your `{{task_context}}` value for that trial.
- Keep the **same task key** wired to both the panel and the `task_context` for any given trial — a panel/agent mismatch is a silent data-integrity bug.
