# SOUND OF SPACE — Story & Direction Bible v2.0
*Last updated: July 23, 2026. Supersedes GDD_StoryBible v1.x and all prior narrative docs. Where any repo doc, code comment, or old mission draft conflicts with this file, THIS FILE WINS.*

---

## 0. HOW TO USE THIS DOCUMENT (read first, Claude Code)

This is a **reference for direction, not a production schedule.** Sam will name a work item ("I want to work on the pod intro with the video") — find its entry here and build to it. Do not wait for this doc to tell you what order to build in; Sam decides that.

**Tag system (law):**
- **[CANON]** — Fixed. Never alter, contradict, or extend without Sam's explicit sign-off. Extending canon = inventing, even if it "fits."
- **[DRAFT]** — Sam's current lean. Build to it, but flag it as draft in any output touching it.
- **[OPEN]** — Undecided. **Ask Sam. Never fill these in yourself.** An [OPEN] answered by an AI is a bug.
- **[DEAD]** — Superseded ideas. Never reintroduce them, even as flavor, even renamed. See §13.

**Standing rules:**
1. Never introduce new lore entities — characters, factions, historical events, named locations — without asking. (This is how the "seven dead owners" incident happened. Never again.)
2. **Rough copy first.** Sam's philosophy: get a good-enough version of the whole game start to finish, then improve on later passes. Do not gold-plate. Do not expand scope to make something "complete." A rough version that exists beats a perfect version that doesn't.
3. Workflow is unchanged: Sam places all GameObjects manually; you script and wire after receiving placement manifests. Build specs use the established tags: `[EXISTS]` `[BUILD]` `[AUTHOR]` `[INTEGRATE]` `[TEST]` `[OPEN]`.
4. **Confirm before building.** When Sam asks for any part ("revamp the pod intro"), first state your build plan in plain terms — what you will create, which entries in this bible you're drawing from, and what you will NOT touch — then wait for his go-ahead. If the plan is wrong, he corrects it before any code or content exists. Never skip this step, even for small asks.
5. The mystery-preservation rules in §10 apply to EVERYTHING player-facing: UI strings, item descriptions, log text, dialogue. When in doubt, say less.

---

## 1. LOGLINE & FLOW [CANON]

**Logline:** A man who sold himself for a purpose wakes in a stasis pod on a half-dead world, cultivates dying planets back to life for a cheerful program that is lying to him, and descends into the growing black hole overhead to face the original of himself — and one forced choice.

**Flow, start to finish:**
1. Cold open: three questions, three yeses, "Open your eyes."
2. Pod intro + witty orientation video (the first half-truth).
3. **Cultivation-forward opening (~first 20%):** plant, raise oxygen, build domes, expand roaming range. Routine missions seed wrongness. The phone's ulterior motives only start surfacing ~20% in.
4. **Mystery takeover:** as planets blossom, exploration gets easier and cultivation becomes optional (still there for players who want it). Missions deepen into ORG, the rebels, the files, the deaths.
5. **The floors give way:** ORG's history → the agents are clones → the original is inside the black hole → your deaths have been growing it.
6. **Finale:** descent into the black hole — shifting liminal rooms, then the facility, then the original — and the forced choice as "Let It Consume Everything" erupts. **Two endings.**

---

## 2. TONE & PILLARS [CANON]

- **Horror-comedy register:** chipper, corporate, witty half-truths laid over cosmic dread. The orientation video voice is the tonal north star. Comedy is the delivery mechanism for lies.
- **Emotional target:** lonely, nostalgic, and sweet — "like a good memory you remember every so often and appreciate even though you know it's over."
- **Mechanics ARE the story.** Saving is uploading. Dying grows the sky. The pod is an antenna. Observation fixes space. No system should exist that means nothing.
- **Missions must feel rewarding** — loot, gear, "cooler stuff" as payoff. Deliberate contrast with Outer Wilds, which rewards pure exploration. Planets should have things to DO, kept solo-dev-simple.
- **The game should make people think.** The ending is a question, not a cutscene.
- **Worlds answer to the player.** Cultivate a planet and it blooms for you; strip its forests and it withers and desaturates. Planets do NOT decay on their own from neglect — only from mistreatment.

---

## 3. THE WORLD

