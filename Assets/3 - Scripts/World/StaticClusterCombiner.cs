using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Merges the static pieces of a few big hand-built clusters (the shuttle, the
/// moon base, the fish market, the start cabin) into a handful of meshes at
/// runtime, so the CPU submits one draw per material-and-area instead of one
/// per bolt.
///
/// WHY: the shuttle alone is 233 MeshRenderers and the main thread pays for
/// every renderer four times a frame (colour, depth pre-pass, two shadow
/// cascades). Static batching cannot do this because the clusters sit on planets
/// that move. Measured 2026-09-11: Camera.Render (cull + sort + submit) was
/// 2.8 ms of a 9 ms frame, and 3000 draws looking at the shuttle vs 1900 away.
///
/// WHAT IS LEFT ALONE (must keep moving / changing on its own):
///   • any object that has, or whose ancestor BELOW the cluster root has, a
///     MonoBehaviour, Animator, Animation, Rigidbody, Joint, ParticleSystem,
///     Canvas, Light or LODGroup — doors, screens, levers, the stasis pod, TV arm;
///   • SkinnedMeshRenderers, disabled renderers, renderers with a
///     MaterialPropertyBlock, transparent-queue materials (glass), unreadable
///     meshes, and names matching NameBlocklist.
/// The root's own scripts are fine: the combined meshes are children of the
/// root and move with it.
///
/// HOW: renderers are grouped by (material, layer, shadow flags, receiveShadows,
/// and a ~8 m spatial cell relative to the root) and merged with
/// Mesh.CombineMeshes relative to the root. Spatial cells keep each merged mesh
/// small so a cabin light only re-draws the chunk it touches. Originals keep
/// their colliders; only their MeshRenderer is disabled. Merged meshes
/// smaller than 35 cm on every axis cast no shadow (a bolt's shadow is
/// sub-pixel) — the rest keep the source renderer's shadow mode.
///
/// Kill switch: FeatureVault.ClusterCombine = false → nothing happens.
/// Not a MainMenu-skipping singleton (trap #1), no seeding needed.
/// </summary>
public class StaticClusterCombiner : MonoBehaviour
{
    public static StaticClusterCombiner Instance { get; private set; }

    static readonly string[] ClusterRoots = { "Shuttle_Lander", "MoonBaseINTER", "Fish_market_with_sections", "StartCabin", "BakeryMarket_no_dop", "ShipMarket" };
    static readonly string[] NameBlocklist = { "Screen", "Door", "Glass", "Window", "Fan", "Button", "Lever", "Display", "Monitor", "Light", "Lamp", "Valve", "Pod" };
    const float CellSize = 8f;
    const float TinyShadowSize = 0.35f;
    const float RescanSec = 2f;

    readonly HashSet<Transform> _done = new HashSet<Transform>();
    float _rescan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!FeatureVault.ClusterCombine) return;
        if (Instance != null) return;
        var go = new GameObject("[StaticClusterCombiner]");
        DontDestroyOnLoad(go);
        go.AddComponent<StaticClusterCombiner>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        _rescan -= Time.unscaledDeltaTime;
        if (_rescan > 0f) return;
        _rescan = RescanSec;
        _done.RemoveWhere(t => t == null);
        // Clusters live under celestial bodies; find them by name, once each.
        foreach (var body in NBodySimulation.Bodies)
        {
            if (body == null || _bodiesDone.Contains(body)) continue;
            _bodiesDone.Add(body);
            var all = body.GetComponentsInChildren<Transform>(false);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (_done.Contains(t)) continue;
                for (int n = 0; n < ClusterRoots.Length; n++)
                    if (t.name == ClusterRoots[n]) { _done.Add(t); Combine(t); break; }
            }
            TinyShadowsOff(body);
        }
    }
    readonly HashSet<CelestialBody> _bodiesDone = new HashSet<CelestialBody>();

    /// <summary>Renderers under 35 cm on every axis (bolts, cage-light bars, trim)
    /// stop casting shadows: their shadow is smaller than a pixel at any normal
    /// distance but each one still costs a shadow-map draw per cascade. Skips
    /// anything animated or under a script (held items, NPCs, cats).</summary>
    static void TinyShadowsOff(CelestialBody body)
    {
        int n = 0;
        foreach (var r in body.GetComponentsInChildren<MeshRenderer>(false))
        {
            if (r == null || r.shadowCastingMode == ShadowCastingMode.Off) continue;
            Vector3 s = r.bounds.size;
            if (s.x >= TinyShadowSize || s.y >= TinyShadowSize || s.z >= TinyShadowSize) continue;
            bool blocked = false;
            for (Transform p = r.transform; p != null && p != body.transform; p = p.parent)
                if (p.GetComponent<Animator>() != null || p.GetComponent<Animation>() != null || p.GetComponent<MonoBehaviour>() != null) { blocked = true; break; }
            if (blocked) continue;
            r.shadowCastingMode = ShadowCastingMode.Off;
            n++;
        }
        if (n > 0) Debug.Log("[StaticClusterCombiner] " + body.bodyName + ": shadows off on " + n + " tiny renderers");
    }

    // ---------------------------------------------------------------------------
    class Group
    {
        public Material material; public int layer; public ShadowCastingMode shadows; public bool receive;
        public readonly List<CombineInstance> parts = new List<CombineInstance>();
        public readonly List<Renderer> sources = new List<Renderer>();
        public int vertexCount; public Bounds bounds; public bool hasBounds;
    }

    static readonly System.Type[] Blockers =
    {
        typeof(MonoBehaviour), typeof(Animator), typeof(Animation), typeof(Rigidbody), typeof(Joint),
        typeof(ParticleSystem), typeof(Canvas), typeof(Light), typeof(LODGroup),
    };

    static bool HasBlocker(Transform t)
    {
        for (int i = 0; i < Blockers.Length; i++) if (t.GetComponent(Blockers[i]) != null) return true;
        return false;
    }

    static bool NameBlocked(string n)
    {
        for (int i = 0; i < NameBlocklist.Length; i++) if (n.IndexOf(NameBlocklist[i], System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    /// <summary>True if any transform strictly between root and t (or t itself) carries a blocker.</summary>
    static bool SubtreeBlocked(Transform root, Transform t)
    {
        for (Transform p = t; p != null && p != root; p = p.parent)
            if (HasBlocker(p) || NameBlocked(p.name)) return true;
        return false;
    }

    void Combine(Transform root)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var renderers = root.GetComponentsInChildren<MeshRenderer>(false);
        var groups = new Dictionary<string, Group>();
        Matrix4x4 worldToRoot = root.worldToLocalMatrix;
        int skipped = 0, taken = 0;

        foreach (var r in renderers)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) { skipped++; continue; }
            if (r.transform == root) { skipped++; continue; }
            if (r.HasPropertyBlock() || SubtreeBlocked(root, r.transform)) { skipped++; continue; }
            var mf = r.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) { skipped++; continue; }
            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0) { skipped++; continue; }
            bool ok = true;
            foreach (var m in mats) if (m == null || m.renderQueue > 2500) { ok = false; break; }   // glass / transparent stay separate
            if (!ok) { skipped++; continue; }

            Vector3 localCentre = worldToRoot.MultiplyPoint3x4(r.bounds.center);
            int cx = Mathf.FloorToInt(localCentre.x / CellSize), cy = Mathf.FloorToInt(localCentre.y / CellSize), cz = Mathf.FloorToInt(localCentre.z / CellSize);
            int subCount = Mathf.Min(mats.Length, mesh.subMeshCount);
            for (int s = 0; s < subCount; s++)
            {
                string key = mats[s].GetInstanceID() + "|" + r.gameObject.layer + "|" + (int)r.shadowCastingMode + "|" + (r.receiveShadows ? 1 : 0) + "|" + cx + "," + cy + "," + cz;
                if (!groups.TryGetValue(key, out var g))
                {
                    g = new Group { material = mats[s], layer = r.gameObject.layer, shadows = r.shadowCastingMode, receive = r.receiveShadows };
                    groups[key] = g;
                }
                g.parts.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = worldToRoot * r.transform.localToWorldMatrix });
                g.vertexCount += mesh.vertexCount;
                if (!g.hasBounds) { g.bounds = r.bounds; g.hasBounds = true; } else g.bounds.Encapsulate(r.bounds);
                if (!g.sources.Contains(r)) g.sources.Add(r);
            }
            taken++;
        }

        if (groups.Count == 0) { Debug.Log("[StaticClusterCombiner] " + root.name + ": nothing combinable (" + skipped + " skipped)"); return; }

        var holder = new GameObject(root.name + " Combined");
        holder.transform.SetParent(root, false);
        holder.transform.localPosition = Vector3.zero; holder.transform.localRotation = Quaternion.identity; holder.transform.localScale = Vector3.one;
        holder.layer = root.gameObject.layer;

        int made = 0, draws = 0;
        var disabled = new HashSet<Renderer>();
        foreach (var kv in groups)
        {
            var g = kv.Value;
            if (g.parts.Count < 2) continue;                       // a lone piece gains nothing
            var mesh = new Mesh { name = root.name + " combined " + made };
            if (g.vertexCount > 65000) mesh.indexFormat = IndexFormat.UInt32;
            try { mesh.CombineMeshes(g.parts.ToArray(), true, true, false); }
            catch (System.Exception e) { Debug.LogWarning("[StaticClusterCombiner] " + root.name + ": combine failed (" + e.Message + ")"); Destroy(mesh); continue; }
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);

            var go = new GameObject(root.name + " Combined " + made);
            go.transform.SetParent(holder.transform, false);
            go.layer = g.layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = g.material;
            mr.receiveShadows = g.receive;
            Vector3 size = g.bounds.size;
            bool tiny = size.x < TinyShadowSize && size.y < TinyShadowSize && size.z < TinyShadowSize;
            mr.shadowCastingMode = tiny ? ShadowCastingMode.Off : g.shadows;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            foreach (var src in g.sources) disabled.Add(src);
            made++; draws += g.parts.Count;
        }
        foreach (var src in disabled) if (src != null) src.enabled = false;

        Debug.Log("[StaticClusterCombiner] " + root.name + ": " + disabled.Count + " renderers (" + draws + " sub-meshes) → " + made + " combined meshes; "
                  + skipped + " left alone; " + sw.ElapsedMilliseconds + " ms");
    }
}
