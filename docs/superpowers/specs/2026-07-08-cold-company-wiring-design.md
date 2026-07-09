# Cold Company — Implementation / Wiring Spec

**Date:** 2026-07-08
**Companion doc:** `docs/GDD_VerticalSlice_Main1_ColdCompany.md` (the story + design; this doc is
the *code wiring* translation of it against the actual repo).
**Branch:** `feat/dimension-polish`
**Division of labor:** Sam places every GameObject; Claude writes all code/JSON/wiring
(per the GDD §⚑ and `docs/story-drafts/INTEGRATION_HANDOFF.md`).

---

## 0. Decisions locked (from brainstorming)

1. **Scope:** build the whole mission (all beats), not just Beat 1.
2. **Ship source:** the pilot opener grants the **license only, no ship**. Tev funds the
   first ship. If the opener currently spawns/grants a ship, that grant is neutralized.
3. **Money mechanic (revised by Sam):** Tev — a fisherman, now grounded — hands the player a
   **fishing bag pre-stocked with rare trophy fish**. The player learns the bag + sell loop
   by selling it at the fish market for ≥ $2000, then buys the ship.
4. **Fish bag contents:** **5 Rare fish @ ~150 lb each = ~$2,250** (value = `weightLbs × $3`
   for Rare). Heavy "trophy" weights above the natural 50 lb catch cap — sold in-fiction as
   Tev's legendary catches. Clears the exactly-$2000 full ship with ~$250 buffer.
5. **Compass guidance:** AI marks the **fish market first**; after the sale it moves the
   locator to the **ship vendor**. Both are real `CompassHUD` waypoints.
6. **Canon:** Cold Company is the canonical first real mission. Built as its own module; the
   older Act-2 "Cold Delivery" moon-delivery plan is treated as separate/superseded — no
   attempt to reconcile them on Constant Companion in this pass.
7. **Handler first-lie:** played as an **immediate in-world HAL beat** (`WorldDialogueUI`,
   speaker "AI") the moment the player opens the pod file — no phone-open delay.
8. **Player keeps no clues** (GDD §9.1): clues are read in place, set a flag, nothing enters
   inventory.

---

## 1. Systems this reuses (verified against source)

| System | Entry point | File |
|---|---|---|
| Money (dollars) | `PlayerWallet.Instance.AddMoney/SpendMoney(int)` | `Player/PlayerWallet.cs` |
| Fish bag (hotbar item) | `Hotbar.TryAddBag()`, `TryAddFishToBag(FishEntry)` | `UI/Hotbar.cs` |
| Fish data/value | `FishInventory.AddFish(tier,lbs)`; `FishEntry.GetValue()` = `lbs×{3/2/1}` | `Fishing/FishInventory.cs` |
| Fish vendor sell | `FishMarketNPC.OnConfirmSale()` sums `GetValue()` → `AddMoney(total)` | `Fishing/FishMarketNPC.cs:355` |
| Ship vendor buy | `ShipMarketNPC.Purchase(ShopItem)` → `SpendMoney(price)` → `SpawnShip` | `Vendor/ShipMarketNPC.cs:292` |
| Full ship price | `Ship44_Full.asset` price = **2000** | `Vendor/ShopItems/` |
| Compass waypoint (persisted) | `CompassHUD.Instance.AddWaypointByTag(id, tag, label)` | `UI/CompassHUD.cs:138` |
| Compass waypoint (closure) | `CompassHUD.Instance.AddWaypoint(id, ()=>pos, label)` | `UI/CompassHUD.cs:159` |
| HAL scripted line | `HALCommentator.Instance.VolunteerExternal(line, bypassRateLimit:true)` | `AI/HALCommentator.cs:381` |
| HAL waypoint alias | `HALToolDispatcher.Execute("waypoint","ship vendor")` (resolves `ShipMarket`) | `AI/HALToolDispatcher.cs:25` |
| Tev NPC (bespoke) | `TevDialogue.cs` flag-driven stage machine; `NPCConversationTracker.NotifyStart` | `NPC_Dialogue/TevDialogue.cs` |
| Mission flags | `StoryDirector` flag dict (saved/reset for free); typed wrappers like `Mission1.cs` | `Story/StoryDirector.cs`, `Story/Mission1.cs` |
| World dialogue UI | `WorldDialogueUI.Begin("conv_id")` (shows speaker) | `Story/WorldDialogueUI.cs:46` |
| License flag | `Mission1.Get(Mission1.FlagLicensed)` (`m1_licensed`) | `Story/Mission1.cs` |

