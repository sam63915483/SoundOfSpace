using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// D28 "The Long Table": a banquet hall whose table runs to the horizon. You walk
/// the tabletop. The chairs flanking it rearrange whenever they leave your view —
/// and some climb ONTO the table behind your back as obstacles. Candelabra risers
/// erupt from the wood ahead, faster the further you push. The far door never
/// moves. Pure gauntlet.
/// </summary>
public class LongTableController : MonoBehaviour
{
    class ChairCell
    {
        public float z;
        public readonly List<GameObject> chairs = new List<GameObject>();
        public readonly ObservationTracker tracker = new ObservationTracker();
        public int seed;
    }

    readonly List<ChairCell> _cells = new List<ChairCell>();
    readonly Stack<GameObject> _chairPool = new Stack<GameObject>();
    readonly List<GameObject> _candelabras = new List<GameObject>();
    Material _chairMat, _brassMat, _flameMat;
    Transform _root;
    float _nextRiserTime;
    float _tableTopY;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        var wallMat  = DimensionSceneUtil.Mat(new Color(0.20f, 0.08f, 0.08f), 0.15f);
        var floorMat = DimensionSceneUtil.Mat(new Color(0.12f, 0.07f, 0.05f), 0.2f);
        var woodMat  = DimensionSceneUtil.Mat(new Color(0.32f, 0.19f, 0.10f), 0.3f);
        var clothMat = DimensionSceneUtil.Mat(new Color(0.45f, 0.10f, 0.10f), 0.15f);
        _chairMat = DimensionSceneUtil.Mat(new Color(0.24f, 0.14f, 0.08f), 0.2f);
        _brassMat = DimensionSceneUtil.Mat(new Color(0.55f, 0.42f, 0.16f), 0.6f);
        _flameMat = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.65f, 0.25f), 2.8f);
        _tableTopY = tableHeight;

        float hallLen = tableLength + 24f;
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Floor",
            new Vector3(0f, -0.5f, hallLen * 0.5f - 8f), new Vector3(20f, 1f, hallLen), floorMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "WallL",
            new Vector3(-10.5f, 6f, hallLen * 0.5f - 8f), new Vector3(1f, 12f, hallLen), wallMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "WallR",
            new Vector3(10.5f, 6f, hallLen * 0.5f - 8f), new Vector3(1f, 12f, hallLen), wallMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "WallBack",
            new Vector3(0f, 6f, -8f), new Vector3(21f, 12f, 1f), wallMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "WallFront",
            new Vector3(0f, 6f, hallLen - 8f), new Vector3(21f, 12f, 1f), wallMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Ceiling",
            new Vector3(0f, 12.5f, hallLen * 0.5f - 8f), new Vector3(21f, 1f, hallLen), wallMat, _root);

        // The table itself: legs are implied; the top is the road.
        DimensionSceneUtil.Block(PrimitiveType.Cube, "TableSlab",
            new Vector3(0f, tableHeight * 0.5f, tableLength * 0.5f), new Vector3(4.4f, tableHeight, tableLength), woodMat, _root);
        var runner = DimensionSceneUtil.Block(PrimitiveType.Cube, "TableRunner",
            new Vector3(0f, tableHeight + 0.02f, tableLength * 0.5f), new Vector3(1.6f, 0.04f, tableLength), clothMat, _root);
        Object.Destroy(runner.GetComponent<Collider>());
        // Steps up onto the table at the near end.
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Step1",
            new Vector3(0f, 0.3f, -1.8f), new Vector3(2.4f, 0.6f, 1.4f), woodMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Step2",
            new Vector3(0f, 0.75f, -0.6f), new Vector3(2.4f, 0.75f, 1.2f), woodMat, _root);

        // Wall sconces for mood.
        for (float z = 4f; z < tableLength; z += 16f)
            for (int side = -1; side <= 1; side += 2)
            {
                var lg = new GameObject("Sconce");
                lg.transform.SetParent(_root, false);
                lg.transform.position = new Vector3(side * 9.5f, 4.5f, z);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point; l.range = 14f; l.intensity = 1.3f;
                l.color = new Color(1f, 0.6f, 0.3f);
                if (z % 32f < 16f) lg.AddComponent<FlickerLight>();
            }

        // Chair rows every few metres down both flanks.
        for (float z = 2f; z < tableLength - 2f; z += 4f)
        {
            var cell = new ChairCell { z = z, seed = Mathf.RoundToInt(z * 217f) + 9 };
            _cells.Add(cell);
            BuildChairs(cell);
        }

        // The far door — it has never moved. It never will.
        var frame = DimensionSceneUtil.Mat(new Color(0.10f, 0.08f, 0.07f));
        var pane  = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.85f, 0.5f), 2.6f);
        float doorZ = tableLength + 3f;
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostL",  new Vector3(-0.9f, tableHeight + 1.7f, doorZ), new Vector3(0.35f, 3.4f, 0.35f), frame, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostR",  new Vector3( 0.9f, tableHeight + 1.7f, doorZ), new Vector3(0.35f, 3.4f, 0.35f), frame, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Lintel", new Vector3(0f, tableHeight + 3.55f, doorZ),   new Vector3(2.15f, 0.35f, 0.35f), frame, _root);
        var glow = DimensionSceneUtil.Block(PrimitiveType.Cube, "Glow",
            new Vector3(0f, tableHeight + 1.7f, doorZ), new Vector3(1.45f, 3.3f, 0.05f), pane, _root);
        Object.Destroy(glow.GetComponent<Collider>());
        // A landing between table end and door.
        DimensionSceneUtil.Block(PrimitiveType.Cube, "DoorLanding",
            new Vector3(0f, tableHeight - 0.15f, tableLength + 1.5f), new Vector3(4.4f, 0.3f, 3.4f),
            DimensionSceneUtil.Mat(new Color(0.28f, 0.17f, 0.09f)), _root);
        DimensionSceneUtil.CreatePortal("ToBackrooms", new Vector3(0f, tableHeight + 1.7f, doorZ),
            new Vector3(1.45f, 3.2f, 0.8f), LevelPortal.PortalAction.EnterInterior, nextScene, _root);

        _nextRiserTime = Time.time + 8f;
        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(55f, 2f, 0.06f), 500f, 1f);
    }

    void BuildChairs(ChairCell cell)
    {
        foreach (var c in cell.chairs) { c.SetActive(false); _chairPool.Push(c); }
        cell.chairs.Clear();
        // 0-2 chairs per flank; a small chance one has CLIMBED ONTO the table.
        for (int side = -1; side <= 1; side += 2)
        {
            int n = Mathf.Abs(cell.seed * side) % 3;
            for (int k = 0; k < n; k++)
            {
                GameObject chair = _chairPool.Count > 0 ? _chairPool.Pop() : NewChair();
                chair.SetActive(true);
                bool onTable = Rand01(cell.seed, side * 10 + k) < chairOnTableChance;
                float x = onTable
                    ? Mathf.Lerp(-1.4f, 1.4f, Rand01(cell.seed, side * 20 + k))
                    : side * (2.9f + Rand01(cell.seed, side * 30 + k) * 2.2f);
                chair.transform.SetPositionAndRotation(
                    new Vector3(x, onTable ? tableHeight : 0f, cell.z + (Rand01(cell.seed, side * 40 + k) - 0.5f) * 2.4f),
                    Quaternion.Euler(0f, Rand01(cell.seed, side * 50 + k) * 360f, 0f));
                cell.chairs.Add(chair);
            }
        }
    }

    GameObject NewChair()
    {
        var chair = new GameObject("Chair");
        chair.transform.SetParent(_root, false);
        var seat = DimensionSceneUtil.Block(PrimitiveType.Cube, "Seat",
            Vector3.zero, new Vector3(0.85f, 0.12f, 0.85f), _chairMat, chair.transform);
        seat.transform.localPosition = new Vector3(0f, 0.85f, 0f);
        var back = DimensionSceneUtil.Block(PrimitiveType.Cube, "Back",
            Vector3.zero, new Vector3(0.85f, 1.5f, 0.12f), _chairMat, chair.transform);
        back.transform.localPosition = new Vector3(0f, 1.6f, -0.38f);
        for (int lx = -1; lx <= 1; lx += 2)
            for (int lz = -1; lz <= 1; lz += 2)
            {
                var leg = DimensionSceneUtil.Block(PrimitiveType.Cube, "Leg",
                    Vector3.zero, new Vector3(0.09f, 0.85f, 0.09f), _chairMat, chair.transform);
                leg.transform.localPosition = new Vector3(lx * 0.36f, 0.42f, lz * 0.36f);
                Object.Destroy(leg.GetComponent<Collider>());
            }
        return chair;
    }

    void SpawnCandelabra(float z)
    {
        var cand = new GameObject("Candelabra");
        cand.transform.SetParent(_root, false);
        DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Stem",
            new Vector3(0f, 0.7f, 0f), new Vector3(0.16f, 0.7f, 0.16f), _brassMat, cand.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Arms",
            new Vector3(0f, 1.32f, 0f), new Vector3(1.5f, 0.08f, 0.08f), _brassMat, cand.transform);
        for (int k = -1; k <= 1; k++)
        {
            var flame = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Flame",
                new Vector3(k * 0.7f, 1.55f, 0f), new Vector3(0.14f, 0.24f, 0.14f), _flameMat, cand.transform);
            Object.Destroy(flame.GetComponent<Collider>());
        }
        var lg = new GameObject("CandLight");
        lg.transform.SetParent(cand.transform, false);
        lg.transform.localPosition = new Vector3(0f, 1.7f, 0f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = 8f; l.intensity = 1.4f;
        l.color = new Color(1f, 0.65f, 0.3f);
        lg.AddComponent<FlickerLight>();

        float x = Random.Range(-1.5f, 1.5f);
        cand.transform.position = new Vector3(x, _tableTopY - 2.2f, z);   // starts inside the wood
        cand.AddComponent<CandelabraRiser>().targetY = _tableTopY;
        _candelabras.Add(cand);
    }

    static float Rand01(int seed, int salt) =>
        (Mathf.Abs(unchecked(seed * (int)2654435761 + salt * 40503)) % 10000) / 10000f;

    void Update()
    {
        var cam = ObserverState.Cam;
        if (cam == null) return;
        if (!_atmosApplied)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.16f, 0.10f, 0.09f),
                fog: new Color(0.09f, 0.05f, 0.04f), fogDensity: 0.017f,
                background: new Color(0.05f, 0.03f, 0.025f));
            _atmosApplied = true;
        }

        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        Vector3 playerPos = _player != null && _player.Rigidbody != null
            ? _player.Rigidbody.position : cam.transform.position;

        // Chair rows reshuffle unseen (never the row beside you).
        foreach (var cell in _cells)
        {
            var b = new Bounds(new Vector3(0f, 1.2f, cell.z), new Vector3(14f, 3.5f, 3.6f));
            cell.tracker.Tick(b, out bool justLost, observeMaxDistance);
            if (justLost && Mathf.Abs(playerPos.z - cell.z) > 3f)
            {
                cell.seed = unchecked(cell.seed * 7919 + 12345);
                BuildChairs(cell);
                cell.tracker.Reset();
            }
        }

        // Candelabra risers erupt ahead — faster the deeper you are down the table.
        if (Time.time >= _nextRiserTime && playerPos.z > 0f && playerPos.z < tableLength - 12f)
        {
            SpawnCandelabra(playerPos.z + Random.Range(8f, 16f));
            float progress = Mathf.Clamp01(playerPos.z / tableLength);
            _nextRiserTime = Time.time + Mathf.Lerp(9f, 3.5f, progress);
        }
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Table")]
    [Tooltip("Length of the tabletop road (metres).")]
    public float tableLength = 160f;
    [Tooltip("Tabletop height — the walking surface.")]
    public float tableHeight = 1.1f;
    [Tooltip("Beyond this distance a chair row counts as unobserved even on screen.")]
    public float observeMaxDistance = 55f;
    [Tooltip("Chance a reshuffled chair lands ON the table as an obstacle.")]
    [Range(0f, 1f)] public float chairOnTableChance = 0.22f;

    [Header("Exit")]
    [Tooltip("Scene the far door leads to — the Backrooms hub ends the reel.")]
    public string nextScene = "R1_Backrooms";
}

/// <summary>Rises out of the tabletop, then stands forever.</summary>
public class CandelabraRiser : MonoBehaviour
{
    [HideInInspector] public float targetY;

    void Update()
    {
        Vector3 p = transform.position;
        if (p.y >= targetY) { enabled = false; return; }
        p.y = Mathf.MoveTowards(p.y, targetY, 1.6f * Time.deltaTime);
        transform.position = p;
    }
}
