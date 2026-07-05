using UnityEngine;

/// <summary>
/// D8 "The Procession": a fog-bound stone garden of statues that only move while
/// unseen. They never harm you — they accumulate, crowding closer every time you look
/// away, silently funnelling you. One statue carries a doorframe on its back: the only
/// way out is to let the crowd reach you.
/// </summary>
public class ProcessionController : MonoBehaviour
{
    class Statue
    {
        public Transform tf;
        public Rigidbody rb;
        public ObservationTracker tracker = new ObservationTracker();
        public bool observedNow = true;
        public float speed;
        public float stopDist;
    }

    Statue[] _statues;
    GameObject _portalGo;
    Material _paneMat;
    bool _doorOpened;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    void Awake()
    {
        var groundMat = DimensionSceneUtil.Mat(new Color(0.16f, 0.18f, 0.15f), 0.05f);
        var stoneMat  = DimensionSceneUtil.Mat(new Color(0.14f, 0.14f, 0.15f), 0.15f);
        var frameMat  = DimensionSceneUtil.Mat(new Color(0.07f, 0.07f, 0.09f), 0.2f);
        _paneMat      = DimensionSceneUtil.Mat(new Color(0.1f, 0.08f, 0.07f), 0.2f);   // dormant; ignites on arrival

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Ground",
            new Vector3(0f, -0.5f, 0f), new Vector3(1500f, 1f, 1500f), groundMat, transform);
        DimensionSceneUtil.CreateDirectionalLight(new Color(0.6f, 0.62f, 0.65f), 0.35f, new Vector3(25f, 40f, 0f), true);

        _statues = new Statue[statueCount];
        for (int i = 0; i < statueCount; i++)
        {
            var root = new GameObject(i == 0 ? "Statue_DoorBearer" : "Statue" + i);
            root.transform.SetParent(transform, false);
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            DimensionSceneUtil.Block(PrimitiveType.Cube, "Legs", new Vector3(0f, 0.6f, 0f), new Vector3(0.8f, 1.2f, 0.6f), stoneMat, root.transform);
            DimensionSceneUtil.Block(PrimitiveType.Cube, "Torso", new Vector3(0f, 1.7f, 0f), new Vector3(1.0f, 1.0f, 0.7f), stoneMat, root.transform);
            var head = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Head", new Vector3(0f, 2.5f, 0f), Vector3.one * 0.55f, stoneMat, root.transform);
            Destroy(head.GetComponent<Collider>());

            if (i == 0)
            {
                // The way out, strapped to a statue's back.
                DimensionSceneUtil.Block(PrimitiveType.Cube, "DoorPostL", new Vector3(-0.55f, 1.4f, 0.55f), new Vector3(0.15f, 2.8f, 0.15f), frameMat, root.transform);
                DimensionSceneUtil.Block(PrimitiveType.Cube, "DoorPostR", new Vector3( 0.55f, 1.4f, 0.55f), new Vector3(0.15f, 2.8f, 0.15f), frameMat, root.transform);
                DimensionSceneUtil.Block(PrimitiveType.Cube, "DoorLintel", new Vector3(0f, 2.85f, 0.55f), new Vector3(1.25f, 0.15f, 0.15f), frameMat, root.transform);
                var pane = DimensionSceneUtil.Block(PrimitiveType.Cube, "DoorPane", new Vector3(0f, 1.4f, 0.55f), new Vector3(0.95f, 2.7f, 0.06f), _paneMat, root.transform);
                Destroy(pane.GetComponent<Collider>());
                _portalGo = DimensionSceneUtil.CreatePortal("ToBackrooms", Vector3.zero, new Vector3(0.95f, 2.6f, 0.8f),
                    LevelPortal.PortalAction.EnterInterior, nextScene, root.transform);
                _portalGo.transform.localPosition = new Vector3(0f, 1.4f, 0.55f);
                _portalGo.SetActive(false);
            }

            float a = Random.value * Mathf.PI * 2f;
            float d = Random.Range(25f, 60f);
            root.transform.position = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
            _statues[i] = new Statue
            {
                tf = root.transform,
                rb = rb,
                speed = Random.Range(2.5f, 4.5f),
                stopDist = i == 0 ? 2.5f : Random.Range(1.8f, 3.2f),
            };
        }

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(65f, 2f, 0.05f), 500f, 1f);
    }

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.22f, 0.24f, 0.25f),
                fog: new Color(0.35f, 0.37f, 0.38f), fogDensity: 0.028f,
                background: new Color(0.32f, 0.34f, 0.35f));
            _atmosApplied = true;
        }
        foreach (var s in _statues)
        {
            var b = new Bounds(s.tf.position + Vector3.up * 1.5f, new Vector3(2f, 3.4f, 2f));
            s.observedNow = s.tracker.Tick(b, out _, float.PositiveInfinity);
        }
    }

    void FixedUpdate()
    {
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null) return;
        Vector3 target = _player.Rigidbody.position;
        target.y = 0f;

        for (int i = 0; i < _statues.Length; i++)
        {
            var s = _statues[i];
            if (s.observedNow) continue;                    // statues never move while seen

            Vector3 pos = s.rb.position; pos.y = 0f;
            Vector3 to = target - pos;
            float dist = to.magnitude;
            if (dist <= s.stopDist)
            {
                if (i == 0 && !_doorOpened) OpenDoor();
                continue;
            }
            Vector3 dir = to / dist;
            Vector3 step = dir * s.speed * Time.fixedDeltaTime;
            if (step.magnitude > dist - s.stopDist) step = dir * (dist - s.stopDist);
            s.rb.MovePosition(s.rb.position + step);
            s.rb.MoveRotation(Quaternion.LookRotation(dir, Vector3.up));
        }
    }

    void OpenDoor()
    {
        _doorOpened = true;
        _portalGo.SetActive(true);
        _paneMat.EnableKeyword("_EMISSION");
        _paneMat.SetColor("_EmissionColor", new Color(0.7f, 0.9f, 1f) * 2f);
        _paneMat.color = new Color(0.7f, 0.9f, 1f);
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Statues")]
    [Tooltip("Crowd size (statue 0 carries the exit door).")]
    public int statueCount = 14;

    [Header("Exit")]
    [Tooltip("Scene the door-bearer's door leads to — the Backrooms hub.")]
    public string nextScene = "R1_Backrooms";
}
