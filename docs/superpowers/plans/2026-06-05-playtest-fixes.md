# Playtest Fixes (2026-06-05) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline) to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the seven 2026-06-05 playtest fixes (ship proximity gate, tip-queue visual stack + drop fix, hull-sealed countdown + milestone warnings, vacuum tip, phone persistent nag, suit O2 into vitals, Coplay menu ambience).

**Architecture:** Reuse existing systems wherever possible — `PlayerController.FindNearestShipInRange`/`Ship.IsPiloted` for proximity, `HALLineHUD`'s existing queue for tips, `OxygenManager`'s `HullState` machine for hull events, `VitalsHUD.BuildStatRow` for the O2 bar. New code follows project conventions: append serialized fields at end, no `FindObjectOfType` in per-frame loops, `rb.position` reads, singleton + MainMenu-seeding patterns.

**Tech Stack:** Unity 2022.3 Built-in RP, C# (Assembly-CSharp, no asmdef), Coplay MCP for compile checks + music generation.

**Verification model (no CLI tests in this repo):** After each code change, run `mcp__coplay-mcp__check_compile_errors`. Expected: no new errors. Behaviour verification is an Editor play-test by Sam at the checkpoints noted. Commit after each green task.

---

## File map

- `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs` — add `PlayerIsNearOrPiloting()` (§5).
- `Assets/3 - Scripts/Ship/ShipReactor.cs` — gate prompt/SFX (§5).
- `Assets/3 - Scripts/Ship/BackHatchButton.cs` — gate prompt/SFX (§5).
- `Assets/3 - Scripts/Survival/VitalsHUD.cs` — gate warning SFX (§5); add suit-O2 row (§2).
- `Assets/3 - Scripts/AI/HALCommentator.cs` — route rate-limited lines to queue instead of dropping (§7).
- `Assets/3 - Scripts/UI/HALLineHUD.cs` — visual stacked queue + slide/scale promotion + live-text line (§7, §4).
- `Assets/3 - Scripts/Survival/OxygenManager.cs` — `hullWasFilledOnGround`, hull-sealed edge + countdown, milestone warnings, vacuum tip, §5 gating of VO (§4, §6).
- `Assets/3 - Scripts/Survival/OxygenHUD.cs` — disable suit bar, keep hull bar (§2).
- `Assets/3 - Scripts/SaveSystem/SaveData.cs` — `hasEverOpenedPhone` field (§3).
- `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` — capture/apply `hasEverOpenedPhone` (§3).
- `Assets/3 - Scripts/SaveSystem/NewGameReset.cs` — reset phone flag (§3).
- `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` — persistent nag prompt + set flag on Open (§3).
- `Assets/3 - Scripts/Story/StoryDirector.cs` — trigger persistent nag on first message (§3).
- `Assets/3 - Scripts/UI/MainMenuController.cs` — looping ambient AudioSource (§1).
- `Assets/.../Audio/MenuAmbience.*` — Coplay-generated loop (§1).

> Every task: read the real file first to anchor edits against current line numbers (the explore line numbers may have drifted). Add new `.cs` AND `.meta` to git for any new files.

---

## Task 1 (§5): `Ship.PlayerIsNearOrPiloting` helper

**Files:** Modify `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs`

- [ ] **Step 1: Read `Ship.cs`** around the static piloted-instance region (~`208-215`), `IsPiloted` (~`1185`), and find the cached `Rigidbody rb` field + how the player Rigidbody is reachable (the `pilot` PlayerController at ~`208`, and `PlayerController.Instance`/`.Rigidbody`).

- [ ] **Step 2: Add the helper** (append near the other public accessors, NOT mid-serialized-field block). Throttled, null-safe, per-instance:

