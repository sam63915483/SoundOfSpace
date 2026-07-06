using UnityEngine;

/// <summary>
/// D4 "Waiting Field": endless quiet grassland; the exit is a monolith door that only
/// moves toward you while you are NOT looking at it. Walk through its doorway to leave.
/// </summary>
public class WaitingFieldController : MonoBehaviour
{
    Transform _root;
    Rigidbody _monolithRb;
    Transform _monolith;
    ObservationTracker _tracker = new ObservationTracker();
    AudioSource _rumble;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _observedNow = true;               // start frozen until proven unobserved
    bool _wasObservedNow;
    float _frozenDist;                      // distance locked in when the current look began
    bool _arrived;                          // stalk complete — door open, portal live
    GameObject _portalGo;
    Material _glowMat;
    bool _atmosApplied;

    // Polish pass: waiting audience of benches + a payphone that only rings unseen.
    GameObject _booth;
    AudioSource _ringSrc;
    readonly ObservationTracker _boothTracker = new ObservationTracker();

    /// <summary>Library texture material, or the pre-polish flat color if missing.</summary>
    static Material TexOr(string key, Color fallback, Vector2 tiling, float smooth = 0.1f, Color? tint = null)
        => DimensionAssetLibrary.Tex(key) != null
            ? DimensionSceneUtil.TexMat(key, tint ?? Color.white, tiling, smooth)
            : DimensionSceneUtil.Mat(fallback, smooth);

    void Awake()
    {
        _root = transform;
        // Ground: ~1 tile per 3 m over the 2 km plane; slight cool tint keeps the
        // dusky mood. Monolith: carved stone, tinted well down toward silhouette.
        var groundMat = TexOr("grass_dry", new Color(0.18f, 0.26f, 0.16f), new Vector2(650f, 650f), 0.05f,
            new Color(0.72f, 0.78f, 0.7f));
        var stoneMat  = TexOr("d4_stone", new Color(0.08f, 0.07f, 0.09f), new Vector2(1f, 2f), 0.3f,
            new Color(0.38f, 0.36f, 0.42f));
        // Doorway starts DARK (dormant). Emission ignites only when the stalk completes.
        var glowMat   = DimensionSceneUtil.Mat(new Color(0.12f, 0.08f, 0.06f), 0.2f);
        _glowMat = glowMat;

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Ground",
            new Vector3(0f, -0.5f, 0f), new Vector3(2000f, 1f, 2000f), groundMat, _root);
        DimensionSceneUtil.CreateDirectionalLight(new Color(0.75f, 0.65f, 0.8f), 0.7f, new Vector3(20f, -30f, 0f), true);

