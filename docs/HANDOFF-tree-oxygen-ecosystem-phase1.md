# HANDOFF — Tree Oxygen Ecosystem, Phase 1

**Project:** Sound of Space (Unity 2022.3, Built-in RP, floating-origin + n-body foundation)
**Goal for this session:** Ship the core tree/oxygen ecosystem loop in one day. Research first, then build.
**Read `CLAUDE.md` first.** Follow existing conventions. Sam playtests; you implement.

---

## 1. Design intent (context)

The core survival loop is being refocused around oxygen. Trees are becoming the ecological engine of the game:

- Every planet has an **oxygen content %**. It is derived from how many trees the planet has relative to its size, plus a reserve that can be built up artificially (see Bubble Domes).
- Standing **near trees locally raises** the effective O2 at the player's position above the planet baseline.
- The suit has an **oxygen converter**: higher ambient O2 = slower suit-tank drain. Above a threshold (dense forest), the converter goes **net-positive and slowly refills the tank**. Forests are literal safe zones the player can create.
- Harvesting a tree now drops **saplings** in addition to wood. Saplings can be **planted** and grow at a speed driven by ambient O2. Below a minimum O2 they will not grow at all.
- **Bubble Domes** are placeable structures that create a quarantined mini-atmosphere (interior baseline 20% O2, independent of the planet). Trees planted inside raise the interior O2. At 100% interior, the dome vents surplus into the planet's atmosphere, slowly raising the planet's O2 — this is how a 0% dead planet gets bootstrapped back to life. One dome is enough; more domes vent faster.

Fantasy: cut down a forest and you kill a region's breathability; plant and cultivate and you terraform dead worlds. Suit O2 converter **tiers are explicitly NOT in scope today** — one fixed converter efficiency.

---

## 2. Research phase (do this before writing any code, report findings)

[EXISTS] Verify and document how each of these currently works. Produce a short findings summary before the build step:

1. **Trees** — How are trees on planets implemented? Prefab instances, spawned procedurally, hand-placed? How is tree-chopping/wood-harvesting done today (the fishing/cooking/building loop era code)? Where do harvest drops go (inventory system entry point)?
2. **Planets** — What does the planet/celestial body class look like (Sebastian Lague lineage — likely a `CelestialBody` with radius)? Is there a per-planet component we can extend, and do planets already have any atmosphere flag (trees exist only on atmosphere planets, never moons)?
3. **Suit oxygen** — Where does the current suit O2 drain live? What drives drain rate today, and where is the tank value stored/displayed?
4. **Floating origin** — How do world-space positions shift? Any O2 sampling or tree registry MUST work in planet-local coordinates (positions relative to the planet transform), or re-anchor on origin shift. Confirm the established pattern other systems use.
5. **Scripted placement** — Freeform building was cut but scripted placement remains. Find the existing placement flow so saplings and domes reuse it rather than inventing a new one.
6. **Persistence** — What save system exists, if any? Tree instances, sapling growth progress, dome placements, and planet O2 reserve all need to persist. If there is no save system, flag it and keep state alive for the session only ([OPEN] below).
7. **HUD** — Locate the helmet HUD cluster screens so an ambient O2 readout can be added with minimal churn (HUD redesign is in progress; keep this integration light).

---

## 3. System spec

All numbers below are tunable defaults — expose them in inspectors/ScriptableObjects per project convention.

### 3.1 Planet atmosphere

[BUILD] `PlanetAtmosphere` component (one per atmosphere-capable planet):

```
treeContribution = 100 * treeCount / expectedTreeCount
expectedTreeCount = treeDensityConstant * planetRadius^2
planetO2 = clamp(treeContribution + ventedReserve, 0, 100)
```

- `treeCount` = live mature trees registered on this planet (see TreeRegistry). Cutting a tree lowers `treeContribution` immediately — ecosystem damage is instant and real.
- `ventedReserve` = persistent float accumulated by Bubble Domes venting (never decays in Phase 1).
- Tune `treeDensityConstant` so **Humble Abode's current tree population lands around 85–95%** planet O2. Report the constant you land on.
- Moons/no-atmosphere bodies: no component, effective planet O2 = 0.

### 3.2 Tree registry & local oxygen

[BUILD] `TreeRegistry` (per planet): mature trees register on spawn/growth, deregister on harvest/destroy. Store **planet-local positions**.

[BUILD] Local O2 sampling at the player (and at any sapling, for growth):

```
localBonus = sum over mature trees within radius R of:
             perTreeBonus * (1 - distance/R)        // linear falloff
localBonus = min(localBonus, localBonusCap)
effectiveO2 = min(100, planetO2 + localBonus)       // unless inside a dome — see 3.5
```

Defaults: `R = 30m`, `perTreeBonus = 3`, `localBonusCap = 25`.

- Sample on a timer (every 0.5–1.0s), never per-frame. Use a spatial grid or physics overlap against a Trees layer — pick whichever fits the codebase; justify the choice in your findings.
- Must survive floating-origin shifts (planet-local math).

### 3.3 Suit converter (single tier)

[INTEGRATE] Replace the flat suit drain with an O2-modulated net rate:

```
netDrain = baseDrain * (1 - effectiveO2 / refillPoint)
```

