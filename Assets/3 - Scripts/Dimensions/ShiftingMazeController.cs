using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// D1 "Shifting Halls": endless corridor maze where any cell that leaves your view
/// reshuffles its walls. Exit doors appear on reshuffles and vanish if you look away
/// after spotting one. Builds the whole world (floor, ceiling, light, fog) at runtime.
/// </summary>
public class ShiftingMazeController : MonoBehaviour
{
    class Cell
    {
        public Vector2Int coord;
        public int seed;
        public readonly List<GameObject> walls = new List<GameObject>();
        public readonly ObservationTracker tracker = new ObservationTracker();
    }

    readonly Dictionary<Vector2Int, Cell> _cells = new Dictionary<Vector2Int, Cell>();
    readonly Stack<GameObject> _wallPool = new Stack<GameObject>();
    readonly List<Vector2Int> _toDespawn = new List<Vector2Int>();

    Material _wallMat, _floorMat, _ceilMat;
    Transform _root;
    GameObject _floor, _ceiling, _lamp;
    GameObject _door;                        // single global exit door (built lazily, SetActive toggled)
    Vector2Int _doorCell;
    ObservationTracker _doorTracker;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        _wallMat  = DimensionSceneUtil.Mat(new Color(0.75f, 0.70f, 0.50f));
        _floorMat = DimensionSceneUtil.Mat(new Color(0.45f, 0.42f, 0.30f));
        _ceilMat  = DimensionSceneUtil.Mat(new Color(0.80f, 0.80f, 0.75f));

        // Static planes: the floor can never despawn (spec safety rule). They follow the
        // camera in grid-snapped steps so tiling never visibly swims.
        _floor   = DimensionSceneUtil.Block(PrimitiveType.Cube, "Floor",
            new Vector3(0f, -0.25f, 0f), new Vector3(220f, 0.5f, 220f), _floorMat, _root);
        _ceiling = DimensionSceneUtil.Block(PrimitiveType.Cube, "Ceiling",
            new Vector3(0f, wallHeight + 0.25f, 0f), new Vector3(220f, 0.5f, 220f), _ceilMat, _root);

