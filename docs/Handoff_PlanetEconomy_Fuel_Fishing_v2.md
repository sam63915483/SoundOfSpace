<!-- doc-status: stamped 2026-09-08 -->
> 🟢 **ACTIVE — this is the current core loop.** Fish → fly → sell into another planet's economy → buy fuel → fly again. Phases 0–4 are built; see `docs/PLAYTEST_PLANET_ECONOMY.md` and `docs/PLAYTEST_FIXES_2026-09-07.md` for what is still awaiting playtest. Tuning revisited 2026-09-08 — jump range is 15 km (§5.2 as written); the burn curve, not the range, is what got softened.

# Handoff — Planet Economy v2: fuel, per-planet species, fish markets

Date: 2026-09-07 · From: Sam (via Claude chat) · For: Claude Code
Branch: main (clean merged baseline, Sep 6)
Tags: [EXISTS] already in repo · [VERIFY] check + report · [BUILD] new · [INTEGRATE] wire into existing · [TEST] · [OPEN] Sam decides
v2 supersedes v1: vendor prefabs move to Phase 1, crystals sold by the GOODS vendor (not the fish vendor), each Twin gets its own table + vendors, dead-rock planets allowed with a hard rule.

---

## 0. Read first

- Rule 4: state your build plan for each phase BEFORE writing code. Sam corrects, then you build.
- Sam places all GameObjects. You build prefabs/components, report object names, he positions.
- Do NOT touch: clockwork rails / SolarSystemSync, floating origin, ShuttleSync rider handover, FishFightSim, cast-distance rarity.
- Scope call: core gameplay only. No dialogue, no story. Fishing is THE loop right now; TRAX becomes the mid-game earner later (not in this handoff).
- Build order matters: Phase 1 (the two vendor prefabs) ships FIRST and alone, because Sam places ~20 instances by hand and that takes him a while. He places while you build Phases 2–4. Tell him the moment the prefabs are ready.
- Phase 0 is a report, not code.

## 1. What we're making

Two halves of one idea: make travel cost something, then give reasons to travel.

