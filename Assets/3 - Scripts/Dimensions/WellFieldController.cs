using UnityEngine;

/// <summary>
/// D2 "Dune Sea": static procedural dunes; wells relocate whenever they leave your view.
/// One true well (audio + glow tells) exits to D3 — the others dump you somewhere else.
/// </summary>
public class WellFieldController : MonoBehaviour
{
    Transform _root;
    Transform[] _wells;
    ObservationTracker[] _trackers;
    Material _sandMat, _stoneMat, _holeMat;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        _sandMat  = DimensionSceneUtil.Mat(new Color(0.85f, 0.72f, 0.45f), 0.05f);
        _stoneMat = DimensionSceneUtil.Mat(new Color(0.45f, 0.42f, 0.38f), 0.05f);
        _holeMat  = DimensionSceneUtil.Mat(new Color(0.02f, 0.02f, 0.03f), 0f);

        BuildTerrain();
        DimensionSceneUtil.CreateDirectionalLight(new Color(1f, 0.95f, 0.8f), 1.25f, new Vector3(55f, 40f, 0f), true);

        _wells = new Transform[wellCount];
        _trackers = new ObservationTracker[wellCount];
        for (int i = 0; i < wellCount; i++)
        {
            _wells[i] = BuildWell(i == 0).transform;
            _trackers[i] = new ObservationTracker();
            PlaceWell(i, Vector3.zero, initial: true);
        }
    }

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.55f, 0.48f, 0.36f),
                fog: new Color(0.88f, 0.78f, 0.55f), fogDensity: 0.012f,
                background: new Color(0.92f, 0.84f, 0.62f));
            _atmosApplied = true;
        }
        var cam = ObserverState.Cam;
        if (cam == null) return;

        for (int i = 0; i < wellCount; i++)
        {
            var b = new Bounds(_wells[i].position + Vector3.up * 0.6f, new Vector3(3.5f, 2.5f, 3.5f));
            _trackers[i].Tick(b, out bool justLost, float.PositiveInfinity);
            if (justLost) PlaceWell(i, cam.transform.position, initial: false);
        }
    }

    /// <summary>Deterministic dune height — same function shapes the mesh and re-seats wells.</summary>
    public float DuneHeight(float x, float z)
    {
        float f1 = 1f / 70f, f2 = 1f / 23f;
        return Mathf.PerlinNoise(x * f1 + 31.7f, z * f1 + 12.3f) * duneAmplitude
             + Mathf.PerlinNoise(x * f2 + 71.1f, z * f2 + 47.9f) * (duneAmplitude * 0.25f);
    }

    void BuildTerrain()
    {
        int n = 128;
        float half = terrainSize * 0.5f, step = terrainSize / n;
        var verts = new Vector3[(n + 1) * (n + 1)];
        var uvs = new Vector2[verts.Length];
        for (int z = 0, i = 0; z <= n; z++)
            for (int x = 0; x <= n; x++, i++)
            {
                float wx = x * step - half, wz = z * step - half;
                verts[i] = new Vector3(wx, DuneHeight(wx, wz), wz);
                uvs[i] = new Vector2(x / (float)n, z / (float)n);
            }
        var tris = new int[n * n * 6];
        for (int z = 0, t = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * (n + 1) + x;
                tris[t++] = i; tris[t++] = i + n + 1; tris[t++] = i + 1;
                tris[t++] = i + 1; tris[t++] = i + n + 1; tris[t++] = i + n + 2;
            }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = verts; mesh.triangles = tris; mesh.uv = uvs;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();

        var go = new GameObject("Dunes");
        go.layer = DimensionSceneUtil.WalkableLayer;
        go.transform.SetParent(_root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = _sandMat;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    GameObject BuildWell(bool isTrue)
    {
        var well = new GameObject(isTrue ? "Well_TRUE" : "Well");
        well.transform.SetParent(_root, false);
        // Octagon rim of stone blocks — low enough to hop over into the mouth.
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.PI / 4f;
            var block = DimensionSceneUtil.Block(PrimitiveType.Cube, "Rim",
                new Vector3(Mathf.Cos(a) * 1.3f, 0.45f, Mathf.Sin(a) * 1.3f),
                new Vector3(1.05f, 0.9f, 0.5f), _stoneMat, well.transform);
            block.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
        }
        // The "hole": a black disc just below the rim.
        var disc = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Hole",
            new Vector3(0f, 0.3f, 0f), new Vector3(2.2f, 0.03f, 2.2f), _holeMat, well.transform);
        Destroy(disc.GetComponent<Collider>());

        if (isTrue)
        {
            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(well.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point; l.range = 12f; l.intensity = 2.5f;
            l.color = new Color(1f, 0.75f, 0.35f);
            // Loot-beam: a tall emissive column — the point light is invisible in full
            // daylight and a sine hum can't be direction-traced by ear, so this is the
            // actual find mechanic. Relocates with the well, so it stays a chase.
            var beam = DimensionSceneUtil.Block(PrimitiveType.Cube, "Beam",
                new Vector3(0f, 25f, 0f), new Vector3(0.7f, 50f, 0.7f),
                DimensionSceneUtil.EmissiveMat(new Color(1f, 0.7f, 0.3f), 3f), well.transform);
            Destroy(beam.GetComponent<Collider>());
            DimensionSceneUtil.LoopingAudio(well, DimensionSceneUtil.ToneClip(528f, 2f, 0.5f), 260f, 1f);
            DimensionSceneUtil.CreatePortal("ToD3", new Vector3(0f, 0.5f, 0f),
                new Vector3(1.7f, 0.8f, 1.7f), LevelPortal.PortalAction.EnterInterior, nextScene, well.transform);
        }
        else
        {
            var trig = new GameObject("WrongWell");
            trig.transform.SetParent(well.transform, false);
            trig.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            var box = trig.AddComponent<BoxCollider>();
            box.isTrigger = true; box.size = new Vector3(1.7f, 0.8f, 1.7f);
            trig.AddComponent<WellTeleportTrigger>().owner = this;
        }
        return well;
    }

    void PlaceWell(int i, Vector3 aroundPos, bool initial)
    {
        // Sample until the spot is off-screen (max 8 tries — relocations must not pop
        // into view). Initial placement scatters around the origin instead.
        Vector3 best = _wells[i].position;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float d = Random.Range(relocateMinDistance, relocateMaxDistance);
            Vector3 c = initial ? Vector3.zero : aroundPos;
            float x = c.x + Mathf.Cos(a) * d, z = c.z + Mathf.Sin(a) * d;
            x = Mathf.Clamp(x, -terrainSize * 0.45f, terrainSize * 0.45f);
            z = Mathf.Clamp(z, -terrainSize * 0.45f, terrainSize * 0.45f);
            best = new Vector3(x, DuneHeight(x, z), z);
            if (initial || !ObserverState.IsObserved(new Bounds(best + Vector3.up, Vector3.one * 4f)))
                break;
        }
        _wells[i].position = best;
        _trackers[i].Reset();
    }

    /// <summary>Wrong-well punishment: dumped on a random distant dune, no damage.</summary>
    public void TeleportPlayerToRandomDune()
    {
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null) return;
        float a = Random.value * Mathf.PI * 2f;
        float d = Random.Range(120f, 200f);
        Vector3 p = _player.Rigidbody.position;
        float x = Mathf.Clamp(p.x + Mathf.Cos(a) * d, -terrainSize * 0.45f, terrainSize * 0.45f);
        float z = Mathf.Clamp(p.z + Mathf.Sin(a) * d, -terrainSize * 0.45f, terrainSize * 0.45f);
        _player.Rigidbody.position = new Vector3(x, DuneHeight(x, z) + 2f, z);
        _player.Rigidbody.velocity = Vector3.zero;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Terrain")]
    [Tooltip("Square dune mesh edge length (metres). Static — never reshuffles.")]
    public float terrainSize = 800f;
    [Tooltip("Main dune height (metres).")]
    public float duneAmplitude = 8f;

    [Header("Wells")]
    [Tooltip("Total wells including the one true well.")]
    public int wellCount = 8;
    [Tooltip("Min distance from the player a relocating well may land.")]
    public float relocateMinDistance = 40f;
    [Tooltip("Max distance from the player a relocating well may land.")]
    public float relocateMaxDistance = 100f;
    [Tooltip("Scene the true well leads to.")]
    public string nextScene = "D3_LongDark";
}
