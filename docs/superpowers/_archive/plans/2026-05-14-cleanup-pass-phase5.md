# Cleanup Pass — Phase 5 (Save-System Polish) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Tighten the save system's documentation accuracy and surface silent failures, plus two small correctness fixes — all low-risk, none touching the fragile Apply *order*.

**Architecture:** Five independent, well-bounded edits. No Apply-order changes (CLAUDE.md explicitly calls that fragile). No structural refactors.

**Tech Stack:** Unity 2022.3, C#, no asmdefs. No CLI build — compile verification is the user's at the end.

**Source spec:** `docs/superpowers/specs/2026-05-13-cleanup-pass-design.md` (Phase 5)

## Scope note — what Phase 5 does and does NOT include

The design doc's Phase 5 listed 10 items. This plan does 5 genuinely-low-risk ones:
- #1 Doc sync (Apply-order comment + CLAUDE.md)
- #4 Persist `BonfireNPCDialogue._firstTimeDone` (additive JSON schema — worst case is no worse than today's bug)
- #6 Warn on silently-skipped save entries
- #9 Unregister extra ships from EndlessManager before destroy
- #10 Pre-placed alien kill loop: O(N·M) → HashSet single pass

**Deferred** (need Unity-open verification):
- #2 Move `ApplyEquipment` after `ApplyShipDamage` — CLAUDE.md explicitly says the Apply order is "fragile and documented inline"; reordering needs incremental verification.
- #3 Extra-ships `MarkIntroComplete` — speculative; depends on whether extras can be damaged.
- #7 Hotbar eviction → `ForceUnequip` — `Hotbar.cs` was modified 3× this pass; eviction-unequip has interaction effects worth verifying live.
- #8 `BonusTutorial` table-driven refactor — structural refactor of a 969-line save-touching file.

**Skipped entirely:**
- #5 `NPCConversationTracker.NotifyStart` in ORG/Interrogation/Bonfire dialogue — its only consumer, `TalkToNPCsStep`, was moved to `_LegacySteps.cs` (dead code) in Phase 2. The fix has no live effect.

---

## Important context for all tasks

- Working dir: `C:\123\1aughhh1`. Master branch.
- The real current `SaveCollector.Apply()` call order (verified) is:
  `ApplyCelestialBodies → ApplyTutorial → ApplyNPCs → ApplyEarlyGame → ApplyNotes → ApplyBuildMenuLock → ApplyCompass → ApplyResources → ApplyWallet → ApplyWood → ApplyFishInventory → ApplyEquipment → ApplyWorldFlags → ApplyShipDamage → ApplyShipTransform → ApplyExtraShips → ApplyPlayerTransform → ApplyBuildings → ApplyLooseParts → ApplyEnemies → ApplyHeldItem → ApplyCassette → ApplyFlashlight → ApplyBonusTutorial → ApplyMapTutorial → ApplyAlienKills`

---

### Task 1: Sync the Apply-order documentation with reality

The inline comment block at the top of `SaveCollector.Apply()` lists only 9 numbered steps, but the real method has 26 calls. The `CLAUDE.md` "Apply order" section is also stale (missing `ApplyEarlyGame`, `ApplyNotes`, `ApplyBuildMenuLock`, `ApplyCompass`, `ApplyExtraShips`, `ApplyMapTutorial`, `ApplyAlienKills`). Also `CLAUDE.md`'s "Currently NOT saved" list contradicts itself: it says "Combat state (enemies respawn fresh)" while elsewhere the doc correctly states "Active enemies ARE saved."

**Files:**
- `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` (the comment block inside `Apply()`, ~lines 455-466)
- `CLAUDE.md` ("Apply order" section + "Currently NOT saved" list)

- [ ] **Step 1: Read the current state**

Read `SaveCollector.cs` lines 451-510 (the full `Apply()` method) to get the EXACT current inline comment text. Read the `CLAUDE.md` "### Apply order (in `SaveCollector.Apply`)" section and the "**Currently NOT saved:**" bullet list to get their exact current text.

- [ ] **Step 2: Rewrite the inline comment in `SaveCollector.Apply()`**

Replace the existing `// Apply order matters:` comment block (the ~12-line block before `ApplyCelestialBodies(...)`) with an accurate one. Use the Edit tool. The new comment should list the real grouped order and keep the load-bearing rationale notes that already exist (the `ApplyEarlyGame` "must run before any apply that reads these flags" note, the `ApplyExtraShips` "before player apply" note). Write it as:

