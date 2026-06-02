# Space Dust & Orbital Net — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Space Dust" resource that ships passively gather while parked in orbit, plus a universal NPC sell flow with rolled-per-conversation accept-% and price.

**Architecture:** A `SpaceNet` MonoBehaviour on each ship runs an orbit check and buffers dust; a new `SpaceDustInventory` singleton holds the player's collected dust + filter unlock. A shared `PostGreetingChoicePanel` UI inserts a numbered choice menu between each NPC's greeting and their existing flow; "Sell space dust" opens a shared `SpaceDustSellUI` populated from a per-NPC `NPCSellDustOption` component that holds the conversation-stable rolled price + accept chance.

**Tech Stack:** Unity 2022.3 (built-in render pipeline), C# (Assembly-CSharp, no asmdef), TextMeshPro, `JsonUtility`-based save system, procedural uGUI (no scene-wired Inspector refs for new UI).

**Spec:** `docs/superpowers/specs/2026-05-15-space-dust-orbital-net-design.md`

**Conventions to follow (from CLAUDE.md):**
- Singleton pattern: `Awake` guard + `OnDestroy` clear.
- Lazy-cache scene lookups (never `FindObjectOfType` per frame).
- Change-detected text updates (no per-frame string alloc).
- Typewriter via `DialogueTextStyling.RevealCharsTMP`.
- `CompareTag("Player")` not `tag == "Player"`.
- No new docs/CLAUDE.md updates unless asked.
- No emojis in files.

**Verification model:**
This project has no automated test framework. Each task verifies via:
1. Unity MCP `check_compile_errors` after code changes.
2. Where applicable, a temporary Editor `execute_script` to inspect runtime state, removed before commit.
3. Manual play-mode walkthrough in the closing task.

---

## File Structure

**New files:**

| Path | Responsibility |
|---|---|
| `Assets/3 - Scripts/Player/SpaceDustInventory.cs` | Singleton: `Count`, `HasFilter`, `Add`/`Spend`/`SetFilterUnlocked`. Parallels `WoodInventory`. |
| `Assets/3 - Scripts/Ship/SpaceNet.cs` | Per-ship component: orbit check, dust buffer, F-prompt collection. |
| `Assets/3 - Scripts/Ship/DustPopup.cs` | Floating world-space "+N dust" text (mirrors `WoodPopup`). |
| `Assets/3 - Scripts/NPC_Dialogue/PostGreetingChoicePanel.cs` | Shared procedural choice menu (numbered rows, digit key + click). |
| `Assets/3 - Scripts/NPC_Dialogue/NPCSellDustOption.cs` | Per-NPC component holding rolled `acceptChance` + `pricePerDust`. |
| `Assets/3 - Scripts/Vendor/SpaceDustSellUI.cs` | Procedural sell panel (quantity slider, SELL / CANCEL, success / fail toast). |

**Modified files:**

| Path | Change |
|---|---|
| `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs` | Expose `IsLanded` accessor. |
| `Assets/3 - Scripts/Vendor/ShopItem.cs` | Add `SpaceDustFilter` to `ShopItemKind` enum. |
| `Assets/3 - Scripts/Vendor/ShipMarketNPC.cs` | Handle `SpaceDustFilter` purchase + `IsAlreadyOwned`; insert post-greeting choice. |
| `Assets/3 - Scripts/Vendor/ShipMarketShopUI.cs` | Grey out cards whose `_vendor.IsAlreadyOwned(kind)` returns true. |
| `Assets/3 - Scripts/Vendor/Alien7Vendor.cs` | Insert post-greeting choice. |
| `Assets/3 - Scripts/Fishing/FishMarketNPC.cs` | Insert post-greeting choice. |
| `Assets/3 - Scripts/NPC_Dialogue/RandomAlienDialogue.cs` | Insert post-greeting choice. |
| `Assets/3 - Scripts/Player/PlayerWallet.cs` | Add a DUST chip mirroring the WOOD chip. |
| `Assets/3 - Scripts/SaveSystem/SaveData.cs` | Add `SpaceDustSave` schema. |
| `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` | Capture + Apply for space dust. |
| `Assets/3 - Scripts/UI/MainMenuController.cs` | Seed `SpaceDustInventory` in `EnsureGameplaySingletons`. |

---

## Task 1: `SpaceDustInventory` singleton + main-menu seed

**Files:**
- Create: `Assets/3 - Scripts/Player/SpaceDustInventory.cs`
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs`

- [ ] **Step 1: Create `SpaceDustInventory.cs`**

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Singleton holding the player's collected space-dust count and the
/// one-time "filter purchased" unlock. Mirrors WoodInventory's auto-create
/// pattern so no scene wiring is required. Persists across scene reload
/// via DontDestroyOnLoad; save state restored by SaveCollector.ApplySpaceDust.
/// </summary>
public class SpaceDustInventory : MonoBehaviour
{
    public static SpaceDustInventory Instance { get; private set; }

    public int Count { get; private set; }
    public bool HasFilter { get; private set; }

    public event System.Action OnChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("SpaceDustInventory");
        DontDestroyOnLoad(go);
        go.AddComponent<SpaceDustInventory>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Add(int amount)
    {
        if (amount <= 0) return;
        Count += amount;
        OnChanged?.Invoke();
    }

    public bool Spend(int amount)
    {
        if (amount <= 0) return true;
        if (Count < amount) return false;
        Count -= amount;
        OnChanged?.Invoke();
        return true;
    }

    public void SetCount(int amount)
    {
        Count = Mathf.Max(0, amount);
        OnChanged?.Invoke();
    }

    public void SetFilterUnlocked(bool unlocked)
    {
        if (HasFilter == unlocked) return;
        HasFilter = unlocked;
        OnChanged?.Invoke();
    }
}
```

- [ ] **Step 2: Compile check**

Use Unity MCP `check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 3: Seed in MainMenuController.EnsureGameplaySingletons**

Open `Assets/3 - Scripts/UI/MainMenuController.cs`, find `EnsureGameplaySingletons` (grep for the method name). After the existing `WoodInventory` seeding lines, add:

```csharp
if (SpaceDustInventory.Instance == null)
{
    var go = new GameObject("SpaceDustInventory");
    DontDestroyOnLoad(go);
    go.AddComponent<SpaceDustInventory>();
}
```

- [ ] **Step 4: Compile check**

Use Unity MCP `check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Player/SpaceDustInventory.cs" "Assets/3 - Scripts/Player/SpaceDustInventory.cs.meta" "Assets/3 - Scripts/UI/MainMenuController.cs"
git commit -m "feat(space-dust): add SpaceDustInventory singleton + main-menu seed"
```

---

## Task 2: Expose `Ship.IsLanded`

**Files:**
- Modify: `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs`

The SpaceNet's orbit check needs to know whether the ship is currently sitting on a planet. `Ship.numCollisionTouches` is private but already filtered to grounded-layer contacts only — we just expose a read-only accessor.

- [ ] **Step 1: Add `IsLanded` accessor**

Locate the line near the existing `IsPiloted` accessor (search for `public bool IsPiloted => shipIsPiloted;`). Add immediately below it:

```csharp
public bool IsLanded => numCollisionTouches > 0;
```

- [ ] **Step 2: Compile check**

Use Unity MCP `check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs"
git commit -m "feat(ship): expose IsLanded for SpaceNet orbit check"
```

---

## Task 3: `SpaceNet` component (orbit detection + accumulation)

**Files:**
- Create: `Assets/3 - Scripts/Ship/SpaceNet.cs`

This task implements orbit detection and accumulation only. F-prompt collection is added in Task 4.

- [ ] **Step 1: Create `SpaceNet.cs`**

```csharp
using UnityEngine;

