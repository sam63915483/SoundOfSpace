using UnityEngine;

// Downward camera feed for the NAV app's HOVER/LANDING views. Runtime-created
// (the hand-maintained prefab carries no camera), torn down on PARKED.
//
// Second-camera gotchas inherited from ShuttleArrivalSequence.StartLandingCam:
//  • Space dust is Graphics.DrawMeshInstanced — manual Render() calls MISS
//    those submissions, so this stays a real enabled camera toggled on for
//    single frames at ~15 fps (the CCTV cadence).
//  • Ocean/atmosphere are post effects on the player camera — copied onto this
//    one (OceanMaskRenderer + CustomPostProcessing, JSON field copy) with a
//    depth texture so the water renders in the feed.
public class ShuttleLandingCamera : MonoBehaviour
{
    const float FeedFps = 15f;

    // Feed direction (2026-08-28, Sam's en-route screen): Down is the classic
    // landing view; Up mounts the camera above the dome looking along the
    // thrust axis — during a burn that is the direction of travel, so the
    // en-route screen shows what you are flying at. "Bottom camera looking
    // back" is exactly Down.
    public enum FeedMode { Down, Up }
    public FeedMode Mode { get; private set; } = FeedMode.Down;

    Camera _cam;
    RenderTexture _rt;
    Transform _shuttle;
    float _nextFrameAt;

    public RenderTexture Texture => _rt;

    public static ShuttleLandingCamera Create(ShuttleAutopilot pilot)
    {
        return Create(pilot, FeedMode.Down);
    }

    public static ShuttleLandingCamera Create(ShuttleAutopilot pilot, FeedMode mode)
    {
        var go = new GameObject(mode == FeedMode.Up ? "TravelTransitCam" : "TravelLandingCam");
        go.transform.SetParent(pilot.transform, false);
        var feed = go.AddComponent<ShuttleLandingCamera>();
        feed.Build(pilot.transform);
        feed.SetMode(mode);
        return feed;
    }

    public void SetMode(FeedMode mode)
    {
        Mode = mode;
        // Up: above the WHOLE roof mast — the red Beacon sits dead-centre at
        // y 7.8 and the antenna tips reach 8.45; a 7 m mount stared straight
        // at the beacon (Sam's "red circle" in the feed). Down: just under
        // the belly (the original landing-cam mount).
        transform.localPosition = mode == FeedMode.Up ? Vector3.up * 9f : Vector3.down * 0.5f;
    }

    void Build(Transform shuttle)
    {
        _shuttle = shuttle;
        _cam = gameObject.AddComponent<Camera>();
        _cam.enabled = false;                        // flipped on for single frames in LateUpdate
        _cam.fieldOfView = 70f;
        _cam.nearClipPlane = 0.3f;
        _cam.farClipPlane = 8000f;
        _cam.depthTextureMode = DepthTextureMode.Depth;
        _cam.depth = -10f;                           // render before the main camera

        var pc = FindObjectOfType<PlayerController>();
        var mainCam = pc != null && pc.Camera != null ? pc.Camera : Camera.main;
        if (mainCam != null)
        {
            _cam.cullingMask = mainCam.cullingMask;
            CopyCamEffect(mainCam, "OceanMaskRenderer");
            CopyCamEffect(mainCam, "CustomPostProcessing");
        }

        // Inherit the flight horizon: the main camera's far plane was raised
        // for transit (planets stay visible from 15 km out) — the en-route
        // Up feed needs the same reach or the destination pops in late.
        if (mainCam != null && mainCam.farClipPlane > _cam.farClipPlane)
            _cam.farClipPlane = mainCam.farClipPlane;

        _rt = new RenderTexture(512, 384, 16);
        _cam.targetTexture = _rt;
    }

    void CopyCamEffect(Camera mainCam, string typeName)
    {
        try
        {
            var src = mainCam.GetComponent(typeName);
            if (src == null) return;
            if (_cam.GetComponent(typeName) != null) return;
            var dst = _cam.gameObject.AddComponent(src.GetType());
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(src), dst);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[ShuttleTravel] couldn't copy " + typeName + " onto the landing cam: " + e.Message);
        }
    }

    void LateUpdate()
    {
        if (_cam == null || _shuttle == null) return;
        transform.rotation = Mode == FeedMode.Up
            ? Quaternion.LookRotation(_shuttle.up, -_shuttle.forward)
            : Quaternion.LookRotation(-_shuttle.up, _shuttle.forward);
        bool due = Time.unscaledTime >= _nextFrameAt;
        if (due) _nextFrameAt = Time.unscaledTime + 1f / FeedFps;
        _cam.enabled = due;
    }

    public void Teardown()
    {
        Destroy(gameObject);   // RT freed in OnDestroy
    }

    /// Deferred teardown (playtest 19): destroying the camera + releasing the
    /// RenderTexture on the touchdown frame stacked onto the door-open
    /// moment. Disable rendering immediately, free the objects well after
    /// the rider handover window has passed.
    public void TeardownDeferred(float delay)
    {
        if (_cam != null) _cam.enabled = false;
        _shuttle = null;   // stops LateUpdate work until the destroy lands
        Destroy(gameObject, delay);
    }

    void OnDestroy()
    {
        if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
    }
}
