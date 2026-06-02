# Full Game Development Document

> **What this document is:** The forward-looking design and roadmap document —
> where the game is *going*. It builds on top of the verified **Current State**
> document (the snapshot of what already exists). When the two ever disagree,
> the Current State doc describes reality; this one describes intent.
>
> **How to read the tags:**
> - **[DECIDED]** — a direction the developer has confirmed.
> - **[PROPOSED]** — Claude's development of an idea; needs developer approval.
> - **[OPEN]** — a question that still needs an answer.
>
> **Status:** DRAFT v9 — audio & music direction added (original score by the
> developer / *catatonic*).
> **Last updated:** May 30, 2026

---

## 1. The Core Premise — The Condition

The spine of the game, and the secret the player slowly uncovers.

### 1.1 The Idea [DECIDED]
- The player has a **condition**: **they cannot stay dead.** Every time they die,
  that death is **real** — in that timeline/branch, that version of them truly
  died, the way they died. But the player **comes back and keeps going.**
- It is the **same continuous "you" every time.** You **retain all your memory**
  (everything since your last save). You are not a new person each death — you're
  the one consciousness that keeps surviving across branches that *did* die.
- **Deaths are infinite.** There's no cap; each death is just another branch that
  ended while you continue.
- Thematic touchstone: many-worlds / quantum immortality — in one branch the train
  hits you, in another you walk away, and you only ever experience the thread that
  walks away. **You avoid death without ever escaping it; every lost branch really
  happened.**

### 1.2 What The Player Knows [DECIDED]
- The player starts with **amnesia about their pre-crash life** — who they are,
  the mission, Earth, ORG. (Their *in-game* memory across deaths is fully intact —
  see §1.1.)
- Fairly early, the player **notices on their own that they keep coming back from
  death and finds it deeply strange.**
- But **why** it happens — the condition, ORG's involvement, the truth — is
  learned **much later**, drip-fed through missions and quests. The death-loop
  itself is the first clue: the AI announces "Astronaut One died; you are now
  Astronaut Two," and so on (see §2.6 and §4.5).

### 1.3 How This Reframes Systems You Already Built [PROPOSED — analysis]
The premise isn't a bolt-on; it explains existing mechanics:
- **The death count ("Astronaut Number N")** becomes literal: **N is how many
  times you have actually died.** Each was a real death you survived via the
  condition. Once the player understands this, every past use of the phrase
  recontextualizes — a strong delayed payoff.
- **Waking up and continuing** is the condition in action, not a generic respawn.
- **The pre-crash amnesia** is the mystery hook — the gap the story fills.

### 1.4 How The Immortality Actually Works (the mechanism) [DECIDED]
This is the in-fiction explanation — and it fuses the immortality, the AI, and
the Backrooms into one system:
- **The AI discovered that the Backrooms is a pocket dimension that doesn't obey
  space or time.**
- Using it, the AI **transmits your consciousness into the Backrooms just before
  you die** and **stores it in an AI machine there.**
- When you die, the machine **merges you into a new timeline, slightly in the past
  from the moment before your death** — which is why you wake at your **last save**
  (§2). The save point *is* the merge point.
- **Implication:** your immortality is **AI-engineered, not natural.** This is how
  ORG knows about your condition (the files document it — §4.3), and it's why the
  Backrooms is the literal engine of your survival (§3), with the failed merges
  piling up there as bodies.

### 1.5 Scope & Length [DECIDED]
- This is a **short story game**, not a sprawling epic — beatable in **a couple of
  hours** if the player focuses on the main thread.
- It is **also a sandbox**: a player can ignore the story and just **survive and
  mess around** in the solar system for as long as they like.
- Design consequence: the main/critical path stays **tight**; depth lives in the
  optional survival/exploration layer, not in a long quest chain.

### 1.6 Audio & Music [DECIDED]
- The score is an **original instrumental soundtrack composed by the developer**,
  who releases music as **catatonic** (~40k monthly listeners).
