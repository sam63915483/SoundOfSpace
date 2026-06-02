# Phone AI Revamp — Design Spec

**Date:** 2026-05-22
**Status:** Approved for implementation planning
**Scope:** Five cohesive subsystems of the in-game phone AI

---

## 1. Goals

Make the phone AI feel like a genuine companion that knows the game and pulls its weight:

1. **Knowledge audit + expansion.** The AI should know how the game's systems actually work — fishing, cooking, raw-fish trip side-effects, water-bottle refill, building, flashlight 3-state, controls, jetpack, concerts (and the rumor they're rebel hotspots), equippables and where to buy them, planets and the aliens that inhabit them, and the space-dust / space-net loop end to end.
2. **Ship-aware AI.** The AI can be asked to mark, show, or query any ship the player owns (scene ship or `BoughtShip` from the vendor / debug menu). Each ship is queryable by its `BoughtShip.shipNumber` ("Ship 2", "Ship 0" = scene ship). Queries cover orbit status, current planet, speed, and per-net dust totals. **Hard gate:** if the ship has no satellite dish (`dishAttached == false` on its `ThrusterDetachOnImpact`), the AI must refuse all requests for that ship with "Ship N is offline. I cannot reach it."
3. **Atmosphere lines feel grounded.** The volunteered atmosphere transition includes the planet name ("Entering Humble Abode atmosphere, Astronaut.") instead of generic text.
4. **HAL volunteered lines are useful, not filler.** Delete the ambient idle pool entirely. HAL only speaks on substantive game-state changes (vitals thresholds, ship dust accumulation, orbit stabilization, concert activation, plus all existing event triggers).
5. **Game overview docs.** Refresh `docs/GAME_OVERVIEW.md` to a current short / medium / long write-up reflecting every system, NPC, loop, tutorial, and save-tracked field as of 2026-05-22.

## 2. Non-Goals

- No save-schema changes. HAL threshold state and AI session state already don't persist; the player re-triggers as they re-cross thresholds, which is the desired behavior.
- No new equippable, no new vendor, no new planet. Purely an AI / knowledge / overview revision.
- No changes to the LLM model, sampling parameters, system-prompt chain-of-thought protocol, tool-call grammar, or `LLMUnity` runtime. Existing infrastructure stays.
- No changes to the forbidden zones from CLAUDE.md (atmosphere shader, planet generation, etc.).
- `docs/PITCH.md` and `docs/PHONE_AI_OVERVIEW.md` stay as-is unless the user later asks for refresh.

## 3. Architecture Overview

The phone AI today is structured as:

```
LLMService (singleton)
  ├─ owns LLM + LLMAgent (Hermes-3-Llama-3.1-8B)
  ├─ BuildSystemPrompt(userMessage) — assembles per-turn prompt from:
  │     • Chain-of-thought instructions
  │     • Tool-call documentation
  │     • Persona (phase-shaded)
  │     • Live telemetry (player vitals, money, wood, story flags)
  │     • Core canon entries
  │     • Retrieved grounding entries (keyword match)
  │     • Memories + standing
  └─ Streaming chat → strips <think> → strips/queues [verb:arg] tool calls

HALToolDispatcher (static)
  ├─ Verbs: waypoint, unwaypoint, map
  └─ Resolves natural names → scene Transforms / CelestialBody

HALCommentator (singleton)
  ├─ Volunteers HAL lines on events: death, killstreak, phase, planet visit,
  │  atmosphere, orbit-match voice, enemy proximity, EarlyGame flag triggers
  └─ Ambient idle pool (~30 stock lines, three phase moods) — to be removed

GameKnowledgeBase (singleton)
  └─ Parses StreamingAssets/AI/game_knowledge.md into PERSONA / ENTRY blocks
     (Core, Grounding, Verbatim modes)
```

This revamp:

- Adds **`FleetTelemetry`** as a new helper alongside `LLMService.BuildLiveTelemetry`. Output gets injected into every system prompt.
- Adds **two new tool verbs** (`markship`, `showship`) to `HALToolDispatcher`, with a defense-in-depth dish gate. `showship` calls the **existing** `SolarSystemMapController.FocusOnShip(Ship)` — no map-controller changes required.
- Edits **`HALCommentator`**: planet-named atmosphere lines, deletes ambient pool, adds substantive vitals / ship-dust / orbit-stabilized / concert-active triggers.
- Edits **`game_knowledge.md`**: audit pass, six new ENTRY blocks, planet-lore expansion.
- Rewrites **`docs/GAME_OVERVIEW.md`** in place.

