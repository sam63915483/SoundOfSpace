using TMPro;
using UnityEngine;

/// A cockpit screen: while the ship is PARKED it's an opaque blue scan-line
/// computer display (ship components status, or fuel + charging); while the
/// ship is PILOTED it fades into a rear-view camera feed.
///
/// The camera is a normal enabled camera (toggled at renderHz) rather than a
/// manual Render() — manual renders miss Graphics.DrawMeshInstanced
/// submissions (space dust), enabled cameras in the render loop don't. It only
/// renders while this ship is piloted, so parked ships pay zero GPU.
public class RearViewMirror : MonoBehaviour
{
    public enum IdleContent { ShipComponents, FuelStatus }

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
    const string GoodColor = "#8dffb0";
    const string BadColor = "#ff5a5a";
    const string DimColor = "#8a97a8";

    static Texture2D s_scanlines;

    Ship _ship;
    ThrusterDetachOnImpact _parts;
    SolarPanelCharger _charger;
    RenderTexture _rt;
    Material _mat;
    TextMeshPro _text;
    bool _mirrorMode;        // what the material is currently configured as
    float _level = 1f;       // fade level of the current mode (0..1)
    float _nextRenderAt, _nextTextAt;
    bool _fxCopied;

    void Awake()
    {
        _ship = GetComponentInParent<Ship>();
        _parts = GetComponentInParent<ThrusterDetachOnImpact>();
        if (_parts == null && _ship != null) _parts = _ship.GetComponentInChildren<ThrusterDetachOnImpact>(true);
        _charger = GetComponentInParent<SolarPanelCharger>();
        if (_charger == null && _ship != null) _charger = _ship.GetComponentInChildren<SolarPanelCharger>(true);

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

        BuildIdleText();
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

        // Camera renders only while piloted, throttled to renderHz. Toggling
        // `enabled` keeps it inside the normal render loop (dust renders).
        if (sourceCamera != null && _rt != null)
        {
            bool due = piloted && Time.unscaledTime >= _nextRenderAt;
            if (due) _nextRenderAt = Time.unscaledTime + 1f / Mathf.Max(1f, renderHz);
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

        // Idle text refresh (cheap, 2 Hz, only while visible).
        if (!_mirrorMode && _text != null && Time.unscaledTime >= _nextTextAt)
        {
            _nextTextAt = Time.unscaledTime + 0.5f;
            _text.text = idleContent == IdleContent.ShipComponents ? BuildComponentsText() : BuildFuelText();
        }
    }

    void ConfigureIdleMaterial()
    {
        if (_mat == null) return;
        _mat.SetTexture("_EmissionMap", Scanlines());
        _mat.mainTextureScale = Vector2.one;
        _mat.mainTextureOffset = Vector2.zero;
        _mat.SetTextureScale("_EmissionMap", new Vector2(1f, 14f));   // tile the lines
        if (_text != null) _text.gameObject.SetActive(true);
    }

    void ConfigureMirrorMaterial()
    {
        if (_mat == null) return;
        _mat.SetTexture("_EmissionMap", _rt);
        _mat.SetTextureScale("_EmissionMap", mirrorHorizontal ? new Vector2(-1f, 1f) : Vector2.one);
        _mat.SetTextureOffset("_EmissionMap", mirrorHorizontal ? new Vector2(1f, 0f) : Vector2.zero);
        if (_text != null) _text.gameObject.SetActive(false);
    }

    void ApplyLevel()
    {
        if (_mat != null)
        {
            Color tint = _mirrorMode ? Color.white : IdleTint;
            _mat.SetColor("_EmissionColor", tint * _level);
        }
        if (_text != null && !_mirrorMode)
            _text.alpha = _level;
    }

    // ── idle displays ──

    void BuildIdleText()
    {
        var go = new GameObject("IdleText");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, -0.075f);   // just in front of the concave face
        _text = go.AddComponent<TextMeshPro>();
        _text.rectTransform.sizeDelta = new Vector2(0.84f, 0.84f);
        _text.fontSize = 0.62f;
        _text.enableAutoSizing = false;
        _text.color = TextColor;
        _text.alignment = TextAlignmentOptions.TopLeft;
        _text.richText = true;
        _text.text = "";
    }

    string BuildComponentsText()
    {
        var sb = new System.Text.StringBuilder(256);
        sb.Append("<u>SHIP SYSTEMS</u>\n");
        if (_parts == null)
        {
            sb.Append($"<color={DimColor}>NO PART DATA</color>");
            return sb.ToString();
        }
        AppendPart(sb, "LEFT THRUSTER", _parts.leftThrusterChild);
        AppendPart(sb, "RIGHT THRUSTER", _parts.rightThrusterChild);
        AppendPart(sb, "SATELLITE DISH", _parts.dishChild);
        AppendPart(sb, "SOLAR PANEL", _parts.solarPanelChild);
        AppendPart(sb, "LEFT NET", _parts.leftSpaceNetChild);
        AppendPart(sb, "RIGHT NET", _parts.rightSpaceNetChild);
        return sb.ToString();
    }

    static void AppendPart(System.Text.StringBuilder sb, string label, GameObject child)
    {
        if (child == null) return;   // this ship variant doesn't have the part slot
        bool good = child.activeSelf;
        sb.Append(label).Append("  ")
          .Append(good ? $"<color={GoodColor}>GOOD</color>" : $"<color={BadColor}>MISSING</color>")
          .Append('\n');
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
        sb.Append("</color>  ").Append(Mathf.RoundToInt(pct * 100f)).Append("%\n\n");

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