/// <summary>
/// Drop this on a child GameObject of a Ship to make that ship gather space
/// dust while parked in orbit. Author also provides the visual mesh for the
/// "filter / net" prop — this component is purely behaviour.
///
/// Orbit definition (all must hold):
///   1. SpaceDustInventory.HasFilter is true (one-time global purchase).
///   2. Owning Ship.IsPiloted is false (the player parked it).
///   3. Owning Ship.IsLanded is false (no contact with anything grounded).
///   4. Distance from the net to the nearest CelestialBody is between
///      body.radius * 1.05 (just above surface) and body.radius * 5 (within
///      the body's gravitational neighbourhood — matches the save system's
///      body-relative attach threshold).
///
/// While in orbit, accumulates dustPerSecond * Time.deltaTime into _buffer,
/// clamped to bufferCapacity. The buffer is float internally so partial-
/// second accumulation works; exposed as floor int via BufferedDust.
/// </summary>
public class SpaceNet : MonoBehaviour
{
    [Tooltip("Dust accumulated per real-time second while in orbit.")]
    public float dustPerSecond = 0.1f;
    [Tooltip("Maximum dust the net can hold before the player must come collect.")]
    public int bufferCapacity = 500;
    [Tooltip("Radius (m) of the auto-added trigger collider for the F-prompt.")]
    public float collectionRadius = 2.5f;

    Ship _owningShip;
    float _buffer;

    public int BufferedDust => Mathf.FloorToInt(_buffer);
    public Ship OwningShip => _owningShip;
    public float RawBuffer => _buffer;
    public void SetRawBuffer(float value) => _buffer = Mathf.Clamp(value, 0f, bufferCapacity);

    void Awake()
    {
        _owningShip = GetComponentInParent<Ship>();
        if (_owningShip == null)
        {
            Debug.LogWarning($"[SpaceNet] '{name}' is not parented to a Ship; net will be inactive.");
        }
    }

    void Update()
    {
        if (!IsCurrentlyInOrbit()) return;
        if (_buffer >= bufferCapacity) return;
        _buffer = Mathf.Min(bufferCapacity, _buffer + dustPerSecond * Time.deltaTime);
    }

    bool IsCurrentlyInOrbit()
    {
        if (_owningShip == null) return false;
        if (SpaceDustInventory.Instance == null || !SpaceDustInventory.Instance.HasFilter) return false;
        if (_owningShip.IsPiloted) return false;
        if (_owningShip.IsLanded) return false;
        var body = ClosestBody();
        if (body == null) return false;
        float dist = Vector3.Distance(transform.position, body.Position);
        float minR = body.radius * 1.05f;
        float maxR = body.radius * 5f;
        return dist >= minR && dist <= maxR;
    }

    CelestialBody ClosestBody()
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        CelestialBody best = null;
        float bestSq = float.MaxValue;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            float d = (b.Position - transform.position).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = b; }
        }
        return best;
    }

    /// <summary>Drain up to `requested` dust from the net's buffer; returns what was actually drained.</summary>
    public int Drain(int requested)
    {
        if (requested <= 0) return 0;
        int available = BufferedDust;
        int drained = Mathf.Min(available, requested);
        _buffer -= drained;
        if (_buffer < 0f) _buffer = 0f;
        return drained;
    }
}
```

- [ ] **Step 2: Compile check**

Use Unity MCP `check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Ship/SpaceNet.cs" "Assets/3 - Scripts/Ship/SpaceNet.cs.meta"
git commit -m "feat(space-dust): SpaceNet component with orbit detection + accumulation"
```

---

## Task 4: F-prompt collection + DustPopup

**Files:**
- Create: `Assets/3 - Scripts/Ship/DustPopup.cs`
- Modify: `Assets/3 - Scripts/Ship/SpaceNet.cs`

> **Implementation note:** The SpaceNet is parented to the Ship, which has a Rigidbody. Putting a trigger collider on a child of a Rigidbody makes the trigger callback fire on the Rigidbody owner (Ship), not on SpaceNet itself — Unity's compound-collider rule. So instead of `OnTriggerEnter`, we do a simple lazy-cached distance check to the Player every frame. Cheap, deterministic, and no physics surprises. The `collectionRadius` Inspector field still does the same thing — just compared in code rather than via a collider.

- [ ] **Step 1: Create `DustPopup.cs`** (clones WoodPopup with palette change)

```csharp
using TMPro;
using UnityEngine;

public class DustPopup : MonoBehaviour
{
    public static void Spawn(Vector3 worldPos, int amount)
    {
        var go = new GameObject("DustPopup");
        go.transform.position = worldPos;
        var p = go.AddComponent<DustPopup>();
        p.Init(amount);
    }

    TextMeshPro tmp;
    float lifetime = 1.5f;
    float age;
    Vector3 upDir = Vector3.up;
    Camera _cam;
    const float FloatSpeed = 1.2f;

    void Init(int amount)
    {
        tmp = gameObject.AddComponent<TextMeshPro>();
        tmp.text = $"+{amount} dust";
        tmp.fontSize = 6f;
        tmp.color = new Color32(184, 140, 255, 255); // violet (dust-y)
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.outlineWidth = 0.25f;
        tmp.outlineColor = Color.black;

        upDir = ComputeUpDirection();
        var planet = ClosestPlanet();
        if (planet != null)
            transform.SetParent(planet.transform, worldPositionStays: true);
    }

    Vector3 ComputeUpDirection()
    {
        var planet = ClosestPlanet();
        if (planet == null) return Vector3.up;
        return (transform.position - planet.Position).normalized;
    }

    CelestialBody ClosestPlanet()
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        CelestialBody closest = null;
        float bestSq = float.MaxValue;
        foreach (var b in bodies)
        {
            if (b == null) continue;
            float d = (b.Position - transform.position).sqrMagnitude;
            if (d < bestSq) { bestSq = d; closest = b; }
        }
        return closest;
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age >= lifetime || tmp == null) { Destroy(gameObject); return; }

        transform.position += upDir * FloatSpeed * Time.deltaTime;

        if (_cam == null) _cam = Camera.main;
        if (_cam != null)
        {
            Vector3 toCam = transform.position - _cam.transform.position;
            if (toCam.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(toCam.normalized, upDir);
        }

        float t = age / lifetime;
        var c = tmp.color;
        c.a = Mathf.Clamp01(1f - t * t);
        tmp.color = c;
    }
}
```

- [ ] **Step 2: Extend `SpaceNet.cs` with proximity check + F-prompt**

Add fields near the existing inspector fields:

```csharp
    Transform _playerCached;
    bool _playerInRange;
    float _findPlayerRetryT;
    const float kFindPlayerRetryInterval = 1f;
