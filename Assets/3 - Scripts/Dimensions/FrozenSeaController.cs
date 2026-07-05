using UnityEngine;

/// <summary>
/// D6 "Frozen Sea": the observer rule INVERTS onto the player. A black ocean frozen
/// mid-storm under a sweeping lighthouse beam. The exit hatch is only solid while the
/// beam is NOT on it, and if the beam catches YOU, the sea rearranges you — teleported
/// to a random distant crest. Time your runs through the dark.
/// </summary>
public class FrozenSeaController : MonoBehaviour
{
    Transform _root;
    Transform _beamHead;
    Transform[] _fires;
    ObservationTracker[] _fireTrackers;
    UnityEngine.UI.Image _white;
    float _whiteAlpha;
    MaterialPropertyBlock _mpb;
    float _beamYaw;
    float _caughtDebounceUntil;
    float _playerLitSince = -1f;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        _root = transform;
        _mpb = new MaterialPropertyBlock();
        var seaMat = DimensionSceneUtil.Mat(new Color(0.05f, 0.08f, 0.12f), 0.85f);

        BuildSea(seaMat);
        DimensionSceneUtil.CreateDirectionalLight(new Color(0.4f, 0.5f, 0.7f), 0.22f, new Vector3(35f, -60f, 0f), false);
        BuildLighthouse(new Vector3(0f, 0f, 140f));
        BuildWhiteoutOverlay();