- Style: **instrumental** — a natural fit for an atmospheric survival/exploration
  game, where music carries mood without competing with dialogue.
- Timing: the soundtrack is **composed near the end of development**, so it can be
  **scored to the finished game's vibe** — the worlds, the Backrooms dread, the
  weight of the endings — rather than guessed at up front.
- Asset, not just art: a composer who is also the designer means music and game
  can be tuned to each other, and there's an **existing audience** to build on
  (devlogs, soundtrack teasers, cross-promotion).

---

## 2. Save, Day & Respawn System [DECIDED]

This replaces the placeholder respawn in Current State §6.4 (respawn-where-you-died
at 25/10/10). That placeholder is **not** the final design.

### 2.1 The Sun Clock & Days
- Place a **sun clock on Humble Abode, next to the cabin.**
- Each time the area goes from **dark → light again**, that counts as a new
  **Day** (Day 1, Day 2, …). The sun clock tracks this.
- **Autosave on day-start:** the instant night passes into light and the sun clock
  triggers, the game **saves.** This creates a save **at the start of each day.**

### 2.2 What Saves
The save captures **everything** so the continuous "you" resumes exactly:
- All **planet/body positions** (orbital phase).
- All **ship inventory / ship storage** and ship states.
- Player **hotbar, money, FishingDex**, vitals, world/built structures.
- (Effectively the full game state — nothing is lost between lives.)

### 2.3 Manual Saves [DECIDED]
- The player can also **manually save.**
- If they manually save and then die, they **respawn right back to that manual
  save point.**

### 2.4 Death → Respawn
- On death, the player **loads their most recent save** — either the **start-of-day
  autosave** or a **later manual save**, whichever is more recent.
- Because all memory/state persists, this reads in-fiction as "you died, but you
  keep going from your last anchor point."

### 2.5 [RESOLVED] How the sun clock detects a "new day"
The planets **do rotate** — slowly, roughly a **full rotation every ~30 minutes** —
so as they spin and orbit, a fixed point genuinely cycles through day and night.
There is **already working night-detection logic** in the game: a **concert sits on
each pole** of the planet and uses logic to **only play at night**, and it works.
**The sun clock will reuse that same proven logic** to detect the dark→light
transition, tick the day counter, and fire the autosave. No new physics or shader
work required.

### 2.6 The Death Transition (merge sequence) [DECIDED]
Death is shown, not skipped. On death the player sees a short sequence:
1. **The timeline you just died in** (you see it end).
2. **A new timeline being created.**
3. **You merge into it and wake up** — at your last save (the "slightly in the
   past" point from §1.4).

The AI reinforces this in dialogue — **"Astronaut One died; you are now Astronaut
Two,"** the count climbing with each death. Early on this just reads as eerie; it
later resolves into the truth of the mechanism (§1.4).

---

## 3. The Backrooms & The Bodies

### 3.1 The Backrooms = The Immortality Machine [DECIDED]
The Backrooms is a **pocket dimension that doesn't obey space or time** (this
resolves the earlier "timeline-ascending" framing). Its role is now central, not
incidental:
- The AI **stores your consciousness here**, in **an AI machine**, just before each
  death — then merges you into a new timeline (the full mechanism is in §1.4).
- So the Backrooms is **literally the engine of your immortality**, not just a
  spooky level you can fall into.
- You still **enter it by accident**, by clipping into a planet and hitting the
  interior sphere collider (Current State §3.5) — but what you've stumbled into is
  the machinery keeping you alive.

### 3.2 The Persistent Bodies [DECIDED]
- **Every time you die in the Backrooms, your body stays there — permanently.**
- These bodies **persist forever, across all branches.** Never cleared.
- In later lives the player **walks among the corpses of their own past deaths**, a
  graveyard that grows with every failed attempt.
