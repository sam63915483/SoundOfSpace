# Interact Prompt + Hotbar Redesign — Design

## Goal

Two related UI overhauls:

1. **Unify every "Press F to ..." prompt** — talk-to-NPC, pickup, hatch, cook, place, close — under one shared component with one font, size, position, and motion. Today each NPC has its own scene-bound `TextMeshProUGUI talkPromptText` and pickups write to a separate `GameUI.interactionInfo` text element. They're inconsistent because nothing forces them to match.

2. **Revamp the hotbar** — replace the text-only slot labels with proper icons, lift/scale the active slot, and add a floating name callout above the equipped slot. Current bar reads as primitive because every slot looks identical at rest and the active state is subtle.

Visual targets, picked from the brainstorm mockups:
- **Prompt:** option A — beveled HUD pill (matches `TutorialUI` 1:1: clipped top-left/bottom-right corners, cyan LED bar on the left edge, dark navy fill, `[F]` keycap glyph).
- **Hotbar:** option B — rounded slots, active slot scales up and lifts. Rounded slot shape continues what's already in `Hotbar.cs` today; the **active-slot name callout reuses the prompt pill's beveled language** so the two systems read as the same family.

## Non-goals

- No layout/anchor moves of other HUD elements (resource HUD, wallet card, compass, tutorial pill).
- No changes to `TutorialUI` itself — the new prompt component *clones* its visual style but is its own class.
- No save/load impact. Hotbar contents are already saved; this is pure visual.
- No controller-glyph rework — `PromptGlyphs` already handles `<sprite name="F">` / `<sprite name="X">` swapping; we keep that.

---

## Part 1 · `InteractPromptUI`

### Component

New file: `Assets/3 - Scripts/UI/InteractPromptUI.cs`. Modeled on `TutorialUI`:

- `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` auto-create, skipped in `MainMenu` scene
- `DontDestroyOnLoad` singleton (matches `TutorialUI`, `Hotbar`, `PlayerWallet`)
- Builds its own `Canvas` (ScreenSpaceOverlay, sortingOrder = 200 — below tutorial pill at 500, above hotbar at 50)
- Anchors **bottom-center**, ~140 px above the bottom edge so it sits clearly above the hotbar (hotbar bottom margin is 36, slot height 64 + lift ≈ ~110 px total).

Visual: identical to the tutorial pill but compact and single-line.
- Same beveled panel sprite (reuse `MakeBeveledPanelTexture` — copy/extract; not worth a refactor for one extra caller)
- Same cyan LED accent bar on the left, same border, same bordered outline, same colors (`#081220` → `#0A1828` gradient body, `#78C8FF73` border, `#5CC8FF` LED)
- Single TMP text field in the body — no `// PROMPT` header, no completion row
- `DecorateKeyGlyphs` (also extracted/reused from `TutorialUI`) wraps `<b>F</b>` / `<b>X</b>` into the bracketed cyan keycap

### Static API

```csharp
public static void Show(Object owner, string text);   // sticky, stays until owner clears
public static void Clear(Object owner);               // clears iff owner matches current
public static void ShowOneShot(string text, float seconds = 3f);  // for legacy DisplayInteractionInfo
```

The owner pattern matches what `GameUI` already does — `GameUI.ShowInteractionPrompt(this, ...)` carries an owner, and `GameUI.ClearInteractionPrompt(this)` only clears when ownership matches. Replicate that semantics so `Interactable.Update`'s per-frame re-assert keeps working.

### Animation

- **Show:** slide up from `+slideOffset` (40 px) to rest, fade alpha 0→1 over 250 ms.
- **Hide:** reverse — slide down + fade out over 200 ms.
- **Text change while shown** (e.g. an NPC swapping from "talk" to "trade" prompt during the same conversation): no animation — just swap. Re-revealing per character every text change would be noisy.

No typewriter on prompts. The prompt is short and asserts every frame the player's in range; a typewriter on every re-assert is wrong. The tutorial pill uses a typewriter because tip text is multi-line and shown once; that doesn't apply here.

### Refactor — call sites

| Caller | Today | After |
|---|---|---|
| `GameUI.ShowInteractionPrompt(owner, text)` | writes to `interactionInfo.text` | delegates to `InteractPromptUI.Show(owner, text)` — and disables the scene-bound `interactionInfo` GameObject so it doesn't double up |
| `GameUI.DisplayInteractionInfo(text)` | legacy 3 s timer write | `InteractPromptUI.ShowOneShot(text, 3f)` |
| 7 NPC scripts (`NPCDialogue`, `TevDialogue`, `RandomAlienDialogue`, `GuitarShopNPC`, `BonfireNPCDialogue`, `FishMarketNPC`, `Alien7Vendor`) | each calls `talkPromptText.text = "Press F to talk"` and toggles `talkPromptText.gameObject.SetActive` | each calls `InteractPromptUI.Show(this, "Press F to talk")` on enter, `InteractPromptUI.Clear(this)` on exit / dialogue start. The `talkPromptText` field stays as-is — the new code never reads it, so existing scene wiring is harmless. A future scene pass can remove both the field and the orphaned GameObject together. |
| `BonfireInteraction` cook-panel close hint | builds `_closeHintText` procedurally inside the cook panel | `InteractPromptUI.Show(this, "Press F to close")` while panel is open. Drop the `_closeHintText` build entirely. |
| `FishMarketNPC` sell-panel close hint | same | same — `InteractPromptUI.Show(this, "Press F to close")`, drop `_closeHintText`. |

