using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// D25 "Candle Sea": a pitch-black plain carpeted with hundreds of small flames.
/// Wherever you LOOK, the candles gutter out; they only relight in the dark behind
/// you (the inverse rule, painted in light). Every flame you stare at dies — except
/// one. The candle that never goes out is the way home. Walk toward the light you
/// cannot kill.
/// </summary>
public class CandleSeaController : MonoBehaviour
{
    class CandleCell
    {
        public Vector3 center;
        public readonly List<Transform> flames = new List<Transform>();
        public readonly ObservationTracker tracker = new ObservationTracker(0.1f);
        public float litT = 1f;             // 1 burning .. 0 snuffed
        public bool wasObserved;            // for the gaze-arrival (rising edge) snuff trigger
        public bool snuffing;               // true while guttering out, before it relights
    }

    CandleCell[] _cells;
    Transform _trueCandle;
    Transform _trueFlame;
    AudioSource _snuffer;
    AudioClip _snuffClip;
    bool _atmosApplied;
    // Unseen-growth wax stalagmites.
    Transform[] _stalagmites;
    ObservationTracker[] _stalagTrackers;
    const float StalagMaxYScale = 1.6f;
    // Global gate so a fast pan across the field can't machine-gun the hiss.
    float _nextSnuffAllowed;

