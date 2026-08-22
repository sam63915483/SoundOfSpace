using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Live watcher for the "walk into a spot and ALL the grass changes brightness /
/// colour, walk back and it returns" pulse.
///
/// ── Why this exists instead of another fix ───────────────────────────────
/// This symptom has now survived four separate theories (grass-light set
/// churn, nearest-N light ranking, light scoring at the player, and the
/// eclipse shadow gate). Each was a plausible mechanism that turned out not to
/// be the one. Guessing again is the wrong move: the symptom is a GLOBAL,
/// STEP-WISE change triggered by crossing a position, and every input capable
/// of doing that is enumerable. So enumerate them, watch them all at once, and
/// let the spot name its own culprit.
///
/// ── How to use ───────────────────────────────────────────────────────────
///   1. Press F2 (works in a BUILD too, not just the editor). A panel appears
///      listing every global that can change grass shading.
///   2. Walk back and forth across the spot. F1 steps through the bisect
///      states (see Bisect) when no uniform flips.
///   3. Whatever flips is highlighted YELLOW and holds "was -> now" for a few
///      seconds. That line is the bug. It is also written to the Console (and
///      so to Player.log in a build) with your position.
///
/// If NOTHING highlights while the grass visibly changes, that is just as
/// useful: it rules out this entire list and points at per-pixel state (the
/// depth pre-pass feeding _CameraDepthTexture, or shadow cascade fitting)
/// rather than a uniform.
///
/// BUILD NOTES: auto-creates without skipping MainMenu, so it exists in builds
/// (CLAUDE.md trap #1); Assembly-CSharp is preserve="all" in link.xml so the
/// stripper cannot drop it; and every change is Debug.Log'd, so a build records
/// it in
///   %AppData%\..\LocalLow\DefaultCompany\Solar System 2\Player.log
///
/// Delete once the pulse is explained — same contract as the 2026-08-18
/// grass-light logger this replaces.
/// </summary>
public class GrassPopDiagnostic : MonoBehaviour
{
    public static GrassPopDiagnostic Instance { get; private set; }

    // F2 because it is one of only two unclaimed function keys. NOT F8 — that
    // is already LightingDebugToolbox.ToggleSunPointLight (which changes grass
    // brightness, i.e. it would confound the very measurement) and
    // CheatCodes.skipToPilotSchool.
    const KeyCode ToggleKey = KeyCode.F2;
    /// How long a changed row stays highlighted.
    const float HoldSeconds = 4f;
    /// Body-renderer shadow scan is the only costly probe; throttle it.
    const float HeavyInterval = 0.25f;

    bool _on;
    readonly Dictionary<string, string> _last = new Dictionary<string, string>();
    readonly Dictionary<string, string> _prevValue = new Dictionary<string, string>();
    readonly Dictionary<string, float> _changedAt = new Dictionary<string, float>();
    readonly List<string> _order = new List<string>();

    InstancedGrassRenderer _grass;
    Light _sunDir;
    CustomPostProcessing _post;
    float _nextHeavy;
    string _casterSummary = "?";
    GUIStyle _styleNormal, _styleHot, _styleHead;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        // Deliberately does NOT skip MainMenu, so it never needs seeding in
        // EnsureGameplaySingletons (CLAUDE.md trap #1) — the same dodge
        // WorldSync/StorageSync/EnemySync use.
        var go = new GameObject("[GrassPopDiagnostic]");
        DontDestroyOnLoad(go);
        go.AddComponent<GrassPopDiagnostic>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (Input.GetKeyDown(ToggleKey))
        {
            _on = !_on;
            if (!_on) RestoreAll();
            _last.Clear(); _changedAt.Clear(); _prevValue.Clear(); _order.Clear();
            Debug.Log($"[GrassPop] watcher {(_on ? "ON" : "off")} — walk across the spot and read the yellow row.");
        }
        if (!_on) return;

        Bisect();

        if (_grass == null) _grass = FindObjectOfType<InstancedGrassRenderer>();
        if (_sunDir == null)
        {
            var c = FindObjectOfType<SunShadowCaster>();
            if (c != null) _sunDir = c.GetComponent<Light>();
        }
        if (Time.unscaledTime >= _nextHeavy) { _nextHeavy = Time.unscaledTime + HeavyInterval; ScanCasters(); }

