# Cleanup Pass — Phase 2 (Dead Code) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove confirmed-dead code — orphaned runtime scripts, one-shot editor scaffolding, and obsolete tools — without breaking the Editor compile or the build.

**Architecture:** Pure deletion + one file reorganization. Each task is independently revertable. Every deletion is gated on a grep that confirms the type is not referenced by any other `.cs` file.

**Tech Stack:** Unity 2022.3, C#, no asmdefs. Verification is manual in the Unity Editor.

**Source spec:** `docs/superpowers/specs/2026-05-13-cleanup-pass-design.md` (Phase 2)

**Deferred from this plan:** Phase 2b (ResourceHUD retirement) — requires editing `1.6.7.7.7.unity` as text to remove the legacy HUD's bar GameObjects, which share a canvas with live gameplay UI. The current `VitalsHUD.DisableLegacyResourceHUD` workaround is cheap, idempotent, and correct. Proper removal is deferred to a focused session with the Editor open.

---

## Important context for all tasks

- Repo root / working dir: `C:\123\1aughhh1`. Master branch.
- No automated tests. No CLI build. Compile verification is the user's job at the end.
- **Deleting a `.cs` file orphans its GUID.** Unity shows a "missing script" warning wherever that GUID was referenced in a `.unity`/`.prefab`/`.asset`. This is harmless for scenes/prefabs that are disabled-in-build or archived, and for prefabs nothing instantiates at runtime — but it must be acknowledged per deletion.
- When deleting a file, delete BOTH the `.cs` and its `.cs.meta`.
- Use `git rm` (not raw `rm`) so the deletion is staged cleanly.

---

### Task 1: Delete 4 dead runtime scripts

**Files to delete:**
- `Assets/3 - Scripts/Fishing/FishingdexEntryUI.cs` (+ `.meta`)
- `Assets/3 - Scripts/Player/PlayerLeash.cs` (+ `.meta`)
- `Assets/3 - Scripts/Camera/CustomLensFlare.cs` (+ `.meta`)
- `Assets/3 - Scripts/Camera/FlareCameraSetup.cs` (+ `.meta`)

- [ ] **Step 1: Verify deadness**

Run from `C:\123\1aughhh1`:
```bash
grep -rl "FishingdexEntryUI\|PlayerLeash\|CustomLensFlare\|FlareCameraSetup" --include="*.cs" "Assets/"
```