- Fuel: the shuttle gets the manual ship's reactor. Full tank = one 15 km jump, then you're stranded until you harvest crystals or buy them from a goods vendor. NAV greys out anything out of range. Crossing the system = short hops + riding orbits until the planet you want swings close.
- Planet economies (No Man's Sky trading-outpost feel): every fishable planet has its own species table and its own fish vendor with its own buy list. Fish pay ×1 at home, more where they're imported, top dollar where they're a delicacy — and every species has at least one far-off planet that treats it as a delicacy. Learn the routes, become a fish runner.

Loop (SAAD): Crystals → Reactor → Nav app → Jump → Planet → Fishing (planet's table) → Catch → Fish vendor (own buy list, appetite) → Money → Goods vendor (crystals) → Reactor; vendor boards seen → phone Markets page → plans next jump.

## 2. Decided (Sam, Sep 7) — don't re-ask

- Twins: EACH twin is its own planet for this system — own species table, own fish vendor, own goods vendor. Four mains total: Humble Abode, Twin A, Twin B, Cyclops.
- Crystals are sold by the GOODS vendor. The fish vendor buys fish and sells bait, nothing else.
- Goods vendor exists on all 4 mains + a few dwarfs (which dwarfs: OPEN-1). Fish vendor exists on every fishable body.
- Dead-rock planets (no natural crystals) are allowed — subject to the hard rule in 4.5.
- Unlisted fish: vendor buys them at ×0.5 (dump price), never refuses.
- Vendor boards show words (Local / Imported / Delicacy), not multipliers or $. Matches the tape word-ladder rule.
- Phone Markets page knowledge is per player in co-op.
- Goods vendor stock is the same on every planet, plus crystals.
- Fixed launch+land fuel cost: yes, 5 units (on top of per-km).
- Intro landing fuel: set after the distance table (OPEN-3).

## 3. Phase 0 — measure before you build [VERIFY]

Report back as docs/PlanetEconomy_Phase0_Report.md. Fuel numbers in Phase 2 depend on #1.

1. Distance table. From rail radii + periods, for every body pair: min distance, max distance, minutes-per-cycle within 5 / 8 / 15 km. Editor menu item or headless test that writes docs/DISTANCE_TABLE.md. Slowest rail is 20 min, so one cycle covers it.
2. Reactor today. Manual ship reactor: capacity, units per crystal, burn model, where fuel is saved. We share it, not fork it.
3. Crystals. Which bodies the crystal spawner runs on, density per body, and whether consumed crystal cells ever respawn. Mushroom cells persist consumed — if crystals do too, the system runs dry.
4. Water. Which bodies have the water collider (= fishable = gets a fish vendor). Moons presumably none.
5. Fish money. Current FishValue by tier × weight — $ at 1 / 25 / 50 lb for common / uncommon / rare. Reference: pure FULL tape median $73, p90 $168 (Aug 18). TRAX must stay the ceiling.
6. Both current vendors. Every component that makes up the Humble Abode fish vendor (NPC spawner, stand, sell UI, bait shop, GRULABU turn-in) AND the goods vendor (NPC, stand, shop UI, stock). Both become prefabs in Phase 1.
7. Species list today (12 names). We append, never rename.
8. Body names of the two Twins as the code knows them.

## 4. Phase 1 — Vendor prefabs [BUILD] — ship this first, alone

Goal: Sam can drag one prefab per planet with ZERO per-instance config.

### 4.1 FishMarket prefab
- Stand + AuthoredNPCSpawner vendor + board (4.3) + fish sell UI hook + bait shop + bounty turn-in.
- Resolves its body from its parent hierarchy (same body-parenting the spawners use) and pulls its buy list from the economy table by body name (Phase 4). Until Phase 4 lands, it behaves like today's vendor (buys everything at ×1) so placement can happen now.
- [INTEGRATE] Migrate the existing Humble Abode fish vendor into an instance. One code path.

### 4.2 GoodsVendor prefab
- Stand + AuthoredNPCSpawner + shop UI + stock. Same body auto-detect.
- [BUILD] Crystal as a purchasable item. Price: OPEN-3 (target: a full tank ≈ one decent local fishing session). [OPEN-2] markup on dead rocks.
- [INTEGRATE] Migrate the existing Humble Abode goods vendor into an instance.

### 4.3 Board (both prefabs)
- Look-at board/screen at the stand. FishMarket board: the buy list in words. GoodsVendor board: stock. Placeholder text is fine in Phase 1; Phase 4 fills it.

### 4.4 Placement helper
- [BUILD, small] Editor menu "Vendor → Snap to shoreline": moves the selected instance to the nearest point where terrain meets the water radius, upright on the planet normal, facing the water. Sam drags under the body root, clicks snap, nudges.
- Report to Sam: prefab names, where they live, the snap menu path, and a one-line "how to place". He'll place roughly 12 fish markets + ~8 goods vendors.

### 4.5 Hard rule (enforced by the Phase 6 test)
- Every fishable body has natural crystals OR a goods vendor (or both). A dead rock MUST have a goods vendor — otherwise landing there with an empty tank is a permanent softlock (the pod in the shuttle is the only save point; dying respawns you on the same rock).
- So OPEN-1 ("which dwarfs get a goods vendor") and OPEN-4 ("which bodies are dead rocks") are the same list, or dead rocks ⊂ goods-vendor dwarfs.
- Co-op: authored spawners are already MP-safe; sale money stays personal; appetite counters (Phase 4) are world state.

## 5. Phase 2 — Fuel [BUILD]

### 5.1 Reactor on the shuttle
- [EXISTS] Manual ship reactor: crystals in → fuel.
- [BUILD] Extract into a shared `ShipReactor` component/prefab used by both ships. Shuttle gets an instance; fuel = shuttle world state (saved with the shuttle, synced by ShuttleSync, rides the join snapshot).
- [INTEGRATE] Build + place the reactor prop in the shuttle interior (whiteboard protocol: place it somewhere sane, report the object name, Sam moves it).
- Co-op: one shared tank, either player inserts crystals.

### 5.2 Fuel math (numbers OPEN-3 until the distance table is in)
- Tank 0–100 units. `RatePerKm = 100 / 15` → a full tank is exactly one 15 km jump. Plus a fixed 5-unit launch+land cost per jump — so the true max hop is slightly under 15 km; keep "15 km" as the displayed max and let the range readout do the honest math.
- Charged once, at GO press, from the distance at that moment. Planets keep moving in flight; no re-billing. Insufficient fuel = button greyed, never a mid-flight failure.
- Crystals → units: copy the manual ship's conversion from Phase 0 #2.

### 5.3 Intro
- [EXISTS] Aug 28 intro: 30s scripted approach, player lands at the 100 m hover.
- [BUILD] Shuttle spawns at 50 units. The scripted approach visibly drains the console gauge (teaches fuel exists before anyone reads a tooltip). Land at ~35–40 units → first-hop range ~5 km. Tune so at least one dwarf is reachable from Humble Abode within a few minutes of orbital waiting.

### 5.4 NAV app
- [EXISTS] NAV lists planets + distances; autopilot flies there.
- [BUILD] Fuel gauge + "range: X km" on the NAV screen.
- [BUILD] Planets beyond range greyed + unselectable; distance still shown.
- [BUILD] Per greyed planet: "in range in ~N min", computed by stepping the deterministic rails forward (10s steps, one cycle). This is what makes riding orbits a strategy.
- [BUILD] Per planet: a fuel-source tag — "crystals" / "vendor" / "crystals + vendor". [OPEN-5] always visible (shuttle scans from orbit) or only after first visit. Sam's intent is "know before you go it's worth it AND has fuel", which reads as always visible.
- NOT in this handoff: the Sep 6 2D solar-system render with a jump-radius ring.

### 5.5 Stranding
- Stranding is the design. No no-strand rule. The 4.5 rule guarantees it's a setback (grind local fish → buy expensive crystals → leave), never a dead save.
- [BUILD if Phase 0 #3 says they don't] Crystal cell respawn on a timer (1 in-game day = 24 real min), same keyed-cell pattern as wild mushrooms.

## 6. Phase 3 — Per-planet species tables [BUILD]

### 6.1 Data
- [BUILD] `FishSpeciesCatalog` (id, display name, tier, model, recolour) + per-body `PlanetFishTable` (species ids). Editable assets or one readable JSON in docs/ — Sam hand-tunes the draft.
- Dwarfs (Puddle, Hearth, Anvil, Ember, Slag, Shard, Pebble, Bruise): exactly 1 common, 1 uncommon, 1 rare.
- Mains (Humble Abode, Twin A, Twin B, Cyclops): 2 common, 2 uncommon, 2 rare.
- Slots: 8 × 3 + 4 × 6 = 48.

### 6.2 Pool
- [EXISTS] 12 species = 4 per tier over 3 models per tier, recoloured.
- [BUILD] Grow to 28 species (keep the 12 verbatim, add 16 — still recolours, no new meshes). That fills 48 slots as 20 species on 2 planets + 8 exclusives on 1 planet. Exclusives are the travel bait. If Sam would rather name only 12 new ones, pool = 24 and every species sits on exactly 2 planets (no exclusives) — his call, OPEN-6.
- Name suggestions, Sam picks/renames (alien bass/trout/perch energy): Grubbler, Skrout, Purchlet, Wallop, Murkfin, Flubb, Knurl, Tarnish, Zibbet, Ossum, Snagg, Vorm, Sallow, Dredge, Brindle, Hushfin, Crag. Placeholders if he hasn't picked when you get here.

### 6.3 Runtime
- [INTEGRATE] Bite/species selection reads the planet's table. Cast-distance rarity stays: cast distance picks the tier, then a species from that planet's tier list.
- [EXISTS] Bounty fish (GRULABU + zone), sun-angle bite rate, bait, fight — untouched.

### 6.4 Generator
- [BUILD] Editor tool that drafts the tables from the distance table under these constraints, writes them out, stops. Sam edits by hand after.
  - Every species on 1 or 2 planets; each table respects its 3/6 shape.
  - A species shared by two planets: never immediate neighbours (overlap on neighbours is pointless). The Twins in particular get fully distinct tables.
  - Hint: make each Twin a delicacy buyer for the other's fish — the shortest jump in the system becomes the tutorial trade route.

## 7. Phase 4 — Planet economies [BUILD]

### 7.1 Each fish vendor's buy list
Multipliers apply on top of the existing FishValue (tier × weight):

| Bucket | What | Mains | Dwarfs | Pays |
|---|---|---|---|---|
| Local | every species in this planet's own table | all | all | ×1.0 |
| Import | off-world species they can't catch here | ~5 | ~3 | ×1.75 |
| Delicacy | off-world species they love; NOT in the local table | 2–3 | 1 | ×3.0 |
| Unlisted | everything else | — | — | ×0.5 |

- Local always pays; fishing at home is never a dead end.
- Multipliers are OPEN-3 tuning once crystals have a price. A delicacy trip must clearly beat staying home after fuel; the best delicacy run per real hour must still sit under a good FULL tape.

### 7.2 Appetite (what stops "one route forever")
- [BUILD] Per vendor, per species: fullness counter, world-scoped. Each Import/Delicacy sale adds 1. Effective multiplier = bucket × (1 − 0.15 × fullness), floored at ×1.0. −1 per in-game day.
- Local never decays. Same shape as the buyer craving counter — reuse the pattern, not the class.
- First 3–4 delicacy fish are the jackpot, then the route cools and you rotate. NMS supply/demand in one counter.

### 7.3 Knowledge
- [INTEGRATE] FishMarket board (4.3) now shows the real list: "Local", "Imported – pays well", "Delicacy – pays top".
- [BUILD] Phone MARKETS page: one row per visited planet showing exactly what its boards said (fish list + whether it sells crystals); "??" for unvisited. Per player. The game remembers what you've SEEN; it never tells you what you haven't.

### 7.4 Generator constraints (extends 6.4)
- For every species: ≥1 vendor lists it as a Delicacy on a planet where it is NOT catchable. Guaranteed route.
- Import lists drawn from species catchable within 1–2 hops (early routes feasible). Delicacy picks prefer farther planets (big payday needs the big trip).
- Every fishable body has exactly one fish vendor.

### 7.5 Sell flow
- [EXISTS] Vendor sell UI for fish (Sep 3) + GRULABU turn-in.
- [INTEGRATE] Sell UI shows the bucket word per fish and the price it pays right now (appetite applied). Displayed == paid, same rule as DealTerms.

## 8. Tests [TEST]

- Distance table: every body has at least one other body within 8 km at some point in the cycle; report the worst gap (Cyclops suspected).
- Hard rule 4.5: every fishable body has natural crystals or a goods vendor. Fails the build if not.
- Economy constraints: every species has ≥1 off-home Delicacy buyer; every fishable body has one fish vendor; every table has its 3/6 shape; species on ≤2 planets; no shared species between neighbours.
- Fuel math: full tank = exactly 15.0 km before the fixed cost; GO refused below cost; charged once; intro ends inside the OPEN-3 band.
- Pricing parity: random (species, weight, vendor, fullness) → displayed price == paid, including appetite decay and floor; unlisted = ×0.5.
- Appetite: −1 per day exactly, floors at ×1.0, Local never decays.

## 9. Open — Sam answers when convenient [OPEN]

1. OPEN-1 — Which dwarfs get a goods vendor (= which are allowed to be dead rocks). Suggestion: 2–3, spread across the system so no region is vendor-free.
2. OPEN-2 — Crystal price markup on dead rocks (scarcity, NMS-style). Suggest ×1.5.
3. OPEN-3 — Numbers: intro landing fuel, crystal price, Import/Delicacy multipliers, appetite step. Proposals above; final after the Phase 0 report.
4. OPEN-4 — Which bodies are dead rocks. Same list as OPEN-1 or a subset.
5. OPEN-5 — NAV fuel-source tag always visible, or only after first visit.
6. OPEN-6 — Pool 28 (16 new names, 8 exclusives) or 24 (12 new names, no exclusives).

## 10. Out of scope (don't build)

- 2D solar-system NAV render with jump-radius ring.
- Fish perishability / coolers.
- TRAX as mid-game unlock and its balance vs fishing — next handoff.
- Any dialogue/story/NPC writing. Dwarf-planet side quests come later, planet by planet.
- New fish meshes, new bounty fish, new bounty zones.
- Walk-in vendor stores (Aug 22 direction) — the stands stay stands.
- Per-planet goods-vendor stock variation.