```

Replace existing `Update()` body with:

```csharp
    void Update()
    {
        // Accumulation while in orbit.
        if (IsCurrentlyInOrbit() && _buffer < bufferCapacity)
            _buffer = Mathf.Min(bufferCapacity, _buffer + dustPerSecond * Time.deltaTime);

        // Lazy-cache the player transform (mirrors the LightLookAt pattern
        // documented in CLAUDE.md — throttled re-find when null).
        if (_playerCached == null)
        {
            _findPlayerRetryT -= Time.deltaTime;
            if (_findPlayerRetryT <= 0f)
            {
                _findPlayerRetryT = kFindPlayerRetryInterval;
                var go = GameObject.FindGameObjectWithTag("Player");
                if (go != null) _playerCached = go.transform;
            }
            if (_playerCached == null) return;
        }

        // Distance check — equivalent to a trigger but immune to compound-
        // collider weirdness (SpaceNet is parented to the Ship's Rigidbody).
        float sqrDist = (_playerCached.position - transform.position).sqrMagnitude;
        bool nowInRange = sqrDist <= collectionRadius * collectionRadius;
        if (nowInRange != _playerInRange)
        {
            _playerInRange = nowInRange;
            if (!_playerInRange) InteractPromptUI.Clear(this);
        }

        if (!_playerInRange) return;

        int n = BufferedDust;
        if (n >= 1)
        {
            InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to collect {n} space dust");
            if (TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
            {
                int drained = Drain(n);
                if (drained > 0 && SpaceDustInventory.Instance != null)
                {
                    SpaceDustInventory.Instance.Add(drained);
                    DustPopup.Spawn(transform.position, drained);
                }
            }
        }
        else
        {
            InteractPromptUI.Clear(this);
        }
    }

    void OnDisable()
    {
        InteractPromptUI.Clear(this);
        _playerInRange = false;
    }
```

- [ ] **Step 3: Compile check**

Use Unity MCP `check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/Ship/DustPopup.cs" "Assets/3 - Scripts/Ship/DustPopup.cs.meta" "Assets/3 - Scripts/Ship/SpaceNet.cs"
git commit -m "feat(space-dust): SpaceNet F-prompt collection + DustPopup"
```

---

## Task 5: `SpaceDustFilter` ShopItem kind + Ship Vendor purchase

**Files:**
- Modify: `Assets/3 - Scripts/Vendor/ShopItem.cs`
- Modify: `Assets/3 - Scripts/Vendor/ShipMarketNPC.cs`
- Modify: `Assets/3 - Scripts/Vendor/ShipMarketShopUI.cs`

- [ ] **Step 1: Add `SpaceDustFilter` enum value**

Open `ShopItem.cs`. Find the `ShopItemKind` enum. Add `SpaceDustFilter` at the end of the existing enum values (keep ordering stable for any saved data).

```csharp
public enum ShopItemKind
{
    // ... existing values ...
    SpaceDustFilter
}
```

(Read the file first to see the existing enum layout; insert as the last value.)

- [ ] **Step 2: Wire `IsAlreadyOwned` + `Purchase` in `ShipMarketNPC.cs`**

Find the `IsAlreadyOwned` method (currently `public bool IsAlreadyOwned(ShopItemKind kind) => false;`). Replace with:

```csharp
    public bool IsAlreadyOwned(ShopItemKind kind)
    {
        if (kind == ShopItemKind.SpaceDustFilter)
            return SpaceDustInventory.Instance != null && SpaceDustInventory.Instance.HasFilter;
        return false; // ships and parts always re-buyable
    }
```

Find the `Purchase` method's switch statement. Add a new case before the `default`:

```csharp
            case ShopItemKind.SpaceDustFilter:
                if (SpaceDustInventory.Instance == null) return PurchaseResult.InvalidItem;
                if (SpaceDustInventory.Instance.HasFilter) return PurchaseResult.InvalidItem;
                if (!PlayerWallet.Instance.SpendMoney(item.price)) return PurchaseResult.NotEnoughMoney;
                SpaceDustInventory.Instance.SetFilterUnlocked(true);
                return PurchaseResult.Success;
```

- [ ] **Step 3: Make `ShipMarketShopUI.ApplyOwnedState` honour `IsAlreadyOwned`**

Open `ShipMarketShopUI.cs`. Find `ApplyOwnedState`. Replace its body with:

```csharp
    void ApplyOwnedState(ShopItem item, RawImage raw, TextMeshProUGUI name, TextMeshProUGUI price, Image bg, Button btn)
    {
        bool owned = _vendor != null && _vendor.IsAlreadyOwned(item.kind);
        bg.color = owned ? C_Owned : C_CardBg;
        if (raw != null) raw.color = owned ? new Color(0.6f, 0.6f, 0.6f, 1f) : Color.white;
        if (price != null)
        {
            price.text = owned ? "OWNED" : ("$" + item.price);
            price.color = owned ? C_Sub : C_Gold;
        }
        if (btn != null) btn.interactable = !owned;
    }
```

Then find `OnBuyClicked`. If the buy button is clicked while greyed, `IsAlreadyOwned` would still let the purchase fall through to `InvalidItem`. That's fine; the button is non-interactable so the click won't fire.

- [ ] **Step 4: Compile check**

Use Unity MCP `check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Vendor/ShopItem.cs" "Assets/3 - Scripts/Vendor/ShipMarketNPC.cs" "Assets/3 - Scripts/Vendor/ShipMarketShopUI.cs"
git commit -m "feat(space-dust): Ship Vendor sells SpaceDustFilter (one-time unlock)"
```

---

## Task 6: `PostGreetingChoicePanel` shared UI

**Files:**
- Create: `Assets/3 - Scripts/NPC_Dialogue/PostGreetingChoicePanel.cs`

This is a singleton procedural panel. Each NPC, after their greeting finishes typing, calls `PostGreetingChoicePanel.Show(rows, onSelect)`; the panel renders numbered rows below the dialogue area and returns the selected index via callback.

- [ ] **Step 1: Create the file**

```csharp
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shared numbered-choice panel used by every NPC after their greeting line.
/// Each row is "<n>. <label>". Player presses the digit key 1-9 OR clicks the
/// row. Closed automatically after a selection or when Hide() is called.
///
/// Singleton — built procedurally on first use, lives on a DontDestroyOnLoad
/// canvas at sortingOrder 900 (above gameplay, below pause menu).
/// </summary>
public class PostGreetingChoicePanel : MonoBehaviour
{
    public static PostGreetingChoicePanel Instance { get; private set; }

    public struct Row
    {
        public string label;
        public bool enabled;
        public Row(string label, bool enabled = true) { this.label = label; this.enabled = enabled; }
    }

    static readonly Color PanelBg     = new Color32(10, 24, 40, 240);
    static readonly Color RowBg       = new Color32(20, 40, 60, 230);
    static readonly Color RowBgHover  = new Color32(40, 70, 100, 240);
    static readonly Color RowText     = new Color32(234, 246, 255, 255);
    static readonly Color RowTextDim  = new Color32(120, 140, 160, 200);
    static readonly Color BorderColor = new Color32(120, 200, 255, 180);

    Canvas _canvas;
    RectTransform _panelRT;
    readonly List<GameObject> _rowGOs = new List<GameObject>();
    readonly List<Row> _currentRows = new List<Row>();
    Action<int> _onSelect;
    bool _visible;

    public bool IsVisible => _visible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("PostGreetingChoicePanel");
        DontDestroyOnLoad(go);
        go.AddComponent<PostGreetingChoicePanel>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildCanvas();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 900;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        // Panel — anchored bottom-center, above dialogue area.
        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        _panelRT = (RectTransform)panel.transform;
        _panelRT.anchorMin = new Vector2(0.5f, 0f);
        _panelRT.anchorMax = new Vector2(0.5f, 0f);
        _panelRT.pivot     = new Vector2(0.5f, 0f);
        _panelRT.anchoredPosition = new Vector2(0f, 220f);
        _panelRT.sizeDelta = new Vector2(520f, 200f);
        var bg = panel.AddComponent<Image>();
        bg.color = PanelBg;
        bg.raycastTarget = true;

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = 6f;
        vlg.padding = new RectOffset(12, 12, 12, 12);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        gameObject.SetActive(true);
        _panelRT.gameObject.SetActive(false);
    }

    public void Show(IList<Row> rows, Action<int> onSelect)
    {
        ClearRows();
        _currentRows.Clear();
        for (int i = 0; i < rows.Count; i++) _currentRows.Add(rows[i]);
        _onSelect = onSelect;
        for (int i = 0; i < rows.Count; i++)
        {
            BuildRow(i, rows[i]);
        }
        _panelRT.gameObject.SetActive(true);
        _visible = true;
    }

    public void Hide()
    {
        if (!_visible) return;
        _visible = false;
        _onSelect = null;
        ClearRows();
        if (_panelRT != null) _panelRT.gameObject.SetActive(false);
    }

    void ClearRows()
    {
        for (int i = 0; i < _rowGOs.Count; i++)
            if (_rowGOs[i] != null) Destroy(_rowGOs[i]);
        _rowGOs.Clear();
    }

    void BuildRow(int index, Row row)
    {
        var go = new GameObject($"Row{index}", typeof(RectTransform));
        go.transform.SetParent(_panelRT, false);
        var img = go.AddComponent<Image>();
        img.color = RowBg;

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 42f;

        var btn = go.AddComponent<Button>();
        btn.interactable = row.enabled;
        var colors = btn.colors;
        colors.normalColor = RowBg;
        colors.highlightedColor = RowBgHover;
        colors.pressedColor = new Color(BorderColor.r, BorderColor.g, BorderColor.b, 0.4f);
        colors.disabledColor = new Color(RowBg.r * 0.6f, RowBg.g * 0.6f, RowBg.b * 0.6f, RowBg.a);
        btn.colors = colors;
        int captured = index;
        btn.onClick.AddListener(() => HandleSelect(captured));

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(go.transform, false);
        var lblRT = (RectTransform)lblGO.transform;
        lblRT.anchorMin = Vector2.zero;
        lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = new Vector2(16, 0);
        lblRT.offsetMax = new Vector2(-16, 0);
        var tmp = lblGO.AddComponent<TextMeshProUGUI>();
        tmp.text = $"{index + 1}. {row.label}";
        tmp.fontSize = 22f;
        tmp.color = row.enabled ? RowText : RowTextDim;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.fontStyle = FontStyles.Bold;
        tmp.raycastTarget = false;

        _rowGOs.Add(go);
    }

    void Update()
    {
        if (!_visible) return;
        for (int i = 0; i < _currentRows.Count && i < 9; i++)
        {
            KeyCode key = (KeyCode)((int)KeyCode.Alpha1 + i);
            if (Input.GetKeyDown(key)) HandleSelect(i);
        }
    }

    void HandleSelect(int index)
    {
        if (index < 0 || index >= _currentRows.Count) return;
        if (!_currentRows[index].enabled) return;
        var cb = _onSelect;
        Hide();
        cb?.Invoke(index);
    }
}
```

- [ ] **Step 2: Compile check**

Use Unity MCP `check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/NPC_Dialogue/PostGreetingChoicePanel.cs" "Assets/3 - Scripts/NPC_Dialogue/PostGreetingChoicePanel.cs.meta"
git commit -m "feat(space-dust): PostGreetingChoicePanel shared numbered-choice UI"
```

---

## Task 7: `NPCSellDustOption` + `SpaceDustSellUI`

**Files:**
- Create: `Assets/3 - Scripts/NPC_Dialogue/NPCSellDustOption.cs`
- Create: `Assets/3 - Scripts/Vendor/SpaceDustSellUI.cs`

- [ ] **Step 1: Create `NPCSellDustOption.cs`**

```csharp
using UnityEngine;

