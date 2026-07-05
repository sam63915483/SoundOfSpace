using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// D26 "The Ferry": a long stone pier over black water, ending at a lantern-lit
/// boat. Pier segments you have passed CRUMBLE — gone for good the moment they're
/// fully off-screen behind you. One-way pressure: no backtracking, and slipping
/// into the water washes you to the nearest surviving segment. The boat's bell
/// tolls louder as you face it (gaze-audio).
/// </summary>
public class FerryController : MonoBehaviour
{
    class Segment
    {
        public GameObject go;
        public float z;
        public bool crumbled;
        public readonly ObservationTracker tracker = new ObservationTracker(0.2f);
    }

    readonly List<Segment> _segments = new List<Segment>();
    Transform _boat;
    AudioSource _bell;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    void Awake()
    {
        var root = transform;
        var stoneMat = DimensionSceneUtil.Mat(new Color(0.30f, 0.30f, 0.33f), 0.15f);
        var waterMat = DimensionSceneUtil.Mat(new Color(0.01f, 0.012f, 0.02f), 0.9f);
        var woodMat  = DimensionSceneUtil.Mat(new Color(0.22f, 0.15f, 0.09f), 0.15f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "BlackWater",
            new Vector3(0f, -3f, 60f), new Vector3(1200f, 1f, 1200f), waterMat, root);

        // Start platform.
        DimensionSceneUtil.Block(PrimitiveType.Cube, "StartPlatform",
            new Vector3(0f, -0.4f, -2f), new Vector3(8f, 0.8f, 8f), stoneMat, root);

        // The pier: segments marching out to the boat.
        for (int i = 0; i < segmentCount; i++)
        {
            float z = 3f + i * segmentLength;
            var seg = DimensionSceneUtil.Block(PrimitiveType.Cube, "Pier" + i,
                new Vector3(0f, -0.4f, z), new Vector3(3.2f, 0.8f, segmentLength * 0.94f), stoneMat, root);
            // Edge posts every other segment for silhouette.
            if (i % 2 == 0)
            {
                var post = DimensionSceneUtil.Block(PrimitiveType.Cube, "Post",
                    Vector3.zero, new Vector3(0.25f, 1.6f, 0.25f), stoneMat, seg.transform);
                post.transform.localPosition = new Vector3(0.45f, 1.4f, 0f);
            }
            _segments.Add(new Segment { go = seg, z = z });
        }

        BuildBoat(new Vector3(0f, 0f, 3f + segmentCount * segmentLength + 6f));

        // Slip catcher — washes you to the nearest SURVIVING segment (handled here,
        // not the generic respawn volume, because the safe spot moves as the pier dies).
        var wash = new GameObject("WashVolume");
        wash.transform.SetParent(root, false);
        wash.transform.position = new Vector3(0f, -2.4f, 60f);
        var wb = wash.AddComponent<BoxCollider>();
        wb.isTrigger = true; wb.size = new Vector3(400f, 1.6f, 400f);
        wash.AddComponent<FerryWashTrigger>().owner = this;

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(43f, 3f, 0.06f), 600f, 1f);
    }

    void BuildBoat(Vector3 pos)
    {
        var woodMat = DimensionSceneUtil.Mat(new Color(0.24f, 0.16f, 0.10f), 0.2f);
        var boat = new GameObject("Boat");
        boat.transform.SetParent(transform, false);
        _boat = boat.transform;
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Hull", new Vector3(0f, 0.2f, 0f), new Vector3(4.5f, 1.2f, 9f), woodMat, boat.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Bow", new Vector3(0f, 0.35f, 5.2f), new Vector3(2.2f, 0.9f, 2.2f), woodMat, boat.transform)
            .transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Deck", new Vector3(0f, 0.85f, 0f), new Vector3(4f, 0.15f, 8.4f), woodMat, boat.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Mast", new Vector3(0f, 3.4f, -1f), new Vector3(0.3f, 2.4f, 0.3f), woodMat, boat.transform);

        var lanternMat = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.7f, 0.35f), 3f);
        var lantern = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Lantern",
            new Vector3(0f, 5.4f, -1f), Vector3.one * 0.5f, lanternMat, boat.transform);
        Object.Destroy(lantern.GetComponent<Collider>());
        var lg = new GameObject("LanternLight");
        lg.transform.SetParent(boat.transform, false);
        lg.transform.localPosition = new Vector3(0f, 5.2f, -1f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = 24f; l.intensity = 2.2f;
        l.color = new Color(1f, 0.7f, 0.35f);
        lg.AddComponent<FlickerLight>();

        _bell = DimensionSceneUtil.LoopingAudio(boat, BellClip(), 320f, 1f);
        DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1.6f, 0f),
            new Vector3(3.6f, 1.8f, 7f), LevelPortal.PortalAction.EnterInterior, nextScene, boat.transform);
        boat.transform.position = pos;
    }

    // A slow bronze bell strike with a long decaying tail.
    static AudioClip BellClip()
    {
        int rate = 44100;
        float seconds = 4.2f;
        int samples = (int)(rate * seconds);
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            float env = Mathf.Exp(-t * 1.1f);
            data[i] = (Mathf.Sin(2f * Mathf.PI * 196f * t) * 0.6f
                     + Mathf.Sin(2f * Mathf.PI * 392.5f * t) * 0.3f
                     + Mathf.Sin(2f * Mathf.PI * 523.7f * t) * 0.15f) * env * 0.7f;
        }
        var clip = AudioClip.Create("bell", samples, 1, rate, false);
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
                ambient: new Color(0.09f, 0.10f, 0.13f),
                fog: new Color(0.02f, 0.025f, 0.04f), fogDensity: 0.011f,
                background: new Color(0.008f, 0.01f, 0.02f));
            _atmosApplied = true;
        }

        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        float playerZ = _player != null && _player.Rigidbody != null
            ? _player.Rigidbody.position.z : cam.transform.position.z;

        // Passed segments crumble once fully off-screen — permanently.
        foreach (var s in _segments)
        {
            if (s.crumbled) continue;
            var b = new Bounds(new Vector3(0f, -0.4f, s.z), new Vector3(3.2f, 1f, segmentLength));
            bool observed = s.tracker.Tick(b, out _, float.PositiveInfinity);
            if (!observed && s.z < playerZ - crumbleLag)
            {
                s.crumbled = true;
                s.go.SetActive(false);
            }
        }

        if (_bell != null)
        {
            Vector3 to = _boat.position - cam.transform.position;
            float align = Vector3.Dot(cam.transform.forward, to.normalized);
            _bell.volume = Mathf.Lerp(0.08f, 1f, Mathf.InverseLerp(0.1f, 0.95f, align));
        }
    }

    /// <summary>Wash-up: the nearest surviving segment ahead of (or under) the player.</summary>
    public void WashToNearestSegment()
    {
        if (_player == null || _player.Rigidbody == null) return;
        float playerZ = _player.Rigidbody.position.z;
        Segment best = null;
        float bestDist = float.MaxValue;
        foreach (var s in _segments)
        {
            if (s.crumbled) continue;
            float d = Mathf.Abs(s.z - playerZ);
            if (d < bestDist) { bestDist = d; best = s; }
        }
        Vector3 target = best != null ? new Vector3(0f, 1.5f, best.z) : new Vector3(0f, 1.5f, -2f);
        _player.Rigidbody.position = target;
        _player.Rigidbody.velocity = Vector3.zero;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Pier")]
    [Tooltip("Number of pier segments between the start platform and the boat.")]
    public int segmentCount = 26;
    [Tooltip("Length of each segment (metres).")]
    public float segmentLength = 4f;
    [Tooltip("A segment must be this far behind you before it can crumble.")]
    public float crumbleLag = 7f;

    [Header("Exit")]
    [Tooltip("Scene the boat sails to.")]
    public string nextScene = "D27_InvertedRain";
}

/// <summary>Water trigger that hands the player to the controller's moving-respawn logic.</summary>
public class FerryWashTrigger : MonoBehaviour
{
    [HideInInspector] public FerryController owner;

    void OnTriggerEnter(Collider other)
    {
        if (owner == null) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;
        owner.WashToNearestSegment();
    }
}
