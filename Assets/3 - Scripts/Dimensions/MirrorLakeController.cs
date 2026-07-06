using UnityEngine;

/// <summary>
/// D12 "Mirror Lake": a glassy black water floor under stars — and "reflected" stars
/// glinting on the surface below you, so the sky reads doubled. Low stone islands
/// drift to new positions whenever they leave your view; only the one carrying the
/// glowing crystal hums (gaze-reactive), and its crystal is the way out.
/// </summary>
public class MirrorLakeController : MonoBehaviour
{
    Transform _root;
    Transform[] _islands;
    ObservationTracker[] _trackers;
    AudioSource _trueHum;
    readonly System.Collections.Generic.List<Transform> _twinkles = new System.Collections.Generic.List<Transform>();
    Transform _shootingStar;
    Vector3 _shootDir;
    float _shootUntil = -1f;
    float _nextShootTime;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        var water = DimensionSceneUtil.Mat(new Color(0.01f, 0.015f, 0.03f), 0.97f);
        water.SetFloat("_Metallic", 0.85f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Lake",
            new Vector3(0f, -0.5f, 0f), new Vector3(1400f, 1f, 1400f), water, _root);
        DimensionSceneUtil.CreateDirectionalLight(new Color(0.5f, 0.6f, 0.9f), 0.35f, new Vector3(40f, -55f, 0f), false);

        BuildStars();