/// <summary>
/// Per-NPC component that holds the rolled accept-chance + price-per-dust for
/// a single conversation. RollFresh() is called at conversation start by each
/// NPC script; the rolled values persist until the NPC's StopConversation
/// flow runs (then the next encounter rolls again).
///
/// Auto-attached on demand via NPCSellDustOption.GetOrAdd(npc) so individual
/// NPC scripts don't need an Inspector field per type.
/// </summary>
public class NPCSellDustOption : MonoBehaviour
{
    [Tooltip("Min/max accept chance, rolled per conversation.")]
    [Range(0f, 1f)] public float minChance = 0.55f;
    [Range(0f, 1f)] public float maxChance = 0.85f;
    [Tooltip("Min/max credits per dust, rolled per conversation.")]
    public int minPricePerDust = 3;
    public int maxPricePerDust = 7;

    public float AcceptChance { get; private set; }
    public int   PricePerDust { get; private set; }

    public static NPCSellDustOption GetOrAdd(MonoBehaviour npc)
    {
        if (npc == null) return null;
        var existing = npc.GetComponent<NPCSellDustOption>();
        return existing != null ? existing : npc.gameObject.AddComponent<NPCSellDustOption>();
    }

    public void RollFresh()
    {
        AcceptChance = Random.Range(minChance, maxChance);
        PricePerDust = Random.Range(minPricePerDust, maxPricePerDust + 1);
    }
}
```

- [ ] **Step 2: Create `SpaceDustSellUI.cs`**

```csharp
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared sell panel used by every NPC. Open(...) takes the rolled values for
/// the active conversation; SELL rolls Random.value against acceptChance.
/// Success awards qty * pricePerDust credits and deducts dust. Fail leaves
/// dust untouched and shows a refusal line; the same rolled values persist
/// so the player can adjust quantity and try again.
/// </summary>
public class SpaceDustSellUI : MonoBehaviour
{
    public static SpaceDustSellUI Instance { get; private set; }

    static readonly Color C_Bg       = new Color32(10, 24, 40, 240);
    static readonly Color C_Border   = new Color32(120, 200, 255, 220);
    static readonly Color C_Header   = new Color32(184, 140, 255, 255);
    static readonly Color C_Label    = new Color32(234, 246, 255, 255);
    static readonly Color C_Value    = new Color32(255, 215, 50, 255);
    static readonly Color C_BtnSell  = new Color32(60, 145, 70, 255);
    static readonly Color C_BtnBack  = new Color32(140, 60, 60, 255);
    static readonly Color C_Ok       = new Color32(110, 220, 130, 255);
    static readonly Color C_Err      = new Color32(255, 110, 110, 255);

    Canvas _canvas;
    RectTransform _panelRT;
    TextMeshProUGUI _header, _priceText, _chanceText, _totalText, _resultText;
    Slider _slider;
    TMP_InputField _qtyInput;
    Button _sellBtn, _cancelBtn;

    string _npcName;
    float _acceptChance;
    int   _pricePerDust;
    Action _onClose;
    Coroutine _resultRoutine;
    bool _suppressInputCallback;
    bool _open;

    string[] _refusalLines = {
        "Hmm, not today.",
        "Pass.",
        "Eh, doesn't speak to me.",
        "I'll think about it... nope.",
        "Not feeling it."
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("SpaceDustSellUI");
        DontDestroyOnLoad(go);
        go.AddComponent<SpaceDustSellUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool IsOpen => _open;

    public void Open(string npcName, float acceptChance, int pricePerDust, Action onClose)
    {
        _npcName = npcName;
        _acceptChance = Mathf.Clamp01(acceptChance);
        _pricePerDust = Mathf.Max(1, pricePerDust);
        _onClose = onClose;
        _open = true;
        _panelRT.gameObject.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (_header != null) _header.text = $"// {npcName.ToUpperInvariant()} — WILL BUY DUST";
        if (_priceText != null) _priceText.text = $"{_pricePerDust} credits / dust";
        if (_chanceText != null) _chanceText.text = $"{Mathf.RoundToInt(_acceptChance * 100f)}% ACCEPT CHANCE";
        RefreshSliderBounds();
        RefreshTotal();
        SetResult("", default);
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;
        _panelRT.gameObject.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        var cb = _onClose;
        _onClose = null;
        cb?.Invoke();
    }

    void Update()
    {
        if (!_open) return;
        // Defensive cursor pinning — same pattern as ShipMarketShopUI.
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;
    }

    void RefreshSliderBounds()
    {
        int total = SpaceDustInventory.Instance != null ? SpaceDustInventory.Instance.Count : 0;
        _slider.minValue = total > 0 ? 1 : 0;
        _slider.maxValue = total;
        _slider.wholeNumbers = true;
        _slider.SetValueWithoutNotify(total);
        _suppressInputCallback = true;
        _qtyInput.text = total.ToString();
        _suppressInputCallback = false;
        _sellBtn.interactable = total > 0;
    }

    void RefreshTotal()
    {
        int qty = Mathf.RoundToInt(_slider.value);
        if (_totalText != null) _totalText.text = $"Payout if accepted: {qty * _pricePerDust} credits";
    }

    void OnSliderChanged(float v)
    {
        _suppressInputCallback = true;
        _qtyInput.text = Mathf.RoundToInt(v).ToString();
        _suppressInputCallback = false;
        RefreshTotal();
    }

    void OnQtyInputChanged(string text)
    {
        if (_suppressInputCallback) return;
        if (!int.TryParse(text, out int v)) v = 1;
        v = Mathf.Clamp(v, (int)_slider.minValue, (int)_slider.maxValue);
        _slider.SetValueWithoutNotify(v);
        _suppressInputCallback = true;
        if (text != v.ToString()) _qtyInput.text = v.ToString();
        _suppressInputCallback = false;
        RefreshTotal();
    }

    void OnSellClicked()
    {
        int qty = Mathf.RoundToInt(_slider.value);
        if (qty <= 0) return;
        if (SpaceDustInventory.Instance == null) return;
        if (SpaceDustInventory.Instance.Count < qty) { qty = SpaceDustInventory.Instance.Count; }
        bool accepted = Random.value < _acceptChance;
        if (accepted)
        {
            int credits = qty * _pricePerDust;
            SpaceDustInventory.Instance.Spend(qty);
            if (PlayerWallet.Instance != null) PlayerWallet.Instance.AddMoney(credits);
            SetResult($"+{credits} credits!", C_Ok);
        }
        else
        {
            SetResult(_refusalLines[Random.Range(0, _refusalLines.Length)], C_Err);
        }
        RefreshSliderBounds();
        RefreshTotal();
    }

    void SetResult(string text, Color color)
    {
        if (_resultText == null) return;
        if (_resultRoutine != null) StopCoroutine(_resultRoutine);
        _resultText.text = text;
        _resultText.color = color;
        if (!string.IsNullOrEmpty(text))
            _resultRoutine = StartCoroutine(FadeResult());
    }

    IEnumerator FadeResult()
    {
        yield return new WaitForSecondsRealtime(2.5f);
        if (_resultText != null) _resultText.text = "";
    }

    void BuildUI()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = UILayer.Vendor; // same band as ShipMarketShopUI
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        var dim = new GameObject("Dim", typeof(RectTransform));
        dim.transform.SetParent(transform, false);
        var dimRT = (RectTransform)dim.transform;
        dimRT.anchorMin = Vector2.zero; dimRT.anchorMax = Vector2.one;
        dimRT.offsetMin = Vector2.zero; dimRT.offsetMax = Vector2.zero;
        var dimImg = dim.AddComponent<Image>();
        dimImg.color = new Color(0, 0, 0, 0.55f);
        dimImg.raycastTarget = true;

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        _panelRT = (RectTransform)panel.transform;
        _panelRT.anchorMin = _panelRT.anchorMax = _panelRT.pivot = new Vector2(0.5f, 0.5f);
        _panelRT.sizeDelta = new Vector2(640, 460);
        var bg = panel.AddComponent<Image>();
        bg.color = C_Bg;

        _header    = MkText(_panelRT, "// VENDOR — WILL BUY DUST", new Vector2(0, -16), 22, C_Header, FontStyles.Bold);
        _priceText = MkText(_panelRT, "0 credits / dust",          new Vector2(0, -70), 30, C_Value,  FontStyles.Bold);
        _chanceText= MkText(_panelRT, "0% ACCEPT CHANCE",          new Vector2(0, -110), 24, C_Header, FontStyles.Bold);
        _totalText = MkText(_panelRT, "Payout if accepted: 0",     new Vector2(0, -240), 18, C_Label,  FontStyles.Normal);
        _resultText= MkText(_panelRT, "",                          new Vector2(0, -280), 22, C_Ok,     FontStyles.Bold);

        // Slider
        var sliderGO = new GameObject("Slider", typeof(RectTransform));
        sliderGO.transform.SetParent(_panelRT, false);
        var sRT = (RectTransform)sliderGO.transform;
        sRT.anchorMin = sRT.anchorMax = new Vector2(0.5f, 1f);
        sRT.pivot = new Vector2(0.5f, 1f);
        sRT.sizeDelta = new Vector2(420, 24);
        sRT.anchoredPosition = new Vector2(0, -160);

        _slider = sliderGO.AddComponent<Slider>();
        var sliderBg = new GameObject("Bg", typeof(RectTransform));
        sliderBg.transform.SetParent(sliderGO.transform, false);
        var bgRT = (RectTransform)sliderBg.transform;
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
        var bgImg = sliderBg.AddComponent<Image>();
        bgImg.color = new Color32(20, 40, 60, 255);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGO.transform, false);
        var faRT = (RectTransform)fillArea.transform;
        faRT.anchorMin = new Vector2(0, 0.25f); faRT.anchorMax = new Vector2(1, 0.75f);
        faRT.offsetMin = new Vector2(8, 0); faRT.offsetMax = new Vector2(-8, 0);
        var fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(fillArea.transform, false);
        var fillRT = (RectTransform)fill.transform;
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = Vector2.zero; fillRT.offsetMax = Vector2.zero;
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = C_Border;
        _slider.fillRect = fillRT;
        _slider.targetGraphic = bgImg;
        _slider.direction = Slider.Direction.LeftToRight;
        _slider.onValueChanged.AddListener(OnSliderChanged);

