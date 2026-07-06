using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// D9 "The Red Forest": endless dark-red pines under heavy fog and a dim blood moon.
/// Trees reshuffle when unobserved (D1's rule, sparser); a glowing white doe stands
/// frozen while watched and walks toward the exit clearing while unseen — let it
/// lead you out. She leaves glowing hoofprints that fade, so you can track her after
/// she's moved; a soft bell hum on her scales with gaze alignment (audio radar).
/// Distant howls keep you honest.
/// </summary>
public class RedForestController : MonoBehaviour
{
    class Cell
    {
        public Vector2Int coord;
        public int seed;
        public GameObject prop;                 // pine, rock or log — null when empty
        public readonly ObservationTracker tracker = new ObservationTracker();
    }

    readonly Dictionary<Vector2Int, Cell> _cells = new Dictionary<Vector2Int, Cell>();
    readonly Stack<GameObject> _treePool = new Stack<GameObject>();
    readonly Stack<GameObject> _rockPool = new Stack<GameObject>();
    readonly Stack<GameObject> _logPool = new Stack<GameObject>();
    readonly List<Vector2Int> _toDespawn = new List<Vector2Int>();

    Transform _root;
    Material _trunkMat, _canopyMat, _rockMat, _printMat;
    Transform _doe;
    Rigidbody _doeRb;
    Transform _doeBody;
    readonly ObservationTracker _doeTracker = new ObservationTracker();
    bool _doeObservedNow = true;
    bool _doeWalking;
    float _doeBobPhase;
    AudioSource _doeChime;
    AudioSource _howler;
    AudioClip _howlClip;
    float _nextHowlTime;
    Vector3 _clearingPos;
    Vector3 _lastPrintPos;
    readonly Queue<Transform> _prints = new Queue<Transform>();
    readonly List<PrintFade> _activePrints = new List<PrintFade>();
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    class PrintFade { public Transform tf; public Renderer rend; public MaterialPropertyBlock mpb; public float bornAt; }
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        _root = transform;
        _trunkMat  = DimensionSceneUtil.Mat(new Color(0.20f, 0.07f, 0.06f), 0.05f);
        _canopyMat = DimensionSceneUtil.Mat(new Color(0.28f, 0.045f, 0.045f), 0.05f);
        _rockMat   = DimensionSceneUtil.Mat(new Color(0.16f, 0.08f, 0.08f), 0.1f);
        _printMat  = DimensionSceneUtil.EmissiveMat(new Color(0.85f, 0.9f, 1f), 1.4f);
        var groundMat = DimensionSceneUtil.Mat(new Color(0.13f, 0.045f, 0.045f), 0.05f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Ground",
            new Vector3(0f, -0.5f, 0f), new Vector3(2000f, 1f, 2000f), groundMat, _root);
        DimensionSceneUtil.CreateDirectionalLight(new Color(0.9f, 0.32f, 0.28f), 0.4f, new Vector3(30f, -40f, 0f), true);

        // A blood moon low over the treeline.
        var moon = DimensionSceneUtil.Block(PrimitiveType.Sphere, "BloodMoon",
            new Vector3(-160f, 90f, 240f), Vector3.one * 26f,
            DimensionSceneUtil.EmissiveMat(new Color(0.9f, 0.28f, 0.2f), 1.8f), _root);
        Destroy(moon.GetComponent<Collider>());

