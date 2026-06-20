using UnityEngine;

/// <summary>
/// Slows you to a dead stop in the centre of the black hole instead of flying through and out
/// the other side. Works for BOTH the piloted ship and the on-foot astronaut.
///
/// Why this is structured the way it is (three earlier attempts failed):
///  • The on-foot <see cref="PlayerController"/> moves via rb.MovePosition(pos + smoothVelocity·dt)
///    — it IGNORES rb.velocity for motion — and its FixedUpdate runs at default order, so anything
///    we wrote to rb.velocity got both ignored and overwritten. So we run LAST
///    ([DefaultExecutionOrder] high) and drive POSITION with MovePosition, which overrides theirs.
///  • The BH's gravity grows as 1/r² and near the centre adds huge per-frame velocity, flinging you
///    through faster than any once-per-frame clamp. So we cancel the BH's own gravity inside the zone.
///
/// Within <see cref="captureRadius"/> we take over: cancel BH gravity, zero velocity, and glide the
/// body toward the centre at a speed that eases to 0 as it arrives — so it settles dead-centre and
/// holds. Outside the zone nothing is touched (the long approach still pulls you in normally).
/// </summary>
[DefaultExecutionOrder(2000)]   // run AFTER Ship and PlayerController so our MovePosition wins
public class BlackHoleCapture : MonoBehaviour
{
    PlayerController _player;        // cached on-foot player (lazy, refind only when null)
    int _playerRefindCooldown;
    CelestialBody _selfBody;         // this BH's body — for cancelling its gravity / its centre

    bool _arrived;                   // reached the middle
    float _arrivedTime;              // when we first reached it (for the delay)
    bool _triggered;                 // backrooms transition fired (guard against double-fire)

    void Awake()
    {
        _selfBody = GetComponent<CelestialBody>();
    }

    void Start()
    {
        // Pre-warm the vignette shader at scene load (hidden by the loading screen / spawn)
        // so its first-render compile doesn't hitch the FPS the moment the vignette appears
        // mid-dive. One blit with the gates on compiles the whole (single-variant) shader.
        if (vignetteMaterial != null && vignetteMaterial.shader != null)
        {
            var tmp = RenderTexture.GetTemporary(16, 16, 0);
            vignetteMaterial.SetFloat("_Intensity", 0.8f);
            vignetteMaterial.SetFloat("_KaleidoStrength", 0.5f);
            Graphics.Blit(tmp, tmp, vignetteMaterial);
            vignetteMaterial.SetFloat("_Intensity", 0f);
            vignetteMaterial.SetFloat("_KaleidoStrength", 0f);
            RenderTexture.ReleaseTemporary(tmp);
        }
    }