Expected output: ONLY the four files themselves (each references its own class name) and possibly `Assets/3 - Scripts/Editor/SetupFishingdex.cs` (which references `FishingdexEntryUI` — that editor script is itself deleted in Task 3, so this reference is acceptable; it'll go away).

If ANY OTHER `.cs` file references these types, STOP and report — the file is not dead.

- [ ] **Step 2: Note the orphaned GUID references (informational, no action)**

These deletions orphan GUIDs in:
- `Assets/1 - samsPrefabs/FishEntryItem.prefab` — FishingdexEntryUI (this prefab is deleted in Task 3)
- `Assets/4 - Scenes/Flashback1.unity` — PlayerLeash + FlareCameraSetup (cinematic scene, disabled in build settings)
- `Assets/4 - Scenes/_Archive/1.7.unity` — CustomLensFlare (archived scene, not in build)

All are out-of-build or deleted-this-phase. The missing-script warnings only appear if those specific archived/disabled scenes are opened in the Editor. Acceptable.

- [ ] **Step 3: Delete the files**

```bash
git rm "Assets/3 - Scripts/Fishing/FishingdexEntryUI.cs" "Assets/3 - Scripts/Fishing/FishingdexEntryUI.cs.meta"
git rm "Assets/3 - Scripts/Player/PlayerLeash.cs" "Assets/3 - Scripts/Player/PlayerLeash.cs.meta"
git rm "Assets/3 - Scripts/Camera/CustomLensFlare.cs" "Assets/3 - Scripts/Camera/CustomLensFlare.cs.meta"
git rm "Assets/3 - Scripts/Camera/FlareCameraSetup.cs" "Assets/3 - Scripts/Camera/FlareCameraSetup.cs.meta"
```

- [ ] **Step 4: Commit**

```bash
git commit -m "$(cat <<'EOF'
chore(cleanup): delete 4 dead runtime scripts

FishingdexEntryUI (Fishingdex builds entries procedurally now),
PlayerLeash (only in disabled Flashback1.unity), CustomLensFlare and
FlareCameraSetup (only in archived 1.7.unity / disabled Flashback1).
None are referenced by live code. Orphaned GUID refs are confined to
out-of-build/archived scenes.

Audit ref: Cross-16/17/18/19, HUD-9.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 5: Verify**

Run `git show --stat HEAD` — confirm exactly 8 files deleted (4 `.cs` + 4 `.meta`).

---

### Task 2: Delete confirmed-dead one-shot editor scripts

These editor scripts have no `[MenuItem]` and were flagged by the audit as one-shot scaffolding whose output is already baked into the scene asset. Editor scripts are NOT in the build, so the only risk is breaking the Editor compile if one references another.

**Candidate files (audit-confirmed one-shots):**
- `Assets/3 - Scripts/Editor/AddEventSystem.cs`
- `Assets/3 - Scripts/Editor/DisableBoostMeters.cs`
- `Assets/3 - Scripts/Editor/DoubleParticleRate.cs`
- `Assets/3 - Scripts/Editor/DuplicateTorchParticles.cs`
- `Assets/3 - Scripts/Editor/FixBonfireAndBottleHold.cs`
- `Assets/3 - Scripts/Editor/FixTorchFade.cs`
- `Assets/3 - Scripts/Editor/RewireBonfireRefs.cs`
- `Assets/3 - Scripts/Editor/SetAlien3DialogueLines.cs`
- `Assets/3 - Scripts/Editor/SetupCookAndWaterUI.cs`
- `Assets/3 - Scripts/Editor/SetupCutsceneUI.cs`
- `Assets/3 - Scripts/Editor/SetupResourceSystem.cs`
- `Assets/3 - Scripts/Editor/SetupSellPanelUI.cs`
- `Assets/3 - Scripts/Editor/WireBonfireAndWaterBottle.cs`
- `Assets/3 - Scripts/Editor/WireBoostMeter.cs`
- `Assets/3 - Scripts/Editor/WireFishingdexBrowseButtons.cs`
- `Assets/3 - Scripts/Editor/WireORGDialogue.cs`

- [ ] **Step 1: Per-file cross-reference verification**

For EACH candidate file, extract its class name(s) and grep the whole `Assets/` tree (excluding the file itself) for references. Run this verification script from `C:\123\1aughhh1`:

```bash
for f in AddEventSystem DisableBoostMeters DoubleParticleRate DuplicateTorchParticles FixBonfireAndBottleHold FixTorchFade RewireBonfireRefs SetAlien3DialogueLines SetupCookAndWaterUI SetupCutsceneUI SetupResourceSystem SetupSellPanelUI WireBonfireAndWaterBottle WireBoostMeter WireFishingdexBrowseButtons WireORGDialogue; do
  hits=$(grep -rl "\b$f\b" --include="*.cs" "Assets/" | grep -v "Editor/$f.cs")
  if [ -n "$hits" ]; then echo "KEEP $f — referenced by: $hits"; else echo "DEAD $f"; fi
done
```

Any file reported as `KEEP` must be EXCLUDED from deletion — report it in the final report. Only delete files reported `DEAD`.

Also confirm each candidate genuinely has no `[MenuItem]`:
```bash
grep -L "MenuItem" Assets/3\ -\ Scripts/Editor/AddEventSystem.cs Assets/3\ -\ Scripts/Editor/DisableBoostMeters.cs # ... (sanity check a few)
```

- [ ] **Step 2: Delete the DEAD files (and their .meta)**

For each file confirmed `DEAD` in Step 1, `git rm` both the `.cs` and `.cs.meta`. Example:
```bash
git rm "Assets/3 - Scripts/Editor/AddEventSystem.cs" "Assets/3 - Scripts/Editor/AddEventSystem.cs.meta"
```
Repeat for every `DEAD` file. Do NOT delete any `KEEP` file.

- [ ] **Step 3: Commit**

```bash
git commit -m "$(cat <<'EOF'
chore(cleanup): delete one-shot editor scaffolding scripts

These editor scripts have no MenuItem and were one-shot scene-mutation
tools whose output is already baked into 1.6.7.7.7.unity. Each was
verified to have zero cross-references from other .cs files before
deletion. Editor scripts are not in the build — no runtime impact.

Audit ref: Editor-1.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: Report**

In the implementer report, list which files were deleted (`DEAD`) and which were kept (`KEEP`) with the reason.

---

### Task 3: Delete obsolete MenuItem editor tools + the dead FishEntryItem prefab

These have `[MenuItem]` but the audit flagged them as obsolete or footguns. Plus the dead `FishEntryItem.prefab` that only `SetupFishingdex` touched.

**Files to delete:**
- `Assets/3 - Scripts/Editor/CreateEnemyPrefab.cs` (+ `.meta`) — superseded by `BuildToyEnemyPrefab`/`BuildToy3EnemyPrefab`; would clobber `Enemy.prefab` if clicked
- `Assets/3 - Scripts/Editor/ViewDistanceSliderInstaller.cs` (+ `.meta`) — its own cleanup script says it's superseded by `GalaxyPauseMenuStyler.BuildViewDistanceRow`
- `Assets/3 - Scripts/Editor/SetupTorchFire.cs` (+ `.meta`) — predecessor of `RebuildTorchFire`; crashes on second run
- `Assets/3 - Scripts/Editor/SetupFishingdex.cs` (+ `.meta`) — Fishingdex is procedural at runtime now; this rebuilds the dead `FishingdexEntryUI` pattern
- `Assets/1 - samsPrefabs/FishEntryItem.prefab` (+ `.meta`) — only referenced by `SetupFishingdex` (deleted here) and contains the dead `FishingdexEntryUI` component

- [ ] **Step 1: Cross-reference verification**

From `C:\123\1aughhh1`:
```bash
for f in CreateEnemyPrefab ViewDistanceSliderInstaller SetupTorchFire SetupFishingdex; do
  hits=$(grep -rl "\b$f\b" --include="*.cs" "Assets/" | grep -v "Editor/$f.cs")
  if [ -n "$hits" ]; then echo "KEEP $f — referenced by: $hits"; else echo "DEAD $f"; fi
done
```

If any reports `KEEP`, exclude it and report. (Note: `ViewDistanceSliderCleanup.cs` mentions `ViewDistanceSliderInstaller` in a *comment* — a comment match is fine, the type isn't *used*. If the grep flags it, manually confirm it's only a comment reference and proceed.)

Also confirm `FishEntryItem.prefab` is referenced only by `SetupFishingdex`:
```bash
grep -rl "FishEntryItem" --include="*.cs" "Assets/"
```
Expected: only `Editor/SetupFishingdex.cs` (being deleted) and possibly `Editor/SetupFishingdex.cs` alone. If `FishingdexManager.cs` or any runtime script references it, STOP and report.

- [ ] **Step 2: Delete the files**

```bash
git rm "Assets/3 - Scripts/Editor/CreateEnemyPrefab.cs" "Assets/3 - Scripts/Editor/CreateEnemyPrefab.cs.meta"
git rm "Assets/3 - Scripts/Editor/ViewDistanceSliderInstaller.cs" "Assets/3 - Scripts/Editor/ViewDistanceSliderInstaller.cs.meta"
git rm "Assets/3 - Scripts/Editor/SetupTorchFire.cs" "Assets/3 - Scripts/Editor/SetupTorchFire.cs.meta"
git rm "Assets/3 - Scripts/Editor/SetupFishingdex.cs" "Assets/3 - Scripts/Editor/SetupFishingdex.cs.meta"
git rm "Assets/1 - samsPrefabs/FishEntryItem.prefab" "Assets/1 - samsPrefabs/FishEntryItem.prefab.meta"
```

- [ ] **Step 3: Update CLAUDE.md pinned-path table**

`CLAUDE.md` has a "Pinned-path files" table. Two rows reference now-deleted files:
- `Assets/1 - samsPrefabs/FishEntryItem.prefab` | `Editor/SetupFishingdex.cs:262`
- `Assets/5 - External Imports/fishing-rod/source/.../fishingrod.obj` | `Editor/FixFishingRodPrefab.cs:9` — KEEP this row, `FixFishingRodPrefab` is not deleted
- The `Assets/2 - Materials/Torch/TorchMat.mat` | `Editor/SetupTorchFire.cs:192` row — `SetupTorchFire` is deleted, but `RebuildTorchFire.cs` also uses `TorchMat.mat`. Update the reference from `Editor/SetupTorchFire.cs:192` to `Editor/RebuildTorchFire.cs` (grep `RebuildTorchFire.cs` for the exact `TorchMat.mat` line number).

Use the Edit tool on `C:\123\1aughhh1\CLAUDE.md`:
- DELETE the `FishEntryItem.prefab` row entirely (file no longer exists).
- UPDATE the `TorchMat.mat` row's "Referenced from" cell to point at `RebuildTorchFire.cs:<line>` instead of `SetupTorchFire.cs:192`.

Read the current "Pinned-path files" table first to get exact text.

- [ ] **Step 4: Commit**

```bash
git commit -m "$(cat <<'EOF'
chore(cleanup): delete obsolete editor tools + dead FishEntryItem prefab

CreateEnemyPrefab (superseded by BuildToy*EnemyPrefab, clobbers
Enemy.prefab on click), ViewDistanceSliderInstaller (superseded by
GalaxyPauseMenuStyler per its own cleanup script), SetupTorchFire
(predecessor of RebuildTorchFire, crashes on rerun), SetupFishingdex
+ FishEntryItem.prefab (Fishingdex is procedural now). CLAUDE.md
pinned-path table updated: FishEntryItem row removed, TorchMat.mat row
repointed to RebuildTorchFire.

Audit ref: Editor-2/4/5/8.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 5: Report** which files were deleted and the CLAUDE.md edits made.

---

### Task 4: Move dead TutorialStep classes to a legacy file

`Assets/3 - Scripts/Tutorial/TutorialSteps.cs` is 1708 lines and contains ~25 `TutorialStep` subclasses that are never instantiated by `BuildDefault()` or `BonusTutorial` — they're kept only because `TutorialManager.ApplyState` resolves saved steps by type name. Moving them (NOT deleting — type names must survive for save compatibility) to `_LegacySteps.cs` keeps the active flow file focused.

**Files:**
- Modify: `Assets/3 - Scripts/Tutorial/TutorialSteps.cs` (cut dead classes out)
- Create: `Assets/3 - Scripts/Tutorial/_LegacySteps.cs` (paste dead classes in)

- [ ] **Step 1: Read TutorialSteps.cs fully and identify the dead classes**

Read `Assets/3 - Scripts/Tutorial/TutorialSteps.cs` end to end. A class is DEAD if its name appears nowhere except its own definition. For each `class XxxStep : TutorialStep` (or `: BonusStep`), grep:
```bash
grep -rn "\bXxxStep\b" --include="*.cs" "Assets/3 - Scripts/Tutorial/" "Assets/3 - Scripts/SaveSystem/"
```
A class is dead if the ONLY hits are inside `TutorialSteps.cs` itself (its definition). If it's named in `TutorialManager.cs`, `BonusTutorial.cs`, or any `BuildDefault`/factory list, it is LIVE — keep it in place.

The audit's candidate dead list (verify each — do NOT trust this list blindly, the codebase may have changed):
`PostCrashExamStep`, `StandUpStep`, `MouseLookStep`, `HatchStep`, `BoostStep`, `DirectionalThrustStep`, `DownThrustStep`, `FlashlightStep`, `MapStep`, `RepairShipStep`, `LebronLightStep`, `BackHatchStep`, `TalkToNPCsStep`, `PilotShipStep`, `ShipUpThrustStep`, `ShipMoveStep`, `ShipDownThrustStep`, `ShipRollStep`, `WakeUpLookStep`, `WakeUpWalkStep`, `CatchFirstFishStep`, `CatchFiveFishStep`, `WalkToFireStep`, `OpenCookPanelStep`, `MainSwingAxeStep`, `MainGatherWoodStep`, `OpenBuildMenuStep`, `MainBuildCabinStep`.

**CRITICAL:** Some of these names are very similar to LIVE classes (e.g. there may be both a dead `MainSwingAxeStep` and a live `SwingAxeStep`). Verify each individually. When in doubt, KEEP IT IN PLACE — a too-long file is harmless; a broken move is not.

- [ ] **Step 2: Create `_LegacySteps.cs` with the confirmed-dead classes**

Create `Assets/3 - Scripts/Tutorial/_LegacySteps.cs`. It needs:
- The SAME `using` directives that `TutorialSteps.cs` has at its top (copy them verbatim — read the top of TutorialSteps.cs).
- A file header comment:
```csharp
// Legacy TutorialStep subclasses — no longer instantiated by BuildDefault()
// or BonusTutorial, but kept because TutorialManager.ApplyState resolves
// saved tutorial progress by type name. Moving them here keeps the active
// TutorialSteps.cs focused. Do NOT rename or delete these types — old save
// files reference them by name.
```
- The full text of each confirmed-dead class, cut verbatim from `TutorialSteps.cs` (including any XML doc comments / attributes immediately above each class).

- [ ] **Step 3: Remove the moved classes from `TutorialSteps.cs`**

Delete each moved class's full body from `TutorialSteps.cs`. Be meticulous about brace balance — after the cuts, the file must still be syntactically valid. Verify by counting: the number of `class ` declarations remaining + the number moved == the original count.

- [ ] **Step 4: Verify brace integrity**

Run from `C:\123\1aughhh1`:
```bash
python3 -c "
for f in ['Assets/3 - Scripts/Tutorial/TutorialSteps.cs','Assets/3 - Scripts/Tutorial/_LegacySteps.cs']:
    s=open(f,encoding='utf-8').read()
    print(f, 'braces balanced:' , s.count('{')==s.count('}'), s.count('{'), s.count('}'))
"
```
Both files must report balanced braces. (This is a crude check — it can be fooled by braces in strings/comments — but a mismatch is a definite red flag.)

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Tutorial/TutorialSteps.cs" "Assets/3 - Scripts/Tutorial/_LegacySteps.cs"
git commit -m "$(cat <<'EOF'
refactor(tutorial): move dead TutorialStep classes to _LegacySteps.cs

~25 step subclasses are never instantiated by BuildDefault or
BonusTutorial — kept only so TutorialManager.ApplyState can resolve
old saves by type name. Moved verbatim (NOT renamed/deleted) to a
dedicated legacy file so the active TutorialSteps.cs stays focused.

Audit ref: Tutorial-2.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 6: Report** the exact list of classes moved, and the line count of `TutorialSteps.cs` before and after.

---

## Self-Review Notes

- **Spec coverage**: Phase 2 of the spec had groups 2a (runtime dead code → Task 1), 2b (ResourceHUD → DEFERRED, documented above), 2c (editor scripts → Tasks 2+3), 2d (dead TutorialSteps → Task 4). 2b deferral is explicit and justified.
- **Placeholder scan**: No TBDs. Tasks 2 and 4 have a verification *gate* (grep) rather than a hardcoded file list because the exact dead set must be confirmed at implementation time — this is a deliberate safety mechanism, not a placeholder.
- **Risk**: Every deletion is grep-gated. The riskiest task is 4 (cut-paste 25 classes from a 1708-line file) — mitigated by the brace-balance check and the "when in doubt keep in place" rule.
- **Type consistency**: `_LegacySteps.cs` uses the same `using` directives and namespace (global) as `TutorialSteps.cs` — Step 2 explicitly says copy them verbatim.
