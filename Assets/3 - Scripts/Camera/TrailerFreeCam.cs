using UnityEngine;
using UnityEngine.SceneManagement;

// ─────────────────────────────────────────────────────────────────────────────
// Trailer cinematic camera. Press P to drop player control; press P again to hand
// it back. Built for recording trailer footage of the BUILD (OBS), so it is pure
// runtime input — no editor-only code — and is build-safe via the auto-singleton +
// EnsureGameplaySingletons seed pattern (CLAUDE.md trap #1).
//
// Two modes, picked automatically at toggle-on:
//   • ORBIT  — used while the stasis-pod descent is playing. The camera auto-spins
//              around the pod (it stays framed in centre); the MOUSE steers the
//              orbit angle + tilt, W/S zoom in/out, Shift spins/zooms faster.
//   • FREE-FLY — used otherwise (e.g. standing in the cabin after the wake-up):
//              mouse look, WASD move, Space/Ctrl up/down, Shift faster, scroll trims
//              speed. Speed scales with distance to the nearest planet surface.
//
// ── Floating origin (CLAUDE.md / EndlessManager): anchor-relative ─────────────
// EndlessManager rebases the world off playerRigidbody.position. Both modes
// recompute the camera's world pose every LateUpdate (order 200) from an anchor that
// is ALWAYS already rebased (orbit: the pod/player centre; free-fly: player + stored
// offset), so origin shifts are invisible in the render — no snapping. We ALSO register
// the camera with EndlessManager so a rebase shifts it in the SAME step as the world;
// otherwise order-0 readers (the glow-dust field) see the camera one rebase behind the
// black hole on a shift frame and visibly snap. The order-200 re-derivation overwrites
// the shifted value cleanly, so registration is safe. We borrow the REAL camera, never
// a second one (that would lose the atmosphere/ocean/post stack — trap #2).
//
// While active we also disable CameraTransformFX, disable PlayerController (freezes
// WASD/look — it moves via rb.MovePosition so the component must be off), freeze the
// rigidbody, hide the HUD, disable the grogginess blur, and re-show the astronaut
// body — all cached and restored exactly on toggle-off. During the descent
// PodArrivalSequence pins the pod to the player anchor whenever Active is true.
// ─────────────────────────────────────────────────────────────────────────────
[DefaultExecutionOrder(200)]   // after EndlessManager (0), CameraTransformFX (100), the pod sequence
public class TrailerFreeCam : MonoBehaviour
{
    public static TrailerFreeCam Instance { get; private set; }
    public static bool Active { get; private set; }

    enum Mode { FreeFly, Orbit }

    [Header("Orbit (pod turntable) — mouse steers, W/S zoom")]
    [SerializeField] float orbitAutoSpinSpeed = 5f;    // deg/sec automatic spin (3x slower)
    [SerializeField] float orbitMouseSens     = 2f;    // mouse → orbit angle (halved: was too touchy)
    [SerializeField] float orbitMinElevation  = -80f;
    [SerializeField] float orbitMaxElevation  = 80f;
    [SerializeField] float orbitZoomRate      = 0.5f;  // fraction of radius per sec, per W/S (3x slower; proportional: slow near the pod, fast far out)
    [SerializeField] float orbitZoomAccel     = 2f;    // how fast the zoom input eases in/out (lower = smoother ramp, no jerk)
    [SerializeField] float orbitMinRadius     = 2f;
    [SerializeField] float orbitMaxRadius     = 400f;
    [SerializeField] float orbitDefaultRadius = 10f;   // used if the camera starts ~on the pod
    [SerializeField] float orbitSmoothing     = 6f;    // lower = softer radius ease (smoother accel/decel)

    [Header("Free-fly — speed scales with distance to nearest planet surface")]
    [SerializeField] float lookSensitivity = 1.5f;     // halved: was too touchy
    [SerializeField] float lookSmoothing   = 18f;
    [SerializeField] float speedPerMeter = 0.27f;   // 3x slower
    [SerializeField] float nearSpeed = 3.3f;        // 3x slower
    [SerializeField] float farSpeed  = 833f;        // 3x slower
    [SerializeField] float fastMultiplier = 4f;
    [SerializeField] float moveSmoothing  = 2f;     // lower = gentler/longer ease-in + ease-out on WASD
    [SerializeField] float scrollSpeedStep = 0.1f;
    [SerializeField] float minSpeedMult = 0.1f;
    [SerializeField] float maxSpeedMult = 10f;