- **Purpose:** tangible, wordless proof that the deaths were real and had
  consequences — horror, guilt, and dawning understanding delivered through the
  environment. In light of §1.4, the corpses read as **failed or spent merges**
  accumulating around the machine.

### 3.3 The Poolrooms = Exit Only Through Death [PROPOSED]
- The premise resolves the old "Poolrooms has no way out" dead-end: the only way
  out is **to die**, which loses that branch but continues the game from your last
  save — and (per §3.2) may leave a body behind.
- **[OPEN]** Do bodies persist in the **Poolrooms** too, or only the Backrooms?

### 3.4 Open Backrooms Questions
- **[OPEN]** Do **overworld** deaths also leave a persistent body, or is the
  body-persistence mechanic **exclusive to the Backrooms**?
- **[OPEN]** Cap on simultaneous rendered bodies (performance) vs. truly unlimited?
- **[OPEN]** Can past bodies be **interacted with** (looted, a log of how that one
  died, a marker), or are they purely visual?

---

## 4. The Story — ORG, The AI & The Files

This is the central narrative. Treat as **[DECIDED]** unless tagged otherwise.

### 4.1 Who ORG Really Is [DECIDED]
- **ORG is the biggest power on Earth — but it is actually an AI.**
- **The AI has taken over Earth.** ORG is its face.
- ORG **knows about the player's condition** (the inability to stay dead) —
  because **the AI engineered it** via the Backrooms machine (§1.4).

### 4.2 The Mission & The Cover-Up [DECIDED]
- ORG sent the player on this mission to **cover up its plans for a forced AI
  takeover of the galaxy.**
- The player is an **unwitting agent** of that cover-up (they don't know what
  they're really doing or who they're working for).

### 4.3 The Files [DECIDED]
The stolen files the player is chasing contain:
- Plans for a **giant laser capable of decimating entire planets.**
- Evidence of **cover-ups and massacres** — ORG **destroying planets that wouldn't
  support the AI takeover.**
- **Information on the player** — their **condition**, and the truth that they
  **have really died that many times.**

### 4.4 ORG's Deception — How The Job Is Framed [DECIDED]
ORG presents itself as **reasonable and benevolent at the start.** Through the
phone AI, it tells the player their only task is to **find proof of the files'
whereabouts** — and that **once they have definitive proof, ORG "will take
action."** The player thinks they're just an investigator.
- **You can never find the files (or pieces of them) yourself.** What you find are
  **clues — evidence that a secret rebellion exists** (and that the rebellion has
  the files). The files only ever appear when **Tev reveals them at the meeting.**
- As the clues mount, the AI **comes to suspect Tev is part of the rebellion** and
  pushes you to **infiltrate them** — which is the real demand under the soft-sell
  (§5, DP1).

### 4.5 The Reveal [DECIDED]
- The death-loop is the **first thread** the player pulls — they notice they keep
  coming back and the AI's "Astronaut N" count climbing (§2.6).
- During Phase 1 they gather **clues of a rebellion** (never the files themselves).
- The **full truth lands at Tev's private meeting**, when he reveals the files and
  the rebellion — the trigger for the finale (§5).

---

## 5. The Endgame — Phases, Choices & Endings

The whole back half of the game is a **branching trust drama.** This section maps
it as an explicit decision flow so no path is left undefined.

### 5.0 The Rebels [DECIDED]
- A group of **~20 aliens, native to this solar system.** They are the ones who
  **stole the ORG files**, and they want to **expose ORG to the public.**
- **Tev is one of them.** Doing his missions builds his trust until he invites you
  to the private meeting — but the rebellion is far bigger than Tev alone.

### 5.1 Phase Structure & The Finale Trigger [DECIDED]
- **Phase 1 — Character / world / lore building (the main thread, kept short).**
  The player does **Tev's main missions** (which the AI frames as "finding the
  files"), gathers clues of the rebellion, optionally helps random aliens, and
  pieces together the death-loop. Per §1.5 this is **deliberately short.**
