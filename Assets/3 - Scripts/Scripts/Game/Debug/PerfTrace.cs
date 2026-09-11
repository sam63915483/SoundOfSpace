using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// PerfTrace — the FPS-hunt recorder. Development builds + Editor only; a
/// release build never creates it (see AutoCreate).
///
/// WHY THIS EXISTS: the Unity Profiler window keeps at most 2000 frames
/// (~40 s at 50 fps), so a 10-minute playtest only ever showed the last few
/// seconds. This writes ONE ROW PER FRAME to a CSV for the whole run, with the
/// frame's timings, the render counters (draws / SetPass / shadow casters /
/// triangles) AND the situation the camera was in (nearest body, altitude,
/// piloting, and — for the known suspects — how far off-axis the village, the
/// moon and the moon base are and whether they're hidden behind a planet). That
/// lets a script afterwards answer "what does looking at the village through
/// the planet cost, exactly" without a human scrubbing a timeline.
///
/// Files land in  &lt;persistentDataPath&gt;/perf/  ( %AppData%\..\LocalLow\
/// DefaultCompany\Solar System 2\perf\ ). Analyse with
/// tools/perf/analyze_perf_trace.py.
///
/// HOTKEYS (numpad, Num Lock ON; PgUp/PgDn/Delete work without a numpad).
/// Every toggle is an A/B bisect: it removes ONE suspect while the trace keeps
/// running, and the row records which toggles were active so the analyser can
/// diff "with" vs "without" automatically. Press it again to restore.
///   Keypad1  all point + spot lights OFF
///   Keypad2  all point + spot lights → vertex-lit (ForceVertex)
///   Keypad3  village renderers OFF          (TOWN-VILLAGE)
///   Keypad4  MSAA OFF                       (QualitySettings.antiAliasing = 0)
///   Keypad5  shadows OFF                    (QualitySettings.shadows)
///   Keypad6  grass OFF                      (InstancedGrassRenderer)
///   Keypad7  shadow cascades → 2, distance → 100 m
///   Keypad8  point/spot lights stop touching planet meshes (cullingMask minus the Body layer)
///   Keypad9  pixel light cap 64 → 8         (QualitySettings.pixelLightCount)
///   KeypadPlus             MARK — stamps a numbered marker into the trace (say what
///                          you were looking at afterwards; the analyser prints
///                          the 3 s before/after each mark)
///   KeypadMinus            SNAPSHOT — writes the next 300 frames as a Unity
///                          Profiler .raw (full hierarchy) next to the CSV.
///                          Tools ▸ Solar System ▸ Perf ▸ Dump Profiler Snapshots
///                          turns them into text; Window ▸ Analysis ▸ Profiler ▸ Load
///                          opens one in the window.
///   PgUp / PgDn + Delete   select a toggle in the legend and flip it (no numpad)
///   KeypadDivide           hide/show the legend
///
/// Cost: a few dozen ProfilerRecorder reads and a zero-alloc CSV row per
/// frame — well under 0.1 ms. The trace does not start until the gameplay scene
/// is up, so the main menu is never in it.
///
/// Deliberately NOT a MainMenu-skipping singleton (trap #1): it creates itself in
/// whatever scene boots first and simply waits for a non-menu scene, so it needs
/// no seeding in MainMenuController.
///
/// v2 (after run 1, 2026-09-11): CSV rows were written with the newline and the
/// header commas sanitised away (one giant line); the village angle was measured
/// to TOWN-VILLAGE's pivot, which sits at the planet centre, not at the houses —
/// landmarks now get an anchor at their renderer-bounds centre; script markers
/// that did not exist yet when the recorder started are retried; and the toggle
/// set was swapped to the levers run 1 pointed at (lights × planet mesh, MSAA,
/// cascades) instead of the ones it ruled out (moon base = CPU only, dust, UI).
/// </summary>
public class PerfTrace : MonoBehaviour
{
    public static PerfTrace Instance { get; private set; }

    const int SnapshotFrames = 300;
    const float FlushIntervalSec = 2f;
    const float ContextRefindSec = 2f;
    const int BodyLayer = 10;      // "Body" — the layer every planet's Mesh Holder is on (TagManager)

