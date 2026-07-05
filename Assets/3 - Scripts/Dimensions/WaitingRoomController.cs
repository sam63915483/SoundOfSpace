using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// D24 "The Waiting Room": an endless beige office lobby. Cubicle partitions
/// reshuffle whenever they leave your view (D1's rule at desk height); potted plants
/// come and go. Four glowing EXIT doors drift the lobby, relocating unseen — an
/// intercom chime swells as you face the REAL one (gaze-audio). The other three walk
/// you back to reception.
/// </summary>
public class WaitingRoomController : MonoBehaviour
{
    class Cell
    {
        public Vector2Int coord;
        public int seed;
        public readonly List<GameObject> props = new List<GameObject>();
        public readonly ObservationTracker tracker = new ObservationTracker();
    }

    readonly Dictionary<Vector2Int, Cell> _cells = new Dictionary<Vector2Int, Cell>();
    readonly Stack<GameObject> _wallPool = new Stack<GameObject>();
    readonly Stack<GameObject> _plantPool = new Stack<GameObject>();
    readonly List<Vector2Int> _toDespawn = new List<Vector2Int>();

    Transform _root;
    Material _wallMat, _potMat, _leafMat;
    GameObject _floor, _ceiling, _lamp;
    Transform[] _exits;
    ObservationTracker[] _exitTrackers;
    AudioSource _chime;
    float _loopDebounceUntil;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        _wallMat = DimensionSceneUtil.Mat(new Color(0.72f, 0.68f, 0.58f), 0.1f);
        _potMat  = DimensionSceneUtil.Mat(new Color(0.45f, 0.25f, 0.15f), 0.1f);
        _leafMat = DimensionSceneUtil.Mat(new Color(0.20f, 0.35f, 0.16f), 0.1f);
        var floorMat = DimensionSceneUtil.Mat(new Color(0.55f, 0.50f, 0.42f), 0.15f);
        var ceilMat  = DimensionSceneUtil.Mat(new Color(0.85f, 0.84f, 0.80f), 0.1f);

        _floor = DimensionSceneUtil.Block(PrimitiveType.Cube, "Floor",
            new Vector3(0f, -0.25f, 0f), new Vector3(220f, 0.5f, 220f), floorMat, _root);
        _ceiling = DimensionSceneUtil.Block(PrimitiveType.Cube, "Ceiling",
            new Vector3(0f, 3.25f, 0f), new Vector3(220f, 0.5f, 220f), ceilMat, _root);

        _lamp = new GameObject("LobbyLamp");
        _lamp.transform.SetParent(_root, false);
        var l = _lamp.AddComponent<Light>();
        l.type = LightType.Point; l.range = 22f; l.intensity = 1.5f;
        l.color = new Color(1f, 0.97f, 0.88f);
        var flicker = _lamp.AddComponent<FlickerLight>();
        flicker.flickerDepth = 0.12f;