        // The exits: three campfires with smoke plumes rising into the night. Any of
        // them teleports you onward — but each fire RELOCATES whenever it leaves your
        // view. Spot the nearest plume and sprint before the light comes back around.
        _fires = new Transform[3];
        _fireTrackers = new ObservationTracker[3];
        for (int i = 0; i < 3; i++)
        {
            _fires[i] = BuildCampfire(i).transform;
            _fireTrackers[i] = new ObservationTracker();
            PlaceFire(i, Vector3.zero, initial: true);
        }

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(45f, 2f, 0.07f), 600f, 1f);
    }

    GameObject BuildCampfire(int index)
    {
        var stoneMat = DimensionSceneUtil.Mat(new Color(0.25f, 0.24f, 0.23f), 0.05f);
        var emberMat = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.45f, 0.1f), 3f);

        var fire = new GameObject("Campfire" + index);
        fire.transform.SetParent(_root, false);
        for (int k = 0; k < 6; k++)
        {
            float a = k * Mathf.PI / 3f;
            DimensionSceneUtil.Block(PrimitiveType.Cube, "Stone",
                new Vector3(Mathf.Cos(a) * 1.1f, 0.25f, Mathf.Sin(a) * 1.1f),
                new Vector3(0.6f, 0.5f, 0.5f), stoneMat, fire.transform);
        }
        var embers = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Embers",
            new Vector3(0f, 0.25f, 0f), new Vector3(1.4f, 0.6f, 1.4f), emberMat, fire.transform);
        Destroy(embers.GetComponent<Collider>());
        var lightGo = new GameObject("FireLight");
        lightGo.transform.SetParent(fire.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point; l.range = 18f; l.intensity = 2.2f;
        l.color = new Color(1f, 0.6f, 0.25f);
        lightGo.AddComponent<FlickerLight>();

        BuildSmokePlume(fire.transform);
        DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1f, 0f),
            new Vector3(2.2f, 2f, 2.2f), LevelPortal.PortalAction.EnterInterior, nextScene, fire.transform);
        return fire;
    }

    // Tall additive smoke column — the far-visible landmark of each exit.
    void BuildSmokePlume(Transform fire)
    {
        var smokeGo = new GameObject("Smoke");
        smokeGo.transform.SetParent(fire, false);
        smokeGo.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        var ps = smokeGo.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 9f;
        main.startSpeed = 5f;
        main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        main.startColor = new Color(0.55f, 0.58f, 0.65f, 0.5f);
        main.maxParticles = 300;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // plume trails when the fire relocates
        var emission = ps.emission;
        emission.rateOverTime = 16f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 5f;
        shape.radius = 0.35f;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3f));
        var colLife = ps.colorOverLifetime;
        colLife.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0.35f, 0.5f), new GradientAlphaKey(0f, 1f) });
        colLife.color = grad;

        var psr = smokeGo.GetComponent<ParticleSystemRenderer>();
        var smokeMat = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
        smokeMat.mainTexture = SoftCircleTex();
        smokeMat.SetColor("_TintColor", new Color(0.45f, 0.5f, 0.58f, 0.35f));
        psr.material = smokeMat;
    }

    static Texture2D _softCircle;
    static Texture2D SoftCircleTex()
    {
        if (_softCircle != null) return _softCircle;
        int n = 64;
        _softCircle = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x - n / 2f) / (n / 2f), dy = (y - n / 2f) / (n / 2f);
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                _softCircle.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        _softCircle.Apply();
        return _softCircle;
    }

    void PlaceFire(int i, Vector3 aroundPos, bool initial)
    {
        Vector3 best = _fires[i].position;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float d = Random.Range(fireMinDistance, fireMaxDistance);
            Vector3 c = initial ? Vector3.zero : aroundPos;
            float x = Mathf.Clamp(c.x + Mathf.Cos(a) * d, -270f, 270f);
            float z = Mathf.Clamp(c.z + Mathf.Sin(a) * d, -270f, 270f);
            best = new Vector3(x, SeaHeight(x, z), z);
            if (initial || !ObserverState.IsObserved(new Bounds(best + Vector3.up * 2f, new Vector3(4f, 6f, 4f))))
                break;
        }
        _fires[i].position = best;
        _fireTrackers[i].Reset();
    }

    // Tapered banded tower on a rock, gallery deck, glass lamp room, domed roof — and
    // the rotating head carrying the spotlight, the lamp, and a translucent light CONE.
    void BuildLighthouse(Vector3 basePos)
    {
        var white = DimensionSceneUtil.Mat(new Color(0.85f, 0.84f, 0.8f), 0.15f);
        var red   = DimensionSceneUtil.Mat(new Color(0.6f, 0.12f, 0.1f), 0.15f);
        var dark  = DimensionSceneUtil.Mat(new Color(0.12f, 0.12f, 0.14f), 0.2f);
        var glass = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.95f, 0.75f), 1.8f);

        float baseY = SeaHeight(basePos.x, basePos.z);
        Vector3 b = new Vector3(basePos.x, baseY, basePos.z);
        var rock = DimensionSceneUtil.Block(PrimitiveType.Sphere, "LighthouseRock",
            b + Vector3.up * 0.5f, new Vector3(16f, 5f, 16f), DimensionSceneUtil.Mat(new Color(0.18f, 0.17f, 0.16f), 0.05f), _root);

        // Tapering banded tower (white / red / white / red / white).
        Cyl("Tower1", b, 2.5f, 5.4f, 4f, white);      // y 2.5..10.5
        Cyl("Band1",  b, 3f, 4.8f, 10.9f, red);       // 9.4..12.4
        Cyl("Tower2", b, 3.5f, 4.2f, 13.9f, white);   // 12.15..15.65
        Cyl("Band2",  b, 2.6f, 3.7f, 16.6f, red);     // 15.3..17.9
        Cyl("Tower3", b, 3f, 3.2f, 19.2f, white);     // 17.7..20.7
        Cyl("Deck",   b, 0.5f, 5.2f, 21f, dark);      // gallery deck
        Cyl("LampRoom", b, 3f, 2.3f, 22.7f, glass);   // glowing glass lamp room
        var roof = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Roof",
            b + Vector3.up * 24.6f, new Vector3(3.2f, 1.6f, 3.2f), red, _root);

        // Rotating head: real spotlight + bright lamp ball + translucent volumetric cone.
        var head = new GameObject("BeamHead");
        head.transform.SetParent(_root, false);
        head.transform.position = b + Vector3.up * 23.4f;
        _beamHead = head.transform;
        var spot = head.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.range = 800f; spot.spotAngle = 14f; spot.intensity = 14f;
        spot.color = new Color(1f, 0.95f, 0.8f);
        var lampBall = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Lamp",
            Vector3.zero, Vector3.one * 1.4f, DimensionSceneUtil.EmissiveMat(new Color(1f, 0.97f, 0.85f), 4f), _beamHead);
        lampBall.transform.localPosition = Vector3.zero;
        Destroy(lampBall.GetComponent<Collider>());

        var coneGo = new GameObject("BeamCone");
        coneGo.transform.SetParent(_beamHead, false);
        var mf = coneGo.AddComponent<MeshFilter>();
        mf.sharedMesh = BuildConeMesh(600f, 63f, 20);
        var mr = coneGo.AddComponent<MeshRenderer>();
        // Dedicated unlit-additive shader: previous attempts (Standard Fade, legacy
        // particle additive) both ended up black at night — this one is unlit, fog-off,
        // and can only brighten what's behind it.
        var coneMat = new Material(Shader.Find("Dimensions/BeamAdditive"));
        coneMat.SetColor("_Color", new Color(1f, 0.95f, 0.75f, 0.10f));
        mr.sharedMaterial = coneMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    void Cyl(string name, Vector3 basePos, float halfHeightScale, float diameter, float centerY, Material mat)
    {
        var go = DimensionSceneUtil.Block(PrimitiveType.Cylinder, name,
            basePos + Vector3.up * centerY, new Vector3(diameter, halfHeightScale, diameter), mat, _root);
    }

    // Cone with apex at the origin opening along +z, double-sided so the shaft reads
    // from outside and from inside the beam.
    static Mesh BuildConeMesh(float length, float endRadius, int segs)
    {
        var verts = new Vector3[segs + 2];
        verts[0] = Vector3.zero;
        for (int i = 0; i <= segs; i++)
        {
            float a = i / (float)segs * Mathf.PI * 2f;
            verts[i + 1] = new Vector3(Mathf.Cos(a) * endRadius, Mathf.Sin(a) * endRadius, length);
        }
        var tris = new int[segs * 6];
        for (int i = 0; i < segs; i++)
        {
            tris[i * 6 + 0] = 0; tris[i * 6 + 1] = i + 1; tris[i * 6 + 2] = i + 2;
            tris[i * 6 + 3] = 0; tris[i * 6 + 4] = i + 2; tris[i * 6 + 5] = i + 1;
        }
        var mesh = new Mesh();
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Full-screen white Image faded in as the beam engulfs the player — the teleport
    // happens at peak white, so being "rearranged" reads as the light itself.
    void BuildWhiteoutOverlay()
    {
        var canvasGo = new GameObject("BeamWhiteout");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;
        var imgGo = new GameObject("White");
        imgGo.transform.SetParent(canvasGo.transform, false);
        _white = imgGo.AddComponent<UnityEngine.UI.Image>();
        _white.color = new Color(1f, 0.98f, 0.9f, 0f);
        _white.raycastTarget = false;
        var rt = _white.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    /// <summary>Frozen storm surface: ridged Perlin (abs-folded) — sharp crests, deep troughs.</summary>
    public float SeaHeight(float x, float z)
    {
        float r1 = Mathf.Abs(Mathf.PerlinNoise(x / 30f + 11.3f, z / 30f + 71.9f) - 0.5f) * 2f;
        float r2 = Mathf.Abs(Mathf.PerlinNoise(x / 9f + 43.7f, z / 9f + 17.1f) - 0.5f) * 2f;
        return r1 * 7f + r2 * 1.6f;
    }

    void BuildSea(Material seaMat)
    {
        int n = 128;
        float size = 600f, half = size * 0.5f, step = size / n;
        var verts = new Vector3[(n + 1) * (n + 1)];
        for (int z = 0, i = 0; z <= n; z++)
            for (int x = 0; x <= n; x++, i++)
            {
                float wx = x * step - half, wz = z * step - half;
                verts[i] = new Vector3(wx, SeaHeight(wx, wz), wz);
            }
        var tris = new int[n * n * 6];
        for (int z = 0, t = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * (n + 1) + x;
                tris[t++] = i; tris[t++] = i + n + 1; tris[t++] = i + 1;
                tris[t++] = i + 1; tris[t++] = i + n + 1; tris[t++] = i + n + 2;
            }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = verts; mesh.triangles = tris;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var go = new GameObject("Sea");
        go.layer = DimensionSceneUtil.WalkableLayer;
        go.transform.SetParent(_root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = seaMat;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.045f, 0.055f, 0.09f),
                fog: new Color(0.01f, 0.015f, 0.03f), fogDensity: 0.008f,
                background: new Color(0.005f, 0.008f, 0.02f));
            _atmosApplied = true;
        }

        // Sweep the beam — but once it touches the player it LOCKS ON and tracks them
        // for the catch. (A pure sweep crossed a player in ~0.5s, under the grace
        // window, so the catch never completed.)
        bool lockedOn = _playerLitSince >= 0f && _player != null && _player.Rigidbody != null;
        if (lockedOn)
        {
            Vector3 tp = _player.Rigidbody.position - _beamHead.position;
            float targetYaw = Mathf.Atan2(tp.x, tp.z) * Mathf.Rad2Deg;
            _beamYaw = Mathf.MoveTowardsAngle(_beamYaw, targetYaw, 120f * Time.deltaTime);
        }
        else
            _beamYaw += beamSweepSpeed * Time.deltaTime;
        _beamHead.rotation = Quaternion.Euler(6f, _beamYaw, 0f);

        // Campfires: any that leaves your view relocates (smoke trail lingers behind).
        var cam = ObserverState.Cam;
        if (cam != null)
            for (int i = 0; i < _fires.Length; i++)
            {
                var fb = new Bounds(_fires[i].position + Vector3.up * 4f, new Vector3(4f, 10f, 4f));
                _fireTrackers[i].Tick(fb, out bool fireLost, float.PositiveInfinity);
                if (fireLost) PlaceFire(i, cam.transform.position, initial: false);
            }

        // Player: caught in the beam for more than a moment → the sea rearranges you.
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null) return;
        bool playerLit = Time.time > _caughtDebounceUntil && LitByBeam(_player.Rigidbody.position);
        if (playerLit && _playerLitSince < 0f) _playerLitSince = Time.time;
        if (!playerLit) _playerLitSince = -1f;

        // The light engulfs you: screen whites out as the beam holds you, the teleport
        // happens at peak white, then the world fades back in somewhere else.
        if (playerLit)
            _whiteAlpha = Mathf.Clamp01((Time.time - _playerLitSince) / caughtGraceSeconds);
        else
            _whiteAlpha = Mathf.MoveTowards(_whiteAlpha, 0f, Time.deltaTime * 1.1f);
        if (_white != null) _white.color = new Color(1f, 0.98f, 0.9f, _whiteAlpha);

        if (playerLit && Time.time - _playerLitSince > caughtGraceSeconds)
        {
            _playerLitSince = -1f;
            _caughtDebounceUntil = Time.time + 3f;
            _whiteAlpha = 1f;
            float a = Random.value * Mathf.PI * 2f;
            float d = Random.Range(60f, 120f);
            Vector3 p = _player.Rigidbody.position;
            float x = Mathf.Clamp(p.x + Mathf.Cos(a) * d, -270f, 270f);
            float z = Mathf.Clamp(p.z + Mathf.Sin(a) * d, -270f, 270f);
            _player.Rigidbody.position = new Vector3(x, SeaHeight(x, z) + 2f, z);
            _player.Rigidbody.velocity = Vector3.zero;
        }
    }

    // Horizontal cone test against the beam's current heading.
    bool LitByBeam(Vector3 worldPos)
    {
        Vector3 to = worldPos - _beamHead.position;
        to.y = 0f;
        if (to.sqrMagnitude < 1f || to.magnitude > 420f) return false;
        Vector3 fwd = _beamHead.forward;
        fwd.y = 0f;
        return Vector3.Angle(fwd, to) < beamHalfAngle;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Beam")]
    [Tooltip("Sweep speed in degrees/second.")]
    public float beamSweepSpeed = 25f;
    [Tooltip("Half-angle (degrees) of the 'you are seen' cone.")]
    public float beamHalfAngle = 7f;
    [Tooltip("Seconds you can be in the beam before it relocates you (the whiteout swells over this window).")]
    public float caughtGraceSeconds = 0.9f;

    [Header("Exit campfires")]
    [Tooltip("Min relocation distance from the player.")]
    public float fireMinDistance = 45f;
    [Tooltip("Max relocation distance from the player.")]
    public float fireMaxDistance = 130f;

    [Header("Exit")]
    [Tooltip("Scene the campfires lead to.")]
    public string nextScene = "D7_HallOfDoors";
}
