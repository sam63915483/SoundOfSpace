using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Splits every planet's terrain mesh into cube-face chunks at runtime, so the
/// renderer can cull the far side of a planet and so a point light only
/// re-draws the patch of ground under it instead of the whole planet.
///
/// WHY: CelestialBodyGenerator renders each planet as ONE "Terrain Mesh"
/// renderer (~2 M triangles at LOD 0). Unity culls per renderer, so the half of
/// the sphere behind the horizon is drawn every frame, and in the built-in
/// forward renderer every pixel light whose range touches a renderer's bounds
/// re-draws that renderer — a 22 m lantern re-drew all 2 M triangles. Measured
/// 2026-09-11 (PerfTrace run 2, RTX 4060 laptop): 14 lanterns + fireflies +
/// tunnel lights = 27–41 M triangles/frame, the GPU wall behind "50 fps".
///
/// HOW: the generator's meshes are left completely alone (that code is
/// off-limits). This watches each body's "Terrain Mesh" MeshFilter; whenever it
/// shows a big mesh, a chunked copy is built once (cube face × K×K grid by
/// triangle centroid, ~20 k triangles per chunk, spread over frames) under a
/// child object, the chunk objects are shown and the original renderer gets
/// forceRenderingOff. LOD swaps (LODHandler → SetLOD) are followed by watching
/// the sharedMesh reference; a mesh edited in place afterwards (PlanetHolePuncher
/// bores the moon tunnel by SetTriangles on the same Mesh) is caught by its index
/// count changing and the chunk set is rebuilt. Colliders, grass raycasts, the
/// ocean/atmosphere post-process (depth) and EclipseShadowGate (which scans the
/// generator's children, so it sees the chunks) are untouched.
///
/// Kill switch: FeatureVault.PlanetChunks = false → nothing is created.
/// Not a MainMenu-skipping singleton, so no seeding needed (trap #1).
/// </summary>
public class PlanetChunker : MonoBehaviour
{
    public static PlanetChunker Instance { get; private set; }

    const int MinTrianglesToChunk = 60_000;   // LOD 2 and dwarf placeholders stay single meshes
    const int TargetTrianglesPerChunk = 20_000;
    const int MaxGridPerFace = 8;
    const int ChunksBuiltPerFrame = 6;
    const float RescanSec = 1f;

    class ChunkSet
    {
        public Mesh source;
        public int indexCount;
        public GameObject root;
        public readonly List<Mesh> meshes = new List<Mesh>();
        public bool ready, failed;
    }

    class Tracked
    {
        public CelestialBody body;
        public Transform terrain;
        public MeshFilter filter;
        public MeshRenderer renderer;
        public readonly Dictionary<Mesh, ChunkSet> sets = new Dictionary<Mesh, ChunkSet>();
        public ChunkSet active;
        public bool building;
    }

