using UnityEngine;
using UnityEngine.SceneManagement;

// Auto-singleton that raises QualitySettings.pixelLightCount on every scene
// load to fix the "ground / ship / concert brightness changes when I turn the
// camera" bug.
//
// Root cause: Unity's built-in render pipeline has a per-pixel light slot
// limit (default 4). When more than that many lights affect one object, the
// engine ranks them by importance — top N render per-pixel (correct), the
// rest are demoted to per-vertex or spherical harmonics (much dimmer, no
// shadows). The ranking is camera-frustum aware, so rotating the camera
// shifts which lights are in the "top N" and the affected surface's
// brightness visibly breathes.
//
// Symptom-level fixes already in the codebase: ConcertConeLight,
// ConcertStrobeLight, ConcertBlinder, ShipMarketShopUI, and GoodsVendorShopUI
// all set their own Light.renderMode = LightRenderMode.ForcePixel to pin
// themselves to a pixel slot. They were guarding their own lights but
// nothing was protecting the Sun or anything else, so demotion still hit the
// ground.
//
// This fix is global: bump the limit to 16. A single object is rarely
// affected by more than ~10 simultaneous lights, so 16 effectively retires
// the demotion path. Cost is small — Unity only spends time per-pixel on
// lights that actually overlap the surface.
//
// Pattern matches SpaceDustInventory / ConcertStageHub / AutosaveManager:
// RuntimeInitializeOnLoadMethod auto-creation, skip MainMenu, DontDestroyOnLoad,
// re-apply on every sceneLoaded so a scene transition can't restore the low
// default.
public class PixelLightLimitFix : MonoBehaviour
{
    public static PixelLightLimitFix Instance { get; private set; }

    // Raised from 16 → 64 after a debugging round revealed that ForcePixel-ing
    // every scene light pushed the count over the 16-slot cap and Unity's
    // importance ranker was demoting some lights per-camera-frustum, producing
    // a yaw-dependent brightness flicker (two ~40° dark wedges as the camera
    // rotated). 64 gives so much headroom that demotion effectively never
    // happens. Per-pixel work is only paid where a light actually touches a
    // surface, so the cost is tied to scene geometry, not the cap.
    const int TargetPixelLightCount = 64;


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("[PixelLightLimitFix]");
        DontDestroyOnLoad(go);
        go.AddComponent<PixelLightLimitFix>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Apply();
        // NOTE: an earlier debugging pass also called ForceAllLightsPixel here.
        // That made the yaw-dependent flicker WORSE by oversubscribing the
        // pixel-light slot count and forcing Unity's importance ranker to
        // demote lights per-frustum. Removed; the raised TargetPixelLightCount
        // alone should be sufficient.
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Re-apply after every scene load — quality settings can be reset
        // when Unity switches quality levels or reloads the active level.
        Apply();
    }

    static void Apply()
    {
        if (QualitySettings.pixelLightCount < TargetPixelLightCount)
            QualitySettings.pixelLightCount = TargetPixelLightCount;
    }
}