No new singletons. No new save fields. No new dependencies.

## 4. Detailed Design

### 4.1 FleetTelemetry (new file)

`Assets/3 - Scripts/AI/FleetTelemetry.cs` — static class, one public method:

```csharp
public static string BuildBlock()
```

Enumerates the scene ship and every active `BoughtShip`. Scene ship resolution: `FindObjectsOfType<Ship>()` filtered to those whose GameObject does NOT have a `BoughtShip` component. (`Ship.PilotedInstance` is only non-null while piloted, so we can't rely on it.) Scene ship is presented as `Ship 0`. `BoughtShip` instances enumerated via `FindObjectsOfType<BoughtShip>()`, presented as `Ship N` where `N = bs.shipNumber`. Result sorted by displayed ship number ascending.

For each ship, reads `ThrusterDetachOnImpact` (the component that owns attachment state — `leftAttached`, `rightAttached`, `dishAttached`, `solarAttached`) and the ship's `Rigidbody` for velocity. Reads `SpaceNet[]` children for dust buffers.

Output format (one line per ship, plus a header):

```
FLEET STATE (live ship data — refer to this, do not contradict it):
  Ship 0: Humble Abode orbit, 1.42 km/s, dust [net1=120, net2=80], dish OK, solar OK, thrusters L/R, hatch closed
  Ship 1: Humble Abode surface, idle, dust [—], dish OK, solar OK, thrusters L/R, hatch open
  Ship 2: OFFLINE (no satellite dish — no telemetry available)
  Ship 3: deep space drifting, 0.8 km/s, dust [net1=full], dish OK, solar OK, thrusters L only, hatch closed
```

Determination rules:
- **Planet:** nearest `CelestialBody` per `NBodySimulation.Bodies`, body-radius proximity test (`<= 5 × body.radius`). Beyond that → "deep space".
- **Orbit / surface / drifting:**
  - Distance to planet `<= 1.05 × body.radius` AND ship's `Rigidbody.velocity.magnitude < 5 m/s` → `surface, idle`
  - In planet proximity AND any tangential velocity → `<planet> orbit, <speed> km/s`
  - Not in any planet proximity → `deep space drifting, <speed> km/s` (or `at rest` if `< 5 m/s`)
- **Speed:** `rb.velocity.magnitude / 1000` formatted to 2 decimals.
- **Dust:** iterate `SpaceNet` siblings under each ship, emit `net<index>=<buffer>` or `net<index>=full` (when `buffer >= bufferCapacity`). If no nets attached → `dust [—]`.
- **Offline:** if `dishAttached == false`, emit ONLY the OFFLINE line and skip all telemetry. This is the model's signal to refuse queries about that ship.

Total token budget: ~30-50 tokens per online ship, ~10 tokens per offline ship. With a typical fleet of 1-4 ships, this is ~50-200 tokens added per turn, well within headroom.

### 4.2 LLMService.BuildSystemPrompt edits

Single new call after the existing `BuildLiveTelemetry()` line:

```csharp
sb.Append(BuildLiveTelemetry()).Append('\n');
sb.Append(FleetTelemetry.BuildBlock()).Append('\n');   // NEW
```

Plus two new lines in the existing TOOLS section documenting the new verbs:

```
  [markship:N]   Drops a compass waypoint on Ship N (e.g. [markship:2]).
                 N=0 is the Astronaut's original ship. Requires Ship N to
                 have a satellite dish — otherwise it is OFFLINE in the
                 FLEET STATE and you must refuse the request in text
                 ("Ship N is offline. I cannot reach it.").
  [showship:N]   Opens the map and focuses the camera on Ship N. Same
                 dish gate.
```

Plus one new Core canon entry (in `game_knowledge.md`, surfaced into the system prompt via the existing Core mode pipeline) instructing the model how offline ships work — see §4.6.

### 4.3 HALToolDispatcher edits

Two new cases in `Execute(verb, arg)`:

```csharp
case "markship": HandleMarkShip(arg); break;
case "showship": HandleShowShip(arg); break;
```

Both implementations:

```csharp
static void HandleMarkShip(string arg) {
    if (!TryResolveShip(arg, out var shipGO, out int n)) return;
    if (!IsShipOnline(shipGO)) {
        Debug.LogWarning($"[HALToolDispatcher] markship:{n} — ship is offline (no dish). Refusing.");
        return;
    }
    // Same shape as HandleWaypoint, key = "ship" + n, label = $"Ship {n}"
    string key = "ship" + n;
    string id  = "hal_" + key;
    string label = $"Ship {n}";
    Transform t = shipGO.transform;
    RemoveTrackedWaypoint(key);
    CompassHUD.Instance.AddWaypoint(id, () => t != null ? t.position : Vector3.zero,
                                    label, null, HALVisuals.EyeRed);
    _activeWaypoints.Add(new TrackedWaypoint { Id=id, Target=t, Label=label, Key=key });
}

static void HandleShowShip(string arg) {
    if (!TryResolveShip(arg, out var shipGO, out int n)) return;
    if (!IsShipOnline(shipGO)) return;
    if (SolarSystemMapController.Instance == null) return;
    var ship = shipGO.GetComponent<Ship>();
    if (ship == null) return;
    SolarSystemMapController.Instance.OpenMap();
    SolarSystemMapController.Instance.FocusOnShip(ship);  // existing method
}
```

Helpers:
- **`TryResolveShip(string arg, out GameObject ship, out int number)`** — parses the integer from `arg` (handles "2", "ship 2", " 2 "). Looks up `BoughtShip` with matching `shipNumber`, or the scene ship for `0`. Returns false (silent failure with `Debug.LogWarning`) if no match.
- **`IsShipOnline(GameObject ship)`** — finds the `ThrusterDetachOnImpact` on the ship root or children, returns `tdoi.dishAttached`. False if no component found (treat as offline — fail safe).

Defense-in-depth: even if the LLM ignores the prompt instruction and emits `[markship:N]` for an offline ship, the dispatcher refuses. The player's chat will still show whatever text the LLM produced — ideally the refusal, but if the LLM hallucinated success, the player at least doesn't get a misleading waypoint.

### 4.4 SolarSystemMapController — no changes required

`SolarSystemMapController.FocusOnShip(Ship ship)` already exists (line 589 of `SolarSystemMapController.cs`). It handles camera framing, lit-side positioning, view caching, and follow tracking — everything `[showship:N]` needs. The dispatcher just calls it directly. No edits to the map controller.

### 4.5 HALCommentator edits

**Atmosphere with planet name** (`PollAtmosphere`):

```csharp
string planetName = body.bodyName;
Volunteer(inAtmoNow
    ? $"Entering {planetName} atmosphere, Astronaut. Descent in progress."
    : $"Leaving {planetName} atmosphere, Astronaut. Vacuum confirmed.");
```

**Delete:**
- Constants: `AmbientIdleThresholdSeconds`, `AmbientPollIntervalSeconds`, `AmbientFireChance`
- Fields: `_nextAmbientCheck`, `_lastAmbientIdx`
- Methods: `TryAmbientObservation`, `PickAmbientLine`
- Static pools: `AmbientLinesPhase1`, `AmbientLinesPhase2`, `AmbientLinesPhase3`
- Call site in `Update`: `TryAmbientObservation();`

**Add substantive triggers** — all polled on the existing 0.5s `_pollTimer`:

```csharp
void PollVitals() {
    var rm = ResourceManager.Instance; if (rm == null) return;
    CheckThreshold(rm.HungerPercent,    ref _hungerStage,    "Hunger at {0}%. Seek food intake.");
    CheckThreshold(rm.ThirstPercent,    ref _thirstStage,    "Thirst at {0}%. Hydration recommended.");
    CheckHealth   (rm.HealthPercent,    ref _healthStage);
    CheckShipPower(rm.ShipPowerPercent, ref _shipPowerLow);
}
```

Threshold logic per metric (hysteresis prevents flapping):
- Hunger / Thirst stages: `0=above 55, 1=at-or-below 50, 2=at-or-below 25, 3=at-or-below 10`. Fire the matching line on a downward stage transition. Decrement the stage when the value rises 5+ percentage points above the previous threshold's lower edge (so leaving stage 1 needs hunger ≥ 55%, leaving stage 2 needs ≥ 30%, leaving stage 3 needs ≥ 15%). The 5-point hysteresis stops re-fires from one-pixel oscillation around the boundary.
- Health stages: `0=above 55, 1=at-or-below 50, 2=at-or-below 25`. Same pattern.
- Ship power: single threshold at 25% with hysteresis — fires `"Ship power at N%. Solar panel exposure recommended."` once on crossing down; resets when crossing back above 40%.

**Ship dust tracking:**

```csharp
Dictionary<int /*shipNumber*/, int /*highestThresholdFired*/> _shipDustStage = new();

void PollShipDust() {
    // Scene ship (number 0): FindObjectsOfType<Ship>() filtered to no BoughtShip component.
    // Bought ships: FindObjectsOfType<BoughtShip>().
    var ships = EnumerateAllShipsWithNumbers(); // shared helper with FleetTelemetry
    foreach (var (shipGO, shipNumber) in ships) {
        int total = SumNetBuffers(shipGO); // iterate SpaceNet siblings
        int prev  = _shipDustStage.TryGetValue(shipNumber, out var v) ? v : 0;
        int next  = ThresholdStage(total); // 0=under 100, 1=100, 2=250, 3=full(500)
        if (next > prev) {
            string line = next == 3
                ? $"Ship {shipNumber} net is full."
                : $"Ship {shipNumber} has collected {total} dust.";
            Volunteer(line);
        }
        _shipDustStage[shipNumber] = next;
    }
}
```

`EnumerateAllShipsWithNumbers()` is shared between `FleetTelemetry` and `HALCommentator` — single source of truth for "which GameObjects count as ships, and what number does each get." Lives in `FleetTelemetry` as a `public static` helper; `HALCommentator` calls it.

`FindObjectsOfType<>` is acceptable here — runs on the 0.5s poll, not per-frame, and fleets are small. If perf becomes an issue later, retrofit a static `AllInstances` list on `BoughtShip` (the CLAUDE.md pattern from `EnemyController.ActiveEnemies`). Out of scope for this revamp.

**Orbit-stabilized trigger** — extend the existing `PollOrbitMatch` to also volunteer a stable-orbit log line per ship the FIRST time it reaches `IsOrbitMatched == true` and stays true for >= 3 seconds:

```csharp
HashSet<int> _shipOrbitAnnounced = new();
// ... after existing orbit-match voice logic ...
if (shipMatched && ship != null && !_shipOrbitAnnounced.Contains(ship.GetShipNumber())) {
    // Dwell timer: track time-since-matched; require 3s before firing.
}
```

Reset announced set when ship leaves the planet's proximity (so re-orbiting later fires again).

**Concert-active trigger** — `ConcertStageHub` does not currently expose a per-stage activation event (confirmed by grep). Two paths:

- **Preferred:** add `public event Action<SpeakerSource> OnStageActivated;` to `ConcertStageHub` and fire it on the per-stage `inactive → active` transition (where the hub already computes `wasActive != isActive`). Then `HALCommentator` subscribes lazily in `TrySubscribe()` alongside the existing `OnPhaseChanged` subscription. Pattern-consistent with the file.
- **Fallback (if any reason to leave the hub untouched):** poll `ConcertStageHub.Instance` from `HALCommentator` once per second, track per-stage active state, fire the line on the flip. Functional but duplicates state.

Go with the preferred path. Volunteer `"Concert active at {stage.gameObject.name}."` once per activation. Reset on hub deactivation so the next night cycle re-fires.

**Keep all existing triggers** as-is: death, killstreak, phase change, planet visit, atmosphere (now with planet name), orbit-match voice, enemy proximity, all 12 EarlyGameProgress flag lines.

### 4.5.1 Concert waypoint pointer correction

The current "concert" waypoint routes through `ConcertStageHub.FindActiveStageRoot()`, which walks up from the speaker until it hits a `CelestialBody` parent and returns that entire stage hierarchy's root transform. The root's pivot is off-center relative to the stage geometry, so the compass marker reads slightly to the side of the actual stage.

The active scene already has a center-stage speaker GameObject per stage named `speaker.005`, referenced as `s.speaker` (a `SpeakerSource`) inside the hub. Using its transform puts the marker exactly center-stage.

Changes:
- `ConcertStageHub` — add a new public method `Transform FindActiveStageSpeaker()` that mirrors `FindActiveStageRoot` but returns `s.speaker.transform` for the active stage (`null` if none active). One-method addition, no behavior change to existing callers.
- `HALToolDispatcher.ResolveTarget` — for the `"concert"` / `"active concert"` / `"the concert"` / `"show"` / `"stage"` branch, call `FindActiveStageSpeaker()` first; fall back to `FindActiveStageRoot()` if it returns null (defensive — covers any future stage that's wired without a speaker reference).

### 4.6 game_knowledge.md additions

**Audit pass** (light-touch corrections, not rewrites). Verify each existing ENTRY matches code reality. Specific spot-checks:

- `ENTRY: Space Dust` — confirm space-net flow and interact-with-F wording matches current `SpaceNet` / `SpaceNetMountController`.
- `ENTRY: Controls Cheat Sheet` — confirm flashlight is 3-state (E for 50%, E again for full, E again for off), all other key bindings current.
- `ENTRY: How to Build a Cabin` — confirm "open phone, building app, place cabin" matches `PlayerPhoneUI` + `BuildMenuUI` flow.
- `ENTRY: How to Drink` — confirm RMB-hold-underwater refill is current.
- `ENTRY: How to Fish` — confirm rod cast / wait / reel mechanics.
- `ENTRY: Where to Buy Things` — confirm vendor list matches current NPC roster.

**Six new ENTRY blocks:**

```markdown
## ENTRY: Tev Intro
mode: grounding
phase: all
keywords: tev, crash, wake, cabin, story, beginning, start
---
You crash-landed on the planet Humble Abode. Your ship was lost. An alien
named Tev found you and brought you to his cabin. You woke up there. Tev
nursed you back to operational condition and is the closest thing to a
guide you currently have. He will direct you to fish, eat, drink, and
build a shelter of your own before he tells you where the village is.

## ENTRY: Space Nets
mode: grounding
phase: all
keywords: net, nets, space net, space dust, dust, collect, gather, harvest, orbit
---
Space nets attach to a ship and passively buffer space dust while the
ship is parked in orbit (in proximity to a body, but not piloted and not
on the surface). Closer orbits collect faster. Each net caps at 500 dust.
To drain a net, stand inside its trigger collider and press F — the
buffered dust transfers into your inventory. Buy a net from the Ship
Market vendor; a ship can carry up to two (left and right mounts).

## ENTRY: Concerts as Rebel Hotspots
mode: grounding
phase: all
keywords: concert, concerts, rebel, rebels, rebellion, stage, show, music, gig
---
Two concert stages run on opposite poles of Humble Abode, gated by the
day/night cycle — only the night-side stage is active at any moment.
Rumor among the aliens says these gatherings may be cover for rebel
activity. I recommend visiting — both for the visual spectacle and for
the possibility of meeting useful people.

## ENTRY: AI Self-Capabilities
mode: verbatim
phase: all
intent: what can you do, what do you do, help me, how do you work, what are you for, your capabilities, your abilities
---
I can do the following, Astronaut:

• Drop a compass waypoint on a person, vendor, landmark, planet, or
  active concert. Ask "mark Tev" or "where is the ship vendor."
• Open the solar-system map and focus it on a planet. Ask "show me
  Cyclops on the map."
• Mark or show any of your ships on the compass or the map. Ask
  "mark Ship 2" or "show Ship 0 on the map." If a ship has no
  satellite dish, I cannot reach it.
• Answer questions about your fleet: orbit status, current planet,
  speed, dust per net. Only for ships with a working dish.
• Recall what I know about the eight bodies of this system, the
  aliens that inhabit them, the vendors, the equippables, and the
  early-game mission.
• Notice when your vitals get low or your ship nets fill up, and
  tell you about it without being asked.

If you want to know how to do something specific — fishing, cooking,
flight, the flashlight, the building menu — just ask.

## ENTRY: Ship Commands
mode: grounding
phase: all
keywords: ship, ships, mark ship, show ship, fleet, my ship, ship 0, ship 1, ship 2, ship 3
---
Each ship you own has a number. Ship 0 is your original ship. Ship 1, 2,
3… are ones you've bought from the Ship Market or spawned from the debug
menu. You can ask me:
  • "Mark Ship N"  — drops a compass waypoint on that ship.
  • "Show Ship N on the map" — opens the map focused on it.
  • "Is Ship N orbiting?" / "What planet is Ship N around?"
  • "How fast is Ship N going?"
  • "How much dust does Ship N have?"
I can answer telemetry questions only for ships with a working satellite
dish. Without a dish, the ship is offline to me and I cannot reach it —
buy a replacement dish from the Ship Market vendor to restore comms.

## ENTRY: Story Phase Marker  (already exists — no change)
```

**Planet lore expansion** — add 3-4 sentences of placeholder lore to each of the existing planet entries. Mark each addition with `(placeholder — replace with real lore later)` so it's obvious where to swap in the canonical version. Bodies to expand: Fiery Twin, Icey Twin, Cyclops, Tumbling Bean, Watchful Eye, Constant Companion. Sun and Humble Abode already have substantive entries.

Example expansion shape:

```markdown
(existing body) ...

(placeholder — replace with real lore later)
Fiery Twin is the system's inner furnace. The aliens here, when there
are any, do not last long; the planet's surface bakes at a hundred-plus
degrees and the storms can flense a hull. The Accord has not bothered
to maintain a checkpoint here for decades. If anything is still on this
world, it has reason to hide.
```

### 4.7 docs/GAME_OVERVIEW.md rewrite

In-place rewrite, three sections at three lengths:

- **Short** (~150 words, one paragraph) — elevator pitch for someone who's never heard of the game. Crash-landed astronaut on a procedurally-orbiting solar system; cabin, fishing, building, soft combat, space-dust trade, concerts, a mysterious AI in your phone, a multi-phase character arc.
- **Medium** (~800 words, one page) — every system at a one-paragraph level: gravity / floating origin, survival vitals, fishing + cooking + raw-trip, building loop, ship damage + reassembly + multi-ship fleet, combat + ragdolls + killstreak + tree-spit standoff, space-dust + space-nets economy, concert system with night-gating, NPC roster + Tev story arc, save system, phone + phone AI, atmosphere & rendering. Plus story arc summary.
- **Long** (~3000-5000 words, comprehensive technical reference) — every system in detail, every NPC named and described, every loop documented, every tutorial step listed, every save-tracked field enumerated, every auto-singleton named. Audience: a new contributor or LLM that needs to ramp up on the codebase. Effectively a curated précis of CLAUDE.md restructured for narrative readability rather than reference lookup.

Same file (`docs/GAME_OVERVIEW.md`), full rewrite.

## 5. Edge Cases & Risks

- **Ship resolution edge cases.** Player typing "mark ship one" instead of "mark ship 1". `TryResolveShip` should handle: integer parse, "ship N" stripping, "Ship N" case-insensitive, leading/trailing whitespace. "Original ship" / "my ship" → resolve to scene ship (number 0).
- **Multiple ships at the same number** (shouldn't happen given `ShipMarketNPC.NextShipNumber()` logic, but defensive). If two ships share `shipNumber == N`, pick the first found; log warning.
- **Ship destroyed mid-chat.** `BoughtShip` could be destroyed (player blows it up?). `FleetTelemetry` skips null entries. `HALToolDispatcher.markship` checks `shipGO != null` before adding the waypoint; the existing closure-based waypoint provider returns `Vector3.zero` for null transforms which the compass tolerates.
- **Dish detached mid-flight.** Player flies into a meteor, dish falls off → ship goes offline. Next chat turn, FLEET STATE shows offline; existing waypoints remain (we don't retroactively clear them, but the AI will refuse new requests). Acceptable.
- **HAL vitals threshold spam.** Hunger drains, player eats, drains again, eats again — we'd retrigger the 50% line every cycle. Threshold-stage logic with hysteresis (re-fire only after crossing back UP above the next-higher threshold) prevents this — player must cross from "above 50" to "below 50" again before the line re-fires.
- **Ship dust threshold spam.** Player drains a net, it refills past 100 again. Same hysteresis pattern — `_shipDustStage` decrements when buffer drops below the previous threshold.
- **Concert event coupling.** If `ConcertStageHub` doesn't already expose a per-stage activation event, we add one. Risk: subtle bug if existing per-stage state machine breaks. Mitigation: minimal additive change, no behavior change to the hub.
- **CLAUDE.md "MainMenu singleton trap".** `FleetTelemetry` is a static helper, not a singleton — no trap risk. The existing `LLMService`, `GameKnowledgeBase`, `HALCommentator`, `HALToolDispatcher`, `HALVolunteeredLog`, `HALLineHUD`, `HALVoicePlayer` are all already seeded in `MainMenuController.EnsureGameplaySingletons` per CLAUDE.md guidance — no new entries needed.
- **CLAUDE.md "DO NOT TOUCH atmosphere/planet generation".** Atmosphere transition lines live in `HALCommentator.PollAtmosphere`, which reads `body.radius` and player position. Not the forbidden zone — gameplay-side observation, not generation. Safe.
- **Save compatibility.** No save-schema changes. Old saves load identically. AI session state (memory store + standing) round-trips through the existing `SaveCollector.ApplyAIState` path untouched.
- **Token budget.** Worst-case FLEET STATE block: 8 ships × 50 tokens = 400 tokens. System prompt today runs ~1500-2500 tokens with retrieved entries. New ceiling ~1900-2900, comfortably under the 8192 ctx of the Hermes-3 model.

## 6. Testing Strategy

No automated tests — this is a Unity Editor project with no test runner. Verification is via Play-mode scenarios:

1. **Knowledge audit smoke test.** Open chat, ask each of the new ENTRY topics in plain language ("how do space nets work?", "what can you do?", "how do I build a cabin?"). Verify the AI answers from the new entries, not from hallucinated knowledge.
2. **Ship-aware AI smoke test.** Spawn 3 ships via the debug menu, leave one without a dish. Ask the AI: "mark Ship 1", "show Ship 2 on the map", "is Ship 1 orbiting?", "how much dust does Ship 1 have?", "mark Ship 3" (offline → expect refusal). Verify compass waypoints land, map focuses, telemetry answers match in-world state, and offline ship is refused both in chat text AND in tool dispatch.
3. **Atmosphere planet name.** Pilot ship from Humble Abode surface to space. Listen for "Leaving Humble Abode atmosphere, Astronaut." Then fly to Cyclops and descend. Listen for "Entering Cyclops atmosphere, Astronaut."
4. **HAL substantive feed.** Idle in the StartCabin for 5 minutes. Verify NO ambient "I am listening" / "All systems nominal" lines fire. Drain hunger to 49%, verify the line fires. Cook + eat, return hunger to 80%, drain again to 49%, verify the line fires again. Buy a ship with a net, leave it in orbit, periodically check that the "Ship N has collected 100 dust" line fires.
5. **Existing triggers regression.** Verify death line, killstreak milestones (5/10/15/20), phase change, planet visit, orbit-match voice, enemy proximity warning, and all 12 EarlyGameProgress flag lines still fire as before.
6. **Save round-trip.** Save, quit, reload. Verify FLEET STATE block contents match what the player just saw before saving (ship attachments, dust totals via `SpaceDustSave.nets`, ship positions).
7. **Overview docs review.** Read the rewritten `GAME_OVERVIEW.md` against CLAUDE.md and current code — no factual contradictions, no missing systems.

## 7. Out of Scope (for this spec)

The following are deliberately deferred to future specs:

- **Voice / TTS for new lines.** New volunteered lines reuse the existing `HALLineHUD` text channel + `HALVoicePlayer` voice manifest. New voice clips would need recording / generation — out of scope here.
- **Save persistence of HAL session state.** HAL threshold flags and ship-dust-announced sets reset each session. Not worth saving — re-firing them on the next session if the state hasn't changed is the desired behavior.
- **Real planet lore.** Placeholder lore is in-spec; real lore is the user's job and explicitly out of scope.
- **PITCH.md / PHONE_AI_OVERVIEW.md refresh.** Untouched unless asked.
- **AI-initiated combat advice / quest hints.** The AI continues to respond when asked, not proactively suggest tactics or quests. Could be a future feature.

## 8. Files Changed

```
NEW   Assets/3 - Scripts/AI/FleetTelemetry.cs
EDIT  Assets/3 - Scripts/AI/LLMService.cs
EDIT  Assets/3 - Scripts/AI/HALToolDispatcher.cs
EDIT  Assets/3 - Scripts/AI/HALCommentator.cs
EDIT  Assets/3 - Scripts/Map/SolarSystemMapController.cs
EDIT  Assets/StreamingAssets/AI/game_knowledge.md
EDIT  docs/GAME_OVERVIEW.md
NEW   docs/superpowers/specs/2026-05-22-phone-ai-revamp-design.md   (this file)
```

```
EDIT  Assets/3 - Scripts/Concert/ConcertStageHub.cs   (add OnStageActivated event — confirmed not present)
```

No `.meta` file management beyond what Unity does automatically for new files.
