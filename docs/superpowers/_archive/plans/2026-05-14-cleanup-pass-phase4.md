# Cleanup Pass — Phase 4 (Per-Frame Perf Hotspots) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Eliminate genuine per-frame `FindObjectOfType` / `FindObjectsOfType` scans in hot paths. Behavior stays identical — only *when* the lookup happens changes.

**Architecture:** Three targeted fixes. Two use the root-cause insight that `FindObjectOfType<PlayerController>()` (no `true` arg) cannot find the player while it's *inactive* during ship piloting — so the call re-scans every frame forever; passing `(true)` lets it find + cache the player once. One adds a throttled-retry. One adds an "already done" guard.

**Tech Stack:** Unity 2022.3, C#, no asmdefs. No CLI build — compile verification is the user's at the end.

**Source spec:** `docs/superpowers/specs/2026-05-13-cleanup-pass-design.md` (Phase 4)

## Scope note — what Phase 4 does and does NOT include

The design doc's Phase 4 listed ~20 sites across 3 patterns. This plan does the **genuinely-safe, behavior-identical, high-value subset**:
- Per-frame `FindObjectOfType<PlayerController>` in combat hot paths (`EnemyController.FixedUpdate` runs at 100 Hz).
- `FindObjectsOfType<Light>` every `LateUpdate` in `LensDirtOverlay`.
- `AttachModules()` called every frame in `CameraEffectsManager`.

**Deferred** (need Unity-open incremental verification, not blind big-bang):
- **Pattern C** (`rb.position` vs `transform.position` on Rigidbody objects) — real behavior implications; `PlayerPickup`'s case may be a non-issue if held objects are already kinematic; touches core files (`ResourceManager`, `Ship`, `GameSetUp`).
- `Hotbar.ResolveRefs` — already lazy-cached with `(true)`; the "cache Player root + GetComponent" optimization is marginal and the file was just modified twice this pass.
- `SpeedLinesOverlay` camera-rebind — only a transient null-camera edge case.
- `ShipHUD` / `CassettePlayer` per-frame string change-detection — `ShipHUD` is in the `Scripts/Game` core area; `CassettePlayer`'s alloc only happens while holding the eject key.
- TutorialStep `Tick()` finds — many of those steps are now in `_LegacySteps.cs` (dead); the live ones run only during the one-time tutorial.

---

## Important context for all tasks

