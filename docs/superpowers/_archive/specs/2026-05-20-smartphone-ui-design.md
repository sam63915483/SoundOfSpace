# Smartphone UI — Design

**Date:** 2026-05-20
**Status:** Design approved by user, ready for implementation plan.

## Goal

Add a diegetic smartphone HUD the player pulls out by pressing **X**, both on foot and while piloting the ship. It replaces the existing X-key auto-align toggle, gives a unified entry point to four existing menus (Fishingdex / Build / Settings / Map), and reserves visual space for future content (notifications, widgets) without re-laying-out.

## Visual style — Cyan Scanner v2

The phone reads as the same UI family as `VitalsHUD`, `InteractPromptUI`, `TutorialUI`, and `AutoAlignToggleUI`. Dark navy chassis with cyan accents, scanner-bracket corners on app tiles, monospace status text. Full smartphone chassis with visible silent switch + volume rocker on the left edge, power button on the right edge, top speaker grille, and a small LED camera dot. Smaller app tiles (≈ 78 px max) leaving deliberate empty space inside the screen for future content.

### Palette (re-used from existing HUDs)

- Chassis background: `#0A1828` α `F2`
- Chassis border: `#78C8FF` α `73`
- Screen background gradient: `#060F1A` → `#02080F`
- Accent / LED cyan: `#5CC8FF` (with `0 0 6px` glow on glyphs)
- Label text: `#EAF6FF`
- App tile background: `#0F192A` α `D9`
- Side buttons (chassis): `#2A4060`

Re-uses `UIPanelSprites.GetBeveledPanel()` / `GetBeveledOutline()` for chassis + screen, and `HudFontResolver.Apply()` for all text.

## Position & layout

- Canvas: `ScreenSpaceOverlay`, `sortingOrder = 850` (above HUDs at 800–820, below `TabbedPauseMenu` at 1000).
- Phone `RectTransform`: anchored bottom-center, pivot bottom-center.
- Horizontal placement: phone sits immediately to the **left** of the hotbar. X offset computed from the hotbar's width at runtime: `-hotbarWidth/2 - gap - phoneWidth/2` from screen center. Gap ≈ 32 px.
- With jetpack equipped (i.e. `BoostMeterUI` visible on screen), the phone occupies the lane between `BoostMeterUI` and the hotbar. `PlayerPhoneUI.Update` polls `BoostMeterUI` visibility and re-anchors so the phone never overlaps it.
- Phone dimensions: ≈ 210 px wide × 430 px tall.

## Animation

- **Closed:** `anchoredPosition.y = -(phoneHeight + 16)` (fully off-screen below).
- **Open:** `anchoredPosition.y = 16` (above bottom edge with a small margin).
- **Tween:** 0.25 s. Ease-out on open, ease-in on close. Concurrent `CanvasGroup.alpha` fade 0→1 to soften the start of the slide.
- Tween uses `Time.unscaledDeltaTime` so it still animates if `Time.timeScale == 0`.

## Phone screen content

```
StatusBar         time (real-world HH:mm) · battery icon + %
NotificationStrip "NO NEW ALERTS" + LED dot  (placeholder, public hook)
AppGrid (2×2)     Fishingdex ⌬ | Build ▦
                  Settings   ⚙ | Map   ◎
ReservedZone      "— RESERVED —" dashed border  (placeholder, public hook)
PutAwayButton     "PUT AWAY" — closes phone
```

- **Time:** `DateTime.Now.ToString("HH:mm")`, updated once per second with change-detection so the `TMP_Text` isn't reassigned every frame.
- **Battery:** integer in `[20, 95]` chosen once in `Awake()`. Rendered as `"{pct}%"` plus a horizontal battery shell whose fill is scaled to `pct/100`. Does not tick down during a session.
- **NotificationStrip** and **ReservedZone**: exposed as public `RectTransform NotificationStripRoot` and `RectTransform ReservedZoneRoot` on `PlayerPhoneUI` so future features can parent into them without touching layout code. Also a `SetNotificationText(string)` helper on the strip.

## Input contract

### X-key (toggle)

`PlayerPhoneUI.Update` listens for `KeyCode.X`. Allowed only when:
- `PlayerController.isInDialogue == false`
- `TabbedPauseMenu.Instance == null || !TabbedPauseMenu.Instance.IsOpen`
- Phone is not currently mid-animation

