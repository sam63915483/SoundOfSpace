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