    void FixedUpdate()
    {
        if (!enableCapture) return;

        // Pick the body that's actually moving you: the piloted ship carries the player,
        // so capture the ship while flying; otherwise the on-foot astronaut's rigidbody.
        var ship = Ship.PilotedInstance;
        Rigidbody rb = null;
        if (ship != null) rb = ship.Rigidbody;
        else
        {
            if (_player == null && --_playerRefindCooldown <= 0)
            {
                _player = FindObjectOfType<PlayerController>();
                _playerRefindCooldown = 60;
            }
            if (_player != null) rb = _player.Rigidbody;
        }
        if (rb == null) return;

        Vector3 center = _selfBody != null ? _selfBody.Position : transform.position;
        Vector3 toCenter = center - rb.position;
        float dist = toCenter.magnitude;
        if (dist >= captureRadius) return;   // outside the zone: hands off, normal gravity/flight

        // Cancel the BH's own gravity so it can't accelerate you while we glide you in.
        if (_selfBody != null)
        {
            Vector3 radial = dist > 1e-4f ? toCenter / dist : Vector3.zero;
            float g = Universe.gravitationalConstant * _selfBody.mass / Mathf.Max(dist * dist, 1f);
            rb.AddForce(-radial * g, ForceMode.Acceleration);
        }

        // Kill momentum (and any spin) so nothing carries you past the middle...
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Auto-spin DISABLED. It used to yaw the on-foot astronaut RIGHT like an auto-mouse,
        // but that hijacked the camera so the player couldn't keep looking at / out of the hole
        // on the way in, and sweeping the bright sun/horizon through the swirl + kaleidoscope
        // fold strobed the screen badly unless you happened to be looking straight up/down. The
        // player now keeps full look control while being pulled in. (spinSpeedMax / spinStartRadius
        // are left as inspector knobs but intentionally no longer applied — re-enable here only if
        // a gentle, non-flashing spin is ever wanted.)

        // ...then glide toward the centre at a speed that eases to 0 on arrival, and HOLD there.
        // MovePosition (run last) overrides the controllers' own position writes.
        float speed = Mathf.Min(captureSpeed, dist * convergeRate);   // → 0 as dist → 0

        // Cinematic final slow-down: once inside slowdownRadius, cap the glide speed
        // down toward slowdownSpeed as you near the centre, so the warp/lensing CLIMAX
        // plays out in slow-motion instead of flashing past. The SQUARED ramp keeps the
        // outer part of the zone near full speed and concentrates the slowdown in the
        // last little bit — so the long approach never drags but the finale lingers.
        // Set slowdownRadius <= 0 to disable.
        if (slowdownRadius > 0f && dist < slowdownRadius)
        {
            float tEdge = Mathf.Clamp01(dist / slowdownRadius);       // 1 at zone edge → 0 at centre
            float cap = Mathf.Lerp(slowdownSpeed, captureSpeed, tEdge * tEdge);
            speed = Mathf.Min(speed, cap);
        }

        Vector3 next = Vector3.MoveTowards(rb.position, center, speed * Time.fixedDeltaTime);
        rb.MovePosition(next);

        // Reached the middle → after a short beat (to take in the view), fall into the Backrooms.
        if (teleportToBackroomsAtCenter && !_triggered && dist < arrivalRadius)
        {
            if (!_arrived) { _arrived = true; _arrivedTime = Time.time; }
            else if (Time.time - _arrivedTime >= delayAtCenter)
            {
                _triggered = true;
                StartCoroutine(CollapseAndEnter());
            }
        }
    }

    // ── Vision FX: groggy haze + camera shake + black-hole vignette near the core ──
    GrogginessImageEffect _grog;
    BlackHoleVignetteEffect _vign;
    Camera _fxCam;
    float _animTime;   // tesseract animation clock — accumulates faster the closer you are to the core

    void Update()
    {
        // As the capture drags you toward the core, ramp three effects that all worsen
        // the closer you get: a groggy haze + camera shake (from visionFxRadius), and a
        // swirling vignette that grows in from the edges (from the nearer vignetteRadius).
        // Runs every frame (not only inside the zone) so backing out fades them away.
        float hazeProx = 0f, vignProx = 0f;
        if (enableCapture && _selfBody != null)
        {
            Camera cam = ResolveFxCamera();
            if (cam != null)
            {
                float dist = Vector3.Distance(cam.transform.position, _selfBody.Position);
                // LINEAR ramps: 0 at the radius, 1 at the core — so each effect's
                // halfway point sits at exactly 50%.
                float hazeR = visionFxRadius > 0f ? visionFxRadius
                            : slowdownRadius > 0f ? slowdownRadius : captureRadius;
                if (hazeR > 0f) hazeProx = Mathf.Clamp01(1f - dist / hazeR);
                if (vignetteRadius > 0f) vignProx = Mathf.Clamp01(1f - dist / vignetteRadius);
            }
        }
        // Tesseract animation clock: runs at normal speed when the effect first appears and
        // accelerates toward the core, so the whole stream gets faster/more frantic as you fall in.
        float tesProx = Mathf.Clamp01(Mathf.InverseLerp(0.6f, 1f, vignProx));
        _animTime += Time.deltaTime * (1f + tesProx * 5f);
        DriveVisionFx(hazeProx, vignProx);
    }