- `refillPoint = 80`. At effectiveO2 = 0 → full drain. At 80 → break-even. Above 80 → negative drain = refill.
- Clamp refill speed: `maxRefill = 0.2 * baseDrain` (refilling is deliberately slow — a reward, not a fountain).
- Interior of a ship keeps its existing safe-zone behavior; do not regress it.

### 3.4 Saplings — drops, planting, growth

[BUILD] Harvest drops: destroying a mature tree yields its existing wood **plus 1 sapling guaranteed**, +1 more at 25% chance, +1 more at 10% chance (max 3). New inventory item: Sapling.

[BUILD] Planting: player plants a sapling via the existing scripted-placement flow (ground surface of a planet, or inside a dome). Planted sapling = small growing-tree prefab with a growth timer.

[BUILD] Growth rule, evaluated at the sapling's own position on the same sampling timer:

```
growthO2   = dome interior O2 if inside a dome, else effectiveO2 at sapling
growthRate = growthO2 >= 10  ?  (growthO2 / 100)  :  0     // hard floor: no growth below 10%
timeToMature = baseGrowthDuration / max(growthRate, epsilon)
```

- `baseGrowthDuration = 10 minutes` at 100% O2 (so ~20 min at 50%, stalled below 10%). [OPEN] — pacing needs playtesting; make it a single tunable.
- Growth progress accrues continuously (a sapling paused at 0 growthRate keeps its progress; it doesn't die in Phase 1).
- On maturing: swap/scale to full tree, register in TreeRegistry (it now counts toward planet + local O2, and drops saplings when harvested — the loop closes).

### 3.5 Bubble Domes

[BUILD] `BubbleDome` placeable structure:

- Placed via scripted placement on any planet surface. Visible hemisphere/bubble field with a trigger volume defining "inside."
- **Quarantined atmosphere:** anything inside samples the dome's interior O2 instead of the planet's. Interior is independent of (and protected from) the planet atmosphere.
- Interior O2 is **computed, not accumulated**:

```
interiorO2 = min(100, 20 + perTreeInterior * matureTreesInside)
```

Default `perTreeInterior = 10` → 8 mature trees to reach 100% from the 20% baseline. Cutting a tree inside instantly lowers interior O2 (falls out of the formula for free).

- **Venting:** while `interiorO2 == 100`, the dome adds to the planet's `ventedReserve` at `ventRate` per second. Multiple full domes stack linearly. Default target: one full dome raises a dead planet by ~1 O2 point per 2 minutes. [OPEN] — pacing, tune with Sam.
- Player inside a full dome (100% interior) gets converter refill per 3.3 (effectiveO2 = 100 > 80). Domes are hand-built safe rooms — intended.
- Dome recipe/cost: [OPEN] — wire it as craftable/placeable with a placeholder cost; Sam decides the real recipe.

### 3.6 HUD (minimal)

[INTEGRATE] Add to an existing helmet cluster screen: current ambient/effective O2 % and a small drain-vs-refill indicator (e.g., ▼/▲ next to the tank readout). Nothing fancier — HUD redesign is in flight.

---

## 4. Test plan

[TEST] On Humble Abode: ambient O2 reads high; walking into dense forest raises effective O2; suit tank visibly refills while standing in forest; walking onto open plain returns to slow drain.
[TEST] Clear-cut a grove: local O2 drops immediately in that area; planet baseline ticks down; harvest yields wood + at least 1 sapling each time (spot-check the 2–3 rolls over ~20 trees).
[TEST] Plant two saplings — one deep in forest, one on a bare plain: forest sapling matures noticeably faster; on maturing, it registers (nearby O2 rises) and can itself be harvested for saplings.
[TEST] On a 0% body: sapling planted outside never progresses. Place a dome: interior reads 20%; sapling inside grows; at 8 mature trees interior reads 100%; planet O2 begins slowly ticking up; a second full dome doubles the tick rate; at planet O2 ≥ 10%, an outdoor sapling starts growing (slowly).
[TEST] Floating-origin regression: fly to space and return; local O2 sampling, sapling growth checks, and dome interiors all still correct.
[TEST] Save/load (if a save system exists): mid-growth saplings, dome placements, and ventedReserve survive a reload.
[TEST] Ship interior still behaves as a safe zone; no regression to existing suit O2 UI/death flow.

---

## 5. Non-goals today (do not build)

- Suit converter upgrade tiers (one fixed efficiency only)
- Flowers/ambient flora spawning near trees (later phase)
- Hunger/thirst rebalance and movement penalties (separate task)
- Trees or domes on moons remaining treeless by design — no new content there
- Any O2 diffusion/simulation — sampling formulas only

---

## 6. Open items for Sam

[OPEN] Growth pacing (`baseGrowthDuration`) and dome vent pacing (`ventRate`) — playtest and tune together.
[OPEN] Dome crafting recipe/cost and where the player obtains it.
[OPEN] If no save system exists: this feature makes persistence load-bearing — decide whether that's today's follow-up or a separate day.
[OPEN] Whether trees inside domes should ALSO count toward planet `treeContribution` once the planet is breathable (Phase 1 answer: no, quarantined means quarantined).

---

## 7. Workflow reminders

- Research findings summary → wait for placement needs → Sam places any hand-authored GameObjects → you script and wire.
- Saplings and domes are **runtime player-placed** via the scripted placement flow; only test-scene setup objects need a placement manifest from Sam.
- Keep commits scoped per system (atmosphere / registry+sampling / converter / saplings / domes / HUD) so anything can be reverted independently.
