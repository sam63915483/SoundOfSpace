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
    AudioSource _choir;                   // swells while the door stands open — you
    AudioSource _choirFifth;              // HEAR it open with your back turned
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    const float HallHalfWidth = 14f;

    void Awake()
    {
        _root = transform;
        var floorMat = DimensionSceneUtil.Mat(new Color(0.16f, 0.14f, 0.13f), 0.25f);
        var wallMat  = DimensionSceneUtil.TexMat("d15_stone", Color.white, new Vector2(18f, 3f), 0.1f);
        var pillarMat = DimensionSceneUtil.TexMat("d15_stone", new Color(0.9f, 0.85f, 0.8f), new Vector2(2f, 3f), 0.15f);
        _pewMat = DimensionSceneUtil.TexMat("d15_wood", Color.white, new Vector2(2f, 1f), 0.2f);

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
            for (int side = -1; side <= 1; side += 2)
            {
                DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Pillar",
                    new Vector3(side * 9.5f, 8f, z), new Vector3(2.2f, 8f, 2.2f), pillarMat, _root);
                var capital = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Capital",
                    new Vector3(side * 9.5f, 16.4f, z), new Vector3(3f, 0.5f, 3f), pillarMat, _root);
                Object.Destroy(capital.GetComponent<Collider>());
                var pedestal = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Pedestal",
                    new Vector3(side * 9.5f, 0.35f, z), new Vector3(2.9f, 0.35f, 2.9f), pillarMat, _root);
                Object.Destroy(pedestal.GetComponent<Collider>());
            }

        // Red carpet runner down the nave to the altar steps.
        var carpet = DimensionSceneUtil.Block(PrimitiveType.Cube, "Carpet",
            new Vector3(0f, 0.03f, 38f), new Vector3(3.2f, 0.06f, 78f),
            DimensionSceneUtil.Mat(new Color(0.42f, 0.08f, 0.08f), 0.05f), _root);
        Object.Destroy(carpet.GetComponent<Collider>());

        BuildWindows();

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
        BuildNaveDressing();
        DimensionSceneUtil.AmbienceLoop2D(gameObject, "amb_d15", 110f, 0.05f, 0.3f);
        DimensionSceneUtil.AmbienceLoop2D(gameObject, "mus_d15_organ", 55f, 0.02f, 0.25f);
        var organ = new GameObject("OrganFifth");
        organ.transform.SetParent(_root, false);
        organ.transform.position = new Vector3(0f, 6f, 85f);
        DimensionSceneUtil.LoopingAudio(organ, DimensionSceneUtil.ToneClip(165f, 2f, 0.04f), 300f, 1f);

        // The door's voice: a two-tone choir chord that only sounds while the slab
        // is open. You cross the door backwards — this is how you know it worked.
        var choirGo = new GameObject("DoorChoir");
        choirGo.transform.SetParent(_root, false);
        choirGo.transform.position = new Vector3(0f, 4f, 87f);
        _choir = DimensionSceneUtil.LoopingAudio(choirGo, DimensionSceneUtil.ToneClip(220f, 2f, 0.4f), 260f, 0f);
        _choirFifth = DimensionSceneUtil.LoopingAudio(choirGo, DimensionSceneUtil.ToneClip(277f, 2f, 0.3f), 260f, 0f);
    }

    // Tall stained-glass windows down both walls: 3-pane columns in jewel colors,
    // glowing through the gloom (emissive — no real light needed).
    void BuildWindows()
    {
        Color[] jewels =
        {
            new Color(0.9f, 0.2f, 0.2f), new Color(0.95f, 0.7f, 0.2f), new Color(0.25f, 0.4f, 0.95f),
            new Color(0.5f, 0.2f, 0.8f), new Color(0.2f, 0.75f, 0.5f),
        };
        int wi = 0;
        // One tall figured pane per window slot — generated stained glass reads as
        // saints-in-glass through the gloom; falls back to a warm jewel pane.
        var glass1 = DimensionSceneUtil.EmissiveTexMat("d15_glass1", new Color(1f, 0.9f, 0.75f), 1.2f);
        var glass2 = DimensionSceneUtil.EmissiveTexMat("d15_glass2", new Color(0.85f, 0.9f, 1f), 1.2f);
        for (float z = 10f; z <= 80f; z += 14f, wi++)
            for (int side = -1; side <= 1; side += 2)
            {
                var glass = DimensionSceneUtil.Block(PrimitiveType.Cube, "Window",
                    new Vector3(side * (HallHalfWidth - 0.05f), 10.6f, z),
                    new Vector3(0.15f, 9.1f, 1.7f), (wi + side) % 2 == 0 ? glass1 : glass2, _root);
                Object.Destroy(glass.GetComponent<Collider>());
                // Stone mullions framing the pane.
                for (int mz = -1; mz <= 1; mz += 2)
                {
                    var mullion = DimensionSceneUtil.Block(PrimitiveType.Cube, "Mullion",
                        new Vector3(side * (HallHalfWidth - 0.02f), 10.6f, z + mz * 0.95f),
                        new Vector3(0.2f, 9.3f, 0.22f), _pewMat, _root);
                    Object.Destroy(mullion.GetComponent<Collider>());
                }
                // Arched cap pane.
                var cap = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "WindowCap",
                    new Vector3(side * (HallHalfWidth - 0.05f), 16.9f, z), new Vector3(1.7f, 0.08f, 1.7f),
                    DimensionSceneUtil.EmissiveMat(jewels[wi % jewels.Length], 1.4f), _root);
                cap.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                Object.Destroy(cap.GetComponent<Collider>());
            }

        // Rose window above the altar: a ring of eight petals around a golden heart.
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.PI / 4f;
            var petal = DimensionSceneUtil.Block(PrimitiveType.Cube, "RosePetal",
                new Vector3(Mathf.Cos(a) * 2.4f, 12.5f + Mathf.Sin(a) * 2.4f, 97.9f),
                new Vector3(1.5f, 1.5f, 0.15f),
                DimensionSceneUtil.EmissiveMat(jewels[k % jewels.Length], 1.6f), _root);
            petal.transform.rotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 45f);
            Object.Destroy(petal.GetComponent<Collider>());
        }
        var heart = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "RoseHeart",
            new Vector3(0f, 12.5f, 97.9f), new Vector3(2f, 0.08f, 2f),
            DimensionSceneUtil.EmissiveMat(new Color(1f, 0.85f, 0.4f), 2f), _root);
        heart.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        Object.Destroy(heart.GetComponent<Collider>());
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

        // Candle stands flanking the dais.
        for (int side = -1; side <= 1; side += 2)
        {
            var stand = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "CandleStand",
                new Vector3(side * 3.6f, 2f, 83f), new Vector3(0.16f, 0.85f, 0.16f),
                DimensionSceneUtil.Mat(new Color(0.5f, 0.4f, 0.18f), 0.6f), _root);
            var flame = DimensionSceneUtil.Block(PrimitiveType.Sphere, "StandFlame",
                new Vector3(side * 3.6f, 3.1f, 83f), new Vector3(0.16f, 0.28f, 0.16f),
                DimensionSceneUtil.EmissiveMat(new Color(1f, 0.65f, 0.25f), 2.8f), _root);
            Object.Destroy(flame.GetComponent<Collider>());
            var cl = new GameObject("CandleLight");
            cl.transform.SetParent(_root, false);
            cl.transform.position = new Vector3(side * 3.6f, 3.3f, 83f);
            var cll = cl.AddComponent<Light>();
            cll.type = LightType.Point; cll.range = 9f; cll.intensity = 1.2f;
            cll.color = new Color(1f, 0.65f, 0.3f);
            cl.AddComponent<FlickerLight>();
        }

        // The hint, carved where you'll read it before the steps.
        var plaque = DimensionSceneUtil.Block(PrimitiveType.Cube, "Plaque",
            new Vector3(2.8f, 1.5f, 74.5f), new Vector3(0.12f, 1f, 1.6f), slabMat, _root);
        var textGo = new GameObject("PlaqueText");
        textGo.transform.SetParent(plaque.transform, false);
        textGo.transform.localPosition = new Vector3(-0.51f, 0f, 0f);
        textGo.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        textGo.transform.localScale = new Vector3(0.030f, 0.048f, 0.5f);
        var tmp = textGo.AddComponent<TMPro.TextMeshPro>();
        tmp.text = "IT OPENS ONLY\nFOR THE UNSEEING";
        tmp.fontSize = 44f;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = new Color(0.95f, 0.88f, 0.7f);
        tmp.GetComponent<RectTransform>().sizeDelta = new Vector2(30f, 16f);
    }

    // Hymn books and candlesticks left in the aisles — never at the same pew twice.
    // Two shuffle zones (front/back half of the nave) so whichever half is at your
    // back re-deals itself while you face the other way.
    void BuildNaveDressing()
    {
        var booksMat = DimensionSceneUtil.TexMat("d5_books", new Color(0.45f, 0.2f, 0.15f), new Vector2(1f, 1f), 0.15f);
        var brassMat = DimensionSceneUtil.Mat(new Color(0.5f, 0.4f, 0.18f), 0.6f);
        var flameMat = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.65f, 0.25f), 2.8f);

        for (int half = 0; half < 2; half++)
        {
            float zCenter = half == 0 ? 26f : 50f;
            var set = PropShuffleSet.Create(half == 0 ? "NaveDressingBack" : "NaveDressingFront",
                _root, new Bounds(new Vector3(0f, 1f, zCenter), new Vector3(HallHalfWidth * 2f - 4f, 3f, 24f)),
                "sfx_wood_creak");
            for (int i = 0; i < 6; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * Random.Range(2.4f, HallHalfWidth - 4f);
                float z = zCenter - 10f + i * 4f;
                set.AddAnchor(new Vector3(x, 0.02f, z), Random.value * 360f);
            }
            set.AddProp(DimensionPropKit.BookStack(_root, booksMat, 3));
            set.AddProp(DimensionPropKit.BookStack(_root, booksMat, 5));
            set.AddProp(DimensionPropKit.Candlestick(_root, brassMat, flameMat));
        }
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
            pew.transform.position = new Vector3(x, 0.275f, row.z);
            row.segments.Add(pew);
        }
    }

    GameObject NewPew()
    {
        // Bench seat + backrest + end caps — reads as church furniture, not a crate.
        var pew = DimensionSceneUtil.Block(PrimitiveType.Cube, "Pew",
            Vector3.zero, new Vector3(5.5f, 0.55f, 1.1f), _pewMat, _root);
        var back = DimensionSceneUtil.Block(PrimitiveType.Cube, "PewBack",
            Vector3.zero, Vector3.one, _pewMat, pew.transform);
        back.transform.localPosition = new Vector3(0f, 0.75f, -0.42f);
        back.transform.localScale = new Vector3(1f, 1.6f, 0.14f);
        for (int side = -1; side <= 1; side += 2)
        {
            var cap = DimensionSceneUtil.Block(PrimitiveType.Cube, "PewEnd",
                Vector3.zero, Vector3.one, _pewMat, pew.transform);
            cap.transform.localPosition = new Vector3(side * 0.5f, 0.45f, 0f);
            cap.transform.localScale = new Vector3(0.03f, 1.9f, 1.05f);
            Object.Destroy(cap.GetComponent<Collider>());
        }
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

        // The choir sings exactly as wide as the door stands open — walk backwards
        // toward the voices.
        if (_choir != null)
        {
            _choir.volume = _doorOpenT * 0.5f;
            _choirFifth.volume = _doorOpenT * 0.35f;
        }
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Altar door")]
    [Tooltip("Seconds for the slab to fully sink (or rise back).")]
    public float doorSlideSeconds = 1.1f;

    [Header("Exit")]
    [Tooltip("Scene behind the altar door.")]
    public string nextScene = "D16_NeonGrid";
}
