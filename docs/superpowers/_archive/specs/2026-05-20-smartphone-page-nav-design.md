# Smartphone Page Navigation — Design

**Status:** Approved (mockup #4 "Bold Tactile" selected)
**Date:** 2026-05-20
**File touched:** `Assets/3 - Scripts/UI/PlayerPhoneUI.cs`

## Goal

Replace the static "— RESERVED —" zone on the smartphone home screen with a three-page paginated home. Pages flip via a left arrow + 3-dot indicator + right arrow widget. The CAMERA button stays in place and is unaffected.

## Pages

| Page | Index | Content |
|---|---|---|
| Apps | 0 | The existing 2×2 grid (Fishingdex / Build / Settings / Map). Unchanged. |
| Vitals | 1 | Four labelled progress bars driven by `ResourceManager`. |
| Quests | 2 | One row per `EarlyGameProgress` flag — strike-through grey when done, cyan dot + label when incomplete. |

The phone opens on page 0 every time. Page index is **not** saved — same convention as map UI state.

## Navigation widget — "Bold Tactile"

Replaces the body of `BuildReservedZone()`. Layout from left to right:

- **Left button.** 32×24, `TileBg` background, 1 px `AccentCyan` border (alpha 0.32), corner radius 6 px. Centered child Image displaying a filled-triangle sprite (pointing left). Hover: background `rgba(92,200,255,0.16)`, border solid `AccentCyan`, triangle white.
- **Dots row.** Three 9×9 dots, 11 px gaps. Inactive = `ButtonGrey` (`#2A4060`). Active = `AccentCyan` with a cyan `UnityEngine.UI.Shadow` glow (effectColor `rgba(92,200,255,0.7)`, zero distance) and `localScale = 1.25`. Update by toggling each dot's `Image.color` + `localScale` + `Shadow.enabled` in a `RefreshDots()` helper.
- **Right button.** Mirror of the left button, triangle pointing right.

The whole strip is laid out via a horizontal `LayoutGroup` inside `_reservedZoneRT` (existing flexible-height container — keep it). Total zone height stays driven by `flexibleHeight = 1` so the layout doesn't shift; arrow buttons just float inside the existing slot.

A new triangle sprite helper `Triangle(bool pointRight)` joins `RoundedRectFilled` / `RoundedRectOutline` / `Disc` / `Ring` in the existing procedural-sprite section of the file. It rasterises a filled isoceles triangle into a small `Texture2D` (16×16 baseline; the Image's RectTransform scales it). Pixels inside the triangle = white (tinted via `Image.color`), outside = transparent. Cached statically per direction (one shared `Sprite` for "left", one for "right") so we don't reallocate on each phone construction.

## State machine

```csharp
int _currentPage = 0;          // 0..2
RectTransform[] _pageRoots = new RectTransform[3];
Image[] _navDots = new Image[3];

void GoToPage(int n) {
    _currentPage = ((n % 3) + 3) % 3;            // wrap (handles negative)
    for (int i = 0; i < 3; i++)
        _pageRoots[i].gameObject.SetActive(i == _currentPage);
    RefreshDots();
}
```

`Open()` calls `GoToPage(0)` after the slide-in animation finishes (or just sets `_currentPage = 0` and calls `GoToPage(0)` directly — same result since the apps page is active by default in the prefab build).

Wrap rule: right from page 2 → page 0; left from page 0 → page 2. The user confirmed they want this.

## Page construction

A new `_pageHostRT` RectTransform takes the slot where the app grid used to live in the screen's `VerticalLayoutGroup`. `LayoutElement.preferredHeight = 170f`. The three page roots are children of `_pageHostRT`, each anchored full-stretch (`anchorMin = (0,0)`, `anchorMax = (1,1)`, zero offsets) so they overlap perfectly. Only one is `SetActive(true)` at a time.

```
ScreenVLG ─┬─ StatusBar
           ├─ NotificationStrip
           ├─ _pageHostRT  (preferredHeight = 170)
           │    ├─ _pageRoots[0]   ← built by BuildAppGrid (existing 2×2)
           │    ├─ _pageRoots[1]   ← built by BuildVitalsPage  (inactive on start)
           │    └─ _pageRoots[2]   ← built by BuildQuestsPage  (inactive on start)
           ├─ ReservedZone (now the nav widget)
           └─ CameraButton
```

`BuildAppGrid()` is renamed to `BuildAppsPage()` and now parents itself to `_pageHostRT` instead of `_screenRT`. Its `GridLayoutGroup` config (2 columns, cellSize 78×78, etc.) is unchanged. `_appGridRT` becomes a `_pageRoots[0]` alias for backward reference; only used internally, no external callers.

Two new methods alongside it:

```csharp
void BuildVitalsPage()  // builds _pageRoots[1] as a child of _pageHostRT
void BuildQuestsPage()  // builds _pageRoots[2] as a child of _pageHostRT
```

**Vitals page.** A `VerticalLayoutGroup` with 4 rows. Each row:
- 54-px-wide TMP label ("HUNGER" etc., 9 pt, AccentCyan, letter-spacing 1).
- Flexible-width track: dark cyan-tinted background, child fill Image whose `anchorMax.x` is set to the live percent.

Cached refs:
```csharp
RectTransform[] _vitalFills = new RectTransform[4];   // hunger/thirst/health/shipPower
int[] _lastVitalPct = new int[] {-1,-1,-1,-1};        // change-detect
```

In `Update`, only while `_currentPage == 1` and `ResourceManager.Instance != null`:
```csharp
RefreshVital(0, ResourceManager.Instance.HungerPercent);
RefreshVital(1, ResourceManager.Instance.ThirstPercent);
RefreshVital(2, ResourceManager.Instance.HealthPercent);
RefreshVital(3, ResourceManager.Instance.ShipPowerPercent);
```

`RefreshVital(i, p)` rounds `p*100` to int, returns if it matches `_lastVitalPct[i]`, otherwise updates the cache + sets `_vitalFills[i].anchorMax = new Vector2(Mathf.Clamp01(p), 1f)`. Matches the existing change-detection pattern from `PlayerWallet` ammo HUD and `CompassHUD`.

**Quests page.** Shows a **sliding window of 5 quests** centered on current progress, not all 12. Pattern: the 2 most-recently-completed quests + the next 3 incomplete (or, near the start/end of the arc, biased so 5 always show). This matches the mockup, gives the player "you just did this!" feedback, and keeps row height comfortable (~30 px per row in a 170 px slot).

Quest list as a static config table at the top of the file:

```csharp
struct QuestRow { public System.Func<bool> Read; public string Label; }
static readonly QuestRow[] _quests = new QuestRow[] {
    new QuestRow{ Read = () => EarlyGameProgress.NoteRead,               Label = "Read the note" },
    new QuestRow{ Read = () => EarlyGameProgress.RodPickedUp,            Label = "Pick up the fishing rod" },
    new QuestRow{ Read = () => EarlyGameProgress.FirstFishCaught,        Label = "Catch your first fish" },
    new QuestRow{ Read = () => EarlyGameProgress.OneOfEachCaught,        Label = "Catch one of each fish" },
    new QuestRow{ Read = () => EarlyGameProgress.FirstMealEaten,         Label = "Cook and eat a meal" },
    new QuestRow{ Read = () => EarlyGameProgress.WaterBottleDrunk,       Label = "Drink from the bottle" },
    new QuestRow{ Read = () => EarlyGameProgress.ReturnedHome,           Label = "Return home" },
    new QuestRow{ Read = () => EarlyGameProgress.TevReturnedDialogueDone,Label = "Speak to Tev" },
    new QuestRow{ Read = () => EarlyGameProgress.CabinBuilt,             Label = "Build a cabin" },
    new QuestRow{ Read = () => EarlyGameProgress.VillageCoordsGiven,     Label = "Get village coordinates" },
    new QuestRow{ Read = () => EarlyGameProgress.FishVendorVisited,      Label = "Visit the fish vendor" },
    new QuestRow{ Read = () => EarlyGameProgress.GoodsVendorVisited,     Label = "Visit the goods vendor" },
};
```

`BuildQuestsPage()` creates a `VerticalLayoutGroup` with exactly 5 row slots; each slot is a cached `(Image dot, TMP_Text label)` pair.

```csharp
const int VisibleQuestRows = 5;
struct QuestRowUI { public Image dot; public TextMeshProUGUI label; }
QuestRowUI[] _questRowUI = new QuestRowUI[VisibleQuestRows];

void RefreshQuests() {
    // Find the first incomplete index; the window shows
    // (firstIncomplete - 2) .. (firstIncomplete + 2) — clamped to [0, _quests.Length].
    int firstIncomplete = _quests.Length;
    for (int i = 0; i < _quests.Length; i++)
        if (!_quests[i].Read()) { firstIncomplete = i; break; }
    int start = Mathf.Clamp(firstIncomplete - 2, 0, Mathf.Max(0, _quests.Length - VisibleQuestRows));
    for (int slot = 0; slot < VisibleQuestRows; slot++) {
        int q = start + slot;
        if (q >= _quests.Length) { _questRowUI[slot].label.text = ""; _questRowUI[slot].dot.enabled = false; continue; }
        bool done = _quests[q].Read();
        _questRowUI[slot].dot.enabled = true;
        _questRowUI[slot].dot.color = done ? ButtonGrey : AccentCyan;
        _questRowUI[slot].label.color = done ? ButtonGrey : LabelWhite;
        _questRowUI[slot].label.text  = done ? $"<s>{_quests[q].Label}</s>" : _quests[q].Label;
    }
}
```

Refresh is much cheaper than vitals — flags only flip on game events, not continuously — so refresh on:
1. **Phone open** — called at the end of the slide-in animation.
2. **Page enter** — called by `GoToPage` whenever the new page is 2.

(Refreshing on every `Update` while page 2 is visible would also be cheap; we skip it because flags don't change while the phone is open — the player can't both walk to an NPC and have the phone open at the same time.)

## File-level layout

The screen's existing `VerticalLayoutGroup` (in `BuildScreen()`) stacks:
1. StatusBar (~14 px)
2. NotificationStrip (~28 px)
3. **App grid → wrapped in a Page Host** (170 px, contains all 3 page roots)
4. **Nav widget (was Reserved zone)** (30 px)
5. CameraButton (30 px)

The vertical layout doesn't change shape — same five rows, same heights. Only row 3's content (now hosts 3 swappable page roots) and row 4's content (now nav widget, was placeholder text) change.

## What is *not* changing

- The chassis, bezels, status bar, notification strip, camera lens, side buttons, close button, movement-warning toast, camera mode logic, slide animation, ESC/X/C handling, EventSystem navigation suppression — all untouched.
- The 2×2 app grid is identical, just lives inside a wrapping page root now.
- The CAMERA button stays where it is. Camera mode is entered by clicking it (or pressing C). Page nav has no effect inside camera mode.
- Save schema. UI state isn't saved (consistent with map UI / fishingdex UI).

## Acceptance criteria

1. Pressing X to open the phone shows page 0 (apps) every time.
2. Clicking the right arrow flips to page 1 (vitals). Right again → page 2 (quests). Right again → wraps to page 0.
3. Clicking the left arrow on page 0 wraps to page 2. Left on page 2 → page 1. Left on page 1 → page 0.
4. The active dot is cyan, scaled 1.25×, with a glow; the other two are grey at normal scale.
5. Vitals bars track `ResourceManager` live (verifiable: bite food → hunger bar grows; drink water → thirst bar grows; take damage → health bar shrinks).
6. Quest rows show grey strike-through for completed flags and cyan-dot active rows for incomplete. Refreshes when the phone opens.
7. The CAMERA button and camera mode work exactly as they did before this change.
8. The build sanity-check passes: the phone widget reflects the same state in a built EXE as in the Editor (per the `EnsureGameplaySingletons` rule — `PlayerPhoneUI` is already seeded, no new singleton being added).

## Risk / things to double-check

- **No new singletons.** All state lives on the existing `PlayerPhoneUI`, so the MainMenu auto-create trap doesn't apply.
- **No new save fields.** Page index intentionally not persisted.
- **EventSystem.** The new arrow buttons are `Button` components inside the phone screen. The phone's existing pattern of disabling `sendNavigationEvents` while open already prevents Space/keyboard from triggering them — only pointer clicks fire. This is the desired behavior; matches the app tiles.
- **No floating-origin or physics interaction.** Pure UI.
