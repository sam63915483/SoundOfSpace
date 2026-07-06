using UnityEngine;

/// <summary>
/// D22 "Rust Sea": dunes of orange scrap metal under a sodium sky. A crane light
/// sweeps the yard — the world watches YOU (D6's inversion): get caught in the beam
/// and the yard rearranges you to a distant dune at peak whiteout. The exits are
/// three burning barrels trailing smoke; each relocates whenever it leaves your view.
/// </summary>
public class RustSeaController : MonoBehaviour
{
    Transform _root;
    Transform _beamHead;
    Transform[] _barrels;
    ObservationTracker[] _barrelTrackers;
    UnityEngine.UI.Image _white;
    float _whiteAlpha;
    float _beamYaw;
    float _caughtDebounceUntil;
    float _playerLitSince = -1f;
    Renderer _beacon;
    AudioSource _ticker;
    AudioClip _tickClip;
    float _nextTickTime;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        // Dune UVs are world-space (1 tile per 2m), so tiling stays (1,1) here.
        var rustMat = DimensionSceneUtil.TexMat("d22_rust", new Color(0.75f, 0.55f, 0.4f), Vector2.one, 0.35f);

        BuildDunes(rustMat);
        DimensionSceneUtil.CreateDirectionalLight(new Color(0.9f, 0.55f, 0.3f), 0.35f, new Vector3(30f, -70f, 0f), false);
        BuildCrane(new Vector3(0f, 0f, 120f));
        BuildContainerStacks();
        BuildWhiteout();

        _barrels = new Transform[3];
        _barrelTrackers = new ObservationTracker[3];
        for (int i = 0; i < 3; i++)
        {
            _barrels[i] = BuildBarrel(i).transform;
            _barrelTrackers[i] = new ObservationTracker();
            PlaceBarrel(i, Vector3.zero, initial: true);
        }

        // Yard ambience (metal groans, wind over hulls) — sits under the geiger ticks.
        DimensionSceneUtil.AmbienceLoop2D(gameObject, "amb_d22", 48f, 0.07f, 0.55f);

