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
        public Transform visuals;           // crossed stalk cards, scaled to bow
        public BoxCollider col;             // tracks the bowed height so you can stand/ride on it
        public ObservationTracker tracker = new ObservationTracker(0.1f);
        public float openT;                 // 0 standing wall .. 1 fully parted
        public bool wasObserved;            // for the gaze-arrival (rising edge) drop trigger
        public bool dropping;               // true while snapping down, before it rises again
    }

    Clump[] _clumps;
    Transform _well;
    AudioSource _wellHum;
    readonly ObservationTracker _wellTracker = new ObservationTracker(0.4f);   // grace so a quick glance-away doesn't move the well
    Transform _windmill;
    Transform _windmillBlades;
    readonly ObservationTracker _windmillTracker = new ObservationTracker();
    Vector3[] _windmillAnchors;
    int _windmillAnchor;
    bool _atmosApplied;

    void Awake()
    {
        var root = transform;
        // Warm-tinted dirt shows where your gaze has parted the wheat.
        var soilMat  = DimensionSceneUtil.TexMat("d18_dirt", new Color(0.85f, 0.68f, 0.5f), new Vector2(400f, 400f), 0.05f);
        var stoneMat = DimensionSceneUtil.Mat(new Color(0.35f, 0.33f, 0.31f), 0.1f);
        // Stalk cards carry the wheat texture; tints keep the three-way field variation.
        Material[] wheatMats =
        {
            DimensionSceneUtil.TexMat("d23_wheat", new Color(1f, 0.92f, 0.72f), new Vector2(2f, 1f), 0.15f),
            DimensionSceneUtil.TexMat("d23_wheat", new Color(0.94f, 0.83f, 0.62f), new Vector2(2f, 1f), 0.15f),
            DimensionSceneUtil.TexMat("d23_wheat", new Color(1f, 0.98f, 0.8f), new Vector2(2f, 1f), 0.15f),
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
        well.transform.localScale = Vector3.one * 2f;   // the well read too small — double its size

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

        BuildFences(root, wellPos);
        BuildWindmill(root, wellPos);
        BuildFireflies(root);
        // Dusk crickets over the field (falls back to the old low hum).
        DimensionSceneUtil.AmbienceLoop2D(gameObject, "amb_d23", 75f, 0.05f, 0.55f);
    }

    /// <summary>Weathered fence lines at the field edges — framing only, never
    /// crossing the walkable field interior.</summary>
    void BuildFences(Transform root, Vector3 wellPos)
    {
        var wood = DimensionSceneUtil.TexMat("wood_worn", new Color(0.75f, 0.62f, 0.5f), Vector2.one, 0.05f);
        Random.State prev = Random.state;
        Random.InitState(2333);
        float edge = fieldSize * 0.5f + 3f;
        Vector3[] starts = { new Vector3(-edge, 0f, -edge), new Vector3(-edge, 0f, edge), new Vector3(edge, 0f, -edge) };
        Vector3[] dirs   = { Vector3.forward, Vector3.right, Vector3.right };
        for (int line = 0; line < 3; line++)
        {
            int posts = 10;
            for (int i = 0; i < posts; i++)
            {
                Vector3 p = starts[line] + dirs[line] * i * (fieldSize / (posts - 1));
                if ((p - wellPos).magnitude < 8f) continue;
                var post = DimensionSceneUtil.Block(PrimitiveType.Cube, "FencePost",
                    p + Vector3.up * 0.6f, new Vector3(0.16f, 1.2f, 0.16f), wood, root);
                post.transform.rotation = Quaternion.Euler(Random.Range(-5f, 5f), Random.value * 20f, Random.Range(-5f, 5f));
                if (i < posts - 1 && Random.value > 0.2f)
                {
                    float span = fieldSize / (posts - 1);
                    var rail = DimensionSceneUtil.Block(PrimitiveType.Cube, "FenceRail",
                        p + dirs[line] * span * 0.5f + Vector3.up * 0.95f,
                        new Vector3(0.06f, 0.1f, span), wood, root);
                    rail.transform.rotation = Quaternion.LookRotation(dirs[line]) * Quaternion.Euler(Random.Range(-2f, 2f), 0f, 0f);
                    Object.Destroy(rail.GetComponent<Collider>());
                }
            }
        }
        Random.state = prev;
    }

    /// <summary>A windmill on the field's horizon, blades turning slow. Look away
    /// long enough and it's standing somewhere else — a landmark that won't hold
    /// still, far outside the walkable wheat so it never touches traversal.</summary>
    void BuildWindmill(Transform root, Vector3 wellPos)
    {
        var wood = DimensionSceneUtil.TexMat("wood_worn", new Color(0.55f, 0.45f, 0.38f), new Vector2(1f, 3f), 0.05f);
        var anchors = new System.Collections.Generic.List<Vector3>();
        for (int k = 0; k < 4; k++)
        {
            float a = (45f + k * 90f) * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (fieldSize * 0.5f + 24f);
            if ((p - wellPos).magnitude < 18f) continue;   // never share a horizon spot with the well
            anchors.Add(p);
        }
        _windmillAnchors = anchors.ToArray();
        if (_windmillAnchors.Length == 0) return;

        var mill = new GameObject("Windmill");
        mill.transform.SetParent(root, false);
        var tower = DimensionSceneUtil.Block(PrimitiveType.Cube, "Tower",
            Vector3.zero, new Vector3(1.7f, 9f, 1.7f), wood, mill.transform);
        tower.transform.localPosition = new Vector3(0f, 4.5f, 0f);
        var head = DimensionSceneUtil.Block(PrimitiveType.Cube, "Head",
            Vector3.zero, new Vector3(2.1f, 2f, 2.1f), wood, mill.transform);
        head.transform.localPosition = new Vector3(0f, 9.9f, 0f);
        var blades = new GameObject("Blades");
        blades.transform.SetParent(mill.transform, false);
        blades.transform.localPosition = new Vector3(0f, 9.9f, 1.3f);
        for (int b = 0; b < 4; b++)
        {
            var blade = DimensionSceneUtil.Block(PrimitiveType.Cube, "Blade",
                Vector3.zero, new Vector3(0.55f, 5.2f, 0.1f), wood, blades.transform);
            blade.transform.localRotation = Quaternion.Euler(0f, 0f, b * 90f);
            blade.transform.localPosition = blade.transform.localRotation * new Vector3(0f, 2.4f, 0f);
            Object.Destroy(blade.GetComponent<Collider>());
        }
        _windmill = mill.transform;
        _windmillBlades = blades.transform;
        _windmillAnchor = Random.Range(0, _windmillAnchors.Length);
        PlaceWindmill();
    }

    void PlaceWindmill()
    {
        Vector3 p = _windmillAnchors[_windmillAnchor];
        _windmill.SetPositionAndRotation(p, Quaternion.LookRotation(-p.normalized, Vector3.up));
        _windmillTracker.Reset();
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
                c.dropping = false; c.wasObserved = false;
                continue;
            }

            var b = new Bounds(new Vector3(pos.x, wheatHeight * 0.5f, pos.z),
                new Vector3(clumpSpacing * 0.98f, wheatHeight, clumpSpacing * 0.98f));
            bool observed = c.tracker.Tick(b, out _, partMaxDistance);

            // Gaze ARRIVING knocks the clump down (a quick drop); then it rises back
            // up on its own — even while you keep looking — over a duration that
            // scales with distance: near wheat lingers down (~nearRiseSeconds), far
            // wheat snaps back almost at once (~farRiseSeconds). Sweep your gaze to
            // keep dropping fresh wheat; the path closes behind you.
            if (observed && !c.wasObserved) c.dropping = true;
            c.wasObserved = observed;

            float next;
            if (c.dropping)
            {
                next = Mathf.MoveTowards(c.openT, 1f, Time.deltaTime / Mathf.Max(0.02f, dropSeconds));
                if (next >= 0.999f) c.dropping = false;
            }
            else
            {
                float dist = flat.magnitude;
                float riseDur = Mathf.Lerp(nearRiseSeconds, farRiseSeconds, Mathf.Clamp01(dist / partMaxDistance));
                next = Mathf.MoveTowards(c.openT, 0f, Time.deltaTime / Mathf.Max(0.05f, riseDur));
            }
            if (!Mathf.Approximately(next, c.openT)) SetClump(c, next);
        }

        if (_wellHum != null)
        {
            Vector3 to = _well.position - camPos;
            float align = Vector3.Dot(cam.transform.forward, to.normalized);
            _wellHum.volume = Mathf.Lerp(0.06f, 1f, Mathf.InverseLerp(0.2f, 0.95f, align));
        }

        if (_windmill != null)
        {
            _windmillBlades.Rotate(0f, 0f, 9f * Time.deltaTime, Space.Self);
            var wb = new Bounds(_windmill.position + Vector3.up * 6f, new Vector3(9f, 14f, 9f));
            _windmillTracker.Tick(wb, out bool wLost, float.PositiveInfinity);
            if (wLost && _windmillAnchors.Length > 1 && Random.value < 0.3f)
            {
                int next = Random.Range(0, _windmillAnchors.Length - 1);
                if (next >= _windmillAnchor) next++;
                _windmillAnchor = next;
                PlaceWindmill();
            }
        }

        // The exit well wanders: when it leaves your view it hops to a fresh spot.
        // This is a frustum test (grass blocking it still counts as looking), so you
        // can keep it framed through the wheat and chase it down.
        if (_well != null)
        {
            var wb = new Bounds(_well.position + Vector3.up * 3f, new Vector3(8f, 8f, 8f));
            _wellTracker.Tick(wb, out bool wellLost, float.PositiveInfinity);
            if (wellLost) RelocateWell();
        }
    }

    // Hops the well to a fresh spot in the field whenever it leaves view. Keeps it
    // inside the field and never drops it on top of the player.
    void RelocateWell()
    {
        if (_well == null) return;
        Vector3 cur = _well.position; cur.y = 0f;
        float maxR = fieldSize * 0.45f;
        var cam = ObserverState.Cam;
        Vector3 playerFlat = Vector3.zero;
        if (cam != null) { playerFlat = cam.transform.position; playerFlat.y = 0f; }
        for (int attempt = 0; attempt < 10; attempt++)
        {
            float ang = Random.value * Mathf.PI * 2f;
            float hop = Random.Range(wellShiftMin, wellShiftMax);
            Vector3 cand = cur + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * hop;
            if (cand.magnitude > maxR) cand = cand.normalized * maxR;                     // stay inside the field
            if (cam != null && (cand - playerFlat).magnitude < wellMinPlayerDist) continue; // not on top of the player
            _well.position = cand;
            _wellTracker.Reset();
            return;
        }
        _wellTracker.Reset();   // couldn't find a spot — re-arm and try again next look-away
    }

    void SetClump(Clump c, float openT)
    {
        c.openT = openT;
        // The stalks BOW: they squash toward the ground as you watch them part.
        float h = Mathf.Lerp(1f, 0.07f, openT);
        c.visuals.localScale = new Vector3(1f, h, 1f);
        // Collider tracks the current standing height: a full-height solid wall when
        // up (you can't run through it), shrinking as it bows down and growing back
        // as it rises — so a player standing on the flat top is carried up with it.
        // Only a mostly-flat clump drops its collider, so the gaze-parted path stays
        // walkable.
        float colH = wheatHeight * h;
        c.col.size = new Vector3(clumpSpacing * 0.98f, colH, clumpSpacing * 0.98f);
        c.col.center = new Vector3(0f, colH * 0.5f, 0f);
        c.col.enabled = colH > standWalkoverHeight;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Field")]
    [Tooltip("Field edge length (metres). 240 ≈ 10x the wheat of the original 72; drop to 200 for ~7x, 170 for ~5x if it hitches on load or FPS.")]
    public float fieldSize = 240f;
    [Tooltip("Clump grid pitch (metres).")]
    public float clumpSpacing = 4f;
    [Tooltip("Standing wheat wall height — deliberately over jump height.")]
    public float wheatHeight = 2.8f;
    [Tooltip("Seconds for gaze-hit wheat to snap DOWN (the quick drop).")]
    public float dropSeconds = 0.25f;
    [Tooltip("Seconds for NEAR wheat to rise back up — slow, lingers open.")]
    public float nearRiseSeconds = 10f;
    [Tooltip("Seconds for FAR wheat (at partMaxDistance) to rise back up — fast, snaps shut.")]
    public float farRiseSeconds = 0.5f;
    [Tooltip("Wheat further than this doesn't animate (and stands solid).")]
    public float partMaxDistance = 45f;
    [Tooltip("A clump bowed below this height (metres) drops its collider so you can walk over the parted path. Above it the clump is solid and can carry you up as it rises. 0 = always solid.")]
    public float standWalkoverHeight = 0.5f;

    [Header("Exit")]
    [Tooltip("Distance from spawn to the well's starting spot.")]
    public float wellDistance = 55f;
    [Tooltip("Scene the well leads to.")]
    public string nextScene = "D24_WaitingRoom";
    [Tooltip("When the well leaves view it hops this far (min/max metres).")]
    public float wellShiftMin = 15f;
    public float wellShiftMax = 45f;
    [Tooltip("Never relocate the well closer than this to the player.")]
    public float wellMinPlayerDist = 20f;
}
