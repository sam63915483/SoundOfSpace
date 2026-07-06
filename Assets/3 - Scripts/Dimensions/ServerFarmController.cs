using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// D11 "The Shelves" (v2 — full remake): a sprawling server farm. Aisle after aisle
/// of humming racks with blinking status LEDs, cable trays overhead trailing sagging
/// wire bundles, cold panel lights, fan-noise wash. Rack rows reshuffle whenever
/// they leave your view (D1's rule with furniture), rewiring the aisles around you.
/// The exit is a single OPEN cabinet glowing terminal-green — it appears on a
/// reshuffle and powers down if you look away from it. Replaces the old D1 recolor.
/// </summary>
public class ServerFarmController : MonoBehaviour
{
    class Cell
    {
        public Vector2Int coord;
        public int seed;
        public readonly List<GameObject> props = new List<GameObject>();
        public readonly ObservationTracker tracker = new ObservationTracker();
    }

    readonly Dictionary<Vector2Int, Cell> _cells = new Dictionary<Vector2Int, Cell>();
    readonly Stack<GameObject> _rackPool = new Stack<GameObject>();
    readonly Stack<GameObject> _trayPool = new Stack<GameObject>();
    readonly List<Vector2Int> _toDespawn = new List<Vector2Int>();
    readonly List<Renderer> _blinkers = new List<Renderer>();

    Transform _root;
    Material _rackMat, _faceMat, _trayMat, _wireMat;
    Material[] _ledMats;
    GameObject _floor, _ceiling;
    readonly List<GameObject> _panelLights = new List<GameObject>();
    GameObject _exitRack;
    Vector2Int _exitCell;
    ObservationTracker _exitTracker;
    AudioSource _beeper;
    AudioClip _beepClip;
    float _nextBeepTime;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        _rackMat = DimensionSceneUtil.Mat(new Color(0.10f, 0.11f, 0.13f), 0.35f);
        _faceMat = DimensionSceneUtil.Mat(new Color(0.05f, 0.06f, 0.08f), 0.5f);
        _trayMat = DimensionSceneUtil.Mat(new Color(0.16f, 0.17f, 0.19f), 0.3f);
        _wireMat = DimensionSceneUtil.Mat(new Color(0.04f, 0.04f, 0.05f), 0.2f);
        _ledMats = new[]
        {
            DimensionSceneUtil.EmissiveMat(new Color(0.2f, 1f, 0.4f), 2.2f),
            DimensionSceneUtil.EmissiveMat(new Color(0.2f, 0.7f, 1f), 2.2f),
            DimensionSceneUtil.EmissiveMat(new Color(1f, 0.65f, 0.15f), 2.2f),
            DimensionSceneUtil.EmissiveMat(new Color(1f, 0.2f, 0.15f), 2.2f),
        };
        var floorMat = DimensionSceneUtil.Mat(new Color(0.13f, 0.14f, 0.16f), 0.4f);
        var ceilMat  = DimensionSceneUtil.Mat(new Color(0.07f, 0.08f, 0.10f), 0.2f);

        _floor = DimensionSceneUtil.Block(PrimitiveType.Cube, "Floor",
            new Vector3(0f, -0.25f, 0f), new Vector3(220f, 0.5f, 220f), floorMat, _root);
        _ceiling = DimensionSceneUtil.Block(PrimitiveType.Cube, "Ceiling",
            new Vector3(0f, 3.65f, 0f), new Vector3(220f, 0.5f, 220f), ceilMat, _root);

        // A small constellation of cold ceiling panels that follows the player.
        var panelMat = DimensionSceneUtil.EmissiveMat(new Color(0.75f, 0.85f, 1f), 1.6f);
        for (int i = 0; i < 5; i++)
        {
            var holder = new GameObject("PanelLight" + i);
            holder.transform.SetParent(_root, false);
            var quad = DimensionSceneUtil.Block(PrimitiveType.Cube, "Panel",
                Vector3.zero, new Vector3(1.8f, 0.06f, 0.9f), panelMat, holder.transform);
            Destroy(quad.GetComponent<Collider>());
            var l = holder.AddComponent<Light>();
            l.type = LightType.Point; l.range = 14f; l.intensity = 1.2f;
            l.color = new Color(0.75f, 0.85f, 1f);
            if (i == 2) holder.AddComponent<FlickerLight>();
            _panelLights.Add(holder);
        }

        _exitTracker = new ObservationTracker();

