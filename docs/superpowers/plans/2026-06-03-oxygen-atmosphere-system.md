# Oxygen & Atmosphere System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a survival oxygen system with two seconds-of-air pools (suit 120 s + ship hull 300 s), altitude-gated breathing, an open-hatch depressurization hazard, a hatch suction-eject, edge-triggered HAL VO, a code-built HUD, save persistence, and a Cyclops autosave checkpoint.

**Architecture:** One DontDestroyOnLoad `OxygenManager` singleton runs all per-frame logic in `FixedUpdate`, reading altitude/planet identity from `NBodySimulation.Bodies` (`CelestialBody` gameplay accessors — never the forbidden atmosphere shaders) and ship/hatch/pilot state from the existing `Ship`. "Inside ship" is a tunable distance check against the ship's `camViewPoint` (no new trigger volume / prefab edits). A separate code-built `OxygenHUD` singleton (mirroring the existing `VitalsHUD`/`HALLineHUD` self-built-canvas precedent) draws the two meters. State persists via the existing `SaveData`/`SaveCollector`/`NewGameReset` recipe. Death routes through `ResourceManager.TakeDamage`.

**Tech Stack:** Unity 2022.3 Built-in RP, C# (Assembly-CSharp, no asmdefs), uGUI + TMP, JsonUtility save system, Coplay MCP (TTS + compile checks + play-mode verification). **No CLI build/test exists** — every task is verified by (a) `mcp__coplay-mcp__check_compile_errors` after the edit and (b) in-Editor play-mode checks for behavioral tasks.

---

## Conventions for this plan (read once)

- **No unit-test runner exists.** Where a normal plan says "write the failing test / run it," this plan substitutes **compile-verify** (`check_compile_errors` must return zero errors) and, for behavioral tasks, a **manual play-mode check** scripted against the acceptance checklist (final task). This is the honest verification path for this Editor-only project.
- **Trap #1 (MainMenu singletons):** any auto-singleton with a MainMenu early-return MUST also be seeded in `MainMenuController.EnsureGameplaySingletons` or it never auto-creates in builds. Both new singletons are seeded in **Task 4**.
- **Serialized fields appended at class END** only (reordering corrupts serialization). New `.cs` files each need their `.meta` `git add`-ed.
- **Forbidden zone untouched:** no edits to `Atmosphere.cs`, `Celestial/`, `Post Processing/Planet Effects/`, or planet shaders. Altitude is derived from `CelestialBody.Position`/`.radius`/`.bodyName` (gameplay accessors, explicitly allowed).
- **Branch first.** Do NOT use a git worktree — the Unity Editor is open on this exact folder and a worktree would point it elsewhere. Work in place on a feature branch. Commit after each task; do not push unless the user asks.

### Verified integration points (ground truth — do not re-discover)

| Symbol | Location | Signature |
|---|---|---|
| `NBodySimulation.Bodies` | static | `CelestialBody[]` — null-safe, `Array.Empty` off the solar scene |
| `CelestialBody.Position` / `.radius` / `.bodyName` | `Assets/3 - Scripts/Scripts/Game/CelestialBody.cs` | `Vector3` / `public float` / `public string` |
| `PlayerController.Rigidbody` | `…/Controllers/PlayerController.cs:1206` | `public Rigidbody` |
| `PlayerController` (find) | — | `FindObjectOfType<PlayerController>()` |
| `Ship.PilotedInstance` / `.AnyShipPiloted` | `…/Controllers/Ship.cs:213/211` | `static Ship` / `static bool` |
| `Ship.HatchOpen` | `Ship.cs:1175` | `public bool` |
| `Ship.Rigidbody` | `Ship.cs:1239` | `public Rigidbody` |
| `Ship.hatch` / `Ship.camViewPoint` | `Ship.cs:16/18` | `public Transform` |
| `ResourceManager.Instance.TakeDamage(amount, playHurtClip)` | `Survival/ResourceManager.cs:158` | sets health, fires existing death pipeline |
| `HALLineHUD.Instance.Show(string)` | `UI/HALLineHUD.cs:65` | shows HUD text **and** plays canned voice via `HALVoicePlayer.TryPlay` |
| `HALVoiceManifest.Lines` | `AI/HALVoiceManifest.cs:26` | `Dictionary<string,string>` exact-line → clip filename |
| `AutosaveManager.Instance.Autosave()` | seeded singleton (used in `NewGameReset.cs:99`) | writes the autosave slot |
| `SaveData` field add | `SaveSystem/SaveData.cs` | append `public O2Save oxygen` |
| Save Capture/Apply | `SaveCollector.cs:24 / 822` | mirror `CaptureResources`/`ApplyResources` |
| New Game reset | `NewGameReset.cs:69-73` | mirror the `ResourceManager` block |
| Singleton seeding | `MainMenuController.cs:498 (Async) / 606 (Legacy)` | add two entries to **both** |
| HAL voice id | `HALVoiceManifest.cs:19` | `JBFqnCBsd6RMkjVDRZzb` (ElevenLabs "George") |

