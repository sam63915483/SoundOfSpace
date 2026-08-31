using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// MENU-ONLY (MenuOrbit scene): Sam IS the camera director.
///
/// MANUAL mode (recording): third-person orbit around the shuttle —
///   • mouse       orbit (azimuth / elevation)
///   • W / S       dolly closer / further
///   • A / D       slide where the shuttle sits on screen (left/right thirds)
/// Recording starts the moment the scene starts and is flushed to
/// Logs/menu_take_&lt;timestamp&gt;.json every few seconds (crash-safe). Get a good
/// 3-6 minute run, stop play, and the take gets baked for playback.
///
/// PLAYBACK mode: if StreamingAssets/menu_camera_take.json exists, the rig
/// replays the baked take for every player, looping on the shuttle-relative
/// parameters (the tour itself never repeats exactly — planets move — but the
/// framing is relative to the shuttle, so the loop is seamless).
/// F9 toggles back to Manual+Record even when a bake exists.
///
/// Pose math mirrors the retired shot director: position exact every frame
/// (never lerp a child-of-moving-rig toward a target), horizon from the
/// tour's smoothed up.
/// </summary>
public class MenuCameraRig : MonoBehaviour
{
    public MenuShuttleTour tour;
    public Camera cam;

    [Header("Manual control")]
    public float mouseSensitivity = 3.5f;
    public float dollySpeed = 35f;
    public float frameSlideSpeed = 30f;
    public float minDistance = 24f;
    public float maxDistance = 220f;

    // Rig state — everything shuttle-relative.
    float azimuth, elevation = 18f, distance = 60f, frameOffset;

    // Recording
    [System.Serializable] class Sample { public float t, az, el, d, off; }
    [System.Serializable] class Take { public List<Sample> samples = new List<Sample>(); }
    Take recording = new Take();
    Take playback;
    float clock;                 // seconds since rig start (tour-relative)
    float nextSampleAt, nextFlushAt;
    string takePath;
    bool manual = true;
    int playIndex;

    const float SampleHz = 20f;

    void Start()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (tour == null) tour = FindObjectOfType<MenuShuttleTour>();
        if (cam == null || tour == null) { enabled = false; return; }
        cam.fieldOfView = 58f;

        // Baked take → playback for players. Missing → Sam's manual+record.
        string baked = Path.Combine(Application.streamingAssetsPath, "menu_camera_take.json");
        if (File.Exists(baked))
        {
            try
            {
                playback = JsonUtility.FromJson<Take>(File.ReadAllText(baked));
                if (playback != null && playback.samples.Count > 1) manual = false;
            }
            catch (System.Exception e) { Debug.LogWarning("[MenuCameraRig] bad baked take: " + e.Message); }
        }

        if (manual)
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
            Directory.CreateDirectory(dir);
            takePath = Path.Combine(dir, $"menu_take_{System.DateTime.Now:yyyyMMdd_HHmmss}.json");
        }
        Debug.Log($"[MenuCameraRig] mode={(manual ? "MANUAL+RECORD -> " + takePath : "PLAYBACK (baked take, " + playback.samples.Count + " samples)")}");
        Apply();
    }

    void Update()
    {
        clock += Time.deltaTime;

        if (Input.GetKeyDown(KeyCode.F9) && !manual)
        {
            manual = true;
            recording = new Take();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
            Directory.CreateDirectory(dir);
            takePath = Path.Combine(dir, $"menu_take_{System.DateTime.Now:yyyyMMdd_HHmmss}.json");
            Debug.Log("[MenuCameraRig] F9 -> MANUAL+RECORD " + takePath);
        }

        if (manual)
        {
            azimuth += Input.GetAxis("Mouse X") * mouseSensitivity;
            elevation = Mathf.Clamp(elevation - Input.GetAxis("Mouse Y") * mouseSensitivity, -70f, 82f);
            if (Input.GetKey(KeyCode.W)) distance -= dollySpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.S)) distance += dollySpeed * Time.deltaTime;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
            if (Input.GetKey(KeyCode.A)) frameOffset -= frameSlideSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.D)) frameOffset += frameSlideSpeed * Time.deltaTime;
            frameOffset = Mathf.Clamp(frameOffset, -26f, 26f);

            if (clock >= nextSampleAt)
            {
                nextSampleAt = clock + 1f / SampleHz;
                recording.samples.Add(new Sample { t = clock, az = azimuth, el = elevation, d = distance, off = frameOffset });
            }
            if (clock >= nextFlushAt)
            {
                nextFlushAt = clock + 5f;
                Flush();
            }
        }
        else
        {
            // Loop the take on its own duration; params are rate-limited so
            // the wrap seam becomes a quick smooth camera move, not a cut.
            var s = playback.samples;
            float dur = s[s.Count - 1].t;
            float t = Mathf.Repeat(clock, dur);
            if (playIndex >= s.Count - 1 || s[playIndex].t > t) playIndex = 0;
            while (playIndex < s.Count - 2 && s[playIndex + 1].t < t) playIndex++;
            var a = s[playIndex];
            var b = s[playIndex + 1];
            float u = Mathf.InverseLerp(a.t, b.t, t);
            float tazRaw = Mathf.LerpAngle(a.az, b.az, u);
            azimuth = Mathf.MoveTowardsAngle(azimuth, tazRaw, 120f * Time.deltaTime);
            elevation = Mathf.MoveTowards(elevation, Mathf.Lerp(a.el, b.el, u), 90f * Time.deltaTime);
            distance = Mathf.MoveTowards(distance, Mathf.Lerp(a.d, b.d, u), 80f * Time.deltaTime);
            frameOffset = Mathf.MoveTowards(frameOffset, Mathf.Lerp(a.off, b.off, u), 40f * Time.deltaTime);
        }
    }

    void LateUpdate() => Apply();

    void Apply()
    {
        Transform sh = tour.transform;
        var body = tour.FocusBody;
        if (body == null) return;

        Vector3 up = tour.CurrentUp;
        Vector3 refFwd = Vector3.Cross(up, Vector3.forward);
        if (refFwd.sqrMagnitude < 0.01f) refFwd = Vector3.Cross(up, Vector3.right);
        refFwd.Normalize();

        Quaternion swing = Quaternion.AngleAxis(azimuth, up)
                         * Quaternion.AngleAxis(elevation, Vector3.Cross(refFwd, up));
        Vector3 pos = sh.position + swing * refFwd * distance;

        var rot = Quaternion.LookRotation((sh.position - pos).normalized, up);
        rot = Quaternion.AngleAxis(frameOffset, rot * Vector3.up) * rot;
        transform.SetPositionAndRotation(pos, rot);
    }

    void Flush()
    {
        if (!manual || recording.samples.Count == 0 || takePath == null) return;
        try { File.WriteAllText(takePath, JsonUtility.ToJson(recording)); }
        catch (System.Exception e) { Debug.LogWarning("[MenuCameraRig] flush failed: " + e.Message); }
    }

    void OnDestroy() => Flush();
}