    void Awake()
    {
        var root = transform;
        // Wax-pool floor texture, tinted well down so the plain still reads
        // near-black until candlelight catches it.
        var groundMat = DimensionSceneUtil.TexMat("d25_wax", new Color(0.30f, 0.26f, 0.21f), new Vector2(1500f / 2f, 1500f / 2f), 0.3f);
        var waxMat    = DimensionSceneUtil.Mat(new Color(0.28f, 0.24f, 0.18f), 0.2f);
        var flameMat  = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.62f, 0.2f), 2.6f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Ground",
            new Vector3(0f, -0.5f, 0f), new Vector3(1500f, 1f, 1500f), groundMat, root);

        // The field of flames, gathered into observation cells.
        Random.State prev = Random.state;
        Random.InitState(2525);
        var cells = new List<CandleCell>();
        int perSide = Mathf.RoundToInt(fieldSize / cellPitch);
        for (int ix = 0; ix <= perSide; ix++)
            for (int iz = 0; iz <= perSide; iz++)
            {
                Vector3 center = new Vector3((ix - perSide * 0.5f) * cellPitch, 0f, (iz - perSide * 0.5f) * cellPitch);
                if (center.magnitude < 5f) continue;
                var cell = new CandleCell { center = center };
                // Candles huddle in little congregations around a shared wax pool.
                Vector3 cluster = center + new Vector3(
                    Random.Range(-cellPitch * 0.25f, cellPitch * 0.25f), 0f,
                    Random.Range(-cellPitch * 0.25f, cellPitch * 0.25f));
                var pool = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "WaxPool",
                    cluster + Vector3.up * 0.015f,
                    new Vector3(Random.Range(0.9f, 1.6f), 0.012f, Random.Range(0.9f, 1.6f)),
                    DimensionSceneUtil.Mat(new Color(0.16f, 0.13f, 0.10f), 0.45f), root);
                Object.Destroy(pool.GetComponent<Collider>());
                int count = Random.Range(4, 8);
                for (int k = 0; k < count; k++)
                {
                    float ca = Random.value * Mathf.PI * 2f;
                    Vector3 pos = cluster + new Vector3(Mathf.Cos(ca), 0f, Mathf.Sin(ca)) * Random.Range(0.1f, 1.1f);
                    float h = Random.Range(0.12f, 0.55f);
                    var wax = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Wax",
                        pos + Vector3.up * h * 0.5f, new Vector3(Random.Range(0.07f, 0.12f), h * 0.5f, Random.Range(0.07f, 0.12f)), waxMat, root);
                    Object.Destroy(wax.GetComponent<Collider>());
                    var flame = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Flame",
                        pos + Vector3.up * (h + 0.09f), new Vector3(0.12f, 0.2f, 0.12f), flameMat, root);
                    Object.Destroy(flame.GetComponent<Collider>());
                    cell.flames.Add(flame.transform);
                }
                cells.Add(cell);
            }
        _cells = cells.ToArray();
        Random.state = prev;

        // The one that never goes out — a shrine candle, taller and golden.
        float a = Random.value * Mathf.PI * 2f;
        Vector3 truePos = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * trueCandleDistance;
        var candle = new GameObject("Candle_TRUE");
        candle.transform.SetParent(root, false);
        _trueCandle = candle.transform;
        // Its shrine: a low stone dais ringed by kneeling-stones.
        var daisMat = DimensionSceneUtil.TexMat("d25_stone", new Color(0.55f, 0.52f, 0.48f), new Vector2(2f, 2f), 0.3f);
        var dais = DimensionSceneUtil.Block(PrimitiveType.Cube, "Dais",
            new Vector3(0f, 0.1f, 0f), new Vector3(2.6f, 0.2f, 2.6f), daisMat, candle.transform);
        for (int k = 0; k < 6; k++)
        {
            float sa = k * Mathf.PI / 3f;
            DimensionSceneUtil.Block(PrimitiveType.Cube, "ShrineStone",
                new Vector3(Mathf.Cos(sa) * 2.1f, 0.3f, Mathf.Sin(sa) * 2.1f),
                new Vector3(0.5f, 0.6f, 0.4f), daisMat, candle.transform)
                .transform.localRotation = Quaternion.Euler(0f, -sa * Mathf.Rad2Deg, 0f);
        }
        DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Wax",
            new Vector3(0f, 0.8f, 0f), new Vector3(0.22f, 0.6f, 0.22f), waxMat, candle.transform);
        var trueFlame = DimensionSceneUtil.Block(PrimitiveType.Sphere, "TrueFlame",
            new Vector3(0f, 1.62f, 0f), new Vector3(0.26f, 0.44f, 0.26f),
            DimensionSceneUtil.EmissiveMat(new Color(1f, 0.85f, 0.4f), 3.2f), candle.transform);
        Object.Destroy(trueFlame.GetComponent<Collider>());
        _trueFlame = trueFlame.transform;
        var lg = new GameObject("TrueLight");
        lg.transform.SetParent(candle.transform, false);
        lg.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = 14f; l.intensity = 2f;
        l.color = new Color(1f, 0.75f, 0.4f);
        lg.AddComponent<FlickerLight>();
        DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1f, 0f),
            new Vector3(1.6f, 2f, 1.6f), LevelPortal.PortalAction.EnterInterior, nextScene, candle.transform);
        candle.transform.position = truePos;

        // Vast cavern air (falls back to the old 50Hz rumble) + a choir so far
        // away you're never sure it's there.
        DimensionSceneUtil.AmbienceLoop2D(gameObject, "amb_d25", 50f, 0.05f, 0.5f);
        DimensionSceneUtil.AmbienceLoop2D(gameObject, "mus_d25_choir", 55f, 0f, 0.18f);

        // Wax stalagmites out in the field — they only grow while unobserved.
        BuildStalagmites(truePos);

        // One roaming voice for the snuff-puff — it hops to whichever cluster dies.
        var libSnuff = DimensionAssetLibrary.Clip("sfx_wax_hiss");
        _snuffClip = libSnuff != null ? libSnuff : SnuffClip();
        var snuffGo = new GameObject("Snuffer");
        snuffGo.transform.SetParent(root, false);
        _snuffer = snuffGo.AddComponent<AudioSource>();
        _snuffer.spatialBlend = 1f;
        _snuffer.rolloffMode = AudioRolloffMode.Linear;
        _snuffer.maxDistance = 40f;
    }

    // Squat wax mounds scattered off the traversal lines; each gains ~15%
    // height every time it leaves your view, capped before it reads as a
    // figure. No relocation, no sound — you just slowly stop trusting them.
    void BuildStalagmites(Vector3 truePos)
    {
        var stalagMat = DimensionSceneUtil.TexMat("d25_wax", new Color(0.62f, 0.55f, 0.44f), Vector2.one, 0.25f);
        _stalagmites = new Transform[10];
        _stalagTrackers = new ObservationTracker[10];
        for (int i = 0; i < 10; i++)
        {
            Vector3 p = Vector3.zero;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                float sa = Random.value * Mathf.PI * 2f;
                p = new Vector3(Mathf.Cos(sa), 0f, Mathf.Sin(sa)) * Random.Range(15f, 55f);
                if ((p - truePos).sqrMagnitude > 100f) break;   // keep the shrine approach clean
            }
            var stalag = new GameObject("WaxStalagmite");
            stalag.layer = DimensionSceneUtil.WalkableLayer;
            stalag.transform.SetParent(transform, false);
            // Uniform-x/z cylinders, colliders dropped; one box on the root
            // instead (the capsule-balloon trap).
            var b0 = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Base",
                new Vector3(0f, 0.35f, 0f), new Vector3(1.0f, 0.35f, 1.0f), stalagMat, stalag.transform);
            var b1 = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Mid",
                new Vector3(0f, 0.85f, 0f), new Vector3(0.6f, 0.3f, 0.6f), stalagMat, stalag.transform);
            var b2 = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Tip",
                new Vector3(0f, 1.3f, 0f), new Vector3(0.3f, 0.26f, 0.3f), stalagMat, stalag.transform);
            Object.Destroy(b0.GetComponent<Collider>());
            Object.Destroy(b1.GetComponent<Collider>());
            Object.Destroy(b2.GetComponent<Collider>());
            var box = stalag.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.75f, 0f);
            box.size = new Vector3(0.9f, 1.5f, 0.9f);
            stalag.transform.position = p;
            stalag.transform.localScale = new Vector3(1f, Random.Range(0.5f, 0.8f), 1f);
            _stalagmites[i] = stalag.transform;
            _stalagTrackers[i] = new ObservationTracker();
        }
    }

    // A short breathy "fft" — air taking a flame.
    static AudioClip SnuffClip()
    {
        int rate = 44100;
        int samples = (int)(rate * 0.28f);
        var data = new float[samples];
        var rng = new System.Random(2525);
        float v = 0f;
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            v = Mathf.Lerp(v, (float)(rng.NextDouble() * 2.0 - 1.0), 0.5f);
            data[i] = v * Mathf.Exp(-t * 16f) * 0.5f;
        }
        var clip = AudioClip.Create("snuff", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void Update()
    {
        var cam = ObserverState.Cam;
        if (cam == null) return;
        if (!_atmosApplied)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.06f, 0.05f, 0.045f),
                fog: new Color(0.008f, 0.006f, 0.005f), fogDensity: 0.010f,
                background: new Color(0.004f, 0.003f, 0.003f));
            _atmosApplied = true;
        }

        Vector3 camPos = cam.transform.position;
        foreach (var cell in _cells)
        {
            Vector3 flat = cell.center - camPos; flat.y = 0f;
            if (flat.sqrMagnitude > snuffMaxDistance * snuffMaxDistance)
            {
                cell.snuffing = false; cell.wasObserved = false;
                continue;
            }

            var b = new Bounds(cell.center + Vector3.up * 0.3f, new Vector3(cellPitch, 1f, cellPitch));
            bool observed = cell.tracker.Tick(b, out _, snuffMaxDistance);

            // Gaze ARRIVING snuffs the cluster; then it comes back ON on its own —
            // even while you keep looking — over a duration that scales with distance:
            // NEAR clusters relight LATER (stay dark around you ~nearRelightSeconds),
            // FAR clusters relight SOONER (~farRelightSeconds). A dark bubble follows you.
            if (observed && !cell.wasObserved)
            {
                cell.snuffing = true;
                // The cluster starting to die exhales audibly (rate-gated so a sweep
                // of the horizon doesn't machine-gun the hiss).
                if (cell.litT >= 0.9f && _snuffer != null && Time.time >= _nextSnuffAllowed)
                {
                    _nextSnuffAllowed = Time.time + 0.45f;
                    _snuffer.transform.position = cell.center + Vector3.up * 0.4f;
                    _snuffer.pitch = Random.Range(0.85f, 1.15f);
                    _snuffer.PlayOneShot(_snuffClip, 0.55f);
                }
            }
            cell.wasObserved = observed;

            float next;
            if (cell.snuffing)
            {
                next = Mathf.MoveTowards(cell.litT, 0f, Time.deltaTime / Mathf.Max(0.05f, snuffSeconds));
                if (next <= 0.001f) cell.snuffing = false;
            }
            else
            {
                float dist = flat.magnitude;
                float relightDur = Mathf.Lerp(nearRelightSeconds, farRelightSeconds, Mathf.Clamp01(dist / snuffMaxDistance));
                next = Mathf.MoveTowards(cell.litT, 1f, Time.deltaTime / Mathf.Max(0.05f, relightDur));
            }
            if (Mathf.Approximately(next, cell.litT)) continue;
            cell.litT = next;
            float s = Mathf.Lerp(0.01f, 1f, next);
            foreach (var f in cell.flames)
                f.localScale = new Vector3(0.12f * s, 0.2f * s, 0.12f * s);
        }

        // Stalagmites: each unobserved window adds height, toward the cap.
        if (_stalagmites != null)
            for (int i = 0; i < _stalagmites.Length; i++)
            {
                var s = _stalagmites[i];
                if (s == null) continue;
                var sb = new Bounds(s.position + Vector3.up * 1f, new Vector3(1.6f, 3f, 1.6f));
                _stalagTrackers[i].Tick(sb, out bool sLost, snuffMaxDistance);
                if (sLost)
                {
                    var ls = s.localScale;
                    ls.y = Mathf.Min(ls.y * 1.15f, StalagMaxYScale);
                    s.localScale = ls;
                }
            }

        // The true flame breathes but never dies.
        if (_trueFlame != null)
        {
            float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 5.1f);
            _trueFlame.localScale = new Vector3(0.26f * pulse, 0.44f * pulse, 0.26f * pulse);
        }
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Candles")]
    [Tooltip("Field edge length (metres). 276 ≈ 5x the candles of the original 120; drop toward 200/170 if it hitches on load or FPS (candles are heavier per cell than the wheat).")]
    public float fieldSize = 276f;
    [Tooltip("Candle cluster pitch (metres) — clusters snuff/relight together.")]
    public float cellPitch = 6f;
    [Tooltip("Seconds for a gaze-hit cluster to gutter OUT (the snuff).")]
    public float snuffSeconds = 0.8f;
    [Tooltip("Seconds for a NEAR cluster to come back ON — long, so it stays dark around you.")]
    public float nearRelightSeconds = 6f;
    [Tooltip("Seconds for a FAR cluster (at snuffMaxDistance) to come back ON — short, snaps back.")]
    public float farRelightSeconds = 0.5f;
    [Tooltip("Clusters further than this don't react (stay lit).")]
    public float snuffMaxDistance = 60f;

    [Header("Exit")]
    [Tooltip("Distance from spawn to the candle that never goes out.")]
    public float trueCandleDistance = 65f;
    [Tooltip("Scene the true candle leads to — the Backrooms hub ends the reel.")]
    public string nextScene = "R1_Backrooms";
}
