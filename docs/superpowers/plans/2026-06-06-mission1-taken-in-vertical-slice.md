# Mission 1 "Taken In" — Vertical Slice Implementation Plan

> **For agentic workers:** This plan adapts the standard format to a Unity 2022.3 project with **no automated tests** — verification is "compiles clean in the Editor Console" plus a human playtest. Steps use checkbox (`- [ ]`) syntax. Several tasks end at a **🛑 PLACEMENT CHECKPOINT** where a human must position/author a GameObject in the scene before wiring continues.

**Goal:** Build the Mission 1 three-way fork ("Taken In") as specified in `docs/GDD_VerticalSlice_Mission1_Fork.md` — shared opening (crash → survive → village → meet Tev → explore → report → choose) plus the content-complete **Pilot** branch, with **Build** and **Fish** present-but-locked stubs.

**Architecture:** Extend the existing live `StoryDirector` (state/flags/objectives/trust, already save-round-tripped) rather than inventing a new mission system. The opening is **restructured** (per user decision): the current "build a cabin before the village" gate is removed; building is soft-locked for the whole slice (deferred to the stubbed Build branch). The report + 3-way fork is an **in-world Tev conversation at the vendor village** using the existing `PostGreetingChoicePanel`. The Pilot branch adds a `PilotLicenseController` (mirrors `PistolController`), a new lightweight `DroneController` (copies — does not subclass — `Ship` flight math), a waypoint/gate drill course, a dedicated flight-instructor NPC, and a Constant-Companion arrival detector that fires the Mission 2 hand-off.

**Tech Stack:** Unity 2022.3 Built-in RP, Assembly-CSharp (no asmdefs), C#, JSON story content under `Assets/StreamingAssets/Story/`, `JsonUtility` save schema.