    Camera ResolveFxCamera()
    {
        if (_fxCam != null) return _fxCam;   // Unity-null when the camera is destroyed → re-resolve
        var mgr = CameraEffectsManager.Instance;
        if (mgr != null && mgr.PlayerCamera != null) _fxCam = mgr.PlayerCamera;
        if (_fxCam == null) _fxCam = Camera.main;
        return _fxCam;
    }

    void DriveVisionFx(float hazeProx, float vignProx)
    {
        // Haze — lazily attach the groggy post effect to the active camera only when
        // needed, ease intensity toward the proximity target, and remove it once it
        // has fully faded back out (so there's no always-on pass-through blit).
        float hazeTarget = hazeProx * maxHaze;
        if (grogginessMaterial != null && (hazeTarget > 0.0001f || (_grog != null && _grog.intensity > 0.0001f)))
        {
            Camera cam = ResolveFxCamera();
            if (cam != null)
            {
                if (_grog == null)
                {
                    _grog = cam.GetComponent<GrogginessImageEffect>();
                    if (_grog == null) _grog = cam.gameObject.AddComponent<GrogginessImageEffect>();
                    _grog.material = grogginessMaterial;
                }
                _grog.intensity = Mathf.MoveTowards(_grog.intensity, hazeTarget, Time.deltaTime * 1.5f);
                if (_grog.intensity <= 0.0001f && hazeTarget <= 0.0001f) { Destroy(_grog); _grog = null; }
            }
        }

        // Vignette — same lazy-attach / ease / remove pattern; a separate component
        // so it stacks on top of the haze.
        float vignTarget = vignProx * maxVignette;
        if (vignetteMaterial != null && (vignTarget > 0.0001f || (_vign != null && _vign.intensity > 0.0001f)))
        {
            Camera cam = ResolveFxCamera();
            if (cam != null)
            {
                if (_vign == null)
                {
                    _vign = cam.GetComponent<BlackHoleVignetteEffect>();
                    if (_vign == null) _vign = cam.gameObject.AddComponent<BlackHoleVignetteEffect>();
                    _vign.material = vignetteMaterial;
                }
                _vign.intensity = Mathf.MoveTowards(_vign.intensity, vignTarget, Time.deltaTime * 1.5f);
                // Anchor the tesseract to the black hole's actual on-screen position so it
                // sits ON the core and tracks it as you move/look — not pinned to the centre.
                if (_selfBody != null && Screen.width > 0 && Screen.height > 0)
                {
                    Vector3 sp = cam.WorldToScreenPoint(_selfBody.Position);
                    if (sp.z > 0f)   // in front of the camera (keep last value if it slips behind)
                        _vign.coreUV = new Vector2(sp.x / Screen.width, sp.y / Screen.height);
                }
                _vign.animTime = _animTime;
                // Kaleidoscope warp (the mushroom-trip fold) lives inside the vignette shader,
                // so it warps the swirl + tesseract too. Starts with the vignette, eases in.
                float kProx = Mathf.SmoothStep(0f, 1f, vignProx);
                bool onFoot = Ship.PilotedInstance == null;
                // Kaleidoscope plays in BOTH modes, but in the cockpit the shader edge-fades
                // it (via _CockpitMask) so it folds the central window/space view and leaves
                // the hull frame put. Sway is a woozy CAMERA move → on-foot only (it wobbled
                // the cockpit view off the window). The cockpit mask also occludes the
                // tesseract behind the hull and makes it +50% opaque while piloting.
                _vign.kaleidoStrength = kProx * kaleidoMax;
                _vign.kaleidoWave     = kProx * kaleidoWaveMax;
                _vign.sway            = 0f;   // DISABLED: the woozy whole-frame sway fought steady looking and added to the strobe (swayMax kept as a knob but unused)
                _vign.cockpitMask     = onFoot ? 0f : 1f;
                _vign.MarkDriven();
                if (_vign.intensity <= 0.0001f && vignTarget <= 0.0001f) { Destroy(_vign); _vign = null; }
            }
        }

        // Shake — refresh a short sustained shake each frame, scaled by haze proximity.
        // CameraShake damps itself, so once we stop feeding it (left the zone or
        // teleported) it settles back to centre on its own.
        if (hazeProx > 0.001f && CameraShake.Instance != null)
            CameraShake.Instance.TriggerShake(0.2f, hazeProx * maxShakeMagnitude, hazeProx * maxShakeRoughness);
    }

