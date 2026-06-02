# Cleanup Pass — Phase 1 (Real Bugs) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the five real bugs identified in the cleanup audit as one verifiable batch, then hand off to user for in-Editor verification before phases 2-5 begin.

**Architecture:** Each bug fix is a small, additive change. Two new tiny modules (`UILayer` constants, `BonfireUIRegistry` static registry) plus localized edits to ~10 existing files. No behavior changes outside the five bug scopes.

**Tech Stack:** Unity 2022.3, C#, no asmdefs, no test framework — verification is manual in Unity Editor.

**Source spec:** `docs/superpowers/specs/2026-05-13-cleanup-pass-design.md`

**Verification model:** Unity has no automated tests in this project. Each task's "verification" step describes the expected behavior and the in-Editor scenario the user runs at the end (Task 14). Compile-clean is the only between-task gate.

---

## File Structure

| File | Action | Purpose |
|---|---|---|
| `Assets/3 - Scripts/UI/UILayer.cs` | Create | Single source of truth for canvas `sortingOrder` constants. |
| `Assets/3 - Scripts/UI/TabbedPauseMenu.cs` | Modify line 237 | Read `UILayer.Pause` instead of literal 1000. |
| `Assets/3 - Scripts/SaveSystem/AutosaveManager.cs` | Modify line 121 | Read `UILayer.Toast` (toast under pause). |
| `Assets/3 - Scripts/UI/StoryImpactNotice.cs` | Modify line 41 | Read `UILayer.Toast`. |
| `Assets/3 - Scripts/Vendor/GoodsVendorShopUI.cs` | Modify line 130 | Read `UILayer.Vendor`. |
| `Assets/3 - Scripts/Vendor/ShipMarketShopUI.cs` | Modify line 125 | Read `UILayer.Vendor`. |
| `Assets/3 - Scripts/Map/MapBootstrapReal.cs` | Modify line 66 | Read `UILayer.Map` (currently 1000 → conflicts with pause). |
| `Assets/3 - Scripts/Map/MapTeleportToPilotButton.cs` | Modify line 66 | Read `UILayer.Modal` (intentionally overlays pause when map is open). |
| `Assets/3 - Scripts/Ship/FlightAssistStatusHUD.cs` | Modify line 76 | Read `UILayer.Modal` (currently 1800 — overkill). |
| `Assets/3 - Scripts/SaveSystem/SaveLoadUI.cs` | Modify line 42 | Read `UILayer.SaveDialog`. |
| `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` | Modify lines 951-984 (`ApplyHeldItem`) | Register held physics object with EndlessManager + PickupUIManager. |
| `Assets/3 - Scripts/Pickups/PickupUIManager.cs` | Modify | Auto-create singleton via `RuntimeInitializeOnLoadMethod`; build marker prefab procedurally. |
| `Assets/3 - Scripts/UI/MainMenuController.cs` | Modify lines 473-606 (`EnsureGameplaySingletons`) | Seed `PickupUIManager`. |
| `Assets/3 - Scripts/Pickups/WaterBottleController.cs` | Modify lines 58-61, 79-82, 210-215 | Add axe + pistol mutual-exclusion. |
| `Assets/3 - Scripts/Pickups/AxeController.cs` | Modify lines 75-79, 86-90, 120-126 | Add pistol mutual-exclusion. |
| `Assets/3 - Scripts/NPC_Dialogue/GuitarController.cs` | Modify lines 32-34, 53-55, 89-94 | Add axe + pistol mutual-exclusion. |
| `Assets/3 - Scripts/Fishing/FishingRodController.cs` | Modify ~line 78 (fields), ~line 121 (Start), lines 342-348 (EquipRod) | Add axe + pistol mutual-exclusion. |
| `Assets/3 - Scripts/NPC_Dialogue/BonfireUIRegistry.cs` | Create | Static class holding `CookPanel`/`PromptText` refs. |
| `Assets/3 - Scripts/NPC_Dialogue/BonfireInteraction.cs` | Modify Start (line 71) | Self-register the scene bonfire's cookPanel/promptText with BonfireUIRegistry. |
| `Assets/3 - Scripts/Building/GhostPlacement.cs` | Modify lines 484-494 | Prefer `BonfireUIRegistry`; fall back to `FindSceneBonfireTemplate`. |
| `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` | Modify lines 881-896 (`ApplyBuildings` bonfire block) | Prefer `BonfireUIRegistry`; fall back to `FindAnotherBonfire`. |

---

### Task 1: Create UILayer constants class

**Files:**
- Create: `Assets/3 - Scripts/UI/UILayer.cs`

- [ ] **Step 1: Document expected behavior**

UILayer is a static class with `public const int` fields defining canvas
sortingOrders so the relationships between HUD layers are visible in one
place. Touching one constant moves every canvas using it; the relationships
in the file's comment header are the contract. No runtime behavior on its
own — purely a constants holder.

- [ ] **Step 2: Create the file**

```csharp
using UnityEngine;

/// <summary>
/// Single source of truth for canvas sortingOrder values. The order is a
/// contract: lower draws under higher. Centralised here so the relationships
/// are obvious and one tweak cascades everywhere.
///
/// Layer order (low → high):
///   Background      = 0     — main menu background, default canvases
///   HudBackground   = 22    — water-fill HUD, behind hotbar
///   Hotbar          = 200   — build menu, fishingdex, hotbar slot grid
///   Hud             = 830   — primary HUDs (vitals, wallet, tutorial)
///   Map             = 970   — map view legend + orbit lines
///   Pause           = 1000  — pause menu (above all HUDs)
///   Modal           = 1100  — toasts that overlay the pause menu or map
///                              (FlightAssist, teleport-to-pilot button)
///   SaveDialog      = 2000  — save/load picker (above pause menu)
///   ControllerBorder= 32000 — controller-nav border (absolute top)
///
/// Use a value in this class rather than typing a magic number. If you need
/// a new layer, add it here (and document the reason in the comment).
/// </summary>
public static class UILayer
{
    public const int Background       = 0;
    public const int HudBackground    = 22;
    public const int Hotbar           = 200;
    public const int Hud              = 830;
    public const int Toast            = 900;
    public const int Vendor           = 950;
    public const int Map              = 970;
    public const int Pause            = 1000;
    public const int Modal            = 1100;
    public const int SaveDialog       = 2000;
    public const int ControllerBorder = 32000;
}
```

- [ ] **Step 3: Verify Unity Console is compile-clean**

