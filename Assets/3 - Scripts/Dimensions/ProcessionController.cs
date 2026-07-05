using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// D8 "The Procession": a fog-bound stone garden. Statues only move while unseen —
/// each steers for its own slot on a tight ring around you (polar movement: approach
/// the ring radially, orbit along it, never through the middle). Blackout pulses give
/// them windows even while you watch. And the ground keeps yielding more of them:
/// risers erupt in front of you, stand dormant until you've seen them and look away,
/// then join the noose. The exit is a glowing doorframe that relocates when unseen.
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
        public float slotAngle;
        public AudioSource voice;
        public float nextSoundTime;
        public bool rising;              // animating up out of the ground
        public bool dormant;             // risen but frozen until seen-then-unseen
    }

    readonly List<Statue> _statues = new List<Statue>();
    Material _stoneMat;
    Transform _door;
    ObservationTracker _doorTracker = new ObservationTracker();
    float _encircledSince = -1f;
    bool _retreating;
    AudioSource _ambience;
    AudioClip _gruntClip, _shriekClip;
    float _startTime;
    bool _climaxed;
    UnityEngine.UI.Image _black;
    float _nextBlackoutTime;
    float _blackoutUntil;
    float _nextRiserTime;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    /// <summary>0 at entry → 1 at the climax (rampSeconds in).</summary>
    float Ramp01 => Mathf.Clamp01((Time.time - _startTime) / rampSeconds);

    void Awake()
    {
        var groundMat = DimensionSceneUtil.Mat(new Color(0.16f, 0.18f, 0.15f), 0.05f);
        _stoneMat = DimensionSceneUtil.Mat(new Color(0.14f, 0.14f, 0.15f), 0.15f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Ground",
            new Vector3(0f, -0.5f, 0f), new Vector3(1500f, 1f, 1500f), groundMat, transform);
        DimensionSceneUtil.CreateDirectionalLight(new Color(0.6f, 0.62f, 0.65f), 0.35f, new Vector3(25f, 40f, 0f), true);

        _gruntClip = GruntClip();
        _shriekClip = ShriekClip();

        for (int i = 0; i < statueCount; i++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float d = Random.Range(25f, 60f);
            BuildStatue(new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d), riser: false);
        }
        _startTime = Time.time;
        _nextRiserTime = Time.time + 14f;

        // The exit: a lone glowing doorframe out in the fog; relocates whenever it
        // leaves your view.
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

        // Blackout overlay: your sight fails on a quickening rhythm. When the dark
        // comes, EVERYTHING counts as unobserved.
        var canvasGo = new GameObject("BlackoutOverlay");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;
        var imgGo = new GameObject("Black");
        imgGo.transform.SetParent(canvasGo.transform, false);
        _black = imgGo.AddComponent<UnityEngine.UI.Image>();
        _black.color = new Color(0f, 0f, 0f, 0f);
        _black.raycastTarget = false;
        var rt = _black.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _nextBlackoutTime = Time.time + 6f;
    }

    Statue BuildStatue(Vector3 pos, bool riser)
    {
        var root = new GameObject(riser ? "Statue_Riser" : "Statue");
        root.transform.SetParent(transform, false);
        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Legs", new Vector3(0f, 0.6f, 0f), new Vector3(0.8f, 1.2f, 0.6f), _stoneMat, root.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Torso", new Vector3(0f, 1.7f, 0f), new Vector3(1.0f, 1.0f, 0.7f), _stoneMat, root.transform);
        var head = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Head", new Vector3(0f, 2.5f, 0f), Vector3.one * 0.55f, _stoneMat, root.transform);
        Destroy(head.GetComponent<Collider>());
        root.transform.position = pos;
        var voice = root.AddComponent<AudioSource>();
        voice.spatialBlend = 1f;
        voice.rolloffMode = AudioRolloffMode.Linear;
        voice.maxDistance = 70f;
        voice.playOnAwake = false;
        var s = new Statue
        {
            tf = root.transform,
            rb = rb,
            speed = Random.Range(3.5f, 5.5f),
            voice = voice,
            nextSoundTime = Time.time + Random.Range(1f, 4f),
            rising = riser,
            dormant = riser,
        };
        _statues.Add(s);
        return s;
    }

    // A statue erupts from the ground in the player's path, then stands dormant until
    // it has been SEEN and then unseen — at which point it joins the hunt.
    void SpawnRiser()
    {
        if (_statues.Count >= maxStatues) return;
        var cam = ObserverState.Cam;
        if (cam == null || _player == null || _player.Rigidbody == null) return;
        Vector3 fwd = cam.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 pos = _player.Rigidbody.position
                    + fwd * Random.Range(6f, 11f)
                    + right * Random.Range(-3.5f, 3.5f);
        pos.y = -3.4f;                                       // starts buried
        var s = BuildStatue(pos, riser: true);
        s.voice.pitch = Random.Range(0.6f, 0.8f);
        s.voice.PlayOneShot(_gruntClip, 1f);                 // the ground groans
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

        // The ground keeps yielding: risers appear faster and faster.
        if (Time.time >= _nextRiserTime)
        {
            SpawnRiser();
            _nextRiserTime = Time.time + Mathf.Lerp(14f, 4f, ramp);
        }

        // Blackout pulse: schedule, and hard-cut the screen to black for its duration.
        if (Time.time >= _nextBlackoutTime)
        {
            _blackoutUntil = Time.time + blackoutDuration;
            _nextBlackoutTime = Time.time + BlackoutInterval();
        }
        bool blackout = Time.time < _blackoutUntil;
        float a0 = _black != null ? _black.color.a : 0f;
        if (_black != null)
            _black.color = new Color(0f, 0f, 0f, Mathf.MoveTowards(a0, blackout ? 1f : 0f, Time.deltaTime / 0.08f));

        foreach (var s in _statues)
        {
            var b = new Bounds(s.tf.position + Vector3.up * 1.5f, new Vector3(2f, 3.4f, 2f));
            bool seen = s.tracker.Tick(b, out bool justLost, float.PositiveInfinity);
            s.observedNow = seen;
            if (blackout) s.observedNow = false;             // in the dark, nothing is watched

            // Dormant risers activate the first time you look at them and look away.
            if (s.dormant && !s.rising && s.tracker.WasEverObserved && (justLost || blackout))
                s.dormant = false;

            // Moving statues vocalise: grunts from the start, more often as the ramp
            // climbs, shrieking chases near/after the climax.
            if (!s.observedNow && !s.dormant && !s.rising && Time.time >= s.nextSoundTime)
            {
                bool shriek = ramp > 0.85f;
                s.voice.pitch = Random.Range(0.8f, 1.2f);
                s.voice.PlayOneShot(shriek ? _shriekClip : _gruntClip, shriek ? 0.9f : 0.55f);
                s.nextSoundTime = Time.time + Mathf.Lerp(4.5f, 0.6f, ramp) * Random.Range(0.7f, 1.3f);
            }
        }

        // Exit door: leaves your sight → it's somewhere else.
        var cam = ObserverState.Cam;
        if (cam != null && !blackout)
        {
            var db = new Bounds(_door.position + Vector3.up * 1.5f, new Vector3(2.5f, 3.5f, 2.5f));
            _doorTracker.Tick(db, out bool doorLost, float.PositiveInfinity);
            if (doorLost) RelocateDoor(cam.transform.position, initial: false);
        }
    }

    // Blackout cadence: every 6s at the start → 5s after 10s → 4s after 20s → 3s once
    // the climax hits.
    float BlackoutInterval()
    {
        float elapsed = Time.time - _startTime;
        if (elapsed < 10f) return 6f;
        if (elapsed < 20f) return 5f;
        return _climaxed ? 3f : 4f;
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
        Vector3 face = aroundPos - best; face.y = 0f;
        if (face.sqrMagnitude > 0.01f) _door.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
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

        // Rising animation (runs regardless of observation — emerging is the jumpscare).
        var active = new List<Statue>();
        foreach (var s in _statues)
        {
            if (s.rising)
            {
                Vector3 p = s.rb.position;
                p.y = Mathf.MoveTowards(p.y, 0f, 3.8f * Time.fixedDeltaTime);
                s.rb.MovePosition(p);
                if (p.y >= 0f) s.rising = false;
                continue;
            }
            if (!s.dormant) active.Add(s);
        }
        if (active.Count == 0) return;

        // Encirclement check on the hunters.
        int onRing = 0;
        foreach (var s in active)
        {
            Vector3 pos = s.rb.position; pos.y = 0f;
            if (Mathf.Abs(Vector3.Distance(pos, target) - ringRadius) < 1.2f) onRing++;
        }
        bool encircled = onRing >= Mathf.CeilToInt(active.Count * 0.7f);
        if (encircled && _encircledSince < 0f) _encircledSince = Time.time;
        if (!encircled) { _encircledSince = -1f; if (_retreating && AllFar(target)) _retreating = false; }
        // Mercy valve: after holding you a while they lose interest and slink back a
        // little — a closed circle is terrifying but never a permanent softlock.
        if (_encircledSince >= 0f && Time.time - _encircledSince > holdSeconds) _retreating = true;

        // Slot assignment: sort hunters by CURRENT angle around the player, deal evenly
        // spaced ring slots in that order — nearest gap, no long orbits, no holes.
        active.Sort((a, b) =>
        {
            Vector3 ra = a.rb.position - target, rbv = b.rb.position - target;
            return Mathf.Atan2(ra.z, ra.x).CompareTo(Mathf.Atan2(rbv.z, rbv.x));
        });
        Vector3 rel0 = active[0].rb.position - target;
        float baseAngle = Mathf.Atan2(rel0.z, rel0.x);
        for (int k = 0; k < active.Count; k++)
            active[k].slotAngle = baseAngle + k * Mathf.PI * 2f / active.Count;

        float mult = Mathf.Lerp(1f, maxSpeedMultiplier, Ramp01);
        foreach (var s in active)
        {
            if (s.observedNow) continue;                    // statues never move while seen

            Vector3 rel = s.rb.position - target; rel.y = 0f;
            float dist = Mathf.Max(rel.magnitude, 0.01f);
            float curDeg = Mathf.Atan2(rel.z, rel.x) * Mathf.Rad2Deg;
            float spd = s.speed * mult;

            // POLAR movement: radius eases to the ring (from outside — never through
            // the player), angle orbits toward this statue's slot.
            float goalRadius = _retreating ? retreatRadius : ringRadius;
            float newDist = Mathf.MoveTowards(dist, goalRadius, spd * Time.fixedDeltaTime);
            float slotDeg = s.slotAngle * Mathf.Rad2Deg;
            float angularDegPerSec = spd / Mathf.Max(newDist, 2f) * Mathf.Rad2Deg;
            float newDeg = _retreating ? curDeg : Mathf.MoveTowardsAngle(curDeg, slotDeg, angularDegPerSec * Time.fixedDeltaTime);
            float newRad = newDeg * Mathf.Deg2Rad;
            Vector3 np = target + new Vector3(Mathf.Cos(newRad), 0f, Mathf.Sin(newRad)) * newDist;
            np.y = s.rb.position.y;
            s.rb.MovePosition(np);
            Vector3 face = target - np; face.y = 0f;
            if (face.sqrMagnitude > 0.01f)
                s.rb.MoveRotation(Quaternion.LookRotation(face.normalized, Vector3.up));
        }
    }

    // The climax: every statue shrieks at once, then the ambience falls away to a low
    // aftermath drone (the statues themselves stay at full speed).
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
            if (s.dormant || s.rising) continue;
            Vector3 pos = s.rb.position; pos.y = 0f;
            if (Vector3.Distance(pos, target) < retreatRadius - 2f) return false;
        }
        return true;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Statues")]
    [Tooltip("Crowd size at entry (risers add to it over time).")]
    public int statueCount = 14;
    [Tooltip("Radius of the circle they close around you.")]
    public float ringRadius = 2.6f;
    [Tooltip("Seconds a closed circle holds you before they lose interest and drift back.")]
    public float holdSeconds = 15f;
    [Tooltip("How far they drift back out after holding you.")]
    public float retreatRadius = 7f;
    [Tooltip("Hard cap on statues including risers.")]
    public int maxStatues = 30;

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
    public float rampSeconds = 60f;
    [Tooltip("Statue speed multiplier at/after the climax.")]
    public float maxSpeedMultiplier = 4f;
    [Tooltip("How long each blackout lasts.")]
    public float blackoutDuration = 0.9f;
}