        Sample();
    }

    // ── bisect: turn ONE system off and see if the pop survives ─────────────
    //
    // The watcher above proved no uniform changes at the spot, which rules out
    // that whole class and leaves per-pixel / per-object state. There is no way
    // to read that off a number, so bisect it instead: kill one system at a
    // time at the spot and see which removal makes the pop STOP. That one is at
    // fault. Keypad 1-9 are the only unclaimed keys in the project.
    //
    // Suggested order — 1 halves the problem on its own:
    //   [1] atmosphere/ocean post-process. It is what tints everything from the
    //       depth texture. Pop gone with it off => the bug is depth-driven
    //       post-processing. Pop still there => it is the grass shading itself.
    //   [2] grass depth pre-pass. Grass missing from _CameraDepthTexture gets
    //       washed to SKY COLOUR by the atmosphere — brighter and bluer, which
    //       is exactly the reported symptom. If toggling this REPRODUCES the pop
    //       on demand, the bug is the pre-pass dropping out at that spot.
    //   [3] sun shadows. Removes every cast shadow, incl. terrain self-shadowing.
    //   [4] grass entirely. Confirms the change is even in the grass and not
    //       something behind it.
    /// 0 = everything on. 1..4 = that one system disabled, the rest restored.
    int _bisect;
    LightShadows _sunShadowsOriginal = LightShadows.Soft;
    bool _capturedSunShadows;

    static readonly string[] BisectNames =
    {
        "nothing disabled",
        "atmosphere/ocean POST off",
        "grass depth PRE-PASS off",
        "sun SHADOWS off",
        "NON-SUN directional lights off",
    };

    /// How many objects the CURRENT step actually touched. Shown in the panel:
    /// a step reporting 0 did NOTHING and its result must be thrown away.
    ///
    /// ⚠️ THIS EXISTS BECAUSE A STEP SILENTLY DID NOTHING. Step 1 used
    /// FindObjectOfType<CustomPostProcessing>() — singular. That component
    /// lives on the PLAYER PREFAB's camera and there are several instances
    /// alive (player cam, map cam, network players), so the single find
    /// returned some other camera's chain and toggling it changed nothing on
    /// screen. Sam spotted it — "turning off the atmosphere doesn't remove the
    /// atmosphere" — and the whole test result was void.
    ///
    /// A bisect step that cannot prove it did something is worse than no test,
    /// because a false "no change" retires a suspect that was never tried.
    int _affected;
    string _affectedNote = "";

    void Bisect()
    {
        if (_sunDir != null && !_capturedSunShadows)
        {
            _sunShadowsOriginal = _sunDir.shadows;
            _capturedSunShadows = true;
        }

        // F1 CYCLES. One key, no numpad, no modifiers — the keypad aliases below
        // need a numpad and every modifier in this project is already bound
        // (Ctrl is held by gameplay, top-row 1-4 are the hotbar and dialogue
        // choices). F1 is the only other unclaimed key, so it carries this.
        bool changed = false;
        if (Input.GetKeyDown(KeyCode.F1)) { _bisect = (_bisect + 1) % BisectNames.Length; changed = true; }
        // Direct aliases for anyone who does have a numpad.
        if (Input.GetKeyDown(KeyCode.Keypad1)) { _bisect = _bisect == 1 ? 0 : 1; changed = true; }
        if (Input.GetKeyDown(KeyCode.Keypad2)) { _bisect = _bisect == 2 ? 0 : 2; changed = true; }
        if (Input.GetKeyDown(KeyCode.Keypad3)) { _bisect = _bisect == 3 ? 0 : 3; changed = true; }
        if (Input.GetKeyDown(KeyCode.Keypad4)) { _bisect = _bisect == 4 ? 0 : 4; changed = true; }
        if (!changed) return;

        // Always restore everything, then disable exactly one — so the state can
        // never drift into "two things off and I forgot which".
        _affected = 0;
        _affectedNote = "";

        int posts = ApplyPost(_bisect != 1);
        InstancedGrassRenderer.DepthPrePassEnabled = _bisect != 2;
        int suns = ApplySunShadows(_bisect != 3);
        int dirs = ApplyExtraDirectionals(_bisect != 4);

        switch (_bisect)
        {
            case 0: _affected = 1; _affectedNote = "everything restored"; break;
            case 1: _affected = posts; _affectedNote = $"{posts} CustomPostProcessing component(s)"; break;
            case 2: _affected = _grass != null ? 1 : 0; _affectedNote = _grass != null
                        ? "grass depth CommandBuffer detached" : "NO InstancedGrassRenderer FOUND"; break;
            case 3: _affected = suns; _affectedNote = suns > 0
                        ? "sun shadows -> None" : "NO SunShadowCaster light FOUND"; break;
            case 4: _affected = dirs; _affectedNote = $"{dirs} non-sun directional light(s)"; break;
        }

        Log($"BISECT -> {BisectNames[_bisect]}  [affected {_affected}: {_affectedNote}]");
    }

    /// EVERY post-process chain, including ones on inactive objects.
    /// FindObjectOfType (singular) is the trap that voided step 1 — see _affected.
    int ApplyPost(bool on)
    {
        var all = FindObjectsOfType<CustomPostProcessing>(true);
        int n = 0;
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null) { all[i].enabled = on; n++; }
        _post = all.Length > 0 ? all[0] : null;
        return n;
    }

    int ApplySunShadows(bool on)
    {
        if (_sunDir == null) return 0;
        _sunDir.shadows = on ? _sunShadowsOriginal : LightShadows.None;
        return 1;
    }

    /// Every DIRECTIONAL light that is NOT the sun shadow caster.
    ///
    /// ⚠️ THIS SCENE HAS TWO: "Sun Shadow Caster" (intensity 1.0) and
    /// "AstronautReflectLight" (intensity 1.3 — BRIGHTER), both RenderMode Auto.
    /// In forward rendering only ONE directional gets the BASE PASS, and
    /// DrawMeshInstanced grass never receives the ForwardAdd pass (the whole
    /// reason every other light in CG_SimpleGrass is faked by hand). So the
    /// grass is lit by whichever directional wins that slot — and the shader
    /// reads _WorldSpaceLightPos0 as "the sun" to derive the day factor, the
    /// wrap floor, the terminator glow and the tip shadow-lift.
    readonly List<Light> _extraDir = new List<Light>();

    int ApplyExtraDirectionals(bool on)
    {
        _extraDir.Clear();
        foreach (var l in FindObjectsOfType<Light>(true))
        {
            if (l == null || l.type != LightType.Directional) continue;
            if (_sunDir != null && l == _sunDir) continue;
            _extraDir.Add(l);
            l.enabled = on;
        }
        return _extraDir.Count;
    }

    /// Put everything back if the watcher is closed mid-test, so a disabled
    /// system can never be left off and mistaken for a real change later.
    void RestoreAll()
    {
        _bisect = 0;
        ApplyPost(true);
        InstancedGrassRenderer.DepthPrePassEnabled = true;
        ApplySunShadows(true);
        ApplyExtraDirectionals(true);
    }

    void Log(string msg)
    {
        var p = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        Debug.Log($"[GrassPop] {msg}    (camera {p.x:0.#},{p.y:0.#},{p.z:0.#})");
    }

    string BisectLine()
    {
        if (_bisect == 0) return "BISECT  [F1] cycle  →  <color=#9AA0A6>nothing disabled</color>";
        string proof = _affected > 0
            ? $"<color=#7BD88F>affected {_affected} — {_affectedNote}</color>"
            : $"<color=#FF6B6B>DID NOTHING ({_affectedNote}) — this test is VOID</color>";
        return $"BISECT  [F1] cycle  →  <color=#FF6B6B>{BisectNames[_bisect]}</color>   {proof}";
    }

    // ── every global that can change grass shading, in one place ────────────

    void Sample()
    {
        // Shader globals the grass shader actually reads.
        Put("grass/_GrassPointLightCount", Shader.GetGlobalFloat("_GrassPointLightCount").ToString("0"));
        Put("grass/_GrassSunColor", Fmt(Shader.GetGlobalVector("_GrassSunColor")));
        Put("grass/_GrassSunRange", Shader.GetGlobalFloat("_GrassSunRange").ToString("0.#"));
        Put("grass/_FlashlightColor", Fmt(Shader.GetGlobalVector("_FlashlightColor")));
        Put("grass/_GrassPlanetCenter", Fmt(Shader.GetGlobalVector("_GrassPlanetCenter")));
        Put("grass/_GrassSpotCenter", Fmt(Shader.GetGlobalVector("_GrassSpotCenter")));

        // Ambient — grass gets it through the forward base pass like everything else.
        Put("ambient/mode", RenderSettings.ambientMode.ToString());
        Put("ambient/light", Fmt(RenderSettings.ambientLight));
        Put("ambient/intensity", RenderSettings.ambientIntensity.ToString("0.###"));
        Put("ambient/sky", Fmt(RenderSettings.ambientSkyColor));
        Put("ambient/equator", Fmt(RenderSettings.ambientEquatorColor));
        Put("ambient/ground", Fmt(RenderSettings.ambientGroundColor));
        Put("ambient/reflectionIntensity", RenderSettings.reflectionIntensity.ToString("0.###"));
        Put("fog/on", RenderSettings.fog.ToString());

        // The sun's directional light — _LightColor0 and atten for every blade.
        if (_sunDir != null)
        {
            Put("sun/enabled", _sunDir.enabled.ToString());
            Put("sun/color", Fmt(_sunDir.color));
            Put("sun/intensity", _sunDir.intensity.ToString("0.###"));
            Put("sun/shadows", _sunDir.shadows.ToString());
            Put("sun/shadowStrength", _sunDir.shadowStrength.ToString("0.###"));
            Put("sun/bounce", _sunDir.bounceIntensity.ToString("0.###"));
            Put("sun/cullingMask", _sunDir.cullingMask.ToString("X"));
            Put("sun/fwdRot", Fmt(_sunDir.transform.forward));
        }

        // EVERY directional light, not just the sun caster. Only one of them can
        // hold the forward BASE PASS, which is the only light instanced grass
        // ever sees, so a change here repaints the entire field.
        int dirCount = 0;
        var sb2 = new StringBuilder();
        foreach (var l in FindObjectsOfType<Light>())
        {
            if (l == null || l.type != LightType.Directional) continue;
            dirCount++;
            if (sb2.Length > 0) sb2.Append(" | ");
            sb2.Append(l.name).Append(l.enabled ? "=on" : "=OFF")
               .Append(' ').Append(l.intensity.ToString("0.##"))
               .Append(' ').Append(l.renderMode);
        }
        Put("directional/count", dirCount.ToString());
        Put("directional/all", sb2.ToString());

        // Shadow settings — a cascade/distance change re-fits the whole map.
        Put("quality/shadowDistance", QualitySettings.shadowDistance.ToString("0.#"));
        Put("quality/shadowCascades", QualitySettings.shadowCascades.ToString());
        Put("quality/shadowResolution", QualitySettings.shadowResolution.ToString());
        Put("quality/shadowProjection", QualitySettings.shadowProjection.ToString());
        Put("quality/shadowmaskMode", QualitySettings.shadowmaskMode.ToString());
        Put("quality/pixelLightCount", QualitySettings.pixelLightCount.ToString());
        Put("quality/level", QualitySettings.GetQualityLevel().ToString());

        // Who is allowed to cast into the directional shadow map (EclipseShadowGate).
        Put("shadowCasters/bodies", _casterSummary);

        // Grass material knobs (a script could be writing these).
        var m = _grass != null ? _grass.grassMaterial : null;
        if (m != null)
        {
            PutMat(m, "_ShadowFill"); PutMat(m, "_TipSunlight"); PutMat(m, "_TerminatorGlow");
            PutMat(m, "_SunFillResponse"); PutMat(m, "_AmbientBoost"); PutMat(m, "_PointLightBoost");
            PutMat(m, "_LanternGrassRadius"); PutMat(m, "_LanternGrassTail"); PutMat(m, "_SpotGrassReach");
            Put("grassMat/renderQueue", m.renderQueue.ToString());
        }
        if (_grass != null)
        {
            Put("grassRenderer/spawnRadius", _grass.spawnRadius.ToString("0.#"));
            Put("grassRenderer/receiveShadows", _grass.receiveShadows.ToString());
        }

        // Position context: a spatial threshold usually correlates with one of
        // these, so seeing them beside the flipping row places it immediately.
        var camT = Camera.main != null ? Camera.main.transform : null;
        if (camT != null && _grass != null && _grass.transform != null)
        {
            Vector3 bodyPos = Shader.GetGlobalVector("_GrassPlanetCenter");
            Put("pos/altitudeFromBodyCentre", (camT.position - bodyPos).magnitude.ToString("0.0"));
        }

        // Camera state that changes how the atmosphere composites the grass.
        var cam = Camera.main;
        if (cam != null)
        {
            Put("camera/depthTextureMode", cam.depthTextureMode.ToString());
            Put("camera/allowHDR", cam.allowHDR.ToString());
            Put("camera/allowMSAA", cam.allowMSAA.ToString());
            Put("camera/farClip", cam.farClipPlane.ToString("0"));
            Put("camera/renderingPath", cam.actualRenderingPath.ToString());
        }
    }

    /// Which celestial bodies currently have ANY renderer casting. This is the
    /// thing EclipseShadowGate toggles, and a directional shadow map re-fits
    /// around whatever set of casters exists.
    void ScanCasters()
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null || bodies.Length == 0) { _casterSummary = "(no bodies)"; return; }
        var sb = new StringBuilder();
        var scratch = new List<Renderer>();
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            scratch.Clear();
            b.GetComponentsInChildren(true, scratch);
            int on = 0;
            for (int r = 0; r < scratch.Count; r++)
                if (scratch[r] != null && scratch[r].shadowCastingMode != ShadowCastingMode.Off) on++;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(b.name.Length > 6 ? b.name.Substring(0, 6) : b.name).Append(':').Append(on);
        }
        _casterSummary = sb.ToString();
    }

    // ── change detection ────────────────────────────────────────────────────

    void Put(string key, string value)
    {
        if (_last.TryGetValue(key, out string old))
        {
            if (old == value) return;
            _prevValue[key] = old;
            _changedAt[key] = Time.unscaledTime;
            var p = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            Debug.Log($"[GrassPop] {key}: {old}  ->  {value}    (camera {p.x:0.#},{p.y:0.#},{p.z:0.#})");
        }
        else _order.Add(key);
        _last[key] = value;
    }

    void PutMat(Material m, string prop)
    {
        if (m.HasProperty(prop)) Put("grassMat/" + prop, m.GetFloat(prop).ToString("0.###"));
    }

    static string Fmt(Vector4 v) => $"{v.x:0.###},{v.y:0.###},{v.z:0.###}";
    static string Fmt(Vector3 v) => $"{v.x:0.##},{v.y:0.##},{v.z:0.##}";
    static string Fmt(Color c) => $"{c.r:0.###},{c.g:0.###},{c.b:0.###}";

    // ── readout ─────────────────────────────────────────────────────────────

    void OnGUI()
    {
        if (!_on) return;
        if (_styleNormal == null)
        {
            _styleNormal = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true };
            _styleHot = new GUIStyle(_styleNormal) { fontStyle = FontStyle.Bold };
            _styleHead = new GUIStyle(_styleNormal) { fontStyle = FontStyle.Bold };
        }

        // Builds run at whatever the monitor is; at 1440p/4K an unscaled IMGUI
        // panel is unreadable. Scale with height so it stays legible.
        float scale = Mathf.Max(1f, Screen.height / 1080f);
        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        float w = 560f, lh = 13f;
        GUI.Box(new Rect(8, 8, w, lh * (_order.Count + 5) + 12), GUIContent.none);
        GUI.Label(new Rect(14, 12, w, lh + 4),
            "[F8] grass-pop watcher — walk the spot; the YELLOW row is the culprit", _styleHead);

        float y = 12 + lh + 4;
        for (int i = 0; i < _order.Count; i++)
        {
            string k = _order[i];
            _last.TryGetValue(k, out string v);
            bool hot = _changedAt.TryGetValue(k, out float t) && Time.unscaledTime - t < HoldSeconds;
            string text = hot
                ? $"<color=#FFD400>{k} = {v}   (was {(_prevValue.TryGetValue(k, out var pv) ? pv : "?")})</color>"
                : $"<color=#9AA0A6>{k}</color> = {v}";
            GUI.Label(new Rect(14, y, w - 12, lh + 2), text, hot ? _styleHot : _styleNormal);
            y += lh;
        }

        GUI.Label(new Rect(14, y + 2, w - 12, lh + 2),
            "<color=#9AA0A6>no row flips while the grass changes? then it is NOT a uniform. "
            + "Use the bisect keys below.</color>", _styleNormal);
        GUI.Label(new Rect(14, y + 2 + lh, w - 12, lh + 2), BisectLine(), _styleHead);
        GUI.Label(new Rect(14, y + 2 + lh * 2, w - 12, lh + 2),
            "<color=#9AA0A6>press F1 to step through them; redo the spot each time. "
            + "Whichever removal makes the pop STOP is the cause.</color>",
            _styleNormal);

        GUI.matrix = oldMatrix;
    }
}
