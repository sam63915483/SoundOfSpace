# Project Review — "Sound of Space" — 2026-06-05

*A comprehensive snapshot of the project for an outside reviewer. Weighted toward
the last ~2 weeks of work (dated), with a full system inventory underneath so the
recent work has context. Written to be read cold by someone who has never seen the
codebase.*

---

## 0. What this is, in one breath

A single-player **third-person space-survival sandbox** built in **Unity 2022.3
(Built-in Render Pipeline — not URP)**. You start crash-landed on a home planet,
learn to survive (eat, drink, breathe, fish, build), pilot a small ship between
**procedurally generated planets orbiting each other under real N-body gravity**,
fight enemies, run concerts, and are accompanied by a **locally-run LLM "phone AI"
companion** (HAL-style, runs offline via llama.cpp). The current creative push is a
**free, Subnautica-style open opening** layered over a **reusable story backbone**,
with the phone AI as the narrative voice.

Everything iterates **inside the Unity Editor** — there is no CLI build/test step.
Scripts compile on save; verification is by playtesting. One developer.

---

## 1. How to read this document

- **§2 — Recent work (the focus).** Everything substantial from ~May 31 to June 3,
  dated, newest first. This is where most of the energy has gone.
- **§3 — Established systems.** The full game, system by system, condensed. This is
  the "what already exists" map so the recent work isn't floating in a vacuum.
- **§4 — Architecture, conventions, and the fragile bits.** The hard-won rules and
  traps that shape how code gets written here. Important for judging *why* the code
  looks the way it does.
- **§5 — Known debt & open questions.** Honest list of what's unfinished, unsaved,
  or scrap.
- **§6 — What we'd most like feedback on.**

**Repo facts:** Git branch at time of writing is `feat/oxygen-atmosphere-system`
(the oxygen system below). `master` is the integration branch. All 55 commits in the
current visible window are from **June 2026** — this has been an intense, recent
sprint.

---

## 2. Recent work — the last two weeks (dated, newest first)

### 2.1 Oxygen / atmosphere survival system — **June 3, 2026** *(current branch, needs a hands-on playtest)*

The headline recent feature. Adds a real **two-pool air survival model** to make space
travel consequential:

- **Two air pools, measured in seconds-of-air:** the **suit** (120 s) keeps the
  player alive; the **ship hull** (300 s) is a buffer. While inside the ship with hull
  air remaining, the suit is sustained off the hull — so **the hull always depletes
  before the suit**. One line of logic (`player_breathing = in_refill_zone || (inside_ship && hull_o2 > 0)`)
  produces that whole behavior.
- **Altitude-gated breathing.** Air exists only in the **lower half of Humble Abode's
  atmosphere** and **all of Cyclops** (the far planet). Above the atmosphere midpoint,
  and everywhere off-world that isn't Cyclops, is vacuum. Crucially, **altitude is read
  from `NBodySimulation.Bodies` (gameplay physics state), NOT from the forbidden
  atmosphere shaders** — staying well clear of the fragile generation code.
- **Open-hatch depressurization.** A closed hatch seals the hull indefinitely (the
  "do it right" path). An open hatch in flight bleeds the hull out — slow (~60 s) just
  above the midpoint, near-instant (~5 s) high up / in space.
- **Suction-eject beat.** If a *standing* (non-piloting) player has the hatch open in
  flight with hull air left, escaping pressure **drags them to the hatch and ejects
  them** into space onto suit-only air. The intended lesson: seal your hull before launch
  or lose your 5-minute buffer.
