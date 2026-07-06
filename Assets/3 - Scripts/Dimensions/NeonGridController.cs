using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// D16 "Neon Grid": a flat black city of glowing wireframe towers. Buildings reshuffle
/// their heights whenever they leave your view. The exit is a taxi — a warm light
/// cruising the streets while unseen, parked politely the moment you watch it. Corner
/// it with your eyes, walk up, get in.
/// </summary>
public class NeonGridController : MonoBehaviour
{
    class Cell
    {
        public Vector2Int coord;
        public int seed;
        public GameObject building;
        public GameObject streetLines;
        public readonly ObservationTracker tracker = new ObservationTracker();
    }

    readonly Dictionary<Vector2Int, Cell> _cells = new Dictionary<Vector2Int, Cell>();
    readonly Stack<GameObject> _buildingPool = new Stack<GameObject>();
    readonly Stack<GameObject> _streetPool = new Stack<GameObject>();
    readonly List<Vector2Int> _toDespawn = new List<Vector2Int>();

    Transform _root;
    Material _blockMat, _cyanMat, _magentaMat, _streetMat;
    Transform _taxi;
    Rigidbody _taxiRb;
    readonly ObservationTracker _taxiTracker = new ObservationTracker();
    bool _taxiObservedNow = true;
    Vector2Int _taxiTarget;                  // intersection (i,j) it's driving toward
    AudioSource _taxiHum;
    AudioSource _taxiHorn;
    AudioClip _hornClip;
    float _nextHonkTime;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        _blockMat   = DimensionSceneUtil.Mat(new Color(0.02f, 0.02f, 0.04f), 0.4f);
        _cyanMat    = DimensionSceneUtil.EmissiveMat(new Color(0.1f, 0.9f, 1f), 1.8f);
        _magentaMat = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.2f, 0.85f), 1.8f);
        _streetMat  = DimensionSceneUtil.EmissiveMat(new Color(0.15f, 0.5f, 0.6f), 0.8f);
        var groundMat = DimensionSceneUtil.Mat(new Color(0.015f, 0.015f, 0.03f), 0.5f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Ground",
            new Vector3(0f, -0.5f, 0f), new Vector3(2000f, 1f, 2000f), groundMat, _root);
        DimensionSceneUtil.CreateDirectionalLight(new Color(0.3f, 0.4f, 0.6f), 0.25f, new Vector3(50f, -30f, 0f), false);

        BuildTaxi();
        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(62f, 2f, 0.05f), 500f, 1f);
    }

    void BuildTaxi()
    {
        var bodyMat = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.75f, 0.15f), 1.6f);
        var taxi = new GameObject("Taxi");
        taxi.transform.SetParent(_root, false);
        _taxi = taxi.transform;
        _taxiRb = taxi.AddComponent<Rigidbody>();
        _taxiRb.isKinematic = true;
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Body",
            Vector3.zero, new Vector3(2.2f, 1.1f, 4.4f), bodyMat, taxi.transform)
            .transform.localPosition = new Vector3(0f, 0.8f, 0f);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Cabin",
            Vector3.zero, new Vector3(1.9f, 0.8f, 2.2f), _blockMat, taxi.transform)
            .transform.localPosition = new Vector3(0f, 1.7f, -0.3f);
        var sign = DimensionSceneUtil.Block(PrimitiveType.Cube, "RoofSign",
            Vector3.zero, new Vector3(0.9f, 0.35f, 0.5f), DimensionSceneUtil.EmissiveMat(new Color(1f, 0.9f, 0.4f), 3f), taxi.transform);
        sign.transform.localPosition = new Vector3(0f, 2.3f, -0.3f);
        Object.Destroy(sign.GetComponent<Collider>());
        var lg = new GameObject("TaxiLight");
        lg.transform.SetParent(taxi.transform, false);
        lg.transform.localPosition = new Vector3(0f, 2.6f, 0f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = 22f; l.intensity = 2.4f;
        l.color = new Color(1f, 0.8f, 0.3f);

        DimensionSceneUtil.CreatePortal("ToNext", Vector3.zero,
            new Vector3(2.6f, 2.2f, 4.8f), LevelPortal.PortalAction.EnterInterior, nextScene, taxi.transform)
            .transform.localPosition = new Vector3(0f, 1.2f, 0f);

        _taxiHum = DimensionSceneUtil.LoopingAudio(taxi, DimensionSceneUtil.ToneClip(150f, 2f, 0.4f), 260f, 1f);
        _hornClip = HornClip();
        _taxiHorn = taxi.AddComponent<AudioSource>();
        _taxiHorn.spatialBlend = 1f;
        _taxiHorn.rolloffMode = AudioRolloffMode.Linear;
        _taxiHorn.maxDistance = 220f;
        _nextHonkTime = Time.time + 4f;

        // Start a few blocks out on an intersection, then wander.
        _taxi.position = IntersectionPos(new Vector2Int(2, 2));
        _taxiTarget = new Vector2Int(2, 3);
    }

    // A cheerful double honk — the taxi calls out while it cruises, so you can
    // chase it by ear through the towers.
    static AudioClip HornClip()
    {
        int rate = 44100;
        int samples = (int)(rate * 0.5f);
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            bool on = t < 0.14f || (t > 0.2f && t < 0.38f);
            if (!on) { data[i] = 0f; continue; }
            data[i] = (Mathf.Sin(2f * Mathf.PI * 440f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 554f * t) * 0.4f) * 0.55f;
        }
        var clip = AudioClip.Create("horn", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    Vector3 IntersectionPos(Vector2Int ij) =>
        new Vector3((ij.x + 0.5f) * cellSize, 0f, (ij.y + 0.5f) * cellSize);

    void Update()
    {
        var cam = ObserverState.Cam;
        if (cam == null) return;
        if (!_atmosApplied)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.07f, 0.09f, 0.14f),
                fog: new Color(0.01f, 0.02f, 0.05f), fogDensity: 0.006f,
                background: new Color(0.004f, 0.006f, 0.02f));
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

        // Taxi observation + soft idle-vs-drive hum; it honks while cruising unseen.
        var tb = new Bounds(_taxi.position + Vector3.up * 1.4f, new Vector3(5.5f, 3.4f, 6.5f));
        _taxiObservedNow = _taxiTracker.Tick(tb, out _, float.PositiveInfinity);
        if (_taxiHum != null) _taxiHum.pitch = _taxiObservedNow ? 0.7f : 1.05f;
        if (!_taxiObservedNow && Time.time >= _nextHonkTime)
        {
            _nextHonkTime = Time.time + Random.Range(4f, 9f);
            _taxiHorn.pitch = Random.Range(0.95f, 1.05f);
            _taxiHorn.PlayOneShot(_hornClip, 0.8f);
        }
    }

    void FixedUpdate()
    {
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null || _taxiObservedNow) return;

        Vector3 target = IntersectionPos(_taxiTarget);
        Vector3 pos = _taxiRb.position;
        Vector3 to = target - pos; to.y = 0f;
        if (to.magnitude < 0.5f) { PickNextIntersection(); return; }
        Vector3 dir = to.normalized;
        _taxiRb.MovePosition(pos + dir * taxiSpeed * Time.fixedDeltaTime);
        _taxiRb.MoveRotation(Quaternion.LookRotation(dir, Vector3.up));
    }

    // Wander the street lattice, biased toward the player so it always drifts within reach.
    void PickNextIntersection()
    {
        Vector3 pp = _player.Rigidbody.position;
        var candidates = new[]
        {
            _taxiTarget + Vector2Int.right, _taxiTarget + Vector2Int.left,
            _taxiTarget + Vector2Int.up, _taxiTarget + Vector2Int.down,
        };
        Vector2Int best = candidates[Random.Range(0, candidates.Length)];
        if (Random.value < 0.65f)
        {
            float bestDist = float.MaxValue;
            foreach (var c in candidates)
            {
                float d = (IntersectionPos(c) - pp).magnitude;
                if (d < bestDist) { bestDist = d; best = c; }
            }
        }
        _taxiTarget = best;
    }

    Bounds CellBounds(Vector2Int c) =>
        new Bounds(new Vector3(c.x * cellSize, 10f, c.y * cellSize),
                   new Vector3(cellSize - 6f, 26f, cellSize - 6f));

    void SpawnCell(Vector2Int coord)
    {
        var cell = new Cell { coord = coord, seed = Hash(coord.x, coord.y, worldSeed) };
        _cells[coord] = cell;
        BuildBuilding(cell);
        // The Tron floor: glowing street edges on every cell, buildings or not.
        GameObject lines = _streetPool.Count > 0 ? _streetPool.Pop() : NewStreetLines();
        lines.SetActive(true);
        lines.transform.position = new Vector3(coord.x * cellSize, 0f, coord.y * cellSize);
        cell.streetLines = lines;
    }

    GameObject NewStreetLines()
    {
        var lines = new GameObject("StreetLines");
        lines.transform.SetParent(_root, false);
        var n = DimensionSceneUtil.Block(PrimitiveType.Cube, "LineN",
            Vector3.zero, Vector3.one, _streetMat, lines.transform);
        n.transform.localPosition = new Vector3(0f, 0.02f, cellSize * 0.5f);
        n.transform.localScale = new Vector3(cellSize, 0.03f, 0.16f);
        Object.Destroy(n.GetComponent<Collider>());
        var e = DimensionSceneUtil.Block(PrimitiveType.Cube, "LineE",
            Vector3.zero, Vector3.one, _streetMat, lines.transform);
        e.transform.localPosition = new Vector3(cellSize * 0.5f, 0.02f, 0f);
        e.transform.localScale = new Vector3(0.16f, 0.03f, cellSize);
        Object.Destroy(e.GetComponent<Collider>());
        return lines;
    }

    void DespawnCell(Vector2Int coord)
    {
        var cell = _cells[coord];
        ReturnBuilding(cell);
        if (cell.streetLines != null)
        {
            cell.streetLines.SetActive(false);
            _streetPool.Push(cell.streetLines);
            cell.streetLines = null;
        }
        _cells.Remove(coord);
    }

    void Reroll(Cell cell)
    {
        cell.seed = unchecked(cell.seed * 7919 + 12345);
        BuildBuilding(cell);
        cell.tracker.Reset();
    }

    void BuildBuilding(Cell cell)
    {
        ReturnBuilding(cell);
        Vector3 center = new Vector3(cell.coord.x * cellSize, 0f, cell.coord.y * cellSize);
        if (center.sqrMagnitude < 144f) return;                      // spawn plaza stays open
        if (Rand01(cell.seed, 1) >= buildingChance) return;

        GameObject b = _buildingPool.Count > 0 ? _buildingPool.Pop() : NewBuilding();
        b.SetActive(true);
        float h = Mathf.Lerp(8f, 26f, Rand01(cell.seed, 2));
        bool cyan = Rand01(cell.seed, 3) < 0.6f;
        var strips = b.transform;
        // Child 0 = block, children 1..4 = corner strips, child 5 = roof cap.
        var block = strips.GetChild(0);
        block.localScale = new Vector3(10f, h, 10f);
        block.localPosition = new Vector3(0f, h * 0.5f, 0f);
        for (int k = 0; k < 4; k++)
        {
            var strip = strips.GetChild(1 + k);
            float sx = (k % 2 == 0 ? -1f : 1f) * 5f, sz = (k < 2 ? -1f : 1f) * 5f;
            strip.localScale = new Vector3(0.18f, h, 0.18f);
            strip.localPosition = new Vector3(sx, h * 0.5f, sz);
            strip.GetComponent<Renderer>().sharedMaterial = cyan ? _cyanMat : _magentaMat;
        }
        var cap = strips.GetChild(5);
        cap.localScale = new Vector3(10.4f, 0.15f, 10.4f);
        cap.localPosition = new Vector3(0f, h + 0.1f, 0f);
        cap.GetComponent<Renderer>().sharedMaterial = cyan ? _cyanMat : _magentaMat;
        // Two dim window bands split the tower's height.
        for (int wband = 0; wband < 2; wband++)
        {
            var band = strips.GetChild(6 + wband);
            band.localScale = new Vector3(10.15f, 0.35f, 10.15f);
            band.localPosition = new Vector3(0f, h * (wband + 1) / 3f, 0f);
        }
        // One tower in five hangs a glowing billboard over the street.
        var billboard = strips.GetChild(8);
        bool hasBillboard = Rand01(cell.seed, 6) < 0.2f && h > 14f;
        billboard.gameObject.SetActive(hasBillboard);
        if (hasBillboard)
        {
            billboard.localScale = new Vector3(6.5f, 3.6f, 0.25f);
            billboard.localPosition = new Vector3(0f, h * 0.62f, -5.3f);
            billboard.GetComponent<Renderer>().sharedMaterial = cyan ? _magentaMat : _cyanMat;   // contrast pop
        }
        b.transform.position = center;
        cell.building = b;
    }

    GameObject NewBuilding()
    {
        var b = new GameObject("Tower");
        b.transform.SetParent(_root, false);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Block", Vector3.zero, Vector3.one, _blockMat, b.transform);
        for (int k = 0; k < 4; k++)
        {
            var strip = DimensionSceneUtil.Block(PrimitiveType.Cube, "Edge", Vector3.zero, Vector3.one, _cyanMat, b.transform);
            Object.Destroy(strip.GetComponent<Collider>());
        }
        var cap = DimensionSceneUtil.Block(PrimitiveType.Cube, "Cap", Vector3.zero, Vector3.one, _cyanMat, b.transform);
        Object.Destroy(cap.GetComponent<Collider>());
        var windowMat = DimensionSceneUtil.EmissiveMat(new Color(0.5f, 0.65f, 0.8f), 0.7f);
        for (int wband = 0; wband < 2; wband++)
        {
            var band = DimensionSceneUtil.Block(PrimitiveType.Cube, "WindowBand", Vector3.zero, Vector3.one, windowMat, b.transform);
            Object.Destroy(band.GetComponent<Collider>());
        }
        var billboard = DimensionSceneUtil.Block(PrimitiveType.Cube, "Billboard", Vector3.zero, Vector3.one, _magentaMat, b.transform);
        Object.Destroy(billboard.GetComponent<Collider>());
        return b;
    }

    void ReturnBuilding(Cell cell)
    {
        if (cell.building == null) return;
        cell.building.SetActive(false);
        _buildingPool.Push(cell.building);
        cell.building = null;
    }

    static int Hash(int x, int y, int seed) =>
        unchecked(x * 73856093 ^ y * 19349663 ^ seed * 83492791);

    static float Rand01(int seed, int salt) =>
        (Mathf.Abs(unchecked(seed * (int)2654435761 + salt * 40503)) % 10000) / 10000f;

    // ================= tuning (appended at END per repo conventions) =================
    [Header("City")]
    [Tooltip("Block pitch (building + street) in metres.")]
    public float cellSize = 18f;
    [Tooltip("Cells kept alive in each direction around the player.")]
    public int radiusCells = 4;
    [Tooltip("Chance a cell carries a tower.")]
    [Range(0f, 1f)] public float buildingChance = 0.85f;
    [Tooltip("Deterministic world seed.")]
    public int worldSeed = 1616;
    [Tooltip("Beyond this distance a cell counts as unobserved even on screen.")]
    public float observeMaxDistance = 90f;

    [Header("Taxi")]
    [Tooltip("Cruise speed while unseen (m/s).")]
    public float taxiSpeed = 9f;

    [Header("Exit")]
    [Tooltip("Scene the taxi takes you to.")]
    public string nextScene = "D18_StaticField";
}
