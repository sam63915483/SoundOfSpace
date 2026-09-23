using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools ▸ Cave ▸ Build Cave Spiders.
///
/// Makes the three hostile cave spiders from the Low Poly Animated Animals
/// pack (red / green / yellow) and hooks a <see cref="CaveSpiderSpawner"/> onto
/// the moon's Cave_Moon prefab. Safe to re-run: prefabs are rebuilt in place
/// (same GUIDs) and the spawner keeps any values tuned in the Inspector.
///
/// What each prefab is: a plain COPY of the pack's spider (not a variant, so a
/// pack re-import can never change it), with the pack's own wander AI and sound
/// scripts removed, its CharacterController swapped for a capsule sized to the
/// mesh, plus a kinematic Rigidbody and CaveSpider (which does its own
/// proximity sensing — no EnemyVision).
/// </summary>
public static class CaveSpiderBuilder
{
    const string PackRoot = "Assets/polyperfect/Low Poly Animated Animals";
    const string OutFolder = "Assets/1 - samsPrefabs/CaveSpiders";
    const string CavePrefab = "Assets/1 - samsPrefabs/Cave/Moon/Cave_Moon.prefab";

    static readonly string[] Colours = { "Red", "Green", "Yellow" };

    [MenuItem("Tools/Cave/Build Cave Spiders")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(OutFolder))
            AssetDatabase.CreateFolder("Assets/1 - samsPrefabs", "CaveSpiders");

        var bite  = AssetDatabase.LoadAssetAtPath<AudioClip>($"{PackRoot}/Sounds/SFX_Spider_Attack.ogg");
        var call  = AssetDatabase.LoadAssetAtPath<AudioClip>($"{PackRoot}/Sounds/SFX_Spider_Call.ogg");
        var call2 = AssetDatabase.LoadAssetAtPath<AudioClip>($"{PackRoot}/Sounds/SFX_Spider_Call_2.ogg");
        var walk  = AssetDatabase.LoadAssetAtPath<AudioClip>($"{PackRoot}/Sounds/SFX_Spider_Walking.ogg");

        var built = new List<GameObject>();
        foreach (var colour in Colours)
        {
            string srcPath = $"{PackRoot}/Prefabs/Animals/Spider_{colour}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(srcPath) == null) { Debug.LogError($"[CaveSpiders] Missing pack prefab Spider_{colour}."); continue; }

            // Loaded in an isolated preview scene, so the open gameplay scene is never touched.
            var go = PrefabUtility.LoadPrefabContents(srcPath);
            go.name = $"CaveSpider_{colour}";
            try
            {
                Strip(go);
                Equip(go, bite, call, call2, walk);
                string path = $"{OutFolder}/CaveSpider_{colour}.prefab";
                var saved = PrefabUtility.SaveAsPrefabAsset(go, path);
                if (saved != null) built.Add(saved);
            }
            finally { PrefabUtility.UnloadPrefabContents(go); }
        }

        HookSpawner(built.ToArray());
        AssetDatabase.SaveAssets();
        Debug.Log($"[CaveSpiders] Built {built.Count} spider prefab(s) in {OutFolder} and hooked the spawner onto Cave_Moon.");
    }

    // The pack's own AI + sound scripts (their wander brain would fight ours)
    // and its CharacterController (a spider on a roof can't use one).
    static void Strip(GameObject go)
    {
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            string ns = mb.GetType().Namespace ?? "";
            if (ns.StartsWith("Polyperfect") || mb.GetType().Assembly.GetName().Name.StartsWith("Polyperfect"))
                Object.DestroyImmediate(mb);
        }
        foreach (var cc in go.GetComponentsInChildren<CharacterController>(true)) Object.DestroyImmediate(cc);
        foreach (var ag in go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) Object.DestroyImmediate(ag);
        foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(a);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
    }

    static void Equip(GameObject go, AudioClip bite, AudioClip call, AudioClip call2, AudioClip walk)
    {
        go.transform.localScale = Vector3.one;
        go.layer = 0;

        // Capsule fitted to the mesh, lying along whichever horizontal axis is longer.
        var b = LocalBounds(go);
        var cap = go.AddComponent<CapsuleCollider>();
        bool alongZ = b.size.z >= b.size.x;
        cap.direction = alongZ ? 2 : 0;
        float length = alongZ ? b.size.z : b.size.x;
        float width = alongZ ? b.size.x : b.size.z;
        cap.radius = Mathf.Max(0.08f, Mathf.Min(width, b.size.y * 1.6f) * 0.4f);
        cap.height = Mathf.Max(cap.radius * 2f, length * 0.85f);
        cap.center = new Vector3(b.center.x, Mathf.Max(cap.radius, b.center.y), b.center.z);

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        var anim = go.GetComponentInChildren<Animator>(true);
        if (anim != null) { anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms; }

        var spider = go.AddComponent<CaveSpider>();
        spider.biteClip = bite;
        spider.alertClip = call;
        spider.walkLoopClip = walk;
        spider.deathClip = call2 != null ? call2 : call;
    }

    static Bounds LocalBounds(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>(true);
        var inv = go.transform.worldToLocalMatrix;
        bool has = false;
        var b = new Bounds();
        foreach (var r in rends)
        {
            var wb = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var l = inv.MultiplyPoint3x4(c);
                if (!has) { b = new Bounds(l, Vector3.zero); has = true; } else b.Encapsulate(l);
            }
        }
        return has ? b : new Bounds(new Vector3(0, 0.25f, 0), new Vector3(0.6f, 0.5f, 0.6f));
    }

    static void HookSpawner(GameObject[] spiders)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(CavePrefab) == null)
        {
            Debug.LogError($"[CaveSpiders] {CavePrefab} not found — spiders built but not hooked to the caves.");
            return;
        }
        var root = PrefabUtility.LoadPrefabContents(CavePrefab);
        try
        {
            var sp = root.GetComponent<CaveSpiderSpawner>();
            if (sp == null) sp = root.AddComponent<CaveSpiderSpawner>();
            sp.spiderPrefabs = spiders;
            PrefabUtility.SaveAsPrefabAsset(root, CavePrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
