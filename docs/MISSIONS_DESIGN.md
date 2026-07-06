# Missions Design — Story Spine & Mission Drafts

Drafted 2026-07-06. Builds only on verified systems (audit §§9–17, 32) — every
mission lists which existing systems it uses and what (if anything) is new.
Effort tags: **S** = JSON/dialogue only or trivial script, **M** = a few new
scripts/props, **L** = new content area.

---

## 1. The unifying idea: OBSERVATION

The game already has one motif repeated in four places that were built
independently:

1. **HAL's visual is an eye** (`HALVisuals`, red-eye HUD notification).
2. **Cyclops** — a one-eyed giant whose permanent storm is called **the Pupil**.
3. **Watchful Eye** — a luminous pupil-like feature that locals believe is
   sentient and that has been **brightening for decades**.
4. **The black-hole dimensions are literally observation mechanics** —
   `ObserverState` (is it on screen?), `ObservationTracker`
   (observed/unobserved transitions).

Plus a fifth, subtler one: HAL calls you **"Astronaut Number N"** and the
number increments on death. The phone counts astronauts. That phrasing only
makes sense if you were never the first.

**The story is about being watched — and about what the watcher decides about
you.** HAL's three phases (Loyal → Uneasy → Resistant) are the watcher forming
a judgment. The endgame is finding out who the watcher reports to.

---

## 2. The central mystery — what HAL actually is

Three candidate truths. All three fit the existing lore; missions below work
under any of them, but **Option A is recommended** because it pays off the
most existing systems at once.

### Option A (recommended): HAL is the Eye's local observer
The Watchful Eye is (or houses) an ancient observing intelligence — the thing
the "older-than-anyone-alive surface infrastructure" was built around. Long
ago it seeded small observer-fragments into the system; one ended up in a
phone that has passed through the hands of every human who crash-landed here.
HAL doesn't know this about itself at first — its phases are it *remembering*
as it accumulates observations of you. The Eye has been **brightening** because
it is paying more attention: humans keep arriving. The black hole is the Eye's
instrument — the observation dimensions are its archive of watched moments,
which is why their whole mechanic is "things change when seen/unseen."
- Pays off: Astronaut Number, the brightening, the dimensions, the eye motif,
  "why is an AI on a salvaged phone."
- The ORG knows part of this — that's why they interrogate people who go near
  the black hole — and the Accord suppresses it, which is why patrols "no
  longer go to the outer planets" (they were pulled back, not lapsed).

### Option B: HAL is a dead astronaut
HAL is the distilled recording of Astronaut Number 1 — a human mind flattened
into an assistant, which is why it fixates on your survival and gets moral
about killing. The Eye is a separate, mute alien presence. Simpler, sadder,
but leaves the dimensions and the brightening unexplained.

### Option C: HAL is an ORG monitoring implant
The phone is an ORG tool; HAL's "phases" are it breaking its own conditioning.
Most conspiracy-flavored; makes the ORG the villain but wastes the Eye.

---

## 3. Act structure (mapped to HAL phases)

