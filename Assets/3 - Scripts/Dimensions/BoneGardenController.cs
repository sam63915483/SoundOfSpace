using UnityEngine;

/// <summary>
/// D19 "Bone Garden": pale spires and ribcage arches on dark soil. One arch is the
/// way out — it relocates whenever it leaves your view, and a low heartbeat swells
/// as you face it (gaze-audio). Walking through any other arch scatters you across
/// the garden. Trust the pulse.
/// </summary>
public class BoneGardenController : MonoBehaviour
{
    Transform _root;
    Transform _trueArch;
    readonly ObservationTracker _trueTracker = new ObservationTracker();
    AudioSource _heartbeat;
    Material _boneMat;
    float _scatterDebounceUntil;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        _boneMat = DimensionSceneUtil.Mat(new Color(0.82f, 0.79f, 0.70f), 0.15f);
        var soilMat = DimensionSceneUtil.Mat(new Color(0.10f, 0.08f, 0.07f), 0.05f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Soil",
            new Vector3(0f, -0.5f, 0f), new Vector3(2000f, 1f, 2000f), soilMat, _root);
        DimensionSceneUtil.CreateDirectionalLight(new Color(0.7f, 0.65f, 0.6f), 0.5f, new Vector3(28f, -45f, 0f), true);

        // Static bone scenery: spires and half-buried ribs, seeded.
        Random.State prev = Random.state;
        Random.InitState(1919);
        for (int i = 0; i < 26; i++)
        {
            float a = Random.value * Mathf.PI * 2f, d = Random.Range(14f, 120f);
            BuildSpire(new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d), Random.Range(2.5f, 7f));
        }
        for (int i = 0; i < 18; i++)
        {
            float a = Random.value * Mathf.PI * 2f, d = Random.Range(10f, 110f);
            BuildRib(new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d), Random.Range(0f, 360f));
        }

        // Wrong arches: fixed, punishing.
        for (int i = 0; i < wrongArchCount; i++)
        {
            float a = (i / (float)wrongArchCount) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
            float d = Random.Range(28f, 85f);
            var arch = BuildArch(false);
            arch.transform.SetPositionAndRotation(
                new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d),
                Quaternion.Euler(0f, Random.value * 360f, 0f));
        }
        Random.state = prev;

        // The true arch relocates unseen and carries the heartbeat + portal.
        var trueArch = BuildArch(true);
        _trueArch = trueArch.transform;
        PlaceTrueArch(Vector3.zero, initial: true);

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(60f, 3f, 0.05f), 500f, 1f);
    }

    void BuildSpire(Vector3 pos, float h)
    {
        var spire = new GameObject("Spire");
        spire.transform.SetParent(_root, false);
        int tiers = 4;
        for (int i = 0; i < tiers; i++)
        {
            float t = i / (float)tiers;
            DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Seg",
                new Vector3(0f, h * t + h / tiers * 0.5f, 0f),
                new Vector3(Mathf.Lerp(1.1f, 0.25f, t), h / tiers * 0.55f, Mathf.Lerp(1.1f, 0.25f, t)),
                _boneMat, spire.transform);
        }
        spire.transform.SetPositionAndRotation(pos, Quaternion.Euler(Random.Range(-7f, 7f), 0f, Random.Range(-7f, 7f)));
    }

    void BuildRib(Vector3 pos, float yaw)
    {
        var rib = new GameObject("Rib");
        rib.transform.SetParent(_root, false);
        // A half-buried curve approximated by three leaning segments per side.
        for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 3; i++)
            {
                var seg = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "RibSeg",
                    new Vector3(side * (1.8f - i * 0.45f), 0.8f + i * 1.1f, 0f),
                    new Vector3(0.28f, 0.75f, 0.28f), _boneMat, rib.transform);
                seg.transform.localRotation = Quaternion.Euler(0f, 0f, side * (18f + i * 22f));
            }
        rib.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
    }

    GameObject BuildArch(bool isTrue)
    {
        var arch = new GameObject(isTrue ? "Arch_TRUE" : "Arch");
        arch.transform.SetParent(_root, false);
        for (int side = -1; side <= 1; side += 2)
        {
            DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Post",
                new Vector3(side * 1.6f, 1.6f, 0f), new Vector3(0.55f, 1.6f, 0.55f), _boneMat, arch.transform);
            DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Bend",
                new Vector3(side * 1.15f, 3.25f, 0f), new Vector3(0.45f, 0.55f, 0.45f), _boneMat, arch.transform)
                .transform.localRotation = Quaternion.Euler(0f, 0f, side * 38f);
        }
        DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Key",
            new Vector3(0f, 3.75f, 0f), new Vector3(0.4f, 1.15f, 0.4f), _boneMat, arch.transform)
            .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

        if (isTrue)
        {
            _heartbeat = DimensionSceneUtil.LoopingAudio(arch, HeartbeatClip(), 260f, 1f);
            DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1.6f, 0f),
                new Vector3(2.2f, 3f, 0.8f), LevelPortal.PortalAction.EnterInterior, nextScene, arch.transform);
        }
        else
        {
            var trig = new GameObject("ScatterTrigger");
            trig.transform.SetParent(arch.transform, false);
            trig.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var box = trig.AddComponent<BoxCollider>();
            box.isTrigger = true; box.size = new Vector3(2.2f, 3f, 0.8f);
            trig.AddComponent<BoneScatterTrigger>().owner = this;
        }
        return arch;
    }

    // Two low thumps per loop — lub-dub.
    static AudioClip HeartbeatClip()
    {
        int rate = 44100;
        float seconds = 1.1f;
        int samples = (int)(rate * seconds);
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            float e1 = Mathf.Exp(-Mathf.Max(0f, t - 0.00f) * 26f) * (t >= 0f ? 1f : 0f);
            float e2 = Mathf.Exp(-Mathf.Max(0f, t - 0.28f) * 30f) * (t >= 0.28f ? 0.75f : 0f);
            data[i] = Mathf.Sin(2f * Mathf.PI * 52f * t) * (e1 + e2) * 0.9f;
        }
        var clip = AudioClip.Create("heartbeat", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void PlaceTrueArch(Vector3 aroundPos, bool initial)
    {
        Vector3 best = _trueArch.position;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float d = initial ? Random.Range(50f, 90f) : Random.Range(relocateMinDistance, relocateMaxDistance);
            Vector3 c = initial ? Vector3.zero : aroundPos;
            best = new Vector3(c.x + Mathf.Cos(a) * d, 0f, c.z + Mathf.Sin(a) * d);
            if (initial || !ObserverState.IsObserved(new Bounds(best + Vector3.up * 2f, new Vector3(5f, 5f, 5f))))
                break;
        }
        _trueArch.position = best;
        Vector3 face = aroundPos - best; face.y = 0f;
        if (face.sqrMagnitude > 0.01f) _trueArch.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
        _trueTracker.Reset();
    }

    void Update()
    {
        var cam = ObserverState.Cam;
        if (cam == null) return;
        if (!_atmosApplied)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.20f, 0.18f, 0.17f),
                fog: new Color(0.16f, 0.13f, 0.13f), fogDensity: 0.020f,
                background: new Color(0.10f, 0.08f, 0.09f));
            _atmosApplied = true;
        }

        var b = new Bounds(_trueArch.position + Vector3.up * 2f, new Vector3(4.5f, 4.5f, 4.5f));
        _trueTracker.Tick(b, out bool justLost, float.PositiveInfinity);
        if (justLost) PlaceTrueArch(cam.transform.position, initial: false);

        if (_heartbeat != null)
        {
            Vector3 to = _trueArch.position - cam.transform.position;
            float align = Vector3.Dot(cam.transform.forward, to.normalized);
            _heartbeat.volume = Mathf.Lerp(0.05f, 1f, Mathf.InverseLerp(0.15f, 0.95f, align));
        }
    }

    /// <summary>Wrong-arch punishment: scattered to a random distant spot. No damage.</summary>
    public void ScatterPlayer()
    {
        if (Time.time < _scatterDebounceUntil) return;
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null) return;
        _scatterDebounceUntil = Time.time + 1.5f;
        float a = Random.value * Mathf.PI * 2f;
        float d = Random.Range(60f, 110f);
        _player.Rigidbody.position = new Vector3(Mathf.Cos(a) * d, 1.5f, Mathf.Sin(a) * d);
        _player.Rigidbody.velocity = Vector3.zero;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Arches")]
    [Tooltip("How many decoy arches dot the garden.")]
    public int wrongArchCount = 5;
    [Tooltip("Min relocation distance of the true arch from the player.")]
    public float relocateMinDistance = 35f;
    [Tooltip("Max relocation distance of the true arch from the player.")]
    public float relocateMaxDistance = 80f;

    [Header("Exit")]
    [Tooltip("Scene the true arch leads to.")]
    public string nextScene = "D20_CloudShelf";
}

/// <summary>Walk-through trigger inside every decoy arch.</summary>
public class BoneScatterTrigger : MonoBehaviour
{
    [HideInInspector] public BoneGardenController owner;

    void OnTriggerEnter(Collider other)
    {
        if (owner == null) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;
        owner.ScatterPlayer();
    }
}