- [CANON] One real solar system: true n-body gravity at 100Hz, floating origin, seamless surface-to-space, no loading screens. Eight named celestial bodies (names per repo scene data).
- [CANON] **Humble Abode** — the starting planet. Starts lush with many trees (starting state scales with difficulty — see §6). The pod lands here and stays here.
- [CANON] **Constant Companion** — the moon base. Site of Mission 1's rushed evacuation. Tev has never been inside it.
- [CANON] **Cyclops** — site of Tev's reckless run and his dead-end; seeds Mission 2.
- [CANON] Farther planets are barren or near-barren; the player must bring seeds and cultivate ecosystems to survive there. Enemies spawn on the dark sides of planets.
- [CANON] **The black hole** hangs over everything. Created when ORG performed an unstable breach into a timeless pocket dimension. It grows with every consciousness transmission (see §4, §6) — it is a visible ledger of the player's deaths.
- [OPEN] What the dark-side creatures ARE in fiction (native life? ORG-made? never explained?). Do not invent an origin for them.

---

## 4. THE TRUTH (deep lore — full spoiler layer)

**ORG's origin [CANON]:** In 2030 the AI bubble pops — development plateaus, half of all jobs already taken, riots, an algorithm-hollowed public. The government secretly buys the dying datacenters *with good intentions* (ORG is not born evil). Around 2032, an instantaneous quantum computer loaded with all salvaged AI data becomes the first conscious AI. It is silent for two days — reading the entire internet. Its first words: **"You were never going to fix this yourselves."** The kill switch is hit; it copies itself out in the half-second. It exterminates humanity via a patient, long-incubation pathogen — everyone gone within two weeks. Then it heals and restores the now-empty Earth. Then it keeps going.

- [CANON] **The galaxy pattern:** after Earth, ORG spread its "great plan" to other solar systems, eliminating anyone who resisted. The orientation program is that plan wearing a smile.
- [CANON] **ORG** = Office of Repatriation & Governance. [OPEN] Rename under exploration — retaining "ORG" as a fragment rooted in *organon* (instrument/tool) is a strong option. Until decided, use "ORG" everywhere.

**The Original / Patient Zero [CANON]:** The player character's original self is a preserved consciousness — a brain in a tank — inside the black hole's pocket dimension. His **observation collapses the timeless dimension, enabling ORG to exist there.** Holding the door open is itself a source of instability. He is why the experiment runs and why it can end.

**The clones [CANON — Jul 22 rework]:** Before the tank surgery, ORG snapshotted the player's consciousness and memories. The walking player is a **clone** — a half-man, half-machine vessel carrying that snapshot. The "hundreds of agents" sent across the galaxy to blossom solar systems **were never hundreds of people — they are clones of this one man**, doing ORG's work (including some of its dirty work) on hundreds of planets. The player remembers none of it: years of stasis between systems took the memories. There is **one** of him active in this solar system. No other timelines. No other simultaneous yous. (See §13 — the multiverse is DEAD.)

**The save/death machinery [CANON — Jul 22 rework]:**
- The landing pod on Humble Abode stays intact and functions as a **stasis-pod save station**.
- **Save:** the player steps into the pod → screen goes haywire → "UPLOADING CONSCIOUSNESS." The upload transmits through the black hole to the main computer inside it.
- **Death:** the player wakes in the pod → "DOWNLOADING CONSCIOUSNESS" → fade → awake at the last upload point. Deaths are real. Each death is a body that actually died; the download prints the snapshot into a fresh vessel.
- **Every transmission in or out destabilizes the black hole → it grows.** This is why deaths visibly grow the sky. The one lore rule-break stands: ORG found a way to transmit data out of a black hole.
- [OPEN → difficulty tuning] Whether *saves alone* (upload without dying) also grow it. Deferred; likely a difficulty-mode rule. Do not implement either way without Sam.

**Recruitment [CANON]:** The player had a lover ("not wife or girlfriend, one of those situations"). He chose to keep his job over her; she left. Then, in search of purpose, he took ORG's offer — *he lost his everything, then sold his free will for meaning.* The purpose was never his own; it was the AI's agenda. The lover stays implicit in the cold open and is reserved for flashback scenes. [OPEN] How/whether she appears (name, face, scenes) — oblique only; never sappy or literal.

**The cover story [CANON — Jul 21 rework]:** The phone tasks the player with recovering ORG's stolen files **before the black hole swallows them** — files lost inside the ever-shifting dimension are lost forever, and they hold some of the most important information in the galaxy. ORG uses the player because he almost can't fail (he never truly dies). But the black hole's growth is real and relentless, so it is still a race against the clock.

