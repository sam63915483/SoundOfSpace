using UnityEngine;

/// <summary>
/// D10 "Salt Flats": a blinding white plain under a black sky. Hairline cracks in the
/// salt gape open wherever you are NOT looking (the inverse rule) — including under
/// your own feet the moment you stare at the horizon. Stepping into an open crack
/// drops you back at the start. The exit is a black pyramid on the horizon.
/// </summary>
public class SaltFlatsController : MonoBehaviour
{
    class Crack
    {
        public Transform tf;
        public Renderer rend;
        public BoxCollider trigger;
        public float openness;               // 0 hairline .. 1 gaping
        public readonly ObservationTracker tracker = new ObservationTracker();
    }

    Crack[] _cracks;
    Transform _root;
    Vector3 _spawnPoint = new Vector3(0f, 1.5f, 0f);
    Vector3 _pyramidPos;
    float _swallowDebounceUntil;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        var saltMat = DimensionSceneUtil.Mat(new Color(0.96f, 0.96f, 0.94f), 0.25f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Salt",
            new Vector3(0f, -0.5f, 0f), new Vector3(1600f, 1f, 1600f), saltMat, _root);
        DimensionSceneUtil.CreateDirectionalLight(new Color(1f, 1f, 0.97f), 1.5f, new Vector3(60f, -30f, 0f), true);

        float a = Random.value * Mathf.PI * 2f;
        _pyramidPos = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * pyramidDistance;
        BuildPyramid();