        // Qty input
        var inputGO = new GameObject("QtyInput", typeof(RectTransform));
        inputGO.transform.SetParent(_panelRT, false);
        var inRT = (RectTransform)inputGO.transform;
        inRT.anchorMin = inRT.anchorMax = new Vector2(0.5f, 1f);
        inRT.pivot = new Vector2(0.5f, 1f);
        inRT.sizeDelta = new Vector2(120, 32);
        inRT.anchoredPosition = new Vector2(0, -200);
        var inImg = inputGO.AddComponent<Image>();
        inImg.color = new Color32(8, 16, 24, 255);

        var inputTextGO = new GameObject("Text", typeof(RectTransform));
        inputTextGO.transform.SetParent(inputGO.transform, false);
        var itRT = (RectTransform)inputTextGO.transform;
        itRT.anchorMin = Vector2.zero; itRT.anchorMax = Vector2.one;
        itRT.offsetMin = new Vector2(8, 4); itRT.offsetMax = new Vector2(-8, -4);
        var itTmp = inputTextGO.AddComponent<TextMeshProUGUI>();
        itTmp.fontSize = 18;
        itTmp.color = C_Label;
        itTmp.alignment = TextAlignmentOptions.Center;
        itTmp.raycastTarget = false;

        _qtyInput = inputGO.AddComponent<TMP_InputField>();
        _qtyInput.textComponent = itTmp;
        _qtyInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        _qtyInput.onValueChanged.AddListener(OnQtyInputChanged);

        // Buttons row
        var rowGO = new GameObject("ButtonRow", typeof(RectTransform));
        rowGO.transform.SetParent(_panelRT, false);
        var rRT = (RectTransform)rowGO.transform;
        rRT.anchorMin = new Vector2(0, 0); rRT.anchorMax = new Vector2(1, 0);
        rRT.pivot = new Vector2(0.5f, 0);
        rRT.sizeDelta = new Vector2(0, 60);
        rRT.anchoredPosition = new Vector2(0, 16);
        var hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 20;
        hlg.padding = new RectOffset(40, 40, 0, 0);
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        _cancelBtn = MkBtn(rRT, "CANCEL", C_BtnBack, Close);
        _sellBtn   = MkBtn(rRT, "SELL",   C_BtnSell, OnSellClicked);

        _panelRT.gameObject.SetActive(false);
    }

    static TextMeshProUGUI MkText(RectTransform parent, string text, Vector2 anchoredPos, int size, Color color, FontStyles style)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(600, 40);
        rt.anchoredPosition = anchoredPos;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }

    static Button MkBtn(Transform parent, string label, Color color, Action onClick)
    {
        var go = new GameObject($"Btn_{label}", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var btn = go.AddComponent<Button>();
        btn.onClick.AddListener(() => onClick?.Invoke());
        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(go.transform, false);
        var lblRT = (RectTransform)lblGO.transform;
        lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = Vector2.zero; lblRT.offsetMax = Vector2.zero;
        var lbl = lblGO.AddComponent<TextMeshProUGUI>();
        lbl.text = label;
        lbl.fontSize = 22;
        lbl.fontStyle = FontStyles.Bold;
        lbl.alignment = TextAlignmentOptions.Center;
        lbl.color = Color.white;
        lbl.raycastTarget = false;
        return btn;
    }
}
```

- [ ] **Step 3: Compile check**

Use Unity MCP `check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/NPC_Dialogue/NPCSellDustOption.cs" "Assets/3 - Scripts/NPC_Dialogue/NPCSellDustOption.cs.meta" "Assets/3 - Scripts/Vendor/SpaceDustSellUI.cs" "Assets/3 - Scripts/Vendor/SpaceDustSellUI.cs.meta"
git commit -m "feat(space-dust): NPCSellDustOption + SpaceDustSellUI"
```

---

## Task 8a: Wire `FishMarketNPC` post-greeting choice

**Files:**
- Modify: `Assets/3 - Scripts/Fishing/FishMarketNPC.cs`

The fish vendor currently: greeting → opens sell-fish panel. We insert the choice panel between greeting and panel open. Choices: `1. Sell fish`, `2. Sell space dust`, `3. Leave`.

- [ ] **Step 1: Read the existing FishMarketNPC**

First read `Assets/3 - Scripts/Fishing/FishMarketNPC.cs` to locate the methods named like `StartConversation`, the typewriter coroutine, and where the sell panel is opened.

- [ ] **Step 2: Add NPCSellDustOption integration**

Near the top of `FishMarketNPC` add (along with the rest of the inspector fields):

```csharp
    NPCSellDustOption _sellDustOption;
    bool _waitingForChoice;
```

In `StartConversation` (or whatever starts the typewriter), right at the top, add:

```csharp
        _sellDustOption = NPCSellDustOption.GetOrAdd(this);
        _sellDustOption.RollFresh();
```

(Tune the dust ranges later in the inspector: vendor band 55–85% / 3–7c is the default already.)

- [ ] **Step 3: Insert the choice panel between greeting and open-shop**

Find the point in the dialogue coroutine where it currently calls the "open sell panel" function immediately after the typewriter line finishes. Replace that direct call with:

```csharp
        ShowPostGreetingChoice();