**Compass persistence caveat:** `AddWaypoint` (closure) is NOT saved; `AddWaypointByTag` IS
(only `Gameplay`-kind, tag-based). Mission objective markers must survive save/load, so the
fish market and ship vendor need a **Unity tag** (or we re-add the marker on load from the
mission director). Decision: use `AddWaypointByTag` with tags `FishMarket` / `ShipMarket`
(Sam confirms/sets the tag on those objects; if tagging is awkward, the director re-adds a
closure marker on `sceneLoaded` — either works, tag is cleaner).

---

## 2. New mission-state module — `ColdCompany.cs`

A static typed wrapper over `StoryDirector` flags (mirrors `Mission1.cs`), so mission code
never hand-types keys and everything saves/resets automatically. Flags:

| Constant | Key | Set when |
|---|---|---|
| `FlagAssigned` | `cc_assigned` | Tev gives the assignment + fish bag |
| `FlagFishSold` | `cc_fish_sold` | player sells at fish market with the mission active |
| `FlagShipBought` | `cc_ship_bought` | player buys any full ship while `cc_assigned` |
| `FlagArrivedMoon` | `cc_arrived_moon` | arrival trigger on Constant Companion |
| `FlagSawPhotoWall` | `cc_saw_photowall` | PhotoWall clue read |
| `FlagSawReview` | `cc_saw_review` | ReviewStation clue read |
| `FlagOpenedPodFile` | `cc_opened_podfile` | PodFile opened (gated last) |
| `FlagGotRoute` | `cc_got_route` | ScrubbedRoute clue read |
| `FlagReadyReport` | `cc_ready_report` | PodFile AND Route done → "return to Tev" |
| `FlagReported` | `cc_reported` | report-back deduction played |
| `FlagComplete` | `cc_complete` | mission complete; Main 2 (Cyclops) unlocked |

Helpers: `ColdCompany.Get(flag)`, `Set(flag,val)`, plus `ReadyToReport()` (PodFile && Route)
and `BaseComplete()`. Null-safe like `Mission1`.

**No StoryStep enum surgery.** Everything gates on flags + the existing `m1_licensed`.
(Optional: append `StoryStep.ColdCompany*` values later for readability — not required.)

---

## 3. Beat-by-beat wiring