---

## File Structure

- **Create** `Assets/3 - Scripts/Survival/OxygenManager.cs` — the system: pools, per-frame logic, suction, death, save hooks, HUD accessors. One focused file (~230 lines).
- **Create** `Assets/3 - Scripts/Survival/OxygenHUD.cs` — code-built two-bar HUD singleton.
- **Modify** `Assets/3 - Scripts/AI/HALVoiceManifest.cs` — two new exact-line → clip entries.
- **Modify** `Assets/3 - Scripts/UI/MainMenuController.cs` — seed both singletons in the Async + Legacy `EnsureGameplaySingletons`.
- **Modify** `Assets/3 - Scripts/SaveSystem/SaveData.cs` — `O2Save` DTO + `oxygen` field.
- **Modify** `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` — `CaptureOxygen`/`ApplyOxygen` + wire into `Capture()`/`Apply()`.
- **Modify** `Assets/3 - Scripts/SaveSystem/NewGameReset.cs` — reset O2 on New Game.
- **Create** `StreamingAssets/AI/voice/hull_reoxygenating.mp3` + `hull_ajar.mp3` — TTS clips (Coplay).

---

## Task 0: Branch + verify Coplay/Unity link

**Files:** none (prep).

- [ ] **Step 1: Create the feature branch in place**

Run:
```bash
git checkout -b feat/oxygen-atmosphere-system
```
Expected: `Switched to a new branch 'feat/oxygen-atmosphere-system'`.

- [ ] **Step 2: Confirm the Unity Editor + Coplay bridge is alive**

Call `mcp__coplay-mcp__get_unity_editor_state`.
Expected: returns editor state (playmode, active scene) without error. If it errors, STOP — the user must focus the Unity Editor / re-login to Coplay (TTS + compile checks depend on it). Memory note: Coplay TTS 401s if the login lapsed.

- [ ] **Step 3: Baseline compile**

Call `mcp__coplay-mcp__check_compile_errors`.
Expected: zero errors (clean starting point). If pre-existing errors exist, report them before proceeding — don't attribute them to this work later.

---

## Task 1: OxygenManager core (the heart)

**Files:**
- Create: `Assets/3 - Scripts/Survival/OxygenManager.cs`

