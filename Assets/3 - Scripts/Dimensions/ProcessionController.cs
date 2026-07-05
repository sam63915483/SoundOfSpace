using UnityEngine;

/// <summary>
/// D8 "The Procession": a fog-bound stone garden. Statues only move while unseen, and
/// they don't merely follow — each one steers for its own slot on a tight ring around
/// you. Let them work unwatched for too long and they close the circle and hold you in
/// it. The exit is a free-standing glowing door that relocates every time it leaves
/// your view (D1-style): spot it, keep it in sight, reach it before the circle closes.
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
        public float slotAngle;              // this statue's place on the encircling ring
        public AudioSource voice;
        public float nextSoundTime;
    }

    Statue[] _statues;
    Transform _door;
    ObservationTracker _doorTracker = new ObservationTracker();
    float _encircledSince = -1f;
    bool _retreating;
    AudioSource _ambience;
    AudioClip _gruntClip, _shriekClip;
    float _startTime;
    bool _climaxed;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    /// <summary>0 at entry → 1 at the climax (rampSeconds in).</summary>
    float Ramp01 => Mathf.Clamp01((Time.time - _startTime) / rampSeconds);

    void Awake()
    {
        var groundMat = DimensionSceneUtil.Mat(new Color(0.16f, 0.18f, 0.15f), 0.05f);
        var stoneMat  = DimensionSceneUtil.Mat(new Color(0.14f, 0.14f, 0.15f), 0.15f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Ground",
            new Vector3(0f, -0.5f, 0f), new Vector3(1500f, 1f, 1500f), groundMat, transform);
        DimensionSceneUtil.CreateDirectionalLight(new Color(0.6f, 0.62f, 0.65f), 0.35f, new Vector3(25f, 40f, 0f), true);

        _statues = new Statue[statueCount];
        for (int i = 0; i < statueCount; i++)
        {
            var root = new GameObject("Statue" + i);
            root.transform.SetParent(transform, false);
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            DimensionSceneUtil.Block(PrimitiveType.Cube, "Legs", new Vector3(0f, 0.6f, 0f), new Vector3(0.8f, 1.2f, 0.6f), stoneMat, root.transform);
            DimensionSceneUtil.Block(PrimitiveType.Cube, "Torso", new Vector3(0f, 1.7f, 0f), new Vector3(1.0f, 1.0f, 0.7f), stoneMat, root.transform);
            var head = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Head", new Vector3(0f, 2.5f, 0f), Vector3.one * 0.55f, stoneMat, root.transform);
            Destroy(head.GetComponent<Collider>());

            float a = Random.value * Mathf.PI * 2f;
            float d = Random.Range(25f, 60f);
            root.transform.position = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
            var voice = root.AddComponent<AudioSource>();
            voice.spatialBlend = 1f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.maxDistance = 70f;
            voice.playOnAwake = false;
            _statues[i] = new Statue
            {
                tf = root.transform,
                rb = rb,
                speed = Random.Range(2.5f, 4.5f),
                slotAngle = i * Mathf.PI * 2f / statueCount,
                voice = voice,
                nextSoundTime = Time.time + Random.Range(1f, 4f),
            };
        }
        _gruntClip = GruntClip();
        _shriekClip = ShriekClip();
        _startTime = Time.time;

        // The exit: a lone glowing doorframe out in the fog. Relocates whenever it
        // leaves your view — the observation chase, D1-style, out in the open.
        var frameMat = DimensionSceneUtil.Mat(new Color(0.07f, 0.07f, 0.09f), 0.2f);
        var paneMat  = DimensionSceneUtil.EmissiveMat(new Color(0.7f, 0.9f, 1f), 2f);
        var door = new GameObject("ExitDoor");
        door.transform.SetParent(transform, false);
        _door = door.transform;
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostL",  new Vector3(-0.8f, 1.5f, 0f), new Vector3(0.3f, 3f, 0.3f), frameMat, _door);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostR",  new Vector3( 0.8f, 1.5f, 0f), new Vector3(0.3f, 3f, 0.3f), frameMat, _door);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Lintel", new Vector3(0f, 3.05f, 0f),   new Vector3(1.9f, 0.3f, 0.3f), frameMat, _door);
        var pane = DimensionSceneUtil.Block(PrimitiveType.Cube, "Glow", new Vector3(0f, 1.5f, 0f), new Vector3(1.3f, 2.9f, 0.05f), paneMat, _door);
        Destroy(pane.GetComponent<Collider>());
        DimensionSceneUtil.CreatePortal("ToBackrooms", new Vector3(0f, 1.5f, 0f),
            new Vector3(1.3f, 2.9f, 0.6f), LevelPortal.PortalAction.EnterInterior, nextScene, _door);
        RelocateDoor(Vector3.zero, initial: true);

        // Ambience bed — volume/pitch climb toward the climax, then fall away.
        _ambience = DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(65f, 2f, 0.5f), 800f, 0.1f);
        _ambience.spatialBlend = 0f;    // dread has no direction
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
        // The longer you stay, the faster and louder it gets.
        float ramp = Ramp01;
        if (!_climaxed)
        {
            _ambience.volume = Mathf.Lerp(0.08f, 0.6f, ramp * ramp);
            _ambience.pitch = Mathf.Lerp(1f, 1.7f, ramp);
            if (ramp >= 1f) Climax();
        }
        else
        {
            _ambience.volume = Mathf.MoveTowards(_ambience.volume, 0.14f, Time.deltaTime * 0.12f);
            _ambience.pitch = Mathf.MoveTowards(_ambience.pitch, 0.85f, Time.deltaTime * 0.25f);
        }

        foreach (var s in _statues)
        {
            var b = new Bounds(s.tf.position + Vector3.up * 1.5f, new Vector3(2f, 3.4f, 2f));
            s.observedNow = s.tracker.Tick(b, out _, float.PositiveInfinity);

            // Moving statues vocalise: grunts from the start, more often as the ramp
            // climbs, shrieking chases near/after the climax.
            if (!s.observedNow && Time.time >= s.nextSoundTime)
            {
                bool shriek = ramp > 0.85f;
                s.voice.pitch = Random.Range(0.8f, 1.2f);
                s.voice.PlayOneShot(shriek ? _shriekClip : _gruntClip, shriek ? 0.9f : 0.55f);
                s.nextSoundTime = Time.time + Mathf.Lerp(4.5f, 0.6f, ramp) * Random.Range(0.7f, 1.3f);
            }
        }

        // Exit door: leaves your sight → it's somewhere else.
        var cam = ObserverState.Cam;
        if (cam != null)
        {
            var db = new Bounds(_door.position + Vector3.up * 1.5f, new Vector3(2.5f, 3.5f, 2.5f));
            _doorTracker.Tick(db, out bool doorLost, float.PositiveInfinity);
            if (doorLost) RelocateDoor(cam.transform.position, initial: false);
        }
    }

    void RelocateDoor(Vector3 aroundPos, bool initial)
    {
        Vector3 best = _door.position;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float d = Random.Range(doorMinDistance, doorMaxDistance);
            Vector3 c = initial ? Vector3.zero : aroundPos;
            best = new Vector3(c.x + Mathf.Cos(a) * d, 0f, c.z + Mathf.Sin(a) * d);
            if (initial || !ObserverState.IsObserved(new Bounds(best + Vector3.up * 1.5f, new Vector3(3f, 4f, 3f))))
                break;
        }
        _door.position = best;
        if (!initial) _door.rotation = Quaternion.LookRotation((aroundPos - best).normalized.Flat(), Vector3.up);
        _doorTracker.Reset();
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

        // Encirclement check: how many statues are already standing on their ring slot?
        int onRing = 0;
        foreach (var s in _statues)
        {
            Vector3 pos = s.rb.position; pos.y = 0f;
            if (Vector3.Distance(pos, target) < ringRadius + 1.2f) onRing++;
        }
        bool encircled = onRing >= Mathf.CeilToInt(statueCount * 0.7f);
        if (encircled && _encircledSince < 0f) _encircledSince = Time.time;
        if (!encircled) { _encircledSince = -1f; if (_retreating && AllFar(target)) _retreating = false; }
        // Mercy valve: after holding you a while they lose interest and drift back out,
        // so a closed circle is terrifying but never a permanent softlock.
        if (_encircledSince >= 0f && Time.time - _encircledSince > holdSeconds) _retreating = true;

        foreach (var s in _statues)
        {
            if (s.observedNow) continue;                    // statues never move while seen

            Vector3 pos = s.rb.position; pos.y = 0f;
            Vector3 goal;
            if (_retreating)
            {
                Vector3 away = (pos - target).normalized;
                if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
                goal = target + away * retreatRadius;
            }
            else
            {
                // Steer for MY slot on the ring around the player — the crowd doesn't
                // chase you, it closes around you.
                goal = target + new Vector3(Mathf.Cos(s.slotAngle), 0f, Mathf.Sin(s.slotAngle)) * ringRadius;
            }

            Vector3 to = goal - pos;
            float dist = to.magnitude;
            if (dist < 0.15f) continue;
            Vector3 dir = to / dist;
            // Speed climbs with time-in-dimension: 1x at entry → maxSpeedMultiplier at
            // the climax and stays there. Past ~90s the circle closes almost instantly.
            float spd = s.speed * Mathf.Lerp(1f, maxSpeedMultiplier, Ramp01);
            Vector3 step = dir * spd * Time.fixedDeltaTime;
            if (step.magnitude > dist) step = to;
            s.rb.MovePosition(s.rb.position + step);
            Vector3 face = target - pos;
            if (face.sqrMagnitude > 0.01f)
                s.rb.MoveRotation(Quaternion.LookRotation(face.normalized, Vector3.up));
        }
    }

    // The 90-second mark: every statue shrieks at once, then the ambience falls away
    // to a low aftermath drone (the statues themselves stay at full speed).
    void Climax()
    {
        _climaxed = true;
        foreach (var s in _statues)
        {
            s.voice.pitch = Random.Range(0.75f, 1.25f);
            s.voice.PlayOneShot(_shriekClip, 1f);
        }
    }

    // Low woody grunt — a pitched noise thump with a fast decay.
    static AudioClip GruntClip()
    {
        int rate = 44100;
        float seconds = 0.4f;
        int samples = (int)(rate * seconds);
        var data = new float[samples];
        var rng = new System.Random(12345);
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float tone = Mathf.Sin(2f * Mathf.PI * (95f + 30f * Mathf.Sin(t * 40f)) * t);
            data[i] = (tone * 0.7f + noise * 0.3f) * Mathf.Exp(-t * 14f) * 0.9f;
        }
        var clip = AudioClip.Create("grunt", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Descending vibrato shriek.
    static AudioClip ShriekClip()
    {
        int rate = 44100;
        float seconds = 0.8f;
        int samples = (int)(rate * seconds);
        var data = new float[samples];
        double phase = 0.0;
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            float f = Mathf.Lerp(1400f, 520f, t / seconds) * (1f + 0.06f * Mathf.Sin(t * 55f));
            phase += 2.0 * Mathf.PI * f / rate;
            float env = Mathf.Clamp01(t / 0.03f) * Mathf.Exp(-t * 3.2f);
            data[i] = (Mathf.Sin((float)phase) * 0.8f + Mathf.Sin((float)phase * 2.01f) * 0.2f) * env;
        }
        var clip = AudioClip.Create("shriek", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    bool AllFar(Vector3 target)
    {
        foreach (var s in _statues)
        {
            Vector3 pos = s.rb.position; pos.y = 0f;
            if (Vector3.Distance(pos, target) < retreatRadius - 2f) return false;
        }
        return true;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Statues")]
    [Tooltip("Crowd size.")]
    public int statueCount = 14;
    [Tooltip("Radius of the circle they close around you.")]
    public float ringRadius = 2.6f;
    [Tooltip("Seconds a closed circle holds you before they lose interest and drift back.")]
    public float holdSeconds = 8f;
    [Tooltip("How far they drift back out after holding you.")]
    public float retreatRadius = 12f;

    [Header("Exit door")]
    [Tooltip("Min relocation distance from the player.")]
    public float doorMinDistance = 20f;
    [Tooltip("Max relocation distance from the player.")]
    public float doorMaxDistance = 45f;

    [Header("Exit")]
    [Tooltip("Scene the door leads to — the Backrooms hub.")]
    public string nextScene = "R1_Backrooms";

    [Header("Escalation")]
    [Tooltip("Seconds from entry to the climax (speed + ambience peak).")]
    public float rampSeconds = 90f;
    [Tooltip("Statue speed multiplier at/after the climax.")]
    public float maxSpeedMultiplier = 4f;
}

static class ProcessionVecExt
{
    /// <summary>Y-flattened, safe-normalized direction (for door facing).</summary>
    public static Vector3 Flat(this Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }
}
