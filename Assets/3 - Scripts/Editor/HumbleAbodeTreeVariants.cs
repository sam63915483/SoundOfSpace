using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Humble Abode's forest = the FantasyForest + FantasyValley trees Sam picked
/// from the Tree Gallery (2026-09-04), in HIS ranking. This builds one prefab
/// VARIANT per tree under Assets/1 - samsPrefabs/Trees/ and wires the list
/// (plus rank weights) into the scene's TreeSpawner.
///
/// Why variants, not the pack prefabs directly:
///   • Sam likes the TALL look, so each variant is authored at the gallery's
///     "tall" scale (1.15, 1.9, 1.15). SpawnedTree / SaplingGrowth treat the
///     prefab's authored scale as the mature size, so planted trees, pooled
///     trees and the size-variety multiplier in TreeSpawner all agree.
///   • The pack LODGroups CULL the tree at ~5-8% screen height, i.e. roughly
///     100-150 m — well inside the spawner's view distance (350 m default, up
///     to 1000 m), so trees popped out of existence while still "spawned".
///     The variants keep the pack's three LOD meshes but retune the switch
///     points so LOD2 (a few hundred tris) carries the tree all the way out
///     to the spawner's despawn radius, with a dither cross-fade between
///     levels instead of a hard swap.
///   • The pack assets stay untouched, so re-importing the pack can't undo it.
///
/// Re-running is safe: variants are rebuilt in place (same GUIDs → the scene
/// reference survives), then the TreeSpawner list is rewritten.
/// </summary>
public static class HumbleAbodeTreeVariants
{
    const string OutDir = "Assets/1 - samsPrefabs/Trees";
    const string ForestDir = "Assets/5 - External Imports/Nature & Trees/FantasyForest/Prefabs/Vegetation";
    const string ValleyDir = "Assets/5 - External Imports/Nature & Trees/FantasyValley/Prefabs/Vegetation";

    /// The gallery's "tall" size — Sam's pick.
    static readonly Vector3 TallScale = new Vector3(1.15f, 1.9f, 1.15f);

    /// Sam's ranking, best first. (Valley 07 wasn't in his list and is left out.)
    static readonly string[] ForestOrder = { "05", "04", "01", "02", "03", "06", "07", "08" };
    static readonly string[] ValleyOrder = { "03", "08", "06", "09", "01", "02", "04", "05", "10" };

    // LOD switch points as screen-height fractions (lodBias 1, 60° vertical
    // FOV). For a ~14 m tall variant: LOD0 to ≈60 m, LOD1 to ≈200 m, and the
    // cull at 0.01 ≈ 1.2 km — past the spawner's maximum view distance, so the
    // spawner's despawn is always what removes a tree, never the LODGroup.
    const float Lod0Height = 0.20f;
    const float Lod1Height = 0.06f;
    const float CullHeight = 0.01f;

    struct Entry { public string variantPath; public float weight; }

    [MenuItem("Tools/Humble Abode Trees/Build Variants + Wire Spawner")]
    public static void BuildAndWire()
    {
        var entries = BuildVariants();
        WireSpawner(entries);
    }

    [MenuItem("Tools/Humble Abode Trees/Build Variants Only")]
    public static void BuildOnly() => BuildVariants();

    static List<Entry> BuildVariants()
    {
        if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/1 - samsPrefabs", "Trees");
        var entries = new List<Entry>();
        // Linear rank weights: best-of-pack = pack size, worst = 1 (still spawns).
        for (int i = 0; i < ForestOrder.Length; i++)
            entries.Add(BuildOne(ForestDir, "FF", ForestOrder[i], ForestOrder.Length - i));
        for (int i = 0; i < ValleyOrder.Length; i++)
            entries.Add(BuildOne(ValleyDir, "FV", ValleyOrder[i], ValleyOrder.Length - i));
        AssetDatabase.SaveAssets();
        Debug.Log($"[HumbleAbodeTrees] Built {entries.Count} tree variants in {OutDir}.");
        return entries;
    }

    static Entry BuildOne(string srcDir, string tag, string number, float weight)
    {
        string srcPath = $"{srcDir}/Tree_{number}.prefab";
        string dstPath = $"{OutDir}/HA_{tag}_Tree_{number}.prefab";
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
        if (src == null) { Debug.LogError("[HumbleAbodeTrees] missing " + srcPath); return new Entry { variantPath = null, weight = weight }; }

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
        try
        {
            inst.transform.localScale = Vector3.Scale(src.transform.localScale, TallScale);

            var lg = inst.GetComponent<LODGroup>();
            if (lg != null)
            {
                var lods = lg.GetLODs();
                if (lods.Length >= 3)
                {
                    lods[0].screenRelativeTransitionHeight = Lod0Height;
                    lods[1].screenRelativeTransitionHeight = Lod1Height;
                    lods[lods.Length - 1].screenRelativeTransitionHeight = CullHeight;
                    for (int i = 2; i < lods.Length - 1; i++)   // any extra middle levels: spread evenly
                        lods[i].screenRelativeTransitionHeight = Mathf.Lerp(Lod1Height, CullHeight, (float)(i - 1) / (lods.Length - 2));
                    for (int i = 0; i < lods.Length; i++) lods[i].fadeTransitionWidth = 0.15f;
                    lg.SetLODs(lods);
                }
                lg.fadeMode = LODFadeMode.CrossFade;
                lg.animateCrossFading = true;
                lg.RecalculateBounds();
            }
            else Debug.LogWarning("[HumbleAbodeTrees] no LODGroup on " + srcPath);

            PrefabUtility.SaveAsPrefabAsset(inst, dstPath, out bool ok);
            if (!ok) Debug.LogError("[HumbleAbodeTrees] failed to save " + dstPath);
        }
        finally
        {
            Object.DestroyImmediate(inst);
        }
        return new Entry { variantPath = dstPath, weight = weight };
    }

    const string GameplayScenePath = "Assets/1.6.7.7.7.unity";

    static void WireSpawner(List<Entry> entries)
    {
        // Wire whichever loaded scene has the spawner; if the gameplay scene
        // isn't open (Sam was in the Tree Gallery the first time this ran),
        // open it ADDITIVELY, save it, and close it again so whatever he has
        // open stays exactly as it is.
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(GameplayScenePath);
        bool openedHere = false;
        if (!scene.IsValid() || !scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Additive);
            openedHere = true;
        }
        try
        {
            TreeSpawner spawner = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                spawner = root.GetComponentInChildren<TreeSpawner>(true);
                if (spawner != null) break;
            }
            if (spawner == null) { Debug.LogError("[HumbleAbodeTrees] No TreeSpawner in " + GameplayScenePath); return; }
            WriteSpawner(spawner, entries);
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void WriteSpawner(TreeSpawner spawner, List<Entry> entries)
    {
        var so = new SerializedObject(spawner);
        var prefabs = so.FindProperty("treePrefabs");
        var weights = so.FindProperty("treeWeights");
        var valid = entries.FindAll(e => e.variantPath != null);
        prefabs.arraySize = valid.Count;
        weights.arraySize = valid.Count;
        for (int i = 0; i < valid.Count; i++)
        {
            prefabs.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(valid[i].variantPath);
            weights.GetArrayElementAtIndex(i).floatValue = valid[i].weight;
        }
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(spawner);

        var scene = spawner.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        Debug.Log($"[HumbleAbodeTrees] TreeSpawner now has {valid.Count} prefabs (weights {string.Join(", ", valid.ConvertAll(e => e.weight.ToString("0")))}). Scene saved: {saved}.");
    }
}