    // ---- toggles --------------------------------------------------------------
    enum Toggle { Lights, LightsVertex, Village, MsaaOff, Shadows, Grass, Cascades2, LightsSkipPlanet, PixelLights8, Count }
    static readonly string[] ToggleNames =
    {
        "point/spot lights OFF", "lights → vertex-lit", "village OFF", "MSAA OFF", "shadows OFF",
        "grass OFF", "cascades 2 + dist 100", "lights skip planet mesh", "pixel lights 64→8",
    };
    static readonly KeyCode[] ToggleKeys =
    {
        KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4, KeyCode.Keypad5,
        KeyCode.Keypad6, KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9,
    };
    readonly bool[] _on = new bool[(int)Toggle.Count];
    int _selected;
    bool _legendVisible = true;

    // what each toggle changed, so restore only touches those
    readonly List<Light> _lightsOff = new List<Light>();
    readonly List<Light> _lightsVertex = new List<Light>();
    readonly List<LightRenderMode> _lightsVertexMode = new List<LightRenderMode>();
    readonly List<Light> _lightsMasked = new List<Light>();
    readonly List<int> _lightsMaskBefore = new List<int>();
    readonly List<Renderer> _villageOff = new List<Renderer>();
    readonly List<InstancedGrassRenderer> _grassOff = new List<InstancedGrassRenderer>();
    ShadowQuality _shadowsBefore;
    int _msaaBefore, _cascadesBefore, _pixelLightsBefore;
    float _shadowDistBefore;

    // ---- recorders ------------------------------------------------------------
    struct Rec
    {
        public string column;
        public ProfilerCategory category;
        public string marker;
        public bool isTime;          // true: nanoseconds → ms; false: raw count
        public ProfilerRecorder recorder;
    }
    Rec[] _recs;
    float _recRetryTimer;

    // ---- csv -------------------------------------------------------------------
    StreamWriter _writer;
    string _dir, _csvPath;
    readonly char[] _buf = new char[2048];
    int _len;
    float _flushTimer;
    int _frame;
    float _t0;
    bool _headerWritten;

    // ---- context ---------------------------------------------------------------
    Camera _cam;
    float _refindTimer;
    Transform _village, _villageAnchor, _moonBase, _shuttle;
    CelestialBody _moon, _villageBody, _moonBaseBody;
    int _mark, _pendingMark;
    int _snapshotLeft, _snapshotIndex;
    string _snapshotName = "";

