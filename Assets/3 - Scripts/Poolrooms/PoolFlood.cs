using UnityEngine;

// Poolrooms flood. Once you arrive in the level the pool water plane creeps up from
// the floor — slowly, and it never stops — until it's over your head. While the
// camera is below the surface it drives a murky underwater wash
// (UnderwaterImageEffect) and swaps the scene fog to dense green-blue, so as the
// water rises it actually reads as the place FLOODING rather than a flat sheet.
//
// Detection here is a flat-plane world-Y compare (submerged = camY < surfaceY), so it
// is completely decoupled from the planet-ocean sphere water system in
// PlayerController — no interference with buoyancy/swim on the planet.
//
// Put this on the "Water" plane. The plane must NOT be Batching-Static (static-batched
// meshes don't move at runtime); PoolFlood clears the static flag defensively on start.
public class PoolFlood : MonoBehaviour
{
    // Current world-Y of the rising surface. NegativeInfinity when no flood is active.
    public static float SurfaceY = float.NegativeInfinity;
    public static bool Active = false;

    [Header("Rise")]
    [Tooltip("Seconds after the level loads before the water starts creeping up.")]
    public float startDelay = 2f;
    [Tooltip("How fast the surface rises (metres/second). 0.13 ≈ 8 m over ~60s.")]
    public float riseSpeed = 0.13f;
    [Tooltip("How far above the starting surface the water climbs before it stops (metres). Keep well above head height so it fully submerges the player.")]
    public float riseHeight = 8f;

    [Header("Underwater look")]
    [Tooltip("Murky water colour — used for BOTH the fog and the screen tint when submerged.")]
    public Color underwaterColor = new Color(0.10f, 0.34f, 0.32f, 1f);
    [Tooltip("Exp² fog density while fully submerged. Higher = murkier / shorter view. ~0.12–0.25 ≈ 5–10 m visibility.")]
    public float fogDensity = 0.14f;
    [Tooltip("Metres below the surface over which the wash fades fully in (avoids a hard pop as you cross the waterline).")]
    public float blendBand = 0.6f;
    [Tooltip("Vertical offset (metres) applied to the camera when testing submersion — raise it so the wash starts when your head, not your feet, goes under.")]
    public float eyeOffset = 0f;

    Material _fxMat;
    UnderwaterImageEffect _fx;
    Camera _cam;
    int _camRefindCooldown;

    float _startY, _targetY, _elapsed;

    // Original global fog, captured once so we can restore it exactly when we surface.
    bool _fogCaptured, _origFog, _fogOverridden;
    Color _origFogColor;
    FogMode _origFogMode;
    float _origFogDensity;

    static readonly int _TintID = Shader.PropertyToID("_TintColor");

    void OnEnable()
    {
        // Static-batched meshes don't move at runtime — make sure this one can.
        gameObject.isStatic = false;

        _startY = transform.position.y;
        _targetY = _startY + riseHeight;
        SurfaceY = _startY;
        Active = true;
        _elapsed = 0f;
    }

    void OnDisable()
    {
        Active = false;
        SurfaceY = float.NegativeInfinity;
        RestoreFog();
    }

    void Update()
    {
        // ── rise ──
        _elapsed += Time.deltaTime;
        if (_elapsed >= startDelay)
        {
            Vector3 p = transform.position;
            p.y = Mathf.MoveTowards(p.y, _targetY, riseSpeed * Time.deltaTime);
            transform.position = p;
        }
        SurfaceY = transform.position.y;

        // ── underwater FX ──
        DriveUnderwater();
    }

    void DriveUnderwater()
    {
        Camera cam = ResolveCamera();
        if (cam == null) return;

        float depth = SurfaceY - (cam.transform.position.y + eyeOffset);
        float intensity = Mathf.Clamp01(depth / Mathf.Max(0.01f, blendBand));

        if (intensity > 0.0001f)
        {
            EnsureMaterial();
            if (_fxMat != null)
            {
                _fxMat.SetColor(_TintID, underwaterColor);   // live-tunable in play mode
                if (_fx == null)
                {
                    _fx = cam.GetComponent<UnderwaterImageEffect>();
                    if (_fx == null) _fx = cam.gameObject.AddComponent<UnderwaterImageEffect>();
                }
                _fx.material = _fxMat;
                _fx.intensity = intensity;
                _fx.MarkDriven();
            }
            ApplyFog(intensity);
        }
        else
        {
            RestoreFog();
        }
    }

    void EnsureMaterial()
    {
        if (_fxMat != null) return;
        var sh = Shader.Find("Hidden/PoolroomsUnderwater");
        if (sh == null)
        {
            Debug.LogWarning("[PoolFlood] Shader 'Hidden/PoolroomsUnderwater' not found — underwater wash disabled (add it to Always Included Shaders for builds).");
            return;
        }
        _fxMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        _fxMat.SetColor(_TintID, underwaterColor);
    }

    void ApplyFog(float intensity)
    {
        if (!_fogCaptured)
        {
            _origFog = RenderSettings.fog;
            _origFogColor = RenderSettings.fogColor;
            _origFogMode = RenderSettings.fogMode;
            _origFogDensity = RenderSettings.fogDensity;
            _fogCaptured = true;
        }
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = underwaterColor;
        RenderSettings.fogDensity = fogDensity * intensity;
        _fogOverridden = true;
    }

    void RestoreFog()
    {
        if (!_fogOverridden) return;
        if (_fogCaptured)
        {
            RenderSettings.fog = _origFog;
            RenderSettings.fogColor = _origFogColor;
            RenderSettings.fogMode = _origFogMode;
            RenderSettings.fogDensity = _origFogDensity;
        }
        _fogOverridden = false;
    }

    Camera ResolveCamera()
    {
        if (_cam != null && _cam.isActiveAndEnabled) return _cam;
        if (--_camRefindCooldown > 0) return _cam;
        _camRefindCooldown = 30;
        _cam = Camera.main;
        if (_cam == null)
        {
            var mgr = CameraEffectsManager.Instance;
            if (mgr != null) _cam = mgr.PlayerCamera;
        }
        return _cam;
    }
}