        // Fan wash + mains hum + occasional far beeps.
        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(120f, 2f, 0.05f), 400f, 1f);
        var fans = DimensionSceneUtil.LoopingAudio(gameObject, FanClip(), 400f, 0.6f);
        fans.spatialBlend = 0f;
        _beepClip = BeepClip();
        var beepGo = new GameObject("Beeper");
        beepGo.transform.SetParent(_root, false);
        _beeper = beepGo.AddComponent<AudioSource>();
        _beeper.spatialBlend = 1f;
        _beeper.rolloffMode = AudioRolloffMode.Linear;
        _beeper.maxDistance = 60f;
        _nextBeepTime = Time.time + 6f;
    }

    // Broadband hiss shaped into a fan-like whoosh.
    static AudioClip FanClip()
    {
        int rate = 44100;
        int samples = (int)(rate * 2.5f);
        var data = new float[samples];
        var rng = new System.Random(1111);
        float v = 0f;
        for (int i = 0; i < samples; i++)
        {
            v = Mathf.Lerp(v, (float)(rng.NextDouble() * 2.0 - 1.0), 0.28f);
            data[i] = v * 0.14f;
        }
        var clip = AudioClip.Create("fans", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip BeepClip()
    {
        int rate = 44100;
        int samples = (int)(rate * 0.32f);
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            bool on = t < 0.09f || (t > 0.16f && t < 0.25f);
            data[i] = on ? Mathf.Sin(2f * Mathf.PI * 1320f * t) * 0.4f : 0f;
        }
        var clip = AudioClip.Create("beep", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void Update()
    {
        var cam = ObserverState.Cam;
        if (cam == null) return;
        if (!_atmosApplied)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.10f, 0.13f, 0.18f),
                fog: new Color(0.02f, 0.035f, 0.06f), fogDensity: 0.055f,
                background: new Color(0.01f, 0.02f, 0.04f));
            _atmosApplied = true;
        }

        Vector3 p = cam.transform.position;
        var playerCell = new Vector2Int(Mathf.RoundToInt(p.x / cellSize), Mathf.RoundToInt(p.z / cellSize));
        Vector3 snapped = new Vector3(playerCell.x * cellSize, 0f, playerCell.y * cellSize);
        _floor.transform.position = snapped + Vector3.up * -0.25f;
        _ceiling.transform.position = snapped + Vector3.up * 3.65f;
        for (int i = 0; i < _panelLights.Count; i++)
        {
            float a = i * Mathf.PI * 2f / _panelLights.Count;
            _panelLights[i].transform.position = p + new Vector3(Mathf.Cos(a) * 6f, 3.3f - p.y, Mathf.Sin(a) * 6f);
        }

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

        // LED blink: cheap renderer toggles driven by a per-LED time hash.
        for (int i = 0; i < _blinkers.Count; i++)
        {
            var r = _blinkers[i];
            if (r == null || !r.transform.parent.gameObject.activeInHierarchy) continue;
            float t = Time.time * (0.7f + (i % 5) * 0.35f) + i * 1.618f;
            r.enabled = Mathf.PerlinNoise(t, i * 0.13f) > 0.35f;
        }

        // The exit cabinet: once seen, looking away powers it down (D1's door rule).
        if (_exitRack != null && _exitRack.activeSelf)
        {
            var b = new Bounds(_exitRack.transform.position + Vector3.up * 1.3f, new Vector3(2.2f, 2.8f, 2.2f));
            _exitTracker.Tick(b, out bool lost, float.PositiveInfinity);
            if (_exitTracker.WasEverObserved && lost) DespawnExit();
        }

        if (Time.time >= _nextBeepTime)
        {
            _nextBeepTime = Time.time + Random.Range(5f, 14f);
            float ba = Random.value * Mathf.PI * 2f;
            _beeper.transform.position = p + new Vector3(Mathf.Cos(ba), 0f, Mathf.Sin(ba)) * Random.Range(12f, 30f);
            _beeper.pitch = Random.Range(0.9f, 1.15f);
            _beeper.PlayOneShot(_beepClip, 0.5f);
        }
    }

    Bounds CellBounds(Vector2Int c) =>
        new Bounds(new Vector3(c.x * cellSize, 1.6f, c.y * cellSize),
                   new Vector3(cellSize, 3.2f, cellSize));

    void SpawnCell(Vector2Int coord)
    {
        var cell = new Cell { coord = coord, seed = Hash(coord.x, coord.y, worldSeed) };
        _cells[coord] = cell;
        BuildCell(cell);
    }

    void DespawnCell(Vector2Int coord)
    {
        var cell = _cells[coord];
        ReturnProps(cell);
        if (_exitRack != null && _exitRack.activeSelf && _exitCell == coord) DespawnExit();
        _cells.Remove(coord);
    }

    void Reroll(Cell cell)
    {
        cell.seed = unchecked(cell.seed * 7919 + 12345);
        BuildCell(cell);
        cell.tracker.Reset();
        if ((_exitRack == null || !_exitRack.activeSelf) && Rand01(cell.seed, 7) < exitChance)
            SpawnExit(cell.coord, cell.seed);
    }

    void BuildCell(Cell cell)
    {
        ReturnProps(cell);
        float s = cellSize, cx = cell.coord.x * s, cz = cell.coord.y * s;
        if (new Vector2(cx, cz).sqrMagnitude < 42f) return;      // entry aisle stays open

        // Rack rows on the cell's N and E edges — the reshuffling aisle walls.
        if (Rand01(cell.seed, 1) < rowChance)
            PlaceRackRow(cell, new Vector3(cx, 0f, cz + s * 0.5f), Quaternion.identity, cell.seed * 3 + 1);
        if (Rand01(cell.seed, 2) < rowChance)
            PlaceRackRow(cell, new Vector3(cx + s * 0.5f, 0f, cz), Quaternion.Euler(0f, 90f, 0f), cell.seed * 5 + 2);
        // Overhead cable tray crossing the cell (with drooping wire bundles).
        if (Rand01(cell.seed, 3) < 0.6f)
        {
            GameObject tray = _trayPool.Count > 0 ? _trayPool.Pop() : NewTray();
            tray.SetActive(true);
            bool alongX = Rand01(cell.seed, 4) < 0.5f;
            tray.transform.SetPositionAndRotation(new Vector3(cx, 0f, cz),
                alongX ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity);
            cell.props.Add(tray);
        }
    }

    void PlaceRackRow(Cell cell, Vector3 pos, Quaternion rot, int seed)
    {
        GameObject row = _rackPool.Count > 0 ? _rackPool.Pop() : NewRackRow();
        row.SetActive(true);
        row.transform.SetPositionAndRotation(pos, rot);
        // Vary the cabinets' LED palettes per placement so rows don't read cloned.
        int k = 0;
        foreach (var t in row.GetComponentsInChildren<Transform>(true))
            if (t.name == "LED")
            {
                t.GetComponent<Renderer>().sharedMaterial = _ledMats[Mathf.Abs(seed + k * 7) % _ledMats.Length];
                k++;
            }
        cell.props.Add(row);
    }

    // A row = 3 cabinets, each with a dark face, vent slits and a column of LEDs.
    GameObject NewRackRow()
    {
        var row = new GameObject("RackRow");
        row.transform.SetParent(_root, false);
        for (int i = -1; i <= 1; i++)
        {
            var cab = DimensionSceneUtil.Block(PrimitiveType.Cube, "Cabinet",
                new Vector3(i * 1.5f, 1.25f, 0f), new Vector3(1.42f, 2.5f, 0.85f), _rackMat, row.transform);
            var face = DimensionSceneUtil.Block(PrimitiveType.Cube, "Face",
                Vector3.zero, new Vector3(1.28f, 2.3f, 0.06f), _faceMat, cab.transform);
            face.transform.localPosition = new Vector3(0f, 0f, -0.47f);
            face.transform.localScale = new Vector3(1.28f / 1.42f, 2.3f / 2.5f, 0.06f / 0.85f);
            Destroy(face.GetComponent<Collider>());
            for (int led = 0; led < 4; led++)
            {
                var dot = DimensionSceneUtil.Block(PrimitiveType.Cube, "LED",
                    Vector3.zero, Vector3.one, _ledMats[led % _ledMats.Length], cab.transform);
                dot.transform.localPosition = new Vector3(-0.42f, 0.32f - led * 0.22f, -0.52f);
                dot.transform.localScale = new Vector3(0.05f, 0.02f, 0.012f);
                Destroy(dot.GetComponent<Collider>());
                _blinkers.Add(dot.GetComponent<Renderer>());
            }
        }
        return row;
    }

    // Overhead tray spanning the cell + three sagging wire bundles hanging off it.
    GameObject NewTray()
    {
        var tray = new GameObject("CableTray");
        tray.transform.SetParent(_root, false);
        var rail = DimensionSceneUtil.Block(PrimitiveType.Cube, "Rail",
            new Vector3(0f, 3.05f, 0f), new Vector3(0.6f, 0.1f, cellSize), _trayMat, tray.transform);
        Destroy(rail.GetComponent<Collider>());
        for (int w = 0; w < 3; w++)
        {
            float z = (w - 1) * cellSize * 0.3f;
            // A drooping bundle: three short leaning segments approximating a catenary.
            for (int seg = 0; seg < 3; seg++)
            {
                var wire = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Wire",
                    Vector3.zero, new Vector3(0.05f, 0.34f, 0.05f), _wireMat, tray.transform);
                Destroy(wire.GetComponent<Collider>());
                float sx = (seg - 1) * 0.5f;
                wire.transform.localPosition = new Vector3(sx, 2.72f - Mathf.Abs(seg - 1) * -0.06f - (seg == 1 ? 0.16f : 0f), z);
                wire.transform.localRotation = Quaternion.Euler(0f, 0f, seg == 0 ? 48f : seg == 2 ? -48f : 90f);
            }
        }
        return tray;
    }

    void SpawnExit(Vector2Int coord, int seed)
    {
        if (_exitRack == null) BuildExitRack();
        _exitCell = coord;
        _exitTracker.Reset();
        float yaw = 90f * (Mathf.Abs(Hash(coord.x, coord.y, seed)) % 4);
        _exitRack.transform.SetPositionAndRotation(
            new Vector3(coord.x * cellSize, 0f, coord.y * cellSize), Quaternion.Euler(0f, yaw, 0f));
        _exitRack.SetActive(true);
    }

    void DespawnExit() { _exitRack.SetActive(false); _exitTracker.Reset(); }

    // The way out: an open cabinet, insides glowing terminal green, cables parted.
    void BuildExitRack()
    {
        _exitRack = new GameObject("ExitRack");
        _exitRack.transform.SetParent(_root, false);
        var green = DimensionSceneUtil.EmissiveMat(new Color(0.3f, 1f, 0.45f), 2.8f);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "SideL",
            new Vector3(-0.75f, 1.35f, 0f), new Vector3(0.25f, 2.7f, 0.95f), _rackMat, _exitRack.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "SideR",
            new Vector3(0.75f, 1.35f, 0f), new Vector3(0.25f, 2.7f, 0.95f), _rackMat, _exitRack.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Top",
            new Vector3(0f, 2.78f, 0f), new Vector3(1.75f, 0.18f, 0.95f), _rackMat, _exitRack.transform);
        var glow = DimensionSceneUtil.Block(PrimitiveType.Cube, "TerminalGlow",
            new Vector3(0f, 1.35f, 0.28f), new Vector3(1.25f, 2.55f, 0.06f), green, _exitRack.transform);
        Destroy(glow.GetComponent<Collider>());
        var lg = new GameObject("ExitLight");
        lg.transform.SetParent(_exitRack.transform, false);
        lg.transform.localPosition = new Vector3(0f, 1.6f, -0.6f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = 12f; l.intensity = 2.2f;
        l.color = new Color(0.35f, 1f, 0.5f);
        DimensionSceneUtil.CreatePortal("ToNext", Vector3.zero,
            new Vector3(1.25f, 2.5f, 0.8f), LevelPortal.PortalAction.EnterInterior, nextScene, _exitRack.transform)
            .transform.localPosition = new Vector3(0f, 1.35f, 0f);
    }

    void ReturnProps(Cell cell)
    {
        foreach (var prop in cell.props)
        {
            prop.SetActive(false);
            if (prop.name == "RackRow") _rackPool.Push(prop);
            else _trayPool.Push(prop);
        }
        cell.props.Clear();
    }

    static int Hash(int x, int y, int seed) =>
        unchecked(x * 73856093 ^ y * 19349663 ^ seed * 83492791);

    static float Rand01(int seed, int salt) =>
        (Mathf.Abs(unchecked(seed * (int)2654435761 + salt * 40503)) % 10000) / 10000f;

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Farm")]
    [Tooltip("Aisle cell pitch (metres) — racks sit on cell edges.")]
    public float cellSize = 6f;
    [Tooltip("Cells kept alive in each direction around the player.")]
    public int radiusCells = 5;
    [Tooltip("Chance each owned edge carries a rack row.")]
    [Range(0f, 1f)] public float rowChance = 0.62f;
    [Tooltip("Deterministic world seed.")]
    public int worldSeed = 1111;
    [Tooltip("Beyond this distance a cell counts as unobserved even on screen.")]
    public float observeMaxDistance = 55f;

    [Header("Exit")]
    [Tooltip("Chance a reshuffled cell births the exit cabinet (one at a time).")]
    [Range(0f, 1f)] public float exitChance = 0.25f;
    [Tooltip("Scene the glowing cabinet leads to.")]
    public string nextScene = "D12_MirrorLake";
}