- [ ] **Step 1: Create the file with the full implementation**

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Survival oxygen system. Two seconds-of-air pools: the suit (keeps the player
/// alive) and the ship hull (a buffer that depletes FIRST while the player is
/// inside, because inside-with-hull-air counts as breathing). Air comes from
/// breathable zones — the lower half of Humble Abode's atmosphere and all of
/// Cyclops. An OPEN hatch exchanges hull air with the outside: it tops the hull
/// up in a refill zone, bleeds it out above the midpoint / in space (faster the
/// higher you are). A CLOSED hatch seals the hull indefinitely. A standing
/// (non-piloting) player with the hatch open in flight is dragged toward the
/// hatch and ejected onto suit-only air.
///
/// Auto-singleton with MainMenu skip — ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1 in CLAUDE.md), or it
/// never auto-creates in builds. All world reads use CelestialBody gameplay
/// accessors via NBodySimulation.Bodies; the forbidden atmosphere/shader code
/// is never touched.
/// </summary>
public class OxygenManager : MonoBehaviour
{
    public static OxygenManager Instance { get; private set; }

    public enum HullState { Sealed, Refilling, Draining }

    // ── Pools (seconds-of-air) ───────────────────────────────────────────
    float suitO2;
    float hullO2;
    HullState hullState = HullState.Sealed;
    bool cyclopsCheckpointReached;

    // ── Public accessors (HUD + save) ────────────────────────────────────
    public float SuitO2 => suitO2;
    public float HullO2 => hullO2;
    public float SuitPercent => suitMax > 0f ? Mathf.Clamp01(suitO2 / suitMax) : 0f;
    public float HullPercent => hullMax > 0f ? Mathf.Clamp01(hullO2 / hullMax) : 0f;
    public HullState State => hullState;
    public bool PlayerOnFoot { get; private set; }
    public bool PlayerPiloting { get; private set; }
    public bool PlayerInsideShip { get; private set; }
    public bool CyclopsCheckpointReached => cyclopsCheckpointReached;

    // ── Runtime caches ───────────────────────────────────────────────────
    PlayerController player;
    Ship mainShip;
    float ajarTimer;
    bool suitDepletedHandled;
    float playerRefindTimer;

    const string VO_REOXY = "Re-oxygenating the hull";
    const string VO_AJAR  = "Hull is ajar";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("OxygenManager");
        DontDestroyOnLoad(go);
        go.AddComponent<OxygenManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        suitO2 = suitMax;
        hullO2 = hullMax;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ── Save hooks ───────────────────────────────────────────────────────
    public void ApplyState(float suit, float hull, bool cyclopsReached)
    {
        suitO2 = Mathf.Clamp(suit, 0f, suitMax);
        hullO2 = Mathf.Clamp(hull, 0f, hullMax);
        cyclopsCheckpointReached = cyclopsReached;
        suitDepletedHandled = false;
    }

    public void ResetForNewGame() => ApplyState(suitMax, hullMax, false);

    float Midpoint => atmosphereTopAltitude * 0.5f;

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        if (dt <= 0f) return;

        EnsureRefs();
        if (player == null) return;

        // Off the solar-system scene (backrooms / poolrooms interiors) there are
        // no celestial bodies — treat as fully breathable so the player never
        // suffocates indoors. Suit tops up, hull holds sealed, no suction.
        var bodies = NBodySimulation.Bodies;
        if (bodies == null || bodies.Length == 0)
        {
            suitO2 = Mathf.Min(suitMax, suitO2 + suitRefillRate * dt);
            hullState = HullState.Sealed;
            suitDepletedHandled = false;
            SetFootState(inside: false, piloting: false, onFoot: true);
            return;
        }

        // ── Resolve the active ship + pilot/inside state ─────────────────
        Ship piloted = Ship.PilotedInstance;
        bool piloting = Ship.AnyShipPiloted && piloted != null;
        Ship ship = piloting ? piloted : mainShip;

        Vector3 playerPos = player.Rigidbody.position;
        Vector3 shipPos = ship != null ? ship.Rigidbody.position : playerPos;

        // While piloting the player GameObject is disabled (its rb.position goes
        // stale), so piloting alone counts as "inside". Otherwise a distance
        // check against the cockpit view-point decides it.
        bool insideVolume = ship != null &&
            Vector3.Distance(playerPos, InteriorAnchor(ship)) <= interiorRadius;
        bool insideShip = piloting || insideVolume;
        bool onFoot = !insideShip;
        bool hatchOpen = ship != null && ship.HatchOpen;

        // ── Altitudes + zones ────────────────────────────────────────────
        CelestialBody shipBody = NearestBody(shipPos);
        CelestialBody playerBody = NearestBody(playerPos);
        float shipAlt = Altitude(shipPos, shipBody);
        float playerAlt = Altitude(playerPos, playerBody);

        bool shipInRefill = InRefillZone(shipBody, shipAlt);
        bool playerInRefill = InRefillZone(playerBody, playerAlt);

        // Altitude factor for hull-drain + suction: 0 at the midpoint, 1 at the
        // atmosphere top, and 1 anywhere that isn't Humble Abode (vacuum).
        float altT;
        if (shipBody != null && shipBody.bodyName == humbleAbodeName)
            altT = Mathf.Clamp01((shipAlt - Midpoint) / Mathf.Max(0.0001f, atmosphereTopAltitude - Midpoint));
        else
            altT = 1f;

        // ── 1) Hull oxygen (only changes with the hatch OPEN) ────────────
        HullState prev = hullState;
        if (ship != null && hatchOpen && shipInRefill)
        {
            hullState = HullState.Refilling;
            hullO2 = Mathf.Min(hullMax, hullO2 + hullRefillRate * dt);
        }
        else if (ship != null && hatchOpen && !shipInRefill)
        {
            hullState = HullState.Draining;
            float rate = Mathf.Lerp(hullDrainMin, hullDrainMax, altT);
            hullO2 = Mathf.Max(0f, hullO2 - rate * dt);
        }
        else
        {
            hullState = HullState.Sealed; // holds its air; never depletes
        }

        // Edge-triggered VO on hull-state ENTRY (never per-frame).
        if (hullState == HullState.Refilling && prev != HullState.Refilling)
            PlayVO(VO_REOXY);
        if (hullState == HullState.Draining && prev != HullState.Draining)
        {
            PlayVO(VO_AJAR);
            ajarTimer = hullAjarRepeat;
        }
        if (hullState == HullState.Draining)
        {
            ajarTimer -= dt;
            if (ajarTimer <= 0f) { PlayVO(VO_AJAR); ajarTimer = hullAjarRepeat; }
        }

        // ── 2) Breathing → 3) Suit oxygen ────────────────────────────────
        // Breathing if standing in breathable air OR inside a hull with air.
        // This single line yields "hull drains before the suit".
        bool breathing = playerInRefill || (insideShip && hullO2 > 0f);
        if (breathing)
        {
            suitO2 = Mathf.Min(suitMax, suitO2 + suitRefillRate * dt); // refill (sanctuary)
            suitDepletedHandled = false;
        }
        else
        {
            suitO2 = Mathf.Max(0f, suitO2 - suitDrainRate * dt);
            if (suitO2 <= 0f && !suitDepletedHandled)
            {
                suitDepletedHandled = true;
                KillPlayer();
            }
        }

        // ── 4) Hatch suction — eject a standing player out an open hatch ──
        bool suction = insideShip && !piloting && hatchOpen && ship != null
                       && !shipInRefill && hullO2 > 0f;
        if (suction && player.gameObject.activeInHierarchy)
        {
            Vector3 dir = HatchPoint(ship) - player.Rigidbody.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                float mag = Mathf.Lerp(suctionForceMin, suctionForceMax, altT); // MIN > 0
                player.Rigidbody.AddForce(dir.normalized * mag, ForceMode.Acceleration);
            }
        }

        // ── Cyclops checkpoint (autosave once on first breathable arrival) ─
        if (!cyclopsCheckpointReached && playerBody != null
            && playerBody.bodyName == cyclopsName && playerInRefill)
        {
            cyclopsCheckpointReached = true;
            if (AutosaveManager.Instance != null) AutosaveManager.Instance.Autosave();
        }

        SetFootState(insideShip, piloting, onFoot);
        if (ship != null) mainShip = ship; // keep the last-known ship after pilot exit
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    void EnsureRefs()
    {
        // Cache once; lazy-refind only when null, throttled (never hammer
        // FindObjectOfType every frame — CLAUDE.md convention).
        if (player == null)
        {
            playerRefindTimer -= Time.fixedDeltaTime;
            if (playerRefindTimer <= 0f)
            {
                player = FindObjectOfType<PlayerController>();
                playerRefindTimer = 0.5f;
            }
        }
        if (mainShip == null) mainShip = FindObjectOfType<Ship>();
    }

    bool InRefillZone(CelestialBody body, float altitude)
    {
        if (body == null) return false;
        if (body.bodyName == humbleAbodeName) return altitude <= Midpoint;
        if (body.bodyName == cyclopsName)     return altitude <= cyclopsBreathableCeiling;
        return false;
    }

    static CelestialBody NearestBody(Vector3 pos)
    {
        var bodies = NBodySimulation.Bodies;
        CelestialBody nearest = null;
        float best = float.MaxValue;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            float d = (b.Position - pos).sqrMagnitude;
            if (d < best) { best = d; nearest = b; }
        }
        return nearest;
    }

    static float Altitude(Vector3 pos, CelestialBody body)
    {
        if (body == null) return float.MaxValue;
        return (pos - body.Position).magnitude - body.radius;
    }

    Vector3 InteriorAnchor(Ship ship)
        => ship.camViewPoint != null ? ship.camViewPoint.position : ship.transform.position;

    Vector3 HatchPoint(Ship ship)
        => ship.hatch != null ? ship.hatch.position : InteriorAnchor(ship);

    void KillPlayer()
    {
        // Overkill damage drives the existing death pipeline (cutscene → reload
        // newest save). playHurtClip:false — suffocation isn't an impact "ow".
        var rm = ResourceManager.Instance;
        if (rm != null) rm.TakeDamage(200f, false);
    }

    void PlayVO(string line)
    {
        // HALLineHUD.Show shows the strip AND plays the canned clip via
        // HALVoicePlayer.TryPlay (manifest lookup added in a later task).
        if (HALLineHUD.Instance != null) HALLineHUD.Instance.Show(line);
        else if (HALVoicePlayer.Instance != null) HALVoicePlayer.Instance.TryPlay(line);
    }

    void SetFootState(bool inside, bool piloting, bool onFoot)
    {
        PlayerInsideShip = inside;
        PlayerPiloting = piloting;
        PlayerOnFoot = onFoot;
    }

    // ── Tunables (APPEND-ONLY at class end; spec defaults) ────────────────
    [Header("Pool capacities (seconds of air)")]
    [SerializeField] float suitMax = 120f;
    [SerializeField] float hullMax = 300f;

    [Header("Rates (seconds-of-air per real second)")]
    [SerializeField] float suitDrainRate  = 1.0f;
    [SerializeField] float suitRefillRate = 24.0f;
    [SerializeField] float hullRefillRate = 60.0f;
    [SerializeField] float hullDrainMin   = 5.0f;
    [SerializeField] float hullDrainMax   = 60.0f;

    [Header("Atmosphere (metres above surface)")]
    [Tooltip("Height above Humble Abode's surface where the atmosphere ends. The lower half (<= half this) is breathable. Tune per level.")]
    [SerializeField] float atmosphereTopAltitude = 600f;
    [Tooltip("Altitude (m) under which Cyclops counts as breathable everywhere. Generous by design.")]
    [SerializeField] float cyclopsBreathableCeiling = 100000f;

    [Header("Hatch suction (always-tug: MIN > 0)")]
    [SerializeField] float suctionForceMin = 12f;
    [SerializeField] float suctionForceMax = 60f;

    [Header("Ship interior")]
    [Tooltip("Radius (m) around the ship's cockpit view-point counted as 'inside the ship'. Tune to the interior size.")]
    [SerializeField] float interiorRadius = 4f;

    [Header("VO")]
    [Tooltip("Seconds between repeats of 'Hull is ajar' while still breaching.")]
    [SerializeField] float hullAjarRepeat = 8f;

    [Header("Planet names (must match CelestialBody.bodyName)")]
    [SerializeField] string humbleAbodeName = "Humble Abode";
    [SerializeField] string cyclopsName = "Cyclops";
}
```

- [ ] **Step 2: Compile-verify**

Call `mcp__coplay-mcp__check_compile_errors`.
Expected: zero errors. Common failures to fix in place: a mistyped accessor (`Ship.HatchOpen`, `player.Rigidbody`, `NBodySimulation.Bodies`) — all confirmed to exist in the ground-truth table above.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Survival/OxygenManager.cs" "Assets/3 - Scripts/Survival/OxygenManager.cs.meta"
git commit -m "feat(survival): add OxygenManager core (suit+hull O2, breathing, suction, death)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: OxygenHUD (code-built meters)

**Files:**
- Create: `Assets/3 - Scripts/Survival/OxygenHUD.cs`

Pattern: a self-built ScreenSpaceOverlay canvas like the existing HUD singletons. Bars use the proven `ResourceHUD` fill technique — a stretched fill RectTransform with a LEFT pivot whose `localScale.x` is the fill fraction (no sprite needed). Suit bar is always visible; hull bar fades in while piloting / inside / draining.

- [ ] **Step 1: Create the file**

```csharp
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Code-built HUD for the OxygenManager pools (mirrors the self-built-canvas
/// pattern used by VitalsHUD / HALLineHUD). Suit meter is always visible; the
/// hull meter fades in while piloting, inside the ship, or while the hull is
/// breaching. Bars use the ResourceHUD fill trick: a left-pivot fill scaled on X.
///
/// Auto-singleton with MainMenu skip — ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1).
/// </summary>
public class OxygenHUD : MonoBehaviour
{
    public static OxygenHUD Instance { get; private set; }

    RectTransform suitFill, hullFill;
    CanvasGroup hullGroup;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("OxygenHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<OxygenHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void BuildUI()
    {
        var canvasGO = new GameObject("OxygenCanvas", typeof(RectTransform));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();
        // Hide over MainMenu like the other HUDs.
        HUDSceneGate.Register(canvas.rootCanvas);

        // Top-left, stacked below where the vitals panel sits. Tune offsets here.
        suitFill = MakeBar(canvasGO.transform, "SuitO2", new Vector2(24f, -300f),
                           new Color32(0x5C, 0xC8, 0xFF, 0xFF), "SUIT O2", out _);
        hullFill = MakeBar(canvasGO.transform, "HullO2", new Vector2(24f, -332f),
                           new Color32(0xFF, 0xC8, 0x5C, 0xFF), "HULL O2", out var hullRow);
        hullGroup = hullRow.AddComponent<CanvasGroup>();
        hullGroup.alpha = 0f;
    }

    // Builds a label + background + left-pivot fill row anchored top-left.
    // Returns the fill RectTransform (scaled on X each frame); `rowGO` is the
    // whole row container (so the hull row can get a CanvasGroup for fading).
    RectTransform MakeBar(Transform parent, string name, Vector2 anchoredPos,
                          Color color, string labelText, out GameObject rowGO)
    {
        rowGO = new GameObject(name + "Row", typeof(RectTransform));
        var rowRT = (RectTransform)rowGO.transform;
        rowRT.SetParent(parent, false);
        rowRT.anchorMin = rowRT.anchorMax = new Vector2(0f, 1f);
        rowRT.pivot = new Vector2(0f, 1f);
        rowRT.anchoredPosition = anchoredPos;
        rowRT.sizeDelta = new Vector2(260f, 24f);

        // Label (left 66px).
        var labGO = new GameObject("Label", typeof(RectTransform));
        var labRT = (RectTransform)labGO.transform;
        labRT.SetParent(rowRT, false);
        labRT.anchorMin = new Vector2(0f, 0f);
        labRT.anchorMax = new Vector2(0f, 1f);
        labRT.pivot = new Vector2(0f, 0.5f);
        labRT.anchoredPosition = Vector2.zero;
        labRT.sizeDelta = new Vector2(66f, 0f);
        var lab = labGO.AddComponent<TextMeshProUGUI>();
        lab.text = labelText;
        lab.fontSize = 13f;
        lab.alignment = TextAlignmentOptions.MidlineLeft;
        lab.color = Color.white;

        // Background (right of label, stretched).
        var bgGO = new GameObject("BG", typeof(RectTransform));
        var bgRT = (RectTransform)bgGO.transform;
        bgRT.SetParent(rowRT, false);
        bgRT.anchorMin = new Vector2(0f, 0f);
        bgRT.anchorMax = new Vector2(1f, 1f);
        bgRT.pivot = new Vector2(0f, 0.5f);
        bgRT.offsetMin = new Vector2(70f, 4f);
        bgRT.offsetMax = new Vector2(0f, -4f);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.5f);

        // Fill (stretched inside BG, LEFT pivot, scaled on X).
        var fillGO = new GameObject("Fill", typeof(RectTransform));
        var fillRT = (RectTransform)fillGO.transform;
        fillRT.SetParent(bgRT, false);
        fillRT.anchorMin = new Vector2(0f, 0f);
        fillRT.anchorMax = new Vector2(1f, 1f);
        fillRT.pivot = new Vector2(0f, 0.5f);
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.color = color;

        return fillRT;
    }

    void Update()
    {
        var om = OxygenManager.Instance;
        if (om == null) return;

        SetBar(suitFill, om.SuitPercent);
        SetBar(hullFill, om.HullPercent);

        bool showHull = om.PlayerPiloting || om.PlayerInsideShip
                        || om.State == OxygenManager.HullState.Draining;
        if (hullGroup != null) hullGroup.alpha = showHull ? 1f : 0f;
    }

    static void SetBar(RectTransform fill, float percent)
    {
        if (fill == null) return;
        fill.localScale = new Vector3(Mathf.Clamp01(percent), 1f, 1f);
    }
}
```

- [ ] **Step 2: Compile-verify** — `mcp__coplay-mcp__check_compile_errors` → zero errors. (`HUDSceneGate.Register` is confirmed in `ResourceHUD.cs:76`.)

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Survival/OxygenHUD.cs" "Assets/3 - Scripts/Survival/OxygenHUD.cs.meta"
git commit -m "feat(survival): add code-built OxygenHUD (suit + hull meters)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: VO manifest entries

**Files:**
- Modify: `Assets/3 - Scripts/AI/HALVoiceManifest.cs` (inside the `Lines` dictionary initializer, after the "Atmosphere transitions" block ~line 80)

- [ ] **Step 1: Add two exact-line → clip mappings**

Find this block (around line 78-84):
```csharp
        // ── Atmosphere transitions ─────────────────────────────────────
        { "Leaving atmosphere, Astronaut. Vacuum confirmed.",     "atmo_leave.mp3" },
        { "Entering atmosphere, Astronaut. Descent in progress.", "atmo_enter.mp3" },
```
Insert immediately after it (still inside the `Lines` initializer):
```csharp

        // ── Oxygen / hull pressurization ───────────────────────────────
        { "Re-oxygenating the hull",                              "hull_reoxygenating.mp3" },
        { "Hull is ajar",                                         "hull_ajar.mp3" },
```
The keys are the EXACT strings `OxygenManager` passes to `HALLineHUD.Show` (the `VO_REOXY`/`VO_AJAR` consts). They must match character-for-character.

- [ ] **Step 2: Compile-verify** — `check_compile_errors` → zero errors.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/AI/HALVoiceManifest.cs"
git commit -m "feat(audio): register hull O2 VO lines in HAL voice manifest

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Seed both singletons in EnsureGameplaySingletons (trap #1)

**Files:**
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs` — the Async method (one-liner style, ~line 536) AND the Legacy method (block style, ~line 704).

- [ ] **Step 1: Async method — add two entries after the `VitalsHUD` line**

Find (around line 536):
```csharp
        if (VitalsHUD.Instance == null) { var go = new GameObject("VitalsHUD"); DontDestroyOnLoad(go); go.AddComponent<VitalsHUD>(); }
```
Insert immediately after it:
```csharp
        if (OxygenManager.Instance == null) { var go = new GameObject("OxygenManager"); DontDestroyOnLoad(go); go.AddComponent<OxygenManager>(); }
        if (OxygenHUD.Instance == null) { var go = new GameObject("OxygenHUD"); DontDestroyOnLoad(go); go.AddComponent<OxygenHUD>(); }
```

- [ ] **Step 2: Legacy method — add two blocks after the `VitalsHUD` block**

Find (around line 704-709):
```csharp
        if (VitalsHUD.Instance == null)
        {
            var go = new GameObject("VitalsHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<VitalsHUD>();
        }
```
Insert immediately after it:
```csharp
        if (OxygenManager.Instance == null)
        {
            var go = new GameObject("OxygenManager");
            DontDestroyOnLoad(go);
            go.AddComponent<OxygenManager>();
        }
        if (OxygenHUD.Instance == null)
        {
            var go = new GameObject("OxygenHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<OxygenHUD>();
        }
```

- [ ] **Step 3: Compile-verify** — `check_compile_errors` → zero errors.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/UI/MainMenuController.cs"
git commit -m "fix(boot): seed OxygenManager + OxygenHUD in EnsureGameplaySingletons (trap #1)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Save persistence

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveData.cs`
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`
- Modify: `Assets/3 - Scripts/SaveSystem/NewGameReset.cs`

- [ ] **Step 1: Add the DTO + field to SaveData**

In `SaveData.cs`, add the `oxygen` field to the `SaveData` class. Find (line 14):
```csharp
    public ResourcesSave resources = new ResourcesSave();
```
Insert immediately after it:
```csharp
    public O2Save oxygen = new O2Save();
```
Then add the DTO class next to `ResourcesSave` (after the `ResourcesSave` class closes, ~line 210):
```csharp

[Serializable]
public class O2Save
{
    // Defaults = full tanks so pre-feature saves (missing this object) load
    // breathing-safe rather than suffocating on load.
    public float suitO2 = 120f;
    public float hullO2 = 300f;
    public bool cyclopsCheckpointReached;
}
```

- [ ] **Step 2: Add Capture + Apply in SaveCollector and wire them in**

In `SaveCollector.cs`, in `Capture()` find (line 24):
```csharp
        CaptureResources(data.resources);
```
Insert immediately after it:
```csharp
        CaptureOxygen(data.oxygen);
```
Add the method right after `CaptureResources` (after line 255):
```csharp

    static void CaptureOxygen(O2Save s)
    {
        var om = OxygenManager.Instance;
        if (om == null) return;
        s.suitO2 = om.SuitO2;
        s.hullO2 = om.HullO2;
        s.cyclopsCheckpointReached = om.CyclopsCheckpointReached;
    }
```
In `Apply()` find (line 822):
```csharp
        ApplyResources(data.resources);
```
Insert immediately after it:
```csharp
        ApplyOxygen(data.oxygen);
```
Add the method right after `ApplyResources` (after line 981):
```csharp

    static void ApplyOxygen(O2Save s)
    {
        if (OxygenManager.Instance != null)
            OxygenManager.Instance.ApplyState(s.suitO2, s.hullO2, s.cyclopsCheckpointReached);
    }
```

- [ ] **Step 3: Reset on New Game**

In `NewGameReset.cs`, find the `ResourceManager` block (lines 69-73):
```csharp
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.ApplyState(100f, 100f, 100f); // full hunger/thirst/health
            ResourceManager.Instance.SetTotalDeaths(0);
        }
```
Insert immediately after it:
```csharp
        if (OxygenManager.Instance != null) OxygenManager.Instance.ResetForNewGame();
```

- [ ] **Step 4: Compile-verify** — `check_compile_errors` → zero errors.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/SaveSystem/SaveData.cs" "Assets/3 - Scripts/SaveSystem/SaveCollector.cs" "Assets/3 - Scripts/SaveSystem/NewGameReset.cs"
git commit -m "feat(save): persist suit/hull O2 + Cyclops checkpoint; reset on New Game

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Generate the two HAL VO clips (Coplay TTS)

**Files:**
- Create: `Assets/StreamingAssets/AI/voice/hull_reoxygenating.mp3`
- Create: `Assets/StreamingAssets/AI/voice/hull_ajar.mp3`

- [ ] **Step 1: Confirm the voice directory + filenames**

Use `mcp__coplay-mcp__list_files` (or `search_files`) on `Assets/StreamingAssets/AI/voice/` to confirm the existing bank (e.g. `atmo_enter.mp3`) and that the two target filenames don't yet exist.

- [ ] **Step 2: Generate "Re-oxygenating the hull"**

Call `mcp__coplay-mcp__generate_tts` with the George voice id `JBFqnCBsd6RMkjVDRZzb`, text `Re-oxygenating the hull.`, targeting `Assets/StreamingAssets/AI/voice/hull_reoxygenating.mp3`. (If the tool returns a generated asset path elsewhere, move/rename it to that exact path so the manifest entry resolves. Use `search_tts_voice_id` for "George" only if the hard-coded id fails.)

- [ ] **Step 3: Generate "Hull is ajar"**

Call `mcp__coplay-mcp__generate_tts` with the same voice id, text `Hull is ajar.`, targeting `Assets/StreamingAssets/AI/voice/hull_ajar.mp3`.

- [ ] **Step 4: Verify both files exist on disk** via `list_files` on the voice folder. Expected: both `hull_reoxygenating.mp3` and `hull_ajar.mp3` present.

> **Note:** This is the only Coplay-auth-dependent task. If TTS 401s (lapsed Coplay login), the feature still works — `HALLineHUD.Show` displays the text silently and logs a "Failed to load voice clip" warning once. Park this task, finish Task 7, and regenerate the clips later; no code changes are needed when they appear.

- [ ] **Step 5: Commit** (only if the voice folder is tracked — check first)

```bash
git status --short "Assets/StreamingAssets/AI/voice/"
```
If the new mp3s show as untracked/modified (not gitignored):
```bash
git add "Assets/StreamingAssets/AI/voice/hull_reoxygenating.mp3" "Assets/StreamingAssets/AI/voice/hull_reoxygenating.mp3.meta" "Assets/StreamingAssets/AI/voice/hull_ajar.mp3" "Assets/StreamingAssets/AI/voice/hull_ajar.mp3.meta"
git commit -m "feat(audio): generate hull O2 VO clips (George voice)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```
If gitignored, note that in the task output and skip the commit (clips are re-generatable, like the rest of the bank).

---

## Task 7: In-Editor verification against the acceptance checklist

**Files:** none (behavioral verification). This is the real "tests pass" gate for this Editor-only project.

- [ ] **Step 1: Clean compile + enter play mode**

Call `check_compile_errors` (zero errors), then `mcp__coplay-mcp__play_game` to enter play mode in `1.6.7.7.7.unity`. Use `mcp__coplay-mcp__get_unity_logs` after each scenario to confirm no exceptions and to read any debug output.

- [ ] **Step 2: Tune the two level constants first**

In play mode, find the `OxygenManager` GameObject (via `get_game_object_info` / `list_game_objects_in_hierarchy`). Read Humble Abode's `radius` (inspect the `Humble Abode` CelestialBody) and set `atmosphereTopAltitude` to a value that matches where the visible atmosphere ends (start near the body radius; the spec's midpoint is half of this). Also sanity-check `interiorRadius` against the ship interior and `suctionForceMin/Max` feel. These are the only values likely to need tuning; everything else uses spec defaults.