    // ---- ui --------------------------------------------------------------------
    Canvas _canvas;
    TextMeshProUGUI _text;
    readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder(1024);
    string _lastLegend;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!Debug.isDebugBuild && !Application.isEditor) return;   // release builds: nothing at all
        if (Instance != null) return;
        var go = new GameObject("[PerfTrace]");
        DontDestroyOnLoad(go);
        go.AddComponent<PerfTrace>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _dir = Path.Combine(Application.persistentDataPath, "perf");
        try { Directory.CreateDirectory(_dir); } catch (Exception e) { Debug.LogWarning("[PerfTrace] cannot create " + _dir + ": " + e.Message); }

        // Marker timings are only collected while the profiler is on. In a dev
        // build with no Editor attached this just fills a ring buffer — no disk,
        // no network — and it is the same for every frame of the run, so the
        // A/B deltas are unaffected. In the Editor the Profiler window owns that
        // switch (and its overhead), so only force it in a player.
        if (!Application.isEditor)
        {
            Profiler.maxUsedMemory = 256 * 1024 * 1024;
            Profiler.enabled = true;
        }

        _recs = new[]
        {
            // whole-frame
            T("main_ms",        ProfilerCategory.Internal, "Main Thread"),
            T("gpu_ms",         ProfilerCategory.Render,   "GPU Frame Time"),
            T("waitgpu_ms",     ProfilerCategory.Render,   "DXGI.WaitOnSwapChain"),
            // where the main thread goes
            T("update_ms",      ProfilerCategory.Scripts,  "Update.ScriptRunBehaviourUpdate"),
            T("lateupdate_ms",  ProfilerCategory.Scripts,  "PreLateUpdate.ScriptRunBehaviourLateUpdate"),
            T("fixed_ms",       ProfilerCategory.Physics,  "FixedUpdate.PhysicsFixedUpdate"),
            T("canvas_ms",      ProfilerCategory.Gui,      "PostLateUpdate.PlayerUpdateCanvases"),
            T("camrender_ms",   ProfilerCategory.Render,   "Camera.Render"),
            T("culling_ms",     ProfilerCategory.Render,   "Culling"),
            T("skinfinal_ms",   ProfilerCategory.Render,   "SkinnedMeshFinalizeUpdate"),
            T("shadowmap_ms",   ProfilerCategory.Render,   "Shadows.RenderShadowMap"),
            T("opaque_ms",      ProfilerCategory.Render,   "Render.OpaqueGeometry"),
            T("transparent_ms", ProfilerCategory.Render,   "Render.TransparentGeometry"),
            T("imagefx_ms",     ProfilerCategory.Render,   "Camera.ImageEffects"),
            T("finishrender_ms",ProfilerCategory.Render,   "PostLateUpdate.FinishFrameRendering"),
            T("renderers_ms",   ProfilerCategory.Render,   "PostLateUpdate.UpdateAllRenderers"),
            // the usual script suspects (dev-build script markers are "Class.Method()")
            T("grass_ms",       ProfilerCategory.Scripts,  "InstancedGrassRenderer.LateUpdate()"),
            T("dust_ms",        ProfilerCategory.Scripts,  "SpaceDustField.LateUpdate()"),
            T("endless_ms",     ProfilerCategory.Scripts,  "EndlessManager.LateUpdate()"),
            T("lensflare_ms",   ProfilerCategory.Scripts,  "LensFlareRegistry.LateUpdate()"),
            T("fish_ms",        ProfilerCategory.Scripts,  "AmbientFishField.LateUpdate()"),
            T("fireflies_ms",   ProfilerCategory.Scripts,  "FireflySpawner.Update()"),
            T("cats_ms",        ProfilerCategory.Scripts,  "CatSpawner.Update()"),
            T("uinav_ms",       ProfilerCategory.Scripts,  "ControllerUINavigator.Update()"),
            // render counters
            C("draws",          ProfilerCategory.Render,   "Draw Calls Count"),
            C("setpass",        ProfilerCategory.Render,   "SetPass Calls Count"),
            C("batches",        ProfilerCategory.Render,   "Batches Count"),
            C("tris_k",         ProfilerCategory.Render,   "Triangles Count"),
            C("verts_k",        ProfilerCategory.Render,   "Vertices Count"),
            C("shadowcasters",  ProfilerCategory.Render,   "Shadow Casters Count"),
            C("skinned",        ProfilerCategory.Render,   "Visible Skinned Meshes Count"),
            C("instanced_draws",ProfilerCategory.Render,   "Instanced Batched Draw Calls Count"),
            C("gc_bytes",       ProfilerCategory.Memory,   "GC Allocated In Frame"),
        };
        StartRecorders();

        BuildUI();
        _t0 = Time.realtimeSinceStartup;
    }

    static Rec T(string col, ProfilerCategory cat, string marker) => new Rec { column = col, category = cat, marker = marker, isTime = true };
    static Rec C(string col, ProfilerCategory cat, string marker) => new Rec { column = col, category = cat, marker = marker, isTime = false };

    /// <summary>Script markers ("X.LateUpdate()") only exist once that method has
    /// run with the profiler on, so a recorder started too early stays invalid.
    /// Retry the invalid ones every couple of seconds until they bind.</summary>
    void StartRecorders()
    {
        for (int i = 0; i < _recs.Length; i++)
        {
            if (_recs[i].recorder.Valid) continue;
            try { _recs[i].recorder = ProfilerRecorder.StartNew(_recs[i].category, _recs[i].marker, 1); }
            catch (Exception) { }
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        RestoreAll();
        CloseCsv();
        if (_recs != null)
            for (int i = 0; i < _recs.Length; i++)
                if (_recs[i].recorder.Valid) _recs[i].recorder.Dispose();
    }

    void OnApplicationQuit()
    {
        RestoreAll();
        CloseCsv();
    }

    // ==============================================================================
    void Update()
    {
        HandleKeys();

        var scene = SceneManager.GetActiveScene();
        if (scene.name == "MainMenu") return;

        RefindContext();
        _recRetryTimer -= Time.unscaledDeltaTime;
        if (_recRetryTimer <= 0f) { _recRetryTimer = 2f; StartRecorders(); }

        WriteRow(scene.name);

        if (_snapshotLeft > 0 && --_snapshotLeft == 0) EndSnapshot();

        _flushTimer += Time.unscaledDeltaTime;
        if (_flushTimer >= FlushIntervalSec && _writer != null) { _flushTimer = 0f; _writer.Flush(); }

        UpdateLegend();
    }

    // ------------------------------------------------------------------ keys
    void HandleKeys()
    {
        for (int i = 0; i < ToggleKeys.Length; i++)
            if (Input.GetKeyDown(ToggleKeys[i])) Flip((Toggle)i);

        if (Input.GetKeyDown(KeyCode.PageUp))   _selected = (_selected + (int)Toggle.Count - 1) % (int)Toggle.Count;
        if (Input.GetKeyDown(KeyCode.PageDown)) _selected = (_selected + 1) % (int)Toggle.Count;
        if (Input.GetKeyDown(KeyCode.Delete))   Flip((Toggle)_selected);

        if (Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.Home))
        {
            _mark++;
            _pendingMark = _mark;
            Debug.Log("[PerfTrace] MARK " + _mark + " at t=" + (Time.realtimeSinceStartup - _t0).ToString("0.0") + "s");
        }
        if (Input.GetKeyDown(KeyCode.KeypadMinus) || Input.GetKeyDown(KeyCode.End))
            BeginSnapshot();
        if (Input.GetKeyDown(KeyCode.KeypadDivide))
        {
            _legendVisible = !_legendVisible;
            if (_canvas != null) _canvas.enabled = _legendVisible;
        }
    }

    // ------------------------------------------------------------------ toggles
    void Flip(Toggle t)
    {
        bool on = !_on[(int)t];
        _on[(int)t] = on;
        try
        {
            switch (t)
            {
                case Toggle.Lights:       if (on) LightsOff(); else RestoreLights(); break;
                case Toggle.LightsVertex: if (on) LightsVertex(); else RestoreLightsVertex(); break;
                case Toggle.Village:      if (on) RenderersOff(_villageOff, _village); else RestoreRenderers(_villageOff); break;
                case Toggle.MsaaOff:
                    if (on) { _msaaBefore = QualitySettings.antiAliasing; QualitySettings.antiAliasing = 0; }
                    else QualitySettings.antiAliasing = _msaaBefore;
                    break;
                case Toggle.Shadows:
                    if (on) { _shadowsBefore = QualitySettings.shadows; QualitySettings.shadows = ShadowQuality.Disable; }
                    else QualitySettings.shadows = _shadowsBefore;
                    break;
                case Toggle.Grass:        if (on) GrassOff(); else RestoreGrass(); break;
                case Toggle.Cascades2:
                    if (on)
                    {
                        _cascadesBefore = QualitySettings.shadowCascades; _shadowDistBefore = QualitySettings.shadowDistance;
                        QualitySettings.shadowCascades = 2; QualitySettings.shadowDistance = 100f;
                    }
                    else { QualitySettings.shadowCascades = _cascadesBefore; QualitySettings.shadowDistance = _shadowDistBefore; }
                    break;
                case Toggle.LightsSkipPlanet: if (on) LightsSkipPlanet(); else RestoreLightsMask(); break;
                case Toggle.PixelLights8:
                    if (on) { _pixelLightsBefore = QualitySettings.pixelLightCount; QualitySettings.pixelLightCount = 8; }
                    else QualitySettings.pixelLightCount = _pixelLightsBefore;
                    break;
            }
        }
        catch (Exception e) { Debug.LogWarning("[PerfTrace] toggle " + t + " failed: " + e.Message); }
        Debug.Log("[PerfTrace] " + ToggleNames[(int)t] + (on ? " = ON" : " = restored") + "  mask=" + ToggleMask());
        _lastLegend = null;
    }

    int ToggleMask()
    {
        int m = 0;
        for (int i = 0; i < _on.Length; i++) if (_on[i]) m |= 1 << i;
        return m;
    }

    static bool IsPointOrSpot(Light l) => l != null && l.enabled && (l.type == LightType.Point || l.type == LightType.Spot);

    void LightsOff()
    {
        _lightsOff.Clear();
        foreach (var l in FindObjectsOfType<Light>(false))
        {
            if (!IsPointOrSpot(l)) continue;
            l.enabled = false;
            _lightsOff.Add(l);
        }
        Debug.Log("[PerfTrace] switched off " + _lightsOff.Count + " point/spot lights");
    }

    void RestoreLights()
    {
        foreach (var l in _lightsOff) if (l != null) l.enabled = true;
        _lightsOff.Clear();
    }

    void LightsVertex()
    {
        _lightsVertex.Clear(); _lightsVertexMode.Clear();
        foreach (var l in FindObjectsOfType<Light>(false))
        {
            if (!IsPointOrSpot(l)) continue;
            _lightsVertex.Add(l); _lightsVertexMode.Add(l.renderMode);
            l.renderMode = LightRenderMode.ForceVertex;
        }
        Debug.Log("[PerfTrace] " + _lightsVertex.Count + " point/spot lights → vertex");
    }

    void RestoreLightsVertex()
    {
        for (int i = 0; i < _lightsVertex.Count; i++) if (_lightsVertex[i] != null) _lightsVertex[i].renderMode = _lightsVertexMode[i];
        _lightsVertex.Clear(); _lightsVertexMode.Clear();
    }

    void LightsSkipPlanet()
    {
        _lightsMasked.Clear(); _lightsMaskBefore.Clear();
        int bit = 1 << BodyLayer;
        foreach (var l in FindObjectsOfType<Light>(false))
        {
            if (!IsPointOrSpot(l) || (l.cullingMask & bit) == 0) continue;
            _lightsMasked.Add(l); _lightsMaskBefore.Add(l.cullingMask);
            l.cullingMask &= ~bit;
        }
        Debug.Log("[PerfTrace] " + _lightsMasked.Count + " point/spot lights no longer touch the Body layer");
    }

    void RestoreLightsMask()
    {
        for (int i = 0; i < _lightsMasked.Count; i++) if (_lightsMasked[i] != null) _lightsMasked[i].cullingMask = _lightsMaskBefore[i];
        _lightsMasked.Clear(); _lightsMaskBefore.Clear();
    }

    static void RenderersOff(List<Renderer> store, Transform root)
    {
        store.Clear();
        if (root == null) return;
        foreach (var r in root.GetComponentsInChildren<Renderer>(false))
        {
            if (r == null || !r.enabled) continue;
            r.enabled = false;
            store.Add(r);
        }
        Debug.Log("[PerfTrace] switched off " + store.Count + " renderers under " + root.name);
    }

    static void RestoreRenderers(List<Renderer> store)
    {
        foreach (var r in store) if (r != null) r.enabled = true;
        store.Clear();
    }

    void GrassOff()
    {
        _grassOff.Clear();
        foreach (var g in FindObjectsOfType<InstancedGrassRenderer>(false))
        {
            if (g == null || !g.enabled) continue;
            g.enabled = false;
            _grassOff.Add(g);
        }
    }

    void RestoreGrass()
    {
        foreach (var g in _grassOff) if (g != null) g.enabled = true;
        _grassOff.Clear();
    }

    void RestoreAll()
    {
        for (int i = 0; i < _on.Length; i++)
            if (_on[i]) { try { Flip((Toggle)i); } catch { } }
    }

    // ------------------------------------------------------------------ snapshots
    void BeginSnapshot()
    {
        if (_snapshotLeft > 0) return;
        _snapshotIndex++;
        _snapshotName = "snap" + _snapshotIndex.ToString("00") + "_mark" + _mark;
        string path = Path.Combine(_dir, RunStamp() + "_" + _snapshotName);
        try
        {
            Profiler.enabled = false;
            Profiler.logFile = path;           // Unity appends .raw
            Profiler.enableBinaryLog = true;
            Profiler.enabled = true;
            _snapshotLeft = SnapshotFrames;
            Debug.Log("[PerfTrace] snapshot → " + path + ".raw (" + SnapshotFrames + " frames)");
        }
        catch (Exception e) { Debug.LogWarning("[PerfTrace] snapshot failed: " + e.Message); _snapshotLeft = 0; }
    }

    void EndSnapshot()
    {
        try
        {
            Profiler.enabled = false;
            Profiler.enableBinaryLog = false;
            Profiler.logFile = "";
            Profiler.enabled = true;           // keep the recorders fed
        }
        catch (Exception e) { Debug.LogWarning("[PerfTrace] snapshot end: " + e.Message); }
        Debug.Log("[PerfTrace] snapshot " + _snapshotName + " written");
        _snapshotName = "";
        _lastLegend = null;
    }

    // ------------------------------------------------------------------ context
    void RefindContext()
    {
        _refindTimer -= Time.unscaledDeltaTime;
        if (_refindTimer > 0f && _cam != null) return;
        _refindTimer = ContextRefindSec;

        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;

        var bodies = NBodySimulation.Bodies;
        if (_moon == null)
            foreach (var b in bodies) if (b != null && b.bodyName == "Constant Companion") { _moon = b; break; }
        if (_moon != null && _moonBase == null)
        {
            foreach (Transform c in _moon.transform) if (c.name == "Tunnel Rig") { _moonBase = Anchor(c); break; }
            _moonBaseBody = _moon;
        }
        if (_village == null)
        {
            var go = GameObject.Find("TOWN-VILLAGE");
            if (go != null) { _village = go.transform; _villageBody = _village.GetComponentInParent<CelestialBody>(); _villageAnchor = Anchor(_village); }
        }
        if (_shuttle == null)
        {
            var go = GameObject.Find("Shuttle_Lander");
            if (go != null) _shuttle = go.transform;
        }
    }

    /// <summary>A pivot can sit anywhere (TOWN-VILLAGE's is at the planet centre),
    /// so measure landmarks from the centre of their renderers instead: an empty
    /// child parked there, which then moves with the planet for free.</summary>
    static Transform Anchor(Transform root)
    {
        var rends = root.GetComponentsInChildren<Renderer>(false);
        if (rends.Length == 0) return root;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        var a = new GameObject("[PerfTrace anchor]");
        a.transform.SetParent(root, true);
        a.transform.position = b.center;
        return a.transform;
    }

    static CelestialBody Nearest(Vector3 p, out float altitude)
    {
        CelestialBody best = null;
        float bestD = float.MaxValue;
        foreach (var b in NBodySimulation.Bodies)
        {
            if (b == null) continue;
            float d = (b.Position - p).magnitude - b.radius;
            if (d < bestD) { bestD = d; best = b; }
        }
        altitude = best != null ? bestD : -1f;
        return best;
    }

    /// <summary>Angle (deg) between the camera forward and the direction to the
    /// target, the distance, and whether a planet is in the way. The target's
    /// OWN body uses a horizon test (things standing on the surface would
    /// otherwise always count as inside their planet); every other body is a
    /// segment-vs-sphere test.</summary>
    static void Look(Camera cam, Transform target, CelestialBody home, out float angle, out float dist, out bool hidden)
    {
        angle = -1f; dist = -1f; hidden = false;
        if (cam == null || target == null) return;
        Vector3 c = cam.transform.position;
        Vector3 p = target.position;
        Vector3 d = p - c;
        dist = d.magnitude;
        if (dist < 1e-3f) { angle = 0f; return; }
        angle = Vector3.Angle(cam.transform.forward, d);

        foreach (var b in NBodySimulation.Bodies)
        {
            if (b == null || b.radius <= 0f) continue;
            Vector3 centre = b.Position;
            float R = b.radius;
            if (b == home)
            {
                // horizon: hidden when the central angle exceeds the sum of the
                // two horizon angles (target gets ~15 m of height allowance for
                // houses / rigs sticking up).
                float dc = (c - centre).magnitude;
                float dp = Mathf.Max((p - centre).magnitude, R + 15f);
                if (dc <= R || dp <= R) continue;
                float limit = Mathf.Acos(Mathf.Clamp(R / dc, -1f, 1f)) + Mathf.Acos(Mathf.Clamp(R / dp, -1f, 1f));
                float central = Vector3.Angle(c - centre, p - centre) * Mathf.Deg2Rad;
                if (central > limit) { hidden = true; return; }
            }
            else
            {
                // segment c→p against a slightly shrunken sphere
                float r = R * 0.98f;
                Vector3 m = c - centre;
                Vector3 dir = d / dist;
                float bq = Vector3.Dot(m, dir);
                float cq = Vector3.Dot(m, m) - r * r;
                if (cq > 0f && bq > 0f) continue;              // sphere is behind the camera
                float disc = bq * bq - cq;
                if (disc < 0f) continue;                        // ray misses
                float tHit = -bq - Mathf.Sqrt(disc);
                if (tHit > 0f && tHit < dist) { hidden = true; return; }
            }
        }
    }

    // ------------------------------------------------------------------ csv
    string RunStamp() => _runStamp ?? (_runStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss"));
    string _runStamp;

    const string Columns =
        "t,frame,scene,dt_ms,mask,mark,snap,body,alt_m,piloting,cx,cy,cz,fx,fy,fz,"
      + "village_ang,village_dist,village_hidden,moon_ang,moon_dist,moon_hidden,moonbase_ang,moonbase_dist,moonbase_hidden,shuttle_ang,shuttle_dist";

    void OpenCsv()
    {
        _csvPath = Path.Combine(_dir, "trace_" + RunStamp() + ".csv");
        try
        {
            _writer = new StreamWriter(_csvPath, false, new System.Text.UTF8Encoding(false), 1 << 16);
            Debug.Log("[PerfTrace] recording → " + _csvPath);
        }
        catch (Exception e) { Debug.LogWarning("[PerfTrace] cannot open " + _csvPath + ": " + e.Message); _writer = null; }
    }

    void CloseCsv()
    {
        if (_writer == null) return;
        try { _writer.Flush(); _writer.Dispose(); } catch { }
        _writer = null;
    }

    void WriteHeader()
    {
        var s = InputSettings.Active;
        string meta = "# PerfTrace v2 " + (Application.isEditor ? "EDITOR" : "BUILD")
            + " unity=" + Application.unityVersion
            + " gpu=" + (SystemInfo.graphicsDeviceName ?? "?").Replace(',', ' ')
            + " res=" + Screen.width + "x" + Screen.height
            + " vsync=" + QualitySettings.vSyncCount + " targetfps=" + Application.targetFrameRate
            + " shadows=" + QualitySettings.shadows + " shadowDist=" + QualitySettings.shadowDistance.ToString("0")
            + " cascades=" + QualitySettings.shadowCascades + " pixelLights=" + QualitySettings.pixelLightCount
            + " msaa=" + QualitySettings.antiAliasing + " lodBias=" + QualitySettings.lodBias.ToString("0.00")
            + " grassScale=" + (s != null ? s.grassRenderScale.ToString("0.00") : "?")
            + "\n";
        _len = 0;
        Raw(meta);
        Raw(Columns);
        for (int i = 0; i < _recs.Length; i++) { Sep(); Raw(_recs[i].column); }
        Nl();
        Emit();
        _headerWritten = true;
    }

    void WriteRow(string sceneName)
    {
        if (_writer == null) OpenCsv();
        if (_writer == null) return;
        if (!_headerWritten) WriteHeader();
        _frame++;

        float alt;
        Vector3 cpos = _cam != null ? _cam.transform.position : Vector3.zero;
        Vector3 fwd = _cam != null ? _cam.transform.forward : Vector3.forward;
        var body = Nearest(cpos, out alt);
        Vector3 rel = body != null ? cpos - body.Position : cpos;

        float vAng, vDist, mAng, mDist, bAng, bDist, sAng, sDist; bool vHid, mHid, bHid, sHid;
        Look(_cam, _villageAnchor != null ? _villageAnchor : _village, _villageBody, out vAng, out vDist, out vHid);
        Look(_cam, _moon != null ? _moon.transform : null, _moon, out mAng, out mDist, out mHid);
        Look(_cam, _moonBase, _moonBaseBody, out bAng, out bDist, out bHid);
        Look(_cam, _shuttle, null, out sAng, out sDist, out sHid);

        _len = 0;
        Fixed(Time.realtimeSinceStartup - _t0, 3); Sep();
        Int(_frame); Sep();
        Str(sceneName); Sep();
        Fixed(Time.unscaledDeltaTime * 1000f, 3); Sep();
        Int(ToggleMask()); Sep();
        Int(_pendingMark); Sep(); _pendingMark = 0;
        Int(_snapshotLeft > 0 ? 1 : 0); Sep();
        Str(body != null ? body.bodyName : "-"); Sep();
        Fixed(alt, 1); Sep();
        Int(Ship.AnyShipPiloted ? 1 : 0); Sep();
        Fixed(rel.x, 1); Sep(); Fixed(rel.y, 1); Sep(); Fixed(rel.z, 1); Sep();
        Fixed(fwd.x, 3); Sep(); Fixed(fwd.y, 3); Sep(); Fixed(fwd.z, 3); Sep();
        Fixed(vAng, 1); Sep(); Fixed(vDist, 0); Sep(); Int(vHid ? 1 : 0); Sep();
        Fixed(mAng, 1); Sep(); Fixed(mDist, 0); Sep(); Int(mHid ? 1 : 0); Sep();
        Fixed(bAng, 1); Sep(); Fixed(bDist, 0); Sep(); Int(bHid ? 1 : 0); Sep();
        Fixed(sAng, 1); Sep(); Fixed(sDist, 0);

        for (int i = 0; i < _recs.Length; i++)
        {
            Sep();
            var r = _recs[i].recorder;
            if (!r.Valid) { Raw("-"); continue; }
            long v = r.LastValue;
            if (_recs[i].isTime) Fixed(v * 1e-6f, 3);                      // ns → ms
            else if (_recs[i].column == "tris_k" || _recs[i].column == "verts_k") Int(v / 1000);
            else Int(v);
        }
        Nl();
        Emit();
    }

    // zero-alloc formatting into _buf --------------------------------------------
    void Emit()
    {
        try { _writer.Write(_buf, 0, _len); } catch (Exception e) { Debug.LogWarning("[PerfTrace] write failed: " + e.Message); CloseCsv(); }
        _len = 0;
    }
    void Sep() { if (_len < _buf.Length) _buf[_len++] = ','; }
    void Nl()  { if (_len < _buf.Length) _buf[_len++] = '\n'; }
    /// <summary>Verbatim (header text, column names).</summary>
    void Raw(string s)
    {
        if (s == null) return;
        for (int i = 0; i < s.Length && _len < _buf.Length; i++) _buf[_len++] = s[i];
    }
    /// <summary>A field value: commas and line breaks become spaces so a name can
    /// never break the row.</summary>
    void Str(string s)
    {
        if (s == null) return;
        for (int i = 0; i < s.Length && _len < _buf.Length; i++)
        {
            char ch = s[i];
            _buf[_len++] = (ch == ',' || ch == '\n' || ch == '\r') ? ' ' : ch;
        }
    }
    void Int(long v)
    {
        if (v < 0) { if (_len < _buf.Length) _buf[_len++] = '-'; v = -v; }
        int start = _len;
        do { if (_len < _buf.Length) _buf[_len++] = (char)('0' + (int)(v % 10)); v /= 10; } while (v > 0);
        for (int i = start, j = _len - 1; i < j; i++, j--) { char t = _buf[i]; _buf[i] = _buf[j]; _buf[j] = t; }
    }
    void Fixed(float f, int decimals)
    {
        if (float.IsNaN(f) || float.IsInfinity(f)) { Raw("nan"); return; }
        if (f < 0f) { if (_len < _buf.Length) _buf[_len++] = '-'; f = -f; }
        long scale = 1;
        for (int i = 0; i < decimals; i++) scale *= 10;
        long whole = (long)f;
        long frac = (long)((f - whole) * scale + 0.5f);
        if (frac >= scale) { whole++; frac -= scale; }
        Int(whole);
        if (decimals == 0) return;
        if (_len < _buf.Length) _buf[_len++] = '.';
        long div = scale / 10;
        while (div > 0) { if (_len < _buf.Length) _buf[_len++] = (char)('0' + (int)((frac / div) % 10)); div /= 10; }
    }

    // ------------------------------------------------------------------ legend
    void BuildUI()
    {
        var canvasGO = new GameObject("Canvas");
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 851;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        canvasGO.AddComponent<GraphicRaycaster>().enabled = false;

        var bgGO = new GameObject("BG");
        bgGO.transform.SetParent(canvasGO.transform, false);
        var bg = bgGO.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);
        bg.raycastTarget = false;
        var bgRT = bg.rectTransform;
        bgRT.anchorMin = new Vector2(1, 1);
        bgRT.anchorMax = new Vector2(1, 1);
        bgRT.pivot = new Vector2(1, 1);
        bgRT.anchoredPosition = new Vector2(-8, -8);
        bgRT.sizeDelta = new Vector2(300, 250);

        var txtGO = new GameObject("Text");
        txtGO.transform.SetParent(bgGO.transform, false);
        _text = txtGO.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_text);
        _text.alignment = TextAlignmentOptions.TopLeft;
        _text.fontSize = 13;
        _text.color = new Color(1f, 0.85f, 0.6f, 1f);
        _text.raycastTarget = false;
        _text.enableWordWrapping = false;
        _text.richText = false;
        var txtRT = _text.rectTransform;
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = new Vector2(8, 4);
        txtRT.offsetMax = new Vector2(-6, -4);
    }

    float _legendTimer;
    void UpdateLegend()
    {
        if (_text == null || !_legendVisible) return;
        _legendTimer -= Time.unscaledDeltaTime;
        if (_legendTimer > 0f && _lastLegend != null) return;
        _legendTimer = 0.5f;

        _sb.Length = 0;
        _sb.Append("PERF TRACE  ").Append(_writer != null ? "recording" : "idle").Append("  mark ").Append(_mark);
        if (_snapshotLeft > 0) _sb.Append("  SNAP ").Append(_snapshotLeft);
        _sb.Append('\n');
        for (int i = 0; i < (int)Toggle.Count; i++)
        {
            _sb.Append(i == _selected ? '>' : ' ').Append(' ');
            _sb.Append("Num").Append(i + 1).Append(' ');
            _sb.Append(_on[i] ? "[ON ] " : "[   ] ");
            _sb.Append(ToggleNames[i]).Append('\n');
        }
        _sb.Append("Num+ = mark   Num- = snapshot\nPgUp/PgDn+Del = toggle   Num/ = hide");
        string s = _sb.ToString();
        if (s != _lastLegend) { _lastLegend = s; _text.SetText(s); }
    }
}
