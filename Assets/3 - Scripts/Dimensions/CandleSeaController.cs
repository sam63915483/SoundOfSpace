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
    }

    CandleCell[] _cells;
    Transform _trueCandle;
    Transform _trueFlame;
    bool _atmosApplied;

    void Awake()
    {
        var root = transform;
        var groundMat = DimensionSceneUtil.Mat(new Color(0.015f, 0.013f, 0.012f), 0.35f);
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
                int count = Random.Range(3, 7);
                for (int k = 0; k < count; k++)
                {
                    Vector3 pos = center + new Vector3(
                        Random.Range(-cellPitch * 0.45f, cellPitch * 0.45f), 0f,
                        Random.Range(-cellPitch * 0.45f, cellPitch * 0.45f));
                    float h = Random.Range(0.15f, 0.45f);
                    var wax = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Wax",
                        pos + Vector3.up * h * 0.5f, new Vector3(0.09f, h * 0.5f, 0.09f), waxMat, root);
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

        // The one that never goes out — taller, golden, and the way home.
        float a = Random.value * Mathf.PI * 2f;
        Vector3 truePos = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * trueCandleDistance;
        var candle = new GameObject("Candle_TRUE");
        candle.transform.SetParent(root, false);
        _trueCandle = candle.transform;
        DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Wax",
            new Vector3(0f, 0.6f, 0f), new Vector3(0.22f, 0.6f, 0.22f), waxMat, candle.transform);
        var trueFlame = DimensionSceneUtil.Block(PrimitiveType.Sphere, "TrueFlame",
            new Vector3(0f, 1.42f, 0f), new Vector3(0.26f, 0.44f, 0.26f),
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

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(50f, 3f, 0.05f), 500f, 1f);
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
            if (flat.sqrMagnitude > snuffMaxDistance * snuffMaxDistance) continue;

            var b = new Bounds(cell.center + Vector3.up * 0.3f, new Vector3(cellPitch, 1f, cellPitch));
            bool observed = cell.tracker.Tick(b, out _, snuffMaxDistance);
            float target = observed ? 0f : 1f;
            float t = Mathf.MoveTowards(cell.litT, target, Time.deltaTime / (observed ? snuffSeconds : relightSeconds));
            if (Mathf.Approximately(t, cell.litT)) continue;
            cell.litT = t;
            float s = Mathf.Lerp(0.01f, 1f, t);
            foreach (var f in cell.flames)
                f.localScale = new Vector3(0.12f * s, 0.2f * s, 0.12f * s);
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
    [Tooltip("Field edge length (metres).")]
    public float fieldSize = 120f;
    [Tooltip("Candle cluster pitch (metres) — clusters snuff/relight together.")]
    public float cellPitch = 6f;
    [Tooltip("Seconds for a watched cluster to gutter out.")]
    public float snuffSeconds = 0.8f;
    [Tooltip("Seconds for an unwatched cluster to relight.")]
    public float relightSeconds = 2.2f;
    [Tooltip("Clusters further than this don't react (stay lit).")]
    public float snuffMaxDistance = 60f;

    [Header("Exit")]
    [Tooltip("Distance from spawn to the candle that never goes out.")]
    public float trueCandleDistance = 65f;
    [Tooltip("Scene the true candle leads to.")]
    public string nextScene = "D26_Ferry";
}