    [Header("Toggle")]
    [SerializeField] KeyCode toggleKey = KeyCode.P;

    // ── Runtime ──────────────────────────────────────────────────────────────
    Mode _mode;
    PlayerController _pc;
    Rigidbody _rb;
    Camera _cam;
    Transform _camT;
    Transform _anchor;            // the player; the pod is pinned here during the descent
    PodArrivalSequence _pod;      // non-null + IsActive while the descent plays
    CameraTransformFX _camFx;
    GrogginessImageEffect _grog;
    EndlessManager _endless;      // registered so the camera rebases IN-STEP with the world

    // free-fly state
    Vector3 _offset;              // camera pos relative to the anchor (origin-shift invariant)
    float _yaw, _pitch, _smoothYaw, _smoothPitch;
    Vector3 _vel;
    float _speedMult = 1f;

    // orbit state
    float _azimuth, _elevation;
    float _radius, _radiusTarget;
    float _zoomVel;               // eased zoom input (-1..1) so W/S ramps in instead of jerking

    // cached restore state
    Transform _origParent;
    Vector3 _origLocalPos;
    Quaternion _origLocalRot;
    bool _pcWasEnabled;
    bool _rbWasKinematic;
    bool _camFxWasEnabled;
    bool _grogWasEnabled;
    bool _hudWasHidden;
    CursorLockMode _cursorLockWas;
    bool _cursorVisibleWas;
    Renderer[] _shownBodyRenderers;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;   // seeded by EnsureGameplaySingletons in builds
        var go = new GameObject("TrailerFreeCam");
        DontDestroyOnLoad(go);
        go.AddComponent<TrailerFreeCam>();
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

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            if (!Active) Enter();
            else Exit();
        }
    }

    // Force back to the normal player camera (e.g. the pod crash hands off to the
    // cabin wake-up). No-op if not currently active; P re-enables it afterwards.
    public static void ForceExit()
    {
        if (Instance != null && Active) Instance.Exit();
    }

    void LateUpdate()
    {
        if (!Active) return;
        if (_camT == null || _anchor == null) { Exit(); return; }

        float dt = Time.unscaledDeltaTime;
        if (_mode == Mode.Orbit) TickOrbit(dt);
        else TickFreeFly(dt);
    }

    // ── ORBIT: auto-spin turntable; mouse steers, W/S zoom, pod stays centred ───
    void TickOrbit(float dt)
    {
        bool fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float spinMult = fast ? fastMultiplier : 1f;

        // Auto-spin + mouse steering.
        _azimuth   += orbitAutoSpinSpeed * spinMult * dt;
        _azimuth   += Input.GetAxisRaw("Mouse X") * orbitMouseSens;
        _elevation -= Input.GetAxisRaw("Mouse Y") * orbitMouseSens;
        _elevation  = Mathf.Clamp(_elevation, orbitMinElevation, orbitMaxElevation);

        // W = zoom in, S = zoom out. The input is EASED (ramps in/out, no jerk on
        // keypress) and the rate is proportional to the current radius, so the camera
        // crawls when close to the pod and moves fast when far out.
        float zoomInput = 0f;
        if (Input.GetKey(KeyCode.W)) zoomInput -= 1f;
        if (Input.GetKey(KeyCode.S)) zoomInput += 1f;
        _zoomVel = Mathf.Lerp(_zoomVel, zoomInput, 1f - Mathf.Exp(-dt * orbitZoomAccel));
        _radiusTarget = Mathf.Clamp(_radiusTarget * (1f + _zoomVel * orbitZoomRate * spinMult * dt),
                                    orbitMinRadius, orbitMaxRadius);
        _radius = Mathf.Lerp(_radius, _radiusTarget, 1f - Mathf.Exp(-dt * orbitSmoothing));

        // Centre on the actual pod if we have it, else the anchor (they coincide
        // during the descent — the pod is pinned to the player while Active).
        Vector3 center = (_pod != null && _pod.PodTransform != null) ? _pod.PodTransform.position : _anchor.position;

        float az = _azimuth * Mathf.Deg2Rad, el = _elevation * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
        _camT.position = center + dir * _radius;
        _camT.rotation = Quaternion.LookRotation(center - _camT.position, Vector3.up);
    }

    // ── FREE-FLY: mouse look + WASD, proximity-scaled speed ─────────────────────
    void TickFreeFly(float dt)
    {
        _yaw   += Input.GetAxisRaw("Mouse X") * lookSensitivity;
        _pitch -= Input.GetAxisRaw("Mouse Y") * lookSensitivity;
        _pitch  = Mathf.Clamp(_pitch, -89f, 89f);
        float lookT = 1f - Mathf.Exp(-dt * lookSmoothing);
        _smoothYaw   = Mathf.LerpAngle(_smoothYaw, _yaw, lookT);
        _smoothPitch = Mathf.LerpAngle(_smoothPitch, _pitch, lookT);
        _camT.rotation = Quaternion.Euler(_smoothPitch, _smoothYaw, 0f);

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.001f)
            _speedMult = Mathf.Clamp(_speedMult + scroll * scrollSpeedStep, minSpeedMult, maxSpeedMult);
        float surfaceDist = DistanceToNearestSurface(_anchor.position + _offset);
        float speed = Mathf.Clamp(surfaceDist * speedPerMeter, nearSpeed, farSpeed) * _speedMult;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) speed *= fastMultiplier;

        Vector3 local = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) local.z += 1f;
        if (Input.GetKey(KeyCode.S)) local.z -= 1f;
        if (Input.GetKey(KeyCode.D)) local.x += 1f;
        if (Input.GetKey(KeyCode.A)) local.x -= 1f;
        Vector3 worldMove = _camT.rotation * local;
        if (Input.GetKey(KeyCode.Space)) worldMove += Vector3.up;
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) worldMove += Vector3.down;
        if (worldMove.sqrMagnitude > 1f) worldMove.Normalize();

        Vector3 desiredVel = worldMove * speed;
        _vel = Vector3.Lerp(_vel, desiredVel, 1f - Mathf.Exp(-dt * moveSmoothing));
        _offset += _vel * dt;
        _camT.position = _anchor.position + _offset;
    }

    static float DistanceToNearestSurface(Vector3 worldPos)
    {
        var bodies = NBodySimulation.Bodies;
        float nearest = float.MaxValue;
        if (bodies != null)
            for (int i = 0; i < bodies.Length; i++)
            {
                var b = bodies[i];
                if (b == null) continue;
                float d = Vector3.Distance(worldPos, b.Position) - b.radius;
                if (d < nearest) nearest = d;
            }
        if (nearest == float.MaxValue) nearest = 100000f;
        return Mathf.Max(0f, nearest);
    }

    // Nearest celestial body (by surface distance) to a world point — the free-cam
    // anchors to it so the camera rides that planet's orbit and stays put relative to it.
    static CelestialBody NearestBody(Vector3 worldPos)
    {
        var bodies = NBodySimulation.Bodies;
        CelestialBody best = null;
        float bestD = float.MaxValue;
        if (bodies != null)
            for (int i = 0; i < bodies.Length; i++)
            {
                var b = bodies[i];
                if (b == null) continue;
                float d = Vector3.Distance(worldPos, b.Position) - b.radius;
                if (d < bestD) { bestD = d; best = b; }
            }
        return best;
    }

    // ── Enter ───────────────────────────────────────────────────────────────────
    void Enter()
    {
        _pc = FindObjectOfType<PlayerController>();
        if (_pc == null) return;

        var mgr = CameraEffectsManager.Instance;
        _cam = (mgr != null && mgr.PlayerCamera != null) ? mgr.PlayerCamera : _pc.Camera;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        _camT = _cam.transform;
        _rb = _pc.Rigidbody;
        _anchor = _pc.transform;

        // Mode: orbit the pod while the descent plays, free-fly otherwise.
        _pod = FindObjectOfType<PodArrivalSequence>();
        _mode = (_pod != null && _pod.IsActive) ? Mode.Orbit : Mode.FreeFly;

        // Free-fly: anchor to the NEAREST planet (not the player). The player is frozen
        // in world space, but planets keep orbiting the sun — anchoring to the player
        // would let the planet drift away. Anchoring to the planet makes the camera ride
        // its orbit, so it sits practically stationary relative to the planet until you
        // move it. Orbit mode keeps the player anchor (the pod is pinned there).
        if (_mode == Mode.FreeFly)
        {
            var nb = NearestBody(_camT.position);
            if (nb != null) _anchor = nb.transform;
        }

        // Cache restore state.
        _origParent   = _camT.parent;
        _origLocalPos = _camT.localPosition;
        _origLocalRot = _camT.localRotation;
        _pcWasEnabled  = _pc.enabled;
        _rbWasKinematic = _rb != null && _rb.isKinematic;
        _camFx = FindObjectOfType<CameraTransformFX>();
        _camFxWasEnabled = _camFx != null && _camFx.enabled;
        _grog = _cam.GetComponent<GrogginessImageEffect>();
        _grogWasEnabled = _grog != null && _grog.enabled;
        _hudWasHidden = HudVisibility.Hidden;
        _cursorLockWas = Cursor.lockState;
        _cursorVisibleWas = Cursor.visible;

        _camT.SetParent(null, true);

        // Register the detached camera with EndlessManager so an origin rebase shifts
        // it IN THE SAME STEP as the black hole / planets. Without this, anything that
        // reads the camera at order 0 (the glow-dust field) sees the camera one rebase
        // behind the world on a shift frame → the dust skips a parallax update and snaps
        // back. We still re-derive the camera's render pose from the anchor every
        // LateUpdate (order 200), which overwrites the shifted value cleanly — so this
        // does NOT reintroduce the old absolute-position snap.
        _endless = FindObjectOfType<EndlessManager>();
        if (_endless != null) _endless.RegisterPhysicsObject(_camT);

        // Suspend the systems that would fight us.
        if (_camFx != null) _camFx.enabled = false;
        _pc.enabled = false;
        if (_rb != null) _rb.isKinematic = true;
        if (_grog != null) _grog.enabled = false;
        HudVisibility.SetForceHidden(true);
        ShowAstronautBody();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        SpaceDustField.CenterOverride = _camT;   // glow-dust field follows the free-cam

        if (_mode == Mode.Orbit) InitOrbitState();
        else InitFreeFlyState();

        Active = true;
    }

    void InitOrbitState()
    {
        Vector3 center = (_pod != null && _pod.PodTransform != null) ? _pod.PodTransform.position : _anchor.position;
        Vector3 rel = _camT.position - center;
        float r = rel.magnitude;
        _radius = _radiusTarget = Mathf.Clamp(r < 0.1f ? orbitDefaultRadius : r, orbitMinRadius, orbitMaxRadius);
        Vector3 d = r < 0.0001f ? Vector3.back : rel / r;
        _elevation = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
        _azimuth   = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        _zoomVel = 0f;
    }

    void InitFreeFlyState()
    {
        _offset = _camT.position - _anchor.position;
        Vector3 e = _camT.eulerAngles;
        _yaw = e.y;
        _pitch = e.x > 180f ? e.x - 360f : e.x;
        _smoothYaw = _yaw; _smoothPitch = _pitch;
        _vel = Vector3.zero;
    }

    // ── Exit (restores everything cached in Enter) ──────────────────────────────
    void Exit()
    {
        Active = false;

        SpaceDustField.CenterOverride = null;   // hand the glow-dust field back to the player cam

        if (_endless != null && _camT != null) _endless.UnregisterPhysicsObject(_camT);

        if (_camT != null)
        {
            _camT.SetParent(_origParent, true);
            _camT.localPosition = _origLocalPos;
            _camT.localRotation = _origLocalRot;
        }

        if (_camFx != null) _camFx.enabled = _camFxWasEnabled;
        if (_pc != null) _pc.enabled = _pcWasEnabled;
        if (_rb != null) _rb.isKinematic = _rbWasKinematic;
        if (_grog != null) _grog.enabled = _grogWasEnabled;
        HudVisibility.SetForceHidden(_hudWasHidden);
        HideAstronautBody();
        Cursor.lockState = _cursorLockWas;
        Cursor.visible = _cursorVisibleWas;

        _pc = null; _rb = null; _cam = null; _camT = null; _anchor = null; _pod = null; _camFx = null; _grog = null; _endless = null;
    }

    void ShowAstronautBody()
    {
        _shownBodyRenderers = null;
        if (_pc == null) return;
        Transform astro = _pc.transform.Find("Astronaut");
        if (astro == null) return;
        var rends = astro.GetComponentsInChildren<Renderer>(true);
        var turnedOn = new System.Collections.Generic.List<Renderer>();
        foreach (var r in rends)
            if (r != null && !r.enabled) { r.enabled = true; turnedOn.Add(r); }
        _shownBodyRenderers = turnedOn.ToArray();
    }

    void HideAstronautBody()
    {
        if (_shownBodyRenderers != null)
            foreach (var r in _shownBodyRenderers)
                if (r != null) r.enabled = false;
        _shownBodyRenderers = null;
    }
}