    // Kill the groggy haze immediately so it can't bleed into the Backrooms if the
    // camera persists across the teleport.
    void ClearVisionFx()
    {
        if (_grog != null) { _grog.intensity = 0f; Destroy(_grog); _grog = null; }
        if (_vign != null) { _vign.intensity = 0f; Destroy(_vign); _vign = null; }
    }

    // Singularity crossing: rush the whole built-up effect into a bright white flash, then
    // load the Backrooms WHILE the screen is white so the synchronous-load hitch is hidden.
    // The vignette component then fades the flash out on the far side (auto-fade when no
    // longer driven), so the Backrooms reveals FROM the white instead of snapping in — and
    // nothing vanishes abruptly the moment you cross.
    System.Collections.IEnumerator CollapseAndEnter()
    {
        float dur = Mathf.Max(0.05f, collapseTime);
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            if (_vign != null)
            {
                float c = Mathf.Clamp01(t / dur);
                _vign.collapse = c * c;     // ease-in to white
                _vign.MarkDriven();
            }
            yield return null;
        }
        if (_vign != null) { _vign.collapse = 1f; _vign.MarkDriven(); }
        // The groggy blur is hidden under the white now; drop it so it can't bleed into the
        // Backrooms — only the vignette's white flash carries across and fades there.
        if (_grog != null) { Destroy(_grog); _grog = null; }
        yield return null;   // render one fully-white frame before the load
        PortalManager.EnterInterior(backroomsScene);
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Capture")]
    [Tooltip("Within this distance (world units) of the BH centre, the hole takes over and glides you to a dead stop in the middle. The visible event horizon is ~1500 units; keep below the orbit shell.")]
    [SerializeField] float captureRadius = 3000f;
    [Tooltip("Top glide speed (m/s) while being pulled to the centre. The pull eases below this as you near the middle.")]
    [SerializeField] float captureSpeed = 600f;
    [Tooltip("How quickly the glide eases to a stop near the centre. speed = min(captureSpeed, distance × this). Lower = gentler/longer ease-in; higher = holds speed until closer.")]
    [SerializeField] float convergeRate = 1.5f;

    [Tooltip("Master switch — uncheck to fly through normally (for comparison).")]
    [SerializeField] bool enableCapture = true;

    [Header("Backrooms")]
    [Tooltip("When you settle in the centre, fall into the Backrooms after a brief pause.")]
    [SerializeField] bool teleportToBackroomsAtCenter = true;
    [Tooltip("Interior scene to load (must be in Build Settings). Default matches the cabin's BackroomsEntrance.")]
    [SerializeField] string backroomsScene = "R1_Backrooms";
    [Tooltip("How close to the centre (world units) counts as 'arrived'.")]
    [SerializeField] float arrivalRadius = 150f;
    [Tooltip("Seconds held in the centre before the Backrooms load (lets the view land).")]
    [SerializeField] float delayAtCenter = 1.5f;
    [Tooltip("Seconds the built-up effect rushes into a bright singularity flash before the Backrooms loads. The white hides the load hitch, then the new scene reveals from it.")]
    [SerializeField] float collapseTime = 0.45f;

