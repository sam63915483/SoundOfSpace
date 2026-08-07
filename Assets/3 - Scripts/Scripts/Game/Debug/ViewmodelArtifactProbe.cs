using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// TEMPORARY DIAGNOSTIC — delete this file once the artifact is identified.
//
// Hunts the "black jittery shape that moves with the camera and never gets
// closer" bug. A view-locked artifact is always one of three things, and this
// tells you WHICH in one keypress instead of another round of guessing:
//
//   A) something PARENTED to the camera that shouldn't be rendering
//   B) a LIGHTING artifact (shadow acne, a cascade seam, per-pixel light
//      demotion — see PixelLightLimitFix for the last incident of that kind)
//   C) a SHADOW cast by something near the camera onto the world
//
// Companion to LightingDebugToolbox (F6-F12), which covers scene lighting.
// This one covers the camera's immediate neighbourhood. Keys are F4/F5 because
// F3 is FPSOverlay, F6-F12 are the lighting toolbox, F8 is also CheatCodes.
//
//   F4 — step through an isolation sequence, one suspect disabled at a time.
//        Whichever step makes the artifact vanish IS the cause. On-screen text
//        shows the current step. Wraps back to "all on".
//   F5 — dump a full report of everything within probeRadius of the camera:
//        every renderer (path, shader, render queue, shadow mode, distance),
//        every light (type, shadows, render mode, range), the shadow quality
//        settings, and the camera's own setup. Press it WHILE THE ARTIFACT IS
//        VISIBLE — a stray camera-parented renderer shows up immediately.
//
// Nothing here persists: every toggle is restored on scene reload, and the
// probe only auto-creates outside MainMenu.
public class ViewmodelArtifactProbe : MonoBehaviour
{
    public static ViewmodelArtifactProbe Instance { get; private set; }

    const float probeRadius = 4f;   // metres around the camera to inspect

    int _step;                      // 0 = everything on
    GUIStyle _style;

    readonly List<Renderer> _hiddenRenderers = new List<Renderer>();
    readonly List<Light> _shadowedLights = new List<Light>();

