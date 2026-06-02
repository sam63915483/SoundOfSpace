# Cleanup Pass — Phase 3 (HudFontResolver Consolidation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Eliminate the duplicated HUD-font fallback chain — 5 files each carry a verbatim copy of `HudFontResolver`'s logic. Replace every copy with a call to the existing `HudFontResolver`.

**Architecture:** Pure dedup against an existing helper. `HudFontResolver.Default` / `HudFontResolver.Apply(TextMeshProUGUI)` already implement the exact same Techno-SDF → raw-Techno → LiberationMono → CourierNewBold → LiberationSans fallback chain, with a *single shared* cache instead of one cache per file.

**Tech Stack:** Unity 2022.3, C#, no asmdefs. No CLI build — compile verification is the user's at the end.

**Source spec:** `docs/superpowers/specs/2026-05-13-cleanup-pass-design.md` (Phase 3)

## Scope note — what Phase 3 does and does NOT include

The design doc's Phase 3 also listed `HudCanvasFactory` + `UIBuild`, `PreviewRig`, `Ship.PilotedShip`, `DialogueNPCBase`, and `HandheldEquippableBase`. **Those are deferred.** They are either base-class extractions across inspector-serialized MonoBehaviours (compile-break risk, needs scene-ref verification) or canvas/preview migrations that risk subtle per-HUD config drift requiring visual verification. They are inappropriate for a blind-subagent + big-bang-verification workflow and should be done in a focused Unity-open session. This plan does only the one zero-risk, behavior-identical dedup.

---

## Important context for all tasks

- Working dir: `C:\123\1aughhh1`. Master branch.
- `HudFontResolver` lives at `Assets/3 - Scripts/UI/HudFontResolver.cs`. Public API:
  - `static TMP_FontAsset HudFontResolver.Default` — resolves once, caches.
  - `static void HudFontResolver.Apply(TextMeshProUGUI t)` — sets `t.font` to `Default` if non-null.
- It is in the global namespace (no `using` needed by consumers in the same `Assembly-CSharp`).
- **Do NOT touch `Assets/3 - Scripts/Player/NotePickup.cs`.** It has its own `ApplyDefaultFont` with a `_paperFont` cache that loads ONLY `LiberationSans SDF` — that is a deliberately different "paper" font for note UI, not the HUD Techno chain. It is NOT a duplicate of HudFontResolver.

---

### Task 1: Consolidate the duplicated HUD-font resolver into HudFontResolver

**Files to modify (the 5 confirmed duplicates):**
- `Assets/3 - Scripts/Player/PlayerWallet.cs`
- `Assets/3 - Scripts/Ship/GForceHUD.cs`
- `Assets/3 - Scripts/Survival/WaterFillHUD.cs`
- `Assets/3 - Scripts/Survival/VitalsHUD.cs`
- `Assets/3 - Scripts/Tutorial/TutorialUI.cs`