    [Header("Cinematic Slow-Down")]
    [Tooltip("Once within this distance (world units) of the centre, the glide eases down toward slowdownSpeed so the final warp/lensing climax plays out in slow-motion. This is the 'how EARLY does it slow' knob — keep it well inside captureRadius (2500) so the long outer approach stays fast and never feels like a boring wait. Set <= 0 to disable.")]
    [SerializeField] float slowdownRadius = 2400f;
    [Tooltip("The cinematic crawl speed (m/s) the glide eases down to right at the centre. This is the 'how SLOW does the finale get' knob — lower = slower / more dramatic.")]
    [SerializeField] float slowdownSpeed = 60f;

    [Header("Vision FX (groggy haze + shake near the core)")]
    [Tooltip("Distance (world units) from the core where the haze + shake BEGIN. They ramp LINEARLY from 0 here up to full at the centre, so the halfway point is exactly 50%. Default 4800 ≈ 2× the slow-down radius, so the FX are already ~50% by the time the slow-down kicks in.")]
    [SerializeField] float visionFxRadius = 4800f;
    [Tooltip("The 'groggy waking' blur/double-vision material (Hidden/Grogginess) — the same one the pod wake-up uses. Drag Grogginess.mat here. Haze ramps from 0 at visionFxRadius to maxHaze at the core. Leave empty to disable the haze (shake still works).")]
    [SerializeField] Material grogginessMaterial;
    [Tooltip("Max groggy-haze intensity (0-1) right at the core. Eases in (squared) from 0 at the slow-down radius, so it's subtle far out and worst at the centre.")]
    [SerializeField, Range(0f, 1f)] float maxHaze = 0.85f;
    [Tooltip("Max camera-shake magnitude at the core (CameraShake units — impacts use ~0.8). Ramps in with the same curve as the haze. Set 0 to disable shake.")]
    [SerializeField] float maxShakeMagnitude = 0.35f;
    [Tooltip("Camera-shake roughness/jitter at the core (higher = more frantic).")]
    [SerializeField] float maxShakeRoughness = 7f;

    [Header("Black-Hole Vignette (swirling band that grows from the edges)")]
    [Tooltip("Distance (world units) from the core where the swirling vignette STARTS growing in from the screen edges. Default 2400 = the midpoint, so it begins exactly when the haze/shake hit 50%. Ramps linearly to full at the core.")]
    [SerializeField] float vignetteRadius = 2400f;
    [Tooltip("The vignette material (Hidden/BlackHoleVignette). Drag BlackHoleVignette.mat here. Leave empty to disable the vignette.")]
    [SerializeField] Material vignetteMaterial;
    [Tooltip("Max vignette intensity (0-1) at the core — how far the swirling band closes in. 1 = it reaches the centre by the time you hit the core.")]
    [SerializeField, Range(0f, 1f)] float maxVignette = 1f;

    [Header("Kaleidoscope (the mushroom-trip fold — warps the whole effect)")]
    [Tooltip("Max kaleidoscope mirror strength at the core — folds the scene, swirl AND tesseract together. Starts with the vignette and eases in with proximity. 0 disables it.")]
    [SerializeField, Range(0f, 1f)] float kaleidoMax = 0.8f;
    [Tooltip("Max wavy heat-haze shimmer strength at the core.")]
    [SerializeField, Range(0f, 1f)] float kaleidoWaveMax = 0.4f;
    [Tooltip("Max woozy camera-sway (slow whole-frame roll + drift) at the core. Ramps in with proximity. 0 disables it.")]
    [SerializeField, Range(0f, 1f)] float swayMax = 1f;
    [Tooltip("Auto-spin rate (radians/sec) at the core — turns the ON-FOOT astronaut (body + camera) RIGHT like an auto-mouse, ramping cubically to here. Never spins the ship. ~6.28 = one full turn/sec. 0 disables it.")]
    [SerializeField] float spinSpeedMax = 2.5f;
    [Tooltip("Distance (world units) from the core where the on-foot spin STARTS. Smaller = kicks in later/closer. Keep it well inside the vignette radius so the spin only sweeps a darkened periphery (otherwise it flashes the bright sun/horizon past the view).")]
    [SerializeField] float spinStartRadius = 1000f;
}