- **Built to the project's conventions:** new `OxygenManager` (core sim) + `OxygenHUD`
  (code-built suit + hull meters), **seeded in `EnsureGameplaySingletons` to survive the
  MainMenu→build singleton trap** (June 3), **persisted in the save system** (suit/hull
  O2 + Cyclops-as-checkpoint, reset on New Game, June 3), and **hull VO lines** ("Re-
  oxygenating the hull" / "Hull is ajar") generated in the HAL "George" British voice and
  registered in the voice manifest (June 3).
- Same day: tuning + fixes — atmosphere heights matched to real body radii, ship-refind
  throttled, **player now ejects out the *back* of the ship** rather than mid-ship, and
  **piloting no longer auto-toggles the hatch**.

Spec, design, and implementation plan committed alongside the code (`docs/oxygen_atmosphere_system.md`,
`docs/superpowers/specs/2026-06-03-…`, `docs/superpowers/plans/2026-06-03-…`).

> ⚠️ This is the one recent feature whose ship-dependent paths (refill while parked,
> in-flight breach, suction eject) still need a dedicated hands-on play test — they're
> hard to unit-verify because the player's ship doesn't exist in the scene until it's
> bought (see §4).

### 2.2 Combat blood VFX overhaul — **June 2, 2026** *(~30 commits — the biggest single thread)*

A deep, iterative polish pass on enemy combat feedback, built around the **Piloto Studio
Blood VFX Essentials** pack (committed June 2):

- **Animation-following hit colliders.** Per-bone capsule colliders sized to measured limb
  thickness, so shots actually connect with limbs instead of clipping/missing. Added a
  Scene-view gizmo for the hitbox capsules and a radius tuning knob.
- **Blood fountains that read correctly in-game.** A long sequence of fixes: spray aimed
  along +Y so it spurts *out* of the wound, forced to Local simulation space, attached to
  the nearest bone so blood rides the ragdoll, `Inherit Velocity` disabled so droplets stop
  smearing. A subtle one (captured in project memory): **world-space gravity on droplet
  sub-systems pulled blood off the cone axis when a wound faced sideways** — fixed with
  local −Y force + a root rotation pin.
- **Living wounds.** Wounds keep weeping after the initial gush (cone stops, droplets
  trickle); irregular "blood beats" pulse the spray (rest shrunk, spurt up, swinging
  10%↔100%); the whole fountain shrinks away over ~15 s.
- **Ragdoll death that behaves.** Corpses ragdoll for ≥10 s, then freeze to the planet
  once nearly stopped — and **keep tracking the planet's orbit so they don't fly off**
  (a floating-origin / N-body interaction bug), with the despawn-fling killed.
- **A nasty oversized-splash bug (June 3 fix):** the center splash randomly rendered ~4×
  too big. Root cause (in memory): a "find nearest descendant" helper attached blood to a
  0.01-scale HealthBar canvas whose `Hierarchy` scaling mode inflated it. Fixed by skipping
  RectTransform / tiny / non-uniform-scale nodes.

Spec + plan: `docs/superpowers/specs/2026-06-02-blood-combat-vfx-design.md`,
`docs/superpowers/plans/2026-06-02-blood-combat-vfx.md`.

### 2.3 Vertical-slice opening + story backbone — **June 1–2, 2026**

The overarching creative arc the recent features feed into: a **free, exploratory
Subnautica-style opening** plus an **authored phone-AI narrative on a reusable story
backbone**. New code lives in `Assets/3 - Scripts/Story/`; the slice's scope ends at a
"village reached" seam. June 2 hardened the backbone and finished optional-tutorial
integration. Spec + a large (79 KB) implementation plan are in `docs/superpowers/`.

### 2.4 Other recent fixes & polish — **June 2–3, 2026**

- **June 3** — Stopped the sun **lens flare bleeding through ship walls** when on foot.
- **June 3** — Killed a real perf bug: **`CompassHUD` was hammering `FindObjectOfType`
  every frame**; also clears a stuck lens flare and chains a queued AI story beat when a
  conversation ends. (Project memory notes this class of bug bit us before — small interior
  scenes ran *worse* than the full solar system because per-frame `FindObjectOfType` was
  searching for an absent N-body sim.)
- **June 2** — **HAL voice bank fully regenerated with a single British voice** ("George",
  ElevenLabs via Coplay).
- **June 2** — Flashlight gains a **quarter-power mode** (4 modes total) + AI tutorial updated.

### 2.5 Just-before-this-window — **May 31 – June 1, 2026**

- **Fall damage** (`FallDamage.cs` on the player): vertical-only impact tiers (18/28/38 m/s).
  Normal jumps land ~14 m/s so the 18 threshold is safe. Spec May 31 / built June 1.
- **Death cutscene system:** death now plays a `DeathCutsceneController` timeline-tree
  cutscene, *then* reloads the newest save (falling back to an in-place respawn if none
  exists). Design May 31.

---

## 3. Established systems (comprehensive inventory)

*Condensed from a full system-by-system audit (May 27). These predate the recent sprint
but define the game the recent work extends.*

### 3.1 Solar system & physics
Eight `CelestialBody` instances (Sun, Fiery Twin, Icey Twin, **Humble Abode** [home —
village, markets, cabins, concert stages], **Constant Companion** [its moon, hosts a moon
base], **Cyclops** [big mid-system planet], Tumbling Bean, Watchful Eye) orbit each other
under `NBodySimulation` at a fixed 100 Hz tick. Player and ship both extend `GravityObject`
and apply gravity manually (Unity gravity is off). Orbital state is deterministic and
save-restored, so day/night and orbital phase resume exactly. **There is no day/night clock
— lighting is emergent from orbital geometry; bodies don't even self-spin in code.**

### 3.2 Player, ship, floating origin
`PlayerController` aligns the player's up-vector to the nearest body's gravity each
FixedUpdate (planet-relative movement). `Ship` (single prefab, **SHIP44**) is the one
pilotable craft; the old prefab-swap damage system was removed in favor of per-part
attachment (`ThrusterDetachOnImpact` → loose `ThrusterPickup` rigidbodies →
reattach via `ShipReassembly`). A rich ship HUD stack exists (boost/g-force/velocity/
crash-warning). **`EndlessManager` floating origin** shifts all registered physics objects
when the camera drifts >1000 units from world origin — every runtime-spawned physics object
*must* register or it desyncs.

### 3.3 Save / load
A `JsonUtility`-based system (`SaveSystem` / `SaveData` / `SaveCollector`) with a **strict,
documented ~17-step apply order** (bodies → tutorial → NPCs → early-game → singletons →
ship → player → buildings → loose parts → enemies → held item → touch-ups). Captures from
**`rb.position`/`rb.rotation`, never `transform`** (transform lags physics and respawns
objects inside colliders). Positions are stored **body-relative** so they survive orbital
motion. Bounded autosave slot (5 min default).

### 3.4 Survival, inventory, fishing
- **Vitals** (`ResourceManager`): hunger, thirst, health, ship power, with death-and-respawn.
  The new oxygen system (§2.1) sits alongside this.
- **Hotbar:** a **7-slot, table-driven inventory** (extend via one registry row, never
  parallel switch cases). Holds equippables (water bottle, fishing rod, guitar, axe, pistol),
  resource stacks (wood, crystal, space dust), per-fish slots, and a fish bag.
- **Fishing loop:** trade a cassette to an NPC for a rod → cast `Bobber` (real buoyancy
  physics) → catch routes into bag/hotbar → log in a lifetime "fishdex" → cook at a bonfire
  or sell at the fish market via a shared drag-and-drop staging picker. Fish can be eaten raw
  for a hunger restore + a psychedelic "trip" effect.

### 3.5 NPCs, story, economy
- ~10 hand-placed NPCs (children of celestial bodies) plus ambient streamed aliens. Shared
  dialogue infra: zero-alloc typewriter (`DialogueTextStyling`), conversation tracker,
  geometric wave animation enforced in `LateUpdate`.
- **Tev's quest arc** (`EarlyGameProgress`, an 8-phase flag-driven early-game story:
  read note → fish → eat → drink → return home → build cabin → get village coords → visit
  vendors). A story placeholder flag `ORG_Reveal` (F9 in dev) gates the AI's story-aware
  knowledge.