        // Monolith: two side slabs + lintel forming a walk-through doorway, on a kinematic RB.
        var mono = new GameObject("Monolith");
        mono.transform.SetParent(_root, false);
        _monolith = mono.transform;
        _monolithRb = mono.AddComponent<Rigidbody>();
        _monolithRb.isKinematic = true;
        DimensionSceneUtil.Block(PrimitiveType.Cube, "SlabL", new Vector3(-1.65f, 4.5f, 0f), new Vector3(1.9f, 9f, 1.5f), stoneMat, _monolith);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "SlabR", new Vector3( 1.65f, 4.5f, 0f), new Vector3(1.9f, 9f, 1.5f), stoneMat, _monolith);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Lintel", new Vector3(0f, 7.5f, 0f), new Vector3(5.2f, 3f, 1.5f), stoneMat, _monolith);
        var glow = DimensionSceneUtil.Block(PrimitiveType.Cube, "DoorGlow", new Vector3(0f, 3f, 0f), new Vector3(1.35f, 5.9f, 0.1f), glowMat, _monolith);
        Destroy(glow.GetComponent<Collider>());
        _portalGo = DimensionSceneUtil.CreatePortal("ToBackrooms", Vector3.zero, new Vector3(1.3f, 5.8f, 1.2f),
            LevelPortal.PortalAction.EnterInterior, nextScene, _monolith);
        _portalGo.transform.localPosition = new Vector3(0f, 3f, 0f);
        _portalGo.SetActive(false);          // the door only works once it has COME TO YOU

        // Spawn far away in a random direction, doorway facing the origin.
        float a = Random.value * Mathf.PI * 2f;
        _monolith.position = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * spawnDistance;
        _monolith.rotation = Quaternion.LookRotation(-_monolith.position.normalized, Vector3.up);

        _rumble = DimensionSceneUtil.LoopingAudio(mono, DimensionSceneUtil.ToneClip(38f, 2f, 0.6f), 260f, 1f);
        DimensionSceneUtil.AmbienceLoop2D(gameObject, "amb_d4", 180f, 0.03f, 0.5f);

        BuildWaitingAudience(a);
        BuildPayphone(a);
    }

    // A loose arc of benches BEHIND the spawn point (opposite the monolith's
    // approach azimuth, so its walk never crosses them) — a waiting audience,
    // facing where the door will come from. They re-deal while unobserved, and
    // sometimes there's one fewer than you remember.
    void BuildWaitingAudience(float monoAzimuth)
    {
        var wood = TexOr("wood_worn", new Color(0.32f, 0.24f, 0.16f), Vector2.one, 0.15f);
        float back = monoAzimuth + Mathf.PI;
        Vector3 arcCenter = new Vector3(Mathf.Cos(back), 0f, Mathf.Sin(back)) * 16f;
        var set = PropShuffleSet.Create("BenchSet", _root,
            new Bounds(arcCenter + Vector3.up, new Vector3(34f, 4f, 34f)),
            "sfx_wood_creak", facePlayer: false, countJitter: true);
        float faceYaw = Quaternion.LookRotation(
            new Vector3(Mathf.Cos(monoAzimuth), 0f, Mathf.Sin(monoAzimuth)), Vector3.up).eulerAngles.y;
        for (int i = 0; i < 12; i++)
        {
            float ang = back + (i / 11f - 0.5f) * 2.4f;
            float r = Random.Range(13f, 22f);
            set.AddAnchor(new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r,
                faceYaw + Random.Range(-15f, 15f));
        }
        for (int i = 0; i < 8; i++)
            set.AddProp(DimensionPropKit.Bench(set.transform, wood));
    }

    // A payphone off to the side that RINGS ONLY WHILE UNOBSERVED — look at it and
    // it goes silent (the inverse tell that teaches the dimension's rule). It also
    // occasionally relocates while unseen.
    void BuildPayphone(float monoAzimuth)
    {
        float side = monoAzimuth + Mathf.PI * 0.5f;
        _booth = new GameObject("Payphone");
        _booth.transform.SetParent(_root, false);
        var metal  = DimensionSceneUtil.Mat(new Color(0.10f, 0.16f, 0.22f), 0.4f);
        var glassy = DimensionSceneUtil.Mat(new Color(0.2f, 0.24f, 0.28f), 0.7f);
        BoothPart(PrimitiveType.Cube, "Base", new Vector3(0f, 0.05f, 0f), new Vector3(1.1f, 0.1f, 1.1f), metal, true);
        BoothPart(PrimitiveType.Cube, "PanelBack", new Vector3(0f, 1.3f, -0.51f), new Vector3(1.1f, 2.4f, 0.08f), glassy, true);
        BoothPart(PrimitiveType.Cube, "PanelL", new Vector3(-0.51f, 1.3f, 0f), new Vector3(0.08f, 2.4f, 1.0f), glassy, true);
        BoothPart(PrimitiveType.Cube, "PanelR", new Vector3(0.51f, 1.3f, 0f), new Vector3(0.08f, 2.4f, 1.0f), glassy, true);
        BoothPart(PrimitiveType.Cube, "Roof", new Vector3(0f, 2.55f, 0f), new Vector3(1.2f, 0.12f, 1.2f), metal, true);
        BoothPart(PrimitiveType.Cube, "PhoneBody", new Vector3(0f, 1.5f, -0.4f), new Vector3(0.35f, 0.5f, 0.16f), metal, false);
        BoothPart(PrimitiveType.Cube, "Handset", new Vector3(0.12f, 1.62f, -0.38f), new Vector3(0.08f, 0.26f, 0.09f), glassy, false);
        _booth.transform.position = new Vector3(Mathf.Cos(side), 0f, Mathf.Sin(side)) * 18f;
        _booth.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        var ringClip = DimensionAssetLibrary.Clip("sfx_phone_ring");
        if (ringClip != null)
        {
            var rgo = new GameObject("RingAudio");
            rgo.transform.SetParent(_booth.transform, false);
            rgo.transform.localPosition = Vector3.up * 1.5f;
            _ringSrc = DimensionSceneUtil.LoopingAudio(rgo, ringClip, 45f, 0.8f);
        }
    }

    GameObject BoothPart(PrimitiveType type, string name, Vector3 localPos, Vector3 scale, Material mat, bool collider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.layer = DimensionSceneUtil.WalkableLayer;
        go.transform.SetParent(_booth.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider) Destroy(go.GetComponent<Collider>());
        return go;
    }

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.3f, 0.26f, 0.34f),
                fog: new Color(0.45f, 0.38f, 0.5f), fogDensity: 0.006f,
                background: new Color(0.35f, 0.28f, 0.42f));
            _atmosApplied = true;
        }
        // Whole-monolith bounds (roughly its 5×9×1.5 body wherever it currently stands).
        var b = new Bounds(_monolith.position + Vector3.up * 4.5f, new Vector3(6f, 10f, 3f));
        _observedNow = _tracker.Tick(b, out _, float.PositiveInfinity);
        // Lock in the separation at the moment a look begins — while watched, the door
        // holds THAT distance (walk at it and it slides away). Only unobserved time
        // can ever close the gap.
        if (_observedNow && !_wasObservedNow && _player != null && _player.Rigidbody != null)
            _frozenDist = FlatDistanceToPlayer();
        _wasObservedNow = _observedNow;

        // Payphone: mute while looked at; occasionally relocate on look-away.
        if (_booth != null)
        {
            var bb = new Bounds(_booth.transform.position + Vector3.up * 1.4f, new Vector3(2f, 3f, 2f));
            bool boothObserved = _boothTracker.Tick(bb, out bool boothLost, 120f);
            if (_ringSrc != null) _ringSrc.mute = boothObserved;
            if (boothLost && Random.value < 0.25f && _player != null && _player.Rigidbody != null)
            {
                Vector3 p = _player.Rigidbody.position; p.y = 0f;
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    float na = Random.value * Mathf.PI * 2f;
                    Vector3 cand = p + new Vector3(Mathf.Cos(na), 0f, Mathf.Sin(na)) * Random.Range(15f, 30f);
                    // Never pop in on screen — only take a spot the player can't see.
                    if (ObserverState.IsObserved(new Bounds(cand + Vector3.up * 1.4f, new Vector3(2f, 3f, 2f))))
                        continue;
                    _booth.transform.position = cand;
                    break;
                }
            }
        }
    }

    float FlatDistanceToPlayer()
    {
        Vector3 d = _player.Rigidbody.position - _monolithRb.position;
        d.y = 0f;
        return d.magnitude;
    }

    void FixedUpdate()
    {
        if (_arrived) return;                                // door open — it waits forever now
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null) return;

        Vector3 target = _player.Rigidbody.position; target.y = 0f;
        Vector3 pos = _monolithRb.position; pos.y = 0f;
        Vector3 to = target - pos;
        float dist = to.magnitude;
        Vector3 dir = dist > 1e-4f ? to / dist : Vector3.forward;

        if (_observedNow)
        {
            // Unapproachable while watched: hold the distance the look began at.
            if (_frozenDist > stopDistance && dist < _frozenDist - 0.01f)
            {
                Vector3 back = -dir * Mathf.Min(advanceSpeed * Time.fixedDeltaTime, _frozenDist - dist);
                _monolithRb.MovePosition(_monolithRb.position + back);
                _monolithRb.MoveRotation(Quaternion.LookRotation(dir, Vector3.up));
            }
            return;
        }

        if (dist <= stopDistance) { Arrive(dir); return; }   // stalk complete

        Vector3 step = dir * advanceSpeed * Time.fixedDeltaTime;
        if (step.magnitude > dist - stopDistance) step = dir * (dist - stopDistance);
        _monolithRb.MovePosition(_monolithRb.position + step);
        _monolithRb.MoveRotation(Quaternion.LookRotation(dir, Vector3.up));
    }

    void Arrive(Vector3 dir)
    {
        _arrived = true;
        _portalGo.SetActive(true);
        // Ignite the doorway — emission on the (instance) glow material.
        _glowMat.EnableKeyword("_EMISSION");
        _glowMat.SetColor("_EmissionColor", new Color(0.9f, 0.55f, 0.25f) * 2f);
        _glowMat.color = new Color(0.9f, 0.55f, 0.25f);
        if (_rumble != null) _rumble.pitch = 0.6f;           // settle into a low idle drone
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Monolith")]
    [Tooltip("How far away the door starts.")]
    public float spawnDistance = 200f;
    [Tooltip("Metres per second it advances while unobserved.")]
    public float advanceSpeed = 16f;
    [Tooltip("It stops this close and waits for you to turn around.")]
    public float stopDistance = 3f;

    [Header("Exit")]
    [Tooltip("Scene the doorway leads to — the Backrooms hub.")]
    public string nextScene = "R1_Backrooms";
}