On X press: `Toggle()` flips state and starts the slide+fade coroutine.

### While phone is open

- `Cursor.lockState = CursorLockMode.None`, `Cursor.visible = true`.
- Look input blocked via a static `PlayerPhoneUI.IsOpen` flag that `PlayerController` (mouse-look read site) early-returns on. WASD movement on foot still works.
- Hotbar number keys 1–5 still work (player can swap held items with phone out).
- **While piloting:** every frame the phone is open AND `Ship.PilotedInstance != null`, poll for any ship-control input (W/A/S/D, Space, LCtrl, Shift, Q/E, primary thrust button). Any such press auto-closes the phone (slide-out animation), and control returns to the ship the same frame the phone finishes animating. The X key itself is excluded from the auto-close check.

### ESC stacking

| State when ESC pressed | Behavior |
|---|---|
| Phone open, pause menu closed | Phone closes, pause menu stays closed |
| Phone closed, pause menu closed | Pause menu opens (unchanged from today) |
| Phone open, pause menu somehow already open | Pause menu closes (its own ESC handler wins), phone state untouched |

Implementation: `PlayerPhoneUI` exposes a static `bool ConsumedEscapeThisFrame`, cleared in `LateUpdate`. In `Update`, if ESC is pressed AND phone is open, it closes the phone and sets the flag. `TabbedPauseMenu.Update` checks the flag at the start of its ESC branch and early-returns if set. Second ESC press (next frame) opens the pause menu because the flag has been cleared and the phone is now closed.

### App clicks

Each app button → `PlayerPhoneUI.OpenApp(AppKind kind)`:

- `Fishingdex` → `FishingdexManager.Instance.OpenDex()`
- `Build` → `BuildMenuUI.Instance.Open()`
- `Settings` → `TabbedPauseMenu.Instance.Open("Settings")` (may require adding an `Open(string tabName)` overload — confirm during implementation)
- `Map` → `SolarSystemMapController.Instance.Open()`

Each call sequence: phone plays its ~150 ms slide-out → target UI's own `Open()` runs after the slide completes. Like tapping an app on a real phone — the home screen hides before the app shows.

## GameObject hierarchy

```
PlayerPhoneUI (auto-singleton, DontDestroyOnLoad)
└── Canvas
    └── Phone (RectTransform — animates anchoredPosition.y + CanvasGroup.alpha)
        ├── Chassis (Image — beveled rounded rect, cyan border)
        │   ├── SilentSwitch, VolUp, VolDn   (left-edge button images)
        │   ├── PowerButton                   (right-edge button image)
        │   ├── SpeakerGrille                 (top center)
        │   └── CameraDot                     (top right of speaker)
        └── Screen (Image — inner darker panel, clipped via RectMask2D)
            ├── StatusBar
            ├── NotificationStrip
            ├── AppGrid (2×2 GridLayoutGroup)
            │   ├── App[Fishingdex]
            │   ├── App[Build]
            │   ├── App[Settings]
            │   └── App[Map]
            ├── ReservedZone
            └── PutAwayButton
```

## Auto-align removal scope

The current X-key auto-align is removed entirely (user-confirmed scope: "Remove the feature entirely").

**Deleted:**
- `Assets/3 - Scripts/UI/AutoAlignToggleUI.cs` (whole file)

**Modified:**
- `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs` — remove `autoAlignEnabled` field, `autoAlignDegPerSec` field, and the auto-align rotation block in `FixedUpdate` (around line 746, lines 734–768).
- `Assets/3 - Scripts/SaveSystem/SaveData.cs` — remove `autoAlignEnabled` from `ShipSave` and `ExtraShipSave`.
- `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` — remove capture + apply lines for `autoAlignEnabled` in both ship paths.
- `Assets/3 - Scripts/UI/MainMenuController.cs` — remove `AutoAlignToggleUI` seed from `EnsureGameplaySingletons` if present.

**Save-system compatibility:** `JsonUtility` ignores unknown fields, so old saves load cleanly into the new schema (the `autoAlignEnabled` JSON value is silently dropped). New saves don't write the field. No version bump or migration needed.

## New files

- `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` — auto-singleton, owns the phone Canvas, procedurally builds the chassis + screen, runs the slide+fade animation, handles X / ESC / ship-input contracts, routes app clicks to existing UIs.
- `Assets/3 - Scripts/UI/PhoneAppButton.cs` (optional helper, will decide during plan) — small per-button component holding an `AppKind` enum and routing clicks back to `PlayerPhoneUI.OpenApp(kind)`. May be inlined as lambdas instead.

