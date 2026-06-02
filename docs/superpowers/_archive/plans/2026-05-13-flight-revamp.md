# Flight Revamp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Outer Wilds-style assists (Match Velocity, Hold-O Circularize) and prograde/retrograde HUD markers to ship flight, plus retune boost/refill/ramp for smoother feel.

**Architecture:** All physics live on `Ship.cs`. Match Velocity reads the focused-target body/ship from `SolarSystemMapController` (already-public `FollowedShip` + a new `PendingHighlight` getter). Circularize finds the nearest Planet/Moon via `NBodySimulation.Bodies`. The HUD markers are a new self-contained singleton next to `GForceHUD`.

**Tech Stack:** Unity 2022.3, C#, `NBodySimulation` N-body gravity at 100Hz. No CLI test framework — verification per task is (a) `mcp__coplay-mcp__check_compile_errors` clean and (b) user play-test in the Editor.

**Spec:** `docs/superpowers/specs/2026-05-13-flight-revamp-design.md`

---

## File Structure

| File | Purpose | Action |
|---|---|---|
| `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs` | Ship state, thrust, boost, new assists | Modify |
| `Assets/3 - Scripts/Map/SolarSystemMapController.cs` | Expose `PendingHighlight` for Match Velocity target lookup | Modify (1 line) |
| `Assets/3 - Scripts/Ship/VelocityMarkersHUD.cs` | Prograde/retrograde triangle overlay | Create |

---

## Task 1: Tuning + input ramp

Tightens the existing manual-flight feel before any new assists land. Standalone change — playable on its own.

**Files:** Modify `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs`

- [ ] **Step 1: Update field defaults and add ramp field**

In the `[Header("Handling")]` block, change `thrustPowerScale` tooltip to mention boost retune, and ADD `thrustRampSeconds`:

```csharp
[Header("Handling")]
public float thrustStrength = 20;
[Tooltip("Default thrust multiplier (no boost). 0.33 ≈ 3× less powerful than legacy — orbits stay stable under typical 1-second taps. Boost (Shift) multiplies this for getting close to orbit.")]
[Range(0.05f, 2f)]
public float thrustPowerScale = 0.33f;
[Tooltip("Time (seconds) for thruster input to ramp 0 → full when a key is pressed. Smooths the 'tap feels binary' problem so short presses produce proportional impulses.")]
[Range(0f, 0.5f)]
public float thrustRampSeconds = 0.15f;
public float rotSpeed = 5;
public float rollSpeed = 30;
public float rotSmoothSpeed = 10;
```

In the `[Header("Boost (mirrors player jetpack)")]` block, change `boostMultiplier` default to `2.5f` and `boostRefillPerSec` default to `0.4f`:

```csharp
[Header("Boost (mirrors player jetpack)")]
[Tooltip("Boost multiplier applied to a thrust axis while LeftShift is held and that axis has fuel remaining. With thrustPowerScale=0.33 and boostMultiplier=2.5 the boosted thrust feels punchy without lurching the ship out of orbit.")]
public float boostMultiplier = 2.5f;
[Tooltip("Per-axis pool capacity. 1.0 = a full bar (matches PlayerController jetpack scale).")]
public float boostFuelMax = 1f;
[Tooltip("Fraction-of-max drained per second of continuous boost on that axis.")]
public float boostDrainPerSec = 0.5f;
[Tooltip("Fraction-of-max refilled per second when that axis is NOT actively boosting.")]
public float boostRefillPerSec = 0.4f;
```

- [ ] **Step 2: Add `_smoothedThrusterInput` field**

Below the existing `Vector3 thrusterInput;` line near the private fields:

```csharp
Vector3 thrusterInput;
Vector3 _smoothedThrusterInput;
```

- [ ] **Step 3: Wire ramp into HandleMovement**

Find the line in `HandleMovement` that assigns the raw input:

```csharp
thrusterInput = new Vector3(thrustInputX, thrustInputY, thrustInputZ);
```

Add the smoothing line immediately after:

```csharp
thrusterInput = new Vector3(thrustInputX, thrustInputY, thrustInputZ);
// Smoothed input drives the AddForce; raw input still drives boost-axis
// detection so Shift kicks in instantly without waiting for the ramp.
float rampRate = thrustRampSeconds > 0.001f ? Time.deltaTime / thrustRampSeconds : 1f;
_smoothedThrusterInput = Vector3.MoveTowards(_smoothedThrusterInput, thrusterInput, rampRate);
```

- [ ] **Step 4: Use smoothed input in FixedUpdate**

In `FixedUpdate`, where the thrust force is applied, replace the local-direction line with the smoothed version:

```csharp
Vector3 scaledLocal = new Vector3(_smoothedThrusterInput.x * scaleX, _smoothedThrusterInput.y * scaleY, _smoothedThrusterInput.z * scaleZ);
```

Leave the existing boost detection (`thrusterInput.y > 0.01f` etc.) untouched — those still read RAW input so boost kicks in instantly.

- [ ] **Step 5: Verify compile**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs"
git commit -m "$(cat <<'EOF'
feat(ship): retune boost + add thrust input ramp

boostMultiplier 4 → 2.5, boostRefillPerSec 0.25 → 0.4, and a new
thrustRampSeconds = 0.15 so short taps produce proportional
impulses instead of binary on/off thrust.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Match Velocity (V key)

Holding V cancels relative drift against the player's focused map target (followed ship if any, else marked body).

**Files:**
- Modify `Assets/3 - Scripts/Map/SolarSystemMapController.cs` (expose `PendingHighlight`)
- Modify `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs`

- [ ] **Step 1: Expose PendingHighlight on the map controller**

Open `SolarSystemMapController.cs`. The field `CelestialBody pendingHighlight;` already exists as private. Add a public read accessor next to the existing `public Ship FollowedShip => followedShip;`:

```csharp
public Ship FollowedShip => followedShip;
public CelestialBody PendingHighlight => pendingHighlight;
```

- [ ] **Step 2: Add Match Velocity fields to Ship**

In `Ship.cs`, after the boost fuel pool fields (after `public float DirBoostFuelPercent ...`), add:

```csharp
[Header("Match Velocity (V)")]
[Tooltip("Maximum acceleration applied while V is held to cancel relative velocity to the focused target. ~8 m/s² gives a 4-10s zero on typical orbital deltas.")]
public float matchAcceleration = 8f;
[Tooltip("Key bound to Match Velocity. V by default. Reads SolarSystemMapController.FollowedShip first, then PendingHighlight body.")]
public KeyCode matchVelocityKey = KeyCode.V;
// Read by VelocityMarkersHUD to flash the prograde marker while matching.
public bool IsMatchingVelocity { get; private set; }
```

- [ ] **Step 3: Add Match Velocity logic in FixedUpdate**

In `Ship.FixedUpdate`, inside the `if (shipIsPiloted && canFly && !PlayerController.isMapOpen)` block, AFTER the existing thrust + drain block but BEFORE the closing brace, add:

```csharp
// ── Match Velocity (V) ──────────────────────────────────────────
// Outer Wilds-style assist: hold V to bleed relative velocity to
// the focused map target. Extra ship takes priority over marked
// body (more recent intent). Drains the DIR pool because lateral.
IsMatchingVelocity = false;
if (Input.GetKey(matchVelocityKey) && _boostFuelDir > 0f)
{
    Vector3 targetVel; bool hasTarget = false;
    var map = SolarSystemMapController.Instance;
    if (map != null)
    {
        if (map.FollowedShip != null && map.FollowedShip != this)
        {
            var trb = map.FollowedShip.GetComponent<Rigidbody>();
            if (trb != null) { targetVel = trb.velocity; hasTarget = true; goto applyMatch; }
        }
        if (map.PendingHighlight != null)
        {
            targetVel = map.PendingHighlight.velocity; hasTarget = true;
        }
    }
    targetVel = Vector3.zero; // unreachable in the goto-path below
    applyMatch:
    if (hasTarget)
    {
        Vector3 relVel = rb.velocity - targetVel;
        float relMag = relVel.magnitude;
        if (relMag > 0.01f)
        {
            // Clamp so a single FixedUpdate doesn't overshoot zero.
            float deltaV = Mathf.Min(matchAcceleration * dt, relMag);
            rb.AddForce(-relVel.normalized * deltaV, ForceMode.VelocityChange);
            _boostFuelDir = Mathf.Clamp(_boostFuelDir - boostDrainPerSec * dt, 0f, boostFuelMax);
            IsMatchingVelocity = true;
        }
    }
}
```