**Owner conflict resolution:** if NPC A and NPC B both call `Show(...)` because their trigger zones overlap, last write wins (matches the current `GameUI` behaviour — `_owner = owner` overwrites). `Interactable.Update`'s per-frame re-assert means whichever zone the player is in *now* wins on the next tick.

**Scene cleanup:** existing `talkPromptText` GameObjects in the scene are no longer driven by code, but they still exist in the hierarchy. We disable them on first `Awake` (`if (talkPromptText != null) talkPromptText.gameObject.SetActive(false);` already happens in most NPCs — keep that path). No deletion required; they'll be cleaned up in a follow-up scene pass if desired.

---

## Part 2 · Hotbar redesign

### Visual changes

| Spec | Current | New |
|---|---|---|
| Slot size at rest | 84×84 | 64×64 |
| Slot corner radius | rounded (GalaxyHudKit `RoundedSprite`) | rounded (kept) |
| Active slot size | 84×84 (same as rest) | 80×80, lifted +8 px |
| Active border | cyan pulse (kept) | cyan pulse (kept), stronger outer glow (`0 0 22px rgba(92,200,255,0.55)`) |
| Slot content | `itemLabel` text ("WATER", "ROD", …) | `itemIcon` image, key number small in top-right |
| Active label | item label colored brighter, in slot | floating beveled name plate **above** the active slot only |
| Empty slot | dim text | dim icon-less slot, key number only |

Slot spacing 12 px and total width recompute (5 × 64 + 4 × 12 = 368, plus padding). Bottom margin unchanged at 36.

### Active-slot animation

Add a per-slot animation coroutine:

```csharp
IEnumerator AnimateSlotState(SlotVisuals v, bool active);
```

Lerps `RectTransform.sizeDelta` (64→80 or 80→64) and `anchoredPosition.y` (0→+8 or back) over 120 ms with cubic ease. Floating name plate fades in/out with the same coroutine. Triggered from `Refresh()` whenever the active slot changes — track previous-active to fire the transition only once.

### Floating name plate

Built once in `BuildUI()`, parented to `bar` (the hotbar root) so it can position above any slot. It's the **bridge to the prompt pill's visual language**:
- Same beveled top-left/bottom-right corners
- Same dark navy fill, cyan border, cyan LED on the left
- Single line: item name in cyan-shadow white text
- Anchored bottom of itself = top of active slot + 10 px gap

`Refresh` finds the active slot (equipped slot if anything is equipped, otherwise the cycle cursor) and positions the name plate's `anchoredPosition.x` to that slot's x. The name plate hides when the active slot's `id == ItemId.None` (i.e., cursor sitting on an empty slot with nothing equipped).

### Icons — sourcing and wiring

**Authoring:** five monochrome cyan icons, transparent background, 256×256 PNG (downscaled by Unity to needed sizes). Generate via Unity MCP `mcp__coplay-mcp__generate_or_edit_images`. Style brief sent to the generator:

> "Flat, monochrome cyan-on-transparent UI icon, 256×256 PNG, sci-fi HUD aesthetic, single colour fill `#5CC8FF`, no gradients, no outline tricks, clean vector look, centred in frame with 12 % padding."

The five subjects:
1. Water bottle (item: drinking-flask silhouette)
2. Fishing rod (item: angled rod with a hanging line/hook)
3. Axe (item: short-handled axe head)
4. Pistol (item: side-view handgun silhouette)
5. Acoustic guitar (item: front-view guitar body silhouette)

**Storage:** `Assets/2 - Materials/HotbarIcons/` — new folder. Files: `water_bottle_icon.png`, `fishing_rod_icon.png`, `axe_icon.png`, `pistol_icon.png`, `guitar_icon.png`. Each PNG imported as Sprite (2D and UI), point or bilinear, alpha is transparency.

If MCP image generation produces something off-style, fall back: hand-author SVG-to-PNG in any vector tool, same brief, same names. Either path lands at the same files; the `Hotbar` code is decoupled from how they're authored.

**Wiring:** add to each of the 5 controllers (`WaterBottleController`, `FishingRodController`, `GuitarController`, `AxeController`, `PistolController`):