```csharp
// --- §5 ship-specific-prompt proximity gate -------------------------------
[System.NonSerialized] float _nearCheckTime;
[System.NonSerialized] bool _nearCached;
const float NearCheckInterval = 0.2f;
const float DefaultPromptRadius = 25f;

/// True if the player is piloting THIS ship, or is within `radius` metres of it.
/// Per-instance (multi-ship safe), throttled, and null-safe.
public bool PlayerIsNearOrPiloting(float radius = DefaultPromptRadius)
{
    if (shipIsPiloted) return true;                 // this ship is being flown
    if (Time.unscaledTime < _nearCheckTime) return _nearCached;
    _nearCheckTime = Time.unscaledTime + NearCheckInterval;

    var pc = PlayerController.Instance;
    if (pc == null || rb == null) { _nearCached = false; return false; }
    Vector3 playerPos = pc.Rigidbody != null ? pc.Rigidbody.position : pc.transform.position;
    float r = radius > 0f ? radius : DefaultPromptRadius;
    _nearCached = (playerPos - rb.position).sqrMagnitude <= r * r;
    return _nearCached;
}
```

> If `PlayerController.Instance` doesn't exist, use the same player accessor `OxygenManager` uses (it reads `player.Rigidbody.position` at ~`119`); mirror exactly. If the rigidbody field is not named `rb`, use the real name.

- [ ] **Step 3: Compile check** — `mcp__coplay-mcp__check_compile_errors`. Expected: no new errors.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs"
git commit -m "feat(ship): add per-instance PlayerIsNearOrPiloting proximity helper (§5)"
```

---

## Task 2 (§5): Gate ship-specific prompt/SFX call sites

**Files:** Modify `ShipReactor.cs`, `BackHatchButton.cs`, `VitalsHUD.cs`

- [ ] **Step 1: ShipReactor** — read `Assets/3 - Scripts/Ship/ShipReactor.cs` around the prompt (`~99`) and feed SFX (`~89`). These need the owning `Ship`. Find how the reactor reaches its ship (parent walk / serialized ref). Wrap the prompt show and the SFX:

```csharp
// before showing "Press F to insert crystals" / playing feedClip:
if (_ship != null && !_ship.PlayerIsNearOrPiloting()) return; // or skip the prompt branch
```

> Use the reactor's existing ship reference name. If none exists, resolve once in `Awake` via `GetComponentInParent<Ship>()` and cache it — do NOT add a per-frame `FindObjectOfType`.

- [ ] **Step 2: BackHatchButton** — read `Assets/3 - Scripts/Ship/BackHatchButton.cs`. It already resolves its ship via parent walk (`~25`). Gate the interaction prompt string (`~45`) and the toggle SFX (`~53`) behind `ship.PlayerIsNearOrPiloting()`. (Prompt is only shown when the player is already at the button, so this is mostly belt-and-suspenders + future multi-ship correctness — keep it for consistency.)

- [ ] **Step 3: VitalsHUD warning SFX** — read `Assets/3 - Scripts/Survival/VitalsHUD.cs` around `~213` (`_audio.PlayOneShot(warningClip)`), currently gated on `Ship.AnyShipPiloted` (~`123`). Tighten: only play when the piloted/near ship is the relevant one. Since vitals power/fuel warnings refer to the piloted ship, gate on `Ship.PilotedInstance != null && Ship.PilotedInstance.PlayerIsNearOrPiloting()` (piloting always true here, so effectively unchanged for piloting — but future-proof). Minimal change; keep existing behaviour for the piloting case.

- [ ] **Step 4: Compile check** — `check_compile_errors`. Expected: no new errors.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Ship/ShipReactor.cs" "Assets/3 - Scripts/Ship/BackHatchButton.cs" "Assets/3 - Scripts/Survival/VitalsHUD.cs"
git commit -m "feat(ship): gate reactor/hatch/vitals prompts behind 25m proximity (§5)"
```

**CHECKPOINT (Sam):** Play-test — fuel/hull prompts should be silent when far from the ship, active within 25 m or while piloting.

---

## Task 3 (§7a): Stop HALCommentator from dropping rate-limited lines

**Files:** Modify `Assets/3 - Scripts/AI/HALCommentator.cs`

- [ ] **Step 1: Read** `HALCommentator.cs` around the `Volunteer` rate-limit block (`~329-341`): `MinSecondsBetweenLines = 8f`, `_nextAllowedTime`, the early-return that drops the line when `Time.unscaledTime < _nextAllowedTime && !bypassRateLimit`.