> Note: the `goto` is intentional — branching cleanly out of the priority chain is more readable than nested ifs here.

- [ ] **Step 4: Verify compile**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 5: User play-test**

Manual verification:
1. Load a save (or new game). Pilot a ship.
2. Open the map (M), click Humble Abode twice (mark + focus). Close map.
3. Fly so you have some velocity relative to Humble Abode (Shift+W for a second).
4. Hold V. Expected: ship velocity bleeds toward Humble Abode's velocity over a few seconds. The "DIR" bar in the bottom-left HUD drains while V held.
5. Release V. Ship is now moving with Humble Abode (you'll see speed tape stabilize relative to it).

If V does nothing: confirm a body or ship is marked (1st-click on a legend entry sets PendingHighlight).

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs" "Assets/3 - Scripts/Map/SolarSystemMapController.cs"
git commit -m "$(cat <<'EOF'
feat(ship): Match Velocity (V) assist

Holding V while piloting bleeds relative velocity to the map's
focused target (followed ship if any, else marked body) at up to
matchAcceleration m/s². Drains the DIR boost pool.

Cornerstone of the flight revamp: makes interplanetary travel
predictable without removing manual control.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Hold-O Circularize

Near a planet/moon, holding O converts radial → tangential velocity to circularize the orbit at current altitude.

**Files:** Modify `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs`

- [ ] **Step 1: Add Circularize fields**

Below the Match Velocity fields added in Task 2:

```csharp
[Header("Circularize (O)")]
[Tooltip("Maximum acceleration applied while O is held near a planet/moon to convert radial → tangential velocity. ~6 m/s² circularizes a mild ellipse in 2-3s.")]
public float circularizeAcceleration = 6f;
[Tooltip("Multiplier on the nearest body's radius for the trigger range. Beyond this distance, O does nothing.")]
public float circularizeRangeMul = 3f;
[Tooltip("Key bound to Circularize. O by default.")]
public KeyCode circularizeKey = KeyCode.O;
public bool IsCircularizing { get; private set; }
```

- [ ] **Step 2: Add Circularize logic in FixedUpdate**

After the Match Velocity block added in Task 2 (still inside the piloted branch), add:

```csharp
// ── Circularize (O) ─────────────────────────────────────────────
// Hold near a planet/moon to convert radial velocity into
// tangential — auto-circularizes the orbit at current altitude.
// Drains the UP pool.
IsCircularizing = false;
if (Input.GetKey(circularizeKey) && _boostFuelUp > 0f)
{
    CelestialBody best = null; float bestSqr = float.MaxValue;
    var bodies = NBodySimulation.Bodies;
    if (bodies != null)
    {
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            if (b.bodyType == CelestialBody.BodyType.Sun) continue; // sun excluded
            float dsq = (b.Position - rb.position).sqrMagnitude;
            if (dsq < bestSqr) { bestSqr = dsq; best = b; }
        }
    }
    if (best != null)
    {
        float maxRange = circularizeRangeMul * best.radius;
        Vector3 r = rb.position - best.Position;
        float rMag = r.magnitude;
        if (rMag <= maxRange && rMag > best.radius * 1.05f)
        {
            Vector3 v = rb.velocity - best.velocity;
            float vMag = v.magnitude;
            if (vMag > 0.1f)
            {
                Vector3 radialDir = r / rMag;
                Vector3 vRadial = Vector3.Dot(v, radialDir) * radialDir;
                Vector3 vTang = v - vRadial;
                // Pick a tangent axis if velocity is almost purely radial.
                Vector3 tangDir = vTang.sqrMagnitude > 0.01f
                    ? vTang.normalized
                    : Vector3.Cross(radialDir, Vector3.up).normalized;
                Vector3 vTarget = tangDir * vMag + best.velocity; // preserve KE
                Vector3 needed = vTarget - rb.velocity;
                float needMag = needed.magnitude;
                if (needMag > 0.01f)
                {
                    float deltaV = Mathf.Min(circularizeAcceleration * dt, needMag);
                    rb.AddForce(needed.normalized * deltaV, ForceMode.VelocityChange);
                    _boostFuelUp = Mathf.Clamp(_boostFuelUp - boostDrainPerSec * dt, 0f, boostFuelMax);
                    IsCircularizing = true;
                }
            }
        }
    }
}
```

- [ ] **Step 3: Verify compile**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 4: User play-test**

Manual verification:
1. Pilot a ship. Fly up to ~2× Humble Abode's radius (above the atmosphere).
2. Shut off thrust and let yourself fall slightly — you'll have a slightly elliptical orbit at best.
3. Hold O for ~2-3 seconds. Expected: the ship reorients its velocity to be perpendicular to "down". The UP bar drains.
4. Release O. Watch the ship for ~30 seconds — altitude should hold roughly steady (small oscillation OK).

If O does nothing: confirm you're within 3× radius (try closer) and that the UP pool has fuel.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs"
git commit -m "$(cat <<'EOF'
feat(ship): Hold-O circularize assist

Near a planet/moon, holding O converts radial velocity to
tangential while preserving total kinetic energy — auto-rounds an
ellipse at the current altitude. Drains the UP boost pool.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Prograde / Retrograde HUD markers

**Files:** Create `Assets/3 - Scripts/Ship/VelocityMarkersHUD.cs`

- [ ] **Step 1: Create the new singleton HUD file**

```csharp
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Cockpit overlay drawing prograde (green) and retrograde (red) triangle
/// markers anchored to the projected velocity vector. Helps the player see
/// where they're actually heading — essential for using Match Velocity and
/// Circularize correctly.
///
/// Singleton, auto-created like GForceHUD. Hidden when the player isn't
/// piloting a ship or velocity is negligible.
/// </summary>
public class VelocityMarkersHUD : MonoBehaviour
{
    public static VelocityMarkersHUD Instance { get; private set; }

    static readonly Color PrograConfigColor = new Color(0.36f, 1f, 0.55f, 1f);
    static readonly Color RetroColor        = new Color(1f, 0.30f, 0.30f, 1f);

    Canvas _canvas;
    RectTransform _prograde;
    RectTransform _retrograde;
    Text _proLabel;
    Text _retLabel;
    Image _proImg;
    Image _retImg;
    Camera _mainCam;
    Ship _cachedShip;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("VelocityMarkersHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<VelocityMarkersHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Build()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 810; // above HUD background, below pause/map menus
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        _prograde   = BuildMarker("Prograde",   PrograConfigColor, "PRO", out _proImg, out _proLabel);
        _retrograde = BuildMarker("Retrograde", RetroColor,        "RET", out _retImg, out _retLabel);
    }

    RectTransform BuildMarker(string name, Color color, string label, out Image img, out Text txt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(28f, 28f);

        // Triangle: use an Image with a simple equilateral sprite (Unity ships
        // a default UISprite which is square — we'll fake a triangle look via
        // a rotated square with rounded corners + colour).
        img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;

        // Label below the marker.
        var labGO = new GameObject("Label", typeof(RectTransform));
        labGO.transform.SetParent(go.transform, false);
        var lrt = (RectTransform)labGO.transform;
        lrt.anchorMin = new Vector2(0.5f, 0f);
        lrt.anchorMax = new Vector2(0.5f, 0f);
        lrt.pivot = new Vector2(0.5f, 1f);
        lrt.anchoredPosition = new Vector2(0f, -2f);
        lrt.sizeDelta = new Vector2(40f, 14f);
        txt = labGO.AddComponent<Text>();
        txt.text = label;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 10;
        txt.fontStyle = FontStyle.Bold;
        txt.color = color;
        txt.alignment = TextAnchor.UpperCenter;
        txt.raycastTarget = false;
        return rt;
    }

    void LateUpdate()
    {
        // Locate the piloted ship + the main camera.
        if (_cachedShip == null || !_cachedShip.IsPiloted)
        {
            var ships = FindObjectsOfType<Ship>(true);
            _cachedShip = null;
            for (int i = 0; i < ships.Length; i++)
                if (ships[i] != null && ships[i].IsPiloted) { _cachedShip = ships[i]; break; }
        }
        if (_mainCam == null) _mainCam = Camera.main;

        bool show = _cachedShip != null && _mainCam != null;
        if (!show) { SetMarkersActive(false); return; }

        var rb = _cachedShip.GetComponent<Rigidbody>();
        if (rb == null) { SetMarkersActive(false); return; }

        Vector3 vel = rb.velocity;
        if (vel.sqrMagnitude < 1f) { SetMarkersActive(false); return; }

        // Project velocity from a reference point 100 units in front of the
        // camera along the vel direction — gives a stable screen-space anchor
        // independent of camera world position.
        Vector3 camPos = _mainCam.transform.position;
        Vector3 proPoint = camPos + vel.normalized * 100f;
        Vector3 retPoint = camPos - vel.normalized * 100f;
        Vector3 proView = _mainCam.WorldToViewportPoint(proPoint);
        Vector3 retView = _mainCam.WorldToViewportPoint(retPoint);

        bool proInFront = proView.z > 0f;
        bool retInFront = retView.z > 0f;
        SetMarkerScreen(_prograde, _proImg, _proLabel, proView, proInFront, _cachedShip.IsMatchingVelocity);
        SetMarkerScreen(_retrograde, _retImg, _retLabel, retView, retInFront, false);
    }

    void SetMarkerScreen(RectTransform rt, Image img, Text label, Vector3 viewport, bool inFront, bool emphasize)
    {
        if (!inFront) { rt.gameObject.SetActive(false); return; }
        rt.gameObject.SetActive(true);
        // viewport.x/y are 0..1; convert to canvas pixel coords. The canvas is
        // ScreenSpaceOverlay so pixel == screen coords.
        Vector2 px = new Vector2(viewport.x * Screen.width, viewport.y * Screen.height);
        rt.position = px;
        // Emphasize prograde while Match Velocity is engaged — slight scale up.
        rt.localScale = emphasize ? Vector3.one * 1.25f : Vector3.one;
    }

    void SetMarkersActive(bool active)
    {
        if (_prograde != null) _prograde.gameObject.SetActive(active);
        if (_retrograde != null) _retrograde.gameObject.SetActive(active);
    }
}
```

- [ ] **Step 2: Verify compile**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 3: User play-test**

Manual verification:
1. Pilot a ship.
2. Get the ship moving (Shift+W for a second). Expected: a green "PRO" marker appears at the screen position your velocity is pointing toward; a red "RET" appears at the opposite (or hides if behind camera).
3. Rotate the ship 90° with the mouse. The markers should track the world-space velocity direction independent of your aim.
4. Hold V (Match Velocity). The PRO marker scales up ~25% as visual confirmation the assist is engaged.
5. Stop the ship (Match Velocity to a body). When velocity is < 1 m/s, both markers hide.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/Ship/VelocityMarkersHUD.cs"
git commit -m "$(cat <<'EOF'
feat(hud): prograde + retrograde velocity markers

Cockpit overlay drawing green/red triangles at the projected
velocity vector. PRO marker scales up when Match Velocity (V) is
engaged so the assist's effect is visible.

Singleton, auto-created like GForceHUD. Hidden on foot and below
1 m/s velocity.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review

**1. Spec coverage:**
- Section 1 (Match Velocity) → Task 2 ✓
- Section 2 (Hold O Circularize) → Task 3 ✓
- Section 3 (Prograde/Retrograde HUD) → Task 4 ✓
- Section 4 (Tuning + ramp) → Task 1 ✓
- Risk note about V/O collision → addressed via spec; bindings are configurable in inspector via `matchVelocityKey` / `circularizeKey`.
- Risk note about Match Velocity drain → covered by Section 4's boostRefillPerSec bump to 0.4 (already in Task 1).

**2. Placeholder scan:** Clean — every step has concrete code or commands.

**3. Type consistency:** `IsMatchingVelocity` (bool) defined in Task 2 is read by Task 4's HUD. `FollowedShip` + `PendingHighlight` (Task 2 step 1) match the property accessor pattern used in `SolarSystemMapController`. `_smoothedThrusterInput` from Task 1 is used in Task 1 step 4 (same file, same task). All consistent.

---

**Plan complete and saved to `docs/superpowers/plans/2026-05-13-flight-revamp.md`. Two execution options:**

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**