    readonly List<Tracked> _tracked = new List<Tracked>();
    readonly HashSet<CelestialBody> _known = new HashSet<CelestialBody>();
    float _rescan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!FeatureVault.PlanetChunks) return;
        if (Instance != null) return;
        var go = new GameObject("[PlanetChunker]");
        DontDestroyOnLoad(go);
        go.AddComponent<PlanetChunker>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        foreach (var t in _tracked) Discard(t, null);
        _tracked.Clear();
    }

    void OnSceneUnloaded(Scene s)
    {
        // Bodies die with their scene; free the chunk meshes (runtime Mesh
        // assets are not destroyed with their GameObjects).
        for (int i = _tracked.Count - 1; i >= 0; i--)
        {
            if (_tracked[i].terrain == null) { Discard(_tracked[i], null); _tracked.RemoveAt(i); }
        }
        _known.RemoveWhere(b => b == null);
    }

    // ==============================================================================
    void Update()
    {
        _rescan -= Time.unscaledDeltaTime;
        if (_rescan <= 0f) { _rescan = RescanSec; Rescan(); }

        for (int i = _tracked.Count - 1; i >= 0; i--)
        {
            var t = _tracked[i];
            if (t.terrain == null || t.filter == null || t.renderer == null)
            {
                Discard(t, null); _tracked.RemoveAt(i);
                continue;
            }
            Mesh mesh = t.filter.sharedMesh;
            ChunkSet want = null;
            if (mesh != null && t.sets.TryGetValue(mesh, out var s) && s.ready) want = s;

            if (t.active != want)
            {
                if (t.active != null && t.active.root != null) t.active.root.SetActive(false);
                t.active = want;
                t.renderer.forceRenderingOff = want != null;
            }
            if (want != null && want.root != null)
            {
                // mirror whoever toggles the original renderer (LOD, culling, debug keys)
                bool show = t.renderer.enabled;
                if (want.root.activeSelf != show) want.root.SetActive(show);
            }
        }
    }

    void Rescan()
    {
        foreach (var body in NBodySimulation.Bodies)
        {
            if (body == null || _known.Contains(body)) continue;
            var gen = body.GetComponentInChildren<CelestialBodyGenerator>(true);
            if (gen == null) continue;                       // dwarf placeholders, black hole, sun
            var terrain = gen.transform.Find("Terrain Mesh");
            if (terrain == null) continue;                   // not generated yet — try again next second
            var mf = terrain.GetComponent<MeshFilter>();
            var mr = terrain.GetComponent<MeshRenderer>();
            if (mf == null || mr == null) continue;
            _known.Add(body);
            _tracked.Add(new Tracked { body = body, terrain = terrain, filter = mf, renderer = mr });
        }

        foreach (var t in _tracked)
        {
            if (t.terrain == null || t.filter == null || t.building) continue;
            Mesh mesh = t.filter.sharedMesh;
            if (mesh == null) continue;
            int indexCount = TotalIndexCount(mesh);
            if (t.sets.TryGetValue(mesh, out var s))
            {
                if (s.failed) continue;
                if (s.indexCount != indexCount)
                {
                    // edited in place (moon tunnel punch) → rebuild
                    Discard(t, mesh);
                    StartCoroutine(Build(t, mesh));
                }
                continue;
            }
            if (indexCount / 3 < MinTrianglesToChunk) continue;
            if (!mesh.isReadable)
            {
                t.sets[mesh] = new ChunkSet { source = mesh, indexCount = indexCount, failed = true };
                Debug.LogWarning("[PlanetChunker] " + t.body.bodyName + ": mesh not readable, left as one renderer");
                continue;
            }
            StartCoroutine(Build(t, mesh));
        }
    }

    static int TotalIndexCount(Mesh m)
    {
        int n = 0;
        for (int sm = 0; sm < m.subMeshCount; sm++) n += (int)m.GetIndexCount(sm);
        return n;
    }

    void Discard(Tracked t, Mesh onlyThis)
    {
        var keys = new List<Mesh>(t.sets.Keys);
        foreach (var k in keys)
        {
            if (onlyThis != null && k != onlyThis) continue;
            var s = t.sets[k];
            if (t.active == s)
            {
                t.active = null;
                if (t.renderer != null) t.renderer.forceRenderingOff = false;
            }
            if (s.root != null) Destroy(s.root);
            foreach (var m in s.meshes) if (m != null) Destroy(m);
            s.meshes.Clear();
            t.sets.Remove(k);
        }
    }

    // ==============================================================================
    IEnumerator Build(Tracked t, Mesh mesh)
    {
        t.building = true;
        var set = new ChunkSet { source = mesh, indexCount = TotalIndexCount(mesh) };
        t.sets[mesh] = set;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // ---- read everything once
        var verts = new List<Vector3>(); mesh.GetVertices(verts);
        var normals = new List<Vector3>(); if (mesh.HasVertexAttribute(VertexAttribute.Normal)) mesh.GetNormals(normals);
        var tangents = new List<Vector4>(); if (mesh.HasVertexAttribute(VertexAttribute.Tangent)) mesh.GetTangents(tangents);
        var colors = new List<Color>(); if (mesh.HasVertexAttribute(VertexAttribute.Color)) mesh.GetColors(colors);
        var uvDim = new int[4];
        var uv2 = new List<Vector2>[4]; var uv3 = new List<Vector3>[4]; var uv4 = new List<Vector4>[4];
        for (int ch = 0; ch < 4; ch++)
        {
            var attr = (VertexAttribute)((int)VertexAttribute.TexCoord0 + ch);
            if (!mesh.HasVertexAttribute(attr)) continue;
            uvDim[ch] = mesh.GetVertexAttributeDimension(attr);
            if (uvDim[ch] == 2) { uv2[ch] = new List<Vector2>(); mesh.GetUVs(ch, uv2[ch]); }
            else if (uvDim[ch] == 3) { uv3[ch] = new List<Vector3>(); mesh.GetUVs(ch, uv3[ch]); }
            else { uv4[ch] = new List<Vector4>(); mesh.GetUVs(ch, uv4[ch]); uvDim[ch] = 4; }
        }
        int subCount = mesh.subMeshCount;
        var tris = new List<int>[subCount];
        int triTotal = 0;
        for (int sm = 0; sm < subCount; sm++) { tris[sm] = new List<int>(); mesh.GetTriangles(tris[sm], sm); triTotal += tris[sm].Count / 3; }

        // ---- bucket triangles: cube face (6) × K×K by centroid direction
        int K = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(triTotal / (float)TargetTrianglesPerChunk / 6f)), 1, MaxGridPerFace);
        int chunkCount = 6 * K * K;
        var bucket = new List<int>[chunkCount * subCount];
        for (int sm = 0; sm < subCount; sm++)
        {
            var src = tris[sm];
            for (int i = 0; i + 2 < src.Count; i += 3)
            {
                int a = src[i], b = src[i + 1], c = src[i + 2];
                Vector3 p = verts[a] + verts[b] + verts[c];
                float ax = Mathf.Abs(p.x), ay = Mathf.Abs(p.y), az = Mathf.Abs(p.z);
                int face; float u, v, d;
                if (ax >= ay && ax >= az) { d = ax; face = p.x >= 0 ? 0 : 1; u = p.y; v = p.z; }
                else if (ay >= az)        { d = ay; face = p.y >= 0 ? 2 : 3; u = p.x; v = p.z; }
                else                      { d = az; face = p.z >= 0 ? 4 : 5; u = p.x; v = p.y; }
                if (d < 1e-6f) d = 1e-6f;
                int cu = Mathf.Clamp((int)((u / d + 1f) * 0.5f * K), 0, K - 1);
                int cv = Mathf.Clamp((int)((v / d + 1f) * 0.5f * K), 0, K - 1);
                int chunk = (face * K + cv) * K + cu;
                int key = chunk * subCount + sm;
                if (bucket[key] == null) bucket[key] = new List<int>(1024);
                bucket[key].Add(a); bucket[key].Add(b); bucket[key].Add(c);
            }
        }
        yield return null;
        if (t.terrain == null || t.filter == null) { t.building = false; yield break; }

        // ---- build chunk objects, a few per frame
        var root = new GameObject("Terrain Mesh Chunks");
        root.transform.SetParent(t.terrain, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;
        root.layer = t.terrain.gameObject.layer;
        root.SetActive(false);
        set.root = root;

        var remap = new int[verts.Count];
        for (int i = 0; i < remap.Length; i++) remap[i] = -1;
        var touched = new List<int>(65536);
        var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nt = new List<Vector4>(); var nc = new List<Color>();
        var nuv2 = new List<Vector2>[4]; var nuv3 = new List<Vector3>[4]; var nuv4 = new List<Vector4>[4];
        for (int ch = 0; ch < 4; ch++) { if (uv2[ch] != null) nuv2[ch] = new List<Vector2>(); if (uv3[ch] != null) nuv3[ch] = new List<Vector3>(); if (uv4[ch] != null) nuv4[ch] = new List<Vector4>(); }
        var chunkTris = new List<int>[subCount];
        for (int sm = 0; sm < subCount; sm++) chunkTris[sm] = new List<int>();

        int builtThisFrame = 0, made = 0;
        for (int chunk = 0; chunk < chunkCount; chunk++)
        {
            bool any = false;
            for (int sm = 0; sm < subCount; sm++) if (bucket[chunk * subCount + sm] != null) { any = true; break; }
            if (!any) continue;

            nv.Clear(); nn.Clear(); nt.Clear(); nc.Clear();
            for (int ch = 0; ch < 4; ch++) { nuv2[ch]?.Clear(); nuv3[ch]?.Clear(); nuv4[ch]?.Clear(); }
            for (int sm = 0; sm < subCount; sm++)
            {
                chunkTris[sm].Clear();
                var src = bucket[chunk * subCount + sm];
                if (src == null) continue;
                for (int i = 0; i < src.Count; i++)
                {
                    int old = src[i];
                    int idx = remap[old];
                    if (idx < 0)
                    {
                        idx = nv.Count;
                        remap[old] = idx; touched.Add(old);
                        nv.Add(verts[old]);
                        if (normals.Count > 0) nn.Add(normals[old]);
                        if (tangents.Count > 0) nt.Add(tangents[old]);
                        if (colors.Count > 0) nc.Add(colors[old]);
                        for (int ch = 0; ch < 4; ch++)
                        {
                            if (uv2[ch] != null) nuv2[ch].Add(uv2[ch][old]);
                            else if (uv3[ch] != null) nuv3[ch].Add(uv3[ch][old]);
                            else if (uv4[ch] != null) nuv4[ch].Add(uv4[ch][old]);
                        }
                    }
                    chunkTris[sm].Add(idx);
                }
            }
            for (int i = 0; i < touched.Count; i++) remap[touched[i]] = -1;
            touched.Clear();

            var cm = new Mesh { name = mesh.name + " chunk " + chunk };
            cm.indexFormat = nv.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            cm.SetVertices(nv);
            if (nn.Count == nv.Count) cm.SetNormals(nn);
            if (nt.Count == nv.Count) cm.SetTangents(nt);
            if (nc.Count == nv.Count) cm.SetColors(nc);
            for (int ch = 0; ch < 4; ch++)
            {
                if (nuv2[ch] != null) cm.SetUVs(ch, nuv2[ch]);
                else if (nuv3[ch] != null) cm.SetUVs(ch, nuv3[ch]);
                else if (nuv4[ch] != null) cm.SetUVs(ch, nuv4[ch]);
            }
            cm.subMeshCount = subCount;
            for (int sm = 0; sm < subCount; sm++) cm.SetTriangles(chunkTris[sm], sm, false);
            cm.RecalculateBounds();
            cm.UploadMeshData(true);
            set.meshes.Add(cm);

            var go = new GameObject("Terrain Mesh Chunk " + chunk);
            go.transform.SetParent(root.transform, false);
            go.layer = root.layer;
            go.AddComponent<MeshFilter>().sharedMesh = cm;
            var r = go.AddComponent<MeshRenderer>();
            CopyRendererSettings(t.renderer, r);
            made++;

            if (++builtThisFrame >= ChunksBuiltPerFrame)
            {
                builtThisFrame = 0;
                yield return null;
                if (t.terrain == null || t.filter == null || t.renderer == null || root == null)
                {
                    foreach (var m in set.meshes) if (m != null) Destroy(m);
                    set.meshes.Clear();
                    if (root != null) Destroy(root);
                    t.sets.Remove(mesh);
                    t.building = false;
                    yield break;
                }
            }
        }

        set.ready = true;
        t.building = false;
        Debug.Log("[PlanetChunker] " + t.body.bodyName + ": " + triTotal + " tris → " + made + " chunks (K=" + K + ") in " + sw.ElapsedMilliseconds + " ms");
    }

    static void CopyRendererSettings(MeshRenderer from, MeshRenderer to)
    {
        to.sharedMaterials = from.sharedMaterials;
        to.shadowCastingMode = from.shadowCastingMode;
        to.receiveShadows = from.receiveShadows;
        to.lightProbeUsage = from.lightProbeUsage;
        to.reflectionProbeUsage = from.reflectionProbeUsage;
        to.motionVectorGenerationMode = from.motionVectorGenerationMode;
        to.allowOcclusionWhenDynamic = from.allowOcclusionWhenDynamic;
        to.renderingLayerMask = from.renderingLayerMask;
    }
}