```csharp
        // Apply order matters — this is the real call sequence:
        //   1. Celestial bodies — restore orbital state first; everything
        //      body-relative below resolves world position from these.
        //   2. Tutorial — suppresses the auto-start-on-collision.
        //   3. NPCs — marks dialogue completion so prompts don't reappear.
        //   4. EarlyGame — static-singleton progress flags; must run before
        //      any later apply that READS these flags.
        //   5. Notes / BuildMenuLock / Compass — UI/progress singletons.
        //   6. Resources / Wallet / Wood / FishInventory / Equipment /
        //      WorldFlags — singleton state.
        //   7. ShipDamage — synchronous prefab swap; may replace the ship.
        //   8. ShipTransform — after the damage swap (positions the new rb).
        //   9. ExtraShips — spawned before the player apply so a saved
        //      isPiloted=true extra exists when player placement reads it.
        //  10. PlayerTransform — after ship damage so the player isn't
        //      positioned relative to a ship about to be destroyed.
        //  11. Buildings / LooseParts — re-spawned body-relative content.
        //  12. Enemies — independent state; bodies already restored.
        //  13. HeldItem — last among gameplay; needs PlayerPickup intact.
        //  14. Cassette / Flashlight / BonusTutorial / MapTutorial /
        //      AlienKills — final touch-ups.
```
(If the existing comment's exact indentation/format differs, match the file's style — the content above is what matters.)

- [ ] **Step 3: Rewrite the CLAUDE.md "Apply order" section**

In `CLAUDE.md`, the "### Apply order (in `SaveCollector.Apply`)" section currently has an 11-item numbered list that's missing 7 calls and has a stale order. Replace the numbered list with one that matches the real 26-call sequence (grouped sensibly — you can keep the grouped structure, but every `ApplyXxx` must be accounted for and in the right relative position). Keep the existing "This order matters; breaking it causes regressions" intro line and the per-step rationale prose where it's still accurate. Use the real order from the "Important context" section above as the source of truth.

- [ ] **Step 4: Fix the CLAUDE.md "Currently NOT saved" contradiction**

In `CLAUDE.md`, the "**Currently NOT saved:**" list has a bullet `- Combat state (enemies respawn fresh)`. This contradicts the (correct) statement elsewhere in the doc that active enemies ARE saved. Replace that bullet with:
```
- Transient combat state (knockback velocity/timer, charge phase, mid-spit attack, ragdolling enemies, the tree-episode timer) — active enemies themselves ARE saved
```

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/SaveSystem/SaveCollector.cs" CLAUDE.md
git commit -m "$(cat <<'EOF'
docs(save): sync Apply-order documentation with the real call sequence

The inline comment in SaveCollector.Apply listed 9 steps but the method
has 26 calls; CLAUDE.md's Apply-order section was missing 7 of them.
Also fixed CLAUDE.md's self-contradiction — "Combat state (enemies
respawn fresh)" vs the correct "active enemies ARE saved". No code
behavior change.

Audit ref: Save-1, Save-13.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 6: Report** — confirm the inline comment and both CLAUDE.md sections were updated; no code logic changed (only the comment block in SaveCollector.cs).

---

### Task 2: Warn on silently-skipped save entries

`ApplyLooseParts`, `ApplyExtraShips`, and `ApplyBuildings` each `continue` past a save entry when a prefab/body/enum can't be resolved — silently losing the player's content. Add `Debug.LogWarning` so this surfaces during testing. (`ApplyBuildings` already has ONE warning for the no-cookPanel case from a prior commit — only the two `continue` points there are silent.)

**File:** `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`

- [ ] **Step 1: ApplyLooseParts — prefab-null skip**

Around line 933, find:
```csharp
            var prefab = ResolvePartPrefab(save.partKind, dmg, detach);
            if (prefab == null) continue;
```
Change to:
```csharp
            var prefab = ResolvePartPrefab(save.partKind, dmg, detach);
            if (prefab == null)
            {
                Debug.LogWarning($"[SaveCollector] ApplyLooseParts: skipping unknown part kind '{save.partKind}' — prefab not found.");
                continue;
            }
```

- [ ] **Step 2: ApplyExtraShips — two silent skips**