```

Add the new method:

```csharp
    void ShowPostGreetingChoice()
    {
        bool hasDust = SpaceDustInventory.Instance != null && SpaceDustInventory.Instance.Count > 0;
        var rows = new System.Collections.Generic.List<PostGreetingChoicePanel.Row>
        {
            new PostGreetingChoicePanel.Row("Sell fish", true),
            new PostGreetingChoicePanel.Row(hasDust ? "Sell space dust" : "Sell space dust (no dust)", hasDust),
            new PostGreetingChoicePanel.Row("Leave", true),
        };
        _waitingForChoice = true;
        PostGreetingChoicePanel.Instance.Show(rows, HandleChoice);
    }

    void HandleChoice(int index)
    {
        _waitingForChoice = false;
        switch (index)
        {
            case 0: OpenSellPanel(); break; // existing method that opens fish-sell UI
            case 1: OpenSellDust(); break;
            case 2: StopConversation(); break;
        }
    }

    void OpenSellDust()
    {
        if (_sellDustOption == null) { StopConversation(); return; }
        SpaceDustSellUI.Instance.Open(
            npcName: "Fish Vendor",
            acceptChance: _sellDustOption.AcceptChance,
            pricePerDust: _sellDustOption.PricePerDust,
            onClose: ShowPostGreetingChoice
        );
    }
```

Replace `OpenSellPanel` with whatever the file's existing method that activates the sell-fish UI is named (read the file to find it; commonly something like `OpenSellPanel()` or `BeginSelling()`). Do not rename it — call the existing method.

- [ ] **Step 4: On `OnTriggerExit` / `StopConversation`, close any open sub-UIs**

In `StopConversation()`, before clearing state, add:

```csharp
        if (PostGreetingChoicePanel.Instance != null && PostGreetingChoicePanel.Instance.IsVisible)
            PostGreetingChoicePanel.Instance.Hide();
        if (SpaceDustSellUI.Instance != null && SpaceDustSellUI.Instance.IsOpen)
            SpaceDustSellUI.Instance.Close();
```

- [ ] **Step 5: Compile check**

Use Unity MCP `check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/Fishing/FishMarketNPC.cs"
git commit -m "feat(space-dust): fish vendor offers Sell space dust in post-greeting menu"
```

---

## Task 8b: Wire `Alien7Vendor` post-greeting choice

**Files:**
- Modify: `Assets/3 - Scripts/Vendor/Alien7Vendor.cs`

Goods vendor currently: greeting → click → `OpenShop`. Insert the choice panel; choices: `1. Open shop`, `2. Sell space dust`, `3. Leave`.

- [ ] **Step 1: Find the existing `OpenShop` call site**

Read `Assets/3 - Scripts/Vendor/Alien7Vendor.cs`. The `PlayDialogueSequence` coroutine ends with `OpenShop();`. We will replace that with `ShowPostGreetingChoice();` and route the choices.

- [ ] **Step 2: Add fields**

Near other private fields:

```csharp
    NPCSellDustOption _sellDustOption;
```

- [ ] **Step 3: Roll values at conversation start**

In `StartConversation`, before `StartCoroutine(PlayDialogueSequence())`:

```csharp
        _sellDustOption = NPCSellDustOption.GetOrAdd(this);
        _sellDustOption.RollFresh();
```

- [ ] **Step 4: Replace `OpenShop()` call in `PlayDialogueSequence` with the choice panel**

Replace the `OpenShop();` line with:

```csharp
        ShowPostGreetingChoice();
```

Add new methods on the class:

```csharp
    void ShowPostGreetingChoice()
    {
        bool hasDust = SpaceDustInventory.Instance != null && SpaceDustInventory.Instance.Count > 0;
        var rows = new System.Collections.Generic.List<PostGreetingChoicePanel.Row>
        {
            new PostGreetingChoicePanel.Row("Open shop", true),
            new PostGreetingChoicePanel.Row(hasDust ? "Sell space dust" : "Sell space dust (no dust)", hasDust),
            new PostGreetingChoicePanel.Row("Leave", true),
        };
        PostGreetingChoicePanel.Instance.Show(rows, HandleChoice);
    }

    void HandleChoice(int index)
    {
        switch (index)
        {
            case 0: OpenShop(); break;
            case 1: OpenSellDust(); break;
            case 2: StopConversation(); break;
        }
    }

    void OpenSellDust()
    {
        if (_sellDustOption == null) { StopConversation(); return; }
        SpaceDustSellUI.Instance.Open(
            npcName: "Goods Vendor",
            acceptChance: _sellDustOption.AcceptChance,
            pricePerDust: _sellDustOption.PricePerDust,
            onClose: ShowPostGreetingChoice
        );
    }
```

- [ ] **Step 5: On `StopConversation`, close open sub-UIs**

In `StopConversation()`, add:

```csharp
        if (PostGreetingChoicePanel.Instance != null && PostGreetingChoicePanel.Instance.IsVisible)
            PostGreetingChoicePanel.Instance.Hide();
        if (SpaceDustSellUI.Instance != null && SpaceDustSellUI.Instance.IsOpen)
            SpaceDustSellUI.Instance.Close();
```

- [ ] **Step 6: Compile check**

Use Unity MCP `check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 7: Commit**

```bash
git add "Assets/3 - Scripts/Vendor/Alien7Vendor.cs"
git commit -m "feat(space-dust): goods vendor offers Sell space dust in post-greeting menu"
```

---

## Task 8c: Wire `ShipMarketNPC` post-greeting choice

**Files:**
- Modify: `Assets/3 - Scripts/Vendor/ShipMarketNPC.cs`

Same shape as Alien7Vendor. Choices: `1. Open shop`, `2. Sell space dust`, `3. Leave`. `OpenShop` already exists.

- [ ] **Step 1: Add field, roll values, replace `OpenShop()`**

Add field:

```csharp
    NPCSellDustOption _sellDustOption;
```

In `StartConversation`, before `StartCoroutine(PlayDialogueSequence())`:

```csharp
        _sellDustOption = NPCSellDustOption.GetOrAdd(this);
        _sellDustOption.RollFresh();
```

In `PlayDialogueSequence`, replace `OpenShop();` with `ShowPostGreetingChoice();`.

Add methods:

```csharp
    void ShowPostGreetingChoice()
    {
        bool hasDust = SpaceDustInventory.Instance != null && SpaceDustInventory.Instance.Count > 0;
        var rows = new System.Collections.Generic.List<PostGreetingChoicePanel.Row>
        {
            new PostGreetingChoicePanel.Row("Open shop", true),
            new PostGreetingChoicePanel.Row(hasDust ? "Sell space dust" : "Sell space dust (no dust)", hasDust),
            new PostGreetingChoicePanel.Row("Leave", true),
        };
        PostGreetingChoicePanel.Instance.Show(rows, HandleChoice);
    }

    void HandleChoice(int index)
    {
        switch (index)
        {
            case 0: OpenShop(); break;
            case 1: OpenSellDust(); break;
            case 2: StopConversation(); break;
        }
    }

    void OpenSellDust()
    {
        if (_sellDustOption == null) { StopConversation(); return; }
        SpaceDustSellUI.Instance.Open(
            npcName: "Ship Vendor",
            acceptChance: _sellDustOption.AcceptChance,
            pricePerDust: _sellDustOption.PricePerDust,
            onClose: ShowPostGreetingChoice
        );
    }
```

- [ ] **Step 2: Close sub-UIs in `StopConversation()`**

```csharp
        if (PostGreetingChoicePanel.Instance != null && PostGreetingChoicePanel.Instance.IsVisible)
            PostGreetingChoicePanel.Instance.Hide();
        if (SpaceDustSellUI.Instance != null && SpaceDustSellUI.Instance.IsOpen)
            SpaceDustSellUI.Instance.Close();
```

- [ ] **Step 3: Compile check**

Use Unity MCP `check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/Vendor/ShipMarketNPC.cs"
git commit -m "feat(space-dust): ship vendor offers Sell space dust in post-greeting menu"
```

---

## Task 8d: Wire `RandomAlienDialogue` post-greeting choice