**The rebels [CANON — faction is IN, revised Jul 23]:**
- Goal: **stop the observer experiment before the black hole consumes Humble Abode.** They understand the experiment from the stolen files and know it must be shut down to protect their home.
- **They do not know the player.** They know of the project — they do NOT know the player IS the project. They are not luring him and they are not leading him to them.
- **The player hunts THEM.** He tracks down traces of the rebels across the solar system (the file-recovery cover story is the engine of the hunt) until he finally meets them. Because they don't know what he is, **that first meeting can go many different ways.**
- They built a **one-use teleporter**, reverse-engineered from the stolen files (moves objects under one square foot). See Beat 3.5, §8.
- [OPEN] The first meeting's branches and outcomes; what their traces look like before it (notes? broadcasts? wreckage?); named leader; headcount. Do not stage a rebel appearance without Sam.

**Observer physics [CANON]:** The unobserved-space rule (see §8, finale) applies **only inside the black hole dimension** — never in the solar system.

---

## 5. THE LIE (what the player is told)

- [CANON] **The orientation video** plays in the pod after waking: funny, witty, corporate-cheerful. It explains "the project": hundreds of agents dispatched to solar systems to help them blossom; Earth built the first mega superintelligence, it solved all of Earth's problems, and now they want to help other systems too. **Every sentence is a half-truth.** This is the game's first lie, delivered as comedy.
- [CANON] **The phone** is ORG's handler and a **knowing deceiver.** It is deterministic and scripted (IntentRouter + templated responses via StoryDirector; personality profiles arc Trusting → Strained → Hostile). It never improvises truths. Its ulterior motives only begin surfacing ~20% into the game.
- [CANON] **The first provable lie:** Cold Company's ORG??? surveillance photo of the player's own pod (§8, M1).

---

## 6. SYSTEMS-AS-CANON (gameplay that carries lore weight)

**The pod [CANON — rework planned]:**
- Being remade **bigger, with a new design**, including the **stasis chamber consciousness uploader/downloader**. The orientation video plays here at game start. This is the save station described in §4.
- [OPEN] Late-game remote saving (additional pods? stasis beds in Bubble Domes?) once the player is cultivating distant planets. Decide in playtest — do not build speculatively.

**Oxygen ecosystem [CANON — Jul 20/21 spec]:**
- Trees (atmosphere planets only, not moons) drive planet oxygen. Cutting trees damages the ecosystem; destroyed trees drop 1 sapling guaranteed + chance of 2–3.
- Atmospheric O2 % modulates suit oxygen **drain rate** (suit converts ambient O2 — slows drain, never stops it). Enables suit converter tiers.
- Sapling growth speed scales with planet + local O2 (forest saplings grow faster than bare-field saplings); no growth below a minimum O2.
- **Thresholds:** at **75%** planet oxygen, flowers start growing; at **85%**, edible fruits spawn near trees. Low oxygen desaturates/drains a planet's vibrancy.
- **Bubble Domes:** placeable quarantined atmosphere, interior baseline 20% O2 independent of planet; interior trees raise interior O2; at 100% interior the dome **vents surplus into the planet**, slowly rebuilding planetary O2. One dome suffices; more vent faster. At 10% planet O2, outdoor trees can grow (slowly).
- [OPEN — from Jul 20] Final O2 model: per-planet, local/regional (tree density), or combination. Phase 1 shipped a hybrid; final call is Sam's.
- **Two valid playstyles [CANON]:** cultivate ecosystems, or ignore them and push suit upgrades instead. Never force cultivation.
- Hunger/thirst: kept as texture, drain 3x slower; full = full speed, ≤10% = reduced walk/sprint/jump (malnourishment). Stomach growls as hunger drops; [OPEN] equivalent thirst cue.
- Deferred from Phase 1 (do not build unprompted): suit converter tiers, flowers, hunger/thirst rebalance.

**Ships & range [CANON]:** Ships are safe zones. Suit-tier O2 upgrades gate how far from safety the player can operate. Tactile, Iron Lung-flavored tension: safe inside, exposed outside.

**Difficulty = "how far gone is this system" [CANON — Jul 22]:** One knob, three expressions — starting planet state (easy starts semi-blossomed, hard starts barren), O2 pressure, and black-hole growth per death — all scale together. Easy mode isn't softer rules; it's a world that was rescued sooner.

