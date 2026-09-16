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
///   • HUD_Canvas — the gameplay HUD. Twenty-one panels the tutorial had none
///     of, including the two that make the box's objectives possible at all:
///     BuildMenu (the build menu with its authored blueprint catalogue, which
///     cannot be auto-created because those are Inspector prefab references)
///     and CookPanel + BonfirePromptText (the bonfire's cooking UI). They are
///     siblings under one canvas, so one prefab keeps every cross-reference
///     between them intact.
///   • The fishing rod — the rod prop with its FishingRodPickup, which lives
///     in Tev's cabin as a plain scene object. The tutorial shuttle carries its
///     own copy so the player can pick a rod up before the first objective.
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
    public const string RodPrefabPath     = "Assets/1 - samsPrefabs/TutorialFishingRod.prefab";
    public const string HudPrefabPath     = "Assets/1 - samsPrefabs/TutorialHUDCanvas.prefab";
    public const string HudCanvasName     = "HUD_Canvas";
    public const string ManagerDir        = "Assets/1 - samsPrefabs/TutorialManagers";
    public static string ManagerPrefabPath(string name) => ManagerDir + "/" + name + ".prefab";
    public const string SpawnerDir        = "Assets/1 - samsPrefabs/TutorialSpawners";
    public static string SpawnerPrefabPath(string name) => SpawnerDir + "/" + name + ".prefab";

    /// <summary>
    /// MANAGERS the box needs that nothing auto-creates.
    ///
    /// Found by diffing the two scenes: every MonoBehaviour type in the gameplay
    /// scene that the box has no copy of, filtered to the ones with a static
    /// Instance and NO RuntimeInitializeOnLoadMethod. Those are the ones that
    /// silently do nothing in the box - every caller null-checks the Instance,
    /// so there is no error, just a feature that quietly does not happen:
    ///
    ///   FishingdexManager - owns the per-tier FISH PREFABS. Without it there is
    ///     no fish on the bobber and no ambient fish in the water: both ask it
    ///     for a model, get null, and draw nothing.
    ///   ResourceManager   - hunger and thirst. Without it eating a cooked fish
    ///     restores nothing (BonfireInteraction null-checks it).
    ///
    /// Both sit alone on their own GameObject under --- Managers ---, so a
    /// snapshot brings nothing else with it.
    ///
    /// NOT here: FishInventory. It lives on the fish-market ALIEN in the
    /// gameplay scene (alongside FishMarketNPC, an Animator and a damageable),
    /// so snapshotting its object would drag a whole vendor into the box. It has
    /// no serialized data at all - just a runtime list - so the builder adds a
    /// bare component instead.
    /// </summary>
    public static readonly (string name, System.Type type)[] Managers =
    {
        ("FishingdexManager", typeof(FishingdexManager)),
        ("ResourceManager",   typeof(ResourceManager)),
    };

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
            SnapshotFishingRod();
            SnapshotHudCanvas();
            SnapshotManagers();
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

    /// <summary>
    /// The gameplay HUD canvas, whole. The tutorial box shipped with nothing but
    /// a crosshair, which is fine until the player is asked to open a build menu
    /// or cook a fish - both of those are panels under this canvas, wired to each
    /// other by scene references that only survive if they travel together.
    ///
    /// Found BY NAME rather than by component: it is a plain Canvas, so there is
    /// no type that picks it out from the other canvases in the scene.
    /// </summary>
    [MenuItem("Tools/Solar System/Snapshot Tutorial HUD Canvas Prefab")]
    public static void SnapshotHudCanvas()
    {
        using (new GameplaySceneScope())
        {
            var src = FindInGameplaySceneByName(HudCanvasName);
            if (src == null) return;
            Save(src, HudPrefabPath);
        }
    }

    /// <summary>
    /// The fishing rod prop the player picks up (look at it, press F). In the
    /// gameplay scene it sits in Tev's cabin as a scene object, so no other
    /// scene can reference it.
    ///
    /// Saved WORLD-SCALED: the rod prop hangs off a parent chain that scales it,
    /// and a prefab keeps only the source's LOCAL scale, so a straight snapshot
    /// comes out the wrong size wherever it is instantiated. The asset is
    /// re-opened after saving and its root transform is rewritten to the
    /// source's lossyScale at identity position/rotation — the prefab then means
    /// "the rod, actual size", and the builder only has to place it.
    /// </summary>
    [MenuItem("Tools/Solar System/Snapshot Tutorial Fishing Rod Prefab")]
    public static void SnapshotFishingRod()
    {
        using (new GameplaySceneScope())
        {
            var src = FindInGameplayScene<FishingRodPickup>();
            if (src == null) return;
            Vector3 worldScale = src.transform.lossyScale;
            Save(src.gameObject, RodPrefabPath);
            NormalizeRoot(RodPrefabPath, worldScale);
        }
    }

    /// Rewrites a saved prefab's ROOT transform: identity pose, the given scale.
    /// Edits the prefab ASSET via LoadPrefabContents — never the scene it came
    /// from, which this class must leave untouched.
    static void NormalizeRoot(string path, Vector3 scale)
    {
        var contents = PrefabUtility.LoadPrefabContents(path);
        if (contents == null) return;
        try
        {
            contents.transform.localPosition = Vector3.zero;
            contents.transform.localRotation = Quaternion.identity;
            contents.transform.localScale    = scale;
            PrefabUtility.SaveAsPrefabAsset(contents, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
    }

    [MenuItem("Tools/Solar System/Snapshot Tutorial Manager Prefabs")]
    public static void SnapshotManagers()
    {
        using (new GameplaySceneScope())
        {
            if (!AssetDatabase.IsValidFolder(ManagerDir))
                AssetDatabase.CreateFolder("Assets/1 - samsPrefabs", "TutorialManagers");
            foreach (var (name, type) in Managers)
            {
                var src = FindInGameplayScene(type);
                if (src == null) continue;
                Save(src.gameObject, ManagerPrefabPath(name));
            }
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

    static GameObject FindInGameplaySceneByName(string name)
    {
        var scene = SceneManager.GetSceneByPath(GameplayScenePath);
        if (scene.IsValid() && scene.isLoaded)
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t.gameObject;
        Debug.LogError("[TutorialSnapshots] No GameObject named '" + name + "' in " + GameplayScenePath + ".");
        return null;
    }

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