- Working dir: `C:\123\1aughhh1`. Master branch.
- CLAUDE.md "Lazy-cached scene lookup" convention: cache once, lazy-refind only if null; for lookups that may never resolve, throttle the retry (see `LightLookAt.cs`'s `_nextFindAttemptTime` / `FindRetryInterval` pattern).
- The player GameObject is **disabled (not destroyed)** while the player is piloting a ship (per CLAUDE.md). `FindObjectOfType<T>()` skips inactive objects; `FindObjectOfType<T>(true)` includes them.

---

### Task 1: Fix per-frame PlayerController finds in combat hot paths

Three files do `FindObjectOfType<PlayerController>()` (WITHOUT `true`) in per-frame methods. Because the player is *inactive* while piloting, the call returns null and re-runs every frame forever during piloting. Adding `(true)` lets it find + cache the player once.

**Files:**
- `Assets/3 - Scripts/Combat/EnemyController.cs`
- `Assets/3 - Scripts/Combat/EnemySpawner.cs`
- `Assets/3 - Scripts/Combat/EnemyHealthBar.cs`

- [ ] **Step 1: EnemyController.cs — FixedUpdate find**

Around line 356 (inside `FixedUpdate`), the current code is:
```csharp
        if (player == null)
        {
            var pc = FindObjectOfType<PlayerController>();
            if (pc != null) player = pc.transform;
            if (player == null) return;
        }
```
Change the `FindObjectOfType<PlayerController>()` call to `FindObjectOfType<PlayerController>(true)`:
```csharp
        if (player == null)
        {
            var pc = FindObjectOfType<PlayerController>(true);
            if (pc != null) player = pc.transform;
            if (player == null) return;
        }
```
Do NOT touch the `FindObjectOfType<PlayerController>()` at line ~279 inside `Start()` (one-time, fine to leave — though adding `(true)` there is also harmless if you want consistency; prefer leaving Start untouched to keep the diff minimal). Do NOT touch the `FindObjectOfType<EndlessManager>()` calls at lines ~887/~925 — those are per-event (death/destroy), out of scope.

- [ ] **Step 2: EnemySpawner.cs — Update find**

Around line 43 (inside `Update`):
```csharp
        if (playerCtl == null) playerCtl = FindObjectOfType<PlayerController>();
```
Change to:
```csharp
        if (playerCtl == null) playerCtl = FindObjectOfType<PlayerController>(true);
```

- [ ] **Step 3: EnemyHealthBar.cs — LateUpdate find + EnsureSpritesAssigned guard**

(a) Around line 49 (inside `LateUpdate`):
```csharp
        if (cam == null)
        {
            var pc = FindObjectOfType<PlayerController>();
            if (pc != null) cam = pc.Camera;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
        }
```
Change `FindObjectOfType<PlayerController>()` to `FindObjectOfType<PlayerController>(true)`.

(b) `EnsureSpritesAssigned()` (around line 25) allocates a `GetComponentsInChildren<Image>(true)` array on EVERY `SetFill` call (i.e. every time an enemy takes damage). Add a one-time guard. Current:
```csharp
    void EnsureSpritesAssigned()
    {
        var images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i].sprite == null) images[i].sprite = GetBlankSprite();
        }
    }
```
Change to (add a `bool _spritesAssigned;` instance field near the top of the class, next to `Camera cam;`, and guard):
```csharp
    bool _spritesAssigned;

    void EnsureSpritesAssigned()
    {
        if (_spritesAssigned) return;
        _spritesAssigned = true;
        var images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i].sprite == null) images[i].sprite = GetBlankSprite();
        }
    }
```
(Place the `bool _spritesAssigned;` field declaration wherever the other instance fields like `Camera cam;` live — read the file to position it cleanly. Do NOT make it `static`.)

- [ ] **Step 4: Brace-balance check**

```bash
python3 -c "
for f in ['Assets/3 - Scripts/Combat/EnemyController.cs','Assets/3 - Scripts/Combat/EnemySpawner.cs','Assets/3 - Scripts/Combat/EnemyHealthBar.cs']:
    s=open(f,encoding='utf-8').read()
    print(f, s.count('{')==s.count('}'))
"
```
All `True`.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Combat/EnemyController.cs" "Assets/3 - Scripts/Combat/EnemySpawner.cs" "Assets/3 - Scripts/Combat/EnemyHealthBar.cs"
git commit -m "$(cat <<'EOF'
perf(combat): stop per-frame PlayerController scans during piloting

EnemyController.FixedUpdate (100 Hz), EnemySpawner.Update, and
EnemyHealthBar.LateUpdate each did FindObjectOfType<PlayerController>()
without the include-inactive arg. While the player pilots a ship its
GameObject is disabled, so the call returned null and re-scanned the
scene every frame forever. Passing (true) finds + caches it once.
Also gates EnemyHealthBar.EnsureSpritesAssigned behind a one-time flag
so it stops allocating a GetComponentsInChildren array per hit.

Audit ref: Combat-2/4/5/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 6: Report** — confirm the 3 `(true)` changes + the `_spritesAssigned` guard, brace check, commit SHA.

---

### Task 2: Throttle the directional-light scan in LensDirtOverlay

`LensDirtOverlay.LateUpdate` calls `FindMainSun()` whenever `_sun == null`, and `FindMainSun()` does `FindObjectsOfType<Light>()` (allocates an array). If there is no directional light, `_sun` stays null and this allocates every `LateUpdate` forever.

**File:** `Assets/3 - Scripts/Camera/LensDirtOverlay.cs`

- [ ] **Step 1: Add a retry-throttle**

Current relevant code:
```csharp
public class LensDirtOverlay : MonoBehaviour
{
    Image _image;
    Light _sun;
    float _alpha;

    void Awake() { BuildCanvas(); }

    void LateUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled || mgr.Input == null || !mgr.Input.fxLensDirt
            || mgr.PlayerCamera == null)
        { Fade(0f); return; }

        if (_sun == null) _sun = FindMainSun();
```

Change the fields + the `_sun` lookup to throttle the retry. Replace:
```csharp
    Image _image;
    Light _sun;
    float _alpha;
```
with:
```csharp
    Image _image;
    Light _sun;
    float _alpha;
    float _nextSunFindTime;
    const float SunFindRetryInterval = 2f;
```

And replace the line:
```csharp
        if (_sun == null) _sun = FindMainSun();
```
with:
```csharp
        if (_sun == null && Time.unscaledTime >= _nextSunFindTime)
        {
            _nextSunFindTime = Time.unscaledTime + SunFindRetryInterval;
            _sun = FindMainSun();
        }
```

Leave `FindMainSun()` itself unchanged. Leave everything else unchanged.

- [ ] **Step 2: Brace-balance check**

```bash
python3 -c "s=open('Assets/3 - Scripts/Camera/LensDirtOverlay.cs',encoding='utf-8').read(); print(s.count('{')==s.count('}'))"
```
Must be `True`.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Camera/LensDirtOverlay.cs"
git commit -m "$(cat <<'EOF'
perf(camera): throttle directional-light scan in LensDirtOverlay

FindMainSun() does FindObjectsOfType<Light>() (array alloc). When no
directional light exists, _sun stayed null and the scan ran every
LateUpdate. Throttled the retry to once every 2s, matching the
LightLookAt _nextFindAttemptTime pattern from CLAUDE.md.

Audit ref: Camera-3.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: Report** — confirm the field additions + throttled lookup, brace check, commit SHA.

---

### Task 3: Stop calling AttachModules() every frame in CameraEffectsManager

`CameraEffectsManager.Update()` calls `AttachModules()` every frame. Once all modules are attached, this is ~14 redundant Unity-`==`-null checks per frame. Add a guard so it only runs until everything is attached.

**File:** `Assets/3 - Scripts/Camera/CameraEffectsManager.cs`

- [ ] **Step 1: Read the file**

Read `Assets/3 - Scripts/Camera/CameraEffectsManager.cs` — at minimum the field declarations, the full `AttachModules()` method (starts ~line 61), and `Update()` (~line 144). Understand:
- `AttachModules()` has two parts: (a) a series of `if (X == null) X = ...` for the non-camera modules (TransformFX, FOVFX, Vignette, DamageFlash, Letterbox, SpeedLines, FilmGrain, LensDirt, Combat, Slowmo, Mood, LensFlares), and (b) a camera-component block (RadialBlur, ChromaticAberration) that depends on `PlayerCamera` being non-null and so must keep running until `PlayerCamera` is acquired.

- [ ] **Step 2: Add a `_modulesAttached` guard**

Add a `bool _modulesAttached;` instance field near the other private fields.

At the END of `AttachModules()`, after all attachment logic, add a check that sets `_modulesAttached = true` ONLY when every non-camera module is non-null AND the camera-component block is satisfied (i.e. `PlayerCamera == null` is still acceptable to mark "done with what we can do" — but then a later frame where `PlayerCamera` becomes non-null still needs to run the camera block). The cleanest correct form:

```csharp
        // Mark fully-attached only when every module — including the
        // camera-dependent ones — is in place. Until PlayerCamera is
        // acquired, RadialBlur/ChromaticAberration can't attach, so we keep
        // _modulesAttached false and Update keeps retrying.
        _modulesAttached =
            TransformFX != null && FOVFX != null && Vignette != null &&
            DamageFlash != null && Letterbox != null && SpeedLines != null &&
            FilmGrain != null && LensDirt != null && Combat != null &&
            Slowmo != null && Mood != null && LensFlares != null &&
            PlayerCamera != null && RadialBlur != null && ChromaticAberration != null;
```
(Adjust the exact field list to match what `AttachModules` actually assigns — read the method and use its real set of module fields. If the camera-component block uses different field names than `RadialBlur`/`ChromaticAberration`, use the real names.)

In `Update()`, change:
```csharp
        AttachModules();
```
to:
```csharp
        if (!_modulesAttached) AttachModules();
```

Leave `Awake()`'s and `OnSceneLoaded()`'s direct `AttachModules()` calls as-is — those should still fire unconditionally (a scene reload may need re-attachment; `OnSceneLoaded` calling `AttachModules()` will re-evaluate and re-set `_modulesAttached`). Optionally, `OnSceneLoaded` may set `_modulesAttached = false;` before calling `AttachModules()` to force a clean re-evaluation — include that one line in `OnSceneLoaded` for correctness:
```csharp
    void OnSceneLoaded(Scene s, LoadSceneMode m) { _modulesAttached = false; TryAcquireRefs(); AttachModules(); }
```

- [ ] **Step 3: Brace-balance check**

```bash
python3 -c "s=open('Assets/3 - Scripts/Camera/CameraEffectsManager.cs',encoding='utf-8').read(); print(s.count('{')==s.count('}'))"
```
Must be `True`.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/Camera/CameraEffectsManager.cs"
git commit -m "$(cat <<'EOF'
perf(camera): stop re-running AttachModules every frame once attached

Update() called AttachModules() unconditionally each frame — ~14
redundant Unity null-checks once everything was seeded. Added a
_modulesAttached guard that flips true only when every module
(including the camera-dependent RadialBlur/ChromaticAberration) is
in place; OnSceneLoaded resets it so a scene reload re-attaches.

Audit ref: Camera-5.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 5: Report** — the exact `_modulesAttached` condition used (with the real field list), the Update + OnSceneLoaded changes, brace check, commit SHA. Flag DONE_WITH_CONCERNS if the camera-component block's structure made the guard non-trivial.

---

## Self-Review Notes

- **Spec coverage**: Phase 4 of the spec listed Patterns A/B/C across ~20 sites. This plan does the safe high-value subset of Pattern A (3 tasks). Pattern B, Pattern C, and the marginal Pattern A sites are explicitly deferred with rationale in the "Scope note".
- **Placeholder scan**: Task 3 Step 2 says "adjust the field list to match what AttachModules actually assigns" — this is a *read-and-match* instruction, not a placeholder; the implementer must read the method (Step 1 mandates it) and the exact module set is determinable from the file. Acceptable.
- **Risk**: Tasks 1 and 2 are behavior-identical (the lookups return the same object, just sooner / less often). Task 3 is the only one with a logic addition — mitigated by the explicit "camera block must keep running until PlayerCamera is non-null" requirement and the `OnSceneLoaded` reset.
- **Type consistency**: `FindObjectOfType<PlayerController>(true)` returns the same `PlayerController` type as the no-arg form. `_spritesAssigned`, `_nextSunFindTime`, `_modulesAttached` are all new private instance fields, named per the project's `_camelCase` private-field convention.