        float a = Random.value * Mathf.PI * 2f;
        _clearingPos = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * clearingDistance;
        BuildClearing();
        BuildDoe();
        _lastPrintPos = _doe.position;

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(70f, 2f, 0.06f), 500f, 1f);
        _howlClip = HowlClip();
        var howlGo = new GameObject("Howler");
        howlGo.transform.SetParent(_root, false);
        _howler = howlGo.AddComponent<AudioSource>();
        _howler.spatialBlend = 1f;
        _howler.rolloffMode = AudioRolloffMode.Linear;
        _howler.maxDistance = 300f;
        _nextHowlTime = Time.time + 12f;
    }

    // Descending mournful howl with vibrato — always far away, never a threat.
    static AudioClip HowlClip()
    {
        int rate = 44100;
        float seconds = 2.8f;
        int samples = (int)(rate * seconds);
        var data = new float[samples];
        double phase = 0.0;
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            float f = Mathf.Lerp(430f, 210f, Mathf.SmoothStep(0f, 1f, t / seconds)) * (1f + 0.025f * Mathf.Sin(t * 31f));
            phase += 2.0 * Mathf.PI * f / rate;
            float env = Mathf.Clamp01(t / 0.4f) * Mathf.Exp(-Mathf.Max(0f, t - 1.6f) * 2.2f);
            data[i] = (Mathf.Sin((float)phase) * 0.85f + Mathf.Sin((float)phase * 2.02f) * 0.15f) * env * 0.5f;
        }
        var clip = AudioClip.Create("howl", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
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

        // Body group bobs while she walks; legs stay planted on the transform root.
        var body = new GameObject("Body");
        body.transform.SetParent(doe.transform, false);
        _doeBody = body.transform;
        Part(PrimitiveType.Sphere, "Torso", new Vector3(0f, 1.15f, 0f), new Vector3(0.75f, 0.85f, 1.6f), white, body.transform);
        Part(PrimitiveType.Sphere, "Neck", new Vector3(0f, 1.55f, 0.6f), new Vector3(0.32f, 0.6f, 0.32f), white, body.transform);
        Part(PrimitiveType.Sphere, "Head", new Vector3(0f, 1.85f, 0.85f), Vector3.one * 0.42f, white, body.transform);
        Part(PrimitiveType.Sphere, "Snout", new Vector3(0f, 1.78f, 1.12f), new Vector3(0.2f, 0.2f, 0.3f), white, body.transform);
        Part(PrimitiveType.Sphere, "EarL", new Vector3(-0.16f, 2.14f, 0.78f), new Vector3(0.1f, 0.3f, 0.12f), white, body.transform);
        Part(PrimitiveType.Sphere, "EarR", new Vector3(0.16f, 2.14f, 0.78f), new Vector3(0.1f, 0.3f, 0.12f), white, body.transform);
        Part(PrimitiveType.Sphere, "Tail", new Vector3(0f, 1.32f, -0.82f), new Vector3(0.14f, 0.14f, 0.14f), white, body.transform);
        Part(PrimitiveType.Cylinder, "LegFL", new Vector3(-0.25f, 0.4f, 0.55f), new Vector3(0.11f, 0.4f, 0.11f), white, doe.transform);
        Part(PrimitiveType.Cylinder, "LegFR", new Vector3(0.25f, 0.4f, 0.55f), new Vector3(0.11f, 0.4f, 0.11f), white, doe.transform);
        Part(PrimitiveType.Cylinder, "LegBL", new Vector3(-0.25f, 0.4f, -0.55f), new Vector3(0.11f, 0.4f, 0.11f), white, doe.transform);
        Part(PrimitiveType.Cylinder, "LegBR", new Vector3(0.25f, 0.4f, -0.55f), new Vector3(0.11f, 0.4f, 0.11f), white, doe.transform);

        var lightGo = new GameObject("DoeGlow");
        lightGo.transform.SetParent(doe.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point; l.range = 14f; l.intensity = 1.8f;
        l.color = new Color(0.9f, 0.92f, 1f);

        Vector3 dir = _clearingPos.normalized;
        _doe.SetPositionAndRotation(dir * 14f, Quaternion.LookRotation(dir, Vector3.up));

        _doeChime = DimensionSceneUtil.LoopingAudio(doe, DimensionSceneUtil.ToneClip(660f, 2f, 0.4f), 220f, 1f);
    }

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

        // Doe observation + gaze-reactive chime.
        var db = new Bounds(_doe.position + Vector3.up * 1.2f, new Vector3(2.6f, 2.8f, 2.6f));
        _doeObservedNow = _doeTracker.Tick(db, out _, float.PositiveInfinity);
        if (_doeChime != null)
        {
            Vector3 to = _doe.position - cam.transform.position;
            float align = Vector3.Dot(cam.transform.forward, to.normalized);
            _doeChime.volume = Mathf.Lerp(0.05f, 1f, Mathf.InverseLerp(0.2f, 0.95f, align));
        }

        // Walk bob while she moves; settle when frozen.
        if (_doeBody != null)
        {
            if (_doeWalking) _doeBobPhase += Time.deltaTime * 9f;
            float bob = _doeWalking ? Mathf.Sin(_doeBobPhase) * 0.05f : 0f;
            _doeBody.localPosition = Vector3.Lerp(_doeBody.localPosition, new Vector3(0f, bob, 0f), Time.deltaTime * 8f);
        }

        // Hoofprints fade out over printLifeSeconds.
        for (int i = _activePrints.Count - 1; i >= 0; i--)
        {
            var pr = _activePrints[i];
            float age = Time.time - pr.bornAt;
            if (age >= printLifeSeconds)
            {
                pr.tf.gameObject.SetActive(false);
                _prints.Enqueue(pr.tf);
                _activePrints.RemoveAt(i);
                continue;
            }
            float fade = 1f - age / printLifeSeconds;
            pr.mpb.SetColor(ColorId, new Color(0.85f, 0.9f, 1f, 1f) * fade);
            pr.mpb.SetColor(EmissionId, new Color(0.85f, 0.9f, 1f) * (1.4f * fade));
            pr.rend.SetPropertyBlock(pr.mpb);
        }

        // Distant howls, always from out in the fog.
        if (Time.time >= _nextHowlTime)
        {
            _nextHowlTime = Time.time + Random.Range(18f, 40f);
            float ha = Random.value * Mathf.PI * 2f;
            _howler.transform.position = p + new Vector3(Mathf.Cos(ha), 0.1f, Mathf.Sin(ha)) * Random.Range(80f, 140f);
            _howler.pitch = Random.Range(0.85f, 1.1f);
            _howler.PlayOneShot(_howlClip, 0.8f);
        }
    }

    void FixedUpdate()
    {
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        _doeWalking = false;
        if (_player == null || _player.Rigidbody == null || _doeObservedNow) return;

        Vector3 toClearing = _clearingPos - _doeRb.position; toClearing.y = 0f;
        if (toClearing.magnitude < 4f) return;
        Vector3 toPlayer = _player.Rigidbody.position - _doeRb.position; toPlayer.y = 0f;
        if (toPlayer.magnitude > doeLeash) return;

        Vector3 dir = toClearing.normalized;
        _doeRb.MovePosition(_doeRb.position + dir * doeSpeed * Time.fixedDeltaTime);
        _doeRb.MoveRotation(Quaternion.LookRotation(dir, Vector3.up));
        _doeWalking = true;

        // Drop a glowing hoofprint every stride — her trail outlives her look-freezes.
        if ((_doeRb.position - _lastPrintPos).sqrMagnitude > printStride * printStride)
        {
            _lastPrintPos = _doeRb.position;
            DropPrint(_doeRb.position, dir);
        }
    }

    void DropPrint(Vector3 pos, Vector3 dir)
    {
        Transform print;
        if (_prints.Count > 0) { print = _prints.Dequeue(); print.gameObject.SetActive(true); }
        else
        {
            var go = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Hoofprint",
                Vector3.zero, new Vector3(0.16f, 0.012f, 0.24f), _printMat, _root);
            Destroy(go.GetComponent<Collider>());
            print = go.transform;
        }
        float side = _activePrints.Count % 2 == 0 ? -0.18f : 0.18f;
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        print.SetPositionAndRotation(pos + right * side + Vector3.up * 0.02f,
            Quaternion.LookRotation(dir, Vector3.up));
        var pf = new PrintFade
        {
            tf = print,
            rend = print.GetComponent<Renderer>(),
            mpb = new MaterialPropertyBlock(),
            bornAt = Time.time,
        };
        _activePrints.Add(pf);
    }

    Bounds CellBounds(Vector2Int c) =>
        new Bounds(new Vector3(c.x * cellSize, 4f, c.y * cellSize),
                   new Vector3(cellSize, 9f, cellSize));

    void SpawnCell(Vector2Int coord)
    {
        var cell = new Cell { coord = coord, seed = Hash(coord.x, coord.y, worldSeed) };
        _cells[coord] = cell;
        BuildProp(cell);
    }

    void DespawnCell(Vector2Int coord)
    {
        ReturnProp(_cells[coord]);
        _cells.Remove(coord);
    }

    void Reroll(Cell cell)
    {
        cell.seed = unchecked(cell.seed * 7919 + 12345);
        BuildProp(cell);
        cell.tracker.Reset();
    }

    void BuildProp(Cell cell)
    {
        ReturnProp(cell);
        Vector3 center = new Vector3(cell.coord.x * cellSize, 0f, cell.coord.y * cellSize);
        if ((center - _clearingPos).sqrMagnitude < clearingRadius * clearingRadius) return;
        if (center.sqrMagnitude < 36f) return;

        float roll = Rand01(cell.seed, 1);
        Stack<GameObject> pool;
        System.Func<GameObject> make;
        if (roll < treeChance) { pool = _treePool; make = NewTree; }
        else if (roll < treeChance + 0.08f) { pool = _rockPool; make = NewRock; }
        else if (roll < treeChance + 0.14f) { pool = _logPool; make = NewLog; }
        else return;

        GameObject prop = pool.Count > 0 ? pool.Pop() : make();
        prop.SetActive(true);
        float jx = (Rand01(cell.seed, 2) - 0.5f) * (cellSize - 3f);
        float jz = (Rand01(cell.seed, 3) - 0.5f) * (cellSize - 3f);
        float s = Mathf.Lerp(0.8f, 1.35f, Rand01(cell.seed, 5));
        prop.transform.SetPositionAndRotation(center + new Vector3(jx, 0f, jz),
            Quaternion.Euler(0f, Rand01(cell.seed, 4) * 360f, 0f));
        prop.transform.localScale = Vector3.one * (prop.name == "Pine" ? s : 1f);
        cell.prop = prop;
    }

    // Proper pine silhouette: trunk + three stacked, shrinking canopy discs.
    GameObject NewTree()
    {
        var tree = new GameObject("Pine");
        tree.transform.SetParent(_root, false);
        DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Trunk",
            new Vector3(0f, 2f, 0f), new Vector3(0.45f, 2f, 0.45f), _trunkMat, tree.transform);
        float[] ys = { 3.6f, 5.4f, 7.1f };
        float[] widths = { 3.8f, 2.8f, 1.7f };
        float[] heights = { 1.9f, 1.7f, 1.6f };
        for (int i = 0; i < 3; i++)
        {
            var c = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Canopy" + i,
                new Vector3(0f, ys[i], 0f), new Vector3(widths[i], heights[i], widths[i]), _canopyMat, tree.transform);
            Destroy(c.GetComponent<Collider>());
        }
        var tip = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Tip",
            new Vector3(0f, 8.3f, 0f), new Vector3(0.7f, 1.3f, 0.7f), _canopyMat, tree.transform);
        Destroy(tip.GetComponent<Collider>());
        return tree;
    }

    GameObject NewRock()
    {
        var rock = new GameObject("Rock");
        rock.transform.SetParent(_root, false);
        var r1 = DimensionSceneUtil.Block(PrimitiveType.Cube, "R1",
            new Vector3(0f, 0.35f, 0f), new Vector3(1.4f, 0.9f, 1.1f), _rockMat, rock.transform);
        r1.transform.localRotation = Quaternion.Euler(12f, 30f, 8f);
        var r2 = DimensionSceneUtil.Block(PrimitiveType.Cube, "R2",
            new Vector3(0.6f, 0.2f, 0.4f), new Vector3(0.7f, 0.5f, 0.6f), _rockMat, rock.transform);
        r2.transform.localRotation = Quaternion.Euler(-8f, 70f, 14f);
        return rock;
    }

    GameObject NewLog()
    {
        var log = new GameObject("Log");
        log.transform.SetParent(_root, false);
        var body = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Fallen",
            new Vector3(0f, 0.3f, 0f), new Vector3(0.55f, 1.9f, 0.55f), _trunkMat, log.transform);
        body.transform.localRotation = Quaternion.Euler(88f, 0f, 0f);
        var stump = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Stump",
            new Vector3(0.9f, 0.25f, -1.2f), new Vector3(0.6f, 0.25f, 0.6f), _trunkMat, log.transform);
        return log;
    }

    void ReturnProp(Cell cell)
    {
        if (cell.prop == null) return;
        cell.prop.SetActive(false);
        if (cell.prop.name == "Pine") _treePool.Push(cell.prop);
        else if (cell.prop.name == "Rock") _rockPool.Push(cell.prop);
        else _logPool.Push(cell.prop);
        cell.prop = null;
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
    [Tooltip("Chance a cell grows a pine (rocks/logs roll above this).")]
    [Range(0f, 1f)] public float treeChance = 0.62f;
    [Tooltip("Deterministic world seed.")]
    public int worldSeed = 909;
    [Tooltip("Beyond this distance a cell counts as unobserved even on screen.")]
    public float observeMaxDistance = 70f;

    [Header("Doe")]
    [Tooltip("Walk speed while unseen (m/s).")]
    public float doeSpeed = 3.2f;
    [Tooltip("She waits if you fall further behind than this.")]
    public float doeLeash = 32f;
    [Tooltip("Metres between hoofprints.")]
    public float printStride = 1.3f;
    [Tooltip("Seconds a hoofprint glows before fading out.")]
    public float printLifeSeconds = 16f;

    [Header("Exit clearing")]
    [Tooltip("Distance from spawn to the exit clearing.")]
    public float clearingDistance = 130f;
    [Tooltip("Tree-free radius around the clearing.")]
    public float clearingRadius = 14f;
    [Tooltip("Scene the clearing portal leads to.")]
    public string nextScene = "D11_Shelves";
}