**Key decisions (from the user, 2026-06-06):**
1. **Full restructure** of the opening per the GDD (remove the cabin-build gate).
2. **Fork happens at the vendor village** — Tev + report + fork live there.
3. **Dedicated instructor NPC** runs the drone drills.
4. (Plan author's calls, confirmable:) ORG framing stays a whisper in M1 (first real tasking lands in M2). Build/Fish menu options are shown **disabled with a "let's save that for later" line** (not hidden) so the fork still demonstrates the reusable pattern. Tev still hands the player the axe + pistol during his village intro so the player isn't left unarmed for the combat tips.

---

## File map

**New scripts (`Assets/3 - Scripts/`):**
- `Story/Discoverable.cs` — trigger-volume component; on first player entry sets a StoryDirector flag + fires an "observed it" HAL line. (§4 discoverables)
- `Story/Mission1.cs` — small static helper holding M1 flag-name constants + branch enum + helpers (which discoverables seen, branch chosen, completion). Keeps magic strings in one place.
- `NPC_Dialogue/TevReportDialogue.cs` — the village report + 3-way fork conversation (greeting → light branch on what was seen → `PostGreetingChoicePanel` 3-way menu → route to Pilot / locked stubs).
- `Ship/PilotLicenseController.cs` — license unlock flag on the Player (mirrors `PistolController`); static accessor for the Ship gate.
- `Ship/DroneController.cs` — lightweight flyable training drone (copied flight math, no hatch/boost/assists/persistence).
- `Ship/DroneSchoolCourse.cs` — drill sequencer: ordered gate list, landing-pad finish, pass/fail-retry, grants license on completion.
- `Ship/DroneGate.cs` — one course waypoint/gate trigger; reports pass-through to its course.
- `NPC_Dialogue/FlightInstructorDialogue.cs` — instructor NPC; starts the course, gives drill briefings, reacts to pass.
- `Ship/ConstantCompanionArrival.cs` — proximity/landing detector for Constant Companion; fires the M2 hand-off once licensed-ship arrives.

**Modified scripts:**
- `Story/StoryDirector.cs` — extend `StoryStep` with mission steps; drop the `NeedsShelter` shelter gate from `CheckGates`; add the village→mission transition; reset new runtime state.
- `Story/VillageReachTrigger.cs` — unchanged (already fires `OnVillageReached`); reused as the trek-to-village anchor.
- `Scripts/Game/Controllers/Ship.cs` — add the pilot-license gate in `TogglePiloting()`.
- `Building/BuildMenuLock.cs` — used as-is via API; no edit (we just call `LockAllExcept()`/`UnlockAll()`).
- `SaveSystem/SaveData.cs` — add `pilotLicenseObtained` to `EquipmentSave`; (StoryDirector flags already round-trip, no schema change there).
- `SaveSystem/SaveCollector.cs` — capture/apply the pilot license; ensure building-lock state is asserted on apply.
- `Tutorial/NewGameReset.cs` — reset `PilotLicenseController`, building lock, and assert the M1 opening state on New Game.
- `NPC_Dialogue/TevDialogue.cs` — rewrite to the new flow (village intro that grants axe+pistol and sends the player to explore; no cabin talk).
- `UI/MainMenuController.cs` — seed any new auto-singleton in `EnsureGameplaySingletons` (trap #1) if we add one.

**Modified content (`Assets/StreamingAssets/Story/`):**
- `objectives.json` — remove/repurpose `obj_shelter`; add `obj_explore` / `obj_report` / mission objectives.
- `conv_gates.json` — remove the `gate_shelter` shelter beat; rewrite `gate_explore` to point at the village + Tev (no shelter prerequisite).
- `conv_menu.json` — drop the shelter help row and its `requiresFlag: hasShelter` gating on later rows.
- `hinttracks.json` — drop/replace the `shelter` track.

---

## Phasing & placement checkpoints (the human-in-the-loop spine)

Each phase is code/content I do solo, ending where a placement is needed. Consolidated placement asks:

- **CP-A (after Phase 2):** Place the 2–3 **Discoverable** trigger volumes in the start→village zone; place/duplicate **Tev at the vendor village** with `TevReportDialogue`; confirm/position the existing `VillageReachTrigger` at the village.
- **CP-B (after Phase 5 code):** Place the **flight-instructor NPC**, the **training drone** + its enter trigger, the **drill gates** (ordered), and the **landing pad** at/near the village.
- **CP-C (after Phase 6 code, optional):** Optionally place a visible **landing marker** on Constant Companion (arrival is code/proximity-based, so this is cosmetic).
- **CP-D (final):** Full playtest run.

---

## Task 1: Mission state scaffolding (`Mission1.cs`) + StoryDirector step enum

**Files:**
- Create: `Assets/3 - Scripts/Story/Mission1.cs`
- Modify: `Assets/3 - Scripts/Story/StoryDirector.cs:7-15` (enum), reset method, save note

- [ ] **Step 1:** Create `Mission1.cs` holding flag-name constants (`FlagExplored`, `FlagReported`, `FlagBranchChosen`, `FlagPilotStarted`, `FlagLicensed`, `FlagMission1Complete`), a `Branch { None, Pilot, Build, Fish }` enum stored as a StoryDirector flag/int, discoverable-id constants (`disc_vista`, `disc_structure`, `disc_fishing`), and static helpers `SeenCount()`, `MarkSeen(id)`, `SetBranch()`, `GetBranch()` that read/write via `StoryDirector.Instance`. All state lives in StoryDirector flags so it round-trips for free.
- [ ] **Step 2:** Add mission steps to `StoryStep` enum after `VillageSeam`: `MeetTevVillage`, `Explore`, `Report`, `Branching`, `PilotSchool`, `RealFlight`, `Mission1Complete`. (Append at end — do not renumber existing values; saves store the int.)
- [ ] **Step 3:** In `StoryDirector.ResetForNewGame()` no change needed for flags (cleared already); confirm `_step` resets to `ColdOpen`.
- [ ] **Step 4 (verify):** Editor compiles clean (Console shows no errors).

---

## Task 2: Restructure the opening — remove the shelter gate

**Files:**
- Modify: `Assets/3 - Scripts/Story/StoryDirector.cs` (`CheckGates`, `HandleBuildingPlaced`)
- Modify: `Assets/StreamingAssets/Story/objectives.json`, `conv_gates.json`, `conv_menu.json`, `hinttracks.json`

- [ ] **Step 1:** In `StoryDirector.CheckGates()`, change the `FirstContact/NeedsWaterFood` case so that once `hasWater && hasFood` it advances **straight to `Explore`** (skip `NeedsShelter`), starts `obj_village`, and queues `conv_gates` at `gate_explore`. Delete the `NeedsShelter` case. Leave `HandleBuildingPlaced` wired (harmless) but it no longer gates progression.
- [ ] **Step 2:** `objectives.json` — remove `obj_shelter`; keep `obj_water`, `obj_food`, `obj_village`.
- [ ] **Step 3:** `conv_gates.json` — delete `gate_shelter` and `gs_help`; rewrite `gate_explore` lines so the AI says (whisper-level ORG): *"You're keeping yourself alive — that's the hard part. Head out and find the main village; someone there took an interest in you. …there's something we'll need to track down eventually, but first, get your feet under you."*
- [ ] **Step 4:** `conv_menu.json` — remove the `m_shelter` node + its row; remove `requiresFlag: hasShelter` gating from the village/flashlight/map rows (gate on `hasWater` instead so they still unlock).
- [ ] **Step 5:** `hinttracks.json` — remove the `shelter` track.
- [ ] **Step 6:** Soft-gate building: at opening start, call `BuildMenuLock.LockAllExcept()` (empty list = nothing buildable). Do this from `NewGameReset` (Task 11) and assert it in `StoryDirector` on the gameplay scene load so a fresh run can't build. (Build branch, when later built, will `UnlockAll()`.)
- [ ] **Step 7 (verify):** Editor compiles; JSON parses (no Console parse errors on entering play); opening no longer demands a cabin.

---

## Task 3: Tev's village intro rewrite (`TevDialogue.cs`)

**Files:**
- Modify: `Assets/3 - Scripts/NPC_Dialogue/TevDialogue.cs`

- [ ] **Step 1:** Replace the stage logic. New stages keyed off StoryDirector flags + `Mission1`:
  - **Intro (not yet met at village):** "Found you in the wreck… took you in." Grants axe + pistol (keep `axeController.Unlock()` + `pistolController.Unlock()`), sets `EarlyGameProgress.TevReturnedDialogueDone`, sets step `MeetTevVillage`, then **hook line:** "Go have a look around, then come back and tell me what you found." Starts `obj_explore`.
  - **Mid-explore (explored < enough, not reported):** small-talk "find anything interesting yet?" lines.
  - **Report+fork ready:** this dialogue **defers to `TevReportDialogue`** (Task 6) — `TevDialogue` detects `FlagExplored` and hands off (or `TevReportDialogue` is the component actually on the village Tev; `TevDialogue` stays on the start-area Tev for the intro). Decide at wire time based on placement (CP-A).
  - **Post-fork done:** random done-lines.
- [ ] **Step 2:** Remove all cabin references (`waitingForCabinLines`, `villageLines` cabin framing). Repurpose `villageLines` → explore-hook lines.
- [ ] **Step 3 (verify):** Editor compiles.

---

## Task 4: Discoverable component (`Discoverable.cs`)

**Files:**
- Create: `Assets/3 - Scripts/Story/Discoverable.cs`

- [ ] **Step 1:** `Discoverable : MonoBehaviour` with `[SerializeField] string discoverableId;` `[TextArea] string observedLine;` and a `SphereCollider` (trigger). On first `OnTriggerEnter` with `Player`: `Mission1.MarkSeen(discoverableId)`, fire `HALCommentator.Instance.VolunteerExternal(observedLine)`, set `_fired` true (one-shot, mirrors `VillageReachTrigger`). Null-safe.
- [ ] **Step 2:** `Reset()` sets `collider.isTrigger = true`, radius default (e.g. 15).
- [ ] **Step 3 (verify):** Editor compiles.

---

## Task 5: Report + 3-way fork conversation (`TevReportDialogue.cs`)

**Files:**
- Create: `Assets/3 - Scripts/NPC_Dialogue/TevReportDialogue.cs`

- [ ] **Step 1:** Mirror `TevDialogue`'s in-world conversation shell (trigger, prompt, typewriter via `DialogueTextStyling.RevealCharsTMP`, `PlayerController.isInDialogue`, `NPCConversationTracker`). 
- [ ] **Step 2:** `PlayDialogueSequence`: greeting → **light report branch** using `Mission1.SeenCount()`/which ids (found the structure → line X; only saw the fishing spot → line Y; saw the vista → line Z; saw nothing → nudge to look closer and `yield break`). Sets `FlagReported`, step `Report`.
- [ ] **Step 3:** Show `PostGreetingChoicePanel` with 3 rows: `("Learn to pilot a ship", true)`, `("Build your own cabin", false)`, `("Go on a fishing trip", false)`. Capture index via callback field; `yield return new WaitUntil(...)`.
- [ ] **Step 4:** Route: index 0 → `Mission1.SetBranch(Pilot)`, step `Branching`→`PilotSchool`, `StoryDirector.AddTrust(small)`, speak a "head to the flight instructor" line + compass waypoint to instructor, start `obj_pilot_school`. Index 1 or 2 → "Let's save that for later — come back and pick the path you want." (disabled rows can't be picked, but keep the line for when they're enabled later).
- [ ] **Step 5 (verify):** Editor compiles.

🛑 **PLACEMENT CHECKPOINT CP-A** — I will stop here and ask you to:
1. Place 2–3 `Discoverable` trigger volumes (vista / strange structure / fishing spot) between the start and the village, each with an id + observed line.
2. Put **Tev at the vendor village** with the `TevReportDialogue` component (and decide whether the start-area Tev keeps `TevDialogue` for the intro, or it's all one Tev — depends on your scene).
3. Confirm the `VillageReachTrigger` sits at the vendor village.
Then I wire the references and continue.

---

## Task 6: Pilot license (`PilotLicenseController.cs`) + Ship gate + save

**Files:**
- Create: `Assets/3 - Scripts/Ship/PilotLicenseController.cs`
- Modify: `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs` (`TogglePiloting`)
- Modify: `Assets/3 - Scripts/SaveSystem/SaveData.cs` (`EquipmentSave`)
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` (`CaptureEquipment`/`ApplyEquipment`)

- [ ] **Step 1:** `PilotLicenseController` on the Player: `bool _obtained; public bool IsObtained => _obtained; public void Grant(){ _obtained = true; }` + `public static PilotLicenseController Instance` set in Awake (or a static `Obtained` mirror) so `Ship` can check without a per-frame `FindObjectOfType`.
- [ ] **Step 2:** In `Ship.TogglePiloting()`, after the `EnterPilot` gate and before the power check, add: if not licensed → `GameUI.DisplayInteractionInfo("You need a pilot's license — finish drone school with the instructor");` `return;`.
- [ ] **Step 3:** Add `public bool pilotLicenseObtained;` to end of `EquipmentSave`.
- [ ] **Step 4:** `CaptureEquipment` → set from `PilotLicenseController`; `ApplyEquipment` → if true, `Grant()`.
- [ ] **Step 5 (verify):** Editor compiles; boarding the real ship is now refused with the license message (quick play check by author later).

---

## Task 7: Training drone (`DroneController.cs`)

**Files:**
- Create: `Assets/3 - Scripts/Ship/DroneController.cs`

- [ ] **Step 1:** New lightweight controller copying `Ship`'s flight feel: read pitch/yaw/roll + thrust axes (reuse `InputSettings` + `TutorialGate` axis reads), apply gravity via `NBodySimulation.CalculateAcceleration` + thrust via `rb.AddForce` in `FixedUpdate`. No hatch/boost/assists/power-fuel persistence (optionally a simple unlimited or generous fuel for the lesson).
- [ ] **Step 2:** Enter/exit: an interact trigger (or call from `FlightInstructorDialogue`) reparents the player camera to the drone's view point and disables player movement (mirror `Ship.PilotShip`/`StopPilotingShip` minimal subset); exit returns control.
- [ ] **Step 3:** In `Awake`, `EndlessManager.Instance?.RegisterPhysicsObject(transform)` (floating-origin requirement).
- [ ] **Step 4:** Expose `OnExited`/state so the course knows when the player is flying.
- [ ] **Step 5 (verify):** Editor compiles.

---

## Task 8: Drill course (`DroneGate.cs`, `DroneSchoolCourse.cs`)

**Files:**
- Create: `Assets/3 - Scripts/Ship/DroneGate.cs`
- Create: `Assets/3 - Scripts/Ship/DroneSchoolCourse.cs`

- [ ] **Step 1:** `DroneGate`: trigger volume; on drone pass-through (detect by `DroneController` tag/component on `other`) calls `course.NotifyGatePassed(this)`. Visual hint optional. One-shot per run.
- [ ] **Step 2:** `DroneSchoolCourse`: ordered `List<DroneGate>` + a landing-pad trigger. Drills = takeoff (leave pad) → pass gates in order → land on pad. Tracks progress, supports **retry without death** (if out-of-order or missed, prompt + reset current leg). On finishing the landing: `PilotLicenseController.Instance.Grant()`, set `Mission1.FlagLicensed`, step `RealFlight`, `StoryDirector.AddTrust(...)`, HAL/instructor "you passed — galactic license granted" beat, set compass waypoint to the real ship.
- [ ] **Step 3:** Show progress to the player (reuse `GameUI.DisplayInteractionInfo` or the hint UI: "Gate 2 / 4").
- [ ] **Step 4 (verify):** Editor compiles.

---

## Task 9: Flight-instructor NPC (`FlightInstructorDialogue.cs`)

**Files:**
- Create: `Assets/3 - Scripts/NPC_Dialogue/FlightInstructorDialogue.cs`

- [ ] **Step 1:** In-world dialogue shell (mirror `TevDialogue`). Intro briefing → "climb into the drone and fly the course" → starts/links the `DroneSchoolCourse` and tells the player to board the drone. Reacts to pass with a congrats line (or the course handles the grant and the instructor just confirms).
- [ ] **Step 2 (verify):** Editor compiles.

🛑 **PLACEMENT CHECKPOINT CP-B** — I will stop and ask you to place: the **flight-instructor NPC** (with `FlightInstructorDialogue`), the **training drone** (with `DroneController` + enter trigger), the **drill gates** in order (with `DroneGate`, assigned to the course in order), and the **landing pad** trigger — all at/near the village. Then I wire references on `DroneSchoolCourse`.

---

## Task 10: Real flight → Constant Companion arrival → M2 hand-off

**Files:**
- Create: `Assets/3 - Scripts/Ship/ConstantCompanionArrival.cs`
- Modify: `Assets/3 - Scripts/NPC_Dialogue/TevDialogue.cs` (departure bond line) or a radio beat

- [ ] **Step 1:** `ConstantCompanionArrival` (auto-singleton or scene object): once `Mission1.FlagLicensed` and the player is piloting the real ship, poll (throttled, NOT every frame; cache the body) distance to the `Constant Companion` `CelestialBody` via `NBodySimulation.Bodies` + `Ship.IsLanded`. On landing/within surface threshold → set `Mission1.FlagMission1Complete`, step `Mission1Complete`, `StoryDirector.AddTrust(...)`, fire the **Mission 2 hand-off** (queue an M2-intro conversation / set the seam flag the future M2 reads). One-shot.
- [ ] **Step 2:** Departure bond beat: a short Tev radio/cabin line at lift-off ("first time off the ground — you'll be fine"). Trigger when the player boards the licensed ship.
- [ ] **Step 3:** If `ConstantCompanionArrival` is an auto-singleton, seed it in `MainMenuController.EnsureGameplaySingletons` (trap #1).
- [ ] **Step 4 (verify):** Editor compiles.

🛑 **PLACEMENT CHECKPOINT CP-C (optional)** — optionally place a cosmetic landing marker on Constant Companion. Arrival itself is proximity-based, so this is not required.

---

## Task 11: New-Game reset + save assertions + singleton seeding

**Files:**
- Modify: `Assets/3 - Scripts/Tutorial/NewGameReset.cs`
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs` (if new singletons added)

- [ ] **Step 1:** In `NewGameReset.Apply()`: reset `PilotLicenseController` (set `_obtained=false` / static mirror), call `BuildMenuLock.LockAllExcept()` (lock building for the slice), and ensure `StoryDirector.ResetForNewGame()` already runs (it does via its own reset path — confirm M1 flags clear).
- [ ] **Step 2:** In `SaveCollector.ApplyEquipment`/load path, re-assert `BuildMenuLock` state for the slice (building stays locked unless a future Build-branch flag says otherwise).
- [ ] **Step 3:** Seed any new auto-singleton (e.g. `ConstantCompanionArrival`) in `EnsureGameplaySingletons` (trap #1).
- [ ] **Step 4 (verify):** Editor compiles; New Game starts with no buildable items and no license.

---

## Task 12: Phone AI integration pass (mostly verification)

**Files:** none new — confirm existing `HALCommentator` tips fire; add at most one soft ORG whisper line (already added in `conv_gates` Task 2 Step 3).

- [ ] **Step 1:** Confirm low-health / enemy-nearby / atmosphere tips + Astronaut-N death line are live (they are per audit) — no code needed.
- [ ] **Step 2:** Keep `EarlyGameProgress.ORG_Reveal = false` for M1 (no change).
- [ ] **Step 3 (verify):** Author playtest notes the whisper line reads right.

---

## Task 13: Full slice playtest (gate)

🛑 **PLACEMENT CHECKPOINT CP-D — author playtest.** End-to-end success condition (GDD §6.4):
crash → survive (water+food, no shelter, can't build) → trek → village → meet Tev → explore 2–3 discoverables → return & report (branches on what was seen) → choose **Pilot** (Build/Fish disabled with "later" line) → instructor → drone drills (gates + landing, retry-on-fail) → **galactic license granted** → board real ship (was refused before license) → fly Humble Abode → Constant Companion (seamless) → **Mission 2 trigger fires** → Tev trust raised.

---

## Self-review notes
- **Spec coverage:** §2 (Task 2 soft-gate + opening), §3 (Task 3 Tev intro), §4 (Tasks 4–5 discoverables + report), §5 (Task 5 fork menu + stubs), §6.1 (Tasks 7–9 drone school), §6.2 (Tasks 6,8 license gate), §6.3 (Task 10 real flight + arrival + bond beat), §6.4 (Tasks 8,10 trust + end-to-end), §7–§8 stubs (Task 5 disabled rows), §10 checklist mapped, §11 assumptions resolved by user.
- **Risk flags:** `Ship.cs` and the save apply-order are fragile (CLAUDE.md traps #1/#3) — license gate is a minimal additive insert; save field appended at end of `EquipmentSave`. **Atmosphere/celestial generation code is forbidden (trap #2)** — Constant Companion arrival only *reads* `CelestialBody.Position`/`bodyName` (allowed), never touches generation/shading.
- **Biggest unknown:** `DroneController` flight-math copy (Task 7) — if reuse proves messy at execution time, fallback is to spawn a real `Ship` instance configured as a trainer; will flag if so.
