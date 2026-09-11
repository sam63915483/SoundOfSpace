using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools ▸ Solar System ▸ Wire Cat Spawner.
///
/// Finds the IndieCat prefabs, the Cat_L avatar and the ten (plus three
/// optional) animation clips by name inside the pack's FBX assets, and drops a
/// configured <see cref="CatSpawner"/> into the open scene. Doing it by hand
/// means fourteen inspector drags and one silent failure mode — an Animator
/// with no Avatar plays nothing and reports nothing.
///
/// Idempotent: run it again after moving the pack and it re-resolves in place.
/// It does NOT save the scene — that stays Sam's call.
/// </summary>
public static class WireCatSpawner
{
    const string PackRoot = "Assets/IndieCat Animals/Cats";
    const string PrefabDir = PackRoot + "/Cats - Low Poly/Prefabs";
    const string MeshFbx   = PackRoot + "/Cats - Low Poly/Meshes/Cat_L.fbx";
    const string AnimDir   = PackRoot + "/Animations";

    [MenuItem("Tools/Solar System/Wire Cat Spawner")]
    public static void Wire()
    {
        var spawner = Object.FindObjectOfType<CatSpawner>();
        if (spawner == null)
        {
            var go = new GameObject("CatSpawner");
            spawner = go.AddComponent<CatSpawner>();
            Undo.RegisterCreatedObjectUndo(go, "Create CatSpawner");
            Debug.Log("[WireCatSpawner] Created a new CatSpawner in the open scene.");
        }

        Undo.RecordObject(spawner, "Wire Cat Spawner");

        // ── prefabs ──
        var prefabs = new List<GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null) prefabs.Add(go);
        }
        prefabs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        spawner.catPrefabs = prefabs.ToArray();

        // ── avatar (a sub-asset of the mesh FBX) ──
        spawner.catAvatar = null;
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(MeshFbx))
        {
            if (obj is Avatar av) { spawner.catAvatar = av; break; }
        }

        // ── clips ──
        var clips = new Dictionary<string, AnimationClip>();
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { AnimDir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            // "For Preview" duplicates every clip under a _forPreview name; they
            // are the pack's inspector previews, not the real ones.
            if (path.Contains("For Preview")) continue;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is AnimationClip clip && !clip.name.StartsWith("__preview"))
                    clips[clip.name] = clip;
            }
        }

        AnimationClip Get(string n) => clips.TryGetValue(n, out var c) ? c : null;

        spawner.clipIdle     = Get("Base");
        spawner.clipLook     = Get("Look");
        spawner.clipStretch  = Get("Stretching");
        spawner.clipSit      = Get("Sit_Idle");
        spawner.clipLie      = Get("Lie_idle");
        spawner.clipSleep    = Get("Sleep_idle");
        spawner.clipWalk     = Get("Walk_F");
        spawner.clipTrot     = Get("Trot_F");
        spawner.clipSwim     = Get("Swim_F");
        spawner.clipSwimIdle = Get("Swim_idle");
        spawner.clipSitTo    = Get("Sit_to");
        spawner.clipLieTo    = Get("Lie_to");
        spawner.clipSleepTo  = Get("Sleep_to");

        // A_Cat_Action — the file whose clips are not split in the .meta, so a
        // grep never saw them. Unity imports them fine.
        spawner.clipLick     = Get("Licking_sit");
        spawner.clipSharpen  = Get("SharpensClaws");
        spawner.clipDig      = Get("Digging");
        spawner.clipShake    = Get("Shaking");
        spawner.clipPet      = Get("Pet");
        spawner.clipPetSit   = Get("Pet_sit");
        spawner.clipPetLie   = Get("Pet_lie");
        spawner.clipEat      = Get("Eat_D");

        // The AI-generated purr if it exists, else the synthesised fallback. The
        // generator (Coplay generate_sfx) always reports a timeout but the clip
        // lands anyway a minute or so later -- Sam, 2026-09-11 -- so this may
        // resolve differently on a re-run.
        spawner.purrClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Cats/cat_purr_generated.wav")
                        ?? AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Cats/cat_purr_loop.wav");

        // ── report ──
        var missing = new List<string>();
        if (spawner.catPrefabs == null || spawner.catPrefabs.Length == 0) missing.Add("cat prefabs");
        if (spawner.catAvatar == null) missing.Add("Cat_L avatar (clips will not play!)");
        if (spawner.purrClip == null) missing.Add("purr clip (Assets/Audio/Cats/cat_purr_loop.wav)");
        foreach (var pair in new (string name, AnimationClip clip)[]
        {
            ("Base", spawner.clipIdle), ("Look", spawner.clipLook), ("Stretching", spawner.clipStretch),
            ("Sit_Idle", spawner.clipSit), ("Lie_idle", spawner.clipLie), ("Sleep_idle", spawner.clipSleep),
            ("Walk_F", spawner.clipWalk), ("Trot_F", spawner.clipTrot),
            ("Swim_F", spawner.clipSwim), ("Swim_idle", spawner.clipSwimIdle),
            ("Licking_sit", spawner.clipLick), ("SharpensClaws", spawner.clipSharpen),
            ("Digging", spawner.clipDig), ("Shaking", spawner.clipShake),
            ("Pet", spawner.clipPet), ("Pet_sit", spawner.clipPetSit), ("Pet_lie", spawner.clipPetLie),
            ("Eat_D", spawner.clipEat),
        })
        {
            if (pair.clip == null) missing.Add("clip " + pair.name);
        }

        EditorUtility.SetDirty(spawner);

        if (missing.Count == 0)
        {
            Debug.Log($"[WireCatSpawner] OK — {spawner.catPrefabs.Length} prefabs, avatar '{spawner.catAvatar.name}', " +
                      $"{clips.Count} clips found. SAVE THE SCENE to keep it.", spawner);
        }
        else
        {
            Debug.LogWarning("[WireCatSpawner] Wired, but these were not found: " +
                             string.Join(", ", missing) +
                             ". Check the pack is still at " + PackRoot, spawner);
        }
    }
}
