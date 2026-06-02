using System.Collections;
using UnityEngine;

public class PlayerFlashlight : MonoBehaviour
{
    [Header("Light")]
    [Tooltip("The Light component on the player that acts as the flashlight. If left empty, an existing child Spot light is auto-found.")]
    public Light flashlight;
    [Tooltip("Toggle key.")]
    public KeyCode toggleKey = KeyCode.E;
    [Tooltip("Optional light cookie texture applied to the spot light at Start. Leave null for no cookie.")]
    public Texture lightCookie;

    [Header("Brightness")]
    // Single fixed brightness. Used to be a scroll-wheel-adjustable range
    // (min..max) but the player-side control was removed — the flashlight
    // now runs at this value continuously, with the flicker / dying-battery
    // multipliers riding on top.
    public float minBrightness = 0.165f;

    [Header("Cone Shape")]
    [Tooltip("Outer cone angle in degrees — the edge of the projected light. Wider = more area covered but lower density.")]
    [Range(10f, 175f)] public float outerSpotAngle = 150f;
    [Tooltip("Inner cone angle in degrees — inside this, intensity is at full brightness; from here out to outerSpotAngle, intensity falls off softly to zero. Smaller inner vs outer = smaller hotspot, wider halo edge — the FPS flashlight look.")]
    [Range(0f, 120f)] public float innerSpotAngle = 8f;
    [Tooltip("Effective range of the spot light in metres. Longer = reaches further but slightly dimmer per surface (inverse-square).")]
    public float range = 200f;
    [Tooltip("Generate a procedural cookie texture at Start that paints a bright hotspot + soft halo ring onto illuminated surfaces. Independent of the inner/outer cone angles — gives the visible 'bright dot with a glowing ring' that real flashlights have.")]
    public bool useProceduralCookie = true;
    [Tooltip("Cookie resolution. 256 is plenty for a soft radial gradient; bigger wastes texture memory.")]
    [Range(64, 1024)] public int cookieResolution = 256;
    [Tooltip("Width of the halo ring as a fraction of the cookie radius. 0.7 puts the halo at 70% of the cone radius.")]
    [Range(0.3f, 0.95f)] public float cookieHaloPosition = 0.607f;
    [Tooltip("How strong the halo ring is compared to the central hotspot. 0 = no halo (smooth Gaussian falloff), 0.5 = subtle ring, 1 = pronounced ring.")]
    [Range(0f, 1f)] public float cookieHaloStrength = 0.35f;

    [Header("Halo")]
    public LensFlare halo;
    public float haloIntensityMultiplier = 1f;

    [Header("Flicker")]
    public bool enableFlicker = true;
    [Range(0f, 0.5f)] public float microDriftAmount = 0.04f;
    public Vector2 panicStutterInterval = new Vector2(6f, 10f);
    public Vector2 dyingDipInterval = new Vector2(35f, 55f);

    [Header("Sway")]
    public bool enableSway = true;
    [Tooltip("Peak rotation in degrees applied at full walking speed.")]
    public float walkBobAmplitude = 2f;
    public float walkBobFrequency = 7f;
    [Tooltip("Speed at which walk factor reaches 1.0 (m/s).")]
    public float walkingTopSpeed = 4f;

    // 3-mode toggle (E cycles Off → Half → Full → Off). Half = 50% of
    // minBrightness, Full = 100%. enabled/disabled mirrors the mode.
    public enum Mode { Off = 0, Half = 1, Full = 2 }

    // --- runtime state ---
    Ship _ship;
    float _baseIntensity;            // fixed at minBrightness; multipliers ride on top
    float _stutterMultiplier = 1f;   // 0..1 set by panic coroutine
    float _dyingMultiplier = 1f;     // 0..1 set by dying-battery coroutine
    Mode _mode = Mode.Off;
    Coroutine _panicRoutine;
    Coroutine _dyingRoutine;
    PlayerController _player;
    Quaternion _baseLocalRot;
    float _walkFactor;
    float _walkVel;