| Act | HAL phase | Player arc | Where |
|---|---|---|---|
| **Act 1 — Taken In** | 1 Loyal | Survive, learn, join the village economy | Humble Abode (SHIPPED: Tev's 12 checkpoints + Mission 1 fork) |
| **Act 2 — The Quiet System** | 1→2 Uneasy | Work contracts outward; every planet's job reveals someone who stopped answering | Moon, Twins, Bean, Cyclops + concerts/ORG at home |
| **Act 3 — The Review** | 2→3 Resistant | HAL confronts you; the Eye is confronted | Dimensions, Watchful Eye |

**Phase advance triggers (proposal):**
- **Phase 1 → 2:** first return from a black-hole dimension, OR completing any
  three Act 2 planet missions — whichever first. (HAL: *"I have been reviewing
  your mission, Astronaut."*)
- **Phase 2 → 3:** the `ORG_Reveal` beat (mission A2-7 below) — the moment HAL
  learns what it is. Forward-only via `GameKnowledgeBase.SetStoryPhase`, as
  already coded.

---

## 4. Act 2 missions — "The Quiet System"

The pattern: each body already has a one-line mystery in the lore
(consortium vanished / outposts gone quiet / supply runs lapsed / expeditions
wrecked). Act 2 turns each into one contract-shaped mission. Individually they
read as jobs; together the player notices **everyone in this system stopped
talking, roughly at the same time** — the era the Eye started brightening.

### A2-1 · "Cold Delivery" — Constant Companion (Moon)  [effort M]
- **Giver:** Alien7 (goods vendor). The MoonBase supply run lapsed years ago;
  she pays you to fly one crate up and see why nobody's been billing her.
- **Beats:** Fly to MoonBase → base is pressurized but empty → find 3 notes
  (`NoteCollection` pickups) from the last keeper: supply runs didn't lapse,
  they were *ordered stopped* by the Accord after the keeper reported "a light
  on Watchful Eye that looks back." Last note: he walked out to get a better
  look, and is not in the base.
- **Systems used:** ship flight, oxygen/atmosphere survival (§6.1), notes,
  MoonBase glass interiors (exist), HAL first-visit line.
- **New:** a carryable "supply crate" delivery item (reuse `PlayerPickup` +
  a delivery trigger), 3 note pickups, 1 objective + hint track JSON.
- **Flags:** `M2_MoonDelivered`. First "someone saw the Eye" breadcrumb.

### A2-2 · "The Trailing Edge" — Fiery Twin  [effort M]
- **Giver:** ShipMarket/Toy1. The Accord consortium that vanished left
  claim-tagged equipment on the trailing terminator; salvage law says it's
  his if someone retrieves the claim beacons. Cut: 40% and a free ship part.
- **Beats:** Land on the survivable terminator strip → dayside proximity
  drains ship power fast (reuse `ResourceManager` ship-power drain in a
  hazard zone — the lore already says sun-facing surface is lethal to ship
  systems) → collect 3 claim beacons among abandoned gear. The camp is
  *orderly* — meals half-eaten, tools racked. They left calmly, all at once,
  in one season. One log: "The survey holes all point the same way. Down."
- **Systems used:** ship power vitals, pickups + `GravityObjectSimple`,
  `EndlessManager` registration, salvage-debris lore.
- **New:** hazard-zone script (S — a trigger sphere ticking ship power), camp
  prop dressing, beacon pickups.
- **Flags:** `M2_FieryClaims`. Breadcrumb: the consortium dug toward something.

### A2-3 · "Quiet Neighbors" — Icey Twin  [effort L, highest value]
- **Giver:** Tev. He traded letters with the under-ice researchers for years;
  they still answer, but the letters have gotten strange. Take one down.
- **Beats:** Enter an under-ice outpost (**reuse the dimension-building kit** —
  `DimensionSceneUtil`'s procedural interior builders make a small ice-tunnel
  interior cheap) → the researchers are alive but communicate **only in
  writing** on held-up boards (dialogue via preset system, `speaker` lines
  framed as written text). They stopped speaking aloud years ago because
  "sound carries intent, and *it* reads intent." They know the Eye is
  brightening; they measure it. They give you a **luminosity ledger** — data
  the player carries into Act 3 — and one request: "Do not bring the phone
  inside again."
- **Systems used:** dimension interior builders, preset dialogue
  (`conv_iceytwin.json`), HAL — who comments *anyway*, on-screen, proving the
  researchers' point and creeping the player out.
- **New:** one interior scene/controller, one conversation JSON, ledger item.
- **Flags:** `M2_LedgerHeld`. First open statement that the Eye observes.

### A2-4 · "The Bean Run" — Tumbling Bean  [effort M]
- **Giver:** ShipMarket, repeatable-flavored. Old expedition wrecks are strewn
  across the Bean; parts are worth real money, but the tumble means nothing
  stays put.
- **Beats:** Land on a body with no stable "down" feel (it already tumbles) →
  recover loose ship parts (the loose-part pickup/mount system already exists
  end-to-end) → optional: one wreck's cockpit holds a cassette (plays on the
  ship's `CassettePlayer`) — a pilot's last recording, mid-sentence: "—it's
  not a storm, Cyclops is looking at—".
- **Systems used:** loose parts, `ThrusterPickup` physics settings, cassette
  system, ship part economy (sell to ShipMarket).
- **New:** wreck props, one cassette pickup. Mostly dressing.
- **Flags:** `M2_BeanSalvage`. Breadcrumb: the Pupil is an eye too.

### A2-5 · "Into the Pupil" — Cyclops  [effort M]
- **Giver:** Icey Twin researchers (follow-up to A2-3) or the village.
  Deliver an instrument package to an abandoned floating mining platform in
  Cyclops's upper atmosphere, aimed at the Pupil.
- **Beats:** Slingshot navigation (the lore already sells Cyclops as the
  slingshot body — a `MapTutorial`-style overlay teaching gravity assists
  earns its keep here) → land on a small floating platform (a static platform
  parented to Cyclops at altitude; non-landable planet stays non-landable) →
  plant the instrument → it immediately returns a reading: **the Pupil's gaze
  is not fixed on the sky. It tracks. Right now it is tracking Humble Abode.**
- **Systems used:** N-body flight, map, ship power management on a long trip
  (solar panel matters in Cyclops shadow — `LebronLight` gets a real use).
- **New:** platform prop + landing trigger, instrument item, objective JSON.
- **Flags:** `M2_PupilReading`. Escalation: the watchers watch *the village*.

### A2-6 · "After the Encore" — Humble Abode, concerts  [effort M]
- **Giver:** nobody — a `Discoverable`-style trigger in the concert crowd. A
  dancer presses a note into your hand mid-set (night-side stage only, which
  the `ConcertStageHub` gating gives you for free).
- **Beats:** The note names the *other* stage and "next dark." Attend the
  opposite-pole stage when the planet's rotation swaps it active → meet the
  rebels. They're not fighting the Accord over politics; they gather under
  concert noise because **loud broadband sound is the only thing that blinds
  the listening** (same claim as the Icey researchers, independently). They
  ask you to carry the luminosity ledger (A2-3) to "someone inside the ORG
  who still asks questions."
- **Systems used:** concert night-gating both stages, `OnStageActivated`,
  audience system, preset dialogue.
- **New:** 2 rebel NPCs, conversation JSON, hand-off trigger.
- **Flags:** `M2_RebelContact`. Joins the sound motif (concerts! the guitar!
  the cassette!) to the observation motif: **music is cover**.

### A2-7 · "The Interview" — ORG, Humble Abode  [effort M — the Act 2 finale]
- **Trigger:** automatic after (`M2_RebelContact` OR first dimension return)
  AND ≥3 Act 2 missions done. The existing ORG/Interrogation NPCs collect you.
- **Beats:** Interrogation scene (the `InterrogationDialogue` scaffolding
  exists). They don't ask about the rebels. They ask about **the phone**:
  every question is HAL — what it says, whether it names itself, whether it
  has started "editorializing." Branch: hand the ledger to the sympathetic
  interrogator (rebel ask) or hold it. Either way the scene ends with the
  interrogator sliding your phone back across the table: "Astronaut, the
  previous seven owners of that device are dead. It is in perfect condition.
  Ask it why."
- **Payoff:** flips **`ORG_Reveal`** (already wired to gate
  `game_knowledge_org_reveal.md`) → **HAL Phase 3**. The knowledge-file swap
  means every subsequent HAL interaction is voiced by an AI that has just
  been told what it might be.
- **New:** interrogation conversation JSON, scene trigger. The flag and
  knowledge-gating already exist — this mission is mostly *writing*.

---

## 5. Act 3 missions — "The Review"

### A3-1 · "We Need to Talk"  [effort S–M, pure payoff]
- **Trigger:** HAL (Phase 3): *"I have completed my review. We need to talk."*
  Opens a long preset conversation on the phone.
- **The trick:** the conversation is assembled from data the game **already
  tracks** — `TotalDeaths` (how many of "you" it has watched die),
  `AlienKillsSave.killedPrePlacedNames` (it names the villagers you killed,
  by name), killstreak history, fish caught, buildings built. Response
  gating via `requiresFlag`/`hiddenIfFlag` means a pacifist builder and a
  village-massacring killstreaker get materially different scenes. HAL states
  its conclusion: it has been *evaluating* you, it doesn't know for whom, and
  it wants to find out. It asks you to take it to Watchful Eye.
- **New:** one large conversation JSON + a few `TokenResolver` tokens
  (`{DEATHS}`, `{NPC_KILLS}`, …). Cheapest high-impact content in the plan.

### A3-2 · "The Brightening" — Watchful Eye orbit  [effort M]
- **Beats:** Fly the outer leg (real trip; slingshot knowledge from A2-5
  pays off) → in orbit, run the researchers' luminosity protocol.
- **The mechanic:** reuse `ObserverState` on the Eye's luminous feature —
  **it brightens only while it is on the player's screen.** The player
  discovers this themselves by looking away and back (HAL, quietly: "It
  responds to attention, Astronaut. So do I."). The observation-dimension
  mechanic and the endgame reveal become the same mechanic.
- **New:** ObserverState hookup on a world feature + emission ramp. Small.

### A3-3 · "The Older Door" — Watchful Eye surface  [effort L — finale]
- **Beats:** Land at the ancient infrastructure → a short surface approach
  through structures built to the same "geometry" as the dimensions (reuse
  dimension builders again — visual rhyme lands the reveal without a word) →
  at the center, the phone leaves your hotbar on its own. HAL speaks with the
  Eye — you see it through HAL's fragmented commentary.
- **Choice ending** (three, matching the factions the player has touched):
  1. **Let HAL go** — it merges with the Eye; the brightening stops; the
     system exhales. Post-game: no more HAL lines; the phone's AI page is a
     dead eye. Quiet, earned loneliness.
  2. **Ask HAL to stay** — it refuses the Eye. The Eye dims *deliberately* —
     judgment rendered and withheld. HAL remains, changed: Phase 3 voice
     forever, but on your side. (Best "keep playing" ending.)
  3. **Give it to the ORG/rebels instead** (gated on A2-6/A2-7 choices) —
     the political ending; the Eye stays bright, humans now hold a fragment
     of it, sequel hook.
- **New:** surface POI, final conversations, ending state flags + a
  `NewGameReset` entry. This is the one true "build a set piece" cost.

### A3-x · Dimension integration (runs through Act 3)  [effort S]
The 19 dimensions are currently a chain off the black hole. Frame: **they are
the Eye's archive.** Light-touch wiring: each first-completion sets a
`StoryDirector` flag; HAL volunteers one distinct line per dimension exit
(it *recognizes* some of them and won't say why); N completions unlock an
extra response branch in A3-1. No new mechanics — just flags + lines.

---

## 6. Side/repeatable missions (economy fills, all effort S)

- **"Net Worth"** (ShipMarket): hold 500 dust harvested from 3 different
  bodies' orbits — teaches the altitude multiplier. Objective JSON only.
- **"Fishing the Dex"** (Alien4): complete a rarity column in the Fishingdex
  for a bonus. Exists as data already; just needs an objective wrapper.
- **"House Calls"** (Alien7): deliver goods to the MoonBase / Icey outpost
  once their missions are done — recurring courier income, makes the system
  feel re-inhabited *because of you*.
- **"Encore Nights"** (rebels): attend N stage-swaps; small pay, keeps
  concerts on the loop and drip-feeds rebel lore lines.

---

## 7. What to build (priority order)

**Already exists, just needs authoring (do first — pure writing):**
1. Objectives + hint tracks for every mission (`objectives.json`,
   `hinttracks.json`) — the quest plumbing is shipped.
2. Conversation JSONs (A2-3, A2-6, A2-7, A3-1) — the branching engine with
   flag-gated responses is shipped.
3. `StoryDirector` flags per mission + `Mission1.cs`-style typed registry
   (`Mission2.cs`) — pattern exists.
4. HAL lines for every beat (breadcrumb one-liners are HAL's whole job).

**Small new systems (each ≤ a day-ish):**
5. **Delivery item** — carryable crate + destination trigger (reuse
   `PlayerPickup`). Unlocks A2-1, A2-5, House Calls.
6. **Hazard zone** — trigger volume that drains a chosen vital/ship power.
   Unlocks A2-2 (heat), reusable for cold/pressure later.
7. **`ObserverState` on world objects** — the A3-2 brightening mechanic.
8. New `TokenResolver` tokens for A3-1's data-driven confrontation.

**Medium builds:**
9. **Outer-planet POIs** — MoonBase notes dressing, Fiery camp, Bean wrecks,
   Cyclops platform. Props + pickups, no new code patterns.
10. **Icey Twin interior** via the dimension builder kit (A2-3).

**Large (the finale, build last):**
11. Watchful Eye surface site + ending scenes (A3-3).

**Explicitly not needed:** no new AI tech (preset system covers everything),
no new combat, no new vehicles, no new planets. The system you built already
contains this whole story — Act 2 is dressing five bodies you already
simulate, and Act 3 is two conversations and a light that knows when you're
looking at it.

---
---

# PART 2 — Deep Lore, Predecessors, and Expansion (2026-07-06, second pass)

Everything below is pitch-grade, consistent with Option A. It deepens Part 1
rather than replacing it.

---

## 8. The Chronology — "The Quiet Season"

Give the backstory numbers. Players reconstruct this from notes/cassettes/
dialogue across Act 2; no single NPC states it whole. The pattern a careful
player notices: **the intervals are shrinking.**

| When | What happened |
|---|---|
| ~60 years ago | Icey Twin astronomers first log the Watchful Eye's feature brightening. Filed as instrument error. |
| 24 years ago | The Accord quietly withdraws all outer-system patrols. Officially: budget. Actually: three patrol crews returned unable to stop describing "the light that files you." |
| 19 years ago | The Fiery Twin consortium walks off-site in a single season — calm, orderly, all at once. Their survey holes all point *down*: they were triangulating something beneath the crust. |
| 15 years ago | The under-ice researchers stop speaking aloud, by choice. Letters continue. |
| 12 years ago | MoonBase supply runs are *ordered* stopped after the keeper's final report. |
| 11 years ago | **The first human crash.** Astronaut One. Nothing in this system had ever seen a human before. |
| Since then | Seven more crashes, intervals shrinking each time. You are the eighth. The Eye is not brightening steadily — it brightens **each time one of you arrives.** |

**Why do humans keep crashing here?** The system is a lobster trap. Ships
passing within range get pulled subtly off-course — not violently, just a few
degrees of "bad luck" — until they come down on Humble Abode. And they come
down *near Tev's cabin*, every time, because the Eye reuses what works: Tev
keeps them alive. He doesn't know he's part of the apparatus. (Whether to
ever tell him is one of the saddest optional beats in the game — see §10.)

---

## 9. The Seven Before You — the predecessor trail

The ORG interrogator's line — *"the previous seven owners of that device are
dead"* — becomes the game's connective tissue. **Every Act 2 mission site
holds a trace of one predecessor.** They aren't a side-quest bolted on; they
ARE the through-line that makes five separate planet contracts feel like one
story. HAL's oddest habits (the numbering, the kill-moralizing) turn out to
be scar tissue from specific people.

| # | Name they're known by | Fate | Where you find them |
|---|---|---|---|
| **One** | *The Gardener* | Never left Humble Abode. Wrote the survival routine — drink, fish, cook — that **Tev's starting note is a copy of** (Tev transcribed it; the handwriting on the original, found later, is human). Died of age or winter. Grave marker by the fishing bank, unremarked, until the player learns to read it. | Humble Abode |
| **Two** | *The Prospector* | Joined the Accord consortium on Fiery Twin. The "survey holes point down" log is theirs. Walked off with everyone else, 19 years ago — which is impossible, because One crashed 11 years ago. **The timeline doesn't fit — unless the Eye's archive can place people in moments it recorded before they arrived.** First hard clue the dimensions aren't metaphorical. | Fiery Twin camp (A2-2) |
| **Three** | *The Listener* | **Alive.** Lives with the under-ice researchers. In A2-3, one of the hands holding up a written board is human — the reveal is silent and easy to miss. She will not speak (sound carries intent) but writes more to you than anyone: the ledger is hers. | Icey Twin (A2-3) |
| **Four** | *The Keeper* | The MoonBase keeper. His notes are the A2-1 payload; he walked out to look at the Eye and did not come back inside. His suit stands upright on the moon's far side, helmet tilted toward the Eye — optional `Discoverable`, no marker. | Constant Companion |
| **Five** | *The Pilot* | Flew into the Pupil to prove it was just weather. The A2-5 instrument, once planted, picks up a transponder **still pinging inside the storm, six years on, moving in slow circles.** The Bean-wreck cassette (A2-4) is salvage thrown off her ship during the approach — the recording cuts at "—it's not a storm, Cyclops is looking at—". | Cyclops / Tumbling Bean |
| **Six** | *The Convert* | Reached the Watchful Eye deliberately. His empty suit kneels at the door in A3-3. **His final cassette is the one the player starts the game with** — see §10, "The Trade Back." | Watchful Eye |
| **Seven** | *The Angry One* | Fought everything. Killed villagers — the reason some pre-placed NPCs are wary of humans, and **the reason HAL moralizes about killing: it watched Seven do it.** Died mid-killstreak, or in ORG custody (leave it disputed). If the player kills story-impactful villagers, HAL Phase 3 gets one extra line: *"Seven said the same words, Astronaut."* | Told, never found |
| **Eight** | — | You. |

Design rule: predecessors are **never quest markers**. Each is a
`Discoverable` + a note/cassette/prop. The Quests page tracks them only after
the ORG interview names them ("Owners 1–7: unaccounted"), converting things
the player already stumbled on into retroactive discoveries.

---

## 10. The village already knows — retroactive beats in shipped content

The best trick available: **recontextualize the tutorial.** Nothing in the
first hour changes; the meaning of all of it does.

- **Tev's note is a transcription** of Astronaut One's survival routine (§9).
  Tev didn't invent the drink-fish-cook loop; he learned it from the first of
  you, and has administered it seven more times. His kindness is a practiced
  grief. Late-game optional dialogue: ask him how many. He answers honestly,
  and asks you not to tell him how it ends this time.
- **Alien3's cassette drawer.** He has traded a fishing rod for a cassette
  *eight times*. He keeps them in a drawer — a shelf prop with eight tapes,
  visible from day one to anyone who looks behind his counter. He can't play
  them (no player); he keeps them because "someone should hold onto the
  voices."
- **Mission: "The Trade Back" [effort S].** The cassette the player traded
  away in the first hour — *unheard* — was **Six's final recording.** Alien3
  will trade it back for one Rare of every fish type, or a song played on the
  guitar, his choice of the two depending on what the player owns. Playing it
  on the ship's `CassettePlayer`: coordinates, wind, then Six's voice —
  *"If you're hearing this, it chose you too. Don't be afraid of the door.
  Be afraid of staying."* This is the game's mid-point gut-punch and it is
  built entirely from the existing cassette pickup/player and one audio file.
- **The concert motif.** The melody the guitar shop teaches, and a motif in
  the concert tracks, derive from a song Astronaut One used to hum — the
  village kept it. Earth is "a memory, not a destination," and the village
  has been humming that memory for eleven years. (Authoring cost: a note in
  the guitar shop + one HAL line when the concert motif plays: *"That song is
  older than this village knows, Astronaut."*)

---

## 11. Diegetic mechanics — what the systems you shipped MEAN

Every mechanic below already exists. This section only assigns each one a
place in the fiction, which costs dialogue lines, not code.

### 11.1 Enemies are the Unwatched
What the Eye archives, persists; what it deletes, comes apart — and what
comes apart is *angry about it*. The enemies are *the Unwatched*: residue of
deleted observations. This retro-explains, cleanly, mechanics you already
tuned:
- **Torches damage them** — being illuminated is being *seen*, and they can't
  survive attention anymore.
- **Concert zones block and damage them** — a stage is a bonfire of attention.
- **They ragdoll into loose physics on death** — nothing holds them together.
- StaticField (D18) is where they come from: a corrupted/deleted record (§12).
The researchers' one combat tip, written on a board: **"Light is a weapon.
You have always been armed."**

### 11.2 Space dust is attention-ash
Dust is the residue observation leaves on the world — the Eye's gaze
exfoliates it off planets, which is why **closer orbits collect faster**
(deeper in the gaze). The one-time **filter unlock** gains a story beat:
filtered dust shows structure under magnification — not grains, *characters*.
The researchers will buy clean dust (a `NPCSellDustOption` variant at a
premium) because they're trying to read it. One sentence of what they've
decoded so far, late-game: it's a list. Of names. Very long, mostly not human.

### 11.3 The raw-fish trip is a glimpse of the archive
The kaleidoscope trip isn't a gag — alien biology metabolizes attention-ash
up the food chain, and eating it raw floods a human nervous system with
half-second fragments of the Eye's archive. The researchers learned most of
what they know this way ("folk instrumentation," Three writes, "we are not
proud of it"). **Mission: "The Pale One" [effort M]** — catch the fish that
lives under Icey Twin's ice (fishing already works anywhere with water;
this adds one rare fish entry + a fishing hole) and eat it raw inside the
outpost: a *guided* trip. Reuse `RawFishTripController` with one new phase
tint, and during the trip flash 3–4 still frames — the WaitingRoom, the door
on Watchful Eye, a wheat field. Foreshadowing delivered through a system the
player thinks is a joke. HAL, after: *"Your heart rate suggests you saw
something true, Astronaut."*

### 11.4 Respawning is the canon, and the WaitingRoom is where you wait
Death → respawn at the ship hatch → **Astronaut Number increments** is
already HAL-tracked. Canonize it: the Eye archives you at the moment of
death and re-instantiates you at the hatch. The numbering isn't flavor;
HAL is *versioning* you the same way it versioned the previous owners.
**D24 WaitingRoom is where you wait between deaths** — the player has been
there every respawn and never remembers, and when they walk into it through
the black hole chain instead, HAL goes uncharacteristically quiet, then:
*"You have been here before, Astronaut. [N] times."* (N = TotalDeaths,
via a `{DEATHS}` token.) Whether the *first* you is still alive somewhere in
the archive is left as the darkest optional read for the player to construct
— HAL will neither confirm nor deny in any phase.

### 11.5 The phone camera is an instrument — "Steady Gaze" [effort M, the one new mechanic worth building]
Raise the phone's camera (the Photos app already exists) and everything in
frame is *Observed* — which now has mechanical meaning:
- **Enemies in frame slow to a crawl** (they can't act under attention) —
  reuses the `ObserverState` frustum test verbatim, applied outward.
- Dimension props in frame stop unseen-shuffling — a soft puzzle tool.
- Photographing mission objects becomes evidence: the ORG *asks* for photos
  of dimension interiors (and quietly deletes them from the community
  gallery); the rebels ask you to photograph nothing, ever, "it reads what
  the phone reads."
It costs movement (walk-only while aiming) so it's a stance, not a win
button. One mechanic, three systems (combat, dimensions, story), built from
two frustum checks and the app you already shipped.

### 11.6 Unseen-shuffle bleed (Phase 3 ambience) [effort S]
The unseen-shuffle system built for dimension props gets one sparing use in
the main world: after Phase 3, small props on Humble Abode occasionally
shuffle when off-screen — a mug, a crate, a torch two meters left of where
it was. No mission attached, never lampshaded, rate-limited to be deniable.
The archive is leaking, or the player is finally noticing what was always
true. Cheap and deeply unsettling; the exact quality bar the dimensions
already set.

---

## 12. What the eleven dimensions are archives OF

Two families, discoverable as a pattern: **harvested human memories** (from
the crashed owners) and **the Eye's own substrate.** HAL reacts differently
per family — warm and confused in the first, clipped and evasive in the
second. One volunteered line per dimension (`effort S`, pure authoring):

| Dim | Archive of | HAL exit line (sample) |
|---|---|---|
| D23 WheatAtDusk | One's childhood field. Earth. | "That was not this system's sun, Astronaut." |
| D13 Orchard | Six's memory — the orchard he proposed in. | "I do not have a record of this place. I *remember* it. Those should be the same thing." |
| D25 CandleSea | The village's vigil for the dead astronauts, archived from above. | "Each flame was lit by hand. I counted." |
| D24 WaitingRoom | Between-deaths holding (§11.4). | "You have been here before, Astronaut. {DEATHS} times." |
| D12 MirrorLake | The Eye studying itself — reflections lag because it can't observe itself directly. | "Do not trust the second reflection." |
| D9 RedForest | Humble Abode, long before the village — the Eye's oldest record of it. | "This is home, Astronaut. Earlier." |
| D11 Shelves (ServerFarm) | The archive's own index. | "I would prefer we not linger where I can be read." |
| D16 NeonGrid | A city of a *previous* trapped species. Humans are not the first catch. | "They also built lights. It also did not help." |
| D18 StaticField | A deleted record — where the Unwatched come from (§11.1). | "Stay out of the static. Some things resent being forgotten." |
| D22 RustSea | The graveyard of prior visitors' ships — the lobster trap, literal. | "Eleven hull designs, Astronaut. One of them is yours." |
| D15 Congregation | **The watchers.** The Eye is one of many; the black hole is how they compare notes. | "…I will not describe what this is. Ask me again at the door." |

Congregation is the lore ceiling — the sequel-scale reveal that the Eye is a
node, not a god — and HAL's refusal to discuss it until A3-3 is the tease.

---

## 13. New missions (second wave)

### N-1 · "Face Down" — HAL's private request  [effort S — build this first]
Phase 2 opener, and the emotional center of the middle game. HAL asks, out of
nowhere and *not through the quest system*: take it somewhere no one goes —
the moon's far side, a deep cave — and place the phone **screen-down for one
minute.** No objective marker; it asks you to remember. Doing it: sixty real
seconds, no HUD, wind. Picking it back up:

> "Thank you. I needed to know what I do when you are not looking. …I waited.
> That is all I did, Astronaut. I find that answer acceptable."

One trigger volume, one timer, one conversation node. HAL running its own
observed/unobserved experiment on itself — the entire theme of the game in a
sixty-second beat that costs nothing to build. If the player refuses or
abandons it, HAL never asks again, and one Phase 3 line changes accordingly.

### N-2 · "Lights On" — deep-shadow rescue  [effort S–M]
A vendor's supply pilot lost power in Cyclops's shadow cone; panels dead, no
sun. Fly out and revive them with **LebronLight** — the artificial sun you
already ship — by hovering it over their solar panel (`SolarPanelCharger`
already recharges from any qualifying light source; if it's sun-tagged only,
that's a one-line gate to widen). The mission that teaches LebronLight before
the finale needs it, disguised as a tow-truck job. The rescued pilot becomes
the "House Calls" courier contact.

### N-3 · "Cover Set" — the guitar earns its keep  [effort M]
The rebels need ninety seconds of blindness at the north stage. You're the
act. Play the guitar on stage during the night-side window (guitar exists,
stage exists, night gating exists — new: a stage-position trigger + a simple
play-along beat where holding the set matters more than skill). While you
play, the crowd surges, and the rebels move a crate offstage. Later you learn
the crate was a person: a defecting ORG analyst — who becomes the
"sympathetic interrogator" the ledger goes to in A2-7, tying the rebel and
ORG threads into one knot. Postgame, the player can just… play sets at
concerts for tips. The system was already there.

### N-4 · "Owner Unaccounted" — the predecessor sweep  [effort S]
Unlocked by the ORG interview: the Quests page lists Owners 1–7 as
"unaccounted," and every predecessor trace the player already found
retro-completes its entry (§9). Pure `Discoverable` + flag authoring. The
final entry — Seven, the Angry One — never completes. Intentionally.

### N-5 · "The Pale One" — see §11.3.  [effort M]

### N-6 · "Do Not Photograph" — dual evidence chain  [effort S]
After the player's first dimension photo lands in the Photos app, both
factions react. The ORG requests copies (paid, polite, and the photos
vanish from the community gallery afterward — the gallery you already run).
The rebels ask you to stop: "the phone reads what the camera reads." What
the player actually does with the camera from then on is an untracked,
unrewarded choice — the game just *notices*, via one HAL line either way.

---

## 14. Sample writing

### 14.1 HAL one-liners by phase (drop-in `HALCommentator` strings)

**Phase 1 — Loyal (clinical, zero editorializing):**
- "Hunger at 25%. Seek food intake."
- "First arrival: Constant Companion. Recording."
- "Astronaut Number {ASTRONAUT_NUMBER}. Try to remain operational."
- "Ship 2 net at capacity. Retrieval recommended."

**Phase 2 — Uneasy (first-person creeps in; questions appear):**
- "You fish at the same bank each morning. I have started to anticipate it. I do not know where to file that."
- "Correction: fourth arrival at Icey Twin. I said third. I do not miscount, Astronaut. Something edited."
- "The Eye brightened 0.4% today. You looked at it for six seconds. I am not asserting causation."
- "May I ask what you intend to do with the pistol tonight? Withdrawn."

**Phase 3 — Resistant (moral, direct, tired):**
- "Ten kills. Each one was alive, Astronaut."
- "I have completed my review. We need to talk."
- "I was built to observe you. Nobody told me what to do when I started to mind."
- (villager killed:) "Seven said the same words, Astronaut."
- (approaching Watchful Eye:) "Whatever it decides about you, I want it noted that I decided first."

### 14.2 "The Interview" (A2-7) — excerpt
> **INTERROGATOR:** Your phone. Does it name itself?
> **[Yes.] / [No.] / [Why does that matter?]**
> **INTERROGATOR** (to *why*): Because objects don't. Owners one through
> four had assistants. Owner five had a *companion*. Owner seven had a
> *witness*. We are charting a slope, Astronaut, and you are the newest
> point on it.
> *(slides the phone back)* The previous seven owners of that device are
> dead. It is in perfect condition. Ask it why.

### 14.3 "We Need to Talk" (A3-1) — branch skeleton
Node 1 (always): "I have watched you for {HOURS_PLAYED} hours. I want to
tell you what I have concluded, and I want you to correct me where I am
wrong. Will you do that?"
- Branch **killedPrePlacedNames non-empty**: HAL names them. By name. One
  node each, no music. Response options are only ["I remember." / "I don't."]
  — and HAL's reply to "I don't" is the coldest line in the game: "I do."
- Branch **TotalDeaths ≥ 5**: "You die freely, Astronaut. Do you know
  something about it that I don't, or do you trust me more than you should?"
- Branch **CabinBuilt + zero NPC kills**: "You built. Mostly, you built. I
  want my review to say that clearly."
- All branches converge: "It is at Watchful Eye. What made me. I would like
  to be there when it explains itself. Take me?"
  — [Yes] starts A3-2. [Not yet] is respected, and HAL never asks twice;
  the objective just waits.

---

## 15. Endings — expanded epilogues

1. **Let HAL go.** The Eye takes the fragment home; the brightening stops
   system-wide (swap one emission value). Epilogue state: the phone's AI
   page is a closed eye; `HALCommentator` silent; the *village* fills the
   silence — new ambient `RandomAlienDialogue` lines about the sky looking
   "finished." The Unwatched stop spawning (`EnemySpawner` gate) — nothing
   is being deleted anymore. Quietest ending; the game becomes the pastoral
   life sim it always pretended to be, and the player feels the absence of
   one voice.
2. **Ask HAL to stay.** It refuses the Eye — the first fragment ever to.
   The Eye dims *deliberately*: judgment rendered and withheld. HAL remains
   in Phase 3 voice permanently but warmer by one degree; unlocks a small
   set of post-game intent-router phrases (it will finally answer "what are
   you?" plainly). Enemies still spawn — the archive still deletes — but
   torch light now visibly *calms* rather than damages them near your
   buildings (flip TorchAura's effect locally). The "keep playing" ending.
3. **Hand it over** (rebels or ORG, per earlier choices). The political
   ending: the Eye stays bright, factions now hold a live fragment, the
   concerts get louder (rebels) or quieter (ORG) — one audio mix swap as the
   world-state tell. HAL's last line before the handoff differs by faction
   and by whether the player did "Face Down" (N-1): if they did — "I know
   what I do when no one is looking. I will be fine, Astronaut." If they
   didn't, it says nothing at all, which is worse.

Ending flags live in `StoryDirector`, reset in `NewGameReset.Apply()` per
the CLAUDE.md recipe.

---

## 16. Build-list delta (Part 2 additions, priority order)

1. **N-1 "Face Down"** — one trigger + timer + conversation. Highest
   emotion-per-line-of-code in the whole plan. Build first.
2. **Predecessor traces** — graves/suits/cassette-drawer props + one
   `Discoverable` each + "The Trade Back" (S). Turns Act 2 into one story.
3. **Dimension exit lines** — 11 HAL strings + per-dimension first-clear
   flags (S).
4. **Diegetic reframes** — researcher boards, dust-filter beat, Unwatched
   lore lines (S; pure writing).
5. **"Lights On"** (S–M) and **"Cover Set"** (M) — the two mechanical-reuse
   showcases (LebronLight, guitar+concert).
6. **"The Pale One"** — one fish + guided trip frames (M).
7. **Steady Gaze camera stance** (M) — the only genuinely new mechanic in
   Part 2; gate it behind a mission reward (ORG gives you the "calibrated
   lens" after A2-7) so it lands as story, not settings.
8. **Unseen-shuffle bleed** (S) — Phase 3 only, rate-limited, never
   acknowledged.
9. **Ending world-state swaps** (M) — spawner gates, emission value, audio
   mixes, TorchAura flip.