        // The exits: 1 true + 3 loops, all relocating unseen.
        _exits = new Transform[4];
        _exitTrackers = new ObservationTracker[4];
        for (int i = 0; i < 4; i++)
        {
            _exits[i] = BuildExit(i == 0).transform;
            _exitTrackers[i] = new ObservationTracker();
            PlaceExit(i, Vector3.zero, initial: true);
        }

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(118f, 2f, 0.05f), 400f, 1f);
    }

    GameObject BuildExit(bool isTrue)
    {
        var frameMat = DimensionSceneUtil.Mat(new Color(0.30f, 0.28f, 0.26f), 0.2f);
        var signMat  = DimensionSceneUtil.EmissiveMat(new Color(0.15f, 1f, 0.35f), 2.4f);
        var exit = new GameObject(isTrue ? "Exit_TRUE" : "Exit");
        exit.transform.SetParent(_root, false);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostL", new Vector3(-0.75f, 1.4f, 0f), new Vector3(0.25f, 2.8f, 0.25f), frameMat, exit.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostR", new Vector3(0.75f, 1.4f, 0f), new Vector3(0.25f, 2.8f, 0.25f), frameMat, exit.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Lintel", new Vector3(0f, 2.9f, 0f), new Vector3(1.75f, 0.25f, 0.25f), frameMat, exit.transform);
        var sign = DimensionSceneUtil.Block(PrimitiveType.Cube, "ExitSign", new Vector3(0f, 3.15f, 0f), new Vector3(1.1f, 0.35f, 0.12f), signMat, exit.transform);
        Object.Destroy(sign.GetComponent<Collider>());
        var dark = DimensionSceneUtil.Block(PrimitiveType.Cube, "Doorway", new Vector3(0f, 1.4f, 0f), new Vector3(1.25f, 2.8f, 0.08f), DimensionSceneUtil.Mat(new Color(0.05f, 0.05f, 0.06f)), exit.transform);
        Object.Destroy(dark.GetComponent<Collider>());

        if (isTrue)
        {
            _chime = DimensionSceneUtil.LoopingAudio(exit, ChimeClip(), 240f, 1f);
            DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1.4f, 0f),
                new Vector3(1.25f, 2.7f, 0.7f), LevelPortal.PortalAction.EnterInterior, nextScene, exit.transform);
        }
        else
        {
            var trig = new GameObject("LoopTrigger");
            trig.transform.SetParent(exit.transform, false);
            trig.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            var box = trig.AddComponent<BoxCollider>();
            box.isTrigger = true; box.size = new Vector3(1.25f, 2.7f, 0.7f);
            trig.AddComponent<WaitingRoomLoopTrigger>().owner = this;
        }
        return exit;
    }

    // Soft two-note intercom chime (ding-dong) with silence between repeats.
    static AudioClip ChimeClip()
    {
        int rate = 44100;
        float seconds = 2.6f;
        int samples = (int)(rate * seconds);
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            float a = t < 0.5f ? Mathf.Sin(2f * Mathf.PI * 880f * t) * Mathf.Exp(-t * 6f) : 0f;
            float b = (t >= 0.35f && t < 1.1f) ? Mathf.Sin(2f * Mathf.PI * 660f * (t - 0.35f)) * Mathf.Exp(-(t - 0.35f) * 5f) : 0f;
            data[i] = (a + b) * 0.55f;
        }
        var clip = AudioClip.Create("chime", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void PlaceExit(int i, Vector3 aroundPos, bool initial)
    {
        Vector3 best = _exits[i].position;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float d = initial ? Random.Range(25f, 60f) : Random.Range(20f, 50f);
            Vector3 c = initial ? Vector3.zero : aroundPos;
            best = new Vector3(c.x + Mathf.Cos(a) * d, 0f, c.z + Mathf.Sin(a) * d);
            if (initial || !ObserverState.IsObserved(new Bounds(best + Vector3.up * 1.6f, new Vector3(3f, 3.6f, 3f))))
                break;
        }
        _exits[i].position = best;
        Vector3 face = aroundPos - best; face.y = 0f;
        if (face.sqrMagnitude > 0.01f) _exits[i].rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
        _exitTrackers[i].Reset();
    }

    void Update()
    {
        var cam = ObserverState.Cam;
        if (cam == null) return;
        if (!_atmosApplied)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.42f, 0.40f, 0.34f),
                fog: new Color(0.62f, 0.58f, 0.48f), fogDensity: 0.040f,
                background: new Color(0.55f, 0.51f, 0.42f));
            _atmosApplied = true;
        }

        Vector3 p = cam.transform.position;
        var playerCell = new Vector2Int(Mathf.RoundToInt(p.x / cellSize), Mathf.RoundToInt(p.z / cellSize));
        Vector3 snapped = new Vector3(playerCell.x * cellSize, 0f, playerCell.y * cellSize);
        _floor.transform.position = snapped + Vector3.up * -0.25f;
        _ceiling.transform.position = snapped + Vector3.up * 3.25f;
        _lamp.transform.position = p + Vector3.up * 1.1f;

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

        for (int i = 0; i < _exits.Length; i++)
        {
            var b = new Bounds(_exits[i].position + Vector3.up * 1.6f, new Vector3(2.6f, 3.6f, 2.6f));
            _exitTrackers[i].Tick(b, out bool lost, float.PositiveInfinity);
            if (lost) PlaceExit(i, cam.transform.position, initial: false);
        }

        if (_chime != null)
        {
            Vector3 to = _exits[0].position - cam.transform.position;
            float align = Vector3.Dot(cam.transform.forward, to.normalized);
            _chime.volume = Mathf.Lerp(0.04f, 1f, Mathf.InverseLerp(0.2f, 0.95f, align));
        }
    }

    Bounds CellBounds(Vector2Int c) =>
        new Bounds(new Vector3(c.x * cellSize, 1.1f, c.y * cellSize),
                   new Vector3(cellSize, 2.2f, cellSize));

    void SpawnCell(Vector2Int coord)
    {
        var cell = new Cell { coord = coord, seed = Hash(coord.x, coord.y, worldSeed) };
        _cells[coord] = cell;
        BuildCell(cell);
    }

    void DespawnCell(Vector2Int coord)
    {
        ReturnProps(_cells[coord]);
        _cells.Remove(coord);
    }

    void Reroll(Cell cell)
    {
        cell.seed = unchecked(cell.seed * 7919 + 12345);
        BuildCell(cell);
        cell.tracker.Reset();
    }

    void BuildCell(Cell cell)
    {
        ReturnProps(cell);
        float s = cellSize, cx = cell.coord.x * s, cz = cell.coord.y * s;
        if (new Vector2(cx, cz).sqrMagnitude < 36f) return;      // reception stays clear

        // Chest-high partitions on the cell's N and E edges (see over, can't cut through).
        if (Rand01(cell.seed, 1) < wallChance)
            PlaceWall(cell, new Vector3(cx, partitionHeight * 0.5f, cz + s * 0.5f), new Vector3(s - 0.7f, partitionHeight, 0.35f));
        if (Rand01(cell.seed, 2) < wallChance)
            PlaceWall(cell, new Vector3(cx + s * 0.5f, partitionHeight * 0.5f, cz), new Vector3(0.32f, partitionHeight, s - 0.7f));
        // The occasional potted plant.
        if (Rand01(cell.seed, 3) < plantChance)
        {
            GameObject plant = _plantPool.Count > 0 ? _plantPool.Pop() : NewPlant();
            plant.SetActive(true);
            plant.transform.position = new Vector3(
                cx + (Rand01(cell.seed, 4) - 0.5f) * (s - 2f), 0f,
                cz + (Rand01(cell.seed, 5) - 0.5f) * (s - 2f));
            cell.props.Add(plant);
        }
    }

    void PlaceWall(Cell cell, Vector3 pos, Vector3 scale)
    {
        GameObject w = _wallPool.Count > 0 ? _wallPool.Pop() : DimensionSceneUtil.Block(
            PrimitiveType.Cube, "Partition", Vector3.zero, Vector3.one, _wallMat, _root);
        w.SetActive(true);
        w.transform.position = pos;
        w.transform.localScale = scale;
        cell.props.Add(w);
    }

    GameObject NewPlant()
    {
        var plant = new GameObject("Plant");
        plant.transform.SetParent(_root, false);
        DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Pot",
            new Vector3(0f, 0.3f, 0f), new Vector3(0.55f, 0.3f, 0.55f), _potMat, plant.transform);
        var leaves = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Leaves",
            new Vector3(0f, 1.05f, 0f), new Vector3(0.9f, 1.1f, 0.9f), _leafMat, plant.transform);
        Object.Destroy(leaves.GetComponent<Collider>());
        return plant;
    }

    void ReturnProps(Cell cell)
    {
        foreach (var prop in cell.props)
        {
            prop.SetActive(false);
            if (prop.name == "Plant") _plantPool.Push(prop);
            else _wallPool.Push(prop);
        }
        cell.props.Clear();
    }

    static int Hash(int x, int y, int seed) =>
        unchecked(x * 73856093 ^ y * 19349663 ^ seed * 83492791);

    static float Rand01(int seed, int salt) =>
        (Mathf.Abs(unchecked(seed * (int)2654435761 + salt * 40503)) % 10000) / 10000f;

    /// <summary>Wrong-exit punishment: escorted back to reception. No damage.</summary>
    public void LoopPlayerBack()
    {
        if (Time.time < _loopDebounceUntil) return;
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null) return;
        _loopDebounceUntil = Time.time + 1.5f;
        _player.Rigidbody.position = new Vector3(0f, 1.5f, 0f);
        _player.Rigidbody.velocity = Vector3.zero;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Lobby")]
    [Tooltip("Cubicle cell pitch (metres).")]
    public float cellSize = 6f;
    [Tooltip("Partition height — chest-high so the lobby reads as one endless room.")]
    public float partitionHeight = 1.7f;
    [Tooltip("Cells kept alive in each direction around the player.")]
    public int radiusCells = 6;
    [Tooltip("Chance each owned edge carries a partition.")]
    [Range(0f, 1f)] public float wallChance = 0.45f;
    [Tooltip("Chance a cell holds a potted plant.")]
    [Range(0f, 1f)] public float plantChance = 0.18f;
    [Tooltip("Deterministic world seed.")]
    public int worldSeed = 2424;
    [Tooltip("Beyond this distance a cell counts as unobserved even on screen.")]
    public float observeMaxDistance = 60f;

    [Header("Exit")]
    [Tooltip("Scene the real EXIT leads to.")]
    public string nextScene = "D25_CandleSea";
}

/// <summary>Doorway trigger on every fake EXIT.</summary>
public class WaitingRoomLoopTrigger : MonoBehaviour
{
    [HideInInspector] public WaitingRoomController owner;

    void OnTriggerEnter(Collider other)
    {
        if (owner == null) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;
        owner.LoopPlayerBack();
    }
}
