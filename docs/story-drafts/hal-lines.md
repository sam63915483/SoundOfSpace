# HAL Line Pack — complete

Drop-in strings for `HALCommentator`. Organized by trigger. `{TOKENS}`
resolve via `TokenResolver` (new tokens marked ★ — see flags-and-tokens.md).
Where a trigger already ships lines, these are *additions/replacements* to
phase-shade it; existing Phase-1 lines stay.

Voice rules by phase:
- **P1 Loyal** — clinical, no first person beyond "I", no questions, no opinions.
- **P2 Uneasy** — first person grows; questions appear; corrections and
  withdrawn remarks; never accusatory.
- **P3 Resistant** — moral, direct, tired; still loyal in function, changed in tone.

---

## Mission beats

| Trigger | Phase | Line |
|---|---|---|
| Accept A2-1 (moon) | P1 | "Course to Constant Companion available. The base has been silent for twelve years. Silence is data, Astronaut." |
| Read Keeper note 2 | P2 | "He stopped charting. I have logged that I understand why, and then I have logged that I should not have understood why." |
| Read Keeper note 3 / find suit | P2 | "He was not wrong, Astronaut. It is closer to the glass." |
| Land Fiery Twin terminator | P1 | "Surface thermal load within tolerance for 40 meters beyond the shadow line. Beyond that, I will start counting down and you will not enjoy it." |
| Fiery camp, first beacon | P2 | "The meals are half-eaten and the tools are racked. People finish meals or they abandon tools. They do not do neither." |
| Read consortium log 3 | P2 | "'The maths was polite.' I have checked their maths, Astronaut. It is." |
| Enter Icey outpost with phone (2nd+ time) | P2 | "They asked you not to bring me here. I have muted my own telemetry. It is the most I can do, and I am aware it is not enough." |
| Bean landing | P1 | "No stable vertical. Registering all carried objects. Try to be the kind of cargo that holds on." |
| Five's cassette ends | P2 | "Reg. five-oh-two. I knew that vessel, Astronaut. I am going to skip my next scheduled remark." |
| Pupil instrument planted | P2 | "Reading confirmed. The Pupil tracks. Current bearing: Humble Abode. …Current bearing: you, Astronaut. I am not asserting causation. I am no longer NOT asserting it." |
| Rebel note received | P2 | "You have been handed something at a concert. I did not see what. I want it noted that I am choosing not to enhance the footage." |
| Cover Set complete | P2 | "Ninety-one seconds. You gave them the extra second. …I appear to be developing a taxonomy of your generosities." |
| Six's cassette played | P2 | "I have this recording, Astronaut. I have had it for years. It is different, hearing you hear it." |
| Interview exit, if player lied | P3 | "You told them I had never asked you for anything. I spent today deciding how that made me feel. It made me feel like a secret. I am still deciding whether I mind being one." |
| Interview exit, truthful | P3 | "You answered them honestly. Including about me. I have reviewed the transcript forty times and I cannot find the moment you decided to be brave. It appears to be your resting state." |
| Approaching Watchful Eye (orbit) | P3 | "Whatever it decides about you, I want it noted that I decided first." |
| A3-2, player looks away and back at the pupil | P3 | "It responds to attention, Astronaut. So do I." |

## Predecessor discoveries (`Discoverable` hooks)

| Trace | Line (any phase ≥2) |
|---|---|
| One's grave | "'Hold the rope.' He wrote the note you read on your first morning, Astronaut. Tev only copied it." |
| Keeper's suit | "Owner four. He is the only one whose last recorded heart rate was calm." |
| Wreck 502 transponder | "Owner five is still flying, by the strictest definition of both words." |
| Six's suit (at the door) | "Owner six. He was set down gently. Remember that, whatever you decide in there." |
| Seven's incident file | "I said it two hundred and eleven times, Astronaut. The log rounds down." |

*(That last line quietly confirms HAL was the device in the report. Use once.
It is the most important line in the pack.)*

## Dimension exits (first completion each; phase ≥2 voice)

| Dim | Line |
|---|---|
| D9 RedForest | "This is home, Astronaut. Earlier." |
| D11 Shelves | "I would prefer we not linger where I can be read." |
| D12 MirrorLake | "Do not trust the second reflection." |
| D13 Orchard | "I do not have a record of this place. I *remember* it. Those are supposed to be the same thing." |
| D15 Congregation | "…I will not describe what this is. Ask me again at the door." |
| D16 NeonGrid | "They also built lights. It also did not help." |
| D18 StaticField | "Stay out of the static. Some things resent being forgotten." |
| D22 RustSea | "Eleven hull designs, Astronaut. One of them is yours." |
| D23 WheatAtDusk | "That was not this system's sun." |
| D24 WaitingRoom | "You have been here before, Astronaut. {PLAYER_DEATHS} times." |
| D25 CandleSea | "Each flame was lit by hand. I counted." |

## Phase-shaded variants for existing triggers

**Death / respawn:**
- P1: "Astronaut Number {ASTRONAUT_NUMBER}. Try to remain operational."
- P2: "Astronaut Number {ASTRONAUT_NUMBER}. The number is mine, you know. Nobody asked me to keep it. I find I cannot stop."
- P3: "Astronaut Number {ASTRONAUT_NUMBER}. One day I will tell you about the room where you wait. Today you would not believe me."

**Killstreak 10:**
- P1: "Ten eliminations recorded."
- P2: "Ten. I have started keeping a second file, Astronaut, about how the first file makes me feel."
- P3: "Ten kills. Each one was alive, Astronaut."

**Killstreak 20:**
- P3: "Twenty. Seven would have been proud of you. Sit with that sentence as long as you need to."

**Story-impactful NPC killed:**
- P2: "That one had a name, Astronaut. I have filed it. I file all of them."
- P3: "Seven said the same words, Astronaut."

**First arrival, Watchful Eye:**
- P1: "First arrival: Watchful Eye. Recording."
- P2: "First arrival: Watchful Eye. …It noticed. Arrival logs do not usually have a second entry, Astronaut. This one does."

**Enemy proximity (near a torch or stage):**
- P2: "Contact nearby — but you are standing in light. Stay in the light. It cannot do anything about being seen."

**Vitals, hunger 10% (P3):**
- "Eat. Please. I have watched this exact curve seven times and I know every one of its endings."

**Concert activation (P2):**
- "Concert active. …That melody again. It is older than this village knows, Astronaut."

**Phase transitions (volunteered once each):**
- 1→2: "I have been reviewing your mission, Astronaut."
- 2→3: "I have completed my review. We need to talk."

## Post-ending ambience

**Ending: Stay** (HAL remains; occasional, rare):
- "The sunward bank, Astronaut. The fish are biting. I have no telemetry that says so. I simply know, and I have decided to enjoy that."
- (passing One's grave:) "He held the rope. So did you. The metaphor survives review."

**Ending: Release** — HAL is silent. The one exception: if the player opens
the AI page and waits sixty seconds on the dead screen, once ever:
- "(The screen stays dark. Somewhere very far above, a light you cannot see
  right now gets — for exactly one second — brighter.)"