    static readonly string[] kStepNames =
    {
        "0: ALL ON (baseline)",
        "1: ViewmodelFillLight OFF",
        "2: + held viewmodel renderers OFF",
        "3: + directional-light shadows OFF",
        "4: + ALL camera-child renderers OFF",
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("[ViewmodelArtifactProbe]");
        DontDestroyOnLoad(go);
        go.AddComponent<ViewmodelArtifactProbe>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Debug.Log("[ViewmodelArtifactProbe] F4 = step through suspects, F5 = dump near-camera report.");
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F4)) { _step = (_step + 1) % kStepNames.Length; ApplyStep(); }
        if (Input.GetKeyDown(KeyCode.F5)) Debug.Log(BuildReport());
    }

    // ── Isolation sequence ────────────────────────────────────────────────
    void ApplyStep()
    {
        RestoreAll();
        var cam = Camera.main;

        if (_step >= 1)
        {
            var fill = ViewmodelFillLight.Instance;
            if (fill != null)
                foreach (var l in fill.GetComponentsInChildren<Light>(true))
                    if (l.enabled) { l.enabled = false; _shadowedLights.Add(l); }
            // The light is parented to the camera, so also catch it there.
            if (cam != null)
                foreach (var l in cam.GetComponentsInChildren<Light>(true))
                    if (l.enabled && l.name.Contains("ViewmodelFill"))
                    { l.enabled = false; _shadowedLights.Add(l); }
        }

        if (_step >= 2 && cam != null)
        {
            // Anything under a *MotorRig / *Pivot — i.e. a held item.
            foreach (var r in cam.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                string path = PathOf(r.transform);
                if (path.Contains("MotorRig") || path.Contains("Pivot") || path.Contains("Held_"))
                { r.enabled = false; _hiddenRenderers.Add(r); }
            }
        }

        if (_step >= 3)
        {
            foreach (var l in FindObjectsOfType<Light>())
                if (l.type == LightType.Directional && l.shadows != LightShadows.None)
                { l.shadows = LightShadows.None; _shadowedLights.Add(l); }
        }

        if (_step >= 4 && cam != null)
        {
            foreach (var r in cam.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) { r.enabled = false; _hiddenRenderers.Add(r); }
        }

        Debug.Log($"[ViewmodelArtifactProbe] {kStepNames[_step]}  (renderers hidden: {_hiddenRenderers.Count}, lights touched: {_shadowedLights.Count})");
    }

    void RestoreAll()
    {
        foreach (var r in _hiddenRenderers) if (r != null) r.enabled = true;
        _hiddenRenderers.Clear();
        // Directional shadows were the only thing we set to None; re-enable soft.
        foreach (var l in _shadowedLights)
        {
            if (l == null) continue;
            if (l.type == LightType.Directional) l.shadows = LightShadows.Soft;
            else l.enabled = true;
        }
        _shadowedLights.Clear();
    }

    // ── Report ────────────────────────────────────────────────────────────
    string BuildReport()
    {
        var sb = new StringBuilder();
        var cam = Camera.main;
        sb.AppendLine("═══ VIEWMODEL ARTIFACT PROBE ═══");
        if (cam == null) { sb.AppendLine("Camera.main is NULL"); return sb.ToString(); }

        Vector3 c = cam.transform.position;
        sb.AppendLine($"Camera '{cam.name}'  near={cam.nearClipPlane} far={cam.farClipPlane} fov={cam.fieldOfView:F1} " +
                      $"mask=0x{cam.cullingMask:X} depthMode={cam.depthTextureMode} msaa={cam.allowMSAA} hdr={cam.allowHDR}");
        sb.AppendLine($"Quality: pixelLightCount={QualitySettings.pixelLightCount} shadowDist={QualitySettings.shadowDistance} " +
                      $"cascades={QualitySettings.shadowCascades} proj={QualitySettings.shadowProjection} " +
                      $"nearPlaneOffset={QualitySettings.shadowNearPlaneOffset} res={QualitySettings.shadowResolution}");

        sb.AppendLine($"--- RENDERERS within {probeRadius}m of camera (enabled only) ---");
        var rends = FindObjectsOfType<Renderer>();
        var near = new List<(float d, Renderer r)>();
        foreach (var r in rends)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(r.bounds.center, c);
            if (d <= probeRadius) near.Add((d, r));
        }
        near.Sort((a, b) => a.d.CompareTo(b.d));
        foreach (var (d, r) in near)
        {
            var mat = r.sharedMaterial;
            sb.AppendLine($"  {d:F2}m  {PathOf(r.transform)}");
            sb.AppendLine($"        type={r.GetType().Name} shadowCast={r.shadowCastingMode} receive={r.receiveShadows} " +
                          $"size={r.bounds.size} camChild={IsUnder(r.transform, cam.transform)}");
            sb.AppendLine($"        mat={(mat != null ? mat.name : "NULL")} shader={(mat != null && mat.shader != null ? mat.shader.name : "NULL")} " +
                          $"queue={(mat != null ? mat.renderQueue.ToString() : "-")}");
        }
        if (near.Count == 0) sb.AppendLine("  (none)");

        sb.AppendLine($"--- LIGHTS within {probeRadius * 4f}m of camera ---");
        foreach (var l in FindObjectsOfType<Light>())
        {
            if (l == null || !l.isActiveAndEnabled) continue;
            float d = Vector3.Distance(l.transform.position, c);
            if (l.type != LightType.Directional && d > probeRadius * 4f) continue;
            sb.AppendLine($"  {(l.type == LightType.Directional ? "  DIR" : $"{d:F2}m")}  {PathOf(l.transform)}");
            sb.AppendLine($"        type={l.type} shadows={l.shadows} renderMode={l.renderMode} intensity={l.intensity:F2} " +
                          $"range={l.range:F1} mask=0x{l.cullingMask:X} bias={l.shadowBias:F4}/{l.shadowNormalBias:F4}");
        }

        sb.AppendLine("═══ END ═══");
        return sb.ToString();
    }

    static bool IsUnder(Transform t, Transform ancestor)
    {
        for (Transform p = t; p != null; p = p.parent) if (p == ancestor) return true;
        return false;
    }

    static string PathOf(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (Transform p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    void OnGUI()
    {
        if (_style == null)
            _style = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.yellow } };
        GUI.Label(new Rect(10, 120, 900, 26), $"[ArtifactProbe] F4 step → {kStepNames[_step]}   |   F5 = dump report", _style);
    }
}