- **Vendors/economy:** goods merchant (pistol/axe/jetpack/rod/bottle/fish-bag), ship market
  (whole ships + individual parts), guitar shop, fish market, and a **space-dust economy**
  (orbital "space nets" buffer dust while a ship is parked in orbit; drain for cash).

### 3.6 Building, combat, concert
- **Building** (key N): ghost-placement preview, placed instances named `<prefab>_Placed`
  and parented to a planet (that suffix is how the save system restores them). A lock system
  gates which blueprints appear, unlocked progressively by the story.
- **Combat:** `EnemyController` + spawner + health bars; axe (melee) and pistol (hitscan)
  damage; knockback, ranged "spit" attacks, runtime ragdolls, a **killstreak system** with
  slow-mo on kill, and "torch aura" no-spawn/damage zones. Enemies/spawn-timing are saved.
- **Concert system:** discovers speakers, binds each to a planet, gates stages to the
  night side via orbital geometry, auto-clones audience zones, drives lasers/blinders/haze/
  strobes/cone-beams with custom shaders, and spawns killable audience members.

### 3.7 Map, world streaming, AI companion, camera FX
- **Map** (M): top-down system map built from live body data; **compass** is a Skyrim-style
  waypoint strip.
- **World streaming:** deterministic per-cell spawners for trees (chop → wood), mushrooms
  (eat → trip), and ambient aliens, each with a live cap slider. A crystal harvesting +
  cubeface spawn system was in progress on a recent branch.
- **Phone AI companion:** a **local llama.cpp backend** (LlamaLib, ~4 GB, gitignored).
  Default is **CPU + Qwen-2.5-3B**; flip one bool for **GPU + Hermes-3-8B (Vulkan)**.
  Includes a chat UI inside an in-world phone, a long-term memory store with LLM-based
  compaction, a hot-reloadable game-knowledge file (with a story-gated extension), and a
  "fleet telemetry" block that feeds live ship state into the system prompt. A HAL-style
  commentary layer (`HALCommentator`) rides on top.
- **Camera FX:** a large, fully-toggleable procedural stack (headbob, FOV, damage vignette/
  flash/shake, letterbox, speed lines, film grain, chromatic aberration, lens dirt, color
  grade, anamorphic streaks, radial motion blur, trauma shake), all menu-tunable.

