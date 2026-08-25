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

    Camera _cam;
    RenderTexture _rt;
    Transform _shuttle;
    float _nextFrameAt;

    public RenderTexture Texture => _rt;

    public static ShuttleLandingCamera Create(ShuttleAutopilot pilot)
    {
        var go = new GameObject("TravelLandingCam");
        go.transform.SetParent(pilot.transform, false);
        go.transform.localPosition = Vector3.down * 0.5f;
        var feed = go.AddComponent<ShuttleLandingCamera>();
        feed.Build(pilot.transform);
        return feed;
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
        transform.rotation = Quaternion.LookRotation(-_shuttle.up, _shuttle.forward);
        bool due = Time.unscaledTime >= _nextFrameAt;
        if (due) _nextFrameAt = Time.unscaledTime + 1f / FeedFps;
        _cam.enabled = due;
    }

    public void Teardown()
    {
        if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
        Destroy(gameObject);
    }
}