        // Geiger-style danger ticks: the closer the sweeping beam's heading is to
        // you, the faster the clicks — you can dodge with your ears.
        _tickClip = TickClip();
        var tickGo = new GameObject("DangerTicker");
        tickGo.transform.SetParent(_root, false);
        _ticker = tickGo.AddComponent<AudioSource>();
        _ticker.spatialBlend = 0f;
    }

    static AudioClip TickClip()
    {
        int rate = 44100;
        int samples = (int)(rate * 0.04f);
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            data[i] = Mathf.Sin(2f * Mathf.PI * 2400f * t) * Mathf.Exp(-t * 160f) * 0.5f;
        }
        var clip = AudioClip.Create("tick", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Scrap dunes: ridged Perlin, sharper than sand.</summary>
    public float DuneHeight(float x, float z)
    {
        float r1 = Mathf.Abs(Mathf.PerlinNoise(x / 42f + 5.1f, z / 42f + 33.7f) - 0.5f) * 2f;
        float r2 = Mathf.PerlinNoise(x / 11f + 91.3f, z / 11f + 8.9f);
        return r1 * 9f + r2 * 1.4f;
    }

    void BuildDunes(Material rustMat)
    {
        int n = 128;
        float size = 600f, half = size * 0.5f, step = size / n;
        var verts = new Vector3[(n + 1) * (n + 1)];
        var uvs = new Vector2[(n + 1) * (n + 1)];
        for (int z = 0, i = 0; z <= n; z++)
            for (int x = 0; x <= n; x++, i++)
            {
                float wx = x * step - half, wz = z * step - half;
                verts[i] = new Vector3(wx, DuneHeight(wx, wz), wz);
                uvs[i] = new Vector2(wx, wz) * 0.5f;   // 1 texture tile per 2m
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
        mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var go = new GameObject("ScrapDunes");
        go.layer = DimensionSceneUtil.WalkableLayer;
        go.transform.SetParent(_root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = rustMat;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;

        // Half-buried scrap: plates, girders, pipes and oil pools breaking the dunes.
        var plateMat  = DimensionSceneUtil.TexMat("d22_rust", new Color(0.6f, 0.42f, 0.3f), new Vector2(2f, 2f), 0.5f);
        var girderMat = DimensionSceneUtil.TexMat("d22_rust", new Color(0.45f, 0.3f, 0.22f), new Vector2(3f, 1f), 0.45f);
        var pipeMat   = DimensionSceneUtil.Mat(new Color(0.24f, 0.14f, 0.09f), 0.55f);
        var oilMat    = DimensionSceneUtil.Mat(new Color(0.03f, 0.025f, 0.02f), 0.95f);
        Random.State prev = Random.state;
        Random.InitState(2222);
        for (int i = 0; i < 34; i++)
        {
            float x = Random.Range(-250f, 250f), z = Random.Range(-250f, 250f);
            var plate = DimensionSceneUtil.Block(PrimitiveType.Cube, "Scrap",
                new Vector3(x, DuneHeight(x, z) + Random.Range(-0.4f, 0.6f), z),
                new Vector3(Random.Range(1.5f, 5f), Random.Range(0.2f, 0.5f), Random.Range(1.5f, 5f)), plateMat, _root);
            plate.transform.rotation = Quaternion.Euler(Random.Range(-35f, 35f), Random.value * 360f, Random.Range(-35f, 35f));
        }
        for (int i = 0; i < 16; i++)
        {
            float x = Random.Range(-240f, 240f), z = Random.Range(-240f, 240f);
            var girder = DimensionSceneUtil.Block(PrimitiveType.Cube, "Girder",
                new Vector3(x, DuneHeight(x, z) + Random.Range(1f, 3.2f), z),
                new Vector3(0.45f, 0.45f, Random.Range(6f, 14f)), girderMat, _root);
            girder.transform.rotation = Quaternion.Euler(Random.Range(-40f, -12f), Random.value * 360f, Random.Range(-10f, 10f));
        }
        for (int i = 0; i < 12; i++)
        {
            float x = Random.Range(-240f, 240f), z = Random.Range(-240f, 240f);
            var pipe = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Pipe",
                new Vector3(x, DuneHeight(x, z) + 0.5f, z),
                new Vector3(Random.Range(0.9f, 1.6f), Random.Range(2.5f, 5f), Random.Range(0.9f, 1.6f)), pipeMat, _root);
            pipe.transform.rotation = Quaternion.Euler(Random.Range(70f, 110f), Random.value * 360f, 0f);
        }
        for (int i = 0; i < 10; i++)
        {
            float x = Random.Range(-200f, 200f), z = Random.Range(-200f, 200f);
            var oil = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "OilPool",
                new Vector3(x, DuneHeight(x, z) + 0.04f, z),
                new Vector3(Random.Range(2.5f, 6f), 0.02f, Random.Range(2.5f, 6f)), oilMat, _root);
            Object.Destroy(oil.GetComponent<Collider>());
        }
        Random.state = prev;
    }

    /// <summary>Three shipping-container yards out in the dunes. Each yard's
    /// containers re-deal onto different ground slots whenever the whole yard
    /// leaves your view — the skyline is never quite the way you left it. Fixed
    /// footprints well away from the crane and spawn, so they can't wall off
    /// gameplay; anchors are ground-level only (no floating stacks).</summary>
    void BuildContainerStacks()
    {
        Vector2[] yards = { new Vector2(-120f, -60f), new Vector2(90f, -140f), new Vector2(150f, 80f) };
        Vector2[] slots = { new Vector2(0f, 0f), new Vector2(8f, 2.5f), new Vector2(-7f, 5f),
                            new Vector2(3.5f, -8f), new Vector2(-5f, -7f) };
        Color[] tints = { new Color(0.85f, 0.75f, 0.7f), new Color(0.7f, 0.75f, 0.85f), new Color(0.8f, 0.65f, 0.55f) };
        Random.State prev = Random.state;
        Random.InitState(2299);
        for (int y = 0; y < yards.Length; y++)
        {
            Vector2 c = yards[y];
            float cy = DuneHeight(c.x, c.y);
            var zone = new Bounds(new Vector3(c.x, cy + 3f, c.y), new Vector3(26f, 12f, 26f));
            var set = PropShuffleSet.Create("ContainerYard" + y, _root, zone, "sfx_wood_creak");
            set.observeMaxDistance = 150f;
            set.posJitter = 0.4f;
            set.yawJitterDeg = 8f;
            foreach (var s in slots)
            {
                float ax = c.x + s.x, az = c.y + s.y;
                set.AddAnchor(new Vector3(ax, DuneHeight(ax, az), az), Random.Range(0f, 360f));
            }
            for (int k = 0; k < 3; k++)
            {
                var box = new GameObject("Container");
                box.transform.SetParent(_root, false);
                var mat = DimensionSceneUtil.TexMat("d22_container", tints[(y + k) % tints.Length], Vector2.one, 0.4f);
                var body = DimensionSceneUtil.Block(PrimitiveType.Cube, "Body",
                    Vector3.zero, new Vector3(2.5f, 2.6f, 6.1f), mat, box.transform);
                body.transform.localPosition = new Vector3(0f, 1.3f, 0f);
                // Pre-place on a yard slot so nothing sits on the spawn before the
                // set's first deal (props default to their built position otherwise).
                float px = c.x + slots[k].x, pz = c.y + slots[k].y;
                box.transform.position = new Vector3(px, DuneHeight(px, pz), pz);
                set.AddProp(box);
            }
        }
        Random.state = prev;
    }

    void BuildCrane(Vector3 basePos)
    {
        var steel = DimensionSceneUtil.Mat(new Color(0.72f, 0.55f, 0.12f), 0.3f);
        var dark  = DimensionSceneUtil.Mat(new Color(0.15f, 0.12f, 0.10f), 0.3f);
        float baseY = DuneHeight(basePos.x, basePos.z);
        Vector3 b = new Vector3(basePos.x, baseY, basePos.z);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "CraneBase", b + Vector3.up * 1.5f, new Vector3(8f, 3f, 8f), dark, _root);
        // Lattice mast: four corner chords + cross-bracing instead of one solid box.
        for (int cx = -1; cx <= 1; cx += 2)
            for (int cz = -1; cz <= 1; cz += 2)
                DimensionSceneUtil.Block(PrimitiveType.Cube, "MastChord",
                    b + new Vector3(cx * 0.9f, 17f, cz * 0.9f), new Vector3(0.35f, 28f, 0.35f), steel, _root);
        for (float y = 5f; y <= 29f; y += 4f)
        {
            var braceA = DimensionSceneUtil.Block(PrimitiveType.Cube, "MastBrace",
                b + new Vector3(0f, y, 0.9f), new Vector3(2.4f, 0.22f, 0.22f), steel, _root);
            braceA.transform.rotation = Quaternion.Euler(0f, 0f, 32f);
            Object.Destroy(braceA.GetComponent<Collider>());
            var braceB = DimensionSceneUtil.Block(PrimitiveType.Cube, "MastBrace",
                b + new Vector3(0.9f, y + 2f, 0f), new Vector3(0.22f, 0.22f, 2.4f), steel, _root);
            braceB.transform.rotation = Quaternion.Euler(32f, 0f, 0f);
            Object.Destroy(braceB.GetComponent<Collider>());
        }
        DimensionSceneUtil.Block(PrimitiveType.Cube, "CraneJib", b + new Vector3(0f, 31f, -9f), new Vector3(2f, 1.8f, 24f), steel, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "CraneCab", b + new Vector3(0f, 29f, 1.5f), new Vector3(3.2f, 2.6f, 3.2f), dark, _root);
        // Aircraft warning beacon on the mast top — slow red pulse.
        var beacon = DimensionSceneUtil.Block(PrimitiveType.Sphere, "WarningBeacon",
            b + Vector3.up * 31.8f, Vector3.one * 0.7f,
            DimensionSceneUtil.EmissiveMat(new Color(1f, 0.12f, 0.08f), 3f), _root);
        Object.Destroy(beacon.GetComponent<Collider>());
        _beacon = beacon.GetComponent<Renderer>();

        // The sweeping head hangs from the jib tip.
        var head = new GameObject("BeamHead");
        head.transform.SetParent(_root, false);
        head.transform.position = b + new Vector3(0f, 29.5f, -19f);
        _beamHead = head.transform;
        var spot = head.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.range = 700f; spot.spotAngle = 13f; spot.intensity = 13f;
        spot.color = new Color(1f, 0.85f, 0.6f);
        var lamp = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Lamp",
            Vector3.zero, Vector3.one * 1.2f, DimensionSceneUtil.EmissiveMat(new Color(1f, 0.9f, 0.7f), 4f), _beamHead);
        lamp.transform.localPosition = Vector3.zero;
        Object.Destroy(lamp.GetComponent<Collider>());

        var coneGo = new GameObject("BeamCone");
        coneGo.transform.SetParent(_beamHead, false);
        var mf = coneGo.AddComponent<MeshFilter>();
        mf.sharedMesh = BuildConeMesh(500f, 52f, 20);
        var mr = coneGo.AddComponent<MeshRenderer>();
        var coneMat = new Material(Shader.Find("Dimensions/BeamAdditive"));
        coneMat.SetColor("_Color", new Color(1f, 0.85f, 0.55f, 0.10f));
        mr.sharedMaterial = coneMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

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

    GameObject BuildBarrel(int index)
    {
        var barrelMat = DimensionSceneUtil.Mat(new Color(0.20f, 0.12f, 0.08f), 0.3f);
        var emberMat  = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.5f, 0.12f), 3f);

        var barrel = new GameObject("Barrel" + index);
        barrel.transform.SetParent(_root, false);
        DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Drum",
            new Vector3(0f, 0.65f, 0f), new Vector3(1.1f, 0.65f, 1.1f), barrelMat, barrel.transform);
        var fire = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Fire",
            new Vector3(0f, 1.35f, 0f), new Vector3(0.9f, 0.7f, 0.9f), emberMat, barrel.transform);
        Object.Destroy(fire.GetComponent<Collider>());
        var lg = new GameObject("FireLight");
        lg.transform.SetParent(barrel.transform, false);
        lg.transform.localPosition = new Vector3(0f, 1.8f, 0f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = 18f; l.intensity = 2.4f;
        l.color = new Color(1f, 0.55f, 0.2f);
        lg.AddComponent<FlickerLight>();

        BuildSmokePlume(barrel.transform);
        DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1f, 0f),
            new Vector3(2.2f, 2f, 2.2f), LevelPortal.PortalAction.EnterInterior, nextScene, barrel.transform);
        return barrel;
    }

    void BuildSmokePlume(Transform barrel)
    {
        var smokeGo = new GameObject("Smoke");
        smokeGo.transform.SetParent(barrel, false);
        smokeGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        var ps = smokeGo.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 9f;
        main.startSpeed = 5f;
        main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        main.startColor = new Color(0.25f, 0.22f, 0.2f, 0.55f);
        main.maxParticles = 300;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 16f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 6f;
        shape.radius = 0.3f;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3.2f));
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
        smokeMat.SetColor("_TintColor", new Color(0.5f, 0.4f, 0.3f, 0.35f));
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

    void PlaceBarrel(int i, Vector3 aroundPos, bool initial)
    {
        Vector3 best = _barrels[i].position;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float d = Random.Range(barrelMinDistance, barrelMaxDistance);
            Vector3 c = initial ? Vector3.zero : aroundPos;
            float x = Mathf.Clamp(c.x + Mathf.Cos(a) * d, -270f, 270f);
            float z = Mathf.Clamp(c.z + Mathf.Sin(a) * d, -270f, 270f);
            best = new Vector3(x, DuneHeight(x, z), z);
            if (initial || !ObserverState.IsObserved(new Bounds(best + Vector3.up * 2f, new Vector3(4f, 6f, 4f))))
                break;
        }
        _barrels[i].position = best;
        _barrelTrackers[i].Reset();
    }

    void BuildWhiteout()
    {
        var canvasGo = new GameObject("BeamWhiteout");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;
        var imgGo = new GameObject("White");
        imgGo.transform.SetParent(canvasGo.transform, false);
        _white = imgGo.AddComponent<UnityEngine.UI.Image>();
        _white.color = new Color(1f, 0.95f, 0.85f, 0f);
        _white.raycastTarget = false;
        var rt = _white.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.30f, 0.18f, 0.10f),
                fog: new Color(0.45f, 0.24f, 0.10f), fogDensity: 0.010f,
                background: new Color(0.35f, 0.16f, 0.07f));
            _atmosApplied = true;
        }

        // Sweep, then lock on once it touches the player (D6's proven catch logic).
        bool lockedOn = _playerLitSince >= 0f && _player != null && _player.Rigidbody != null;
        if (lockedOn)
        {
            Vector3 tp = _player.Rigidbody.position - _beamHead.position;
            float targetYaw = Mathf.Atan2(tp.x, tp.z) * Mathf.Rad2Deg;
            _beamYaw = Mathf.MoveTowardsAngle(_beamYaw, targetYaw, 120f * Time.deltaTime);
        }
        else
            _beamYaw += beamSweepSpeed * Time.deltaTime;
        _beamHead.rotation = Quaternion.Euler(14f, _beamYaw, 0f);

        var cam = ObserverState.Cam;
        if (cam != null)
            for (int i = 0; i < _barrels.Length; i++)
            {
                var fb = new Bounds(_barrels[i].position + Vector3.up * 4f, new Vector3(4f, 10f, 4f));
                _barrelTrackers[i].Tick(fb, out bool lost, float.PositiveInfinity);
                if (lost) PlaceBarrel(i, cam.transform.position, initial: false);
            }

        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        // Beacon pulse rides a slow sine (renderer toggle — no material churn).
        if (_beacon != null) _beacon.enabled = Mathf.Sin(Time.time * 2.4f) > -0.2f;

        if (_player == null || _player.Rigidbody == null) return;

        // Danger ticker: tick rate scales with how close the beam heading is to you.
        Vector3 toPlayer = _player.Rigidbody.position - _beamHead.position; toPlayer.y = 0f;
        Vector3 beamFlat = _beamHead.forward; beamFlat.y = 0f;
        float beamAngle = Vector3.Angle(beamFlat, toPlayer);
        if (beamAngle < 55f && toPlayer.magnitude < 380f && Time.time >= _nextTickTime)
        {
            _nextTickTime = Time.time + Mathf.Lerp(0.09f, 0.85f, beamAngle / 55f);
            _ticker.PlayOneShot(_tickClip, Mathf.Lerp(0.7f, 0.25f, beamAngle / 55f));
        }

        bool playerLit = Time.time > _caughtDebounceUntil && LitByBeam(_player.Rigidbody.position);
        if (playerLit && _playerLitSince < 0f) _playerLitSince = Time.time;
        if (!playerLit) _playerLitSince = -1f;

        if (playerLit)
            _whiteAlpha = Mathf.Clamp01((Time.time - _playerLitSince) / caughtGraceSeconds);
        else
            _whiteAlpha = Mathf.MoveTowards(_whiteAlpha, 0f, Time.deltaTime * 1.1f);
        if (_white != null) _white.color = new Color(1f, 0.95f, 0.85f, _whiteAlpha);

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
            _player.Rigidbody.position = new Vector3(x, DuneHeight(x, z) + 2f, z);
            _player.Rigidbody.velocity = Vector3.zero;
        }
    }

    bool LitByBeam(Vector3 worldPos)
    {
        Vector3 to = worldPos - _beamHead.position;
        to.y = 0f;
        if (to.sqrMagnitude < 1f || to.magnitude > 380f) return false;
        Vector3 fwd = _beamHead.forward;
        fwd.y = 0f;
        return Vector3.Angle(fwd, to) < beamHalfAngle;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Crane beam")]
    [Tooltip("Sweep speed in degrees/second.")]
    public float beamSweepSpeed = 28f;
    [Tooltip("Half-angle (degrees) of the 'you are seen' cone.")]
    public float beamHalfAngle = 7f;
    [Tooltip("Seconds you can stand in the beam before the yard rearranges you.")]
    public float caughtGraceSeconds = 0.9f;

    [Header("Exit barrels")]
    [Tooltip("Min relocation distance from the player.")]
    public float barrelMinDistance = 45f;
    [Tooltip("Max relocation distance from the player.")]
    public float barrelMaxDistance = 130f;

    [Header("Exit")]
    [Tooltip("Scene the burning barrels lead to.")]
    public string nextScene = "D23_WheatAtDusk";
}
