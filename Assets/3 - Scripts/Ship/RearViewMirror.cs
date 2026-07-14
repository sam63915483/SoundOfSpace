using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// A cockpit screen: while the ship is PARKED it's an opaque blue scan-line
/// computer display (ship status board), while the ship is PILOTED it fades
/// into a rear-view camera feed.
///
/// The camera is a normal enabled camera (toggled at renderHz) rather than a
/// manual Render() — manual renders miss Graphics.DrawMeshInstanced
/// submissions (space dust), enabled cameras in the render loop don't. It only
/// renders while this ship is piloted, so parked ships pay zero GPU.
public class RearViewMirror : MonoBehaviour
{
    public enum IdleContent { ShipComponents, FuelStatus, Combined }

    public Camera sourceCamera;
    public int resolution = 384;
    public float renderHz = 30f;
    [Tooltip("Flip the feed left-right like a real mirror. OFF = raw camera feed (reads correctly with rear-angled side cameras).")]
    public bool mirrorHorizontal = false;
    [Tooltip("What this screen shows while the ship is not being piloted.")]
    public IdleContent idleContent = IdleContent.ShipComponents;
    [Tooltip("EXPERIMENTAL: copy the main camera's ocean/atmosphere post-processing onto the mirror camera so water and sky render properly. Untick if the mirrors glitch.")]
    public bool renderPlanetEffects = true;

    static readonly Color IdleTint = new Color(0.30f, 0.62f, 0.95f);
    static readonly Color TextColor = new Color(0.72f, 0.90f, 1.00f);
    static readonly Color LabelBlue = new Color(0.62f, 0.85f, 1.00f);
    static readonly Color GoodGreen = new Color(0.55f, 1.00f, 0.69f);
    static readonly Color BadRed = new Color(1.00f, 0.35f, 0.35f);
    static readonly Color DimGray = new Color(0.50f, 0.56f, 0.63f);
    const string GoodColor = "#8dffb0";
    const string BadColor = "#ff5a5a";
    const string DimColor = "#8a97a8";

    static Texture2D s_scanlines;

    Ship _ship;
    ThrusterDetachOnImpact _parts;
    SolarPanelCharger _charger;
    InputSettings _input;    // for the 30/60 Hz mirror setting
    RenderTexture _rt;
    Material _mat;
    bool _mirrorMode;        // what the material is currently configured as
    float _level = 1f;       // fade level of the current mode (0..1)
    float _nextRenderAt, _nextTextAt;
    bool _fxCopied;

    // ── idle display objects (Option B "split cockpit" status board) ──
    // A glow quad = black Standard material whose color lives in the emission
    // channel, so everything on the screen fades with the same _level knob.
    class Glow { public Material mat; public Color color; public Transform tf; }

    GameObject _idleRoot;
    readonly List<TextMeshPro> _idleTexts = new List<TextMeshPro>();
    readonly List<Glow> _glows = new List<Glow>();
    TextMeshPro _text;                 // single-text layout (non-Combined modes)
    TextMeshPro[] _statusTexts;
    TextMeshPro _bigPct, _chargeText;
    Glow[] _lights;
    Glow _barFill;
    GameObject[] _partKids;
    bool[] _rowGood;
    bool _rowInit, _blinkOn, _chargingNow, _lastCharging;
    int _lastPct = -1;
    float _barLeftX, _barMaxW, _barY, _barFillZ;

    static readonly string[] PartLabels = { "L THRUST", "R THRUST", "DISH", "SOLAR", "L NET", "R NET" };