- [ ] **Step 2: Replace the silent drop with enqueue-to-HUD.** Instead of `return;` on rate-limited non-bypass lines, still hand the line to `HALLineHUD.Instance.Show(line)` (the HUD now owns pacing + the 3-item cap from Task 5). Keep `_nextAllowedTime` updates for the *spoken/primary* cadence, but do not discard:

```csharp
bool tooSoon = Time.unscaledTime < _nextAllowedTime;
if (tooSoon && !bypassRateLimit)
{
    // Was: return; (silently dropped). Now: queue it so it isn't lost (§7).
    if (HALLineHUD.Instance != null) HALLineHUD.Instance.Show(line);
    HALVolunteeredLog.Instance?.Append(line);   // keep log parity (mirror existing call)
    return;
}
_nextAllowedTime = Time.unscaledTime + MinSecondsBetweenLines;
// ... existing path that also calls Show(line) + Append ...
```

> Match the exact existing log/Show calls in the non-dropped path so behaviour is identical except that rate-limited lines now queue instead of vanish. Avoid double-Show: ensure the line is shown exactly once on each branch.

- [ ] **Step 3: Compile check.** Expected: no new errors.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/AI/HALCommentator.cs"
git commit -m "fix(tips): queue rate-limited HAL lines instead of silently dropping them (§7)"
```

---

## Task 4 (§7b): HALLineHUD visual stacked queue

**Files:** Modify `Assets/3 - Scripts/UI/HALLineHUD.cs`

- [ ] **Step 1: Read** `HALLineHUD.cs` fully (it is ~140 lines): `Show()` (`~65`), `_queue` (`~39`), `ProcessQueue()` (`~72-96`), `BuildUI()` (`~113`), fade constants (`~34-37`), the single `_label`/`CanvasGroup`.

- [ ] **Step 2: Build the stack containers.** In `BuildUI()`, under the primary label, create a vertical container that holds up to 3 "queued preview" rows (a small TMP label each, ~80% scale, reduced alpha, no eye icon). Anchor them just below the primary strip. Keep references in an array `RectTransform[] _previewRows` + `TMP_Text[] _previewLabels` + `CanvasGroup[] _previewGroups`.

- [ ] **Step 3: Cap the queue at 3 with drop-oldest + warning.** In `Show()`:

```csharp
const int MaxQueued = 3;
public void Show(string text)
{
    if (string.IsNullOrWhiteSpace(text)) return;
    if (_queue.Count >= MaxQueued)
    {
        var dropped = _queue.Dequeue();              // drop OLDEST queued, not incoming
        Debug.LogWarning($"[HALLineHUD] tip queue full; dropped oldest queued line: \"{dropped}\"");
    }
    _queue.Enqueue(text);
    RefreshPreviews();
    if (_processRoutine == null) _processRoutine = StartCoroutine(ProcessQueue());
}
```

- [ ] **Step 4: Render previews.** `RefreshPreviews()` fills `_previewLabels` from the current `_queue` contents (peek without dequeue — copy to array), shows N rows at ~80% scale / reduced opacity, hides the rest:

```csharp
void RefreshPreviews()
{
    var arr = _queue.ToArray();   // oldest..newest still waiting
    for (int i = 0; i < _previewRows.Length; i++)
    {
        bool on = i < arr.Length;
        _previewGroups[i].alpha = on ? 0.55f : 0f;
        if (on) { _previewLabels[i].text = arr[i]; _previewRows[i].localScale = Vector3.one * 0.8f; }
    }
}
```

- [ ] **Step 5: Promotion animation.** In `ProcessQueue()`, when dequeuing the next line for the primary slot, first run a ~0.3s slide-up + scale-up of the about-to-be-promoted preview into the primary position, THEN set `_label.text`, THEN fire TTS (`HALVoicePlayer.TryPlay`) at the END of the transition (currently TTS fires at fade-in start — move it to post-transition for promoted items; first/immediate item can keep current timing). After dequeue, call `RefreshPreviews()` so the stack shifts up. Preserve the existing 0.4/5.5/0.7/0.3 fade timing for the primary.

```csharp
// sketch inside ProcessQueue loop:
string line = _queue.Dequeue();
RefreshPreviews();                       // remaining previews shift up
yield return PromoteTransition();        // ~0.3s slide-up + scale-up of primary slot
_label.text = line;
if (HALVoicePlayer.Instance != null) HALVoicePlayer.Instance.TryPlay(line);  // TTS at end of transition
yield return FadeTo(1f, FadeInSeconds);
yield return new WaitForSecondsRealtime(HoldSeconds);
yield return FadeTo(0f, FadeOutSeconds);
yield return new WaitForSecondsRealtime(GapBetweenLines);
```

> Keep it robust: if `PromoteTransition` is visually awkward for the very first line (empty stack), skip the slide for the first item and only animate promotions when a preview existed. Use `WaitForSecondsRealtime`/unscaled time to match existing code (verify which the file uses and match it).

- [ ] **Step 6: Compile check.** Expected: no new errors.

- [ ] **Step 7: Commit**

```bash
git add "Assets/3 - Scripts/UI/HALLineHUD.cs"
git commit -m "feat(tips): visual stacked tip queue with slide-up promotion + TTS-on-arrival (§7)"
```

**CHECKPOINT (Sam):** Play-test — fire two tips quickly; first shows full-size with TTS, second shows below at 80%, then slides up + scales to primary and speaks. Nothing dropped.

---

## Task 5 (§4 infra): Live-text line support in HALLineHUD

**Files:** Modify `Assets/3 - Scripts/UI/HALLineHUD.cs`

- [ ] **Step 1: Add a live-text overload.** A prompt whose text re-evaluates each frame while shown (for the hull-sealed countdown). Add:

```csharp
public void ShowLive(System.Func<string> textSource, string voiceKey = null) { ... }
```

Store an optional `Func<string>` alongside the queued entry (change `_queue` to hold a small struct `{ string staticText; Func<string> live; string voiceKey; }`, or a parallel queue). While the primary line is a live entry, set `_label.text = live()` every frame during hold/fade. TTS uses `voiceKey ?? live()` once at arrival (the countdown speaks the initial value; text keeps updating visually).

> This is the only structural change to the queue element type — update `Show()` and previews to use the struct (static lines set `staticText`, `live = null`). Preview rows always show the static/snapshot text (no per-frame eval for previews).

- [ ] **Step 2: Compile check.** Expected: no new errors.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/UI/HALLineHUD.cs"
git commit -m "feat(hud): HALLineHUD live-text line for real-time countdowns (§4 infra)"
```

