using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// D9 "The Red Forest": endless dark-red pines under heavy fog. Trees reshuffle when
/// unobserved (D1's rule, sparser); a glowing white doe stands frozen while watched
/// and walks toward the exit clearing while unseen — let it lead you out. A soft bell
/// hum on the doe scales with gaze alignment so you can re-find her by ear.
/// </summary>
public class RedForestController : MonoBehaviour
{
    class Cell
    {
        public Vector2Int coord;
        public int seed;
        public GameObject tree;                 // null when this cell rolled empty
        public readonly ObservationTracker tracker = new ObservationTracker();
    }

    readonly Dictionary<Vector2Int, Cell> _cells = new Dictionary<Vector2Int, Cell>();
    readonly Stack<GameObject> _treePool = new Stack<GameObject>();
    readonly List<Vector2Int> _toDespawn = new List<Vector2Int>();

    Transform _root;
    Material _trunkMat, _canopyMat;
    Transform _doe;
    Rigidbody _doeRb;
    readonly ObservationTracker _doeTracker = new ObservationTracker();
    bool _doeObservedNow = true;
    AudioSource _doeChime;
    Vector3 _clearingPos;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        _trunkMat  = DimensionSceneUtil.Mat(new Color(0.22f, 0.08f, 0.06f), 0.05f);
        _canopyMat = DimensionSceneUtil.Mat(new Color(0.30f, 0.05f, 0.05f), 0.05f);
        var groundMat = DimensionSceneUtil.Mat(new Color(0.14f, 0.05f, 0.05f), 0.05f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Ground",
            new Vector3(0f, -0.5f, 0f), new Vector3(2000f, 1f, 2000f), groundMat, _root);
        DimensionSceneUtil.CreateDirectionalLight(new Color(0.9f, 0.35f, 0.3f), 0.45f, new Vector3(30f, -40f, 0f), true);