Around lines 787-790, find:
```csharp
            if (entry == null) continue;
            if (!System.Enum.TryParse<ShopItemKind>(entry.tier, out var tier)) continue;
```
Change to:
```csharp
            if (entry == null) continue;
            if (!System.Enum.TryParse<ShopItemKind>(entry.tier, out var tier))
            {
                Debug.LogWarning($"[SaveCollector] ApplyExtraShips: skipping ship with unknown tier '{entry.tier}'.");
                continue;
            }
```
(The `entry == null` skip can stay silent — a null list element is a non-issue, not lost player content.)

- [ ] **Step 3: ApplyBuildings — two silent skips**

Around lines 870-873, find:
```csharp
            if (entry == null) continue;

            var body = FindBodyByName(save.parentBodyName);
            if (body == null) continue;
```
Change to:
```csharp
            if (entry == null)
            {
                Debug.LogWarning($"[SaveCollector] ApplyBuildings: skipping building — no buildable matches prefab key '{save.prefabKey}'.");
                continue;
            }

            var body = FindBodyByName(save.parentBodyName);
            if (body == null)
            {
                Debug.LogWarning($"[SaveCollector] ApplyBuildings: skipping building '{save.prefabKey}' — parent body '{save.parentBodyName}' not found.");
                continue;
            }
```

- [ ] **Step 4: Brace-balance check**

```bash
python3 -c "s=open('Assets/3 - Scripts/SaveSystem/SaveCollector.cs',encoding='utf-8').read(); print(s.count('{')==s.count('}'))"
```
Must print `True`.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/SaveSystem/SaveCollector.cs"
git commit -m "$(cat <<'EOF'
fix(save): warn instead of silently dropping unresolvable save entries

ApplyLooseParts, ApplyExtraShips, and ApplyBuildings each `continue`d
past a save entry whose prefab/body/tier couldn't be resolved — the
player's content vanished with no trace. Added Debug.LogWarning at
each skip so renamed/removed prefabs surface during testing.

Audit ref: Save-17.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 6: Report** — confirm 4 warnings added (1 ApplyLooseParts + 1 ApplyExtraShips + 2 ApplyBuildings), brace check, commit SHA.

---

### Task 3: Persist BonfireNPCDialogue's first-time-met flag

`BonfireNPCDialogue._firstTimeDone` is not saved — loading a save where the player already met the bonfire NPC replays the first-time axe/pistol-grant line. The `NPCSave` schema already has the fields needed (`npcId`, `completed`). Wire it the same way `NPCDialogue` is wired.

**Files:**
- `Assets/3 - Scripts/NPC_Dialogue/BonfireNPCDialogue.cs` — add a public accessor
- `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` — `CaptureNPCs` + `ApplyNPCs`

`NPCSave` already has: `string npcId; string stateString = ""; bool completed;` — no schema change needed.

- [ ] **Step 1: Add a public accessor to BonfireNPCDialogue**

