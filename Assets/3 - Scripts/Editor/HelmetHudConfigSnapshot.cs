using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools ▸ Solar System ▸ Snapshot HelmetHudConfig Prefab.
///
/// HelmetHudConfig is a scene-placed settings holder (the helmet art texture +
/// the painted pod rects the compass / boost / vitals clusters seat into). It
/// exists only in 1.6.7.7.7.unity, so any OTHER scene — the tutorial box —
/// gets the clusters' old floating-card fallback look. This copies the gameplay
/// scene's object into a prefab that TutorialSceneBuilder instantiates.
///
/// Requires the gameplay scene to be open. Does not modify it (SaveAsPrefabAsset
/// leaves the scene object unconnected). Re-run after tuning the config in the
/// gameplay scene, then rebuild the tutorial scene.
/// </summary>
public static class HelmetHudConfigSnapshot
{
    public const string PrefabPath = "Assets/1 - samsPrefabs/HelmetHudConfig.prefab";

    [MenuItem("Tools/Solar System/Snapshot HelmetHudConfig Prefab")]
    public static void Snapshot()
    {
        HelmetHudConfig src = null;
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            var scene = EditorSceneManager.GetSceneAt(i);
            if (!scene.isLoaded || !scene.path.EndsWith("1.6.7.7.7.unity")) continue;
            foreach (var root in scene.GetRootGameObjects())
            {
                src = root.GetComponentInChildren<HelmetHudConfig>(true);
                if (src != null) break;
            }
        }
        if (src == null)
        {
            Debug.LogError("[HelmetHudConfigSnapshot] Open Assets/1.6.7.7.7.unity first — no HelmetHudConfig found in it.");
            return;
        }
        var prefab = PrefabUtility.SaveAsPrefabAsset(src.gameObject, PrefabPath, out bool ok);
        if (!ok || prefab == null) { Debug.LogError("[HelmetHudConfigSnapshot] SaveAsPrefabAsset failed."); return; }
        var cfg = prefab.GetComponent<HelmetHudConfig>();
        Debug.Log("[HelmetHudConfigSnapshot] Wrote " + PrefabPath + " (texture: " +
                  (cfg != null && cfg.helmetTexture != null ? cfg.helmetTexture.name : "NONE") + "). Rebuild the tutorial scene to pick it up.");
    }
}
