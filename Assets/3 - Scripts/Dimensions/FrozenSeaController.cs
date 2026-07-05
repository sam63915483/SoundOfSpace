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
    Transform _hatch;
    Renderer _hatchRend;
    Collider _hatchCol;
    GameObject _portalGo;
    MaterialPropertyBlock _mpb;
    float _beamYaw;
    float _hatchAlpha = 1f;
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
        var seaMat   = DimensionSceneUtil.Mat(new Color(0.05f, 0.08f, 0.12f), 0.85f);
        var towerMat = DimensionSceneUtil.Mat(new Color(0.55f, 0.52f, 0.5f), 0.1f);
        var hatchMat = DimensionSceneUtil.FadeMat(new Color(0.45f, 0.9f, 0.6f, 1f));

        BuildSea(seaMat);
        DimensionSceneUtil.CreateDirectionalLight(new Color(0.4f, 0.5f, 0.7f), 0.22f, new Vector3(35f, -60f, 0f), false);

        // Lighthouse tower + rotating head with a spotlight and a visible emissive beam arm.
        var tower = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Lighthouse",
            new Vector3(0f, 15f, 140f), new Vector3(6f, 15f, 6f), towerMat, _root);
        var head = new GameObject("BeamHead");
        head.transform.SetParent(_root, false);
        head.transform.position = new Vector3(0f, 29f, 140f);
        _beamHead = head.transform;
        var spot = head.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.range = 400f; spot.spotAngle = 12f; spot.intensity = 9f;
        spot.color = new Color(1f, 0.95f, 0.8f);
        var beamArm = DimensionSceneUtil.Block(PrimitiveType.Cube, "BeamArm",
            Vector3.zero, new Vector3(0.5f, 0.5f, 60f),
            DimensionSceneUtil.EmissiveMat(new Color(1f, 0.95f, 0.7f), 1.2f), _beamHead);
        beamArm.transform.localPosition = new Vector3(0f, 0f, 30f);
        Destroy(beamArm.GetComponent<Collider>());

        // The exit hatch: fixed spot on the sea, marked by a small green lamp. Solid
        // (and enterable) only while the beam is looking elsewhere.
        float a = Random.value * Mathf.PI * 2f;
        float d = Random.Range(40f, 90f);
        Vector3 hp = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
        hp.y = SeaHeight(hp.x, hp.z) + 0.15f;
        var hatchGo = DimensionSceneUtil.Block(PrimitiveType.Cube, "ExitHatch",
            hp, new Vector3(2.4f, 0.3f, 2.4f), hatchMat, _root);
        _hatch = hatchGo.transform;
        _hatchRend = hatchGo.GetComponent<Renderer>();
        _hatchCol = hatchGo.GetComponent<Collider>();
        var marker = DimensionSceneUtil.Block(PrimitiveType.Sphere, "HatchLamp",
            hp + Vector3.up * 1.2f, Vector3.one * 0.35f,
            DimensionSceneUtil.EmissiveMat(new Color(0.3f, 1f, 0.5f), 2.5f), _root);
        Destroy(marker.GetComponent<Collider>());
        var ml = marker.AddComponent<Light>();
        ml.type = LightType.Point; ml.range = 10f; ml.intensity = 1.5f; ml.color = new Color(0.3f, 1f, 0.5f);
        _portalGo = DimensionSceneUtil.CreatePortal("ToNext", hp + Vector3.up * 0.6f,
            new Vector3(2f, 1.2f, 2f), LevelPortal.PortalAction.EnterInterior, nextScene, _root);

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(45f, 2f, 0.07f), 600f, 1f);
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

        // Sweep the beam.
        _beamYaw += beamSweepSpeed * Time.deltaTime;
        _beamHead.rotation = Quaternion.Euler(6f, _beamYaw, 0f);

        // Hatch: melts away while the beam is ON it.
        bool hatchLit = LitByBeam(_hatch.position);
        float targetAlpha = hatchLit ? 0.05f : 1f;
        _hatchAlpha = Mathf.MoveTowards(_hatchAlpha, targetAlpha, Time.deltaTime / 0.3f);
        _mpb.SetColor(ColorId, new Color(0.45f, 0.9f, 0.6f, _hatchAlpha));
        _hatchRend.SetPropertyBlock(_mpb);
        _hatchCol.enabled = !hatchLit;
        _portalGo.SetActive(!hatchLit);

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
        if (playerLit && Time.time - _playerLitSince > caughtGraceSeconds)
        {
            _playerLitSince = -1f;
            _caughtDebounceUntil = Time.time + 3f;
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
    [Tooltip("Seconds you can be in the beam before it relocates you.")]
    public float caughtGraceSeconds = 0.5f;

    [Header("Exit")]
    [Tooltip("Scene the hatch leads to.")]
    public string nextScene = "D7_HallOfDoors";
}
