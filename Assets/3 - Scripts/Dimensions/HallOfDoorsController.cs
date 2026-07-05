using UnityEngine;

/// <summary>
/// D7 "Hall of Doors": one endless hotel corridor, identical doors on both sides.
/// Every door returns you to the corridor start — except one, findable by a knocking
/// sound that gets louder the more directly you face it (D2's gaze-audio) and closer
/// you get. Whenever the true door leaves your view, the knock moves to another door.
/// </summary>
public class HallOfDoorsController : MonoBehaviour
{
    Transform _root;
    Transform[] _doors;
    int _trueIndex = -1;
    ObservationTracker _trueTracker = new ObservationTracker();
    AudioSource _knock;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        var carpetMat = DimensionSceneUtil.Mat(new Color(0.30f, 0.12f, 0.12f), 0.05f);
        var wallMat   = DimensionSceneUtil.Mat(new Color(0.52f, 0.46f, 0.36f), 0.1f);
        var ceilMat   = DimensionSceneUtil.Mat(new Color(0.28f, 0.26f, 0.24f), 0.1f);
        var lampMat   = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.85f, 0.6f), 1.4f);
        var frameMat  = DimensionSceneUtil.Mat(new Color(0.20f, 0.13f, 0.08f), 0.15f);
        var panelMat  = DimensionSceneUtil.Mat(new Color(0.33f, 0.22f, 0.13f), 0.2f);

        // Corridor extends back to z=-4 so the (0, 1.5, 0) spawn lands well inside it.
        float length = (doorCount / 2) * doorSpacing + 12f;
        float zc = (length - 4f) * 0.5f;
        float span = length + 4f;
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Carpet", new Vector3(0f, -0.15f, zc), new Vector3(4f, 0.3f, span), carpetMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "WallL", new Vector3(-2.15f, 1.9f, zc), new Vector3(0.3f, 4.1f, span), wallMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "WallR", new Vector3( 2.15f, 1.9f, zc), new Vector3(0.3f, 4.1f, span), wallMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Ceil",  new Vector3(0f, 4f, zc), new Vector3(4.6f, 0.3f, span), ceilMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "EndA",  new Vector3(0f, 1.9f, -4.5f), new Vector3(4.6f, 4.1f, 0.3f), wallMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "EndB",  new Vector3(0f, 1.9f, length + 0.5f), new Vector3(4.6f, 4.1f, 0.3f), wallMat, _root);
        for (float z = 6f; z < length; z += 12f)
        {
            var l = DimensionSceneUtil.Block(PrimitiveType.Cube, "Lamp", new Vector3(0f, 3.8f, z), new Vector3(0.8f, 0.1f, 0.8f), lampMat, _root);
            Destroy(l.GetComponent<Collider>());
            var lightGo = new GameObject("LampLight");
            lightGo.transform.SetParent(l.transform, false);
            lightGo.transform.localPosition = Vector3.down * 2f;
            var pl = lightGo.AddComponent<Light>();
            pl.type = LightType.Point; pl.range = 10f; pl.intensity = 1.0f;
            pl.color = new Color(1f, 0.88f, 0.65f);
        }

        // Doors: pairs facing each other down the corridor.
        _doors = new Transform[doorCount];
        for (int i = 0; i < doorCount; i++)
        {
            bool left = i % 2 == 0;
            float z = 6f + (i / 2) * doorSpacing;
            float x = left ? -2.0f : 2.0f;
            // Build the door at identity FIRST, then place it — Block positions in
            // WORLD space, so building after rotating/moving the root piled all 40
            // doors' parts at the origin (the "no doors, just a hallway" bug).
            var door = new GameObject("Door" + i);
            door.transform.SetParent(_root, false);
            DimensionSceneUtil.Block(PrimitiveType.Cube, "Panel", new Vector3(0f, 1.4f, 0f), new Vector3(1.2f, 2.8f, 0.12f), panelMat, door.transform);
            DimensionSceneUtil.Block(PrimitiveType.Cube, "FrameL", new Vector3(-0.7f, 1.4f, 0f), new Vector3(0.18f, 2.8f, 0.2f), frameMat, door.transform);
            DimensionSceneUtil.Block(PrimitiveType.Cube, "FrameR", new Vector3( 0.7f, 1.4f, 0f), new Vector3(0.18f, 2.8f, 0.2f), frameMat, door.transform);
            DimensionSceneUtil.Block(PrimitiveType.Cube, "FrameT", new Vector3(0f, 2.85f, 0f), new Vector3(1.58f, 0.18f, 0.2f), frameMat, door.transform);
            var knob = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Knob", new Vector3(0.45f, 1.35f, -0.12f), Vector3.one * 0.12f, frameMat, door.transform);
            Destroy(knob.GetComponent<Collider>());
            door.transform.SetPositionAndRotation(new Vector3(x, 0f, z), Quaternion.Euler(0f, left ? 90f : -90f, 0f));
            // Trigger pad in FRONT of the door (inside the corridor).
            var trig = new GameObject("DoorTrigger");
            trig.transform.SetParent(door.transform, false);
            trig.transform.localPosition = new Vector3(0f, 1.2f, -0.5f);
            var box = trig.AddComponent<BoxCollider>();
            box.isTrigger = true; box.size = new Vector3(1.1f, 2.4f, 0.7f);
            var dt = trig.AddComponent<HallDoorTrigger>();
            dt.owner = this; dt.index = i;
            _doors[i] = door.transform;
        }

        // Knock lives on its OWN child — MoveTrueDoor repositions this transform, and
        // putting the source on DimensionRoot moved the entire corridor with it (the
        // world teleported out from under the player on load).
        var knockGo = new GameObject("KnockSource");
        knockGo.transform.SetParent(_root, false);
        _knock = DimensionSceneUtil.LoopingAudio(knockGo, KnockClip(), 60f, 1f);
        MoveTrueDoor();
        var hum = new GameObject("HumBed");
        hum.transform.SetParent(_root, false);
        DimensionSceneUtil.LoopingAudio(hum, DimensionSceneUtil.ToneClip(110f, 2f, 0.04f), 300f, 1f);
    }

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.17f, 0.15f, 0.12f),
                fog: new Color(0.05f, 0.04f, 0.035f), fogDensity: 0.03f,
                background: new Color(0.03f, 0.025f, 0.02f));
            _atmosApplied = true;
        }
        var cam = ObserverState.Cam;
        if (cam == null || _trueIndex < 0) return;

        // True door relocates whenever it leaves your view.
        Vector3 dp = _doors[_trueIndex].position;
        var b = new Bounds(dp + Vector3.up * 1.5f, new Vector3(2f, 3.2f, 2f));
        _trueTracker.Tick(b, out bool justLost, float.PositiveInfinity);
        if (justLost) { MoveTrueDoor(); return; }

        // Gaze + distance reactive knocking (the find mechanic).
        Vector3 to = dp + Vector3.up * 1.4f - cam.transform.position;
        float align = Vector3.Dot(cam.transform.forward, to.normalized);
        float look01 = Mathf.InverseLerp(0.2f, 0.95f, align);
        _knock.volume = Mathf.Lerp(0.06f, 1f, look01);
    }

    void MoveTrueDoor()
    {
        int next = _trueIndex;
        while (next == _trueIndex) next = Random.Range(0, doorCount);
        _trueIndex = next;
        _trueTracker.Reset();
        _knock.transform.position = _doors[_trueIndex].position + Vector3.up * 1.4f;
    }

    public void DoorEntered(int index)
    {
        if (index == _trueIndex)
        {
            PortalManager.EnterInterior(nextScene);
            return;
        }
        // Wrong door: every one of them opens back onto the start of the corridor.
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null) return;
        _player.Rigidbody.position = new Vector3(0f, 1.5f, 2f);
        _player.Rigidbody.velocity = Vector3.zero;
        MoveTrueDoor();
    }

    // Three knocks, low and woody, looping every couple of seconds.
    static AudioClip KnockClip()
    {
        int rate = 44100;
        float seconds = 2.2f;
        int samples = (int)(rate * seconds);
        var data = new float[samples];
        float[] knockTimes = { 0f, 0.28f, 0.56f };
        foreach (float t0 in knockTimes)
        {
            int start = (int)(t0 * rate);
            int len = (int)(0.14f * rate);
            for (int i = 0; i < len && start + i < samples; i++)
            {
                float t = i / (float)rate;
                data[start + i] += Mathf.Sin(2f * Mathf.PI * 85f * t) * Mathf.Exp(-t * 34f) * 0.9f;
            }
        }
        var clip = AudioClip.Create("knock", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Corridor")]
    [Tooltip("Total doors (both sides). Even number.")]
    public int doorCount = 40;
    [Tooltip("Distance between door pairs along the corridor.")]
    public float doorSpacing = 6f;

    [Header("Exit")]
    [Tooltip("Scene the true door leads to.")]
    public string nextScene = "D8_Procession";
}

/// <summary>Per-door trigger: reports which door the player stepped into.</summary>
public class HallDoorTrigger : MonoBehaviour
{
    [HideInInspector] public HallOfDoorsController owner;
    [HideInInspector] public int index;
    float _cooldownUntil;

    void OnTriggerEnter(Collider other)
    {
        if (Time.time < _cooldownUntil || owner == null) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;
        _cooldownUntil = Time.time + 1f;
        owner.DoorEntered(index);
    }
}
