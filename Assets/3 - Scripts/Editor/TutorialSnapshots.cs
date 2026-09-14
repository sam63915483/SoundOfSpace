using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Prefab snapshots of GAMEPLAY-SCENE objects the tutorial box needs
/// (docs/superpowers/specs/2026-09-14-tutorial-box-design.md).
///
/// Two things in 1.6.7.7.7.unity exist only as scene objects with scene
/// overrides, so any other scene can't have them without a copy:
///
///   • HelmetHudConfig — the helmet art texture + the painted pod rects the
///     compass / boost / vitals clusters seat into. Without it the clusters
///     fall back to their old floating-card look.
///   • The Player — Player.prefab + ~20 scene-added components (every
///     equippable, PlayerSuitAudio, FallDamage, CameraShake, …) + the audio
///     clips (footsteps, jump / land, jetpack) set as overrides on the
///     instance. The bare prefab has none of that: no sound, no equipment.
///
/// Both menu items need the gameplay scene open and never modify it
/// (SaveAsPrefabAsset leaves the scene object unconnected). Re-run after
/// tuning the originals, then rebuild the tutorial scene.
/// </summary>
public static class TutorialSnapshots
{
    public const string HelmetPrefabPath = "Assets/1 - samsPrefabs/HelmetHudConfig.prefab";
    public const string PlayerPrefabPath = "Assets/1 - samsPrefabs/TutorialPlayer.prefab";

    [MenuItem("Tools/Solar System/Snapshot HelmetHudConfig Prefab")]
    public static void Snapshot()
    {
        var src = FindInGameplayScene<HelmetHudConfig>();
        if (src == null) return;
        var prefab = PrefabUtility.SaveAsPrefabAsset(src.gameObject, HelmetPrefabPath, out bool ok);
        if (!ok || prefab == null) { Debug.LogError("[TutorialSnapshots] SaveAsPrefabAsset failed for the helmet config."); return; }
        var cfg = prefab.GetComponent<HelmetHudConfig>();
        Debug.Log("[TutorialSnapshots] Wrote " + HelmetPrefabPath + " (texture: " +
                  (cfg != null && cfg.helmetTexture != null ? cfg.helmetTexture.name : "NONE") + "). Rebuild the tutorial scene to pick it up.");
    }

    [MenuItem("Tools/Solar System/Snapshot Tutorial Player Prefab")]
    public static void SnapshotPlayer()
    {
        // The scene keeps its player INACTIVE until GameSetUp switches it on, so
        // search inactive objects too. The MenuOrbit / NetworkPlayer puppets are
        // never in this scene at edit time.
        var src = FindInGameplayScene<PlayerController>();
        if (src == null) return;
        var prefab = PrefabUtility.SaveAsPrefabAsset(src.gameObject, PlayerPrefabPath, out bool ok);
        if (!ok || prefab == null) { Debug.LogError("[TutorialSnapshots] SaveAsPrefabAsset failed for the player."); return; }
        int comps = prefab.GetComponents<MonoBehaviour>().Length;
        Debug.Log("[TutorialSnapshots] Wrote " + PlayerPrefabPath + " (" + comps + " behaviours on the root). Rebuild the tutorial scene to pick it up.");
    }

    static T FindInGameplayScene<T>() where T : Component
    {
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            var scene = EditorSceneManager.GetSceneAt(i);
            if (!scene.isLoaded || !scene.path.EndsWith("1.6.7.7.7.unity")) continue;
            foreach (var root in scene.GetRootGameObjects())
            {
                var c = root.GetComponentInChildren<T>(true);
                if (c != null) return c;
            }
        }
        Debug.LogError("[TutorialSnapshots] Open Assets/1.6.7.7.7.unity first — no " + typeof(T).Name + " found in it.");
        return null;
    }
}