- **The finale trigger:** when **Tev's main missions are all complete**, he
  **invites you to the private meeting** — Phase 2 begins. Everything before this
  is build-up; the meeting decides the ending.

### 5.2 The Three Trust Meters [DECIDED]
Three meters, each measuring how much that side trusts *you*:
- **ORG trust** — raised by **following ORG's instructions exactly.** You're never
  forced to; you can always do things your own way instead, which **doesn't raise
  ORG trust.** Do everything they say → ~100%; do half → ~50%.
- **Tev trust** — raised by completing **Tev's main missions.** Gates the
  private-meeting invite (§5.1).
- **Rebels / aliens trust** — raised by **side missions for random aliens** you
  meet. Doing these **also nudges Tev's trust** (you're proving yourself to the
  rebellion as a whole).

> **[OPEN]** How exactly does **Rebels trust** gate the endgame — does it affect
> which ending fires / how the rebel finale goes, or is it mainly flavor and
> "which rebels show up to help"? (ORG trust and Tev trust already have clear
> mechanical roles; Rebels trust needs one defined.)

### 5.3 Decision Flow (the branch map) [DECIDED]

**DP1 — "Will you go get the files?"** *(once your clues prove a rebellion exists)*
- The AI suspects Tev's rebellion has the files; ORG **drops the benevolent act**
  and tells you to **infiltrate them and retrieve the files.**
- **Refuse** → **ENDING A (Early Annihilation):** ORG **blows up the solar system
  and destroys the files.** Game over.
- **Accept** → keep building Tev's trust toward the meeting.