**Files:**
- Modify: `Assets/3 - Scripts/NPC_Dialogue/RandomAlienDialogue.cs`

Random alien currently: greeting → click → `StopConversation`. We insert the choice panel. Choices: `1. Sell space dust`, `2. Leave`. Random aliens use wider ranges (15–75% / 4–18c).

- [ ] **Step 1: Read the file**

`Assets/3 - Scripts/NPC_Dialogue/RandomAlienDialogue.cs`.

- [ ] **Step 2: Add field + roll fresh**

Add a private field `NPCSellDustOption _sellDustOption;`.

In `StartConversation()`:

```csharp
        _sellDustOption = NPCSellDustOption.GetOrAdd(this);
        // Random aliens roll wider/swingier than vendors.
        _sellDustOption.minChance = 0.15f;
        _sellDustOption.maxChance = 0.75f;
        _sellDustOption.minPricePerDust = 4;
        _sellDustOption.maxPricePerDust = 18;
        _sellDustOption.RollFresh();
```

- [ ] **Step 3: After greeting typewriter finishes, show choice instead of waiting**

Find the line in the dialogue coroutine that runs after the typewriter finishes and waits for click. The current flow is: type → wait for click → end conversation. Replace the "end conversation" path with:

```csharp
        // Wait for player click as before — keeps the existing skip / dismiss feel.
        yield return new WaitUntil(() => !_waitingForClick || !_playerInRange);
        if (!_playerInRange) yield break;
        ShowPostGreetingChoice();
```

Add the methods:

```csharp
    void ShowPostGreetingChoice()
    {
        bool hasDust = SpaceDustInventory.Instance != null && SpaceDustInventory.Instance.Count > 0;
        var rows = new System.Collections.Generic.List<PostGreetingChoicePanel.Row>
        {
            new PostGreetingChoicePanel.Row(hasDust ? "Sell space dust" : "Sell space dust (no dust)", hasDust),
            new PostGreetingChoicePanel.Row("Leave", true),
        };
        PostGreetingChoicePanel.Instance.Show(rows, HandleChoice);
    }

    void HandleChoice(int index)
    {
        switch (index)
        {
            case 0: OpenSellDust(); break;
            case 1: StopConversation(); break;
        }
    }

    void OpenSellDust()
    {
        if (_sellDustOption == null) { StopConversation(); return; }
        SpaceDustSellUI.Instance.Open(
            npcName: "Wandering Alien",
            acceptChance: _sellDustOption.AcceptChance,
            pricePerDust: _sellDustOption.PricePerDust,
            onClose: ShowPostGreetingChoice
        );
    }
```

