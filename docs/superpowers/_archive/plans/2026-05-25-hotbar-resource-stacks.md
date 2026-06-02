# Hotbar resource stacks — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move wood / crystal / space dust out of the top-left HUD chips and into the 5-slot hotbar as stackable items. Equipping a resource slot is a no-op visual highlight for now; gathering still routes through the existing singletons (`WoodInventory` etc.), which become thin facades over the hotbar's new slot model.

**Architecture:** `Hotbar` becomes the sole source of truth for resource counts via a `Slot { ItemId, int count }` array. Adding the three new `ItemId` enum values (`Wood`, `Crystal`, `SpaceDust`) reuses every existing tool-slot UI path. Resource singletons keep their public API and delegate to `Hotbar`. Save format gains a `HotbarSave` block; legacy total-only saves auto-redistribute on load.

**Tech Stack:** Unity 2022.3 (C# / Assembly-CSharp), TextMeshProUGUI, procedural UI built in code, `JsonUtility` save serialization.

**Spec:** [`docs/superpowers/specs/2026-05-25-hotbar-resource-stacks-design.md`](../specs/2026-05-25-hotbar-resource-stacks-design.md)

**Testing model:** No automated tests in this project. Each task ends with **in-editor verification** in the gameplay scene (`Assets/1.6.7.7.7.unity`) — verify Unity Console shows no compile errors after save, then run the listed in-game checks before committing. The final task runs the full integration sweep including a build-from-MainMenu sanity check per the CLAUDE.md MainMenu trap.

---

## Task 1: Extend Hotbar with the resource data model + resource API

Lay the foundation: add the three new enum values, switch slots from `ItemId[]` to `Slot[]`, and add `GetResourceTotal` / `AddResource` / `SpendResource` / `SetResourceTotal` / `StackMax`. No callers wired up yet; existing tool behavior preserved unchanged.

**Files:**
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs`

- [ ] **Step 1: Extend the `ItemId` enum and add slot struct + stack constants**

  At the top of `Hotbar.cs` (replacing the enum at line 8):

  ```csharp
  public enum ItemId {
      None, WaterBottle, FishingRod, Guitar, Axe, Pistol,
      Wood, Crystal, SpaceDust
  }

  // Per-slot value: either a tool (count=1) or a resource stack (1..StackMax).
  // Empty: id=None, count=0.
  public struct Slot {
      public ItemId id;
      public int count;
  }
  ```

  Then replace the `slots` field (line ~30) and add the stack-max helper:

  ```csharp
  readonly Slot[] slots = new Slot[NumSlots];

  public static int StackMax(ItemId id) {
      switch (id) {
          case ItemId.Wood:      return 100;
          case ItemId.Crystal:   return 20;
          case ItemId.SpaceDust: return 100;
          default: return 1; // tools are unstackable
      }
  }

  static bool IsResource(ItemId id) =>
      id == ItemId.Wood || id == ItemId.Crystal || id == ItemId.SpaceDust;
  ```

- [ ] **Step 2: Update every internal slot reference in `Hotbar.cs` to read `.id`**

  Search the file for `slots[` and update reads:
  - `slots[i] == ItemId.None` → `slots[i].id == ItemId.None`
  - `slots[i] == id` → `slots[i].id == id`
  - `slots[i] == equipped` → `slots[i].id == equipped`
  - Assignments: `slots[i] = ItemId.None` → `slots[i] = default;`
  - Assignments: `slots[i] = id` (in `TryAddItem`) → `slots[i] = new Slot { id = id, count = 1 };`

  In `TryAddItem` (line ~230), replace the body with:

  ```csharp
  void TryAddItem(ItemId id) {
      for (int i = 0; i < NumSlots; i++) if (slots[i].id == id) return;
      for (int i = 0; i < NumSlots; i++)
          if (slots[i].id == ItemId.None) { slots[i] = new Slot { id = id, count = 1 }; return; }
  }
  ```

  In `DetectAcquisitions`'s evict loop (line ~217), replace `slots[i] = ItemId.None;` with `slots[i] = default;`.

- [ ] **Step 3: Add the resource API and `OnResourceChanged` event**

  At a stable location below `BuildRegistry()` (around line 205), insert:

  ```csharp
  public event System.Action<ItemId> OnResourceChanged;

  public int GetResourceTotal(ItemId resource) {
      if (!IsResource(resource)) return 0;
      int sum = 0;
      for (int i = 0; i < NumSlots; i++)
          if (slots[i].id == resource) sum += slots[i].count;
      return sum;
  }

  // Returns leftover amount that didn't fit (0 = fully accepted).
  public int AddResource(ItemId resource, int amount) {
      if (!IsResource(resource) || amount <= 0) return amount > 0 ? amount : 0;
      int cap = StackMax(resource);
      int remaining = amount;
      bool changed = false;

      // Fill existing stacks first.
      for (int i = 0; i < NumSlots && remaining > 0; i++) {
          if (slots[i].id != resource) continue;
          int room = cap - slots[i].count;
          if (room <= 0) continue;
          int take = Mathf.Min(room, remaining);
          slots[i].count += take;
          remaining -= take;
          changed = true;
      }

      // Spill into empty slots.
      for (int i = 0; i < NumSlots && remaining > 0; i++) {
          if (slots[i].id != ItemId.None) continue;
          int take = Mathf.Min(cap, remaining);
          slots[i] = new Slot { id = resource, count = take };
          remaining -= take;
          changed = true;
      }

      if (changed) OnResourceChanged?.Invoke(resource);
      return remaining;
  }

  // All-or-nothing: drain leftmost stacks first, return false if total insufficient.
  public bool SpendResource(ItemId resource, int amount) {
      if (!IsResource(resource)) return false;
      if (amount <= 0) return true;
      if (GetResourceTotal(resource) < amount) return false;

      int remaining = amount;
      for (int i = 0; i < NumSlots && remaining > 0; i++) {
          if (slots[i].id != resource) continue;
          int take = Mathf.Min(slots[i].count, remaining);
          slots[i].count -= take;
          remaining -= take;
          if (slots[i].count <= 0) slots[i] = default;
      }
      OnResourceChanged?.Invoke(resource);
      return true;
  }

  // Used by save-load legacy fallback only. Clears existing stacks then re-adds.
  public void SetResourceTotal(ItemId resource, int total) {
      if (!IsResource(resource)) return;
      for (int i = 0; i < NumSlots; i++)
          if (slots[i].id == resource) slots[i] = default;
      if (total > 0) AddResource(resource, total);
      else OnResourceChanged?.Invoke(resource);
  }
  ```

- [ ] **Step 4: Save and verify the project compiles in Unity**

  Unity recompiles on save. Open the Editor, focus the Console, save `Hotbar.cs`. Expected: no compile errors, no new warnings related to `Hotbar.cs`.

- [ ] **Step 5: Editor sanity check — existing tool hotbar still works**

  Press Play in Unity. In gameplay, verify the existing 5-slot hotbar shows tools as before. Pick up / equip the water bottle (slot 1), confirm number keys 1-5 still toggle equipping, D-pad cycle still works. No visible difference yet.

- [ ] **Step 6: Commit**

  ```bash
  git add "Assets/3 - Scripts/UI/Hotbar.cs"
  git commit -m "feat(hotbar): add Slot{id,count} model + resource API (wood/crystal/dust)"
  ```

---

## Task 2: Refactor equip selection to be slot-driven

Replace the registry-driven `GetEquipped` / `ToggleSlot` / `CycleSlot` logic with a slot-driven model so a slot holding a resource can be "selected" (highlight only) without trying to call a controller's ForceEquip.

**Files:**
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs`

- [ ] **Step 1: Add `_equippedSlot` state field**

  Below the existing `_cycleCursor` field (~line 241), add:

  ```csharp
  // Index of the slot the player has currently selected, regardless of whether
  // its contents is a tool (controller is equipped) or a resource (highlight only).
  // -1 = nothing selected.
  int _equippedSlot = -1;
  ```

- [ ] **Step 2: Rewrite `ToggleSlot` to dispatch by slot contents**

  Replace the existing `ToggleSlot` method body (~line 284) with:

  ```csharp
  void ToggleSlot(int idx) {
      var slot = slots[idx];
      ItemId currentEquipped = GetEquipped();
      bool togglingOff = slot.id != ItemId.None && slot.id == currentEquipped;
      UnequipAll();
      if (togglingOff || slot.id == ItemId.None) {
          _equippedSlot = -1;
          _cycleCursor = -1;
          return;
      }
      _cycleCursor = idx;
      _equippedSlot = idx;
      if (!IsResource(slot.id)) Equip(slot.id);
      // Resources: no controller call. _equippedSlot drives highlight.
  }
  ```

- [ ] **Step 3: Rewrite `CycleSlot` to walk `_equippedSlot`**

  Replace the existing `CycleSlot` method body (~line 261) with:

  ```csharp
  void CycleSlot(int step) {
      // Seed from currently-equipped tool slot if cursor is unset.
      if (_cycleCursor < 0) {
          ItemId equipped = GetEquipped();
          if (equipped != ItemId.None) {
              for (int i = 0; i < NumSlots; i++)
                  if (slots[i].id == equipped) { _cycleCursor = i; break; }
          }
      }
      int next = _cycleCursor < 0
          ? (step > 0 ? 0 : NumSlots - 1)
          : ((_cycleCursor + step) % NumSlots + NumSlots) % NumSlots;
      _cycleCursor = next;
      UnequipAll();
      var slot = slots[next];
      if (slot.id == ItemId.None) { _equippedSlot = -1; return; }
      _equippedSlot = next;
      if (!IsResource(slot.id)) Equip(slot.id);
  }
  ```

- [ ] **Step 4: Update `GetEquipped` so resource selection is also reported**

  Replace the existing `GetEquipped` body (~line 299) with:

  ```csharp
  ItemId GetEquipped() {
      // Prefer the slot-driven answer (covers resources).
      if (_equippedSlot >= 0 && _equippedSlot < NumSlots) {
          var sid = slots[_equippedSlot].id;
          if (sid != ItemId.None) {
              // For tools, double-check controller state — dialogue/phone may have
              // force-unequipped under us. If desynced, clear.
              if (!IsResource(sid)) {
                  if (_registry != null) {
                      for (int i = 0; i < _registry.Length; i++)
                          if (_registry[i].Id == sid && _registry[i].IsEquipped()) return sid;
                  }
                  _equippedSlot = -1;
                  return ItemId.None;
              }
              return sid;
          }
      }
      // Fallback: a controller may have been equipped externally (e.g. SaveCollector
      // restored axe via ApplyEquipment). Sync _equippedSlot to it.
      if (_registry != null) {
          for (int i = 0; i < _registry.Length; i++) {
              if (!_registry[i].IsEquipped()) continue;
              for (int j = 0; j < NumSlots; j++)
                  if (slots[j].id == _registry[i].Id) { _equippedSlot = j; break; }
              return _registry[i].Id;
          }
      }
      return ItemId.None;
  }
  ```

- [ ] **Step 5: Extend `UnequipAll` to clear the resource selection too**

  Replace the existing `UnequipAll` body (~line 307) with:

  ```csharp
  void UnequipAll() {
      if (_registry != null) {
          for (int i = 0; i < _registry.Length; i++)
              if (_registry[i].IsEquipped()) _registry[i].ForceUnequip();
      }
      // Clear resource highlight too (caller will set _equippedSlot if a new slot is being selected).
      if (_equippedSlot >= 0 && _equippedSlot < NumSlots && IsResource(slots[_equippedSlot].id))
          _equippedSlot = -1;
  }
  ```

- [ ] **Step 6: Save and verify compile**

  Save `Hotbar.cs`. Console: no errors. The previously-working tool flow should still work.

- [ ] **Step 7: Editor sanity check**

  Play. Equip water bottle (1), axe (4), pistol (5) — they swap as before. Press the same number twice to toggle off. D-pad cycle hits all slots with wrap.

- [ ] **Step 8: Commit**

  ```bash
  git add "Assets/3 - Scripts/UI/Hotbar.cs"
  git commit -m "refactor(hotbar): slot-driven equip selection to support resource stacks"
  ```

---

## Task 3: Add stack-count overlay + resource swatch visuals

Resource slots need a colored placeholder swatch in the middle and a stack count in the bottom-right. The name plate format gains `×N` for resources.

**Files:**
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs`

- [ ] **Step 1: Add a `countText` field to `SlotVisuals`**

  Add to the `SlotVisuals` class (~line 72):

  ```csharp
  public TextMeshProUGUI countText;
  ```

  Initialize the field in `BuildSlot` (just before the final `v.glow.gameObject.SetActive(false);` near line 625):

  ```csharp
  var countRT = NewRT("__Count", slotRT);
  countRT.anchorMin = new Vector2(1f, 0f);
  countRT.anchorMax = new Vector2(1f, 0f);
  countRT.pivot = new Vector2(1f, 0f);
  countRT.anchoredPosition = new Vector2(-6f, 4f);
  countRT.sizeDelta = new Vector2(40f, 16f);
  v.countText = countRT.gameObject.AddComponent<TextMeshProUGUI>();
  HudFontResolver.Apply(v.countText);
  v.countText.text = "";
  v.countText.fontSize = 14f;
  v.countText.fontStyle = FontStyles.Bold;
  v.countText.alignment = TextAlignmentOptions.BottomRight;
  v.countText.color = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
  v.countText.raycastTarget = false;
  // Drop shadow for legibility over any swatch color.
  var countDrop = countRT.gameObject.AddComponent<Shadow>();
  countDrop.effectColor = new Color(0f, 0f, 0f, 0.9f);
  countDrop.effectDistance = new Vector2(0f, -1.5f);
  ```

- [ ] **Step 2: Add a resource-swatch sprite generator at the bottom of the file**

  Add a new static class after `HotbarRoundedRing` (after line 759):

  ```csharp
  // Procedural colored rounded-corner swatch used as a placeholder icon for
  // resource stacks (wood/crystal/dust). One sprite shared, color applied via
  // Image.color tint. Replace with real textures later.
  static class HotbarResourceSwatch
  {
      static Sprite _swatch;

      public static Sprite GetSprite() {
          if (_swatch != null) return _swatch;
          const int size = 48, radius = 10;
          var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
          tex.filterMode = FilterMode.Bilinear;
          tex.wrapMode = TextureWrapMode.Clamp;
          var pixels = new Color[size * size];
          for (int y = 0; y < size; y++)
              for (int x = 0; x < size; x++)
                  pixels[y * size + x] = new Color(1f, 1f, 1f, RoundedAlpha(x, y, size, radius));
          tex.SetPixels(pixels);
          tex.Apply();
          _swatch = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                                  100f, 0u, SpriteMeshType.FullRect, new Vector4(12, 12, 12, 12));
          _swatch.name = "HotbarResourceSwatch";
          return _swatch;
      }

      static float RoundedAlpha(int x, int y, int size, int radius) {
          int dx = 0, dy = 0;
          if (x < radius) dx = radius - x;
          else if (x >= size - radius) dx = x - (size - radius - 1);
          if (y < radius) dy = radius - y;
          else if (y >= size - radius) dy = y - (size - radius - 1);
          if (dx <= 0 || dy <= 0) return 1f;
          float d = Mathf.Sqrt(dx * dx + dy * dy);
          return Mathf.Clamp01(radius - d + 0.5f);
      }
  }
  ```

- [ ] **Step 3: Add resource color + display name lookups**

  Near `IsResource` (added in Task 1, ~line 50), add:

  ```csharp
  static readonly Color WoodSwatchColor    = new Color32(0xD4, 0xA0, 0x6B, 0xFF);
  static readonly Color CrystalSwatchColor = new Color32(0x8C, 0xE6, 0xFF, 0xFF);
  static readonly Color DustSwatchColor    = new Color32(0xB8, 0x8C, 0xFF, 0xFF);

  static Color ResourceSwatchColor(ItemId id) {
      switch (id) {
          case ItemId.Wood:      return WoodSwatchColor;
          case ItemId.Crystal:   return CrystalSwatchColor;
          case ItemId.SpaceDust: return DustSwatchColor;
          default: return Color.white;
      }
  }

  static string ResourceDisplayName(ItemId id) {
      switch (id) {
          case ItemId.Wood:      return "WOOD";
          case ItemId.Crystal:   return "CRYSTAL";
          case ItemId.SpaceDust: return "DUST";
          default: return "—";
      }
  }
  ```

- [ ] **Step 4: Update `Refresh()` to draw swatches and counts**

  In `Refresh()` (line ~329), find the icon-resolution block (line ~349):

  ```csharp
  Sprite sprite = null;
  if (!empty && _registry != null) {
      for (int r = 0; r < _registry.Length; r++)
          if (_registry[r].Id == id) { sprite = _registry[r].Icon; break; }
  }
  v.itemIcon.sprite = sprite;
  v.itemIcon.enabled = sprite != null;
  ```

  Replace with:

  ```csharp
  // Resource slots: procedural colored swatch + bottom-right count.
  // Tool slots: controller's hotbarIcon (existing behavior).
  // Empty slots: no icon.
  bool isRes = IsResource(id);
  Sprite sprite = null;
  Color iconTint = new Color32(0xF1, 0xF4, 0xFF, 0xC0);
  if (!empty) {
      if (isRes) {
          sprite = HotbarResourceSwatch.GetSprite();
          iconTint = ResourceSwatchColor(id);
      } else if (_registry != null) {
          for (int r = 0; r < _registry.Length; r++)
              if (_registry[r].Id == id) { sprite = _registry[r].Icon; break; }
      }
  }
  v.itemIcon.sprite = sprite;
  v.itemIcon.enabled = sprite != null;

  // Stack count text (resource only).
  if (v.countText != null) {
      if (isRes) {
          string s = slots[i].count.ToString();
          if (v.countText.text != s) v.countText.text = s;
          v.countText.enabled = true;
      } else if (v.countText.enabled) {
          v.countText.enabled = false;
      }
  }
  ```

  Then find the active/non-active tint block immediately below it (line ~360) and add this after the `else` branch's `v.itemIcon.color = ...` assignment:

  Actually, the tint block sets `v.itemIcon.color` based on active/empty. For resources we want the swatch color to come through. **Override** that block by re-applying our `iconTint` after the existing branch. After this existing assignment (line ~362):

  ```csharp
  if (active) {
      v.itemIcon.color = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
      v.background.color = new Color32(0x14, 0x28, 0x44, 0xF8);
  }
  ```

  And the else branch:

  ```csharp
  else {
      v.itemIcon.color = empty
          ? new Color32(0xF1, 0xF4, 0xFF, 0x00)
          : new Color32(0xF1, 0xF4, 0xFF, 0x80);
      v.background.color = empty
          ? new Color32(0x05, 0x03, 0x12, 0xC0)
          : GalaxyHudKit.SlotColor;
  }
  ```

  Immediately after that closing `}`, add:

  ```csharp
  // Resource swatches own their color; re-apply iconTint with active-state alpha.
  if (isRes && !empty) {
      Color c = iconTint;
      c.a = active ? 1f : 0.85f;
      v.itemIcon.color = c;
  }
  ```

- [ ] **Step 5: Update name-plate format for resources**

  In `Refresh()` (~line 410), replace:

  ```csharp
  if (plateShown && _namePlateRT != null) {
      _namePlateText.text = ItemName(activeId);
      ...
  }
  ```

  with:

  ```csharp
  if (plateShown && _namePlateRT != null) {
      string label = IsResource(activeId)
          ? $"{ResourceDisplayName(activeId)} ×{slots[newActive].count}"
          : ItemName(activeId);
      if (_namePlateText.text != label) _namePlateText.text = label;
      float slotX = slotViews[newActive].root.anchoredPosition.x;
      float barWidth = ((RectTransform)_namePlateRT.parent).sizeDelta.x;
      var p = _namePlateRT.anchoredPosition;
      p.x = barWidth * 0.5f + slotX;
      _namePlateRT.anchoredPosition = p;
  }
  ```

- [ ] **Step 6: Save and verify compile**

  Console: no errors. (Visual verification needs resources in slots, which we add in Task 4.)

- [ ] **Step 7: Commit**

  ```bash
  git add "Assets/3 - Scripts/UI/Hotbar.cs"
  git commit -m "feat(hotbar): resource swatch + stack count + ×N name plate"
  ```

---

## Task 4: Convert the three resource singletons into Hotbar facades

`WoodInventory.Wood` etc. become read-throughs to `Hotbar.GetResourceTotal`. `AddWood`, `SpendWood`, `SetWood` delegate. Existing callers and event subscribers keep working.

**Files:**
- Modify: `Assets/3 - Scripts/Player/WoodInventory.cs`
- Modify: `Assets/3 - Scripts/Player/CrystalInventory.cs`
- Modify: `Assets/3 - Scripts/Player/SpaceDustInventory.cs`

- [ ] **Step 1: Rewrite `WoodInventory.cs`**

  Replace the entire file contents with:

  ```csharp
  using UnityEngine;
  using UnityEngine.SceneManagement;

  // Facade over Hotbar's resource API. Public surface kept intact so all existing
  // callers (BuildMenuUI, GhostPlacement, BonusTutorial, SpawnedTree, save system,
  // AI knowledge) keep working unchanged.
  public class WoodInventory : MonoBehaviour
  {
      public static WoodInventory Instance { get; private set; }

      public int Wood => Hotbar.Instance != null
          ? Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.Wood) : 0;

      public event System.Action OnChanged;

      [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
      static void AutoCreate() {
          if (Instance != null) return;
          if (SceneManager.GetActiveScene().name == "MainMenu") return;
          var go = new GameObject("WoodInventory");
          DontDestroyOnLoad(go);
          go.AddComponent<WoodInventory>();
      }

      void Awake() {
          if (Instance != null && Instance != this) { Destroy(gameObject); return; }
          Instance = this;
      }

      void OnEnable() {
          if (Hotbar.Instance != null) Hotbar.Instance.OnResourceChanged += HandleResourceChanged;
          else StartCoroutine(SubscribeWhenHotbarReady());
      }

      void OnDisable() {
          if (Hotbar.Instance != null) Hotbar.Instance.OnResourceChanged -= HandleResourceChanged;
      }

      void OnDestroy() {
          if (Instance == this) Instance = null;
      }

      System.Collections.IEnumerator SubscribeWhenHotbarReady() {
          while (Hotbar.Instance == null) yield return null;
          Hotbar.Instance.OnResourceChanged += HandleResourceChanged;
      }

      void HandleResourceChanged(Hotbar.ItemId id) {
          if (id == Hotbar.ItemId.Wood) OnChanged?.Invoke();
      }

      public void AddWood(int amount) {
          if (amount <= 0 || Hotbar.Instance == null) return;
          int leftover = Hotbar.Instance.AddResource(Hotbar.ItemId.Wood, amount);
          if (leftover > 0) InventoryFullPopup.Show();
          Debug.Log($"[WoodInventory] +{amount} wood ({leftover} overflow). Total: {Wood}");
      }

      public bool SpendWood(int amount) {
          if (amount <= 0) return true;
          if (Hotbar.Instance == null) return false;
          return Hotbar.Instance.SpendResource(Hotbar.ItemId.Wood, amount);
      }

      public bool Has(int amount) => Wood >= amount;

      public void SetWood(int amount) {
          if (Hotbar.Instance == null) return;
          Hotbar.Instance.SetResourceTotal(Hotbar.ItemId.Wood, Mathf.Max(0, amount));
      }
  }
  ```

  Note: `InventoryFullPopup.Show()` doesn't exist yet — temporarily comment that line out, OR add a forward-declared stub. To keep tasks self-contained and the project compiling between commits, **stub the call here** with an inline guard:

  Replace `if (leftover > 0) InventoryFullPopup.Show();` with:

  ```csharp
  if (leftover > 0 && InventoryFullPopup.Instance != null) InventoryFullPopup.Instance.ShowImpl();
  ```

  No — this still references the missing type. **Cleanest approach: include a placeholder `InventoryFullPopup` declaration in this same task** so the project compiles. We'll fill in the real implementation in Task 5.

  Add a new file `Assets/3 - Scripts/UI/InventoryFullPopup.cs` with this stub:

  ```csharp
  using UnityEngine;

  // Stub created in Task 4 so the WoodInventory/CrystalInventory/SpaceDustInventory
  // facades can compile. Full implementation arrives in Task 5.
  public class InventoryFullPopup : MonoBehaviour
  {
      public static void Show() { /* Task 5 */ }
  }
  ```

- [ ] **Step 2: Create the stub `InventoryFullPopup.cs`**

  Create file at `Assets/3 - Scripts/UI/InventoryFullPopup.cs` with exactly:

  ```csharp
  using UnityEngine;

  // Stub created in Task 4 so the WoodInventory/CrystalInventory/SpaceDustInventory
  // facades can compile. Full implementation arrives in Task 5.
  public class InventoryFullPopup : MonoBehaviour
  {
      public static void Show() { /* Task 5 */ }
  }
  ```

- [ ] **Step 3: Rewrite `CrystalInventory.cs`**

  Replace the entire file with:

  ```csharp
  using UnityEngine;
  using UnityEngine.SceneManagement;

  public class CrystalInventory : MonoBehaviour
  {
      public static CrystalInventory Instance { get; private set; }

      public int Count => Hotbar.Instance != null
          ? Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.Crystal) : 0;

      public event System.Action OnChanged;

      [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
      static void AutoCreate() {
          if (Instance != null) return;
          if (SceneManager.GetActiveScene().name == "MainMenu") return;
          var go = new GameObject("CrystalInventory");
          DontDestroyOnLoad(go);
          go.AddComponent<CrystalInventory>();
      }

      void Awake() {
          if (Instance != null && Instance != this) { Destroy(gameObject); return; }
          Instance = this;
      }

      void OnEnable() {
          if (Hotbar.Instance != null) Hotbar.Instance.OnResourceChanged += HandleResourceChanged;
          else StartCoroutine(SubscribeWhenHotbarReady());
      }

      void OnDisable() {
          if (Hotbar.Instance != null) Hotbar.Instance.OnResourceChanged -= HandleResourceChanged;
      }

      void OnDestroy() {
          if (Instance == this) Instance = null;
      }

      System.Collections.IEnumerator SubscribeWhenHotbarReady() {
          while (Hotbar.Instance == null) yield return null;
          Hotbar.Instance.OnResourceChanged += HandleResourceChanged;
      }

      void HandleResourceChanged(Hotbar.ItemId id) {
          if (id == Hotbar.ItemId.Crystal) OnChanged?.Invoke();
      }

      public void Add(int amount) {
          if (amount <= 0 || Hotbar.Instance == null) return;
          int leftover = Hotbar.Instance.AddResource(Hotbar.ItemId.Crystal, amount);
          if (leftover > 0) InventoryFullPopup.Show();
          Debug.Log($"[CrystalInventory] +{amount} crystal ({leftover} overflow). Total: {Count}");
      }

      public bool Spend(int amount) {
          if (amount <= 0) return true;
          if (Hotbar.Instance == null) return false;
          return Hotbar.Instance.SpendResource(Hotbar.ItemId.Crystal, amount);
      }

      public bool Has(int amount) => Count >= amount;

      public void SetCount(int amount) {
          if (Hotbar.Instance == null) return;
          Hotbar.Instance.SetResourceTotal(Hotbar.ItemId.Crystal, Mathf.Max(0, amount));
      }
  }
  ```

- [ ] **Step 4: Rewrite `SpaceDustInventory.cs`**

  Keep the `HasFilter` state (which is unrelated to stacks — it's a separate one-time unlock). Replace the file with:

  ```csharp
  using UnityEngine;
  using UnityEngine.SceneManagement;

  // Count facade over Hotbar; HasFilter is unrelated state that stays here.
  public class SpaceDustInventory : MonoBehaviour
  {
      public static SpaceDustInventory Instance { get; private set; }

      public int Count => Hotbar.Instance != null
          ? Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.SpaceDust) : 0;
      public bool HasFilter { get; private set; }

      public event System.Action OnChanged;

      [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
      static void AutoCreate() {
          if (Instance != null) return;
          if (SceneManager.GetActiveScene().name == "MainMenu") return;
          var go = new GameObject("SpaceDustInventory");
          DontDestroyOnLoad(go);
          go.AddComponent<SpaceDustInventory>();
      }

      void Awake() {
          if (Instance != null && Instance != this) { Destroy(gameObject); return; }
          Instance = this;
      }

      void OnEnable() {
          if (Hotbar.Instance != null) Hotbar.Instance.OnResourceChanged += HandleResourceChanged;
          else StartCoroutine(SubscribeWhenHotbarReady());
      }

      void OnDisable() {
          if (Hotbar.Instance != null) Hotbar.Instance.OnResourceChanged -= HandleResourceChanged;
      }

      void OnDestroy() {
          if (Instance == this) Instance = null;
      }

      System.Collections.IEnumerator SubscribeWhenHotbarReady() {
          while (Hotbar.Instance == null) yield return null;
          Hotbar.Instance.OnResourceChanged += HandleResourceChanged;
      }

      void HandleResourceChanged(Hotbar.ItemId id) {
          if (id == Hotbar.ItemId.SpaceDust) OnChanged?.Invoke();
      }

      public void Add(int amount) {
          if (amount <= 0 || Hotbar.Instance == null) return;
          int leftover = Hotbar.Instance.AddResource(Hotbar.ItemId.SpaceDust, amount);
          if (leftover > 0) InventoryFullPopup.Show();
      }

      public bool Spend(int amount) {
          if (amount <= 0) return true;
          if (Hotbar.Instance == null) return false;
          return Hotbar.Instance.SpendResource(Hotbar.ItemId.SpaceDust, amount);
      }

      public void SetCount(int amount) {
          if (Hotbar.Instance == null) return;
          Hotbar.Instance.SetResourceTotal(Hotbar.ItemId.SpaceDust, Mathf.Max(0, amount));
      }

      public void SetFilterUnlocked(bool unlocked) {
          if (HasFilter == unlocked) return;
          HasFilter = unlocked;
          OnChanged?.Invoke();
      }
  }
  ```

- [ ] **Step 5: Save all four files and verify compile**

  Console: no errors. (The InventoryFullPopup is a stub MonoBehaviour with a static `Show()` no-op — provides a valid type for the facades to reference until Task 5 fills it in.)

  Note: `InventoryFullPopup.cs` needs a `.meta` file. Unity will auto-generate one when the file is first added — open the Editor and let the asset import run.

- [ ] **Step 6: Editor end-to-end check — mining now fills hotbar slots**

  Play. Walk to a tree, chop with axe until tree falls — verify a brown swatch appears in an empty hotbar slot with "16" in the bottom-right (or whatever the tree yield is). Chop a second tree: same slot's count goes up to "32". Drain a crystal: cyan swatch appears in the next empty slot with the count. Press number key 1-5 over the wood slot: name plate reads "WOOD ×32" and slot highlights, but no animation plays (no controller equipped). Press the same number again: deselects.

  Old top-left wood/dust/crystal chips will still be there — that's removed in Task 7.

- [ ] **Step 7: Commit**

  ```bash
  git add "Assets/3 - Scripts/Player/WoodInventory.cs" \
          "Assets/3 - Scripts/Player/CrystalInventory.cs" \
          "Assets/3 - Scripts/Player/SpaceDustInventory.cs" \
          "Assets/3 - Scripts/UI/InventoryFullPopup.cs"
  git commit -m "refactor: WoodInventory/CrystalInventory/SpaceDustInventory → Hotbar facades"
  ```

---

## Task 5: Implement the InventoryFullPopup

Replace the stub from Task 4 with the real fade-in/hold/fade-out red pill above the hotbar.

**Files:**
- Modify: `Assets/3 - Scripts/UI/InventoryFullPopup.cs`

- [ ] **Step 1: Replace the stub with the real implementation**

  Replace the entire file contents with:

  ```csharp
  using System.Collections;
  using TMPro;
  using UnityEngine;
  using UnityEngine.UI;

  // Brief floating pill that fades in above the hotbar to warn the player that
  // mined resources didn't all fit. Auto-creates on first Show() call (no
  // RuntimeInitializeOnLoadMethod — sidesteps the MainMenu trap entirely).
  // Restarting Show() while visible resets the timer.
  public class InventoryFullPopup : MonoBehaviour
  {
      public static InventoryFullPopup Instance { get; private set; }

      Canvas _canvas;
      CanvasGroup _group;
      RectTransform _pill;
      TextMeshProUGUI _label;
      Coroutine _running;

      const float FadeIn  = 0.15f;
      const float Hold    = 1.20f;
      const float FadeOut = 0.40f;

      public static void Show() {
          if (Instance == null) {
              if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
              var go = new GameObject("InventoryFullPopup");
              DontDestroyOnLoad(go);
              Instance = go.AddComponent<InventoryFullPopup>();
              Instance.Build();
          }
          Instance.ShowImpl();
      }

      void Build() {
          _canvas = gameObject.AddComponent<Canvas>();
          _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
          _canvas.sortingOrder = 835; // above Hotbar (830)
          HUDSceneGate.Register(_canvas);

          var scaler = gameObject.AddComponent<CanvasScaler>();
          scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
          scaler.referenceResolution = new Vector2(1920f, 1080f);
          scaler.matchWidthOrHeight = 0.5f;
          gameObject.AddComponent<GraphicRaycaster>();

          _pill = new GameObject("Pill", typeof(RectTransform)).GetComponent<RectTransform>();
          _pill.SetParent(transform, false);
          _pill.anchorMin = new Vector2(0.5f, 0f);
          _pill.anchorMax = new Vector2(0.5f, 0f);
          _pill.pivot = new Vector2(0.5f, 0f);
          _pill.anchoredPosition = new Vector2(0f, 220f); // above hotbar bar
          _pill.sizeDelta = new Vector2(260f, 44f);

          var bg = _pill.gameObject.AddComponent<Image>();
          bg.sprite = GalaxyHudKit.RoundedSprite();
          bg.type = Image.Type.Sliced;
          bg.color = new Color32(0x3C, 0x15, 0x18, 0xF0);
          bg.raycastTarget = false;

          var outline = _pill.gameObject.AddComponent<Outline>();
          outline.effectColor = new Color32(0xFF, 0x6F, 0x70, 0xC0);
          outline.effectDistance = new Vector2(1.5f, -1.5f);

          var glow = _pill.gameObject.AddComponent<Shadow>();
          glow.effectColor = new Color(1f, 0.4f, 0.4f, 0.35f);
          glow.effectDistance = new Vector2(0f, 0f);

          var labelRT = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
          labelRT.SetParent(_pill, false);
          labelRT.anchorMin = Vector2.zero;
          labelRT.anchorMax = Vector2.one;
          labelRT.offsetMin = Vector2.zero;
          labelRT.offsetMax = Vector2.zero;
          _label = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
          HudFontResolver.Apply(_label);
          _label.text = "INVENTORY FULL";
          _label.alignment = TextAlignmentOptions.Center;
          _label.fontSize = 18f;
          _label.fontStyle = FontStyles.Bold;
          _label.characterSpacing = 4f;
          _label.color = new Color32(0xFF, 0xE6, 0xE6, 0xFF);
          _label.raycastTarget = false;

          _group = gameObject.AddComponent<CanvasGroup>();
          _group.alpha = 0f;
          _group.blocksRaycasts = false;
          _group.interactable = false;
      }

      public void ShowImpl() {
          if (_running != null) StopCoroutine(_running);
          _running = StartCoroutine(RunFade());
      }

      IEnumerator RunFade() {
          // Fade in.
          float t = 0f;
          while (t < FadeIn) {
              t += Time.unscaledDeltaTime;
              _group.alpha = Mathf.Clamp01(t / FadeIn);
              yield return null;
          }
          _group.alpha = 1f;
          // Hold.
          yield return new WaitForSecondsRealtime(Hold);
          // Fade out.
          t = 0f;
          while (t < FadeOut) {
              t += Time.unscaledDeltaTime;
              _group.alpha = 1f - Mathf.Clamp01(t / FadeOut);
              yield return null;
          }
          _group.alpha = 0f;
          _running = null;
      }

      void OnDestroy() { if (Instance == this) Instance = null; }
  }
  ```

- [ ] **Step 2: Save and verify compile**

  Console: no errors. Unity recreates the `.meta` link unchanged.

- [ ] **Step 3: Editor verification — popup fires on overflow**

  Play. Manually fill the hotbar: chop several trees to put two wood stacks at 100 each (use `Universe.cheatsEnabled` debug spawning if available, or just chop enough trees). Make sure all 5 slots are occupied (use water/rod/guitar/axe/pistol if you have them, plus wood stacks). Chop one more tree — verify the red `INVENTORY FULL` pill fades in just above the hotbar and fades out after ~1.5s. Wood count in the affected slot does NOT increase.

  Faster check: temporarily edit `WoodInventory.AddWood` to call `Hotbar.Instance.AddResource(..., 9999);` once, observe popup. **Revert** that change before continuing.

- [ ] **Step 4: Commit**

  ```bash
  git add "Assets/3 - Scripts/UI/InventoryFullPopup.cs"
  git commit -m "feat(hotbar): InventoryFullPopup pill for overflow feedback"
  ```

---

## Task 6: Save / load — `HotbarSave` schema + capture + apply

Persist slot layout across save/load. Legacy total-only saves redistribute on load.

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveData.cs`
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs`

- [ ] **Step 1: Add the schema classes to `SaveData.cs`**

  At the end of `SaveData.cs` (after the existing `[Serializable]` classes), add:

  ```csharp
  [Serializable]
  public class HotbarSlotSave {
      public string itemId;  // Hotbar.ItemId enum.ToString(): "None", "Wood", "Pistol", ...
      public int count;
  }

  [Serializable]
  public class HotbarSave {
      public List<HotbarSlotSave> slots = new List<HotbarSlotSave>();
  }
  ```

  Add the field on `SaveData` (around line 46, near `spaceDust`):

  ```csharp
  public HotbarSave hotbar = new HotbarSave();
  ```

- [ ] **Step 2: Expose slot read/write helpers on `Hotbar`**

  In `Hotbar.cs`, immediately after the `SetResourceTotal` method added in Task 1, add:

  ```csharp
  // ── Save / load access ───────────────────────────────────────────
  public IReadOnlyList<Slot> GetSlotsForSave() => slots;

  public void ApplySlotsFromSave(System.Collections.Generic.List<HotbarSlotSave> saved) {
      // Clear current.
      for (int i = 0; i < NumSlots; i++) slots[i] = default;
      if (saved == null) return;
      int max = Mathf.Min(saved.Count, NumSlots);
      for (int i = 0; i < max; i++) {
          var entry = saved[i];
          if (entry == null) continue;
          if (!System.Enum.TryParse<ItemId>(entry.itemId, out var id)) continue;
          int count = Mathf.Clamp(entry.count, 0, StackMax(id));
          // Empty slot or impossible state.
          if (id == ItemId.None || count <= 0) { slots[i] = default; continue; }
          slots[i] = new Slot { id = id, count = count };
      }
      // Notify any resource subscribers (facades) so their OnChanged fires once each.
      if (OnResourceChanged != null) {
          OnResourceChanged(ItemId.Wood);
          OnResourceChanged(ItemId.Crystal);
          OnResourceChanged(ItemId.SpaceDust);
      }
  }
  ```

  Note: `IReadOnlyList<>` requires `using System.Collections.Generic;` — verify that's already imported at the top of `Hotbar.cs`. If not, add it.

- [ ] **Step 3: Add `CaptureHotbar` and `ApplyHotbar` to `SaveCollector.cs`**

  Near the existing `CaptureWood` / `CaptureCrystals` block (~line 256), add:

  ```csharp
  static void CaptureHotbar(HotbarSave s) {
      s.slots.Clear();
      if (Hotbar.Instance == null) return;
      var live = Hotbar.Instance.GetSlotsForSave();
      for (int i = 0; i < live.Count; i++) {
          s.slots.Add(new HotbarSlotSave {
              itemId = live[i].id.ToString(),
              count = live[i].count
          });
      }
  }
  ```

  Near the existing `ApplyWood` / `ApplyCrystals` block (~line 850), add:

  ```csharp
  static void ApplyHotbar(SaveData data) {
      if (data == null || Hotbar.Instance == null) return;

      // Preferred path: new saves carry the full slot layout.
      bool hasLayout = data.hotbar != null && data.hotbar.slots != null && data.hotbar.slots.Count > 0;
      if (hasLayout) {
          Hotbar.Instance.ApplySlotsFromSave(data.hotbar.slots);
          return;
      }

      // Legacy fallback: redistribute totals into fresh stacks.
      if (data.wood != null && data.wood.wood > 0)
          Hotbar.Instance.SetResourceTotal(Hotbar.ItemId.Wood, data.wood.wood);
      if (data.crystal != null && data.crystal.count > 0)
          Hotbar.Instance.SetResourceTotal(Hotbar.ItemId.Crystal, data.crystal.count);
      if (data.spaceDust != null && data.spaceDust.playerDust > 0)
          Hotbar.Instance.SetResourceTotal(Hotbar.ItemId.SpaceDust, data.spaceDust.playerDust);
  }
  ```

- [ ] **Step 4: Wire `CaptureHotbar` into `Capture` and `ApplyHotbar` into `Apply`**

  In `SaveCollector.Capture` near line 26 (immediately after `CaptureCrystals(data.crystal);`), add:

  ```csharp
  CaptureHotbar(data.hotbar);
  ```

  In `SaveCollector.Apply` near line 697 (immediately after `ApplyEquipment(data.equipment);`), add:

  ```csharp
  ApplyHotbar(data);
  ```

  Per the spec's apply-order section: this runs after `ApplyEquipment` so tool controllers are already in their saved state. The hotbar's `GetEquipped` fallback then auto-syncs `_equippedSlot` to the equipped tool on its next `Update` tick.

- [ ] **Step 5: Save and verify compile**

  Console: no errors.

- [ ] **Step 6: Editor verification — save/load round-trip**

  Play. Chop trees to put 90 wood in slot 1 and 12 wood in slot 2 (or any layout — chop different times to create separated stacks). Drain a space net to put dust in another slot. Open the pause menu → save → "CREATE NEW SAVE", name it "hotbar-test". Return to main menu → load "hotbar-test". Verify: hotbar slots show exactly the saved layout — same slot positions, same counts, same resource types. Total in each slot matches what was saved.

- [ ] **Step 7: Legacy save check (if you have one handy)**

  If a pre-refactor save exists in `%AppData%\..\LocalLow\DefaultCompany\Solar System 2\saves\`, load it. Verify: wood/dust/crystal totals from the old format appear as fresh stacks in the leftmost empty slots (e.g. 47 wood → single stack of 47 in slot 1 if no tools are unlocked, or in slot N if tools occupy 1..N-1).

  If no legacy save exists, skip this — the fallback code path is straightforward enough that visual proof in step 6 covers correctness for new saves.

- [ ] **Step 8: Commit**

  ```bash
  git add "Assets/3 - Scripts/SaveSystem/SaveData.cs" \
          "Assets/3 - Scripts/SaveSystem/SaveCollector.cs" \
          "Assets/3 - Scripts/UI/Hotbar.cs"
  git commit -m "feat(save): HotbarSave slot layout + legacy-total fallback"
  ```

---

## Task 7: Remove the old top-left wood / dust / crystal HUD chips

Strip the three obsolete chips from `PlayerWallet`. Money and Ammo chips stay.

**Files:**
- Modify: `Assets/3 - Scripts/Player/PlayerWallet.cs`

- [ ] **Step 1: Remove the three field declarations**

  At ~line 17, delete these lines:

  ```csharp
  public TextMeshProUGUI woodText;
  ...
  public TextMeshProUGUI dustText;
  public TextMeshProUGUI crystalText;
  ```

  At ~line 32, delete:

  ```csharp
  static readonly Color WoodValueColor  = new Color32(0xD4, 0xA0, 0x6B, 0xFF);
  static readonly Color DustValueColor  = new Color32(0xB8, 0x8C, 0xFF, 0xFF);
  static readonly Color CrystalValueColor = new Color32(0x8C, 0xE6, 0xFF, 0xFF);
  ```

  At ~line 41, delete:

  ```csharp
  int _lastWoodSeen = int.MinValue;
  int _lastDustSeen = int.MinValue;
  int _lastCrystalSeen = int.MinValue;
  bool _dustChipVisible;
  bool _crystalChipVisible;
  GameObject _woodChip;
  GameObject _dustChip;
  GameObject _crystalChip;
  ```

  (Keep `_lastAmmoSeen`, `_ammoChipVisible`, `_moneyChip`, `_ammoChip`, `_pistolCached`.)

- [ ] **Step 2: Remove the polling blocks in `Update()`**

  In `Update()` (~line 83), delete the wood block:

  ```csharp
  int wood = WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;
  if (wood != _lastWoodSeen) {
      _lastWoodSeen = wood;
      RefreshWood();
  }
  ```

  And delete both dust and crystal blocks (~line 105 through ~line 131).

  Keep the ammo block (~line 92).

- [ ] **Step 3: Remove the `RefreshWood()` method (~line 160)**

  Delete the method body entirely.

  Also remove the `RefreshWood();` call in `Start()` (~line 80) — only `RefreshMoney();` remains.

- [ ] **Step 4: Remove the chip-build calls in `CreateCornerHUD()`**

  Around line 213, delete:

  ```csharp
  _woodChip    = BuildChip(stack, "WoodChip",    "WOOD",    WoodValueColor,    out woodText);
  _ammoChip    = BuildChip(stack, "AmmoChip",    "AMMO",    AmmoValueColor,    out ammoText);
  _dustChip    = BuildChip(stack, "DustChip",    "DUST",    DustValueColor,    out dustText);
  _crystalChip = BuildChip(stack, "CrystalChip", "CRYSTAL", CrystalValueColor, out crystalText);
  ```

  Replace with just the ammo chip (money is built above this — keep that line too):

  ```csharp
  _ammoChip = BuildChip(stack, "AmmoChip", "AMMO", AmmoValueColor, out ammoText);
  ```

  Delete:

  ```csharp
  _dustChip.SetActive(false);
  _crystalChip.SetActive(false);
  ```

  Keep:

  ```csharp
  _ammoChip.SetActive(false);
  ```

  Delete the initial text resets for the removed chips (~line 222):

  ```csharp
  woodText.text    = "0";
  dustText.text    = "0";
  crystalText.text = "0";
  ```

  Keep `moneyText.text = "$0";` and `ammoText.text = "0";`.

- [ ] **Step 5: Save and verify compile**

  Console: no errors. (Any leftover references to removed fields will surface here — fix on the spot if so.)

- [ ] **Step 6: Editor verification — old chips gone**

  Play. Top-left corner shows only **MONEY** (and **AMMO** when pistol is equipped). No WOOD / DUST / CRYSTAL chips. Resources are visible only in the hotbar.

- [ ] **Step 7: Commit**

  ```bash
  git add "Assets/3 - Scripts/Player/PlayerWallet.cs"
  git commit -m "refactor: remove obsolete wood/dust/crystal HUD chips (now in hotbar)"
  ```

---

## Task 8: Full integration verification (no code changes)

Walk the entire test plan from the spec, including the build sanity check per the MainMenu trap. This is the final go/no-go.

**Files:** None (verification only).

- [ ] **Step 1: Editor — full collection flow**

  Press Play in `1.6.7.7.7.unity`. Verify each:
  - [ ] Chop a tree (16 wood) → brown swatch with "16" appears in the first empty hotbar slot.
  - [ ] Chop a second tree (16 more) → same slot, count rises to "32".
  - [ ] Mine a crystal → cyan swatch in next empty slot with count.
  - [ ] Drain a space net → violet swatch in next empty slot with count.
  - [ ] Old top-left chips for wood/dust/crystal are gone. Money chip still shows. Ammo chip still shows when pistol is equipped.

- [ ] **Step 2: Editor — stack overflow within a single resource**

  Use the build menu or repeat chops to push wood toward 100 in one slot. Verify: once a slot hits 100, the next chop creates a NEW stack in the next empty slot. Crystal slot caps at 20; verify the same behavior for crystals (mine until cap, then a new stack starts).

- [ ] **Step 3: Editor — INVENTORY FULL popup**

  Engineer the failure: fill all 5 hotbar slots (e.g. with a mix of tools and resource stacks all at max). One way: pick up water/rod/axe/pistol/guitar so 5 tool slots are taken. Then chop a tree. Verify: red `INVENTORY FULL` pill fades in above the hotbar for ~1.5s; existing slot contents unchanged; chopped wood is lost. Chopping multiple trees back-to-back restarts the timer rather than stacking pills.

  Alternate path if tools aren't all unlocked yet: keep filling wood/crystal/dust stacks until all 5 slots are non-empty AND each is at its `StackMax`. Then any further collection triggers the popup.

- [ ] **Step 4: Editor — building spends wood across stacks**

  With wood split across multiple stacks (e.g. 40 + 30 = 70), open the build menu (N), place a Cabin recipe (or any wood recipe costing more than one stack). Verify: build succeeds, leftmost stack drains first and empties (slot becomes empty / `id=None`), next stack drains as needed. Insufficient totals leave both stacks untouched and the recipe shows as unaffordable.

- [ ] **Step 5: Editor — equip behavior on resource slots**

  Press the number key for a resource slot. Verify: slot highlights, name plate reads `"WOOD ×N"` / `"CRYSTAL ×N"` / `"DUST ×N"`, no animation plays, no controller equips. Pressing the same number again deselects. Press a tool number key from a resource selection — tool equips normally.

- [ ] **Step 6: Editor — save/load round-trip**

  Save a layout like `(slot 1: wood 90, slot 2: wood 12, slot 4: crystal 7, slot 5: pistol)`. Reload that save. Verify exact slot positions and counts.

- [ ] **Step 7: Editor — dialogue + phone auto-unequip still works**

  Equip the axe via slot 4. Talk to an NPC — axe should unequip on dialogue start (existing behavior). Equip pistol via slot 5, open the phone UI (whatever the input is) — pistol should unequip. Resource slot selections should also clear on the same events.

- [ ] **Step 8: Build sanity check — MainMenu trap**

  Per CLAUDE.md, build the game (`Solar System 2.exe`), launch from MainMenu, click **PLAY** or **NEW GAME**. Mine a tree once you're in the gameplay scene. Verify: wood appears in a hotbar slot, no NullReferenceExceptions in `Player.log` related to `Hotbar.Instance`, `WoodInventory`, `CrystalInventory`, `SpaceDustInventory`, or `InventoryFullPopup`.

  Specifically check `%AppData%\..\LocalLow\DefaultCompany\Solar System 2\Player.log` for entries like `NullReferenceException` near `Hotbar` or any of the four singletons. The seeding in `EnsureGameplaySingletons` should cover Hotbar / WoodInventory / CrystalInventory / SpaceDustInventory already. `InventoryFullPopup` does NOT use `RuntimeInitializeOnLoadMethod` — it only auto-creates on the first `Show()` call, so it sidesteps the MainMenu trap regardless.

  If a singleton is missing in build but present in editor, that's the canonical MainMenu-trap fingerprint — check whether it's seeded in `EnsureGameplaySingletons` and add it there.

- [ ] **Step 9: Final commit (if any docs need updating)**

  If `CLAUDE.md` notes anything about the resource HUD chips that's now stale, update those sentences in this same commit. Otherwise skip.

  ```bash
  # Only if CLAUDE.md was edited
  git add CLAUDE.md
  git commit -m "docs: update CLAUDE.md notes for resource hotbar revamp"
  ```

---

## Out of scope (per design spec)

- **Ship storage**: explicit future work. The `GetSlotsForSave` / `ApplySlotsFromSave` / `Slot` types provide the seam.
- **Real resource icons**: still placeholders; swap `HotbarResourceSwatch.GetSprite()` references when art is ready.
- **Equip action for resources**: highlight + name plate only.
- **Stack-cap tuning**: caps are `Wood=100, Crystal=20, SpaceDust=100` per the design brief. Revisit after playtest.