    void Start()
    {
        if (flashlight == null)
        {
            var lights = GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].type == LightType.Spot)
                {
                    flashlight = lights[i];
                    break;
                }
            }
        }
        if (flashlight != null)
        {
            flashlight.enabled = false;
            // Force per-pixel rendering so the spotlight's cone stays tight.
            // Without this, Unity's built-in pipeline can demote the flashlight
            // to vertex shading at certain camera angles when other scene lights
            // (sun, ambient caster, bonfires) push it out of the limited
            // pixel-light budget — the demotion makes the focused cone look
            // like a flat wash spread over a wide area.
            flashlight.renderMode = LightRenderMode.ForcePixel;
            // Cookie precedence: a manually-assigned lightCookie always wins.
            // Otherwise generate a procedural one if enabled. The procedural
            // cookie is what actually makes the "bright dot + halo" effect
            // visible on illuminated surfaces — innerSpotAngle gives a smooth
            // falloff but not a distinct halo *ring*, which is what real-
            // world LED flashlights project (and what FPS games imitate).
            if (lightCookie != null) flashlight.cookie = lightCookie;
            else if (useProceduralCookie) flashlight.cookie = BuildProceduralCookie();
            ApplyConeShape();
            _baseIntensity = minBrightness;
        }
        _ship = FindObjectOfType<Ship>(true);
        _player = FindObjectOfType<PlayerController>(true);
        // Bake a 10° downward pitch into the cached base rotation so the cone
        // aims slightly toward the ground at rest. Sway in Update multiplies
        // onto _baseLocalRot, so the small walk bob still oscillates around
        // this tilted resting pose instead of the scene-authored level pose.
        if (flashlight != null) _baseLocalRot = flashlight.transform.localRotation * Quaternion.Euler(10f, 0f, 0f);

        SetVisualLayersVisible(false);
    }

    void Update()
    {
        if (flashlight == null) return;
        // While typing in the phone's AI chat, E (the flashlight toggle key)
        // must not also cycle the flashlight mode.
        if (AIChatScreen.IsTypingActive) return;

        bool piloting = _ship != null && _ship.IsPiloted;
        if (piloting)
        {
            if (flashlight.enabled)
            {
                flashlight.enabled = false;
                SetVisualLayersVisible(false);
            }
            return;
        }

        // Configurable keyboard key OR controller Y button. Cycles
        // Off → Half (50%) → Full (100%) → Off.
        if (TutorialGate.GetKeyDown(toggleKey, TutorialAbility.Flashlight) ||
            TutorialGate.FlashlightPressed(TutorialAbility.Flashlight))
        {
            CycleMode();
        }

        if (!flashlight.enabled) return;

        // Brightness is fixed. Resync each tick so an inspector change to
        // minBrightness during Play mode takes effect immediately.
        _baseIntensity = minBrightness;

        // Continuous tiny shimmer — "the bulb is alive."
        float micro = enableFlicker
            ? 1f - microDriftAmount * (Mathf.PerlinNoise(Time.time * 3f, 0f) - 0.5f)
            : 1f;

        float modeMultiplier = (_mode == Mode.Half) ? 0.5f : 1f;
        float finalIntensity = _baseIntensity * micro * modeMultiplier * _stutterMultiplier * _dyingMultiplier;
        flashlight.intensity = finalIntensity;
        if (halo != null) halo.brightness = finalIntensity * haloIntensityMultiplier;

        // Walk-driven sway. SurfaceVelocity is the correct source (rb.velocity
        // misses MovePosition-driven walking; see PlayerController.cs:794).
        if (enableSway && _player != null)
        {
            float speed = _player.SurfaceVelocity.magnitude;
            float target = Mathf.Clamp01(speed / Mathf.Max(0.01f, walkingTopSpeed));
            _walkFactor = Mathf.SmoothDamp(_walkFactor, target, ref _walkVel, 0.15f);
            float pitch = Mathf.Sin(Time.time * walkBobFrequency) * walkBobAmplitude * _walkFactor;
            float yaw = Mathf.Sin(Time.time * walkBobFrequency * 0.5f) * walkBobAmplitude * 0.6f * _walkFactor;
            flashlight.transform.localRotation = _baseLocalRot * Quaternion.Euler(pitch, yaw, 0f);
        }
        else
        {
            flashlight.transform.localRotation = _baseLocalRot;
        }

    }

    /// <summary>
    /// Save-system entry point. Brightness is now fixed at minBrightness, so
    /// we ignore the saved value and clamp to the current minimum. Kept as a
    /// public no-op-ish hook so SaveCollector.ApplyFlashlight continues to
    /// compile and older saves with a stored intensity load without error.
    /// </summary>
    public void ApplyBaseIntensity(float v)
    {
        _baseIntensity = minBrightness;
    }

    public Mode CurrentMode => _mode;

    public void ApplyMode(Mode m)
    {
        _mode = m;
        if (flashlight != null)
        {
            flashlight.enabled = (m != Mode.Off);
            SetVisualLayersVisible(flashlight.enabled);
        }
    }

    void CycleMode()
    {
        Mode next = _mode switch
        {
            Mode.Off  => Mode.Half,
            Mode.Half => Mode.Full,
            _         => Mode.Off,
        };
        ApplyMode(next);
    }

    // Builds a square RGBA cookie texture with FOUR concentric zones, radially
    // symmetric:
    //   1. Bright center spot     (r=0  → 0.15)    : full bright
    //   2. Dim gap / dark band    (~0.15 → ~0.45)  : low — creates the visible
    //                                                 "shadow" gap that makes
    //                                                 the inner dot read as a
    //                                                 distinct circle.
    //   3. Bright halo ring       (~0.45 → ~0.70)  : second peak — the visible
    //                                                 glowing band around the
    //                                                 inner spot.
    //   4. Fade out to edge       (~0.70 → 1)      : smooth tail to zero.
    // The four-zone shape is what makes a real LED flashlight beam look
    // distinct from a single Gaussian — there's a physical gap between the
    // reflector hotspot and the outer beam spill.
    Texture2D BuildProceduralCookie()
    {
        int res = Mathf.Clamp(cookieResolution, 64, 1024);
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, mipChain: true, linear: false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "FlashlightCookie_Procedural",
        };

        float cx = (res - 1) * 0.5f;
        float cy = (res - 1) * 0.5f;
        float maxR = (res - 1) * 0.5f;
        float haloPos = Mathf.Clamp01(cookieHaloPosition);

        // Center + halo σ values scaled by 130°/150° = 0.867 from the earlier
        // (cone=130°) tuning. The cone widened to 150° to give the outer fade
        // ~2× ground coverage, but the user wanted the inner pattern (bright
        // dot + dim ring + halo) to stay the same world size. Scaling cookie
        // parameters by the inverse of the cone widening keeps inner world
        // angles invariant — only the outer fade zone grows.
        const float centerSigma = 0.260f;  // was 0.30 at cone=130°
        float centerDenom = 2f * centerSigma * centerSigma;

        const float haloSigma  = 0.069f;   // was 0.08 at cone=130°
        float haloDenom = 2f * haloSigma * haloSigma;

        // Background lift inside the dim gap. Bumped from 0.08 → 0.15 → 0.22
        // so the band between the (now larger) center hotspot and the halo
        // ring isn't pitch-dark — feels like real beam spill instead of a
        // hard ring on black. The latest bump also fills the outer fringe
        // with visible (but faint) light, which combined with the softer
        // edgeFade curve gives the "glow around the main cone" look.
        const float gapFloor = 0.22f;

        var pixels = new Color32[res * res];
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dx = (x - cx) / maxR;
                float dy = (y - cy) / maxR;
                float r = Mathf.Sqrt(dx * dx + dy * dy);

                // Hard zero outside the cookie disc — respects the cone edge.
                if (r > 1f) { pixels[y * res + x] = new Color32(0, 0, 0, 0); continue; }

                // Zone 1: bright center spot.
                float center = Mathf.Exp(-(r * r) / centerDenom);

                // Zone 3: bright halo ring at haloPos.
                float d = r - haloPos;
                float halo = Mathf.Exp(-(d * d) / haloDenom) * cookieHaloStrength;

                // Zone 4: two-piece edge fade. The old single smoothstep
                // from r=0.477 to r=1.0 dropped to ~50% by r=0.65 and ~15%
                // by r=0.9, which combined with Unity's angular falloff in
                // the outer cone made the spill zone too dim to actually
                // see in the dark. The new curve holds at high brightness
                // (linear 1.0→0.55) across most of the fade zone so the
                // peripheral glow is visible, then drops smoothly to 0 in
                // the final 15% of the cookie radius to keep a clean dark
                // rim. The plateau value at r=0.607 (halo position) lands
                // at ~0.84 — within 1% of what the old smoothstep gave,
                // so the bright halo ring still reads as it did.
                float edgeFade;
                if (r <= 0.85f)
                {
                    edgeFade = Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(0.477f, 0.85f, r));
                }
                else
                {
                    float t = Mathf.InverseLerp(0.85f, 1f, r);
                    edgeFade = 0.55f * (1f - t * t * (3f - 2f * t));
                }

                // Zone 2 emerges naturally from the sum: in the band between
                // ~0.15 and the halo position, both the center Gaussian and
                // the halo Gaussian have decayed, and only gapFloor remains
                // → the dim-gap band that creates the inner-dot silhouette.
                float v = Mathf.Clamp01((center + halo + gapFloor) * edgeFade);

                byte b = (byte)(v * 255f);
                pixels[y * res + x] = new Color32(b, b, b, b);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        return tex;
    }

    // Pushes the inspector cone-shape values onto the Light. Inner < Outer
    // gives a bright hotspot in the middle with a soft falloff at the edge —
    // the typical FPS flashlight look. Unity's built-in pipeline interpolates
    // intensity from full at innerSpotAngle down to zero at spotAngle.
    void ApplyConeShape()
    {
        if (flashlight == null) return;
        flashlight.type = LightType.Spot;
        float outer = Mathf.Clamp(outerSpotAngle, 1f, 175f);
        // Inner must be strictly less than outer or the falloff band collapses
        // to zero width (= hard edge, no halo). Clamp to outer-1 just in case
        // the user sets them equal in the inspector.
        float inner = Mathf.Clamp(innerSpotAngle, 0f, outer - 1f);
        flashlight.spotAngle = outer;
        flashlight.innerSpotAngle = inner;
        flashlight.range = Mathf.Max(0.1f, range);
    }

    void OnValidate()
    {
        // Live-tune the cone while in Play mode — drag the sliders, see the
        // hotspot / halo change in the Scene view immediately.
        if (flashlight != null) ApplyConeShape();
    }

    void SetVisualLayersVisible(bool visible)
    {
        if (halo != null) halo.enabled = visible;
    }

    void OnEnable()
    {
        _panicRoutine = StartCoroutine(PanicStutterLoop());
        _dyingRoutine = StartCoroutine(DyingBatteryLoop());
    }

    void OnDisable()
    {
        if (_panicRoutine != null) StopCoroutine(_panicRoutine);
        if (_dyingRoutine != null) StopCoroutine(_dyingRoutine);
        _panicRoutine = null;
        _dyingRoutine = null;
        _stutterMultiplier = 1f;
        _dyingMultiplier = 1f;
    }

    // Rare hard glitch — 6–10s gaps between events. Only fires while the
    // flashlight is enabled (skipped otherwise so the player doesn't see
    // flicker on a powered-off torch). Total event duration ~120ms.
    IEnumerator PanicStutterLoop()
    {
        while (true)
        {
            float wait = Random.Range(panicStutterInterval.x, panicStutterInterval.y);
            yield return new WaitForSeconds(wait);
            if (!enableFlicker || flashlight == null || !flashlight.enabled) continue;

            // 1f @ 0.20, 1f @ 1.00, 1f @ 0.40, 1f @ 1.00, 2f @ 0.15, then restore.
            _stutterMultiplier = 0.20f; yield return null;
            _stutterMultiplier = 1.00f; yield return null;
            _stutterMultiplier = 0.40f; yield return null;
            _stutterMultiplier = 1.00f; yield return null;
            _stutterMultiplier = 0.15f; yield return null; yield return null;
            _stutterMultiplier = 1.00f;
        }
    }

    // Occasional slow sag toward darkness then recovery — 35–55s gaps.
    // 1.8s sweep from 1.0 -> 0.45 -> 1.0 with tiny Perlin jitter at the trough.
    IEnumerator DyingBatteryLoop()
    {
        while (true)
        {
            float wait = Random.Range(dyingDipInterval.x, dyingDipInterval.y);
            yield return new WaitForSeconds(wait);
            if (!enableFlicker || flashlight == null || !flashlight.enabled) continue;

            const float duration = 1.8f;
            float t0 = Time.time;
            while (Time.time - t0 < duration)
            {
                float u = (Time.time - t0) / duration;     // 0..1
                // Symmetric V-curve: 1.0 at edges, 0.45 at center, with jitter near trough.
                float v = 1f - Mathf.Sin(u * Mathf.PI) * 0.55f;
                float jitter = (Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f) * 0.10f * Mathf.Sin(u * Mathf.PI);
                _dyingMultiplier = Mathf.Clamp01(v + jitter);
                yield return null;
            }
            _dyingMultiplier = 1f;
        }
    }
}
