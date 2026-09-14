using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Prefab snapshots of GAMEPLAY-SCENE objects the tutorial box needs
/// (docs/superpowers/specs/2026-09-14-tutorial-box-design.md).
///
/// Several things in 1.6.7.7.7.unity exist only as scene objects with scene
/// overrides, so any other scene can't have them without a copy:
///
///   • HelmetHudConfig — the helmet art texture + the painted pod rects the
///     compass / boost / vitals clusters seat into.
///   • The Player — Player.prefab + ~20 scene-added components (every
///     equippable, PlayerSuitAudio, FallDamage, CameraShake, …) + the audio
///     clips set as overrides on the instance.
///   • The spawners — TreeSpawner, CrystalSpawner, MushroomSpawner, CatSpawner
///     and the GrassSpawner object (InstancedGrassRenderer), each with Sam's
///     serialized tuning (prefab lists, weights, scales, cell sizes). Fresh
///     components use the CODE defaults, which are not what the game runs
///     (the cats came out a third of their size that way).
///
/// The gameplay scene is opened ADDITIVELY if it isn't already open, and
/// closed again without saving. SaveAsPrefabAsset leaves the scene objects
/// unconnected, so the gameplay scene is never modified. Re-run after tuning
/// the originals, then rebuild the tutorial scene.
/// </summary>
public static class TutorialSnapshots
{
    public const string GameplayScenePath = "Assets/1.6.7.7.7.unity";
    public const string HelmetPrefabPath  = "Assets/1 - samsPrefabs/HelmetHudConfig.prefab";
    public const string PlayerPrefabPath  = "Assets/1 - samsPrefabs/TutorialPlayer.prefab";
    public const string SpawnerDir        = "Assets/1 - samsPrefabs/TutorialSpawners";
    public static string SpawnerPrefabPath(string name) => SpawnerDir + "/" + name + ".prefab";

    /// The spawner objects to copy: (prefab name, component that identifies the object).
    public static readonly (string name, System.Type type)[] Spawners =
    {
        ("TreeSpawner",     typeof(TreeSpawner)),
        ("CrystalSpawner",  typeof(CrystalSpawner)),
        ("MushroomSpawner", typeof(MushroomSpawner)),
        ("CatSpawner",      typeof(CatSpawner)),
        ("Grass",           typeof(InstancedGrassRenderer)),   // the 'GrassSpawner' object: disabled GrassSpawner + the live InstancedGrassRenderer
    };

    [MenuItem("Tools/Solar System/Snapshot ALL Tutorial Prefabs")]
    public static void SnapshotAll()
    {
        using (new GameplaySceneScope())
        {
            Snapshot();
            SnapshotPlayer();
            SnapshotSpawners();
        }
    }

    [MenuItem("Tools/Solar System/Snapshot HelmetHudConfig Prefab")]
    public static void Snapshot()
    {
        using (new GameplaySceneScope())
        {
            var src = FindInGameplayScene<HelmetHudConfig>();
            if (src == null) return;
            Save(src.gameObject, HelmetPrefabPath);
        }
    }

    [MenuItem("Tools/Solar System/Snapshot Tutorial Player Prefab")]
    public static void SnapshotPlayer()
    {
        using (new GameplaySceneScope())
        {
            // The scene keeps its player INACTIVE until GameSetUp switches it on.
            var src = FindInGameplayScene<PlayerController>();
            if (src == null) return;
            Save(src.gameObject, PlayerPrefabPath);
        }
    }

    [MenuItem("Tools/Solar System/Snapshot Tutorial Spawner Prefabs")]
    public static void SnapshotSpawners()
    {
        using (new GameplaySceneScope())
        {
            if (!AssetDatabase.IsValidFolder(SpawnerDir)) AssetDatabase.CreateFolder("Assets/1 - samsPrefabs", "TutorialSpawners");
            foreach (var (name, type) in Spawners)
            {
                var src = FindInGameplayScene(type);
                if (src == null) continue;
                Save(src.gameObject, SpawnerPrefabPath(name));
            }
        }
    }

    static void Save(GameObject src, string path)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(src, path, out bool ok);
        if (!ok || prefab == null) { Debug.LogError("[TutorialSnapshots] SaveAsPrefabAsset failed for " + path); return; }
        Debug.Log("[TutorialSnapshots] Wrote " + path + " from '" + src.name + "'.");
    }

    static T FindInGameplayScene<T>() where T : Component => FindInGameplayScene(typeof(T)) as T;

    static Component FindInGameplayScene(System.Type type)
    {
        var scene = SceneManager.GetSceneByPath(GameplayScenePath);
        if (scene.IsValid() && scene.isLoaded)
            foreach (var root in scene.GetRootGameObjects())
            {
                var c = root.GetComponentInChildren(type, true);
                if (c != null) return c;
            }
        Debug.LogError("[TutorialSnapshots] No " + type.Name + " found in " + GameplayScenePath + ".");
        return null;
    }

    /// Opens the gameplay scene additively for the duration if it isn't open,
    /// and closes it (never saving) afterwards. Leaves an already-open copy alone.
    sealed class GameplaySceneScope : System.IDisposable
    {
        readonly bool _opened;
        readonly Scene _prevActive;
        public GameplaySceneScope()
        {
            _prevActive = SceneManager.GetActiveScene();
            var s = SceneManager.GetSceneByPath(GameplayScenePath);
            if (!(s.IsValid() && s.isLoaded))
            {
                Debug.Log("[TutorialSnapshots] Opening " + GameplayScenePath + " additively (read-only) …");
                EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Additive);
                _opened = true;
            }
        }
        public void Dispose()
        {
            if (!_opened) return;
            var s = SceneManager.GetSceneByPath(GameplayScenePath);
            if (_prevActive.IsValid()) SceneManager.SetActiveScene(_prevActive);
            if (s.IsValid() && s.isLoaded) EditorSceneManager.CloseScene(s, true);   // discard, never save
        }
    }
}