        // One warm flickering lamp that follows the player — no per-cell lights.
        _lamp = new GameObject("HallLamp");
        _lamp.transform.SetParent(_root, false);
        var l = _lamp.AddComponent<Light>();
        l.type = LightType.Point; l.range = 20f; l.intensity = 1.4f;
        l.color = new Color(1f, 0.93f, 0.75f);
        _lamp.AddComponent<FlickerLight>();

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(120f, 2f, 0.06f), 60f, 1f);
    }

    void Update()
    {
        var cam = ObserverState.Cam;
        if (cam == null) return;
        if (!_atmosApplied)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.28f, 0.26f, 0.18f),
                fog: new Color(0.55f, 0.50f, 0.33f), fogDensity: 0.045f,
                background: new Color(0.35f, 0.32f, 0.20f));
            _atmosApplied = true;
        }

        Vector3 p = cam.transform.position;
        var playerCell = new Vector2Int(Mathf.RoundToInt(p.x / cellSize), Mathf.RoundToInt(p.z / cellSize));

        // Floor/ceiling/lamp follow in snapped steps.
        Vector3 snapped = new Vector3(playerCell.x * cellSize, 0f, playerCell.y * cellSize);
        _floor.transform.position   = snapped + Vector3.up * -0.25f;
        _ceiling.transform.position = snapped + Vector3.up * (wallHeight + 0.25f);
        _lamp.transform.position    = p + Vector3.up * 1.2f;

        // Spawn window around the player; despawn what's left behind.
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

        // The rule: a cell you stop observing reshuffles (never the one you're standing in).
        foreach (var kv in _cells)
        {
            Cell cell = kv.Value;
            cell.tracker.Tick(CellBounds(cell.coord), out bool justLost, observeMaxDistance);
            if (justLost && cell.coord != playerCell) Reroll(cell);
        }

        // Exit door: once seen, look away past the grace window and it's gone.
        if (_door != null && _door.activeSelf)
        {
            var b = new Bounds(_door.transform.position + Vector3.up * 1.5f, new Vector3(2.5f, 3.5f, 2.5f));
            _doorTracker.Tick(b, out bool doorLost, float.PositiveInfinity);
            if (_doorTracker.WasEverObserved && doorLost) DespawnDoor();
        }
    }

    Bounds CellBounds(Vector2Int c) =>
        new Bounds(new Vector3(c.x * cellSize, wallHeight * 0.5f, c.y * cellSize),
                   new Vector3(cellSize, wallHeight, cellSize));

    void SpawnCell(Vector2Int coord)
    {
        var cell = new Cell { coord = coord, seed = Hash(coord.x, coord.y, worldSeed) };
        _cells[coord] = cell;
        BuildWalls(cell);
    }

    void DespawnCell(Vector2Int coord)
    {
        var cell = _cells[coord];
        ReturnWalls(cell);
        if (_door != null && _door.activeSelf && _doorCell == coord) DespawnDoor();
        _cells.Remove(coord);
    }

    void Reroll(Cell cell)
    {
        cell.seed = unchecked(cell.seed * 7919 + 12345);
        BuildWalls(cell);
        cell.tracker.Reset();
        // A fresh layout may carry the exit — but only one door exists at a time.
        if ((_door == null || !_door.activeSelf) && Rand01(cell.seed, 7) < exitDoorChance)
            SpawnDoor(cell.coord, cell.seed);
    }

    void BuildWalls(Cell cell)
    {
        ReturnWalls(cell);
        float s = cellSize, cx = cell.coord.x * s, cz = cell.coord.y * s;
        // North + East edges only — each wall has exactly one owner cell, and density
        // stays walkable (≤2 walls per owned pair). Dead ends self-heal: look away and
        // the blocking cell reshuffles.
        // North and east walls get slightly DIFFERENT thicknesses so no two faces are
        // ever coplanar where they cross at corners — identical planes z-fight (the
        // flickering joint-line artifact); a 5% split buries every overlap inside the
        // other wall's volume where the depth buffer hides it.
        float tN = wallThickness * 1.05f, tE = wallThickness * 0.95f;
        if (Rand01(cell.seed, 1) < wallChance)
            PlaceWall(cell, new Vector3(cx, wallHeight * 0.5f, cz + s * 0.5f), new Vector3(s + tN, wallHeight, tN));
        if (Rand01(cell.seed, 2) < wallChance)
            PlaceWall(cell, new Vector3(cx + s * 0.5f, wallHeight * 0.5f, cz), new Vector3(tE, wallHeight, s + tE));
    }

    void PlaceWall(Cell cell, Vector3 pos, Vector3 scale)
    {
        GameObject w = _wallPool.Count > 0 ? _wallPool.Pop() : NewWall();
        w.SetActive(true);
        w.transform.position = pos;
        w.transform.localScale = scale;
        cell.walls.Add(w);
    }

    GameObject NewWall() =>
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Wall", Vector3.zero, Vector3.one, _wallMat, _root);

    void ReturnWalls(Cell cell)
    {
        foreach (var w in cell.walls) { w.SetActive(false); _wallPool.Push(w); }
        cell.walls.Clear();
    }

    void SpawnDoor(Vector2Int coord, int seed)
    {
        if (_door == null) BuildDoor();
        _doorCell = coord;
        _doorTracker.Reset();
        float yaw = 90f * (Mathf.Abs(Hash(coord.x, coord.y, seed)) % 4);
        _door.transform.SetPositionAndRotation(
            new Vector3(coord.x * cellSize, 0f, coord.y * cellSize), Quaternion.Euler(0f, yaw, 0f));
        _door.SetActive(true);
    }

    void DespawnDoor() { _door.SetActive(false); _doorTracker.Reset(); }

    void BuildDoor()
    {
        _doorTracker = new ObservationTracker();
        _door = new GameObject("ExitDoor");
        _door.transform.SetParent(_root, false);
        var frame = DimensionSceneUtil.Mat(new Color(0.1f, 0.1f, 0.12f));
        var glow  = DimensionSceneUtil.EmissiveMat(new Color(0.75f, 0.95f, 1f), 2.5f);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostL",  new Vector3(-0.8f, 1.5f, 0f), new Vector3(0.3f, 3f, 0.3f), frame, _door.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostR",  new Vector3( 0.8f, 1.5f, 0f), new Vector3(0.3f, 3f, 0.3f), frame, _door.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Lintel", new Vector3(0f, 3.05f, 0f),   new Vector3(1.9f, 0.3f, 0.3f), frame, _door.transform);
        var pane = DimensionSceneUtil.Block(PrimitiveType.Cube, "Glow", new Vector3(0f, 1.5f, 0f), new Vector3(1.3f, 2.9f, 0.05f), glow, _door.transform);
        Destroy(pane.GetComponent<Collider>());              // walk THROUGH the light
        DimensionSceneUtil.CreatePortal("ToD2", _door.transform.position + Vector3.up * 1.5f,
            new Vector3(1.3f, 2.9f, 0.6f), LevelPortal.PortalAction.EnterInterior, nextScene, _door.transform);
    }

    static int Hash(int x, int y, int seed) =>
        unchecked(x * 73856093 ^ y * 19349663 ^ seed * 83492791);

    static float Rand01(int seed, int salt) =>
        (Mathf.Abs(unchecked(seed * (int)2654435761 + salt * 40503)) % 10000) / 10000f;

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Maze")]
    [Tooltip("Cell size in metres (corridor pitch).")]
    public float cellSize = 6f;
    public float wallHeight = 4f;
    public float wallThickness = 0.4f;
    [Tooltip("Cells kept alive in each direction around the player.")]
    public int radiusCells = 6;
    [Tooltip("Chance each owned edge (N and E per cell) carries a wall.")]
    [Range(0f, 1f)] public float wallChance = 0.55f;
    [Tooltip("Deterministic world seed (cells reshuffle away from it over time).")]
    public int worldSeed = 1337;
    [Tooltip("Beyond this distance a cell counts as unobserved even on screen (fog hides it anyway).")]
    public float observeMaxDistance = 80f;

    [Header("Exit")]
    [Tooltip("Chance a reshuffled cell contains the exit door (only one exists at a time).")]
    [Range(0f, 1f)] public float exitDoorChance = 0.08f;
    [Tooltip("Scene the exit door leads to.")]
    public string nextScene = "D2_DuneSea";
}
