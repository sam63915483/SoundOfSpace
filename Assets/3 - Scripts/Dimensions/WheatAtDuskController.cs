using UnityEngine;

/// <summary>
/// D23 "Wheat at Dusk": a waist-high golden field under a purple sky. The wheat is
/// solid — except where you LOOK: watched clumps bow down and part (collider off),
/// unwatched wheat stands back up as a wall. You can only walk where your gaze has
/// opened a path. Somewhere out there is a stone well; it hums when faced.
/// </summary>
public class WheatAtDuskController : MonoBehaviour
{
    class Clump
    {
        public Transform tf;
        public Collider col;
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
        var soilMat  = DimensionSceneUtil.Mat(new Color(0.24f, 0.16f, 0.10f), 0.05f);
        var wheatMat = DimensionSceneUtil.Mat(new Color(0.85f, 0.68f, 0.28f), 0.15f);
        var stoneMat = DimensionSceneUtil.Mat(new Color(0.35f, 0.33f, 0.31f), 0.1f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Soil",
            new Vector3(0f, -0.5f, 0f), new Vector3(1200f, 1f, 1200f), soilMat, root);
        DimensionSceneUtil.CreateDirectionalLight(new Color(1f, 0.7f, 0.5f), 0.8f, new Vector3(14f, -120f, 0f), true);

        // The well, out in the field in a random direction.
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
        var roofL = DimensionSceneUtil.Block(PrimitiveType.Cube, "RoofL",
            new Vector3(-0.7f, 2.5f, 0f), new Vector3(1.6f, 0.12f, 2.2f), stoneMat, well.transform);
        roofL.transform.localRotation = Quaternion.Euler(0f, 0f, 35f);
        var roofR = DimensionSceneUtil.Block(PrimitiveType.Cube, "RoofR",
            new Vector3(0.7f, 2.5f, 0f), new Vector3(1.6f, 0.12f, 2.2f), stoneMat, well.transform);
        roofR.transform.localRotation = Quaternion.Euler(0f, 0f, -35f);
        var hole = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Hole",
            new Vector3(0f, 0.3f, 0f), new Vector3(2.2f, 0.03f, 2.2f), DimensionSceneUtil.Mat(new Color(0.02f, 0.02f, 0.03f)), well.transform);
        Object.Destroy(hole.GetComponent<Collider>());
        _wellHum = DimensionSceneUtil.LoopingAudio(well, DimensionSceneUtil.ToneClip(494f, 2f, 0.45f), 260f, 1f);
        DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 0.5f, 0f),
            new Vector3(1.7f, 0.8f, 1.7f), LevelPortal.PortalAction.EnterInterior, nextScene, well.transform);
        well.transform.position = wellPos;

        // The field: a grid of waist-high wheat clump walls.
        int perSide = Mathf.RoundToInt(fieldSize / clumpSpacing);
        var clumps = new System.Collections.Generic.List<Clump>();
        for (int ix = 0; ix <= perSide; ix++)
            for (int iz = 0; iz <= perSide; iz++)
            {
                Vector3 pos = new Vector3((ix - perSide * 0.5f) * clumpSpacing, 0f, (iz - perSide * 0.5f) * clumpSpacing);
                if (pos.magnitude < 6f) continue;                            // spawn clearing
                if ((pos - wellPos).magnitude < 7f) continue;                // well clearing
                var go = DimensionSceneUtil.Block(PrimitiveType.Cube, "Wheat",
                    pos + Vector3.up * (wheatHeight * 0.5f),
                    new Vector3(clumpSpacing * 0.92f, wheatHeight, clumpSpacing * 0.92f), wheatMat, root);
                clumps.Add(new Clump { tf = go.transform, col = go.GetComponent<Collider>() });
            }
        _clumps = clumps.ToArray();

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(75f, 3f, 0.05f), 500f, 1f);
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
        foreach (var c in _clumps)
        {
            Vector3 pos = c.tf.position;
            // Only clumps near the player animate; far wheat just stands (cheap).
            Vector3 flat = pos - camPos; flat.y = 0f;
            if (flat.sqrMagnitude > partMaxDistance * partMaxDistance)
            {
                if (c.openT > 0f) SetClump(c, 0f);
                continue;
            }

            var b = new Bounds(new Vector3(pos.x, wheatHeight * 0.5f, pos.z),
                new Vector3(clumpSpacing * 0.92f, wheatHeight, clumpSpacing * 0.92f));
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
        float h = Mathf.Lerp(wheatHeight, 0.25f, openT);
        Vector3 pos = c.tf.position;
        c.tf.position = new Vector3(pos.x, h * 0.5f, pos.z);
        Vector3 s = c.tf.localScale;
        c.tf.localScale = new Vector3(s.x, h, s.z);
        c.col.enabled = openT < 0.55f;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Field")]
    [Tooltip("Field edge length (metres).")]
    public float fieldSize = 70f;
    [Tooltip("Clump grid pitch (metres).")]
    public float clumpSpacing = 3.5f;
    [Tooltip("Standing wheat wall height.")]
    public float wheatHeight = 1.6f;
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