    void Awake()
    {
        _ship = GetComponentInParent<Ship>();
        _parts = GetComponentInParent<ThrusterDetachOnImpact>();
        if (_parts == null && _ship != null) _parts = _ship.GetComponentInChildren<ThrusterDetachOnImpact>(true);
        _charger = GetComponentInParent<SolarPanelCharger>();
        if (_charger == null && _ship != null) _charger = _ship.GetComponentInChildren<SolarPanelCharger>(true);
        _input = FindObjectOfType<InputSettings>();
        if (_parts != null)
            _partKids = new[]
            {
                _parts.leftThrusterChild, _parts.rightThrusterChild, _parts.dishChild,
                _parts.solarPanelChild, _parts.leftSpaceNetChild, _parts.rightSpaceNetChild
            };

        if (sourceCamera != null)
        {
            _rt = new RenderTexture(resolution, resolution, 16);
            _rt.name = name + "_RT";
            sourceCamera.targetTexture = _rt;
            sourceCamera.enabled = false;
        }

        // One opaque Standard material for both modes — black albedo, the
        // content lives in the emission channel so it glows like a screen and
        // can fade. Opaque queue = never see-through, hides behind atmosphere.
        var r = GetComponent<Renderer>();
        if (r != null)
        {
            _mat = new Material(Shader.Find("Standard"));
            _mat.color = new Color(0.01f, 0.012f, 0.02f, 1f);
            _mat.SetFloat("_Glossiness", 0.65f);
            _mat.EnableKeyword("_EMISSION");
            r.material = _mat;
        }

        BuildIdleDisplay();
        ConfigureIdleMaterial();
        _mirrorMode = false;
        _level = 1f;
    }

    void Start()
    {
        // Copy the main camera's planet post-processing (ocean/atmosphere)
        // onto the mirror camera. Purely additive + runtime-only; guarded so a
        // failure just leaves the mirror without water rather than erroring.
        if (renderPlanetEffects && sourceCamera != null) TryCopyPlanetEffects();
    }

    void TryCopyPlanetEffects()
    {
        if (_fxCopied) return;
        var mainCam = Camera.main;
        if (mainCam == null) return;
        _fxCopied = true;
        sourceCamera.depthTextureMode = DepthTextureMode.Depth;
        CopyEffect(mainCam, "OceanMaskRenderer");
        CopyEffect(mainCam, "CustomPostProcessing");
    }