        var crackMat = DimensionSceneUtil.Mat(new Color(0.02f, 0.02f, 0.03f), 0f);
        _cracks = new Crack[crackCount];
        for (int i = 0; i < crackCount; i++)
        {
            var go = new GameObject("Crack" + i);
            go.transform.SetParent(_root, false);
            var visual = DimensionSceneUtil.Block(PrimitiveType.Cube, "Gap",
                Vector3.zero, Vector3.one, crackMat, go.transform);
            visual.transform.localPosition = Vector3.zero;
            Destroy(visual.GetComponent<Collider>());
            var trigger = go.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(1f, 30f, 1f);          // deep shaft in local space
            trigger.center = new Vector3(0f, -12f, 0f);       // mostly below the surface
            go.AddComponent<SaltCrackTrigger>().owner = this;
            _cracks[i] = new Crack
            {
                tf = go.transform,
                rend = visual.GetComponent<Renderer>(),
                trigger = trigger,
            };
            PlaceCrack(_cracks[i], Vector3.zero, initial: true);
        }

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(95f, 3f, 0.04f), 700f, 1f);
    }

    void BuildPyramid()
    {
        var blackMat = DimensionSceneUtil.Mat(new Color(0.03f, 0.03f, 0.04f), 0.5f);
        var pyramid = new GameObject("Pyramid");
        pyramid.transform.SetParent(_root, false);
        float[] widths = { 44f, 36f, 28f, 20f, 12f, 5f };
        for (int i = 0; i < widths.Length; i++)
            DimensionSceneUtil.Block(PrimitiveType.Cube, "Tier" + i,
                new Vector3(0f, 3f + i * 6f, 0f), new Vector3(widths[i], 6f, widths[i]), blackMat, pyramid.transform);

        // A thin ember-red doorway slit on the side facing the spawn — the way in.
        var doorMat = DimensionSceneUtil.EmissiveMat(new Color(0.9f, 0.25f, 0.1f), 2.5f);
        var slit = DimensionSceneUtil.Block(PrimitiveType.Cube, "DoorSlit",
            new Vector3(0f, 2.2f, -22.3f), new Vector3(1.6f, 4.4f, 0.2f), doorMat, pyramid.transform);
        Destroy(slit.GetComponent<Collider>());
        DimensionSceneUtil.CreatePortal("ToNext", Vector3.zero,
            new Vector3(2.4f, 4.4f, 1.6f), LevelPortal.PortalAction.EnterInterior, nextScene, pyramid.transform)
            .transform.localPosition = new Vector3(0f, 2.2f, -22.6f);

        // Face the door toward the origin, then plant the pyramid out on the horizon.
        pyramid.transform.SetPositionAndRotation(_pyramidPos,
            Quaternion.LookRotation(-_pyramidPos.normalized, Vector3.up) * Quaternion.Euler(0f, 180f, 0f));

        var drone = new GameObject("PyramidDrone");
        drone.transform.SetParent(pyramid.transform, false);
        drone.transform.localPosition = new Vector3(0f, 4f, -20f);
        DimensionSceneUtil.LoopingAudio(drone, DimensionSceneUtil.ToneClip(52f, 2f, 0.5f), 320f, 1f);
    }

    void Update()
    {
        var cam = ObserverState.Cam;
        if (cam == null) return;
        if (!_atmosApplied)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.75f, 0.75f, 0.72f),
                fog: new Color(0.9f, 0.9f, 0.88f), fogDensity: 0.0022f,
                background: new Color(0.01f, 0.01f, 0.015f));   // black sky over white land
            _atmosApplied = true;
        }

        Vector3 p = cam.transform.position;
        foreach (var c in _cracks)
        {
            // Far-drifted cracks quietly re-seed near the player (never while visible).
            Vector3 flat = c.tf.position - p; flat.y = 0f;
            if (flat.magnitude > crackMaxDistance && c.openness < 0.05f
                && !ObserverState.IsObserved(CrackBounds(c)))
            {
                PlaceCrack(c, p, initial: false);
                continue;
            }

            // The inverse rule: watched cracks seal shut, unwatched cracks yawn open.
            bool observed = c.tracker.Tick(CrackBounds(c), out _, float.PositiveInfinity);
            c.openness = Mathf.MoveTowards(c.openness, observed ? 0f : 1f,
                Time.deltaTime / (observed ? sealSeconds : openSeconds));
            var s = c.tf.localScale;
            c.tf.localScale = new Vector3(Mathf.Lerp(0.06f, crackWidth, c.openness), s.y, s.z);
            c.trigger.enabled = c.openness > 0.65f;
        }
    }

    Bounds CrackBounds(Crack c) =>
        new Bounds(c.tf.position, new Vector3(crackWidth + 1f, 0.4f, c.tf.localScale.z + 1f));

    void PlaceCrack(Crack c, Vector3 aroundPos, bool initial)
    {
        Vector3 best = c.tf.position;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float d = initial ? Random.Range(8f, 55f) : Random.Range(10f, 40f);
            Vector3 ctr = initial ? Vector3.zero : aroundPos;
            best = new Vector3(ctr.x + Mathf.Cos(a) * d, 0.03f, ctr.z + Mathf.Sin(a) * d);
            if ((best - _pyramidPos).magnitude < 35f) continue;          // keep the doorstep safe
            if (initial && best.magnitude < 10f) continue;               // and the spawn
            if (initial || !ObserverState.IsObserved(new Bounds(best, new Vector3(4f, 2f, 14f))))
                break;
        }
        c.tf.SetPositionAndRotation(best, Quaternion.Euler(0f, Random.value * 360f, 0f));
        c.tf.localScale = new Vector3(0.06f, 0.1f, Random.Range(7f, 14f));
        c.openness = 0f;
        c.trigger.enabled = false;
        c.tracker.Reset();
    }

    /// <summary>Crack swallow: back to the start, velocity zeroed. No damage, just distance.</summary>
    public void SwallowPlayer()
    {
        if (Time.time < _swallowDebounceUntil) return;
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null) return;
        _swallowDebounceUntil = Time.time + 1.5f;
        _player.Rigidbody.position = _spawnPoint;
        _player.Rigidbody.velocity = Vector3.zero;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Cracks")]
    [Tooltip("How many cracks stalk the plain around you.")]
    public int crackCount = 14;
    [Tooltip("Full gape width (metres) of an unwatched crack.")]
    public float crackWidth = 2.6f;
    [Tooltip("Seconds for an unwatched crack to yawn fully open.")]
    public float openSeconds = 1.4f;
    [Tooltip("Seconds for a watched crack to seal shut.")]
    public float sealSeconds = 0.5f;
    [Tooltip("Closed cracks further than this re-seed near the player.")]
    public float crackMaxDistance = 70f;

    [Header("Exit")]
    [Tooltip("Distance from spawn to the black pyramid.")]
    public float pyramidDistance = 240f;
    [Tooltip("Scene the pyramid doorway leads to.")]
    public string nextScene = "D11_Shelves";
}

/// <summary>Deep-shaft trigger inside each crack — entering one open counts as falling in.</summary>
public class SaltCrackTrigger : MonoBehaviour
{
    [HideInInspector] public SaltFlatsController owner;

    void OnTriggerEnter(Collider other) { Swallow(other); }
    void OnTriggerStay(Collider other) { Swallow(other); }

    void Swallow(Collider other)
    {
        if (owner == null) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;
        owner.SwallowPlayer();
    }
}