In Unity, save the file. Console must show 0 errors and 0 warnings related
to `UILayer`. No runtime change yet — nothing is consuming the constants.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/UI/UILayer.cs"
git commit -m "feat(ui): add UILayer constants for canvas sortingOrder

Centralises the magic numbers spread across ~15 HUD/UI files into one
file with documented layer relationships. Phase 1 consumers will migrate
next; existing magic numbers stay until each consumer is migrated.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Migrate sortingOrder consumers to UILayer

**Files:**
- Modify: `Assets/3 - Scripts/UI/TabbedPauseMenu.cs:237`
- Modify: `Assets/3 - Scripts/SaveSystem/AutosaveManager.cs:121`
- Modify: `Assets/3 - Scripts/UI/StoryImpactNotice.cs:41`
- Modify: `Assets/3 - Scripts/Vendor/GoodsVendorShopUI.cs:130`
- Modify: `Assets/3 - Scripts/Vendor/ShipMarketShopUI.cs:125`
- Modify: `Assets/3 - Scripts/Map/MapBootstrapReal.cs:66`
- Modify: `Assets/3 - Scripts/Map/MapTeleportToPilotButton.cs:66`
- Modify: `Assets/3 - Scripts/Ship/FlightAssistStatusHUD.cs:76`
- Modify: `Assets/3 - Scripts/SaveSystem/SaveLoadUI.cs:42`

- [ ] **Step 1: Document expected behavior**

After this task: pause menu paints over all HUDs and toasts (Toast/Vendor/Map
< Pause). FlightAssist + teleport-to-pilot buttons can intentionally overlay
the pause menu and map (Modal > Pause). Save dialog is always on top
(SaveDialog > Modal). No literal sortingOrder numbers in these 9 files.

- [ ] **Step 2: Migrate `TabbedPauseMenu.cs` line 237**

Replace:
```csharp
        _canvas.sortingOrder = 1000; // above every HUD canvas
```

With:
```csharp
        _canvas.sortingOrder = UILayer.Pause; // above HUDs, vendor, toasts
```

- [ ] **Step 3: Migrate `AutosaveManager.cs` line 121**

Replace:
```csharp
        toastCanvas.sortingOrder = 1500; // above HUD, below pause menu (2000)
```

With:
```csharp
        toastCanvas.sortingOrder = UILayer.Toast; // below pause menu
```

- [ ] **Step 4: Migrate `StoryImpactNotice.cs` line 41**

Replace:
```csharp
        _canvas.sortingOrder = 1500;
```

With:
```csharp
        _canvas.sortingOrder = UILayer.Toast;
```

- [ ] **Step 5: Migrate `GoodsVendorShopUI.cs` line 130**

Replace:
```csharp
        _canvas.sortingOrder = 1500;
```

With:
```csharp
        _canvas.sortingOrder = UILayer.Vendor;
```

- [ ] **Step 6: Migrate `ShipMarketShopUI.cs` line 125**

Replace:
```csharp
        _canvas.sortingOrder = 1500;
```

With:
```csharp
        _canvas.sortingOrder = UILayer.Vendor;
```

- [ ] **Step 7: Migrate `MapBootstrapReal.cs` line 66**

Replace:
```csharp
        canvas.sortingOrder = 1000; // above HUD canvases
```

With:
```csharp
        canvas.sortingOrder = UILayer.Map; // above HUD, below pause
```

- [ ] **Step 8: Migrate `MapTeleportToPilotButton.cs` line 66**

Replace:
```csharp
        _canvas.sortingOrder = 1700; // above legend (1000) and orbit FX, below pause menu (1000+)
```

With:
```csharp
        _canvas.sortingOrder = UILayer.Modal; // overlays map + pause when map is open
```

- [ ] **Step 9: Migrate `FlightAssistStatusHUD.cs` line 76**

Replace:
```csharp
        _canvas.sortingOrder = 1800;
```

With:
```csharp
        _canvas.sortingOrder = UILayer.Modal;
```

- [ ] **Step 10: Migrate `SaveLoadUI.cs` line 42**

Replace:
```csharp
        ownCanvas.sortingOrder = 2000;  // above pause menu (1000), tutorial UI (500), etc.
```

With:
```csharp
        ownCanvas.sortingOrder = UILayer.SaveDialog;
```

- [ ] **Step 11: Verify compile-clean**

Save in Unity. Console must show 0 errors. The Toast/Vendor/Map/Modal
canvases will render in their new visual order — sanity-check in Task 14.

- [ ] **Step 12: Commit**

```bash
git add "Assets/3 - Scripts/UI/TabbedPauseMenu.cs" "Assets/3 - Scripts/SaveSystem/AutosaveManager.cs" "Assets/3 - Scripts/UI/StoryImpactNotice.cs" "Assets/3 - Scripts/Vendor/GoodsVendorShopUI.cs" "Assets/3 - Scripts/Vendor/ShipMarketShopUI.cs" "Assets/3 - Scripts/Map/MapBootstrapReal.cs" "Assets/3 - Scripts/Map/MapTeleportToPilotButton.cs" "Assets/3 - Scripts/Ship/FlightAssistStatusHUD.cs" "Assets/3 - Scripts/SaveSystem/SaveLoadUI.cs"
git commit -m "fix(ui): migrate canvas sortingOrder to UILayer constants

Toast/Vendor/Map sit BELOW the pause menu (1000) — previously they were
at 1500-1800 and painted over it, stealing raycasts. FlightAssist and
teleport-to-pilot stay above the map via UILayer.Modal (1100).

Audit ref: HUD-2 / HUD-11.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Register held items with EndlessManager + PickupUIManager

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs:951-984`

- [ ] **Step 1: Document expected behavior**

When a save is loaded with the player carrying a thruster pickup, the spawned
held instance must be registered with `EndlessManager` so floating-origin
shifts move it with the world. If the prefab has a `PickupMarker` component,
also register with `PickupUIManager`. Mirrors what `ApplyLooseParts` already
does for free-standing pickups. Without this, dropping a loaded-save-held
thruster desyncs from world on the next floating-origin shift.

- [ ] **Step 2: Modify `ApplyHeldItem` to add registration**

In `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`, replace lines 951-984
(the entire `ApplyHeldItem` method) with:

```csharp
    static void ApplyHeldItem(string heldKind)
    {
        if (string.IsNullOrEmpty(heldKind)) return;
        var pickup = Object.FindObjectOfType<PlayerPickup>();
        if (pickup == null || pickup.holdPosition == null) return;

        GameObject prefab = null;
        if (heldKind == "cassette")
        {
            var cp = Object.FindObjectOfType<CassettePlayer>();
            if (cp != null) prefab = cp.cassettePickupPrefab;
        }
        else if (heldKind.StartsWith("thruster"))
        {
            var dmg = Object.FindObjectOfType<ShipDamageManager>();
            var detach = Object.FindObjectOfType<ThrusterDetachOnImpact>();
            prefab = ResolvePartPrefab(heldKind, dmg, detach);
        }
        if (prefab == null) return;

        var go = Object.Instantiate(prefab, pickup.holdPosition.position, pickup.holdPosition.rotation);
        if (heldKind.StartsWith("thruster"))
        {
            var tp = go.GetComponent<ThrusterPickup>();
            if (tp != null) tp.thrusterType = heldKind.Substring("thruster".Length);
        }

        var rb = go.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        var col = go.GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // Mirror ApplyLooseParts (lines 943-947): held items are physics
        // objects parented under the player, but the moment they're dropped
        // they detach and need EndlessManager to track them across floating-
        // origin shifts. PickupMarker (if present) needs to be visible on
        // the marker HUD again after a drop.
        if (EndlessManager.Instance != null)
            EndlessManager.Instance.RegisterPhysicsObject(go.transform);

        var marker = go.GetComponent<PickupMarker>();
        if (marker != null && PickupUIManager.Instance != null)
            PickupUIManager.Instance.RegisterPickup(marker);

        pickup.ForcePickup(go);
    }
```

- [ ] **Step 3: Verify `EndlessManager.Instance` exists**

```bash
grep -n "public static EndlessManager Instance" "Assets/3 - Scripts/Physics/EndlessManager.cs"
```

Expected: line printed. If `EndlessManager` doesn't expose `.Instance`,
the existing `ApplyLooseParts` line 902 (`Object.FindObjectOfType<EndlessManager>()`)
is the precedent — fall back to that pattern: cache `var em = Object.FindObjectOfType<EndlessManager>();`
at the top and use `if (em != null) em.RegisterPhysicsObject(...)`.

If the grep failed (no `Instance` property), use this alternative for Step 2:

Replace `if (EndlessManager.Instance != null) EndlessManager.Instance.RegisterPhysicsObject(go.transform);`
with:
```csharp
        var em = Object.FindObjectOfType<EndlessManager>();
        if (em != null) em.RegisterPhysicsObject(go.transform);
```

- [ ] **Step 4: Verify compile-clean**

Save in Unity. Console must show 0 errors.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/SaveSystem/SaveCollector.cs"
git commit -m "fix(save): register held items with EndlessManager + PickupUIManager

ApplyHeldItem instantiated a thruster/cassette and parented it to the
player but never registered it. On drop, the freshly-held part would
desync from world on the next floating-origin shift. Marker would also
not reappear after drop. Mirrors ApplyLooseParts.

Audit ref: Save-3.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Convert PickupUIManager to auto-create singleton with procedural marker prefab

**Files:**
- Modify: `Assets/3 - Scripts/Pickups/PickupUIManager.cs`

- [ ] **Step 1: Document expected behavior**

PickupUIManager currently depends on Inspector-wired `markerPrefab`,
`markerContainer`, and `playerCamera` refs. If the scene's instance is
destroyed (return-to-menu) or never existed (load directly into gameplay),
the singleton is null and `RegisterPickup` calls silently do nothing.
After this task: `RuntimeInitializeOnLoadMethod` creates the singleton on
demand; the canvas + marker prefab are built procedurally; `playerCamera`
is resolved lazily from `Camera.main` with retry-throttling.

- [ ] **Step 2: Rewrite the file**

