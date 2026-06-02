using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Fixes "the blue atmosphere is gone after returning from the backrooms (or any
/// warm gameplay-scene reload within one process run)."
///
/// ROOT CAUSE (see AtmosphereSettings.cs in the forbidden Celestial zone):
/// AtmosphereSettings is a ScriptableObject *asset* whose SetProperties() — which
/// pushes every scattering uniform AND the precomputed _BakedOpticalDepth texture
/// onto the atmosphere material — is gated by a private, non-serialized
/// `settingsUpToDate` flag and runs only while that flag is false (in a build,
/// Application.isPlaying is always true). The flag is set true on the first
/// gameplay load and never reset at runtime (only OnValidate, editor-only).
/// On a warm reload, PlanetEffects rebuilds its holders with brand-new Materials,
/// but SetProperties is skipped because the persistent asset's flag is still true,
/// so those new materials never receive their properties → the shader renders
/// nothing → no visible atmosphere. A trip through the main menu happens to work
/// only because LoadScene's Resources.UnloadUnusedAssets reclaims the now-
/// unreferenced asset, so it reloads fresh with the flag back to false.
///
/// THE FIX: on each scene (re)load, reset `settingsUpToDate = false` on every
/// loaded AtmosphereSettings (via reflection — we do NOT edit the forbidden
/// source). The next render's SetProperties then re-bakes the optical-depth
/// texture and re-applies all uniforms — exactly the cold-load / main-menu
/// behaviour that already works. The re-bake is a single 256x256 compute
/// dispatch per atmosphere planet per load: cheap, and identical to what
/// already runs on first launch.
///
/// ROLLBACK: delete this file (+ .meta). Nothing else references it; the
/// forbidden atmosphere code is untouched. (Git: the commit before this one is
/// the clean restore point.)
///
/// Auto-created BeforeSceneLoad + DontDestroyOnLoad so it fires reliably in a
/// build regardless of the first scene (no MainMenu-seed trap).
/// </summary>
public class AtmosphereReloadFix : MonoBehaviour
{
    static FieldInfo _flagField;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AutoCreate()
    {
        var go = new GameObject("AtmosphereReloadFix");
        DontDestroyOnLoad(go);
        go.AddComponent<AtmosphereReloadFix>();
    }

    void OnEnable()  { SceneManager.sceneLoaded += OnSceneLoaded; }
    void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // MainMenu has no atmosphere; resetting there would be harmless but
        // pointless. Reset on every gameplay/interior load — the flag only
        // matters once the atmosphere actually renders (gameplay), and the
        // next render after THIS load is where the re-apply needs to happen.
        if (scene.name == "MainMenu") return;
        InvalidateAtmosphereSettings();
    }

    static void InvalidateAtmosphereSettings()
    {
        if (_flagField == null)
        {
            _flagField = typeof(AtmosphereSettings)
                .GetField("settingsUpToDate", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_flagField == null)
            {
                Debug.LogWarning("[AtmosphereReloadFix] Could not find AtmosphereSettings.settingsUpToDate " +
                                 "— field renamed? Atmosphere-reload fix is INACTIVE.");
                return;
            }
        }

        // FindObjectsOfTypeAll reaches loaded assets (ScriptableObjects) that
        // aren't part of any scene — which is exactly where these persistent
        // AtmosphereSettings live.
        var all = Resources.FindObjectsOfTypeAll<AtmosphereSettings>();
        int n = 0;
        for (int i = 0; i < all.Length; i++)
        {
            try { _flagField.SetValue(all[i], false); n++; }
            catch (System.Exception e) { Debug.LogWarning("[AtmosphereReloadFix] reset failed: " + e.Message); }
        }
        Debug.Log($"[AtmosphereReloadFix] invalidated {n} AtmosphereSettings — atmosphere will re-bake on next render.");
    }
}