---

## Task 6 (§4): Hull-sealed flag, edge trigger, countdown + milestone warnings

**Files:** Modify `Assets/3 - Scripts/Survival/OxygenManager.cs`

- [ ] **Step 1: Read** `OxygenManager.cs` FixedUpdate hull block (`~151-181`): `HullState` enum, `prev`/`hullState`, `InRefillZone()` (`~245`), `PlayVO()` (`~344-350`), `HullO2`, `hullMax`. Note the existing edge pattern (`hullState == X && prev != X`).

- [ ] **Step 2: Add state fields** (append at end of the class fields region, NOT mid-serialized block):

```csharp
[System.NonSerialized] bool hullWasFilledOnGround;
[System.NonSerialized] bool[] hullMilestoneFired = new bool[4];   // 4m,2m,1m,30s
static readonly float[] HullMilestones = { 240f, 120f, 60f, 30f };
static readonly string[] HullMilestoneMsgs = {
    "4 minutes of hull air remaining.",
    "2 minutes of hull air remaining.",
    "1 minute of hull air remaining.",
    "30 seconds of hull air remaining."
};
```

- [ ] **Step 3: Set/clear `hullWasFilledOnGround`.** On `Refilling` enter while `InRefillZone()`, set true and reset `hullMilestoneFired` to all-false (new fill). When `HullO2 <= 0f`, set false.

