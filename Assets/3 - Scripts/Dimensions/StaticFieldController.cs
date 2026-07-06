using UnityEngine;

/// <summary>
/// D18 "The Static Field": a night meadow under a light that flickers like a dying
/// TV. Every few seconds your vision cuts to black for a heartbeat — and when it
/// comes back, the scarecrows are closer. They never move while you can see; they
/// never touch you; they only ever stand a little nearer than you remember. The
/// exit is a lit farmhouse doorway on the treeline. Pure dread, no fail state.
/// </summary>
public class StaticFieldController : MonoBehaviour
{
    Transform _root;
    Rigidbody[] _crows;
    UnityEngine.UI.Image _black;
    float _nextPulseTime;
    float _pulseUntil;
    bool _pulseApplied;
    AudioSource _staticBed;
    Vector3 _farmhousePos;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        var groundMat = DimensionSceneUtil.Mat(new Color(0.07f, 0.09f, 0.06f), 0.05f);
        var woodMat   = DimensionSceneUtil.Mat(new Color(0.13f, 0.10f, 0.07f), 0.05f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Ground",
            new Vector3(0f, -0.5f, 0f), new Vector3(2000f, 1f, 2000f), groundMat, _root);

        // The dying-TV light: an aggressive FlickerLight on the only real light source.
        var sun = DimensionSceneUtil.CreateDirectionalLight(new Color(0.55f, 0.6f, 0.7f), 0.55f, new Vector3(35f, -50f, 0f), true);
        var flicker = sun.gameObject.AddComponent<FlickerLight>();
        flicker.flickerSpeed = 16f;
        flicker.flickerDepth = 0.55f;
        flicker.dropThreshold = 0.30f;
        flicker.dropAmount = 0.65f;