## Other modified files

- `Assets/3 - Scripts/UI/MainMenuController.cs` — add `PlayerPhoneUI` seed block to `EnsureGameplaySingletons` per the build/MainMenu rule documented at the top of `CLAUDE.md`. (Without this, the phone won't auto-create in built games because the `AfterSceneLoad` callback fires once in MainMenu and skips creation there.)
- `Assets/3 - Scripts/UI/TabbedPauseMenu.cs` — gate its ESC branch on `PlayerPhoneUI.ConsumedEscapeThisFrame`.
- `Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs` — early-return the mouse-look read site when `PlayerPhoneUI.IsOpen`.

## Edge cases

| Case | Behavior |
|---|---|
| Open phone while in dialogue | X press ignored (input contract gates on `isInDialogue`) |
| Enter dialogue while phone open | Phone force-closes (no animation) via `NPCConversationTracker.OnConversationStarted` |
| Player dies while phone open | Force-close via `ResourceManager.OnDeath` |
| Enter / exit ship with phone open | Phone stays open; layout poll re-anchors when `BoostMeterUI` visibility changes |
| Save with phone open | Phone is transient, not saved. Loads always start with phone closed (mirrors `BuildMenuUI` and `SolarSystemMapController`) |
| Scene reload | Singleton survives via `DontDestroyOnLoad`. Phone force-closes on scene transition |
| App target singleton missing (e.g. `BuildMenuUI.Instance == null`) | App button no-ops with `Debug.LogWarning`; phone stays open. No crash |
| ESC during slide animation | Opening → slide aborts and reverses to closed. Closing → no-op |
| X spam during animation | Mid-animation gate ignores the press |

## Testing checklist

1. **On-foot toggle** — X opens / closes; works while walking, jumping, holding items.
2. **Piloting toggle** — X opens; pressing W or any thrust key auto-closes; ESC closes without opening pause.
3. **ESC stacking** — phone open → ESC closes phone (no pause). ESC again → pause menu opens. Phone closed → ESC opens pause directly (unchanged).
4. **Layout** — phone sits left of hotbar. Equipping jetpack does not cause overlap with `BoostMeterUI`.
5. **App routing** — each of the 4 apps opens its respective UI, phone closes first.
6. **Time + battery** — system clock format `HH:mm` matches OS, battery shows a stable random int in [20, 95] for the session, refreshed next session.
7. **Auto-align gone** — no `[X] AUTO ALIGN` pill in any ship; ships do not auto-upright when piloted.
8. **Build sanity** — built game, start from MainMenu, load gameplay scene, phone works. (Tests the `EnsureGameplaySingletons` seed.)
9. **Save round-trip** — saving / loading with phone closed = phone closed on load. Saving with phone open = phone closed on load. Old saves with `autoAlignEnabled` field load cleanly (field ignored).

## Out of scope (intentionally)

- Real notifications system (just placeholder strip + public hook)
- Lock screen / unlock animation
- Multiple home-screen pages, swiping
- Haptic / sound feedback on tap
- Persistent battery (random per session as requested)
- Quests, mini-games, contacts, photos, anything beyond the four launchers
- App icons as PNG sprites (using unicode glyphs as in the mockup; can be swapped without layout changes)

## File summary

**New (2):**
- `Assets/3 - Scripts/UI/PlayerPhoneUI.cs`
- `Assets/3 - Scripts/UI/PhoneAppButton.cs` (optional)

**Modified (6):**
- `Assets/3 - Scripts/UI/MainMenuController.cs` (add phone seed; also remove auto-align seed if present)
- `Assets/3 - Scripts/UI/TabbedPauseMenu.cs` (ESC gate via `PlayerPhoneUI.ConsumedEscapeThisFrame`; possibly add `Open(string tabName)` overload)
- `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs` (strip auto-align field + FixedUpdate block)
- `Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs` (early-return look read when `PlayerPhoneUI.IsOpen`)
- `Assets/3 - Scripts/SaveSystem/SaveData.cs` (drop `autoAlignEnabled` from `ShipSave` + `ExtraShipSave`)
- `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` (drop capture + apply lines for `autoAlignEnabled`)

**Deleted (1):**
- `Assets/3 - Scripts/UI/AutoAlignToggleUI.cs`