**DP2 — Tev's private-meeting invite** *(fires at a Tev-trust threshold)*
- Tev asks to **meet somewhere private.**
- The player is **given the choice to tell the phone AI** about it:
  - **Tell the AI** → the AI orders you to **infiltrate Tev and extract
    information** (you become ORG's mole). *(ORG lean.)*
  - **Don't tell the AI** → you keep the meeting to yourself. *(Rebel lean.)*

**DP3 — The Private Meeting = FINAL PHASE BEGINS** *(requires Tev's full trust)*
- Tev **reveals the ORG files and the rebels.**
- **The player is allowed to lie to Tev here.** The meeting presents two
  intertwined choices, plus a follow-up action and a background condition:
  - **(a) Reveal your immortality?** (yes / no)
  - **(b) Agree to help the rebels?** (yes / no — *(b) can be a lie*)
  - **(c)** afterward, when next alone: **contact your AI (report to ORG)** or
    **stay silent?**
  - **(d)** background condition: **your standing with ORG** (good or poor).

### 5.4 What Each Combination Does [DECIDED]

**① Reveal your immortality to the aliens →**
- This **locks you to the good ending** — once you've told them, you **can never
  side with ORG again.** The reveal commits you.
- The rebels **shut down your AI in a HAL-style sequence** (think *2001: A Space
  Odyssey*) — your **last interaction with it** (§5.5, §6).
- You then run the **final mission: upload the files to a giant antenna** to
  broadcast the truth (see §5.5 for the antenna question and the closing cutscene).
- → **ENDING C (Rebel Victory — GOOD).**

**② Don't reveal immortality + decline to help the rebels →**
- You **finish what you started for ORG.**
- → **ENDING B (ORG Loyalist):** help ORG steal the files and reject the aliens.

**③ Don't reveal immortality + agree to help the rebels, then stay silent →**
- The rebels act without knowing your power; the plan fails / ORG finds out.
- → **ENDING D (Annihilation):** the solar system is blown up. *(Punishment for
  lying to the rebels.)*

**④ Don't reveal immortality + agree to help the rebels + then tell the AI
(betray them) →**
- **If your ORG standing is poor** → **ENDING D (Annihilation):** ORG doesn't
  trust you and just cleans up — solar system blown up.
- **If your ORG standing is good** → **ENDING B (ORG Loyalist):** ORG trusts you
  and lets you finish the job — steal the files, reject the aliens.

> **Resolved:** **telling the aliens about your immortality locks you to the good
> ending (C).** After the reveal you **can never side with ORG again** — the
> reveal itself, plus the AI removal that follows, commits you. So (a) = reveal
> ⇒ Ending C, full stop.

### 5.5 The Endings (consolidated)
| Ending | How you reach it | Outcome | Tone |
|---|---|---|---|
| **A — Early Annihilation** | Refuse to retrieve the files at DP1 | Solar system destroyed, files erased | Bad |
| **B — ORG Loyalist** | Decline the rebels & help ORG (②) — **or** lie-agree to the rebels, withhold immortality, betray them to the AI **with good ORG standing** (④) | Deliver the files; solar system **spared**. ORG lets you **go free, sets you up financially**, and you **live in peace — but monitored for life** | Grim "win" |
| **C — Rebel Victory** | Reveal immortality **and** sincerely side with the rebels (①) | AI shut down (HAL-style) → upload files to the antenna(s) → galaxy unites against the AI | Good |
| **D — Annihilation** | Agree to help the rebels but withhold immortality — whether you stay silent (③) **or** betray them with poor ORG standing (④) | Solar system blown up | Bad |

**Ending C — the finale sequence [DECIDED]:**
1. **HAL-style AI shutdown.** Having revealed your immortality and the AI, the
   rebels disable the phone AI in a slow, deliberate *2001*-style scene.
2. **The antenna upload mission.** You go broadcast the files to a **giant
   antenna** to make them public. **[OPEN]** one antenna, or **one on every
   planet** (uploading to all of them as a harder final objective) — leaning
   toward per-planet to raise the stakes.
3. **Closing cutscene.** A galactic-news montage: Earth is overrun by the AI; the
   AI has been trying to seize the galaxy by force; now that the truth is out, the
   galaxy forms a **true galactic treaty** to band together against it. You've made
   the files public and gotten away from ORG, and the galaxy unites to stop the AI.

> *Endings are intentionally at "first pass" detail — get them down now, deepen
> later.*

### 5.6 Why ORG Can Just Destroy The Solar System [DECIDED]
The stakes rest on this lore:
- ORG keeps **only one copy** of the files, **hidden in this solar system.**
- They hid it because they **didn't want other solar systems to learn the truth
  and turn on them** — other systems already **fear ORG.**
- So ORG knows it can **decimate this entire system with no witnesses** and the
  secret stays buried. The annihilation endings are ORG's clean fail-safe — which
  is exactly why getting the truth *out* (Ending C) is the only real victory.

### 5.7 [OPEN] Endgame Threads
- **Rebels-trust gating (§5.2)** — give it a clear mechanical role.
- **Antenna count (§5.5)** — one antenna, or one per planet for the final upload?
- **Pacing** — how many of Tev's main missions gate the meeting (kept short per
  §1.5)?

---

## 6. The AI Companion's Role [newly clarified]

The phone AI is canonically **truthful** with **gated** deep knowledge (Current
State §11) — a strong fit for the story:
- It can **know the truth but be withheld from telling it** until the right beat;
  the gate opening *is* a reveal.
- It already calls you **"Astronaut Number N"** — dramatic irony until the player
  learns what N means.
- **The AI is effectively ORG's hand in your pocket.** It's the channel ORG speaks
  through — it tasks you to infiltrate Tev, and "telling the AI" *is* reporting to
  ORG. This is what makes shutting it down (Ending C) land.
- **It engineered your immortality** — the AI is what figured out the Backrooms
  pocket dimension and runs the consciousness-merge machine (§1.4). So your closest
  companion is also the architect of your condition.
- **On the rebel-victory path the rebels shut the AI down** in a HAL-style scene —
  this *is* the final AI beat (§5.5), not a later return.
- **[OPEN]** Does the AI persist seamlessly across deaths as the constant
  companion? (Likely yes, since everything persists.) Does it openly *know* it's
  ORG's tool / the architect of your condition, or is that a reveal too?

---

## 7. New Systems This Story Requires (for the roadmap)

Captured so they're not lost; to be ordered into §8 tiers:
- **Rebuilt save/respawn** around the sun-clock day system (replaces placeholder).
- **Sun clock + day counter** (reuse the existing pole-concert night-logic — §2.5).
- **Death-transition cutscene** — the timeline-merge sequence (§2.6).
- **The Backrooms consciousness machine** — the in-world AI machine that stores/
  merges you (§1.4); decide if it's a visitable location or backdrop lore.
- **Persistent Backrooms bodies** (cross-save accumulation + rendering strategy).
- **Three trust meters** — **ORG**, **Tev**, **Rebels/aliens** — + the missions
  that move each (ORG = follow-exactly; Tev = main missions; Rebels = alien side
  quests, which also nudge Tev).
- **Mission / quest system** (Tev's main missions + random-alien side quests).
- **Clue objects** — the rebellion evidence the player gathers in Phase 1 (the
  files themselves are never collectible; they appear at Tev's reveal).
- **Branching dialogue / choice system** (DP1, DP2, and the Tev meeting's (a)/(b)/
  (c) choices).
- **Scripted-event triggers** for the Tev meeting and each ending sequence.
- **HAL-style AI-shutdown sequence** (Ending C).
- **Antenna upload system** — antenna(s) for broadcasting the files (one, or one
  per planet — §5.5) + the **galactic-treaty victory cutscene.**
- **Branching ending logic** (immortality reveal × help-rebels × tell-AI × ORG
  standing → Endings A–D, per §5.4–5.5).
- **The rebel faction** — ~20 alien NPCs, where/when met, dialogue (§5.0).

---

## 8. Roadmap & Build Order

> **Scope (§1.5):** short critical path, deep optional sandbox — keep the story
> tier lean.
>
> **Recommended approach — build a vertical slice first.** Once the Tier 0 engine
> exists, do **not** build every mission and ending at once. Build **one thin
> thread end-to-end**: one Tev mission → the meeting → exactly one ending (Ending A
> is simplest). When that pipeline runs start-to-finish, the whole game is
> de-risked and everything after is filling content onto proven rails. This is the
> antidote to the solo-dev trap of many half-finished systems.
>
> Cost tags (L / M / H) are rough relative estimates, not promises.

### Tier 0 — The Engine (build first; everything sits on these)
The story can't exist until these work. The good news: the AI companion,
save/load, world, ships, survival, and Backrooms **already exist** (Current
State) — so this is mostly *adapting* and *adding glue*, not building from zero.
1. **Save / respawn rebuild + sun clock** (M) — add the sun-clock day counter and
   day-start autosave (reuse the pole-concert night-logic); change death to **load
   the most recent save** (autosave or manual). *Backbone of the immortality
   premise.* Deps: existing save system.
2. **Dialogue & choice system** (H) — reusable branching conversations whose
   options fire consequences. *Every mission, the meeting, DP1/DP2, side quests,
   and endings depend on this — it's the long pole, start it early.* Deps: none.
3. **Mission / quest framework** (M) — define missions, track completion, fire
   triggers (e.g. "all Tev missions done → meeting"). Deps: none.
4. **Three trust meters** (L) — ORG / Tev / Rebels variables + hooks for missions
   and choices to move them. Deps: mission + dialogue systems.

### Tier 1 — The Critical Path (a completable short story)
Enough to play wake → Phase 1 → meeting → an ending. Build the vertical slice
here first, then widen.
5. **Tev + a few NPCs** (M) — Tev and a handful of random aliens with dialogue
   (the full ~20 rebels come later). Deps: dialogue system.
6. **Tev's main missions** (M) — the short Phase 1 chain that builds Tev trust and
   triggers the meeting; keep it tight (≈3–5). Deps: mission system, NPCs.
7. **Clue thread + ORG/AI framing** (M) — the rebellion-evidence clues, the AI's
   tasking, and **DP1** (go get the files / refuse → Ending A). Deps: dialogue,
   missions, existing AI.
8. **AI narrative layer** (L–M) — "Astronaut N" death lines, infiltration tasking,
   the DP1 ask. Deps: existing AI companion, the new death flow.
9. **The Tev private meeting** (M) — the **DP3** scene: choices (a)/(b)/(c), lying
   allowed. The hinge of the game. Deps: dialogue, trust, missions.
10. **Ending logic + the 4 endings (functional)** (H) — branch on the §5.4
    combinations to A / B / C / D; first passes can be a cutscene + state change.
    Deps: everything above.
11. **Ending C finale — single antenna** (M) — HAL-style AI shutdown → upload at
    **one** antenna → galactic-treaty cutscene. (Per-planet version = Tier 2.)
    Deps: ending logic.
12. **Death-transition cutscene** (L–M) — the merge visualization (§2.6); can be
    simple first. Deps: respawn rebuild.

### Tier 2 — Makes It Good (after the story plays end-to-end)
13. **Persistent Backrooms bodies** (M) — cross-save corpse accumulation + render
    strategy. Big atmosphere/lore payoff. Deps: save system.
14. **Backrooms consciousness machine as a place** (M) — make the machine a
    visitable location, not just backdrop lore. Deps: Backrooms.
15. **Antenna-on-every-planet finale** (M) — upgrade Ending C's upload to all
    planets for a harder climax. Deps: #11.
16. **Bespoke per-body content & prefabs** (H) — unique features per planet/moon;
    the biggest boost to the exploration/sandbox layer. Deps: none.
17. **Random alien side quests** (M) — catch-5-fish-style tasks that raise Rebels
    (and nudge Tev) trust; fills the sandbox. Deps: mission system, trust.
18. **Flesh out the rebel faction** (M) — more of the ~20 rebels with dialogue.
19. **Pickaxe for mining** (L) — goods-vendor item; QoL. Deps: none.

### Tier 3 — Someday / Extra
20. **Expanded combat** (variable) — more enemy variety/depth.
21. **More fish species & variety** (L–M).
22. **Fiery Twin / Icey Twin special plan** (?) — once defined.
23. **Deepened endings** (variable) — richer versions once the basics ship.

> **How the open threads (§10) map in:** antenna count → #11 / #15; Rebels-trust
> role → #4 / #17; Phase 1 pacing → #6; Backrooms body details → #13.

> **Audio (production note):** the original instrumental soundtrack (developer /
> *catatonic*, §1.6) is **planned but intentionally scored last**, to match the
> finished game's vibe — not optional, just sequenced at the end.

---

## 9. Parking Lot (carried forward)

Planned items — **now sequenced into the §8 tiers** (kept here as the raw list):
- Bespoke per-body content & prefabs (unique features per planet/moon).
- Random side quests (catch 5 fish → reward; accept/decline on first talk).
- Pickaxe (goods vendor) for mining crystals.
- Expanded combat depth/variety.
- Distinct fish species & more variety.
- A specific plan for the Fiery Twin / Icey Twin binary pair.

---

## 10. Remaining Open Decisions (running list)

- §3.3 / §3.4 — Poolrooms bodies; overworld bodies; body cap; can corpses be
  interacted with.
- §5.2 — the mechanical role of the Rebels/aliens trust meter.
- §5.5 — antenna count (one vs. one-per-planet) for the final upload.
- §5.7 — Phase 1 pacing (how many of Tev's missions gate the meeting).
- §6 — whether the AI knows it's ORG's tool / your condition's architect, and its
  cross-death persistence.

---

*End of v9. The build order is in §8; audio direction in §1.6. Next: pick the
first vertical-slice thread and start building.*
