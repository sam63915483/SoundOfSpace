using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A small, short-range point light riding on the player's camera so held items
/// (pistol, axe, bottle…) stay readable in the dark.
///
/// This is the standard trick — most shooters ship some version of it, variously
/// called a viewmodel light, weapon fill light, or eye light. The dark-scene
/// problem is that a first-person gun sits ~0.4m from the eye while every light
/// in the world is metres away and often behind the player, so the viewmodel
/// ends up a black silhouette even when the scene reads fine. The flashlight
/// doesn't help because it's mounted past the gun and its cone starts beyond it.
///
/// Approaches games use:
///   1. A dedicated light that ONLY renders the viewmodel (separate layer +
///      culling mask, or a whole second "weapon camera"). Cleanest — zero effect
///      on the world — but needs held items moved onto their own layer.
///   2. A tiny point light at the camera with a very short range, so it lights
///      the gun strongly (inverse-square: it's centimetres away) and the world
///      barely at all. Cheap, no layer surgery.
///   3. Baking emissive/rim into the viewmodel materials.
///
/// This is (2): range is deliberately tight, so a ~0.4m gun gets a lot of light
/// and the world past `range` gets none. Darkness and stealth are preserved —
/// enemy detection is vision-cone based, not light based, so nothing gameplay
/// facing changes.
///
/// Auto-singleton per the SpaceDustInventory pattern, and seeded in
/// MainMenuController.EnsureGameplaySingletons — RuntimeInitializeOnLoadMethod
/// fires once after the FIRST scene, which in a build is MainMenu, so a
/// MainMenu-skipping singleton never auto-creates in builds (CLAUDE.md trap #1).
/// </summary>
public class ViewmodelFillLight : MonoBehaviour
{
    public static ViewmodelFillLight Instance { get; private set; }

    [Tooltip("Brightness. Keep it low — the viewmodel is centimetres from the light, so a little goes a long way.")]
    public float intensity = 0.55f;
    [Tooltip("Metres. The whole point of the effect: short enough that the world past the player's hands is untouched.")]
    public float range = 4.5f;
    [Tooltip("Light colour. Very slightly warm reads as 'ambient bounce' rather than a torch.")]
    public Color color = new Color(0.92f, 0.94f, 1f, 1f);
    [Tooltip("Local offset from the camera. Pushed slightly forward and down so the gun is lit from a believable angle instead of getting a flat head-on flash.")]
    public Vector3 localOffset = new Vector3(0.05f, -0.12f, 0.18f);

    Light _light;
    Transform _camera;
    float _nextCameraSearch;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("ViewmodelFillLight");
        DontDestroyOnLoad(go);
        go.AddComponent<ViewmodelFillLight>();
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

    void LateUpdate()
    {
        // Follow the ACTIVE camera. It changes across the game's life — the
        // kill-shot cam and trailer free-cam borrow it, scene reloads replace
        // it — so re-parent whenever the main camera isn't the one we're on.
        // Throttled: never a per-frame Camera.main.
        if (_camera == null || (Camera.main != null && Camera.main.transform != _camera))
        {
            if (Time.unscaledTime < _nextCameraSearch) return;
            _nextCameraSearch = Time.unscaledTime + 0.5f;
            var cam = Camera.main;
            if (cam == null) return;
            _camera = cam.transform;
            EnsureLight();
            _light.transform.SetParent(_camera, false);
        }

        if (_light == null) { EnsureLight(); if (_camera != null) _light.transform.SetParent(_camera, false); }

        bool on = InputSettings.Active == null || InputSettings.Active.fxViewmodelLight;
        if (_light.enabled != on) _light.enabled = on;
        if (!on) return;

        // Live-apply so the values can be tuned in Play mode without a restart.
        _light.transform.localPosition = localOffset;
        _light.intensity = intensity;
        _light.range = range;
        _light.color = color;
    }

    void EnsureLight()
    {
        if (_light != null) return;
        var go = new GameObject("ViewmodelFill");
        _light = go.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.shadows = LightShadows.None;          // a light AT the eye casts no visible shadows anyway
        _light.bounceIntensity = 0f;
        // Pinned to a per-pixel slot so it can't be demoted to vertex/SH lighting
        // when other lights are around — that demotion is exactly the bug
        // PixelLightLimitFix documents. The cap there is 64, so there's headroom.
        _light.renderMode = LightRenderMode.ForcePixel;
        _light.intensity = intensity;
        _light.range = range;
        _light.color = color;
    }
}