- [ ] **Step 4: Hull-sealed edge + live countdown.** On `hullState == Sealed && prev == Refilling && hullWasFilledOnGround` AND `ship.PlayerIsNearOrPiloting()` (§5 gate):

```csharp
if (HALLineHUD.Instance != null)
    HALLineHUD.Instance.ShowLive(() => {
        int t = Mathf.Max(0, Mathf.RoundToInt(HullO2));
        int m = t / 60, s = t % 60;
        return $"Hull sealed — {m} minute{(m==1?"":"s")} {s} second{(s==1?"":"s")} of air remaining.";
    });
```

- [ ] **Step 5: Milestone warnings.** Each FixedUpdate while `hullWasFilledOnGround` and hull is Sealed and `ship.PlayerIsNearOrPiloting()`: for each threshold i, if `!hullMilestoneFired[i] && HullO2 <= HullMilestones[i]`, fire `PlayVO(HullMilestoneMsgs[i])` and set `hullMilestoneFired[i] = true`. (Crossing-from-above is implicit: fires once when first at/under the threshold.) Reset all four to false whenever a new fill begins (Step 3).

- [ ] **Step 6: Compile check.** Expected: no new errors.

- [ ] **Step 7: Commit**

```bash
git add "Assets/3 - Scripts/Survival/OxygenManager.cs"
git commit -m "feat(oxygen): hull-sealed live countdown + 4/2/1/0.5min warnings, gated (§4,§5)"
```

**CHECKPOINT (Sam):** Open hatch on Humble Abode (fills hull), close it → "Hull sealed — m s remaining" with live countdown, fading like re-oxy. Warnings fire once each as it drains. No prompt if hatch was never opened on an O2 planet.

---

## Task 7 (§6): Vacuum-exposure tip

**Files:** Modify `Assets/3 - Scripts/Survival/OxygenManager.cs`

- [ ] **Step 1: Add flag** `[System.NonSerialized] bool hullVacuumTipFired;`.

- [ ] **Step 2: Trigger** in FixedUpdate where altitude/vacuum is already known (the Draining branch / `altT` computation): condition = hatch open AND in vacuum (above atmosphere midpoint, off-world, not Cyclops — reuse the exact `!InRefillZone()`/`altT`-based test the drain logic uses). On the rising edge (condition true && !`hullVacuumTipFired`) AND `ship.PlayerIsNearOrPiloting()`:

```csharp
if (HALLineHUD.Instance != null) HALLineHUD.Instance.Show("Hull exposed to the vacuum of space.");
hullVacuumTipFired = true;
```

Reset `hullVacuumTipFired = false` when the condition clears (hatch closed or back in breathable zone).

- [ ] **Step 3: Compile check.** Expected: no new errors.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/Survival/OxygenManager.cs"
git commit -m "feat(oxygen): one-shot 'hull exposed to vacuum' tip via tip queue (§6)"
```

---

## Task 8 (§3): Phone-opened save flag

**Files:** Modify `SaveData.cs`, `SaveCollector.cs`, `NewGameReset.cs`

- [ ] **Step 1: SaveData** — read `Assets/3 - Scripts/SaveSystem/SaveData.cs`. Add `public bool hasEverOpenedPhone;` to the appropriate top-level save section (mirror how a simple bool like `cyclopsCheckpointReached` is placed; JsonUtility-friendly). Append, don't reorder.

- [ ] **Step 2: SaveCollector** — read `SaveCollector.cs`. In the capture for that section, `s.hasEverOpenedPhone = PlayerPhoneUI.HasEverOpened;` (a static the phone exposes — Task 9 adds it). In apply, `PlayerPhoneUI.HasEverOpened = data....hasEverOpenedPhone;`. Place at a safe order point (UI/singleton touch-up, late).

- [ ] **Step 3: NewGameReset** — read `Assets/3 - Scripts/SaveSystem/NewGameReset.cs`. Add `PlayerPhoneUI.HasEverOpened = false;` so a New Game re-arms the nag.

- [ ] **Step 4: Compile check** (will fail until Task 9 adds `HasEverOpened` — do Task 9 first if compile order matters; otherwise add the static stub in Task 9 before this compiles). Sequence: do Task 9 step that adds the static, then this.

- [ ] **Step 5: Commit (with Task 9)** — combined, since they’re mutually dependent.

---

## Task 9 (§3): Persistent nag prompt + flag wiring

**Files:** Modify `Assets/3 - Scripts/UI/PlayerPhoneUI.cs`, `Assets/3 - Scripts/Story/StoryDirector.cs`

- [ ] **Step 1: Read** `PlayerPhoneUI.cs`: `Open()` (`~329`), `Toggle()` (`~367`), `IsOpen` (`~27`), the notification strip build (`~1916`), `FlashNotification` (`~234`). Read `StoryDirector.cs` `~220-236` (first-contact trigger).

- [ ] **Step 2: Add static flag + persistent-prompt API to PlayerPhoneUI:**

```csharp
public static bool HasEverOpened;            // saved via SaveCollector (§3)

