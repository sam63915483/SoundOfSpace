using UnityEngine;

/// <summary>
/// D23 "Wheat at Dusk" (v2): an over-head-height golden field under a purple sky,
/// fireflies drifting through it. The wheat is a solid wall everywhere except where
/// you LOOK — watched clumps bow down flat and part; unwatched wheat stands back up
/// behind you, too tall to jump. Your gaze carves the only path. Somewhere out there
/// a stone well hums when faced.
/// </summary>
public class WheatAtDuskController : MonoBehaviour
{
    class Clump
    {
        public Transform tf;                // invisible collider root
        public Collider col;
        public Transform visuals;           // crossed stalk cards, scaled to bow
        public ObservationTracker tracker = new ObservationTracker(0.1f);
        public float openT;                 // 0 standing wall .. 1 fully parted
    }

    Clump[] _clumps;
    Transform _well;
    AudioSource _wellHum;
    bool _atmosApplied;

    void Awake()
    {
        var root = transform;
        var soilMat  = DimensionSceneUtil.Mat(new Color(0.22f, 0.15f, 0.10f), 0.05f);
        var stoneMat = DimensionSceneUtil.Mat(new Color(0.35f, 0.33f, 0.31f), 0.1f);
        Material[] wheatMats =
        {
            DimensionSceneUtil.Mat(new Color(0.86f, 0.68f, 0.26f), 0.15f),
            DimensionSceneUtil.Mat(new Color(0.80f, 0.60f, 0.22f), 0.15f),
            DimensionSceneUtil.Mat(new Color(0.90f, 0.74f, 0.34f), 0.15f),
        };
        var headMat = DimensionSceneUtil.Mat(new Color(0.94f, 0.82f, 0.45f), 0.2f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Soil",
            new Vector3(0f, -0.5f, 0f), new Vector3(1200f, 1f, 1200f), soilMat, root);
        DimensionSceneUtil.CreateDirectionalLight(new Color(1f, 0.7f, 0.5f), 0.8f, new Vector3(14f, -120f, 0f), true);

        // A giant dusk sun sinking behind the field.
        var sun = DimensionSceneUtil.Block(PrimitiveType.Sphere, "DuskSun",
            new Vector3(210f, 26f, 320f), Vector3.one * 60f,
            DimensionSceneUtil.EmissiveMat(new Color(1f, 0.45f, 0.3f), 2.2f), root);
        Object.Destroy(sun.GetComponent<Collider>());

        // The well, out in a random direction.
        float wa = Random.value * Mathf.PI * 2f;
        Vector3 wellPos = new Vector3(Mathf.Cos(wa), 0f, Mathf.Sin(wa)) * wellDistance;
        var well = new GameObject("Well_TRUE");
        well.transform.SetParent(root, false);
        _well = well.transform;
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.PI / 4f;
            var rim = DimensionSceneUtil.Block(PrimitiveType.Cube, "Rim",
                new Vector3(Mathf.Cos(a) * 1.3f, 0.45f, Mathf.Sin(a) * 1.3f),
                new Vector3(1.05f, 0.9f, 0.5f), stoneMat, well.transform);
            rim.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
        }
        for (int side = -1; side <= 1; side += 2)
        {
            var post = DimensionSceneUtil.Block(PrimitiveType.Cube, "RoofPost",
                new Vector3(side * 1.5f, 1.5f, 0f), new Vector3(0.18f, 1.4f, 0.18f), stoneMat, well.transform);
            var roof = DimensionSceneUtil.Block(PrimitiveType.Cube, "Roof",
                new Vector3(side * 0.7f, 2.5f, 0f), new Vector3(1.6f, 0.12f, 2.2f), stoneMat, well.transform);
            roof.transform.localRotation = Quaternion.Euler(0f, 0f, side * -35f);
        }
        var hole = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Hole",
            new Vector3(0f, 0.3f, 0f), new Vector3(2.2f, 0.03f, 2.2f),
            DimensionSceneUtil.Mat(new Color(0.02f, 0.02f, 0.03f)), well.transform);
        Object.Destroy(hole.GetComponent<Collider>());
        // A warm lantern on the well roof — visible over the wheat once you're close.
        var lantern = DimensionSceneUtil.Block(PrimitiveType.Sphere, "WellLantern",
            new Vector3(0f, 2.9f, 0f), Vector3.one * 0.35f,
            DimensionSceneUtil.EmissiveMat(new Color(1f, 0.75f, 0.35f), 2.6f), well.transform);
        Object.Destroy(lantern.GetComponent<Collider>());
        var lg = new GameObject("WellLight");
        lg.transform.SetParent(well.transform, false);
        lg.transform.localPosition = new Vector3(0f, 3f, 0f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = 16f; l.intensity = 1.8f;
        l.color = new Color(1f, 0.75f, 0.4f);
        lg.AddComponent<FlickerLight>();
        _wellHum = DimensionSceneUtil.LoopingAudio(well, DimensionSceneUtil.ToneClip(494f, 2f, 0.45f), 260f, 1f);
        DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 0.5f, 0f),
            new Vector3(1.7f, 0.8f, 1.7f), LevelPortal.PortalAction.EnterInterior, nextScene, well.transform);
        well.transform.position = wellPos;

        // The field: invisible collider walls + crossed stalk-card visuals per clump.
        Random.State prev = Random.state;
        Random.InitState(2323);
        int perSide = Mathf.RoundToInt(fieldSize / clumpSpacing);
        var clumps = new System.Collections.Generic.List<Clump>();
        for (int ix = 0; ix <= perSide; ix++)
            for (int iz = 0; iz <= perSide; iz++)
            {
                Vector3 pos = new Vector3((ix - perSide * 0.5f) * clumpSpacing, 0f, (iz - perSide * 0.5f) * clumpSpacing);
                if (pos.magnitude < 6f) continue;
                if ((pos - wellPos).magnitude < 6.5f) continue;

                var clumpRoot = new GameObject("Wheat");
                clumpRoot.transform.SetParent(root, false);
                clumpRoot.transform.position = pos;
                clumpRoot.layer = DimensionSceneUtil.WalkableLayer;
                var box = clumpRoot.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, wheatHeight * 0.5f, 0f);
                box.size = new Vector3(clumpSpacing * 0.98f, wheatHeight, clumpSpacing * 0.98f);

                var visuals = new GameObject("Stalks");
                visuals.transform.SetParent(clumpRoot.transform, false);
                Material mat = wheatMats[(ix + iz * 3) % wheatMats.Length];
                float yaw = Random.Range(0f, 90f);
                for (int card = 0; card < 2; card++)
                {
                    var sheet = DimensionSceneUtil.Block(PrimitiveType.Cube, "Card",
                        Vector3.zero, Vector3.one, mat, visuals.transform);
                    sheet.transform.localPosition = new Vector3(0f, wheatHeight * 0.5f, 0f);
                    sheet.transform.localRotation = Quaternion.Euler(0f, yaw + card * 90f, 0f);
                    sheet.transform.localScale = new Vector3(clumpSpacing * 1.12f, wheatHeight, 0.16f);
                    Object.Destroy(sheet.GetComponent<Collider>());
                }
                // Seed heads: a paler fringe along the top.
                var head = DimensionSceneUtil.Block(PrimitiveType.Cube, "Heads",
                    Vector3.zero, Vector3.one, headMat, visuals.transform);
                head.transform.localPosition = new Vector3(0f, wheatHeight - 0.12f, 0f);
                head.transform.localRotation = Quaternion.Euler(0f, yaw + 45f, 0f);
                head.transform.localScale = new Vector3(clumpSpacing * 0.9f, 0.28f, clumpSpacing * 0.9f);
                Object.Destroy(head.GetComponent<Collider>());

                clumps.Add(new Clump { tf = clumpRoot.transform, col = box, visuals = visuals.transform });
            }
        _clumps = clumps.ToArray();
        Random.state = prev;

        BuildFireflies(root);
        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(75f, 3f, 0.05f), 500f, 1f);
    }

    ParticleSystem _fireflies;

    void BuildFireflies(Transform root)
    {
        var go = new GameObject("Fireflies");
        go.transform.SetParent(root, false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 8f;
        main.startSpeed = 0.15f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
        main.startColor = new Color(1f, 0.9f, 0.45f, 0.9f);
        main.maxParticles = 220;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 26f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 24f;
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        vel.y = new ParticleSystem.MinMaxCurve(-0.15f, 0.3f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        var colLife = ps.colorOverLifetime;
        colLife.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.2f), new GradientAlphaKey(0.1f, 0.6f), new GradientAlphaKey(0.8f, 0.8f), new GradientAlphaKey(0f, 1f) });
        colLife.color = grad;
        var psr = go.GetComponent<ParticleSystemRenderer>();
        var mat = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
        mat.SetColor("_TintColor", new Color(1f, 0.85f, 0.4f, 0.6f));
        psr.material = mat;
        _fireflies = ps;
    }

    void Update()
    {
        var cam = ObserverState.Cam;
        if (cam == null) return;
        if (!_atmosApplied)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.34f, 0.24f, 0.36f),
                fog: new Color(0.45f, 0.28f, 0.48f), fogDensity: 0.014f,
                background: new Color(0.32f, 0.16f, 0.40f));
            _atmosApplied = true;
        }

        Vector3 camPos = cam.transform.position;
        if (_fireflies != null)
            _fireflies.transform.position = new Vector3(camPos.x, 1.6f, camPos.z);

        foreach (var c in _clumps)
        {
            Vector3 pos = c.tf.position;
            Vector3 flat = pos - camPos; flat.y = 0f;
            if (flat.sqrMagnitude > partMaxDistance * partMaxDistance)
            {
                if (c.openT > 0f) SetClump(c, 0f);
                continue;
            }

            var b = new Bounds(new Vector3(pos.x, wheatHeight * 0.5f, pos.z),
                new Vector3(clumpSpacing * 0.98f, wheatHeight, clumpSpacing * 0.98f));
            bool observed = c.tracker.Tick(b, out _, partMaxDistance);
            float target = observed ? 1f : 0f;
            float t = Mathf.MoveTowards(c.openT, target, Time.deltaTime / (observed ? partSeconds : standSeconds));
            if (!Mathf.Approximately(t, c.openT)) SetClump(c, t);
        }

        if (_wellHum != null)
        {
            Vector3 to = _well.position - camPos;
            float align = Vector3.Dot(cam.transform.forward, to.normalized);
            _wellHum.volume = Mathf.Lerp(0.06f, 1f, Mathf.InverseLerp(0.2f, 0.95f, align));
        }
    }

    void SetClump(Clump c, float openT)
    {
        c.openT = openT;
        // The stalks BOW: they squash toward the ground as you watch them part.
        float h = Mathf.Lerp(1f, 0.07f, openT);
        c.visuals.localScale = new Vector3(1f, h, 1f);
        c.col.enabled = openT < 0.55f;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Field")]
    [Tooltip("Field edge length (metres).")]
    public float fieldSize = 72f;
    [Tooltip("Clump grid pitch (metres).")]
    public float clumpSpacing = 4f;
    [Tooltip("Standing wheat wall height — deliberately over jump height.")]
    public float wheatHeight = 2.8f;
    [Tooltip("Seconds for watched wheat to bow open.")]
    public float partSeconds = 0.7f;
    [Tooltip("Seconds for unwatched wheat to stand back up.")]
    public float standSeconds = 1.2f;
    [Tooltip("Wheat further than this doesn't animate (and stands solid).")]
    public float partMaxDistance = 45f;

    [Header("Exit")]
    [Tooltip("Distance from spawn to the well.")]
    public float wellDistance = 55f;
    [Tooltip("Scene the well leads to.")]
    public string nextScene = "D24_WaitingRoom";
}