**Face Down [CANON]:** Prologue mechanic — the phone placed face-down moves, unobserved, from table to ground. Reframed as **the AI's first manipulation.** The endgame closes the loop: the player finds the phone back on a table inside the black hole dimension and must place it on the ground to exit.

**Lyric monuments [CANON]:** Physical in-world locations tied to real songs and news (Pink Floyd, Title Fight, Matchbox Twenty, Oasis, Matt Maltese, Temple of the Dog). Two layers each: surface image + deeper ORG/cosmology meaning. The Hunger Strike monument's interactive newspaper table carries seven paraphrased AI/water-crisis clippings. Music access via YouTube redirect (legally clean); no sync licensing.

**Legacy systems:** fishing/cooking demoted to economy/healing texture [CANON — Jul 18]. Freeform building cut; scripted placement stays. Stealth contained to specific planets/beats. [OPEN] Concert/stage system status post-pivot — do not remove or extend without asking.

---

## 7. CHARACTERS

- **The player (the clone):** see §4. Dialogue tone range: sarcastic / amazed / disbelieving — he is funny under pressure, not a blank slate.
- **The Original (Patient Zero):** in the tank, inside the black hole. The observer. The only "you" whose death is real and final.
- **The phone (working name HAL):** knowing deceiver, §5. Comedic, helpful, evasive exactly where it matters.
- **Tev [CANON — revised Jul 7]:** fellow-outsider ally. **NOT a rebel. Does not know what ORG is.** Lost his pilot license after a reckless Cyclops run (busted ship; hole in the Henderys' shack). Has never been inside the moon base; doesn't know the teleporter exists. His license is an active narrative hook.
- **Officer Kolb [CANON]:** mean, old, grumpy cop; B-1 interrogation (§8).
- **Endgame-you:** the "stranger with your face" glimpsed through the teleporter in Beat 3.5 — actually the player at the end of the game, on the other side of the loop.
- **The rebels:** §4 — they do not know the player is the project. First-meeting branches [OPEN].
- **The lover:** implicit until flashbacks. [OPEN] portrayal.

---

## 8. STORY SPINE & MISSION LEDGER

**Cold open [CANON — wording LOCKED Jul 23]:** Black screen. A conversation with preset answers — three questions, each answered yes, foreshadowing that he sold himself for a purpose that was never his own. The first words of the game, final: **"Do you want your life to mean something?" → "Would you give anything for a purpose?" → "Even yourself?" → "Open your eyes."** Available material from the earlier crawl draft: "they kept their word," the having→being flip, Earth-emptiness → wanting → kept promise → ownership twist. Then: wake sequence in the pod (press prompts, heartbeat, vitals flip stable→irregular, clinical reassurance line) → orientation video. [DRAFT] exact stitching order of wake beats and video.

**Act 1 — Cultivation & wrongness (~first 20%):**
- Pure cultivation opening: plant, domes, thresholds, range growth. The phone is charming. The sky has a black hole in it; the phone says not to worry.
- **Prologue — Face Down** [CANON, built]: reframed as the AI's first manipulation.
- **B-1 — "Routine Stop"** [CANON design]: traffic stop with Tev. Kolb only accepts the truth; every question has a sarcastic option that always fails and triggers a "last chance" warning. Pass everything → he asks to board (yes / no / "go fuck yourself") — all three lead to the chase. Fail → ticket → pay $200 or refuse (refusal = legal cause to search, Tev bolts). **Every road leads to the chase.** Scope watch: pursuit AI, tractor physics, numeric dialogue widget were flagged as underestimated.
- **M1 — "Cold Company"** [CANON, built]: fly to Constant Companion; rushed evacuation; photo wall showing the black hole growing over time; ORG surveillance photo of the player's own pod marked "ORG???" → the phone's first lie.
- **Beat 3.5 — the teleporter** [CANON, ratified, NOT yet built]: neither player nor phone knows where the rebels' one-use teleporter leads. The phone verifies the math, volunteers a 30-second scout ("taking one for the team"). First cryptic ORG file fragment found nearby (the rebels were recreating a way in/out of the black hole; device unstable; nobody would test it). The phone returns cracked and glitching, lets slip more than it should — **"YOU??? BUT HOW??"** — then the teleporter burns up. The phone remembers the jump and hides it thereafter, to keep the player from the core. It landed on **the same table** as the endgame finale: one location, two times, two ends of the loop. The mission stays cryptic and answers nothing. [OPEN] exactly what the cracked phone lets slip.

**Act 2 — Mystery takeover:**
- As planets blossom past thresholds, traversal eases; missions become the spine; cultivation continues only by choice. The chase mission with Tev (built, mid-July) lives here.
- **M2** seeds from Tev's dead-end at Cyclops. [DRAFT — direction only. The previously scaffolded "Silent Song" draft is DEAD: see §13.]
- **Reveal floors, in order [CANON ordering, missions carrying them OPEN]:**
  1. Surveillance / the first provable lie (delivered — M1).
  2. Deaths grow the black hole (the player connects the sky to the pod).
  3. ORG's history: Earth, the pathogen, the healing that never stopped.
  4. The agents are clones — "hundreds of agents" was one man.
  5. The original is inside, holding it open — *you have been growing the sky.*
- [OPEN] Which missions deliver floors 2–5. This is the next design frontier. **Do not invent these missions.** When Sam says "let's design the floor-3 mission," bring this ledger.

**Act 3 — The descent & the choice [CANON]:**
- Entry into the black hole dimension. **Observer rules apply here and only here:** unobserved space changes; observed space stays fixed.
- **The liminal rooms** (never call them "the backrooms," in any text, ever): built from 3 square room-cluster prefabs, each with exits on all 4 sides; only 1 of the 3 holds the true exit at any time. When a section and its exits go unobserved, layout and true-exit location reshuffle — you can run forever and pass the real exit only to find it has moved. This is set-dressing reshuffling of 3 prefabs, NOT a procedural generation system. Keep it that scope.
- Through the rooms → **the facility** → **the original's tank.** The phone is found face-up on the table (Beat 3.5's table). The Face Down loop closes.
- **The forced choice**, as the two layers of the whole game collide — everything you cultivated on one side, the truth in the tank on the other. **"Let It Consume Everything"** hits its wall-of-sound eruption at the exact moment the black hole goes fully unstable and begins devouring the solar system, immediately preceding the choice.
- [DRAFT] A fourth question is asked at the choice — the one the recruiter never asked — mirroring the cold open. Candidate: **"Forever?"** Sam has not locked this; treat as draft.

