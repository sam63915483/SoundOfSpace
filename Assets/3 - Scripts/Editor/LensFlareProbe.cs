#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;

// Drop-in probe: scans the runtime hierarchy for the LensFlare overlay canvas
// + sun/bonfire images and dumps everything that matters to the console. Run
// via MCP execute_script while play mode is paused.
public static class LensFlareProbe
{
    public static void Execute()
    {
        var canvases = Object.FindObjectsOfType<Canvas>(true);
        Canvas overlay = null;
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null && canvases[i].gameObject.name == "[LensFlareOverlayCanvas]")
            {
                overlay = canvases[i];
                break;
            }
        }
        if (overlay == null) { Debug.Log("[Probe] no overlay canvas in scene"); return; }
        Debug.Log($"[Probe] canvas: active={overlay.gameObject.activeInHierarchy} enabled={overlay.enabled} sortingOrder={overlay.sortingOrder} renderMode={overlay.renderMode} childCount={overlay.transform.childCount}");

        // Player camera
        Camera cam = Camera.main;
        var mgrType = System.Type.GetType("CameraEffectsManager");
        if (mgrType != null)
        {
            var inst = mgrType.GetProperty("Instance")?.GetValue(null);
            if (inst != null)
            {
                var camProp = mgrType.GetProperty("PlayerCamera");
                if (camProp != null) cam = camProp.GetValue(inst) as Camera ?? cam;
            }
        }
        Debug.Log($"[Probe] cam='{(cam != null ? cam.name : "<null>")}' fwd={(cam != null ? cam.transform.forward.ToString("F2") : "?")} pos={(cam != null ? cam.transform.position.ToString("F1") : "?")} farClip={(cam != null ? cam.farClipPlane.ToString("F1") : "?")}");

        // Sun body
        var sim = NBodySimulation.Bodies;
        CelestialBody sun = null;
        if (sim != null) for (int i = 0; i < sim.Length; i++) if (sim[i] != null && sim[i].bodyType == CelestialBody.BodyType.Sun) { sun = sim[i]; break; }
        if (sun != null && cam != null)
        {
            Vector3 toSun = sun.Position - cam.transform.position;
            float dot = Vector3.Dot(cam.transform.forward, toSun.normalized);
            Debug.Log($"[Probe] sun: pos={sun.Position.ToString("F1")} distFromCam={toSun.magnitude:F0}m dot(camForward,toSun)={dot:F3} (>0 = looking toward sun)");
            Vector3 proj = cam.transform.position + toSun.normalized * (cam.nearClipPlane + 1f);
            Vector3 sp = cam.WorldToScreenPoint(proj);
            Debug.Log($"[Probe] sun screen: ({sp.x:F0},{sp.y:F0},z={sp.z:F2}) screenSize=({Screen.width}x{Screen.height})");
        }

        // Children
        for (int i = 0; i < overlay.transform.childCount; i++)
        {
            var child = overlay.transform.GetChild(i);
            var img = child.GetComponent<Image>();
            var rt = (RectTransform)child;
            Debug.Log($"[Probe] child[{i}] '{child.name}': active={child.gameObject.activeInHierarchy} imgEnabled={(img!=null?img.enabled:false)} sprite={(img!=null&&img.sprite!=null?img.sprite.name:"<null>")} color={(img!=null?img.color.ToString():"?")} anchored={rt.anchoredPosition} size={rt.sizeDelta}");
        }

        // InputSettings flag
        var inputSettings = Object.FindObjectOfType<InputSettings>();
        Debug.Log($"[Probe] InputSettings='{(inputSettings != null ? inputSettings.name : "<null>")}' fxLensFlares={(inputSettings != null ? inputSettings.fxLensFlares.ToString() : "?")} cameraEffectsEnabled={(inputSettings != null ? inputSettings.cameraEffectsEnabled.ToString() : "?")}");
    }
}
#endif