    void CopyEffect(Camera mainCam, string typeName)
    {
        try
        {
            var src = mainCam.GetComponent(typeName);
            if (src == null) return;
            if (sourceCamera.GetComponent(typeName) != null) return;
            var dst = sourceCamera.gameObject.AddComponent(src.GetType());
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(src), dst);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[RearViewMirror] couldn't copy {typeName} onto {sourceCamera.name}: {e.Message}");
        }
    }

    // ── mode switching ──

    void LateUpdate()
    {
        bool piloted = _ship != null && Ship.PilotedInstance == _ship;

        // Camera renders only while piloted, throttled by the settings-menu
        // refresh option (CAMERA tab → REAR-VIEW 60HZ). Toggling `enabled`
        // keeps it inside the normal render loop (dust renders).
        if (sourceCamera != null && _rt != null)
        {
            float hz = _input != null ? (_input.mirror60Hz ? 60f : 30f) : renderHz;
            bool due = piloted && Time.unscaledTime >= _nextRenderAt;
            if (due) _nextRenderAt = Time.unscaledTime + 1f / Mathf.Max(1f, hz);
            sourceCamera.enabled = due;
        }

        // Fade out the current mode, flip configuration at zero, fade back in.
        bool wantMirror = piloted;
        float dt = Time.deltaTime * 5f;
        if (wantMirror != _mirrorMode)
        {
            _level -= dt;
            if (_level <= 0f)
            {
                _mirrorMode = wantMirror;
                if (_mirrorMode) ConfigureMirrorMaterial();
                else ConfigureIdleMaterial();
                _level = 0f;
            }
        }
        else if (_level < 1f)
        {
            _level = Mathf.Min(1f, _level + dt);
        }
        ApplyLevel();

        // Idle content refresh (cheap, 2 Hz, only while visible).
        if (!_mirrorMode && Time.unscaledTime >= _nextTextAt)
        {
            _nextTextAt = Time.unscaledTime + 0.5f;
            switch (idleContent)
            {
                case IdleContent.Combined:
                    RefreshStatusBoard();
                    break;
                case IdleContent.ShipComponents:
                    if (_text != null) _text.text = BuildComponentsText();
                    break;
                default:
                    if (_text != null) _text.text = BuildFuelText();
                    break;
            }
        }
    }

    void ConfigureIdleMaterial()
    {
        if (_mat == null) return;
        _mat.SetTexture("_EmissionMap", Scanlines());
        _mat.mainTextureScale = Vector2.one;
        _mat.mainTextureOffset = Vector2.zero;
        _mat.SetTextureScale("_EmissionMap", new Vector2(1f, 14f));   // tile the lines
        _mat.SetTextureOffset("_EmissionMap", Vector2.zero);
        if (_idleRoot != null) _idleRoot.SetActive(true);
    }

    void ConfigureMirrorMaterial()
    {
        if (_mat == null) return;
        _mat.SetTexture("_EmissionMap", _rt);
        _mat.SetTextureScale("_EmissionMap", mirrorHorizontal ? new Vector2(-1f, 1f) : Vector2.one);
        _mat.SetTextureOffset("_EmissionMap", mirrorHorizontal ? new Vector2(1f, 0f) : Vector2.zero);
        if (_idleRoot != null) _idleRoot.SetActive(false);
    }

    void ApplyLevel()
    {
        if (_mat != null)
        {
            Color tint = _mirrorMode ? Color.white : IdleTint;
            _mat.SetColor("_EmissionColor", tint * _level);
        }
        if (_mirrorMode || _idleRoot == null || !_idleRoot.activeSelf) return;

        for (int i = 0; i < _idleTexts.Count; i++)
        {
            var t = _idleTexts[i];
            if (t == null) continue;
            float a = _level;
            // "CHARGING" gently pulses like the mockup.
            if (t == _chargeText && _chargingNow) a *= 0.60f + 0.40f * Mathf.Sin(Time.time * 4f);
            t.alpha = a;
        }
        for (int i = 0; i < _glows.Count; i++)
        {
            var g = _glows[i];
            if (g.mat != null) g.mat.SetColor("_EmissionColor", g.color * _level);
        }
    }

    // ── idle display construction ──

    void BuildIdleDisplay()
    {
        // The mesh's visible (front) face is its +Z side — the pilot reads
        // the screen from +Z. TMP text (and Unity quads) read from their own
        // -Z, so everything is spun 180°, floats a fixed ~1.4cm WORLD distance
        // off the glass on the +Z side, and viewer-left is local +X.
        _idleRoot = new GameObject("IdleDisplay");
        _idleRoot.transform.SetParent(transform, false);
        float z = 0.014f / Mathf.Max(0.02f, Mathf.Abs(transform.lossyScale.z));

        // NOTE: TMP world-space text renders at ~0.11 local units per font
        // point (fontSize 60 ≈ a 7-unit-tall line) — size fonts accordingly,
        // NOT 1:1 against the rect units.
        if (idleContent == IdleContent.Combined) BuildStatusBoard(z);
        else _text = MakeIdleText("IdleText", new Vector3(0f, 0f, z), new Vector2(0.80f, 0.44f), 48f, TextAlignmentOptions.TopLeft);
    }

    // Option B "split cockpit": left = parts checklist with glowing indicator
    // lights (red ones blink), right = huge fuel %, CHARGING readout, and a
    // charge bar under it. Divider column between. All positions are in the
    // screen mesh's local units (glass spans x -0.5..0.5, flat-edge y ±0.25,
    // arch above to ~0.4).
    void BuildStatusBoard(float z)
    {
        // The glass is a horizontal ARC, concave toward the pilot — flat
        // content placed at the center's depth sinks INTO the glass toward
        // the edges (verified: everything past |x|≈0.34 vanished). Lift each
        // element by the arc's sagitta at its widest |x|, with margin.
        float CurveZ(float xMax)
        {
            const float R = 1.6f;   // arc radius in mesh units (35° arc, chord 1)
            float sag = R - Mathf.Sqrt(Mathf.Max(0.01f, R * R - xMax * xMax));
            return z + 1.3f * sag;
        }

        var title = MakeIdleText("Title", new Vector3(0f, 0.28f, CurveZ(0.20f)), new Vector2(0.70f, 0.09f), 40f, TextAlignmentOptions.Center);
        title.text = "SHIP STATUS";
        title.color = DimGray;
        title.characterSpacing = 14f;

        int n = PartLabels.Length;
        _statusTexts = new TextMeshPro[n];
        _lights = new Glow[n];
        _rowGood = new bool[n];
        // Round lights: compensate the screen's non-uniform world scale.
        float lightW = 0.040f;
        float lightH = lightW * Mathf.Abs(transform.lossyScale.x) / Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.y));

        const float rowTop = 0.22f, rowBottom = -0.24f;
        float rowH = (rowTop - rowBottom) / n;
        for (int i = 0; i < n; i++)
        {
            float y = rowTop - rowH * (i + 0.5f);
            _lights[i] = MakeGlowQuad("Light" + i, new Vector3(0.45f, y, CurveZ(0.47f)), new Vector2(lightW, lightH), DimGray * 0.3f);

            var name = MakeIdleText("Name" + i, new Vector3(0.195f, y, CurveZ(0.42f)), new Vector2(0.43f, rowH), 60f, TextAlignmentOptions.MidlineLeft);
            name.text = PartLabels[i];
            name.color = LabelBlue;

            _statusTexts[i] = MakeIdleText("Status" + i, new Vector3(0.175f, y, CurveZ(0.15f)), new Vector2(0.58f, rowH), 60f, TextAlignmentOptions.MidlineRight);
            _statusTexts[i].fontStyle = FontStyles.Bold;
        }

        MakeGlowQuad("Divider", new Vector3(-0.14f, -0.01f, CurveZ(0.15f)), new Vector2(0.005f, 0.47f), LabelBlue * 0.35f);

        const float rx = -0.32f;   // right column center (viewer-right)
        var fuelWord = MakeIdleText("FuelWord", new Vector3(rx, 0.175f, CurveZ(0.38f)), new Vector2(0.32f, 0.07f), 34f, TextAlignmentOptions.Center);
        fuelWord.text = "FUEL";
        fuelWord.color = DimGray;
        fuelWord.characterSpacing = 18f;

        _bigPct = MakeIdleText("FuelPct", new Vector3(rx, 0.055f, CurveZ(0.45f)), new Vector2(0.34f, 0.15f), 110f, TextAlignmentOptions.Center);
        _bigPct.fontStyle = FontStyles.Bold;
        _bigPct.color = Color.white;

        _chargeText = MakeIdleText("Charge", new Vector3(rx, -0.075f, CurveZ(0.47f)), new Vector2(0.34f, 0.06f), 34f, TextAlignmentOptions.Center);
        _chargeText.fontStyle = FontStyles.Bold;

        _barY = -0.165f;
        _barMaxW = 0.29f;
        _barLeftX = rx + _barMaxW * 0.5f;   // viewer-left end of the bar
        _barFillZ = CurveZ(0.47f) + 0.004f; // fill sits just above its backing
        MakeGlowQuad("BarBg", new Vector3(rx, _barY, CurveZ(0.47f)), new Vector2(0.30f, 0.045f), new Color(0.10f, 0.17f, 0.26f));
        _barFill = MakeGlowQuad("BarFill", new Vector3(_barLeftX, _barY, _barFillZ), new Vector2(0.001f, 0.034f), new Color(0.43f, 0.76f, 1f));
    }

    TextMeshPro MakeIdleText(string name, Vector3 localPos, Vector2 size, float fontSize, TextAlignmentOptions align)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_idleRoot.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // face the pilot on the +Z side
        // Healthy glyph sizes scaled down by the transform (TMP degenerates
        // at sub-1pt sizes). Fixed size, no wrapping, no auto-sizing.
        const float shrink = 0.01f;
        go.transform.localScale = Vector3.one * shrink;
        var t = go.AddComponent<TextMeshPro>();
        t.rectTransform.sizeDelta = size / shrink;
        t.fontSize = fontSize;
        t.enableAutoSizing = false;
        t.enableWordWrapping = false;
        t.color = TextColor;
        t.alignment = align;
        t.richText = true;
        t.text = "";
        _idleTexts.Add(t);
        return t;
    }

    Glow MakeGlowQuad(string name, Vector3 localPos, Vector2 size, Color emission)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(_idleRoot.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // face the pilot on the +Z side
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var m = new Material(Shader.Find("Standard"));
        m.color = Color.black;
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission);
        r.material = m;
        var g = new Glow { mat = m, color = emission, tf = go.transform };
        _glows.Add(g);
        return g;
    }

    // ── idle display refresh ──

    void RefreshStatusBoard()
    {
        _blinkOn = !_blinkOn;
        int n = PartLabels.Length;
        for (int i = 0; i < n; i++)
        {
            GameObject kid = _partKids != null && i < _partKids.Length ? _partKids[i] : null;
            bool has = kid != null;
            bool good = has && kid.activeSelf;
            if (!_rowInit || good != _rowGood[i])
            {
                _rowGood[i] = good;
                _statusTexts[i].text = has ? (good ? "GOOD" : "GONE") : "--";
                _statusTexts[i].color = has ? (good ? GoodGreen : BadRed) : DimGray;
            }
            // Lights: solid green when good, blinking red when gone.
            _lights[i].color = !has ? DimGray * 0.3f
                             : good ? GoodGreen * 1.4f
                             : (_blinkOn ? BadRed * 1.6f : BadRed * 0.22f);
        }
        _rowInit = true;

        float pct = _ship != null ? Mathf.Clamp01(_ship.PowerPercent) : 0f;
        int p = Mathf.RoundToInt(pct * 100f);
        if (p != _lastPct)
        {
            _lastPct = p;
            _bigPct.text = p + "%";
        }

        float w = Mathf.Max(0.001f, _barMaxW * pct);
        _barFill.tf.localScale = new Vector3(w, 0.034f, 1f);
        _barFill.tf.localPosition = new Vector3(_barLeftX - w * 0.5f, _barY, _barFillZ);

        bool charging = _charger != null && _charger.IsCharging;
        if (!_rowInit || charging != _lastCharging || _chargeText.text.Length == 0)
        {
            _chargeText.text = charging ? "CHARGING" : "NOT CHARGING";
            _chargeText.color = charging ? GoodGreen : DimGray;
        }
        _lastCharging = charging;
        _chargingNow = charging;
    }

    // ── single-text idle layouts (ShipComponents / FuelStatus modes) ──

    string BuildComponentsText()
    {
        var sb = new System.Text.StringBuilder(256);
        sb.Append("<u>SHIP SYSTEMS</u>\n");
        if (_partKids == null)
        {
            sb.Append($"<color={DimColor}>NO PART DATA</color>");
            return sb.ToString();
        }
        for (int i = 0; i < PartLabels.Length; i++)
        {
            var kid = _partKids[i];
            if (kid == null) continue;   // this ship variant doesn't have the part slot
            bool good = kid.activeSelf;
            sb.Append(PartLabels[i]).Append("  ")
              .Append(good ? $"<color={GoodColor}>GOOD</color>" : $"<color={BadColor}>MISSING</color>")
              .Append('\n');
        }
        return sb.ToString();
    }

    string BuildFuelText()
    {
        var sb = new System.Text.StringBuilder(128);
        sb.Append("<u>FUEL</u>\n");
        if (_ship == null)
        {
            sb.Append($"<color={DimColor}>NO SHIP DATA</color>");
            return sb.ToString();
        }
        float pct = Mathf.Clamp01(_ship.PowerPercent);
        int filled = Mathf.RoundToInt(pct * 10f);
        sb.Append("<color=#7fd4ff>");
        for (int i = 0; i < filled; i++) sb.Append('|');
        sb.Append("</color><color=#33475a>");
        for (int i = filled; i < 10; i++) sb.Append('|');
        sb.Append("</color> ").Append(Mathf.RoundToInt(pct * 100f)).Append("%\n\n");

        bool charging = _charger != null && _charger.IsCharging;
        sb.Append(charging ? $"<color={GoodColor}>++ CHARGING ++</color>"
                           : $"<color={DimColor}>NOT CHARGING</color>");
        return sb.ToString();
    }

    static Texture2D Scanlines()
    {
        if (s_scanlines != null) return s_scanlines;
        const int size = 64;
        s_scanlines = new Texture2D(size, size, TextureFormat.RGB24, false);
        s_scanlines.wrapMode = TextureWrapMode.Repeat;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = (y % 4 == 0) ? 0.45f : 1f;                    // dark line every 4th row
            float band = 0.9f + 0.1f * Mathf.Sin(y * 0.35f);        // soft banding
            for (int x = 0; x < size; x++)
                px[y * size + x] = new Color(0.20f * v * band, 0.28f * v * band, 0.38f * v * band);
        }
        s_scanlines.SetPixels(px);
        s_scanlines.Apply();
        return s_scanlines;
    }

    void OnDestroy()
    {
        if (_rt != null) _rt.Release();
    }
}