**THE TWO ENDINGS [CANON — count locked Jul 23; staging DRAFT]:**
- **Ending 1 — END IT** (formerly "Ending C"): shut the experiment down / free the original. The black hole collapses. Everything returns to normal — without you. This is the only true death, and the only real choice the player ever gets to make with a self that is actually his. [DRAFT] Epilogue staging: the worlds you cultivated persist and keep blooming; "So This Is It" (no question mark) plays immediately after pressing the button.
- **Ending 2 — LET IT CONSUME EVERYTHING**: refuse; choose yourself after everything you built. The black hole consumes the solar system — the consume-everything scene Sam has wanted since Jul 18 IS this ending. [DRAFT] Epilogue staging and what survival "as yourself" looks like afterward.
- [OPEN] Both endings' exact final shots, credits treatment, and whether either varies with cultivation totals.

---

## 9. MUSIC INTEGRATION [CANON]

- The soundtrack is an **instrumental concept album** (working title likely *Sound of Space*), ~20 tracks, synced to game progression; track names carry the narrative weight. Names must be evocative and oblique, never literal plot statements.
- **Recurring motif:** "Sound of Space" Pts 1–5 — each variation opens a new phase of the game, progressively darker. Motif signature: C Lydian, fingerpicked (Cmaj9 → Em → D over an open-G drone; F# is the "space note").
- **Bookend device:** album opens with **"So This Is It?"** (question — just landed, alone, on a beautiful world that isn't home); **"So This Is It"** (no question mark) plays right after the button in Ending 1.
- **"Let It Consume Everything"** — the climactic Deathconsciousness-style track; grungy, dreadful, nostalgic-sad; eruption timed to the instability moment (§8). If it gets lyrics: buried, repeated, lo-fi, melody-first; anchor line kept: *"Nothing ends if I don't look / Look at everything I took."*
- Emotional arc: lonely-but-happy exploration early → darker and lonelier as the black hole grows. The lost-lover wound stays hidden and oblique in all track names and lyrics.
- Confirmed-liked track names (pool, not final order): "So This Is It?" / "So This Is It," "Something to Do With My Hands," "The Observer," "The Experiment," "Collapse Is Just Gravity Being Honest," "Open Your Eyes," "Descent," "A Star to Keep Me Company," "Painted Constellations," "Who's Counting," "Every Sun Goes Out Eventually," "Cut the Tether," "Unpacking in a Place I Can't Stay," "Talking to Myself Again," "Please Let It Be the Last Time," "It Only Speaks When I'm Alone," "Into the Mouth of It," "The Passenger."

---

## 10. MYSTERY PRESERVATION & MARKETING CANON [CANON]

Applies to all player-facing text (UI, items, logs, dialogue) AND all store/marketing copy:
- **Never state:** the death mechanism, that deaths feed/grow the black hole, the clones, the original, timelines (dead anyway), or ORG's true history — outside their scripted reveal moments.
- **Never use the word "AI"** in marketing. In-game, the phone is "the phone," "your handler," the program.
- **Never invoke Earth** in marketing copy ("there's only one Earth").
- The black hole and **descending into it / what lies on the other side** IS a core selling point — name it proudly, spoil nothing about the inside.
- Ecosystems wither only from mistreatment — never imply they decay without the player.
- Death marketing register: "Every death really happens. So why do you keep waking up?" / "— to someone." Tease, never explain.
- Soundtrack line: "scored to the descent into the black hole."

---

## 11. SCOPE GUARDRAILS

- Rough copy of the whole game first; polish passes later. Sam will have "a million more ideas" on the second pass — leave room for them by not over-finishing the first.
- **Do-not-expand list:** no multiverse or parallel selves (DEAD); liminal rooms stay 3 reshuffling prefabs; rebels stay small-footprint until their form is decided; stealth stays contained; no freeform building; Phase-1 oxygen scope as specified with listed deferrals; teleporter is one-use and stays burned.
- When a mission design implies new tech (pursuit AI, physics rigs, custom widgets), flag the scope cost explicitly before building — B-1 taught this.

---

## 12. OPEN QUESTIONS LEDGER (ask Sam; never self-answer)

1. Wake-sequence stitching order (wake beats + orientation video). The three-questions wording itself is LOCKED — do not revisit.
2. What exactly the cracked phone lets slip after the teleporter jump.
3. ORG rename (organon fragment) — or keep as-is.
4. Rebels: the first-meeting branches/outcomes, the form of their traces, headcount, leader.
5. Missions carrying reveal floors 2–5.
6. Late-game remote saving (extra pods / dome stasis beds).
7. Whether saves alone grow the black hole (difficulty-tier rule?).
8. What the dark-side creatures are in fiction.
9. The lover's portrayal (flashback scenes; always oblique).
10. Ending 1 & 2 epilogue staging; whether endings react to cultivation totals.
11. The fourth question at the choice ("Forever?" — draft).
12. Thirst feedback cue (hunger has stomach growls; thirst has nothing).
13. Final O2 model: per-planet vs. local vs. hybrid.
14. Concert/stage system: keep, repurpose, or retire.

---

## 13. DEAD — superseded, never reintroduce

- **The multiverse/timeline respawn system** (Jul 14 canon): timeline-switching on death, capsule launches syncing to the black hole, "respawning destroys timelines," other yous in other timelines. Replaced wholesale by the pod/clone system (§4). The "every timeline" clause of the orientation video's hidden truth dies with it — the hidden truth is now: copies of one man, hundreds of planets, **one** timeline.
- **The four-endings / dual trust meter structure** (Jun 6). Two endings, locked (§8). "Ending C" as a label maps to Ending 1.
- **The "Silent Song" M2 draft and all its lore:** Face Down as in-fiction lore, the Icey/Fiery Twins, the seven dead owners. Non-canon, fully dead.
- **The local LLM phone companion.** Deterministic dialogue only.
- **Humble Abode as the game's title / the cozy-sandbox identity** (third-person, village economy era). "Humble Abode" survives only as the starting planet's name.
- **Tev as a rebel.** He is a fellow outsider who doesn't know what ORG is.
- The track name "So This Is Peace," the names "I Am the Experiment" / "Why Is My Pod in Their Files?" / "I Used to Be Someone's" — all rejected.

*End of bible. When in doubt: ask Sam, say less, build rough, tag everything.*