        // Silhouettes: broken fence LINES with sagging wire, plus lone posts and
        // tufts of dead grass.
        Random.State prev = Random.state;
        Random.InitState(1818);
        for (int line = 0; line < 4; line++)
        {
            float a = Random.value * Mathf.PI * 2f, d = Random.Range(25f, 110f);
            Vector3 start = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
            Vector3 dir = Quaternion.Euler(0f, Random.value * 360f, 0f) * Vector3.forward;
            int posts = Random.Range(5, 9);
            for (int i = 0; i < posts; i++)
            {
                Vector3 p0 = start + dir * i * 3.4f;
                DimensionSceneUtil.Block(PrimitiveType.Cube, "Post",
                    p0 + Vector3.up * 0.7f, new Vector3(0.18f, 1.4f, 0.18f), woodMat, _root)
                    .transform.rotation = Quaternion.Euler(Random.Range(-7f, 7f), Random.value * 20f, Random.Range(-7f, 7f));
                if (i < posts - 1 && Random.value > 0.25f)      // some spans broken
                {
                    var wire = DimensionSceneUtil.Block(PrimitiveType.Cube, "Wire",
                        p0 + dir * 1.7f + Vector3.up * (1.15f + Random.Range(-0.1f, 0.05f)),
                        new Vector3(0.03f, 0.03f, 3.4f), woodMat, _root);
                    wire.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(Random.Range(-3f, 3f), 0f, 0f);
                    Destroy(wire.GetComponent<Collider>());
                }
            }
        }
        for (int i = 0; i < 18; i++)
        {
            float a = Random.value * Mathf.PI * 2f, d = Random.Range(15f, 140f);
            DimensionSceneUtil.Block(PrimitiveType.Cube, "Post",
                new Vector3(Mathf.Cos(a) * d, 0.7f, Mathf.Sin(a) * d),
                new Vector3(0.18f, 1.4f, 0.18f), woodMat, _root)
                .transform.rotation = Quaternion.Euler(Random.Range(-6f, 6f), Random.value * 360f, Random.Range(-6f, 6f));
        }
        var grassMat = DimensionSceneUtil.Mat(new Color(0.10f, 0.13f, 0.08f), 0.05f);
        for (int i = 0; i < 70; i++)
        {
            float a = Random.value * Mathf.PI * 2f, d = Random.Range(4f, 130f);
            var tuft = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Tuft",
                new Vector3(Mathf.Cos(a) * d, 0.22f, Mathf.Sin(a) * d),
                new Vector3(Random.Range(0.5f, 1.1f), Random.Range(0.35f, 0.7f), Random.Range(0.5f, 1.1f)), grassMat, _root);
            Destroy(tuft.GetComponent<Collider>());
        }

        _crows = new Rigidbody[crowCount];
        for (int i = 0; i < crowCount; i++)
        {
            float a = Random.value * Mathf.PI * 2f, d = Random.Range(35f, 60f);
            _crows[i] = BuildScarecrow(new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d));
        }
        Random.state = prev;

        BuildFarmhouseDoor();
        BuildBlackout();

        // Static bed: broadband noise, no direction. It ROARS during each blackout.
        _staticBed = DimensionSceneUtil.LoopingAudio(gameObject, NoiseClip(2f, 0.16f), 800f, 1f);
        _staticBed.spatialBlend = 0f;
        _nextPulseTime = Time.time + 5f;
    }

    Rigidbody BuildScarecrow(Vector3 pos)
    {
        var cloth = DimensionSceneUtil.Mat(new Color(0.12f, 0.10f, 0.09f), 0.05f);
        var straw = DimensionSceneUtil.Mat(new Color(0.28f, 0.22f, 0.12f), 0.05f);
        var eyes  = DimensionSceneUtil.EmissiveMat(new Color(0.9f, 0.15f, 0.1f), 1.6f);
        var crow = new GameObject("Scarecrow");
        crow.transform.SetParent(_root, false);
        var rb = crow.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        Part(PrimitiveType.Cube, new Vector3(0f, 1.05f, 0f), new Vector3(0.16f, 2.1f, 0.16f), cloth, crow.transform);
        // Crossbar arms with a random droop each — no two hang alike.
        float droop = Random.Range(-14f, 6f);
        var armL = DimensionSceneUtil.Block(PrimitiveType.Cube, "Part",
            Vector3.zero, new Vector3(0.85f, 0.12f, 0.12f), cloth, crow.transform);
        armL.transform.localPosition = new Vector3(-0.45f, 1.62f, 0f);
        armL.transform.localRotation = Quaternion.Euler(0f, 0f, -droop);
        Destroy(armL.GetComponent<Collider>());
        var armR = DimensionSceneUtil.Block(PrimitiveType.Cube, "Part",
            Vector3.zero, new Vector3(0.85f, 0.12f, 0.12f), cloth, crow.transform);
        armR.transform.localPosition = new Vector3(0.45f, 1.62f, 0f);
        armR.transform.localRotation = Quaternion.Euler(0f, 0f, droop + Random.Range(-8f, 8f));
        Destroy(armR.GetComponent<Collider>());
        Part(PrimitiveType.Cube, new Vector3(0f, 1.25f, 0f), new Vector3(0.6f, 0.9f, 0.35f), cloth, crow.transform);
        // Straw poking from cuffs and hem.
        Part(PrimitiveType.Sphere, new Vector3(0f, 0.76f, 0f), new Vector3(0.5f, 0.18f, 0.32f), straw, crow.transform);
        Part(PrimitiveType.Sphere, new Vector3(0f, 2.05f, 0f), Vector3.one * 0.42f, cloth, crow.transform);
        if (Random.value < 0.55f)      // some wear hats
        {
            Part(PrimitiveType.Cylinder, new Vector3(0f, 2.28f, 0f), new Vector3(0.62f, 0.02f, 0.62f), straw, crow.transform);
            Part(PrimitiveType.Cylinder, new Vector3(0f, 2.38f, 0f), new Vector3(0.3f, 0.12f, 0.3f), straw, crow.transform);
        }
        Part(PrimitiveType.Sphere, new Vector3(-0.09f, 2.1f, 0.17f), Vector3.one * 0.06f, eyes, crow.transform);
        Part(PrimitiveType.Sphere, new Vector3(0.09f, 2.1f, 0.17f), Vector3.one * 0.06f, eyes, crow.transform);
        crow.transform.position = pos;
        crow.transform.rotation = Quaternion.Euler(Random.Range(-4f, 4f), Random.value * 360f, Random.Range(-4f, 4f));
        return rb;
    }

    static void Part(PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Transform parent)
    {
        var go = DimensionSceneUtil.Block(type, "Part", Vector3.zero, scale, mat, parent);
        go.transform.localPosition = pos;
        Object.Destroy(go.GetComponent<Collider>());
    }

    void BuildFarmhouseDoor()
    {
        float a = Random.value * Mathf.PI * 2f;
        Vector3 pos = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * doorDistance;
        var wood = DimensionSceneUtil.Mat(new Color(0.16f, 0.12f, 0.08f), 0.1f);
        var warm = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.75f, 0.4f), 2.4f);

        var house = new GameObject("Farmhouse");
        house.transform.SetParent(_root, false);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "FrontWall", new Vector3(0f, 2.5f, 0.3f), new Vector3(8f, 5f, 0.5f), wood, house.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Roof", new Vector3(0f, 5.4f, 0.3f), new Vector3(9f, 0.8f, 2f), wood, house.transform);
        var doorway = DimensionSceneUtil.Block(PrimitiveType.Cube, "DoorwayGlow", new Vector3(0f, 1.6f, 0f), new Vector3(1.4f, 3.2f, 0.2f), warm, house.transform);
        Object.Destroy(doorway.GetComponent<Collider>());
        var lg = new GameObject("DoorLight");
        lg.transform.SetParent(house.transform, false);
        lg.transform.localPosition = new Vector3(0f, 2.5f, -1.5f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = 20f; l.intensity = 2.2f;
        l.color = new Color(1f, 0.75f, 0.4f);
        lg.AddComponent<FlickerLight>();
        DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1.6f, 0f),
            new Vector3(1.4f, 3f, 1.2f), LevelPortal.PortalAction.EnterInterior, nextScene, house.transform);

        house.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-pos.normalized, Vector3.up));
        _farmhousePos = pos;

        // A faint tone from the doorway so the treeline can be scanned by ear too.
        DimensionSceneUtil.LoopingAudio(lg, DimensionSceneUtil.ToneClip(220f, 2f, 0.25f), 300f, 1f);
    }

    void BuildBlackout()
    {
        var canvasGo = new GameObject("StaticBlackout");
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
    }

    // Broadband hiss (seeded, loopable).
    static AudioClip NoiseClip(float seconds, float volume)
    {
        int rate = 44100;
        int samples = (int)(rate * seconds);
        var data = new float[samples];
        var rng = new System.Random(1818);
        for (int i = 0; i < samples; i++)
            data[i] = (float)(rng.NextDouble() * 2.0 - 1.0) * volume;
        var clip = AudioClip.Create("static", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.10f, 0.11f, 0.13f),
                fog: new Color(0.03f, 0.035f, 0.045f), fogDensity: 0.014f,
                background: new Color(0.012f, 0.015f, 0.02f));
            _atmosApplied = true;
        }

        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }

        // The pulse: screen hard-cuts to black; exactly once per pulse, every
        // scarecrow steps nearer. They arrive with the darkness, never on camera.
        if (Time.time >= _nextPulseTime)
        {
            _pulseUntil = Time.time + pulseDuration;
            _nextPulseTime = Time.time + Random.Range(pulseIntervalMin, pulseIntervalMax);
            _pulseApplied = false;
        }
        bool dark = Time.time < _pulseUntil;
        // TV feel: a grey static flicker leads the hard black, and the noise roars.
        if (_black != null)
        {
            float sinceStart = Time.time - (_pulseUntil - pulseDuration);
            if (dark && sinceStart < 0.07f)
                _black.color = new Color(0.5f, 0.5f, 0.52f, Random.Range(0.5f, 0.85f));
            else
                _black.color = new Color(0f, 0f, 0f, Mathf.MoveTowards(_black.color.a, dark ? 1f : 0f, Time.deltaTime / 0.05f));
        }
        if (_staticBed != null)
            _staticBed.volume = Mathf.MoveTowards(_staticBed.volume, dark ? 1f : 0.55f, Time.deltaTime * 12f);

        if (dark && !_pulseApplied && _player != null && _player.Rigidbody != null)
        {
            _pulseApplied = true;
            Vector3 pp = _player.Rigidbody.position; pp.y = 0f;
            // Mercy at the threshold: near the farmhouse light, nothing comes closer.
            Vector3 farmFlat = _farmhousePos; farmFlat.y = 0f;
            if ((pp - farmFlat).magnitude < safeZoneRadius) return;
            foreach (var rb in _crows)
            {
                Vector3 pos = rb.position; pos.y = 0f;
                Vector3 to = pp - pos;
                float dist = to.magnitude;
                if (dist > 90f || dist <= holdDistance + 0.1f) continue;
                float step = Mathf.Min(Random.Range(stepMin, stepMax), dist - holdDistance);
                Vector3 np = pos + to.normalized * step;
                rb.position = new Vector3(np.x, 0f, np.z);
                Vector3 face = pp - np; face.y = 0f;
                if (face.sqrMagnitude > 0.01f) rb.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
            }
        }
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Scarecrows")]
    [Tooltip("How many scarecrows share the field with you.")]
    public int crowCount = 6;
    [Tooltip("They stop advancing at this distance and just... stand there.")]
    public float holdDistance = 3f;
    [Tooltip("Min metres gained per blackout pulse.")]
    public float stepMin = 2.5f;
    [Tooltip("Max metres gained per blackout pulse.")]
    public float stepMax = 5f;

    [Header("Blackout pulses")]
    public float pulseDuration = 0.35f;
    public float pulseIntervalMin = 4.5f;
    public float pulseIntervalMax = 8f;

    [Header("Exit")]
    [Tooltip("Distance from spawn to the farmhouse door.")]
    public float doorDistance = 120f;
    [Tooltip("Scene the doorway leads to.")]
    public string nextScene = "D22_RustSea";
    [Tooltip("Within this range of the farmhouse the scarecrows stop advancing.")]
    public float safeZoneRadius = 22f;
}