```csharp
[Header("UI")]
public Sprite hotbarIcon;
```

Assigned once via the Player prefab inspector (each of these controllers is a sibling MonoBehaviour on the Player root, per `CLAUDE.md`).

`Hotbar.BuildRegistry` reads `controller.hotbarIcon` and stores it on the `Entry`. `Refresh` pushes the sprite onto the slot's `Image`. Null-safe — if the sprite isn't assigned, slot renders empty (key number only) and logs a one-shot warning.

### Save/load

Zero impact. Hotbar persists slot assignments and equipped state today, all unchanged. The visual revamp is render-only.

---

## Coding-convention compliance (per `CLAUDE.md`)

- **Singleton pattern:** `InteractPromptUI` follows the standard `Instance` / `Awake` / `OnDestroy` shape used by `TutorialUI`, `Hotbar`, `PlayerWallet`.
- **No `Resources.Load` in user code:** font lookup goes through `Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF")` — same exception `TutorialUI` already uses; no new violation.
- **No `FindObjectOfType` per-frame:** `InteractPromptUI` doesn't search; it's owner-driven from callers. `Hotbar` already lazy-caches its 5 controllers.
- **No `transform.position` writes on rigidbodies:** UI only. N/A.
- **`MainMenuController.EnsureGameplaySingletons`:** add `InteractPromptUI` to the seeding list so it exists during a load-from-main-menu apply, matching the existing pattern for `TutorialUI`, `Hotbar`, `PlayerWallet`, `WoodInventory`, `BonusTutorial`.

## File-change summary

**New:**
- `Assets/3 - Scripts/UI/InteractPromptUI.cs`
- `Assets/2 - Materials/HotbarIcons/water_bottle_icon.png` (+`.meta`)
- `Assets/2 - Materials/HotbarIcons/fishing_rod_icon.png` (+`.meta`)
- `Assets/2 - Materials/HotbarIcons/axe_icon.png` (+`.meta`)
- `Assets/2 - Materials/HotbarIcons/pistol_icon.png` (+`.meta`)
- `Assets/2 - Materials/HotbarIcons/guitar_icon.png` (+`.meta`)

**Modified:**
- `Assets/3 - Scripts/Scripts/Game/UI/GameUI.cs` — delegate to `InteractPromptUI`
- `Assets/3 - Scripts/UI/Hotbar.cs` — icon swap, lift/scale animation, name plate, smaller slot size
- `Assets/3 - Scripts/UI/MainMenuController.cs` — seed `InteractPromptUI` in `EnsureGameplaySingletons`
- `Assets/3 - Scripts/Pickups/WaterBottleController.cs` — add `hotbarIcon` field
- `Assets/3 - Scripts/Fishing/FishingRodController.cs` — add `hotbarIcon` field
- `Assets/3 - Scripts/Pickups/GuitarController.cs` — add `hotbarIcon` field
- `Assets/3 - Scripts/Pickups/AxeController.cs` — add `hotbarIcon` field
- `Assets/3 - Scripts/Pickups/PistolController.cs` — add `hotbarIcon` field
- `Assets/3 - Scripts/NPC_Dialogue/NPCDialogue.cs` — replace `talkPromptText` writes with `InteractPromptUI`
- `Assets/3 - Scripts/NPC_Dialogue/TevDialogue.cs` — same
- `Assets/3 - Scripts/NPC_Dialogue/RandomAlienDialogue.cs` — same
- `Assets/3 - Scripts/NPC_Dialogue/GuitarShopNPC.cs` — same
- `Assets/3 - Scripts/NPC_Dialogue/BonfireNPCDialogue.cs` — same
- `Assets/3 - Scripts/NPC_Dialogue/BonfireInteraction.cs` — same + drop `_closeHintText` build
- `Assets/3 - Scripts/Vendor/Alien7Vendor.cs` — same
- `Assets/3 - Scripts/Fishing/FishMarketNPC.cs` — same + drop `_closeHintText` build

## Risks / open questions

- **Prompt anchor while in cook/sell panels:** the panels are full-screen modals; an "[F] to close" prompt at bottom-center should still be visible above the panel since `InteractPromptUI` canvas sortingOrder = 200. Verify in editor — if the panels render above 200, raise the prompt sort order.
- **Player prefab assignment:** the 5 `hotbarIcon` fields need to be wired up in the Player prefab once. Document this in CLAUDE.md as a follow-up step in "adding a new equippable."
- **Image-generation style consistency:** five icons generated independently might not share visual weight. If MCP outputs are uneven, regenerate with the same seed/prompt or drop in hand-authored replacements — the code path doesn't care which.
- **Two existing world-space prompts** (`PickupMarker` floating tags above thrusters/cassette pickups) are *not* in scope — they're 3D world-space, separate from the 2D screen-prompt unification. Leave them alone unless the user says otherwise.