In `Assets/3 - Scripts/NPC_Dialogue/BonfireNPCDialogue.cs`, the field is `bool _firstTimeDone;` (around line 42, private). Add a public getter property and a public setter method right after the field declaration:
```csharp
    bool _firstTimeDone;

    // Save-system accessors: the first-time-met flag must round-trip so
    // loading a save where the player already met this NPC doesn't replay
    // the first-time axe/pistol-grant line.
    public bool FirstTimeDone => _firstTimeDone;
    public void ApplyFirstTimeDone(bool v) => _firstTimeDone = v;
```
(Match the file's existing indentation. Read around line 42 first to get the exact context.)

- [ ] **Step 2: Capture in CaptureNPCs**

In `SaveCollector.cs`, `CaptureNPCs` (around lines 246-264) currently captures `NPCDialogue` and `GuitarShopNPC`. After the `GuitarShopNPC` foreach block, add a third block:
```csharp
        foreach (var npc in Object.FindObjectsOfType<BonfireNPCDialogue>(true))
        {
            list.Add(new NPCSave
            {
                npcId = "BonfireNPCDialogue:" + npc.gameObject.name,
                completed = npc.FirstTimeDone,
            });
        }
```
Read the existing `CaptureNPCs` first to match its exact structure and place the new block correctly (inside the method, after the GuitarShopNPC loop, before the closing brace).

- [ ] **Step 3: Apply in ApplyNPCs**

In `SaveCollector.cs`, `ApplyNPCs` (around lines 565-585) has `if (save.npcId.StartsWith("NPCDialogue:"))` / `else if (save.npcId.StartsWith("GuitarShopNPC:"))` branches. Add a third `else if`:
```csharp
            else if (save.npcId.StartsWith("BonfireNPCDialogue:"))
            {
                var name = save.npcId.Substring("BonfireNPCDialogue:".Length);
                foreach (var b in bonfires)
                    if (b.gameObject.name == name) b.ApplyFirstTimeDone(save.completed);
            }
```
This needs a `bonfires` collection — at the top of `ApplyNPCs`, alongside the existing `var dialogues = ...` and `var guitarNpcs = ...` lines, add:
```csharp
    var bonfires = Object.FindObjectsOfType<BonfireNPCDialogue>(true);
```
Read the existing `ApplyNPCs` first to match its exact structure.

- [ ] **Step 4: Brace-balance check**

```bash
python3 -c "
for f in ['Assets/3 - Scripts/SaveSystem/SaveCollector.cs','Assets/3 - Scripts/NPC_Dialogue/BonfireNPCDialogue.cs']:
    s=open(f,encoding='utf-8').read()
    print(f, s.count('{')==s.count('}'))
"
```
Both `True`.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/NPC_Dialogue/BonfireNPCDialogue.cs" "Assets/3 - Scripts/SaveSystem/SaveCollector.cs"
git commit -m "$(cat <<'EOF'
fix(save): persist BonfireNPCDialogue first-time-met flag

_firstTimeDone wasn't saved, so loading a save where the player had
already met the bonfire NPC replayed the first-time axe/pistol-grant
line. Wired it through NPCSave (schema already had the fields),
mirroring how NPCDialogue completion round-trips — keyed by
"BonfireNPCDialogue:" + gameObject.name.

Audit ref: NPC-5.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 6: Report** — confirm the accessor pair, the capture block, the apply branch + `bonfires` lookup, brace check, commit SHA.

---

### Task 4: Unregister extra ships from EndlessManager before destroying them

`ApplyExtraShips`'s destroy loop removes existing `BoughtShip` instances without calling `EndlessManager.UnregisterPhysicsObject` first — `ApplyLooseParts` does this correctly and is the reference pattern. Leaving a destroyed transform registered means `EndlessManager` carries a stale entry until its next list scan.

**File:** `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`

- [ ] **Step 1: Add the unregister call**

Around lines 777-782, find the `ApplyExtraShips` destroy loop:
```csharp
        var existing = Object.FindObjectsOfType<BoughtShip>(true);
        if (existing != null)
        {
            foreach (var m in existing)
                if (m != null) Object.Destroy(m.gameObject);
        }
```
Change to:
```csharp
        var em = Object.FindObjectOfType<EndlessManager>();
        var existing = Object.FindObjectsOfType<BoughtShip>(true);
        if (existing != null)
        {
            foreach (var m in existing)
            {
                if (m == null) continue;
                if (em != null) em.UnregisterPhysicsObject(m.transform);
                Object.Destroy(m.gameObject);
            }
        }
```
(`em.UnregisterPhysicsObject` is a no-op if the transform was never registered, so this is safe even if extra ships aren't always registered. Mirrors `ApplyLooseParts` lines ~913-923.)

- [ ] **Step 2: Brace-balance check**

```bash
python3 -c "s=open('Assets/3 - Scripts/SaveSystem/SaveCollector.cs',encoding='utf-8').read(); print(s.count('{')==s.count('}'))"
```
Must print `True`.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/SaveSystem/SaveCollector.cs"
git commit -m "$(cat <<'EOF'
fix(save): unregister extra ships from EndlessManager before destroy

ApplyExtraShips destroyed existing BoughtShip instances without first
calling EndlessManager.UnregisterPhysicsObject — leaving a stale
transform entry until the next list scan. ApplyLooseParts already does
this; brought ApplyExtraShips into line. UnregisterPhysicsObject is a
no-op if the transform was never registered, so this is safe.

Audit ref: Save-2.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: Report** — confirm the destroy loop now unregisters first, brace check, commit SHA.

---

### Task 5: Convert the pre-placed alien kill loop to a single-pass HashSet lookup

`ApplyAlienKills` has a nested loop: for each killed pre-placed name, it scans every `AlienNPCDamageable` in the scene — O(N·M). Worse, the inner `break` stops at the first name match, so two pre-placed aliens sharing a name would leave the duplicate alive. A `HashSet` + single pass fixes both.

**File:** `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`

- [ ] **Step 1: Replace the nested loop**

Around lines 529-549, find:
```csharp
        if (s.killedPrePlacedNames != null && s.killedPrePlacedNames.Count > 0)
        {
            var damageables = Object.FindObjectsOfType<AlienNPCDamageable>(true);
            for (int i = 0; i < s.killedPrePlacedNames.Count; i++)
            {
                string targetName = s.killedPrePlacedNames[i];
                if (string.IsNullOrEmpty(targetName)) continue;
                for (int j = 0; j < damageables.Length; j++)
                {
                    var d = damageables[j];
                    if (d == null) continue;
                    if (!d.isStoryImpactful) continue;
                    if (d.gameObject.name != targetName) continue;
                    Object.Destroy(d.gameObject);
                    break;
                }
            }
        }
```
Replace with:
```csharp
        if (s.killedPrePlacedNames != null && s.killedPrePlacedNames.Count > 0)
        {
            // Single pass: build a name set, then destroy every story-impactful
            // pre-placed alien whose name is in it. The old nested loop was
            // O(N*M) and its inner `break` left duplicate-named aliens alive.
            var killedNames = new System.Collections.Generic.HashSet<string>(s.killedPrePlacedNames);
            killedNames.Remove("");
            var damageables = Object.FindObjectsOfType<AlienNPCDamageable>(true);
            for (int j = 0; j < damageables.Length; j++)
            {
                var d = damageables[j];
                if (d == null) continue;
                if (!d.isStoryImpactful) continue;
                if (!killedNames.Contains(d.gameObject.name)) continue;
                Object.Destroy(d.gameObject);
            }
        }
```
(The `killedNames.Remove("")` handles the old `string.IsNullOrEmpty` guard — a null entry can't be in `killedPrePlacedNames` if it's a `List<string>` populated normally, but empty strings are removed defensively. If `HashSet` construction from a list containing `null` is a concern, note it — but `List<string>` from JsonUtility won't contain nulls.)

- [ ] **Step 2: Brace-balance check**

```bash
python3 -c "s=open('Assets/3 - Scripts/SaveSystem/SaveCollector.cs',encoding='utf-8').read(); print(s.count('{')==s.count('}'))"
```
Must print `True`.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/SaveSystem/SaveCollector.cs"
git commit -m "$(cat <<'EOF'
fix(save): single-pass HashSet for pre-placed alien kill restore

ApplyAlienKills scanned every AlienNPCDamageable once per killed name
(O(N*M)), and the inner `break` meant two pre-placed aliens sharing a
name would leave the duplicate alive. Replaced with a HashSet built
once + a single pass that destroys every match.

Audit ref: Save-4.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: Report** — confirm the nested loop is now single-pass HashSet, behavior preserved (story-impactful + name match → destroy), brace check, commit SHA.

---

## Self-Review Notes

- **Spec coverage**: Phase 5 of the spec had 10 items. This plan does 5 (#1, #4, #6, #9, #10). #2, #3, #7, #8 are deferred with rationale; #5 is skipped (dead consumer) — all documented in the "Scope note".
- **Placeholder scan**: No TBDs. Several steps say "read the existing method first to match its structure" — that's a precision instruction (line numbers may have drifted from prior Phase 5 tasks touching the same file), not a placeholder.
- **Risk**: All 5 tasks are low-risk. Task 1 is doc-only. Task 2 adds log lines. Task 3 is an additive JSON-schema use (the `NPCSave` fields already exist; worst case an old save loads with `completed=false` which is exactly today's behavior). Task 4's `UnregisterPhysicsObject` is a no-op if not registered. Task 5 is a localized algorithm swap with preserved semantics.
- **Sequencing note**: Tasks 1, 2, 3, 4, 5 ALL touch `SaveCollector.cs`. They must be executed in order (each rebases on the prior commit) — the subagent-driven workflow does this naturally (one task at a time, commit between). Line numbers cited are from the pre-Phase-5 state; later tasks should grep/read to re-locate if a prior task shifted lines.
- **Type consistency**: `NPCSave` fields (`npcId`, `completed`) used in Task 3 match the verified schema. `BonfireNPCDialogue.FirstTimeDone` / `ApplyFirstTimeDone(bool)` — the getter/setter pair Task 3 adds and Task 3 consumes. `EndlessManager.UnregisterPhysicsObject(Transform)` in Task 4 matches the signature `ApplyLooseParts` already uses.
