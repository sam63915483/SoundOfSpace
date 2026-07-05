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
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        var groundMat = DimensionSceneUtil.Mat(new Color(0.18f, 0.26f, 0.16f), 0.05f);
        var stoneMat  = DimensionSceneUtil.Mat(new Color(0.08f, 0.07f, 0.09f), 0.3f);
        var glowMat   = DimensionSceneUtil.EmissiveMat(new Color(0.9f, 0.55f, 0.25f), 2f);

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
        var portal = DimensionSceneUtil.CreatePortal("ToBackrooms", Vector3.zero, new Vector3(1.3f, 5.8f, 1.2f),
            LevelPortal.PortalAction.EnterInterior, nextScene, _monolith);
        portal.transform.localPosition = new Vector3(0f, 3f, 0f);

        // Spawn far away in a random direction, doorway facing the origin.
        float a = Random.value * Mathf.PI * 2f;
        _monolith.position = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * spawnDistance;
        _monolith.rotation = Quaternion.LookRotation(-_monolith.position.normalized, Vector3.up);

        _rumble = DimensionSceneUtil.LoopingAudio(mono, DimensionSceneUtil.ToneClip(38f, 2f, 0.6f), 260f, 1f);
        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(180f, 3f, 0.03f), 900f, 1f); // faint wind bed
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
    }

    void FixedUpdate()
    {
        if (_observedNow) return;                            // frozen the moment you look
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null) return;

        Vector3 target = _player.Rigidbody.position; target.y = 0f;
        Vector3 pos = _monolithRb.position; pos.y = 0f;
        Vector3 to = target - pos;
        if (to.magnitude <= stopDistance) return;            // arrived — waiting at your back

        Vector3 step = to.normalized * advanceSpeed * Time.fixedDeltaTime;
        if (step.magnitude > to.magnitude - stopDistance) step = to.normalized * (to.magnitude - stopDistance);
        _monolithRb.MovePosition(_monolithRb.position + step);
        _monolithRb.MoveRotation(Quaternion.LookRotation(to.normalized, Vector3.up));
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Monolith")]
    [Tooltip("How far away the door starts.")]
    public float spawnDistance = 200f;
    [Tooltip("Metres per second it advances while unobserved.")]
    public float advanceSpeed = 8f;
    [Tooltip("It stops this close and waits for you to turn around.")]
    public float stopDistance = 3f;

    [Header("Exit")]
    [Tooltip("Scene the doorway leads to — the Backrooms hub.")]
    public string nextScene = "R1_Backrooms";
}