Each of these files contains this duplicated pattern (the exact field names and method name are identical across all 5; only line numbers differ):
```csharp
    static TMP_FontAsset _hudFont;
    static bool _hudFontResolved;

    static void ApplyDefaultFont(TextMeshProUGUI t)
    {
        if (!_hudFontResolved)
        {
            _hudFont = Resources.Load<TMP_FontAsset>("Techno SDF");
            if (_hudFont == null)
            {
                var rawFont = Resources.Load<Font>("Techno");
                if (rawFont != null) _hudFont = TMP_FontAsset.CreateFontAsset(rawFont);
            }
            if (_hudFont == null) _hudFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationMono SDF");
            if (_hudFont == null) _hudFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/CourierNewBold SDF");
            if (_hudFont == null) _hudFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            _hudFontResolved = true;
        }
        if (_hudFont != null) t.font = _hudFont;
    }
```
(`TutorialUI.cs`'s copy has slightly different brace placement on the `rawFont` block but is functionally identical — match whatever is actually in each file.)

This chain is byte-for-byte the same logic as `HudFontResolver.Default` + `HudFontResolver.Apply`.

- [ ] **Step 1: Confirm the scope with grep**

From `C:\123\1aughhh1`:
```bash
grep -rln "_hudFontResolved" --include="*.cs" "Assets/"
```
Expected: exactly the 5 files listed above. If a 6th file appears (the audit mentioned `InteractPromptUI`, `TabbedPauseMenu`, `Hotbar` as possibles), include it too — apply the same transformation. If a file you expected is missing, note it. `NotePickup.cs` must NOT appear (it uses `_paperFont`, not `_hudFontResolved`) — if it does, STOP and report.

- [ ] **Step 2: For EACH file, apply the transformation**

For each of the 5 (or more) files, do three edits:

(a) **Delete** the `static TMP_FontAsset _hudFont;` field, the `static bool _hudFontResolved;` field, and the entire `static void ApplyDefaultFont(TextMeshProUGUI t) { ... }` method. Read the file to get the exact current text of that block (line numbers drift; the field/method names are stable).

(b) **Replace every call site** of `ApplyDefaultFont(x)` in that file with `HudFontResolver.Apply(x)`. Grep within the file first: `grep -n "ApplyDefaultFont" <file>` — there will be one or more call sites plus the (now-deleted) definition. Update all CALL sites; the definition is gone from step (a).

(c) **Check the `using` block.** If removing the `ApplyDefaultFont` method makes a `using` directive unused (unlikely — these files use TMPro/UnityEngine for plenty else), leave the usings alone. Do NOT add a `using` for HudFontResolver — it is in the global namespace.

Work one file at a time. After each file, re-grep that file for `ApplyDefaultFont` and `_hudFont` — there should be ZERO matches remaining (definition gone, all call sites migrated).

- [ ] **Step 3: Verify no stragglers**

From `C:\123\1aughhh1`:
```bash
grep -rln "_hudFontResolved\|_hudFont\b" --include="*.cs" "Assets/"
```
Expected: ZERO matches (every duplicate removed). `HudFontResolver.cs` itself uses `_font`/`_resolved` (different names) so it won't match. `NotePickup.cs` uses `_paperFont` so it won't match.

```bash
grep -rln "ApplyDefaultFont" --include="*.cs" "Assets/"
```
Expected: ONLY `Assets/3 - Scripts/Player/NotePickup.cs` (its own separate paper-font method — untouched).

- [ ] **Step 4: Verify brace balance of each modified file**

```bash
python3 -c "
import sys
for f in ['Assets/3 - Scripts/Player/PlayerWallet.cs','Assets/3 - Scripts/Ship/GForceHUD.cs','Assets/3 - Scripts/Survival/WaterFillHUD.cs','Assets/3 - Scripts/Survival/VitalsHUD.cs','Assets/3 - Scripts/Tutorial/TutorialUI.cs']:
    s=open(f,encoding='utf-8').read()
    print(f, s.count('{')==s.count('}'), s.count('{'), s.count('}'))
"
```
(Add any extra files found in Step 1.) All must report balanced.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Player/PlayerWallet.cs" "Assets/3 - Scripts/Ship/GForceHUD.cs" "Assets/3 - Scripts/Survival/WaterFillHUD.cs" "Assets/3 - Scripts/Survival/VitalsHUD.cs" "Assets/3 - Scripts/Tutorial/TutorialUI.cs"
git commit -m "$(cat <<'EOF'
refactor(hud): consolidate duplicated font resolver into HudFontResolver

5 HUDs each carried a verbatim copy of the Techno-SDF -> fallback chain
(static _hudFont/_hudFontResolved + ApplyDefaultFont). HudFontResolver
already implements the identical chain with a single shared cache.
Replaced every copy with HudFontResolver.Apply(). NotePickup's separate
paper-font resolver is intentionally left alone.

Audit ref: HUD-1, Cross-12.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```
(If extra files were found in Step 1, `git add` them too.)

- [ ] **Step 6: Report**
- The grep result from Step 1 (exact file list)
- For each file: how many `ApplyDefaultFont` call sites were migrated
- Step 3 verification output (must be zero / NotePickup only)
- Step 4 brace-balance output
- Commit SHA

---

## Self-Review Notes

- **Spec coverage**: Phase 3 of the spec listed 6 helper items. This plan does 1 (HudFontResolver consolidation) and explicitly defers the other 5 with rationale in the "Scope note". The deferred items are documented for a follow-up Unity-open session.
- **Placeholder scan**: No TBDs. Step 1 is a scope-confirmation gate (the exact file set is verified at implementation time) — deliberate, not a placeholder.
- **Risk**: Lowest-risk task in the entire cleanup pass. `HudFontResolver.Apply` has the identical signature to the local `ApplyDefaultFont`, and the resolver chain is byte-identical. The only behavior change is a shared cache instead of 5 per-file caches — strictly an improvement. Brace-balance check guards against a botched method deletion.
- **Type consistency**: `HudFontResolver.Apply(TextMeshProUGUI)` — same signature as the deleted `ApplyDefaultFont(TextMeshProUGUI)`. Call sites map 1:1.