        // The exit clearing sits far out in a random direction; the doe knows the way.
        float a = Random.value * Mathf.PI * 2f;
        _clearingPos = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * clearingDistance;
        BuildClearing();
        BuildDoe();

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(70f, 2f, 0.06f), 500f, 1f);
    }

    void BuildClearing()
    {
        var stoneMat = DimensionSceneUtil.Mat(new Color(0.30f, 0.22f, 0.20f), 0.1f);
        var clearing = new GameObject("Clearing");
        clearing.transform.SetParent(_root, false);
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.PI / 4f;
            DimensionSceneUtil.Block(PrimitiveType.Cube, "Stone",
                new Vector3(Mathf.Cos(a) * 4f, 0.4f, Mathf.Sin(a) * 4f),
                new Vector3(1.1f, 0.8f, 0.7f), stoneMat, clearing.transform)
                .transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
        }
        // Soft white light pillar — the far-visible landmark once you get close enough
        // for it to pierce the fog.
        var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.name = "LightPillar";
        pillar.transform.SetParent(clearing.transform, false);
        pillar.transform.localPosition = new Vector3(0f, 9f, 0f);
        pillar.transform.localScale = new Vector3(2.6f, 9f, 2.6f);
        Destroy(pillar.GetComponent<Collider>());
        var beamMat = new Material(Shader.Find("Dimensions/BeamAdditive"));
        beamMat.SetColor("_Color", new Color(1f, 0.9f, 0.85f, 0.10f));
        pillar.GetComponent<Renderer>().sharedMaterial = beamMat;
        var lightGo = new GameObject("ClearingLight");
        lightGo.transform.SetParent(clearing.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 3f, 0f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point; l.range = 24f; l.intensity = 2.2f;
        l.color = new Color(1f, 0.9f, 0.8f);
        DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1.5f, 0f),
            new Vector3(3f, 3f, 3f), LevelPortal.PortalAction.EnterInterior, nextScene, clearing.transform);
        clearing.transform.position = _clearingPos;
    }

    void BuildDoe()
    {
        var white = DimensionSceneUtil.EmissiveMat(new Color(0.95f, 0.95f, 1f), 1.6f);
        var doe = new GameObject("Doe");
        doe.transform.SetParent(_root, false);
        _doe = doe.transform;
        _doeRb = doe.AddComponent<Rigidbody>();
        _doeRb.isKinematic = true;

        Part(PrimitiveType.Sphere, "Body", new Vector3(0f, 1.15f, 0f), new Vector3(0.75f, 0.85f, 1.6f), white, doe.transform);
        Part(PrimitiveType.Sphere, "Head", new Vector3(0f, 1.8f, 0.85f), Vector3.one * 0.45f, white, doe.transform);
        Part(PrimitiveType.Sphere, "EarL", new Vector3(-0.16f, 2.1f, 0.8f), new Vector3(0.1f, 0.3f, 0.12f), white, doe.transform);
        Part(PrimitiveType.Sphere, "EarR", new Vector3(0.16f, 2.1f, 0.8f), new Vector3(0.1f, 0.3f, 0.12f), white, doe.transform);
        Part(PrimitiveType.Cylinder, "LegFL", new Vector3(-0.25f, 0.4f, 0.55f), new Vector3(0.12f, 0.4f, 0.12f), white, doe.transform);
        Part(PrimitiveType.Cylinder, "LegFR", new Vector3(0.25f, 0.4f, 0.55f), new Vector3(0.12f, 0.4f, 0.12f), white, doe.transform);
        Part(PrimitiveType.Cylinder, "LegBL", new Vector3(-0.25f, 0.4f, -0.55f), new Vector3(0.12f, 0.4f, 0.12f), white, doe.transform);
        Part(PrimitiveType.Cylinder, "LegBR", new Vector3(0.25f, 0.4f, -0.55f), new Vector3(0.12f, 0.4f, 0.12f), white, doe.transform);

        var lightGo = new GameObject("DoeGlow");
        lightGo.transform.SetParent(doe.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point; l.range = 14f; l.intensity = 1.8f;
        l.color = new Color(0.9f, 0.92f, 1f);

        // She starts a little way toward the clearing, in view of the spawn.
        Vector3 dir = _clearingPos.normalized;
        _doe.SetPositionAndRotation(dir * 14f, Quaternion.LookRotation(dir, Vector3.up));

        _doeChime = DimensionSceneUtil.LoopingAudio(doe, DimensionSceneUtil.ToneClip(660f, 2f, 0.4f), 220f, 1f);
    }

    // Doe parts must not carry colliders — she is scenery that walks, not a platform.
    static void Part(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat, Transform parent)
    {
        var go = DimensionSceneUtil.Block(type, name, Vector3.zero, scale, mat, parent);
        go.transform.localPosition = pos;
        Destroy(go.GetComponent<Collider>());
    }

    void Update()
    {
        var cam = ObserverState.Cam;
        if (cam == null) return;
        if (!_atmosApplied)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.30f, 0.10f, 0.09f),
                fog: new Color(0.32f, 0.07f, 0.06f), fogDensity: 0.035f,
                background: new Color(0.25f, 0.05f, 0.05f));
            _atmosApplied = true;
        }

        Vector3 p = cam.transform.position;
        var playerCell = new Vector2Int(Mathf.RoundToInt(p.x / cellSize), Mathf.RoundToInt(p.z / cellSize));

        for (int dx = -radiusCells; dx <= radiusCells; dx++)
            for (int dz = -radiusCells; dz <= radiusCells; dz++)
            {
                var c = new Vector2Int(playerCell.x + dx, playerCell.y + dz);
                if (!_cells.ContainsKey(c)) SpawnCell(c);
            }
        _toDespawn.Clear();
        foreach (var kv in _cells)
            if (Mathf.Max(Mathf.Abs(kv.Key.x - playerCell.x), Mathf.Abs(kv.Key.y - playerCell.y)) > radiusCells + 1)
                _toDespawn.Add(kv.Key);
        foreach (var c in _toDespawn) DespawnCell(c);

        foreach (var kv in _cells)
        {
            Cell cell = kv.Value;
            cell.tracker.Tick(CellBounds(cell.coord), out bool justLost, observeMaxDistance);
            if (justLost && cell.coord != playerCell) Reroll(cell);
        }

        // Doe observation + gaze-reactive chime (the "audio radar" pattern).
        var db = new Bounds(_doe.position + Vector3.up * 1.2f, new Vector3(2.6f, 2.8f, 2.6f));
        _doeObservedNow = _doeTracker.Tick(db, out _, float.PositiveInfinity);
        if (_doeChime != null)
        {
            Vector3 to = _doe.position - cam.transform.position;
            float align = Vector3.Dot(cam.transform.forward, to.normalized);
            _doeChime.volume = Mathf.Lerp(0.05f, 1f, Mathf.InverseLerp(0.2f, 0.95f, align));
        }
    }

    void FixedUpdate()
    {
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null || _doeObservedNow) return;

        // She only walks while unseen, never abandons you (leash), and stops at the clearing.
        Vector3 toClearing = _clearingPos - _doeRb.position; toClearing.y = 0f;
        if (toClearing.magnitude < 4f) return;
        Vector3 toPlayer = _player.Rigidbody.position - _doeRb.position; toPlayer.y = 0f;
        if (toPlayer.magnitude > doeLeash) return;

        Vector3 dir = toClearing.normalized;
        _doeRb.MovePosition(_doeRb.position + dir * doeSpeed * Time.fixedDeltaTime);
        _doeRb.MoveRotation(Quaternion.LookRotation(dir, Vector3.up));
    }

    Bounds CellBounds(Vector2Int c) =>
        new Bounds(new Vector3(c.x * cellSize, 4f, c.y * cellSize),
                   new Vector3(cellSize, 9f, cellSize));

    void SpawnCell(Vector2Int coord)
    {
        var cell = new Cell { coord = coord, seed = Hash(coord.x, coord.y, worldSeed) };
        _cells[coord] = cell;
        BuildTree(cell);
    }

    void DespawnCell(Vector2Int coord)
    {
        ReturnTree(_cells[coord]);
        _cells.Remove(coord);
    }

    void Reroll(Cell cell)
    {
        cell.seed = unchecked(cell.seed * 7919 + 12345);
        BuildTree(cell);
        cell.tracker.Reset();
    }

    void BuildTree(Cell cell)
    {
        ReturnTree(cell);
        // No trees inside the clearing or right at the spawn point.
        Vector3 center = new Vector3(cell.coord.x * cellSize, 0f, cell.coord.y * cellSize);
        if ((center - _clearingPos).sqrMagnitude < clearingRadius * clearingRadius) return;
        if (center.sqrMagnitude < 36f) return;
        if (Rand01(cell.seed, 1) >= treeChance) return;

        GameObject tree = _treePool.Count > 0 ? _treePool.Pop() : NewTree();
        tree.SetActive(true);
        float jx = (Rand01(cell.seed, 2) - 0.5f) * (cellSize - 3f);
        float jz = (Rand01(cell.seed, 3) - 0.5f) * (cellSize - 3f);
        tree.transform.SetPositionAndRotation(center + new Vector3(jx, 0f, jz),
            Quaternion.Euler(0f, Rand01(cell.seed, 4) * 360f, 0f));
        cell.tree = tree;
    }

    GameObject NewTree()
    {
        var tree = new GameObject("Pine");
        tree.transform.SetParent(_root, false);
        DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Trunk",
            new Vector3(0f, 2.2f, 0f), new Vector3(0.5f, 2.2f, 0.5f), _trunkMat, tree.transform);
        var c1 = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Canopy1",
            new Vector3(0f, 5f, 0f), new Vector3(3.4f, 2.8f, 3.4f), _canopyMat, tree.transform);
        var c2 = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Canopy2",
            new Vector3(0f, 7f, 0f), new Vector3(2.2f, 2f, 2.2f), _canopyMat, tree.transform);
        Destroy(c1.GetComponent<Collider>());
        Destroy(c2.GetComponent<Collider>());
        return tree;
    }

    void ReturnTree(Cell cell)
    {
        if (cell.tree == null) return;
        cell.tree.SetActive(false);
        _treePool.Push(cell.tree);
        cell.tree = null;
    }

    static int Hash(int x, int y, int seed) =>
        unchecked(x * 73856093 ^ y * 19349663 ^ seed * 83492791);

    static float Rand01(int seed, int salt) =>
        (Mathf.Abs(unchecked(seed * (int)2654435761 + salt * 40503)) % 10000) / 10000f;

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Forest")]
    [Tooltip("Grid pitch between potential trees (metres).")]
    public float cellSize = 9f;
    [Tooltip("Cells kept alive in each direction around the player.")]
    public int radiusCells = 6;
    [Tooltip("Chance a cell grows a pine.")]
    [Range(0f, 1f)] public float treeChance = 0.7f;
    [Tooltip("Deterministic world seed.")]
    public int worldSeed = 909;
    [Tooltip("Beyond this distance a cell counts as unobserved even on screen.")]
    public float observeMaxDistance = 70f;

    [Header("Doe")]
    [Tooltip("Walk speed while unseen (m/s).")]
    public float doeSpeed = 3.2f;
    [Tooltip("She waits if you fall further behind than this.")]
    public float doeLeash = 32f;

    [Header("Exit clearing")]
    [Tooltip("Distance from spawn to the exit clearing.")]
    public float clearingDistance = 130f;
    [Tooltip("Tree-free radius around the clearing.")]
    public float clearingRadius = 14f;
    [Tooltip("Scene the clearing portal leads to.")]
    public string nextScene = "D10_SaltFlats";
}