        _islands = new Transform[islandCount];
        _trackers = new ObservationTracker[islandCount];
        for (int i = 0; i < islandCount; i++)
        {
            _islands[i] = BuildIsland(i == 0, i).transform;
            _trackers[i] = new ObservationTracker();
            PlaceIsland(i, Vector3.zero, initial: true);
        }

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(58f, 3f, 0.05f), 600f, 1f);
    }

    // Sky stars overhead + still "reflections" lying on the black water — the doubled
    // starfield illusion without any actual reflection rendering.
    void BuildStars()
    {
        var starMat = DimensionSceneUtil.EmissiveMat(new Color(0.85f, 0.9f, 1f), 2f);
        var dimMat  = DimensionSceneUtil.EmissiveMat(new Color(0.5f, 0.6f, 0.85f), 0.9f);
        var stars = new GameObject("Stars");
        stars.transform.SetParent(_root, false);
        Random.State prev = Random.state;
        Random.InitState(4242);
        for (int i = 0; i < 260; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y) * 0.9f + 0.08f;
            var s = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Star",
                dir.normalized * Random.Range(380f, 460f), Vector3.one * Random.Range(0.9f, 2.4f),
                starMat, stars.transform);
            Destroy(s.GetComponent<Collider>());
        }
        // Faint nebula washes behind the stars — huge translucent tinted spheres.
        Color[] nebula = { new Color(0.3f, 0.4f, 1f, 0.05f), new Color(0.7f, 0.3f, 0.9f, 0.045f), new Color(0.2f, 0.8f, 0.9f, 0.04f) };
        for (int i = 0; i < nebula.Length; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y) * 0.6f + 0.25f;
            var n = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Nebula",
                dir.normalized * 470f, Vector3.one * Random.Range(140f, 220f),
                DimensionSceneUtil.FadeMat(nebula[i]), stars.transform);
            Destroy(n.GetComponent<Collider>());
        }
        for (int i = 0; i < 150; i++)
        {
            float a = Random.value * Mathf.PI * 2f, d = Mathf.Sqrt(Random.value) * 260f;
            var g = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Glint",
                new Vector3(Mathf.Cos(a) * d, 0.02f, Mathf.Sin(a) * d),
                new Vector3(Random.Range(0.25f, 0.6f), 0.01f, Random.Range(0.25f, 0.6f)),
                dimMat, stars.transform);
            Destroy(g.GetComponent<Collider>());
            if (i % 4 == 0) _twinkles.Add(g.transform);
        }
        Random.state = prev;

        // One pooled shooting star, re-aimed every so often.
        var shoot = DimensionSceneUtil.Block(PrimitiveType.Cube, "ShootingStar",
            Vector3.zero, new Vector3(0.4f, 0.4f, 14f), DimensionSceneUtil.EmissiveMat(new Color(0.9f, 0.95f, 1f), 2.6f), stars.transform);
        Destroy(shoot.GetComponent<Collider>());
        _shootingStar = shoot.transform;
        shoot.SetActive(false);
        _nextShootTime = Time.time + 5f;
    }

    GameObject BuildIsland(bool isTrue, int index)
    {
        var stone = DimensionSceneUtil.Mat(new Color(0.13f, 0.15f, 0.19f), 0.15f);
        var island = new GameObject(isTrue ? "Island_TRUE" : "Island" + index);
        island.transform.SetParent(_root, false);
        // Low flat slabs (boxes, not spheres — sphere colliders can't flatten).
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Slab",
            new Vector3(0f, 0.3f, 0f), new Vector3(7f, 0.9f, 7f), stone, island.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Slab2",
            new Vector3(1.8f, 0.55f, -1.2f), new Vector3(3.5f, 0.6f, 3.5f), stone, island.transform)
            .transform.localRotation = Quaternion.Euler(0f, 30f, 0f);
        var rock = DimensionSceneUtil.Block(PrimitiveType.Cube, "Rock",
            new Vector3(-2f, 1f, 1.6f), new Vector3(1.2f, 1.3f, 1f), stone, island.transform);
        rock.transform.localRotation = Quaternion.Euler(15f, 45f, 10f);

        // Every island carries a crystal — but the decoys' are dead, dark glass.
        // From a distance they all look like candidates; only the ear tells truth.
        if (!isTrue)
        {
            var deadMat = DimensionSceneUtil.Mat(new Color(0.10f, 0.14f, 0.18f), 0.7f);
            var dead = DimensionSceneUtil.Block(PrimitiveType.Cube, "DeadCrystal",
                new Vector3(0f, 1.7f, 0f), new Vector3(0.5f, 1.7f, 0.5f), deadMat, island.transform);
            dead.transform.localRotation = Quaternion.Euler(0f, 45f, 10f);
            Destroy(dead.GetComponent<Collider>());
        }

        if (isTrue)
        {
            var crystalMat = DimensionSceneUtil.EmissiveMat(new Color(0.45f, 0.9f, 1f), 2.6f);
            var crystal = DimensionSceneUtil.Block(PrimitiveType.Cube, "Crystal",
                new Vector3(0f, 1.9f, 0f), new Vector3(0.55f, 2f, 0.55f), crystalMat, island.transform);
            crystal.transform.localRotation = Quaternion.Euler(0f, 45f, 8f);
            Destroy(crystal.GetComponent<Collider>());
            var lightGo = new GameObject("CrystalLight");
            lightGo.transform.SetParent(island.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point; l.range = 16f; l.intensity = 2.4f;
            l.color = new Color(0.5f, 0.85f, 1f);
            _trueHum = DimensionSceneUtil.LoopingAudio(island, DimensionSceneUtil.ToneClip(440f, 2f, 0.45f), 300f, 1f);
            DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1.9f, 0f),
                new Vector3(1.6f, 2.4f, 1.6f), LevelPortal.PortalAction.EnterInterior, nextScene, island.transform);
        }
        return island;
    }

    void Update()
    {
        var cam = ObserverState.Cam;
        if (cam == null) return;
        if (!_atmosApplied)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.08f, 0.10f, 0.16f),
                fog: new Color(0.01f, 0.015f, 0.035f), fogDensity: 0.004f,
                background: new Color(0.004f, 0.006f, 0.015f));
            _atmosApplied = true;
        }

        for (int i = 0; i < _islands.Length; i++)
        {
            var b = new Bounds(_islands[i].position + Vector3.up * 1.2f, new Vector3(9f, 4f, 9f));
            _trackers[i].Tick(b, out bool justLost, float.PositiveInfinity);
            if (justLost) PlaceIsland(i, cam.transform.position, initial: false);
        }

        if (_trueHum != null)
        {
            Vector3 to = _islands[0].position - cam.transform.position;
            float align = Vector3.Dot(cam.transform.forward, to.normalized);
            _trueHum.volume = Mathf.Lerp(0.06f, 1f, Mathf.InverseLerp(0.2f, 0.95f, align));
        }

        // Floor glints twinkle softly; every so often a star falls.
        for (int i = 0; i < _twinkles.Count; i++)
        {
            float tw = 0.7f + 0.3f * Mathf.Sin(Time.time * 2.2f + i * 2.4f);
            var t = _twinkles[i];
            var s = t.localScale;
            t.localScale = new Vector3(s.x, 0.01f * tw, s.z);
        }
        if (_shootUntil > 0f)
        {
            if (Time.time >= _shootUntil) { _shootingStar.gameObject.SetActive(false); _shootUntil = -1f; }
            else _shootingStar.position += _shootDir * 220f * Time.deltaTime;
        }
        else if (Time.time >= _nextShootTime)
        {
            _nextShootTime = Time.time + Random.Range(7f, 18f);
            _shootUntil = Time.time + 1.4f;
            float a = Random.value * Mathf.PI * 2f;
            Vector3 start = new Vector3(Mathf.Cos(a) * 260f, Random.Range(140f, 240f), Mathf.Sin(a) * 260f);
            _shootDir = (new Vector3(-start.x, start.y * 0.3f, -start.z).normalized + Vector3.down * 0.35f).normalized;
            _shootingStar.SetPositionAndRotation(start, Quaternion.LookRotation(_shootDir));
            _shootingStar.gameObject.SetActive(true);
        }
    }

    void PlaceIsland(int i, Vector3 aroundPos, bool initial)
    {
        Vector3 best = _islands[i].position;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float d = Random.Range(relocateMinDistance, relocateMaxDistance);
            Vector3 c = initial ? Vector3.zero : aroundPos;
            float x = Mathf.Clamp(c.x + Mathf.Cos(a) * d, -300f, 300f);
            float z = Mathf.Clamp(c.z + Mathf.Sin(a) * d, -300f, 300f);
            best = new Vector3(x, 0f, z);
            // Don't land on the player or overlap a sibling island.
            Vector3 flatP = best - aroundPos; flatP.y = 0f;
            if (!initial && flatP.magnitude < 12f) continue;
            bool overlaps = false;
            for (int k = 0; k < _islands.Length; k++)     // later siblings are null mid-Awake
                if (k != i && _islands[k] != null && (_islands[k].position - best).magnitude < 16f) overlaps = true;
            if (overlaps) continue;
            if (initial || !ObserverState.IsObserved(new Bounds(best + Vector3.up * 1.5f, new Vector3(10f, 5f, 10f))))
                break;
        }
        _islands[i].SetPositionAndRotation(best, Quaternion.Euler(0f, Random.value * 360f, 0f));
        _trackers[i].Reset();
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Islands")]
    [Tooltip("Total islands including the true crystal island.")]
    public int islandCount = 6;
    [Tooltip("Min distance from the player a relocating island may land.")]
    public float relocateMinDistance = 30f;
    [Tooltip("Max distance from the player a relocating island may land.")]
    public float relocateMaxDistance = 85f;

    [Header("Exit")]
    [Tooltip("Scene the crystal leads to.")]
    public string nextScene = "D13_Orchard";
}
