using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// D15 "The Congregation": a vast cathedral nave lit by colored shafts. The pew rows
/// reshuffle into new blockades whenever they leave your view — the aisle keeps
/// closing behind and ahead of you. The altar door only OPENS while you look away
/// from it: walk to the top step, turn around, and back through.
/// </summary>
public class CongregationController : MonoBehaviour
{
    class PewRow
    {
        public float z;
        public readonly List<GameObject> segments = new List<GameObject>();
        public readonly ObservationTracker tracker = new ObservationTracker();
        public int seed;
    }

    readonly List<PewRow> _rows = new List<PewRow>();
    readonly Stack<GameObject> _pewPool = new Stack<GameObject>();
    Material _pewMat;
    Transform _root;
    Transform _doorSlab;
    readonly ObservationTracker _doorTracker = new ObservationTracker();
    float _doorOpenT;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    const float HallHalfWidth = 14f;

    void Awake()
    {
        _root = transform;
        var floorMat = DimensionSceneUtil.Mat(new Color(0.16f, 0.14f, 0.13f), 0.25f);
        var wallMat  = DimensionSceneUtil.Mat(new Color(0.22f, 0.19f, 0.17f), 0.1f);
        var pillarMat = DimensionSceneUtil.Mat(new Color(0.28f, 0.25f, 0.22f), 0.15f);
        _pewMat = DimensionSceneUtil.Mat(new Color(0.20f, 0.12f, 0.08f), 0.2f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Floor",
            new Vector3(0f, -0.5f, 45f), new Vector3(HallHalfWidth * 2f + 4f, 1f, 110f), floorMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "WallL",
            new Vector3(-HallHalfWidth - 1f, 9f, 45f), new Vector3(2f, 18f, 110f), wallMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "WallR",
            new Vector3(HallHalfWidth + 1f, 9f, 45f), new Vector3(2f, 18f, 110f), wallMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "WallBack",
            new Vector3(0f, 9f, -9f), new Vector3(HallHalfWidth * 2f + 4f, 18f, 2f), wallMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "WallFront",
            new Vector3(0f, 9f, 99f), new Vector3(HallHalfWidth * 2f + 4f, 18f, 2f), wallMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Ceiling",
            new Vector3(0f, 18.5f, 45f), new Vector3(HallHalfWidth * 2f + 4f, 1f, 110f), wallMat, _root);

        for (float z = 4f; z <= 86f; z += 12f)
        {
            DimensionSceneUtil.Block(PrimitiveType.Cylinder, "PillarL",
                new Vector3(-9.5f, 8f, z), new Vector3(2.2f, 8f, 2.2f), pillarMat, _root);
            DimensionSceneUtil.Block(PrimitiveType.Cylinder, "PillarR",
                new Vector3(9.5f, 8f, z), new Vector3(2.2f, 8f, 2.2f), pillarMat, _root);
        }

        // Colored light shafts slanting down through the nave (BeamAdditive — the only
        // shader here that reads through fog and never goes black).
        Color[] shaft = { new Color(1f, 0.3f, 0.25f, 0.10f), new Color(1f, 0.8f, 0.35f, 0.10f),
                          new Color(0.35f, 0.55f, 1f, 0.10f), new Color(0.8f, 0.4f, 1f, 0.08f),
                          new Color(1f, 0.6f, 0.3f, 0.09f) };
        for (int i = 0; i < shaft.Length; i++)
        {
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "Shaft" + i;
            beam.transform.SetParent(_root, false);
            beam.transform.position = new Vector3(i % 2 == 0 ? -5f : 5f, 9f, 12f + i * 16f);
            beam.transform.rotation = Quaternion.Euler(18f, 0f, i % 2 == 0 ? 24f : -24f);
            beam.transform.localScale = new Vector3(2.4f, 10f, 2.4f);
            Object.Destroy(beam.GetComponent<Collider>());
            var mat = new Material(Shader.Find("Dimensions/BeamAdditive"));
            mat.SetColor("_Color", shaft[i]);
            beam.GetComponent<Renderer>().sharedMaterial = mat;
        }

        for (float z = 14f; z <= 62f; z += 6f)
        {
            var row = new PewRow { z = z, seed = Mathf.RoundToInt(z * 131f) + 77 };
            _rows.Add(row);
            BuildRow(row);
        }

        BuildAltar();
        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(110f, 2f, 0.05f), 400f, 1f);
        var organ = new GameObject("OrganFifth");
        organ.transform.SetParent(_root, false);
        organ.transform.position = new Vector3(0f, 6f, 85f);
        DimensionSceneUtil.LoopingAudio(organ, DimensionSceneUtil.ToneClip(165f, 2f, 0.04f), 300f, 1f);
    }

    void BuildAltar()
    {
        var stoneMat = DimensionSceneUtil.Mat(new Color(0.32f, 0.28f, 0.24f), 0.2f);
        var slabMat  = DimensionSceneUtil.Mat(new Color(0.10f, 0.08f, 0.07f), 0.3f);
        var glowMat  = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.85f, 0.5f), 2.2f);

        for (int i = 0; i < 3; i++)
            DimensionSceneUtil.Block(PrimitiveType.Cube, "Step" + i,
                new Vector3(0f, 0.25f + i * 0.5f, 76f + i * 1.6f), new Vector3(12f - i * 2f, 0.5f, 1.8f), stoneMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "AltarDais",
            new Vector3(0f, 1.25f, 84f), new Vector3(10f, 0.5f, 9f), stoneMat, _root);

        // Doorframe on the dais; the slab SINKS INTO THE FLOOR while unobserved.
        DimensionSceneUtil.Block(PrimitiveType.Cube, "DoorPostL",
            new Vector3(-1.3f, 3.5f, 87f), new Vector3(0.6f, 5f, 0.8f), stoneMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "DoorPostR",
            new Vector3(1.3f, 3.5f, 87f), new Vector3(0.6f, 5f, 0.8f), stoneMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "DoorLintel",
            new Vector3(0f, 6.2f, 87f), new Vector3(3.2f, 0.6f, 0.8f), stoneMat, _root);
        var halo = DimensionSceneUtil.Block(PrimitiveType.Cube, "DoorHalo",
            new Vector3(0f, 3.75f, 87.45f), new Vector3(2f, 4.5f, 0.05f), glowMat, _root);
        Object.Destroy(halo.GetComponent<Collider>());

        var slab = DimensionSceneUtil.Block(PrimitiveType.Cube, "DoorSlab",
            new Vector3(0f, 3.75f, 87f), new Vector3(2f, 4.5f, 0.4f), slabMat, _root);
        _doorSlab = slab.transform;

        DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 3f, 88.2f),
            new Vector3(1.8f, 3.4f, 1f), LevelPortal.PortalAction.EnterInterior, nextScene, _root);

        var lg = new GameObject("AltarLight");
        lg.transform.SetParent(_root, false);
        lg.transform.position = new Vector3(0f, 5f, 84f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = 20f; l.intensity = 1.8f;
        l.color = new Color(1f, 0.85f, 0.55f);
    }

    void BuildRow(PewRow row)
    {
        foreach (var s in row.segments) { s.SetActive(false); _pewPool.Push(s); }
        row.segments.Clear();
        // 2-4 pew segments at seeded lateral offsets — the gaps are the path, and they
        // move every time the row reshuffles.
        int count = 2 + Mathf.Abs(row.seed % 3);
        for (int i = 0; i < count; i++)
        {
            float x = Mathf.Lerp(-HallHalfWidth + 3.5f, HallHalfWidth - 3.5f,
                Rand01(row.seed, i * 2 + 1));
            GameObject pew = _pewPool.Count > 0 ? _pewPool.Pop() : NewPew();
            pew.SetActive(true);
            pew.transform.position = new Vector3(x, 0.55f, row.z);
            row.segments.Add(pew);
        }
    }

    GameObject NewPew()
    {
        var pew = DimensionSceneUtil.Block(PrimitiveType.Cube, "Pew",
            Vector3.zero, new Vector3(5.5f, 1.1f, 1.2f), _pewMat, _root);
        return pew;
    }

    static float Rand01(int seed, int salt) =>
        (Mathf.Abs(unchecked(seed * (int)2654435761 + salt * 40503)) % 10000) / 10000f;

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.16f, 0.13f, 0.12f),
                fog: new Color(0.10f, 0.08f, 0.07f), fogDensity: 0.018f,
                background: new Color(0.05f, 0.04f, 0.04f));
            _atmosApplied = true;
        }

        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        Vector3 playerPos = _player != null && _player.Rigidbody != null
            ? _player.Rigidbody.position
            : Vector3.zero;

        // Pew rows reshuffle when unseen (never the row you're standing beside).
        foreach (var row in _rows)
        {
            var b = new Bounds(new Vector3(0f, 1f, row.z), new Vector3(HallHalfWidth * 2f, 2.4f, 1.6f));
            row.tracker.Tick(b, out bool justLost, float.PositiveInfinity);
            if (justLost && Mathf.Abs(playerPos.z - row.z) > 2.5f)
            {
                row.seed = unchecked(row.seed * 7919 + 12345);
                BuildRow(row);
                row.tracker.Reset();
            }
        }

        // The altar door: observed → shut; unobserved → it sinks open. Cross it backwards.
        var db = new Bounds(new Vector3(0f, 3.75f, 87f), new Vector3(3.4f, 5.2f, 1.6f));
        bool doorObserved = _doorTracker.Tick(db, out _, float.PositiveInfinity);
        _doorOpenT = Mathf.MoveTowards(_doorOpenT, doorObserved ? 0f : 1f, Time.deltaTime / doorSlideSeconds);
        _doorSlab.position = new Vector3(0f, 3.75f - _doorOpenT * 4.6f, 87f);
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Altar door")]
    [Tooltip("Seconds for the slab to fully sink (or rise back).")]
    public float doorSlideSeconds = 1.1f;

    [Header("Exit")]
    [Tooltip("Scene behind the altar door.")]
    public string nextScene = "D16_NeonGrid";
}