(If `RandomAlienDialogue` doesn't currently have a `StopConversation` method, add a simple one that hides dialogue, clears `_waitingForClick`, and is also called from `OnTriggerExit` — read the file to see existing exit logic and either re-use it or add this method.)

- [ ] **Step 4: Close sub-UIs on exit**

In the existing `OnTriggerExit` / conversation-stop path, add:

```csharp
        if (PostGreetingChoicePanel.Instance != null && PostGreetingChoicePanel.Instance.IsVisible)
            PostGreetingChoicePanel.Instance.Hide();
        if (SpaceDustSellUI.Instance != null && SpaceDustSellUI.Instance.IsOpen)
            SpaceDustSellUI.Instance.Close();
```

- [ ] **Step 5: Compile check**

Use Unity MCP `check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/NPC_Dialogue/RandomAlienDialogue.cs"
git commit -m "feat(space-dust): random aliens offer Sell space dust after their line"
```

---

## Task 9: PlayerWallet DUST chip

**Files:**
- Modify: `Assets/3 - Scripts/Player/PlayerWallet.cs`

Add a fourth chip mirroring the existing WOOD chip — only visible while the player has dust OR has unlocked the filter (the second condition so the player can see the chip the moment they buy the filter, before they've gathered anything).

- [ ] **Step 1: Read the existing chip layout**

Read `Assets/3 - Scripts/Player/PlayerWallet.cs` in full. Identify `CreateCornerHUD`, the `RefreshWood` method, the `_woodChip` / `_ammoChip` field layout, and the `Update` loop's chip-visibility logic.

- [ ] **Step 2: Add DUST fields + value colour**

Near the existing chip fields, add:

```csharp
    public TextMeshProUGUI dustText;
    GameObject _dustChip;
    int _lastDustSeen = int.MinValue;
    bool _dustChipVisible;
    static readonly Color DustValueColor = new Color32(0xB8, 0x8C, 0xFF, 0xFF); // violet (matches DustPopup)
```

- [ ] **Step 3: In `CreateCornerHUD`, add a DUST chip alongside MONEY / WOOD / AMMO**

Locate the line(s) that create `_woodChip` (calls something like `CreateChip(...)`). Immediately after the wood chip's creation block, add a parallel block that creates `_dustChip` with `dustText` as its value text and "DUST" as its label, using `DustValueColor`. Start it disabled (`_dustChip.SetActive(false);`).

(Pattern-match exactly what `_woodChip` does — same parent, same horizontal layout slot, same chip-builder helper.)

- [ ] **Step 4: Update visibility + value in `Update()`**

After the existing wood / ammo blocks, add:

```csharp
        var dustInv = SpaceDustInventory.Instance;
        int dust = dustInv != null ? dustInv.Count : 0;
        bool dustShouldShow = dustInv != null && (dust > 0 || dustInv.HasFilter);
        if (dustShouldShow != _dustChipVisible)
        {
            _dustChipVisible = dustShouldShow;
            if (_dustChip != null) _dustChip.SetActive(dustShouldShow);
        }
        if (dustShouldShow && dust != _lastDustSeen)
        {
            _lastDustSeen = dust;
            if (dustText != null) dustText.text = dust.ToString();
        }
```

- [ ] **Step 5: Compile check**

Use Unity MCP `check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/Player/PlayerWallet.cs"
git commit -m "feat(space-dust): add DUST chip to PlayerWallet HUD"
```

---

## Task 10: Save schema + capture/apply

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveData.cs`
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`

- [ ] **Step 1: Add `SpaceDustSave` to `SaveData.cs`**

At the bottom of `SaveData.cs`, alongside the other `[Serializable]` classes:

```csharp
    [Serializable]
    public class SpaceDustSave
    {
        public int playerDust;
        public bool hasFilter;
        // Parallel arrays — bought ships indexed by BoughtShip.shipNumber.
        public List<int> netShipNumbers = new List<int>();
        public List<int> netBuffers     = new List<int>();
        // Sentinel for the scene's original (non-bought) ship.
        public int sceneShipBuffer;
    }
```

Add a corresponding field on the top-level `SaveData` class (read the file to see where other top-level saves are declared — `playerSave`, `shipSave`, etc.). Add:

```csharp
    public SpaceDustSave spaceDust = new SpaceDustSave();
```

- [ ] **Step 2: Add `CaptureSpaceDust` to `SaveCollector.cs`**

```csharp
    static void CaptureSpaceDust(SaveData data)
    {
        var s = data.spaceDust;
        s.playerDust = SpaceDustInventory.Instance != null ? SpaceDustInventory.Instance.Count : 0;
        s.hasFilter  = SpaceDustInventory.Instance != null && SpaceDustInventory.Instance.HasFilter;
        s.netShipNumbers.Clear();
        s.netBuffers.Clear();
        s.sceneShipBuffer = 0;
        var nets = UnityEngine.Object.FindObjectsOfType<SpaceNet>(true);
        for (int i = 0; i < nets.Length; i++)
        {
            var net = nets[i];
            if (net == null || net.OwningShip == null) continue;
            var bought = net.OwningShip.GetComponent<BoughtShip>();
            int buffered = net.BufferedDust;
            if (bought != null)
            {
                s.netShipNumbers.Add(bought.shipNumber);
                s.netBuffers.Add(buffered);
            }
            else
            {
                s.sceneShipBuffer = buffered;
            }
        }
    }
```

Call `CaptureSpaceDust(data)` from `Capture(name)` near the other capture calls (order doesn't matter for capture).

- [ ] **Step 3: Add `ApplySpaceDust` and call it after `ApplyExtraShips`**

```csharp
    static void ApplySpaceDust(SaveData data)
    {
        if (data == null || data.spaceDust == null) return;
        var s = data.spaceDust;
        if (SpaceDustInventory.Instance == null)
        {
            var go = new GameObject("SpaceDustInventory");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<SpaceDustInventory>();
        }
        SpaceDustInventory.Instance.SetCount(s.playerDust);
        SpaceDustInventory.Instance.SetFilterUnlocked(s.hasFilter);

        var nets = UnityEngine.Object.FindObjectsOfType<SpaceNet>(true);
        for (int i = 0; i < nets.Length; i++)
        {
            var net = nets[i];
            if (net == null || net.OwningShip == null) continue;
            var bought = net.OwningShip.GetComponent<BoughtShip>();
            if (bought != null)
            {
                int idx = s.netShipNumbers.IndexOf(bought.shipNumber);
                int v = (idx >= 0 && idx < s.netBuffers.Count) ? s.netBuffers[idx] : 0;
                net.SetRawBuffer(v);
            }
            else
            {
                net.SetRawBuffer(s.sceneShipBuffer);
            }
        }
    }
```

In `Apply(data)`, after the existing `ApplyExtraShips(data);` call, insert:

```csharp
        ApplySpaceDust(data);
```

(Apply order matters: extra ships must exist before we look up `BoughtShip.shipNumber` to match buffers.)

- [ ] **Step 4: Compile check**

Use Unity MCP `check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/SaveSystem/SaveData.cs" "Assets/3 - Scripts/SaveSystem/SaveCollector.cs"
git commit -m "feat(save): persist space dust inventory + per-ship net buffers"
```

---

## Task 11: Author / Unity-MCP setup

This task uses Unity MCP to wire scene-side / asset-side requirements that aren't editable from raw C#.

**Author manual prerequisites (state these clearly to the user before starting this task):**

1. Make a small visual mesh for the "filter / net" prop (any cube / box / your preferred art). Save it as a prefab or scene-side GameObject if you prefer — it just needs to be a child of each ship.
2. Drop one onto each ship variant prefab you want to be dust-capable (typical set: `Ship_Full`, `Ship_MissingLeft`, `Ship_MissingRight`, `Ship_NoThrusters`, plus the `SHIP44` bought-ship prefab). Position it visually wherever looks right; the SpaceNet's collider is auto-sized at runtime.
3. Tell me when you've placed them; I'll add the `SpaceNet` component to each placement via Unity MCP.

Once placements are done:

- [ ] **Step 1: For each placed visual, add the `SpaceNet` component via MCP**

For each path the user supplies, use Unity MCP `add_component`:

```text
add_component(gameObjectPath, "SpaceNet")
```

- [ ] **Step 2: Create the `SpaceDustFilter` ShopItem asset**

Run a one-off editor script that creates the asset. Save it as `Assets/Editor/_CreateSpaceDustFilterShopItem.cs`:

```csharp
using UnityEditor;
using UnityEngine;

public static class _CreateSpaceDustFilterShopItem
{
    public static void Execute()
    {
        var item = ScriptableObject.CreateInstance<ShopItem>();
        item.kind = ShopItemKind.SpaceDustFilter;
        item.displayName = "Space Dust Filter";
        item.price = 600;
        item.description = "A square mesh filter that mounts on any ship. While the ship is parked in orbit, the filter passively gathers space dust. Sell the dust to any NPC for credits.";
        // Use a sensible default preview prefab if one is available; otherwise leave null
        // and the shop card shows a flat coloured panel. Replace via Inspector if desired.
        AssetDatabase.CreateAsset(item, "Assets/3 - Scripts/Vendor/ShopItems/SpaceDustFilter.asset");
        AssetDatabase.SaveAssets();
        Debug.Log("[_CreateSpaceDustFilterShopItem] Created at Assets/3 - Scripts/Vendor/ShopItems/SpaceDustFilter.asset");
    }
}
```

Run via Unity MCP `execute_script`. Verify the asset exists.

- [ ] **Step 3: Add the new asset to the Ship Vendor's `inventory[]` via MCP**

Find the Ship Vendor GameObject in the scene (path will be inside `--- NPCs ---/Ship Vendor` or similar; use `list_game_objects_in_hierarchy` with `componentFilter: "ShipMarketNPC"`). Then read its inventory array and append the new asset using `set_property` on the `inventory` field. (If `set_property` cannot append, write a second one-off editor script that loads the GameObject, appends the asset, and `EditorUtility.SetDirty`s the scene.)

- [ ] **Step 4: Delete the temp editor script**

```bash
rm "Assets/Editor/_CreateSpaceDustFilterShopItem.cs" "Assets/Editor/_CreateSpaceDustFilterShopItem.cs.meta"
```

- [ ] **Step 5: Compile check**

Use Unity MCP `check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(space-dust): wire SpaceNet onto ships + register ShopItem with ship vendor"
```

---

## Task 12: End-to-end play-mode verification

This task is a manual checklist run by the implementer inside Unity. No code changes; produce a written pass/fail per item, fix anything broken, then commit any fixes as separate commits.

- [ ] **Step 1: Filter unlock**

1. Play the gameplay scene.
2. Open the Ship Vendor shop. Verify "Space Dust Filter" card visible at $600.
3. Buy it. Verify wallet decremented and card greys to "OWNED".
4. Verify the HUD now shows the DUST chip at 0.

- [ ] **Step 2: Dust accumulation**

1. Pilot the ship, get above the planet (not landed), exit.
2. Walk away from the ship.
3. Wait ~30s (with default 0.1 dust/sec that's ~3 dust).
4. Return to the ship → SpaceNet's F prompt should read "Press F to collect 3 space dust" (approx).
5. Press F. DUST chip increments; DustPopup floats up from the net.

- [ ] **Step 3: Fish vendor sell flow**

1. Walk up to the Fish Vendor. Press F to greet.
2. After the greeting, verify the choice panel shows: `1. Sell fish`, `2. Sell space dust`, `3. Leave`.
3. Press `2`. Verify the sell-dust UI opens with rolled %  and price in the vendor range (3–7c, 55–85%).
4. Slide quantity, hit SELL. Verify success → credits up, dust down; or failure → refusal line, dust untouched.
5. Cancel back to choice panel; Leave to dismiss.

- [ ] **Step 4: Goods + ship vendor sell flow**

Same as fish vendor — verify the choice panel shows `Open shop`, `Sell space dust`, `Leave` for both Alien7 and Ship Vendor.

- [ ] **Step 5: Random alien sell flow**

1. Find any random alien NPC.
2. Press F; let greeting play; left-click to skip.
3. Verify the choice panel shows: `1. Sell space dust`, `2. Leave`.
4. Pick "Sell space dust"; verify rolled values are in the wider range (4–18c, 15–75%).

- [ ] **Step 6: Save round-trip**

1. Save with: filter unlocked, dust > 0, at least one ship's net has buffer > 0.
2. Reload save.
3. Verify: filter still unlocked, HUD dust matches, the ship net F prompt still shows the buffered amount.

- [ ] **Step 7: Existing flows unbroken**

1. Verify fish vendor "Sell fish" → opens existing fish sell panel; works as before.
2. Verify goods vendor "Open shop" → opens existing shop; works as before.
3. Verify ship vendor "Open shop" → opens existing shop; ships still buyable.

- [ ] **Step 8: Pilot ship → buffer paused; land → buffer paused**

1. Park a ship in orbit, let it accumulate a few seconds.
2. Pilot it. Wait — verify buffer count does NOT change while piloting.
3. Land — verify buffer count does NOT change while landed.
4. Exit + take off again → buffer resumes.

- [ ] **Step 9: If any test fails — fix, commit, retry that test only**

For each fix:
```bash
git add <changed files>
git commit -m "fix(space-dust): <what was broken>"
```

---

## Self-Review Notes

I checked the plan against the spec and confirmed every section is covered by at least one task:

- **Architecture / Components** → Tasks 1, 3, 4, 6, 7
- **Filter purchase** → Task 5
- **Orbit detection** → Task 3 (with `Ship.IsLanded` from Task 2)
- **F-prompt collection** → Task 4
- **NPC sell flow** → Tasks 6, 7, 8a–d
- **Sell UI** → Task 7
- **Rolled values per NPC** → Task 7 (defaults) + Task 8d (random-alien override)
- **HUD** → Task 9
- **Save system** → Task 10
- **Build / Author Setup** → Task 11
- **Testing Plan** → Task 12

No placeholders remain.