### Beat 1 — Tev's assignment (edit `TevDialogue.cs`)
Add a new stage in the flag-branch machine (`PlayDialogueSequence`), gated:
`Mission1.Get(FlagLicensed) && !ColdCompany.Get(FlagAssigned)` → `RunColdCompanyBriefing()`:
1. Speak the §2.1 lines (can't-fly backstory, culture line, the moon ask). Author as Tev
   lines (kept in code like existing `introLines`, or as a `conv_*.json` played via
   `WorldDialogueUI` — **decision: keep in TevDialogue code** to match how Tev already works).
2. **Grant the bag:** guard `HasEmptyHotbarSlot() && !HasFishBagAnywhere()`, then
   `Hotbar.TryAddBag()` + 5× (`FishInventory.AddFish("Rare",150)` → `TryAddFishToBag`).
   Edge: if no empty slot, defer/notify (rare; player hotbar is near-empty this early).
3. `ColdCompany.Set(FlagAssigned,true)`; `StartObjective("obj_cc_sellfish")`; `AddTrust`.
4. **HAL + compass:** `HALCommentator.VolunteerExternal("Marking the fish market on your
   compass — sell Tev's catch there.", true)` + `CompassHUD.AddWaypointByTag("cc_fishmarket",
   "FishMarket", "Fish Market")`.

### Beat 1b — Sell hook (edit `FishMarketNPC.OnConfirmSale`)
After the existing `AddMoney(total)` and only if `ColdCompany.Get(FlagAssigned) &&
!Get(FlagFishSold)`:
- `ColdCompany.Set(FlagFishSold,true)`; `CompleteObjective("obj_cc_sellfish")`;
  `StartObjective("obj_cc_buyship")`.
- `CompassHUD.RemoveWaypoint("cc_fishmarket")` + `AddWaypointByTag("cc_shipvendor",
  "ShipMarket","Ship Vendor")`.
- `HALCommentator.VolunteerExternal("That's more than enough. Marking the ship vendor — go
  get yourself a ship.", true)`.
- Kept minimal + flag-guarded so normal fishing sales outside the mission are untouched.

### Beat 1c — Buy hook (edit `ShipMarketNPC.Purchase`)
On a successful full-ship purchase, if `ColdCompany.Get(FlagAssigned) && !Get(FlagShipBought)`:
- `ColdCompany.Set(FlagShipBought,true)`; `CompleteObjective("obj_cc_buyship")`;
  `RemoveWaypoint("cc_shipvendor")`; `StartObjective("obj_cc_flymoon")`.
- Optionally mark Constant Companion on the compass (closure to the moon body) as a soft
  guide — **decision: yes**, `AddWaypointByTag("cc_moon","ConstantCompanion","Constant Companion")`.

### Beat 2 — Flight + arrival (new `ColdCompanyArrivalTrigger.cs`)
Reuses existing floating-origin flight. A trigger volume Sam places at the base approach:
`OnTriggerEnter` (CompareTag("Player")) + `cc_ship_bought` → `Set(FlagArrivedMoon)`,
`CompleteObjective("obj_cc_flymoon")`, `RemoveWaypoint("cc_moon")`, HAL "left in a hurry"
line, `StartObjective("obj_cc_investigate")`. One-shot.

### Beat 3 — Base clues (new `MissionClue.cs` + a display panel)
`MissionClue` (mirrors `Discoverable.cs` gaze/F prompt): fields `clueId` (the `ColdCompany`
flag to set), `title`, `bodyText`, optional `Sprite image`, `requiresFlag` /
`requiresAllFlags` (for the pod-file soft-gate), `oneShot`. On interact → show the clue panel
(title + body + image), set the flag, fire any completion checks. Instances Sam places:
`Clue_PhotoWall`, `Clue_ReviewStation`, `Clue_PodFile`, `Clue_ScrubbedRoute` (+ optional
`Clue_Keepsake`, `Clue_CensoredScan`).
- **PodFile soft-gate:** `requiresAllFlags = [cc_saw_photowall, cc_saw_review]`; if unmet, the
  prompt shows "Not yet — look around first" and doesn't open.
- **PodFile open** → `Set(FlagOpenedPodFile)` → immediately `WorldDialogueUI.Begin(
  "conv_cc_first_lie")` (HAL first-lie beat, §4.4 lines).
- Clue art (`Clue_PodFile` crash photo w/ ORG???, `Clue_PhotoWall` dated black-hole
  sequence) generated via Coplay image-gen; placeholder sprites acceptable to start.
- After each clue: if `ReadyToReport()` → `Set(FlagReadyReport)`,
  `CompleteObjective("obj_cc_investigate")`, `StartObjective("obj_cc_report")`, HAL "let's
  see where they went / head back to Tev" + optional compass mark back to Tev.

### Beat 3b — Death → black-hole growth (new `BlackHoleDeathGrowth.cs`)
Component on `BlackHole_MoonView`. Reads the player death count (source: `HALCommentator`
already tracks deaths — expose/read the count, or the save's death stat / `{PLAYER_DEATHS}`
token source) and scales the object (localScale and/or an instability param) monotonically
with deaths, refreshed on enable + on death events. No dialogue (GDD §3). Baseline growth in
the photo wall stays pre-arrival (GDD continuity note).

### Beat 4 — Report back (edit `TevDialogue.cs`, second new stage)
Gated `ColdCompany.Get(FlagReadyReport) && !Get(FlagReported)` → `RunColdCompanyReport()`:
speak the §6 two-part deduction (Tev's sealed-Cyclops-base memory snaps against the player's
evidence). Then: `Set(FlagReported)`, `AddTrust` (Tev trust ↑), `Set(FlagComplete)`, unlock
Main 2 — set a `main2_cyclops_available` flag / `StartObjective("obj_m2_intro")` stub so
Tev's next ask can open from here. `CompleteObjective("obj_cc_report")`.

---

## 4. New conversation JSON (StreamingAssets/Story)

- `conv_cc_first_lie.json` — HAL, speaker "AI", the §4.4 first-lie comfort lines (warm,
  never defensive). Played via `WorldDialogueUI` on pod-file open. Single node, "Continue"
  response, no branching required (may add a quiet player reply).

Tev's lines stay in `TevDialogue.cs` code (matching existing Tev pattern), not JSON.

## 5. Objectives / hints (merge into live JSON)
Add to `objectives.json`: `obj_cc_sellfish`, `obj_cc_buyship`, `obj_cc_flymoon`,
`obj_cc_investigate`, `obj_cc_report` (merge — keyed list, don't replace). Hint tracks
optional; add short tips if time.

## 6. New files & edits summary

**New code:** `Story/ColdCompany.cs`, `Story/ColdCompanyDirector.cs` (auto-singleton — seed
in `MainMenuController.EnsureGameplaySingletons`, trap #1), `World/ColdCompanyArrivalTrigger.cs`,
`World/MissionClue.cs`, `UI/MissionCluePanel.cs` (or reuse an existing readable panel if one
fits — check first), `World/BlackHoleDeathGrowth.cs`.
**New JSON:** `StreamingAssets/Story/conv_cc_first_lie.json`; objectives merge.
**Edits:** `TevDialogue.cs` (2 stages), `FishMarketNPC.cs` (sell hook), `ShipMarketNPC.cs`
(buy hook), `MainMenuController.cs` (seed director), the pilot opener (neutralize any ship
grant — verify it exists first).
**Each new `.cs`** needs a hand-written `.meta` (unique GUID) + `git add` both.
**Compile-verify** with coplay `check_compile_errors` after each code change.

## 7. Placement manifest (Sam) — see §8 of the GDD; delta from it:
- Fish market + ship vendor already exist in-world — Sam only needs to **set their Unity tags**
  to `FishMarket` / `ShipMarket` (for the persisted compass markers), or confirm existing tags.
- Everything else per GDD §8.1 (base clues, arrival trigger, black-hole view, dressing).

## 8. Test gate (GDD §7)
End-to-end: license held → talk to Tev → briefing + fish bag → sell at fish market (≥$2000)
→ buy ship → fly to Constant Companion → arrival → clues (photo wall + review + **pod file →
first lie**) → scrubbed route → return to Tev → deduction → Tev trust ↑, Main 2 unlocked.
Plus: license gate (no license → no briefing); death → black-hole visibly grows from the moon.

## 9. Open risks
- Fish bag grant if hotbar is unexpectedly full (guarded; deferred-grant fallback).
- Compass tag vs. closure persistence (tag chosen; director re-add fallback documented).
- Death-count source for the black hole — confirm the canonical counter before wiring.
- Clue art pipeline (Coplay image-gen) — placeholders first, polish later.