Replace the entire contents of `Assets/3 - Scripts/Pickups/PickupUIManager.cs`
with:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PickupUIManager : MonoBehaviour
{
    public static PickupUIManager Instance { get; private set; }

    // Inspector wiring is retained as an OPTIONAL override path. New scenes
    // get the auto-created singleton; older scenes that wire these in the
    // inspector continue to work without re-saving them.
    [Header("Marker Prefab (auto-built if null)")]
    public GameObject markerPrefab;
    public Transform markerContainer;

    [Header("Player Camera (auto-resolved if null)")]
    public Transform playerCamera;

    Camera _cameraComponent;
    float _nextCameraFindAttempt;
    const float CameraFindRetryInterval = 0.5f;

    private List<PickupMarkerData> activeMarkers = new List<PickupMarkerData>();

    private class PickupMarkerData
    {
        public PickupMarker pickup;
        public GameObject uiInstance;
        public RectTransform uiRect;
        public Image iconImage;
        public TextMeshProUGUI distanceText;
        public CanvasGroup canvasGroup;
        public string namePrefix;
        public int lastDistanceTenths;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        // Skip in MainMenu scene; the gameplay scene transition seeds it via
        // MainMenuController.EnsureGameplaySingletons. (Pattern matches the
        // other HUDs.)
        var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (active.name == "MainMenu") return;
        if (Instance != null) return;
        var go = new GameObject("PickupUIManager");
        DontDestroyOnLoad(go);
        go.AddComponent<PickupUIManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        EnsureCanvasAndPrefab();
    }

    void EnsureCanvasAndPrefab()
    {
        if (markerContainer == null)
        {
            var canvasGo = new GameObject("PickupMarkerCanvas");
            DontDestroyOnLoad(canvasGo);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UILayer.Hud;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            HUDSceneGate.Register(canvas);
            markerContainer = canvasGo.transform;
        }

        if (markerPrefab == null)
        {
            // Procedural marker: a transparent container with an Image (icon
            // slot — left untouched if no customIcon) plus a TMP text below
            // it. Same shape the legacy inspector-wired prefab had.
            markerPrefab = new GameObject("PickupMarker");
            markerPrefab.hideFlags = HideFlags.HideAndDontSave;
            var rt = markerPrefab.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(80, 100);
            markerPrefab.AddComponent<CanvasGroup>();

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(markerPrefab.transform, false);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.sizeDelta = new Vector2(40, 40);
            iconRt.anchoredPosition = new Vector2(0, 20);
            iconGo.AddComponent<Image>();

            var textGo = new GameObject("Distance", typeof(RectTransform));
            textGo.transform.SetParent(markerPrefab.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.sizeDelta = new Vector2(120, 40);
            textRt.anchoredPosition = new Vector2(0, -20);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 18;
            HudFontResolver.Apply(tmp);
        }
    }

    Camera ResolveCamera()
    {
        if (_cameraComponent != null) return _cameraComponent;
        if (playerCamera != null)
        {
            _cameraComponent = playerCamera.GetComponent<Camera>();
            if (_cameraComponent != null) return _cameraComponent;
        }
        if (Time.unscaledTime < _nextCameraFindAttempt) return null;
        _nextCameraFindAttempt = Time.unscaledTime + CameraFindRetryInterval;
        var cam = Camera.main;
        if (cam != null)
        {
            playerCamera = cam.transform;
            _cameraComponent = cam;
        }
        return _cameraComponent;
    }

    public void RegisterPickup(PickupMarker pickup)
    {
        if (pickup == null) return;
        EnsureCanvasAndPrefab();
        foreach (var data in activeMarkers)
            if (data.pickup == pickup) return;

        GameObject ui = Instantiate(markerPrefab, markerContainer);
        ui.hideFlags = HideFlags.None;
        ui.SetActive(true);
        PickupMarkerData newData = new PickupMarkerData
        {
            pickup = pickup,
            uiInstance = ui,
            uiRect = ui.GetComponent<RectTransform>(),
            iconImage = ui.GetComponentInChildren<Image>(),
            distanceText = ui.GetComponentInChildren<TextMeshProUGUI>(),
            canvasGroup = ui.GetComponent<CanvasGroup>(),
            namePrefix = string.IsNullOrEmpty(pickup.displayName) ? "" : pickup.displayName + "\n",
            lastDistanceTenths = int.MinValue,
        };

        if (pickup.customIcon != null && newData.iconImage != null)
            newData.iconImage.sprite = pickup.customIcon;

        activeMarkers.Add(newData);
    }

    public void UnregisterPickup(PickupMarker pickup)
    {
        for (int i = activeMarkers.Count - 1; i >= 0; i--)
        {
            if (activeMarkers[i].pickup == pickup)
            {
                Destroy(activeMarkers[i].uiInstance);
                activeMarkers.RemoveAt(i);
                break;
            }
        }
    }

    void LateUpdate()
    {
        var cam = ResolveCamera();
        if (cam == null || playerCamera == null) return;

        for (int i = activeMarkers.Count - 1; i >= 0; i--)
        {
            var data = activeMarkers[i];
            if (data.pickup == null || data.pickup.gameObject == null)
            {
                Destroy(data.uiInstance);
                activeMarkers.RemoveAt(i);
                continue;
            }
            UpdateMarker(data, cam);
        }
    }

    void UpdateMarker(PickupMarkerData data, Camera cam)
    {
        PickupMarker pickup = data.pickup;
        Transform target = pickup.transform;

        Vector3 worldPos = target.position + target.TransformDirection(pickup.worldOffset);
        Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
        bool isBehind = screenPos.z < 0;

        float distance = Vector3.Distance(playerCamera.position, target.position);
        bool shouldHide = distance <= pickup.hideDistance || isBehind;

        if (data.canvasGroup != null)
            data.canvasGroup.alpha = shouldHide ? 0f : 1f;

        if (shouldHide) return;

        if (data.uiRect != null) data.uiRect.position = screenPos;

        if (data.distanceText != null)
        {
            int tenths = Mathf.RoundToInt(distance * 10f);
            if (tenths != data.lastDistanceTenths)
            {
                data.lastDistanceTenths = tenths;
                data.distanceText.text = data.namePrefix + (tenths * 0.1f).ToString("F1") + "m";
            }
        }
    }
}
```

- [ ] **Step 3: Verify compile-clean**

Save in Unity. Console must show 0 errors. If a scene reference to
`PickupUIManager` exists, Unity may warn about the serialized fields — those
warnings are expected and harmless (the new auto-create path doesn't need
the fields).

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/Pickups/PickupUIManager.cs"
git commit -m "fix(pickup-ui): auto-create singleton with procedural marker prefab

PickupUIManager previously required Inspector-wired markerPrefab,
markerContainer, and playerCamera — if the scene's instance died or
load order skipped it, RegisterPickup silently no-op'd. Now creates
itself via RuntimeInitializeOnLoadMethod, builds its canvas + marker
prefab procedurally (font via HudFontResolver), and lazy-resolves
Camera.main with retry throttle. Inspector wiring is still honoured
as override.

Likely root cause of stale-state HUD bugs in recent save-load commits.

Audit ref: HUD-10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Seed PickupUIManager in EnsureGameplaySingletons

**Files:**
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs:600-606`

- [ ] **Step 1: Document expected behavior**

`EnsureGameplaySingletons` is called when the user clicks Play/Load in the
main menu, just before the gameplay scene loads. Adding PickupUIManager to
this list guarantees the singleton exists during `SaveCollector.Apply`
(which now calls `PickupUIManager.Instance.RegisterPickup` in `ApplyHeldItem`
and `ApplyLooseParts`). Without this seed, the `AutoCreate` from Task 4 will
still fire after scene load — but seeding here makes the timing reliable
during the menu→play transition.

- [ ] **Step 2: Add the seed block**

In `Assets/3 - Scripts/UI/MainMenuController.cs`, find the block ending
at line 605 (just before the closing brace of `EnsureGameplaySingletons`).

Replace:
```csharp
        if (KillstreakHUD.Instance == null)
        {
            var go = new GameObject("KillstreakHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<KillstreakHUD>();
        }
    }
```

With:
```csharp
        if (KillstreakHUD.Instance == null)
        {
            var go = new GameObject("KillstreakHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<KillstreakHUD>();
        }
        if (PickupUIManager.Instance == null)
        {
            // Save-load round-trip calls PickupUIManager.Instance.RegisterPickup
            // during Apply; seed here so the singleton exists before the
            // gameplay scene starts processing.
            var go = new GameObject("PickupUIManager");
            DontDestroyOnLoad(go);
            go.AddComponent<PickupUIManager>();
        }
    }
```

- [ ] **Step 3: Verify compile-clean**

Save in Unity. Console must show 0 errors.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/UI/MainMenuController.cs"
git commit -m "fix(main-menu): seed PickupUIManager in EnsureGameplaySingletons

Pairs with Task 4 of the cleanup pass: AutoCreate handles the AfterSceneLoad
case, this seeds for the menu→play transition so the singleton exists
during SaveCollector.Apply.

Audit ref: HUD-10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Pistol + axe mutual-exclusion in WaterBottleController

**Files:**
- Modify: `Assets/3 - Scripts/Pickups/WaterBottleController.cs:58-61, 79-82, 210-215`

- [ ] **Step 1: Document expected behavior**

`WaterBottleController.Equip()` currently refuses if the rod, guitar, or
`PlayerPickup.IsHoldingObject` is active — but not if axe or pistol is
equipped. So `ForceEquipBottle()` (called from save-apply or any non-Hotbar
path) spawns a bottle alongside an active pistol/axe. After this fix:
all 4 sibling controllers are checked.

- [ ] **Step 2: Add sibling-ref fields**

Replace the field block at lines 58-61:

```csharp
    // ── references ────────────────────────────────────────────────
    FishingRodController fishingRodController;
    GuitarController     guitarController;
    PlayerPickup         playerPickup;
    Ship                 ship;
```

With:

```csharp
    // ── references ────────────────────────────────────────────────
    FishingRodController fishingRodController;
    GuitarController     guitarController;
    AxeController        axeController;
    PistolController     pistolController;
    PlayerPickup         playerPickup;
    Ship                 ship;
```

- [ ] **Step 3: Cache the new sibling refs in Start**

Replace lines 79-82:

```csharp
        fishingRodController = GetComponent<FishingRodController>();
        guitarController     = GetComponent<GuitarController>();
        playerPickup         = GetComponent<PlayerPickup>();
        ship                 = FindObjectOfType<Ship>();
```

With:

```csharp
        fishingRodController = GetComponent<FishingRodController>();
        guitarController     = GetComponent<GuitarController>();
        axeController        = GetComponent<AxeController>();
        pistolController     = GetComponent<PistolController>();
        playerPickup         = GetComponent<PlayerPickup>();
        ship                 = FindObjectOfType<Ship>();
```

- [ ] **Step 4: Add mutual-exclusion checks in Equip**

Replace lines 210-215:

```csharp
    void Equip()
    {
        if (fishingRodController != null && fishingRodController.IsEquipped) return;
        if (guitarController     != null && guitarController.IsEquipped)     return;
        if (playerPickup         != null && playerPickup.IsHoldingObject)    return;
        if (waterBottlePrefab    == null)                                    return;
```

With:

```csharp
    void Equip()
    {
        if (fishingRodController != null && fishingRodController.IsEquipped) return;
        if (guitarController     != null && guitarController.IsEquipped)     return;
        if (axeController        != null && axeController.IsEquipped)        return;
        if (pistolController     != null && pistolController.IsEquipped)     return;
        if (playerPickup         != null && playerPickup.IsHoldingObject)    return;
        if (waterBottlePrefab    == null)                                    return;
```

- [ ] **Step 5: Verify compile-clean**

Save in Unity. Console must show 0 errors.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/Pickups/WaterBottleController.cs"
git commit -m "fix(water-bottle): mutual-exclusion vs axe + pistol

The bottle is the oldest controller and predates both. ForceEquipBottle
from save-apply could spawn alongside an active pistol/axe; the hotbar
path was the only safety net.

Audit ref: Equip-1 / Equip-3.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: Pistol mutual-exclusion in AxeController

**Files:**
- Modify: `Assets/3 - Scripts/Pickups/AxeController.cs:75-79, 86-90, 120-126`

- [ ] **Step 1: Document expected behavior**

`AxeController.EquipAxe()` already checks rod/water/guitar/pickup but not
pistol. After this fix: pistol is checked too.

- [ ] **Step 2: Add the pistol field**

Replace lines 75-79:

```csharp
    FishingRodController _fishingRodController;
    GuitarController _guitarController;
    WaterBottleController _waterBottleController;
    PlayerPickup _playerPickup;
    Ship _ship;
```

With:

```csharp
    FishingRodController _fishingRodController;
    GuitarController _guitarController;
    WaterBottleController _waterBottleController;
    PistolController _pistolController;
    PlayerPickup _playerPickup;
    Ship _ship;
```

- [ ] **Step 3: Cache it in Start**

Replace lines 86-90:

```csharp
        _fishingRodController  = GetComponent<FishingRodController>();
        _guitarController      = GetComponent<GuitarController>();
        _waterBottleController = GetComponent<WaterBottleController>();
        _playerPickup          = GetComponent<PlayerPickup>();
        _ship                  = FindObjectOfType<Ship>();
```

With:

```csharp
        _fishingRodController  = GetComponent<FishingRodController>();
        _guitarController      = GetComponent<GuitarController>();
        _waterBottleController = GetComponent<WaterBottleController>();
        _pistolController      = GetComponent<PistolController>();
        _playerPickup          = GetComponent<PlayerPickup>();
        _ship                  = FindObjectOfType<Ship>();
```

- [ ] **Step 4: Add the mutual-exclusion check in EquipAxe**

Replace lines 120-126:

```csharp
    void EquipAxe()
    {
        if (axePrefab == null || axeHoldPosition == null) return;
        if (_fishingRodController  != null && _fishingRodController.IsEquipped) return;
        if (_waterBottleController != null && _waterBottleController.IsEquipped) return;
        if (_guitarController      != null && _guitarController.IsEquipped) return;
        if (_playerPickup          != null && _playerPickup.IsHoldingObject) return;
```

With:

```csharp
    void EquipAxe()
    {
        if (axePrefab == null || axeHoldPosition == null) return;
        if (_fishingRodController  != null && _fishingRodController.IsEquipped) return;
        if (_waterBottleController != null && _waterBottleController.IsEquipped) return;
        if (_guitarController      != null && _guitarController.IsEquipped) return;
        if (_pistolController      != null && _pistolController.IsEquipped) return;
        if (_playerPickup          != null && _playerPickup.IsHoldingObject) return;
```

- [ ] **Step 5: Verify compile-clean**

Save in Unity. Console must show 0 errors.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/Pickups/AxeController.cs"
git commit -m "fix(axe): mutual-exclusion vs pistol

Pistol was added later and only PistolController checked AxeController.
ForceEquipAxe from save-apply or NPC handoff could spawn alongside an
active pistol.

Audit ref: Equip-1.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: Pistol + axe mutual-exclusion in GuitarController

**Files:**
- Modify: `Assets/3 - Scripts/NPC_Dialogue/GuitarController.cs:32-34, 53-55, 89-94`

- [ ] **Step 1: Document expected behavior**

`GuitarController.EquipGuitar()` currently checks rod/water/pickup but
not axe or pistol. After this fix: all sibling controllers are checked.

- [ ] **Step 2: Add the two new fields**

Replace lines 32-34:

```csharp
    private FishingRodController _fishingRodController;
    private PlayerPickup _playerPickup;
    private WaterBottleController _waterBottleController;
```

With:

```csharp
    private FishingRodController _fishingRodController;
    private PlayerPickup _playerPickup;
    private WaterBottleController _waterBottleController;
    private AxeController _axeController;
    private PistolController _pistolController;
```

- [ ] **Step 3: Cache them in Start**

Replace lines 53-55:

```csharp
        _fishingRodController  = GetComponent<FishingRodController>();
        _playerPickup          = GetComponent<PlayerPickup>();
        _waterBottleController = GetComponent<WaterBottleController>();
```

With:

```csharp
        _fishingRodController  = GetComponent<FishingRodController>();
        _playerPickup          = GetComponent<PlayerPickup>();
        _waterBottleController = GetComponent<WaterBottleController>();
        _axeController         = GetComponent<AxeController>();
        _pistolController      = GetComponent<PistolController>();
```

- [ ] **Step 4: Add the mutual-exclusion checks in EquipGuitar**

Replace lines 89-94:

```csharp
    void EquipGuitar()
    {
        if (guitarPrefab == null || guitarHoldPosition == null) return;
        if (_fishingRodController  != null && _fishingRodController.IsEquipped)      return;
        if (_waterBottleController != null && _waterBottleController.IsEquipped)    return;
        if (_playerPickup          != null && _playerPickup.IsHoldingObject)        return;
```

With:

```csharp
    void EquipGuitar()
    {
        if (guitarPrefab == null || guitarHoldPosition == null) return;
        if (_fishingRodController  != null && _fishingRodController.IsEquipped)      return;
        if (_waterBottleController != null && _waterBottleController.IsEquipped)    return;
        if (_axeController         != null && _axeController.IsEquipped)            return;
        if (_pistolController      != null && _pistolController.IsEquipped)         return;
        if (_playerPickup          != null && _playerPickup.IsHoldingObject)        return;
```

- [ ] **Step 5: Verify compile-clean**

Save in Unity. Console must show 0 errors.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/NPC_Dialogue/GuitarController.cs"
git commit -m "fix(guitar): mutual-exclusion vs axe + pistol

ForceEquipGuitar from any non-Hotbar path could spawn alongside active
axe or pistol — neither was checked. Brings the guitar into line with
PistolController's full 5-way exclusion.

Audit ref: Equip-1.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 9: Pistol + axe mutual-exclusion in FishingRodController

**Files:**
- Modify: `Assets/3 - Scripts/Fishing/FishingRodController.cs` near line 78 (fields), line 121 (Start), lines 342-348 (EquipRod)

- [ ] **Step 1: Document expected behavior**

`FishingRodController.EquipRod()` currently checks guitar/water/pickup but
not axe or pistol. After this fix: all sibling controllers are checked.

- [ ] **Step 2: Locate the existing sibling-ref fields**

Run:
```bash
grep -n "private.*Controller\|public.*Controller\|GuitarController guitarController" "Assets/3 - Scripts/Fishing/FishingRodController.cs" | head -20
```

Expected: a line declaring `GuitarController guitarController;` (and similar
for `WaterBottleController waterBottleController` and `PlayerPickup playerPickup`).
Note the exact line number where these are declared — the rod's controller
naming convention uses no underscore prefix.

- [ ] **Step 3: Add the two new fields next to the existing ones**

After the line declaring `GuitarController guitarController;` (or wherever
the sibling refs live), add:

```csharp
    AxeController         axeController;
    PistolController      pistolController;
```

- [ ] **Step 4: Cache them in Start**

In `Assets/3 - Scripts/Fishing/FishingRodController.cs`, find the existing
`Start()` block (around line 113) that contains
`guitarController = FindObjectOfType<GuitarController>();` and the
GetComponent calls. After the `waterBottleController = GetComponent<WaterBottleController>();`
line (around line 123), add:

```csharp
        axeController         = GetComponent<AxeController>();
        pistolController      = GetComponent<PistolController>();
```

- [ ] **Step 5: Add the mutual-exclusion checks in EquipRod**

Replace lines 342-348:

```csharp
    void EquipRod()
    {
        if (fishingRodPrefab == null) return;
        if (guitarController      != null && guitarController.IsEquipped)      return;
        if (waterBottleController != null && waterBottleController.IsEquipped) return;
        if (playerPickup          != null && playerPickup.IsHoldingObject)     return;
```

With:

```csharp
    void EquipRod()
    {
        if (fishingRodPrefab == null) return;
        if (guitarController      != null && guitarController.IsEquipped)      return;
        if (waterBottleController != null && waterBottleController.IsEquipped) return;
        if (axeController         != null && axeController.IsEquipped)         return;
        if (pistolController      != null && pistolController.IsEquipped)      return;
        if (playerPickup          != null && playerPickup.IsHoldingObject)     return;
```

- [ ] **Step 6: Verify compile-clean**

Save in Unity. Console must show 0 errors.

- [ ] **Step 7: Commit**

```bash
git add "Assets/3 - Scripts/Fishing/FishingRodController.cs"
git commit -m "fix(rod): mutual-exclusion vs axe + pistol

ForceEquipRod (called by NPCDialogue post-trade and save-apply) could
spawn alongside active axe or pistol.

Audit ref: Equip-1.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 10: Create BonfireUIRegistry static class

**Files:**
- Create: `Assets/3 - Scripts/NPC_Dialogue/BonfireUIRegistry.cs`

- [ ] **Step 1: Document expected behavior**

A static registry holds the scene's bonfire cook-panel + prompt-text refs.
The scene's "source" bonfire registers them in `Start()`. Placed bonfires
(both via build menu and via save restore) read from the registry instead
of scanning the scene for a template. The static survives even if the
source bonfire's GameObject is destroyed — the Canvas / TMP refs live on
the HUD Canvas, which is a separate scene object.

- [ ] **Step 2: Create the file**

```csharp
using UnityEngine;
using TMPro;

/// <summary>
/// Shared registry for the scene's bonfire cook-panel + prompt-text refs.
/// The "source" bonfire in the gameplay scene populates these on Start;
/// runtime-placed bonfires (build menu and save-load round-trip) read
/// from here instead of scanning the scene for another bonfire to copy.
///
/// The static survives source-bonfire destruction because the refs point
/// to the HUD Canvas, which lives independently in the scene.
///
/// On scene/domain reload, the static is cleared so a stale reference
/// from a prior play session can't leak into a fresh scene.
/// </summary>
public static class BonfireUIRegistry
{
    public static GameObject CookPanel;
    public static TextMeshProUGUI PromptText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        CookPanel = null;
        PromptText = null;
    }
}
```

- [ ] **Step 3: Verify compile-clean**

Save in Unity. Console must show 0 errors.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/NPC_Dialogue/BonfireUIRegistry.cs"
git commit -m "feat(bonfire): add UI registry for cook-panel handoff

Replaces the fragile 'find another scene bonfire and copy its refs'
pattern with a single static. The next two tasks wire it in.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 11: BonfireInteraction self-registers with BonfireUIRegistry

**Files:**
- Modify: `Assets/3 - Scripts/NPC_Dialogue/BonfireInteraction.cs:71-88`

- [ ] **Step 1: Document expected behavior**

When a bonfire's `Start()` runs and finds it has a valid `cookPanel` (the
scene's source bonfire wired via Inspector — placed bonfires get their
`cookPanel` populated AFTER Start by GhostPlacement/SaveCollector), register
it with `BonfireUIRegistry`. First-write-wins so a single source bonfire
populates the registry once and subsequent placed bonfires don't overwrite.

- [ ] **Step 2: Modify the Start() method**

In `Assets/3 - Scripts/NPC_Dialogue/BonfireInteraction.cs`, replace lines
71-88:

```csharp
    void Start()
    {
        rodCtrl    = FindObjectOfType<FishingRodController>();
        guitarCtrl = FindObjectOfType<GuitarController>();
        bottleCtrl = FindObjectOfType<WaterBottleController>();

        if (cookPanel != null) { HideOldChildren(); BuildUI(); }
        if (cookPanel != null) cookPanel.SetActive(false);
        if (promptText != null) promptText.gameObject.SetActive(false);

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;

        fireSource = gameObject.AddComponent<AudioSource>();
        fireSource.playOnAwake = false;
        fireSource.loop = true;
        fireSource.volume = fireVolume;
    }
