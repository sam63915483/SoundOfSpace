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
        public readonly List<GameObject> props = new List<GameObject>();
        public readonly ObservationTracker tracker = new ObservationTracker();
    }

    readonly Dictionary<Vector2Int, Cell> _cells = new Dictionary<Vector2Int, Cell>();
    readonly Stack<GameObject> _wallPool = new Stack<GameObject>();
    readonly List<Vector2Int> _toDespawn = new List<Vector2Int>();
    readonly Dictionary<Vector2Int, GameObject> _pillars = new Dictionary<Vector2Int, GameObject>();
    readonly Stack<GameObject> _pillarPool = new Stack<GameObject>();

    Material _wallMat, _floorMat, _ceilMat;
    Transform _root;
    GameObject _floor, _ceiling, _lamp;
    GameObject _door;                        // single global exit door (built lazily, SetActive toggled)
    Vector2Int _doorCell;
    ObservationTracker _doorTracker;
    bool _atmosApplied;

    // Polish pass: per-cell furniture that rebuilds with the walls, so looking away
    // rearranges the room. Theme branches at runtime on the scene name — D5_Archive
    // reuses this controller (adding serialized theme fields would need per-scene
    // pushes; the scene name is already the distinguisher).
    bool _archive;
    Material _woodMat, _fabricMat, _booksMat, _clockFaceMat, _clockHandMat, _shadeLitMat, _shadeDarkMat;
    Material[] _canvasMats;
    float _nextShiftSfxTime;
    Vector2Int _lastPlayerCell;
    const float PropChance = 0.33f;          // most cells stay empty corridor

    void Awake()
    {
        _root = transform;
        _archive = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.StartsWith("D5");

        // Textures multiply against a whitened theme tint so D1/D5 stay distinct
        // through their serialized colors; missing textures fall back to flat tint.
        Color Soft(Color c) => Color.Lerp(c, Color.white, 0.7f);
        _wallMat  = DimensionSceneUtil.TexMat(_archive ? "d5_wall" : "d1_wall", Soft(wallColor), new Vector2(2f, 1.5f));
        // 220 m planes, cube UVs are 0-1 per face — tiling 110 reads ~1 tile / 2 m.
        _floorMat = DimensionSceneUtil.TexMat(_archive ? "wood_parquet" : "d1_floor", Soft(floorColor), new Vector2(110f, 110f));
        _ceilMat  = DimensionSceneUtil.TexMat("d1_ceiling", Soft(ceilColor), new Vector2(110f, 110f));

        _woodMat      = DimensionSceneUtil.TexMat("wood_worn", Color.white, Vector2.one);
        _fabricMat    = DimensionSceneUtil.TexMat("fabric_couch", Color.white, Vector2.one);
        _booksMat     = DimensionSceneUtil.TexMat("d5_books", Color.white, Vector2.one);
        _clockFaceMat = DimensionSceneUtil.Mat(new Color(0.92f, 0.90f, 0.84f));
        _clockHandMat = DimensionSceneUtil.Mat(new Color(0.08f, 0.08f, 0.08f));
        _shadeLitMat  = DimensionSceneUtil.EmissiveMat(lampColor, 1.1f);
        _shadeDarkMat = DimensionSceneUtil.Mat(new Color(0.35f, 0.30f, 0.25f));
        _canvasMats = new[]
        {
            DimensionSceneUtil.Mat(new Color(0.18f, 0.24f, 0.18f)),
            DimensionSceneUtil.Mat(new Color(0.16f, 0.18f, 0.28f)),
            DimensionSceneUtil.Mat(new Color(0.30f, 0.20f, 0.12f)),
            DimensionSceneUtil.Mat(new Color(0.24f, 0.10f, 0.10f)),
        };

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
        l.color = lampColor;
        _lamp.AddComponent<FlickerLight>();

        DimensionSceneUtil.AmbienceLoop2D(gameObject, _archive ? "amb_d5" : "amb_d1", 120f, 0.06f, 0.35f);
    }

    void Update()
    {
        var cam = ObserverState.Cam;
        if (cam == null) return;
        if (!_atmosApplied)
        {
            DimensionSceneUtil.ApplyAtmosphere(ambientColor, fogColor, fogDensity, backgroundColor);
            _atmosApplied = true;
        }

        Vector3 p = cam.transform.position;
        var playerCell = new Vector2Int(Mathf.RoundToInt(p.x / cellSize), Mathf.RoundToInt(p.z / cellSize));
        _lastPlayerCell = playerCell;

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
        if (coord != _lastPlayerCell) BuildProps(cell);   // never on top of the player
    }

    void DespawnCell(Vector2Int coord)
    {
        var cell = _cells[coord];
        ReturnWalls(cell);
        ClearProps(cell);
        if (_door != null && _door.activeSelf && _doorCell == coord) DespawnDoor();
        _cells.Remove(coord);
        RefreshPillarsAround(coord);
    }

    // A pillar keyed by cell v sits at v's NORTH-EAST vertex. It exists iff any of the
    // four wall slots meeting that vertex is present. Cells refresh their four corner
    // pillars whenever their walls change, so junctions stay in sync with reshuffles.
    void RefreshPillarsAround(Vector2Int c)
    {
        RefreshPillar(c);
        RefreshPillar(new Vector2Int(c.x - 1, c.y));
        RefreshPillar(new Vector2Int(c.x, c.y - 1));
        RefreshPillar(new Vector2Int(c.x - 1, c.y - 1));
    }

    void RefreshPillar(Vector2Int v)
    {
        bool needed = HasWall(v, north: true) || HasWall(v, north: false)
                   || HasWall(new Vector2Int(v.x, v.y + 1), north: false)   // cell above's east wall ends here
                   || HasWall(new Vector2Int(v.x + 1, v.y), north: true);   // cell right's north wall ends here
        bool has = _pillars.ContainsKey(v);
        if (needed == has) return;
        if (needed)
        {
            GameObject p = _pillarPool.Count > 0 ? _pillarPool.Pop() : NewWall();
            p.name = "Pillar";
            p.SetActive(true);
            p.transform.position = new Vector3((v.x + 0.5f) * cellSize, wallHeight * 0.5f, (v.y + 0.5f) * cellSize);
            p.transform.localScale = new Vector3(pillarSize, wallHeight, pillarSize);
            _pillars[v] = p;
        }
        else
        {
            var p = _pillars[v];
            p.SetActive(false);
            _pillarPool.Push(p);
            _pillars.Remove(v);
        }
    }

    bool HasWall(Vector2Int coord, bool north) =>
        _cells.TryGetValue(coord, out var cell) && Rand01(cell.seed, north ? 1 : 2) < wallChance;

    void Reroll(Cell cell)
    {
        cell.seed = unchecked(cell.seed * 7919 + 12345);
        BuildWalls(cell);
        ClearProps(cell);
        if (cell.coord != _lastPlayerCell) BuildProps(cell);
        cell.tracker.Reset();

        // The room moved just out of sight — a muffled drag/shuffle sells it.
        // Throttled so a sweep of rerolls doesn't stack one-shots.
        if (Time.time >= _nextShiftSfxTime &&
            Mathf.Max(Mathf.Abs(cell.coord.x - _lastPlayerCell.x),
                      Mathf.Abs(cell.coord.y - _lastPlayerCell.y)) <= 2)
        {
            _nextShiftSfxTime = Time.time + 4f;
            Vector3 center = new Vector3(cell.coord.x * cellSize, 1f, cell.coord.y * cellSize);
            DimensionSceneUtil.PlayOneShot3D(_archive ? "sfx_paper_shuffle" : "sfx_furniture_drag",
                center, 0.5f, 25f);
        }

        // A fresh layout may carry the exit — but only one door exists at a time.
        if ((_door == null || !_door.activeSelf) && Rand01(cell.seed, 7) < exitDoorChance)
            SpawnDoor(cell.coord, cell.seed);
    }

    void BuildWalls(Cell cell)
    {
        ReturnWalls(cell);
        float s = cellSize, cx = cell.coord.x * s, cz = cell.coord.y * s;
        // North + East edges only — each cell owns two wall slots, and density stays
        // walkable. Dead ends self-heal: look away and the blocking cell reshuffles.
        // Walls span BETWEEN grid vertices (length s - pillarSize) and never touch each
        // other; corner pillars fill every vertex. Overlapping walls of equal thickness
        // put identical faces on the identical plane and z-fight (the flickering
        // joint-line artifact) — butt joints against fatter pillars can't.
        float tN = wallThickness * 1.05f, tE = wallThickness * 0.95f, len = s - pillarSize;
        if (Rand01(cell.seed, 1) < wallChance)
            PlaceWall(cell, new Vector3(cx, wallHeight * 0.5f, cz + s * 0.5f), new Vector3(len, wallHeight, tN));
        if (Rand01(cell.seed, 2) < wallChance)
            PlaceWall(cell, new Vector3(cx + s * 0.5f, wallHeight * 0.5f, cz), new Vector3(tE, wallHeight, len));
        RefreshPillarsAround(cell.coord);
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

    // ── per-cell furniture (polish pass) ─────────────────────────────
    // Deterministic from cell.seed, so a reroll = a genuinely different room.
    // Layout keeps a diagonal path clear and stays ≥1.2 m off cell edges so
    // doorway gaps never clog. Compound props are cheap to rebuild (event-
    // driven, only on reroll) so they're destroyed rather than pooled.

    void BuildProps(Cell cell)
    {
        int seed = cell.seed;
        float s = cellSize;
        Vector3 origin = new Vector3(cell.coord.x * s, 0f, cell.coord.y * s);
        bool hasN = Rand01(seed, 1) < wallChance, hasE = Rand01(seed, 2) < wallChance;
        float tN = wallThickness * 1.05f, tE = wallThickness * 0.95f;

        // Wall dressing on this cell's own walls (interior faces).
        if (hasN && Rand01(seed, 31) < 0.45f)
        {
            var art = DimensionPropKit.Painting(_root, _woodMat,
                _canvasMats[Mathf.Abs(Hash(cell.coord.x, cell.coord.y, seed)) % _canvasMats.Length]);
            art.transform.position = origin + new Vector3((Rand01(seed, 32) - 0.5f) * (s - 3f),
                1.7f, s * 0.5f - tN * 0.5f - 0.06f);
            art.transform.rotation = Quaternion.Euler(0f, 180f, 0f);      // face into the cell
            cell.props.Add(art);
        }
        if (hasE && Rand01(seed, 33) < 0.25f)
        {
            // Every clock shows a DIFFERENT wrong time — and it changes when the room does.
            var clock = DimensionPropKit.WallClock(_root, _clockFaceMat, _clockHandMat,
                Rand01(seed, 34) * 12f, Rand01(seed, 35) * 60f);
            clock.transform.position = origin + new Vector3(s * 0.5f - tE * 0.5f - 0.06f,
                1.8f, (Rand01(seed, 36) - 0.5f) * (s - 3f));
            clock.transform.rotation = Quaternion.Euler(0f, -90f, 0f);    // face west, into the cell
            cell.props.Add(clock);
        }

        if (Rand01(seed, 40) >= PropChance) return;

        float yawA = Mathf.Floor(Rand01(seed, 41) * 4f) * 90f + (Rand01(seed, 42) - 0.5f) * 24f;
        float sideX = Rand01(seed, 43) < 0.5f ? -1f : 1f;
        Vector3 spotA = origin + new Vector3(sideX * 1.45f, 0f, (Rand01(seed, 44) - 0.5f) * 1.6f);
        Vector3 spotB = origin + new Vector3(-sideX * 1.35f, 0f, (Rand01(seed, 45) - 0.5f) * 1.6f);
        int variant = Mathf.Abs(Hash(cell.coord.y, cell.coord.x, seed)) % 4;

        if (!_archive)
        {
            GameObject a = null, b = null;
            switch (variant)
            {
                case 0:
                    a = DimensionPropKit.Couch(_root, _fabricMat, _woodMat);
                    b = DimensionPropKit.CoffeeTable(_root, _woodMat);
                    break;
                case 1:
                    a = DimensionPropKit.Armchair(_root, _fabricMat, _woodMat);
                    b = DimensionPropKit.FloorLamp(_root, _clockHandMat,
                        Rand01(seed, 46) < 0.12f ? _shadeLitMat : _shadeDarkMat,
                        withLight: Rand01(seed, 46) < 0.12f, lightColor: lampColor);
                    break;
                case 2:
                    a = DimensionPropKit.Couch(_root, _fabricMat, _woodMat);
                    b = DimensionPropKit.Armchair(_root, _fabricMat, _woodMat);
                    break;
                default:
                    a = DimensionPropKit.DiningTable(_root, _woodMat);
                    b = DimensionPropKit.ChairSimple(_root, _woodMat);
                    break;
            }
            a.transform.SetPositionAndRotation(spotA, Quaternion.Euler(0f, yawA, 0f));
            b.transform.SetPositionAndRotation(spotB, Quaternion.Euler(0f, yawA + 180f + (Rand01(seed, 47) - 0.5f) * 30f, 0f));
            cell.props.Add(a);
            cell.props.Add(b);
        }
        else
        {
            // Archive theme: shelves hug the north wall when it exists; a reading
            // desk + chair and toppled stacks re-deal every reroll.
            var shelf = DimensionPropKit.Shelf(_root, _woodMat, _booksMat, 1.4f, 2.2f, 4);
            if (hasN)
                shelf.transform.SetPositionAndRotation(
                    origin + new Vector3((Rand01(seed, 48) - 0.5f) * (s - 3.4f), 0f, s * 0.5f - tN * 0.5f - 0.24f),
                    Quaternion.Euler(0f, 180f, 0f));
            else
                shelf.transform.SetPositionAndRotation(spotA, Quaternion.Euler(0f, yawA, 0f));
            cell.props.Add(shelf);

            if (variant >= 1)
            {
                var desk = DimensionPropKit.Desk(_root, _woodMat);
                desk.transform.SetPositionAndRotation(spotB, Quaternion.Euler(0f, yawA + 180f, 0f));
                cell.props.Add(desk);
                var chair = DimensionPropKit.ChairSimple(_root, _woodMat);
                chair.transform.SetPositionAndRotation(
                    spotB + Quaternion.Euler(0f, yawA + 180f, 0f) * new Vector3(0f, 0f, 0.65f),
                    Quaternion.Euler(0f, yawA, 0f));
                cell.props.Add(chair);
            }
            if (variant >= 2)
            {
                var stack = DimensionPropKit.BookStack(_root, _booksMat, 3 + variant);
                stack.transform.position = spotB + new Vector3(0.7f, 0f, 0.4f);
                cell.props.Add(stack);
            }
        }
    }

    void ClearProps(Cell cell)
    {
        foreach (var prop in cell.props) if (prop != null) Destroy(prop);
        cell.props.Clear();
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

    [Tooltip("Corner pillar width at grid vertices. Must exceed wallThickness so wall ends bury cleanly (no coplanar faces).")]
    public float pillarSize = 0.55f;

    [Header("Theme (defaults = D1 backrooms; D5 Archive overrides these)")]
    public Color wallColor = new Color(0.75f, 0.70f, 0.50f);
    public Color floorColor = new Color(0.45f, 0.42f, 0.30f);
    public Color ceilColor = new Color(0.80f, 0.80f, 0.75f);
    public Color lampColor = new Color(1f, 0.93f, 0.75f);
    public Color ambientColor = new Color(0.28f, 0.26f, 0.18f);
    public Color fogColor = new Color(0.55f, 0.50f, 0.33f);
    public float fogDensity = 0.045f;
    public Color backgroundColor = new Color(0.35f, 0.32f, 0.20f);
}