### 3.8 Tutorials & cutscenes
A main input tutorial (advance with Tab) plus separate "bonus" tutorials (axe/building,
fishing) that pause the main one. Cutscene/flashback scenes are disabled in build settings
but **hard-referenced by code by name — must not be deleted or renamed.**

---

## 4. Architecture, conventions & the fragile bits

*These are the rules that shape the code. A reviewer should weigh the code against these,
because several were learned by breaking the build.*

- **Built-in Render Pipeline, not URP.** URP-authored asset packs render magenta; their
  materials get converted to Standard. Never install URP.
- **The MainMenu singleton trap.** Builds boot in `MainMenu.unity`; the Editor usually
  presses Play directly in the gameplay scene. `RuntimeInitializeOnLoadMethod(AfterSceneLoad)`
  fires once after the *first* scene — which in a build is MainMenu. So any auto-singleton
  that early-returns on MainMenu **also has to be seeded in
  `MainMenuController.EnsureGameplaySingletons()`**, or it silently never exists in builds.
  (The oxygen managers were seeded for exactly this reason.)
- **A forbidden zone: procedural planet generation + atmosphere shaders.** A past session
  broke a working build editing here (killed the planet's directional light, removed the top
  grass layer — unrecoverable). The whole `Celestial/` generation pipeline, the atmosphere/
  ocean image effects, and their shaders/materials are **read-only**. The recent oxygen work
  deliberately reads altitude from physics state to avoid touching it. (A related fixed trap:
  atmosphere vanishing after a warm scene reload, solved by a reflection-based flag reset
  *outside* the forbidden zone.)
- **The save apply-order is strict** and capturing `transform` instead of `rb` causes objects
  to respawn inside colliders.
- **Performance hygiene is enforced by hard rules** because they've each caused real bugs:
  never call `FindObjectOfType` / `Camera.main` / `GameObject.Find` in Update loops (cache,
  or throttle refinds for "may never appear" targets — e.g. the player's ship, which doesn't
  exist until bought); iterate live instances via static `AllInstances` lists; zero-alloc
  typewriter; rigidbodies written only via `MovePosition`/`MoveRotation` in FixedUpdate.
- **Serialized fields are only ever appended to the end of a MonoBehaviour** — reordering
  corrupts existing scene/prefab serialization.
- **No `.asmdef` files** (everything is `Assembly-CSharp`, so moving scripts never breaks
  compilation) and **no CLI build/test** — all verification is Editor playtesting.
- **Extension is recipe-driven**: documented patterns exist for adding an equippable, an
  auto-singleton, a save field, a placed building, a vendor item, or a camera effect — the
  expectation is to mirror the canonical example, not invent a new shape.

---

## 5. Known debt & open questions

- **Oxygen ship-paths need a real playtest** (§2.1) — the ship doesn't exist in the scene
  until purchased, so the refill/breach/suction paths can't be trusted until played.
- **Deliberately not saved:** transient combat state, ragdolls, tree-chop state (wood count
  *is* saved), killstreak, concert state (rebuilds on load), and a save taken mid-piloting
  puts the player at the spawn point on load.
- **Scrap candidates** identified in the audit: an obsolete 1.5B model `.meta` stub, a
  temporary `JitterDiagnostic.cs` (self-disabled), ~400 MB of archived old-version scenes,
  a 73 MB `transfer/` dumping ground (but it holds some *in-use* SFX — grep GUIDs before
  deleting), and a bundled Triangle.NET geometry lib whose dev-only visualizer may be dead.
- **Big binaries are gitignored and re-importable** (the ~4 GB LlamaLib, the GGUF models,
  the Backrooms/Poolrooms packs).

---

## 6. What we'd most like an outside opinion on

1. **Is the oxygen model's "hull drains before suit" mechanic legible to a player**, or
   will the altitude-midpoint refill cutoff read as a bug without a strong visual telegraph?
2. **Scope of the vertical-slice opening** — is a free Subnautica-style start + authored
   phone-AI narrative the right hook, and is the "village reached" seam a sensible first
   milestone?
3. **The local-LLM companion as a core pillar** — is shipping a ~4 GB offline model
   defensible, or is that a liability vs. the gameplay payoff?
4. **General architecture health** given the constraints in §4 — anything that looks like
   it's accruing dangerous debt, especially around the save system, floating origin, and the
   forbidden-zone boundary.

---

*Generated 2026-06-05. System inventory cross-referenced against the May 27 full audit
(`docs/CURRENT_STATE_AUDIT.md`); recent work dated from git history and the spec/plan docs
under `docs/superpowers/`.*