```

With:

```csharp
    void Start()
    {
        rodCtrl    = FindObjectOfType<FishingRodController>();
        guitarCtrl = FindObjectOfType<GuitarController>();
        bottleCtrl = FindObjectOfType<WaterBottleController>();

        // First scene bonfire with a valid cookPanel publishes its refs so
        // placed bonfires (build menu + save load) can read them without
        // re-scanning the scene for a template. First-write-wins so a placed
        // bonfire (which gets cookPanel populated by GhostPlacement AFTER
        // Start) can't overwrite the source.
        if (cookPanel != null && BonfireUIRegistry.CookPanel == null)
        {
            BonfireUIRegistry.CookPanel  = cookPanel;
            BonfireUIRegistry.PromptText = promptText;
        }

        if (cookPanel != null) { HideOldChildren(); BuildUI(); }
        if (cookPanel != null) cookPanel.SetActive(false);
        if (promptText != null) promptText.gameObject.SetActive(false);

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;

        fireSource = gameObject.AddComponent<AudioSource>();
        fireSource.playOnAwake = false;
        fireSource.loop = true;
        fireSource.volume = fireVolume;
    }
```

- [ ] **Step 3: Verify compile-clean**

Save in Unity. Console must show 0 errors.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/NPC_Dialogue/BonfireInteraction.cs"
git commit -m "feat(bonfire): scene bonfire registers cook-panel in registry

Pairs with BonfireUIRegistry. Source bonfire publishes its refs in
Start; the next two tasks wire placed bonfires to read from the
registry.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 12: GhostPlacement uses BonfireUIRegistry first, scene-template as fallback

**Files:**
- Modify: `Assets/3 - Scripts/Building/GhostPlacement.cs:476-494`

- [ ] **Step 1: Document expected behavior**

`GhostPlacement.Place` currently calls `FindSceneBonfireTemplate` to find
another bonfire with a valid cookPanel and copy its refs. After this task:
prefer `BonfireUIRegistry` (set in Task 11); fall back to the scan only if
the registry isn't populated yet. This survives source-bonfire destruction.

- [ ] **Step 2: Modify the bonfire wiring block**

In `Assets/3 - Scripts/Building/GhostPlacement.cs`, replace lines 476-494
(the `if (entry.addBonfireInteractionOnPlace)` block, up to and including
the `else { Debug.LogWarning... }` block — but stopping before the
"Ensure there's a trigger collider" comment on line 496):

```csharp
        if (entry.addBonfireInteractionOnPlace)
        {
            var bf = real.GetComponent<BonfireInteraction>();
            if (bf == null) bf = real.AddComponent<BonfireInteraction>();

            // Wire shared HUD references from the existing scene bonfire so the cook
            // panel and prompt actually appear (BonfireInteraction.Start calls BuildUI
            // only when cookPanel is non-null).
            var template = FindSceneBonfireTemplate(bf);
            if (template != null)
            {
                bf.cookPanel  = template.cookPanel;
                bf.promptText = template.promptText;
                Debug.Log($"[GhostPlacement] Placed '{real.name}' wired with cookPanel='{(bf.cookPanel != null ? bf.cookPanel.name : "<null>")}' promptText='{(bf.promptText != null ? bf.promptText.gameObject.name : "<null>")}' (template='{template.gameObject.name}')");
            }
            else
            {
                Debug.LogWarning("GhostPlacement: no existing BonfireInteraction found to copy cookPanel/promptText from. The placed bonfire won't be cookable until a scene bonfire exists.");
            }
```

With:

```csharp
        if (entry.addBonfireInteractionOnPlace)
        {
            var bf = real.GetComponent<BonfireInteraction>();
            if (bf == null) bf = real.AddComponent<BonfireInteraction>();

            // Prefer the registry (populated by the scene's source bonfire in
            // its Start) — survives source-bonfire destruction. Fall back to
            // scanning the scene for another live bonfire as a safety net.
            if (BonfireUIRegistry.CookPanel != null)
            {
                bf.cookPanel  = BonfireUIRegistry.CookPanel;
                bf.promptText = BonfireUIRegistry.PromptText;
            }
            else
            {
                var template = FindSceneBonfireTemplate(bf);
                if (template != null)
                {
                    bf.cookPanel  = template.cookPanel;
                    bf.promptText = template.promptText;
                }
                else
                {
                    Debug.LogWarning("GhostPlacement: no BonfireUIRegistry entry and no scene bonfire template — placed bonfire won't be cookable.");
                }
            }
```

- [ ] **Step 3: Verify compile-clean**

Save in Unity. Console must show 0 errors.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/Building/GhostPlacement.cs"
git commit -m "fix(building): use BonfireUIRegistry for placed-bonfire wiring

Survives destruction of the scene's source bonfire. Scene-scan template
remains as a safety net.

Audit ref: Build-2.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 13: SaveCollector.ApplyBuildings uses BonfireUIRegistry first

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs:881-896`

- [ ] **Step 1: Document expected behavior**

`SaveCollector.ApplyBuildings` does the same bonfire-template copy that
`GhostPlacement` did. After this task: prefer registry, fall back to
`FindAnotherBonfire`. Add a `Debug.LogWarning` for the silent-failure case
(the design's Phase 5 #6 — surface unknown-prefab issues).

- [ ] **Step 2: Modify the bonfire wiring block in ApplyBuildings**

In `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`, replace lines 881-896
(the `if (entry.addBonfireInteractionOnPlace)` block):

```csharp
            if (entry.addBonfireInteractionOnPlace)
            {
                var bf = go.GetComponent<BonfireInteraction>() ?? go.AddComponent<BonfireInteraction>();
                var template = FindAnotherBonfire(bf);
                if (template != null)
                {
                    bf.cookPanel = template.cookPanel;
                    bf.promptText = template.promptText;
                }
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var sc = go.AddComponent<SphereCollider>();
                    sc.isTrigger = true;
                    sc.radius = 2f;
                }
            }
```

With:

```csharp
            if (entry.addBonfireInteractionOnPlace)
            {
                var bf = go.GetComponent<BonfireInteraction>() ?? go.AddComponent<BonfireInteraction>();
                if (BonfireUIRegistry.CookPanel != null)
                {
                    bf.cookPanel  = BonfireUIRegistry.CookPanel;
                    bf.promptText = BonfireUIRegistry.PromptText;
                }
                else
                {
                    var template = FindAnotherBonfire(bf);
                    if (template != null)
                    {
                        bf.cookPanel = template.cookPanel;
                        bf.promptText = template.promptText;
                    }
                    else
                    {
                        Debug.LogWarning($"[SaveCollector] ApplyBuildings: placed bonfire '{go.name}' has no cookPanel — neither registry nor scene template available.");
                    }
                }
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var sc = go.AddComponent<SphereCollider>();
                    sc.isTrigger = true;
                    sc.radius = 2f;
                }
            }
```

- [ ] **Step 3: Verify compile-clean**

Save in Unity. Console must show 0 errors.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/SaveSystem/SaveCollector.cs"
git commit -m "fix(save): ApplyBuildings uses BonfireUIRegistry first

Mirrors GhostPlacement: registry preferred, scene-scan fallback,
warning when neither is available (no more silent data loss).

Audit ref: Build-2.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 14: User-driven Unity Editor verification gate

**Files:**
- None (manual verification)

- [ ] **Step 1: Inform the user the implementation is complete and list verification scenarios**

Tell the user: "Phase 1 complete. 13 tasks landed across 12 commits. Open
Unity, ensure the Console is clean on scene load, and run the following
scenarios. If any fails, stop — we bisect with `git log --oneline` since
the design phase commit (49213f1) and revert. Do NOT proceed to Phase 2-5
until all of these pass."

**Scenario A — Sorting orders correct**
1. Press Play. Watch the autosave toast appear (after first interaction).
2. Open the pause menu (Esc) while the toast is visible.
3. Expected: pause menu paints OVER the toast. Toast text/background is
   hidden behind the pause panel.
4. Click any pause menu button. Expected: button registers (raycast not
   stolen by toast).

**Scenario B — Held items survive save round-trip**
1. Detach a thruster (collide ship hard with a planet).
2. Pick up the loose thruster (F).
3. Save (pause menu → Save Game).
4. Quit to main menu, load that save.
5. Expected: player still holding the thruster. Drop it (F again).
6. Walk away ~100m to trigger a floating-origin shift.
7. Expected: the dropped thruster moves with the world; doesn't desync.

**Scenario C — PickupUIManager survives all entry points**
1. Quit to main menu. Click Load (your save with loose thrusters from B).
2. Expected: every loose thruster has a marker UI above it (with name +
   distance).
3. Open Console. Expected: no errors about null PickupUIManager.

**Scenario D — Mutual-exclusion between all 5 equippables**
1. Buy/grant pistol via Alien7 vendor.
2. Equip pistol (hotbar slot).
3. Try to drink water (right-click while bottle item available).
4. Expected: bottle does NOT spawn alongside the pistol.
5. Repeat: with pistol equipped, try equipping axe via hotbar — old item
   should put away before new one appears. With axe equipped, RMB to drink
   — bottle blocked.
6. With ANY non-pistol item equipped, swap to pistol — works.

**Scenario E — Bonfire registry survives source destruction**
1. Build a bonfire (N, select bonfire, place near the scene's original).
2. Confirm BOTH bonfires open the cook panel when stood next to.
3. Quit, edit `1.6.7.7.7.unity` mentally as "what if the source bonfire
   was deleted" — for this verification, build a SECOND bonfire instead
   so the original isn't the only source.
4. (Skip the hardcore version: actually destroying the scene's source
   bonfire is out of scope for verification — the registry just makes
   the future scenario survivable.)

**Pause-menu raycast spot-check**: with the autosave toast on-screen,
click each visible pause-menu button at least once. Watch for missed
clicks.

- [ ] **Step 2: Wait for user confirmation**

When the user replies green-light on all five scenarios, mark this task
done and request the Phase 2-5 plan as a follow-up.

---

## Self-Review Notes

(Done by the plan author after writing.)

- **Spec coverage**: Spec Phase 1 has 5 bugs (UILayer/sortingOrder, ApplyHeldItem
  registration, PickupUIManager singleton, cross-controller pistol awareness,
  BonfireUIRegistry). All 5 are implemented across Tasks 1-13. ✓
- **Placeholder scan**: No TBDs or vague steps. Each step has exact file
  paths, line numbers, and complete code blocks. ✓
- **Type consistency**: `UILayer.Pause`/`UILayer.Toast`/`UILayer.Vendor`/`UILayer.Map`/`UILayer.Modal`/`UILayer.SaveDialog`/`UILayer.Hud` used
  consistently across Task 1 declaration and Tasks 2 + 4 consumers. ✓
  `BonfireUIRegistry.CookPanel`/`BonfireUIRegistry.PromptText` consistent across
  Tasks 10/11/12/13. ✓ `PickupUIManager.Instance` (capital-I) consistent. ✓
- **Equippable naming**: WaterBottle uses no-underscore fields (`fishingRodController`),
  Axe/Pistol use `_` prefix, Guitar uses `_` prefix, Rod uses no-underscore.
  Task 9 explicitly accounts for the rod's no-underscore convention via a
  grep step before editing. ✓