// a non-fading on-screen prompt shown until the phone is first opened
void ShowPersistentOpenPrompt(string msg) { /* build/enable a CanvasGroup label, alpha=1, no fade coroutine */ }
void HidePersistentOpenPrompt() { /* disable it */ }
public void RequestFirstOpenNag()            // called by StoryDirector on first message
{
    if (HasEverOpened) return;
    ShowPersistentOpenPrompt("Press X to open your phone.");
}
```

- [ ] **Step 3: Set flag + dismiss on Open().** In `Open()`, add:

```csharp
if (!HasEverOpened) { HasEverOpened = true; HidePersistentOpenPrompt(); }
```

(Persisting: the flag is captured on next autosave via Task 8; that's sufficient — no need to force an immediate save unless Sam wants it.)

- [ ] **Step 4: Trigger from StoryDirector.** Where the first message currently calls `FlashNotification("Incoming transmission")` (`~228`), also call `PlayerPhoneUI.Instance.RequestFirstOpenNag();`. The existing `VolunteerExternal` HUD line can stay or be removed (keep — it's harmless and reinforces). Only the persistent nag is new.

- [ ] **Step 5: Compile check.** Expected: no new errors.

- [ ] **Step 6: Commit (Tasks 8+9 together)**

```bash
git add "Assets/3 - Scripts/SaveSystem/SaveData.cs" "Assets/3 - Scripts/SaveSystem/SaveCollector.cs" "Assets/3 - Scripts/SaveSystem/NewGameReset.cs" "Assets/3 - Scripts/UI/PlayerPhoneUI.cs" "Assets/3 - Scripts/Story/StoryDirector.cs"
git commit -m "feat(phone): persistent 'Press X to open your phone' nag until first open (§3)"
```

**CHECKPOINT (Sam):** New game → first message → "Press X to open your phone." stays on screen until you press X; never reappears after. Survives reload.

---

## Task 10 (§2): Suit O2 row in VitalsHUD; disable standalone suit bar

**Files:** Modify `Assets/3 - Scripts/Survival/VitalsHUD.cs`, `Assets/3 - Scripts/Survival/OxygenHUD.cs`

- [ ] **Step 1: Read** `VitalsHUD.cs`: `StatRow` inner class (`~53-61`), `BuildStatRow()` (`~344-412`), `BuildCanvas()` row assembly (`~260-342`), `Update()`/`UpdateStat()` (`~115-205`). Read `OxygenHUD.cs`: suit bar build (`~58`, `MakeBar` `~69-120`) + hull bar (`~60`) + visibility (`~130-132`).

- [ ] **Step 2: Add a suit-O2 StatRow.** Mirror an existing row (e.g. thirst): build `_suitO2` row with a cyan gradient (`new Color32(0x5C,0xC8,0xFF,0xFF)` → lighter), add it into the vertical layout (after thirst, before ship rows). Label "O2" or "SUIT O2".

- [ ] **Step 3: Drive it.** In `Update()`, after the health/hunger/thirst updates:

```csharp
if (OxygenManager.Instance != null)
    UpdateStat(_suitO2, OxygenManager.Instance.SuitPercent);