- [ ] **Step 3: Walk the acceptance checklist** (from the design doc §11). Mark each:
  - [ ] Standing on Humble Abode below midpoint → suit sits at full; suit bar full.
  - [ ] Walk uphill past the midpoint → suit stops refilling and drains; HUD reflects it.
  - [ ] In the ship at base, open the hatch → "Re-oxygenating the hull" plays/shows, hull climbs to full.
  - [ ] Close hatch, fly to space → hull stays full (Sealed), no warning, suit topped up inside.
  - [ ] Hatch open + piloting to space → "Hull is ajar" plays, hull drains fast; at 0 the suit drains; death ~120 s later.
  - [ ] Stand mid-flight with hatch open above midpoint → pulled to the hatch and ejected; then suit-only.
  - [ ] Just above midpoint → slow drain + gentle (nonzero) suction; high up → near-instant drain + strong suction.
  - [ ] Constant Companion on-foot → suit drains, no refill; back in the oxygenated hull → safe.
  - [ ] Reach Cyclops → suit + hull refill; an autosave fires (Cyclops becomes the checkpoint). Confirm via `get_unity_logs` / the autosave file timestamp.
  - [ ] Suit at 0 → death + respawn at the last checkpoint, suit restored to full (load path sets suit/hull from the save).

- [ ] **Step 4: Record results.** For any failing item, capture the symptom + the `get_unity_logs` output and either fix in place (re-run the relevant task's compile-verify + commit) or report it. Do NOT claim the checklist passes without having observed each item — evidence before assertions.

- [ ] **Step 5: Final commit (any tuning/fixes) + summary**

```bash
git add -A
git commit -m "tune(survival): oxygen system constants + verification fixes

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```
Then surface to the user: which acceptance items passed (with evidence), which needed tuning, and the final constant values used.

---

## Self-Review

**Spec coverage** (design §11 acceptance items → task):
- Suit refill/drain by altitude on Humble Abode → Task 1 (breathing + suit block, `altT`/`Midpoint`). ✓
- Hull refill/drain/sealed by hatch + zone + altitude → Task 1 (hull block). ✓
- Edge-triggered VO "Re-oxygenating"/"Hull is ajar" + 8 s repeat → Task 1 (`PlayVO` edge logic) + Task 3 (manifest) + Task 6 (clips). ✓
- "Hull drains before suit" → Task 1 (`breathing = ... || (insideShip && hullO2 > 0)`). ✓
- Suction eject, always-tug floor, altitude scaling → Task 1 (suction block, `suctionForceMin > 0`, `Mathf.Lerp(..., altT)`). ✓
- Suit-only death → existing pipeline → Task 1 (`KillPlayer` → `ResourceManager.TakeDamage`). ✓
- HUD: suit always, hull while piloting/inside/draining → Task 2. ✓
- Save suit/hull/checkpoint + New Game reset + load-restore-to-full → Task 5. ✓
- Cyclops checkpoint autosave → Task 1 (checkpoint block) + Task 5 (persisted flag). ✓
- Interior scenes (backrooms) never suffocate → Task 1 (`bodies.Length == 0` early breathe). ✓
- Build-safe singletons → Task 4 (trap #1 seeding). ✓

**Placeholder scan:** no TBD/TODO; every code step is complete. The only deferred artifact is the TTS audio (Task 6), which fails open (silent text) and is explicitly handled.

**Type consistency:** accessors used in later tasks match Task 1 definitions — `SuitO2`/`HullO2`/`CyclopsCheckpointReached`/`SuitPercent`/`HullPercent`/`State`/`PlayerPiloting`/`PlayerInsideShip` (HUD + save reads), `ApplyState(float,float,bool)`/`ResetForNewGame()` (save + New Game), `HullState.Draining` (HUD). VO consts `VO_REOXY`/`VO_AJAR` match the Task 3 manifest keys character-for-character. ✓

**Scope:** single cohesive feature, one plan. No decomposition needed.