```

(Match `UpdateStat` signature; reuse the change-detected percent text path.)

- [ ] **Step 4: Disable standalone suit bar in OxygenHUD.** In `OxygenHUD.BuildUI()`, stop creating/showing the **suit** bar (comment out its `MakeBar` + remove its update), keep the **hull** bar and its contextual visibility logic intact. Verify nothing else references the removed suit field.

- [ ] **Step 5: Compile check.** Expected: no new errors.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/Survival/VitalsHUD.cs" "Assets/3 - Scripts/Survival/OxygenHUD.cs"
git commit -m "feat(hud): move suit O2 bar into bottom-right vitals card; keep hull bar contextual (§2)"
```

**CHECKPOINT (Sam):** Suit O2 appears in the bottom-right vitals card (cyan), draining in vacuum; top-left suit bar gone; hull bar still appears when piloting/inside.

---

## Task 11 (§1): Main-menu ambient audio (Coplay-generated)

**Files:** Create `Assets/.../Audio/MenuAmbience.*` (+ `.meta`); Modify `Assets/3 - Scripts/UI/MainMenuController.cs`

- [ ] **Step 1: Generate the loop** via `mcp__coplay-mcp__generate_music` — prompt for a slow, dark, space-ambient drone, loopable, ~60–90s. Confirm the clip with Sam before wiring. Import into an Audio folder; note the asset path.

- [ ] **Step 2: Read** `MainMenuController.cs` `Awake`/`Start` (`~40-60`) and `EnsureGameplaySingletons`. Add a serialized `AudioClip menuAmbience;` field (append at END) and a private `AudioSource`. In `Awake`/`Start`, create a looping AudioSource (`loop=true`, modest volume, `playOnAwake=false`), assign the clip, `Play()`. Respect `AudioListener.volume`.

```csharp
[SerializeField] AudioClip menuAmbience;   // appended at end
AudioSource _ambience;
void StartMenuAmbience()
{
    if (menuAmbience == null) return;
    _ambience = gameObject.AddComponent<AudioSource>();
    _ambience.clip = menuAmbience; _ambience.loop = true;
    _ambience.playOnAwake = false; _ambience.volume = 0.5f;
    _ambience.Play();
}
```

- [ ] **Step 3: Assign the clip** to the serialized field on the MainMenuController object in `MainMenu.unity` (via Coplay `set_property` or in-Editor), since the menu is scene-built.

- [ ] **Step 4: Compile check + Sam confirms the track.** Expected: no new errors; ambience loops on the menu.

- [ ] **Step 5: Commit** (include the audio asset + its `.meta`)

```bash
git add "Assets/.../Audio/MenuAmbience.ogg" "Assets/.../Audio/MenuAmbience.ogg.meta" "Assets/3 - Scripts/UI/MainMenuController.cs" Assets/MainMenu.unity
git commit -m "feat(menu): looping space-ambient audio on the main menu (§1)"
```

**Deferred (discussion-gated):** settings screen / solar-system visual — present options to Sam after audio lands; do not build this round.

---

## Self-review notes

- **Spec coverage:** §1 Task 11; §2 Task 10; §3 Tasks 8–9; §4 Tasks 5–6; §5 Tasks 1–2; §6 Task 7; §7 Tasks 3–4. All covered.
- **Type consistency:** `PlayerIsNearOrPiloting(float)` used identically in Tasks 1,2,6,7. `HasEverOpened` static defined in Task 9, consumed in Task 8 (note the build-order dependency — add the static before SaveCollector references it). `ShowLive(Func<string>,…)` defined Task 5, used Task 6. `UpdateStat`/`StatRow` reused from existing VitalsHUD in Task 10.
- **No-CLI-test adaptation:** every task verifies via `check_compile_errors` + a Sam play-test at checkpoints, in place of unit tests (repo has no test runner).
- **Convention guards:** serialized fields appended at end (Tasks 1,6,11); no per-frame `FindObjectOfType` (Task 1 throttles, Task 2 caches in Awake); `rb.position` reads; OxygenHUD/MainMenu singleton seeding left intact.
