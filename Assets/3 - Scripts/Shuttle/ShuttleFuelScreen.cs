using TMPro;
using UnityEngine;

/// <summary>
/// The readout on the front of the shuttle's reactor: how full the tank is, and
/// — the number that actually decides anything — how far that gets you.
/// (docs/Handoff_PlanetEconomy_Fuel_Fishing_v2.md, Phase 2.)
///
/// Goes on the flat panel Sam modelled onto the reactor face. The display is
/// drawn on the <b>+Z side, the blue arrow</b>, so point the blue arrow out of
/// the reactor and the readout faces the room.
///
/// <b>How it is sized, and why it took three goes.</b> Two independent traps:
///
///  1. The panel is a flat cube, so it is NON-UNIFORMLY scaled. Text parented
///     to it inherits that and comes out stretched. The text root divides the
///     panel's scale back out before anything else happens.
///
///  2. <b>TextMeshPro font sizes are not world units</b>, and there is no
///     reliable conversion — it depends on the font asset's point size and
///     sampling. Computing a font size from the panel's height gave a readout at
///     a few percent of the face, and switching auto-sizing on did not rescue it
///     because the ceiling handed to the auto-sizer was itself wrong.
///
/// So this stops trying to predict TMP's units and MEASURES instead: lay the
/// text out at an arbitrary font size in an unconstrained rect, ask TMP how big
/// it actually came out (<c>GetRenderedValues</c>), then scale the whole object
/// by exactly the factor that makes that fill the panel. Measured, not guessed,
/// so it fills the face whatever the font, the panel size or the text length.
/// It measures the WIDEST reading the gauge can ever show, so the layout never
/// jumps around as the numbers change.
/// </summary>
[DisallowMultipleComponent]
public class ShuttleFuelScreen : MonoBehaviour
{
    [Header("Fit")]
    [Tooltip("Fraction of the panel face the readout fills. 1 = right to the edges. " +
             "0.78 leaves a margin so the text sits ON a screen rather than bursting " +
             "out of it (Sam, 2026-09-07: 0.94 filled it a little too completely).")]
    [Range(0.4f, 1f)] public float fillFraction = 0.78f;

    [Tooltip("Gap between the text and the panel face, in METRES. Two millimetres is " +
             "enough to beat z-fighting and reads as printed on the glass; anything " +
             "much more and the readout visibly hovers off the screen.")]
    public float surfaceGapMetres = 0.002f;

    [Tooltip("The readout faces the panel's +Z (blue arrow). Tick this if the text comes " +
             "out MIRRORED — that means you are seeing the BACK of it, and this turns it " +
             "round to face you without moving it.")]
    public bool faceTheOtherWay = true;

    [Header("Look")]
    [Tooltip("Characters wide the fuel bar is drawn with.")]
    [Range(4, 20)] public int barCells = 12;

    [Tooltip("Darken the panel so the readout reads as a screen, not white plastic.")]
    public bool darkenPanel = true;

    public Color screenTint = new Color(0.02f, 0.05f, 0.07f);
    public Color inkFull    = new Color(0.35f, 0.85f, 1f);      // reactor blue
    public Color inkLow     = new Color(1f,    0.45f, 0.2f);    // running dry

    [Tooltip("Below this fraction of a tank the readout turns warning-coloured.")]
    [Range(0f, 1f)] public float lowFuelAt = 0.25f;

    ShuttleFuel _tank;
    TextMeshPro _tmp;
    string      _shown;
    float       _nextPoll;

    // Font size the text is LAID OUT at. Arbitrary on purpose — the object is
    // then scaled by whatever makes the result fit, so this never has to be right.
    const float LayoutFontSize = 24f;

    void Awake()
    {
        BuildDisplay();
        if (darkenPanel) Darken();
    }

    void OnEnable() => _nextPoll = 0f;

    ShuttleFuel Tank()
    {
        if (_tank != null) return _tank;
        _tank = GetComponentInParent<ShuttleFuel>(true) ?? ShuttleFuel.Instance;
        return _tank;
    }

    void BuildDisplay()
    {
        var existing = transform.Find("Readout");
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing.gameObject);
            else                       DestroyImmediate(existing.gameObject);
        }

        var go = new GameObject("Readout");
        go.transform.SetParent(transform, false);

        _tmp = go.AddComponent<TextMeshPro>();
        _tmp.alignment          = TextAlignmentOptions.Center;
        _tmp.enableWordWrapping = false;
        _tmp.richText           = true;
        _tmp.enableAutoSizing   = false;          // we do the fitting ourselves
        _tmp.fontSize           = LayoutFontSize;
        _tmp.characterSpacing   = 0f;
        _tmp.lineSpacing        = 0f;             // solved below, from the panel shape
        _tmp.margin             = Vector4.zero;
        _tmp.color              = inkFull;

        // Unconstrained while measuring, so nothing wraps or clips and what we
        // measure is the TEXT rather than this rectangle.
        _tmp.rectTransform.sizeDelta = new Vector2(1000f, 1000f);

        // Measure the WIDEST reading the gauge can ever show, so the fit holds
        // for every value and the layout never jumps.
        _tmp.text = Compose(100, WidestBar(), 88.8f);

        // Match the text block's shape to the PANEL's shape before fitting it.
        //
        // Fitting alone only guarantees one axis: two wide lines on this panel
        // filled 94% of the width but 62% of the height, which still reads as
        // "it isn't using the screen". The block is too wide for its height, and
        // the free fix is line spacing — we are width-limited, so pushing the
        // lines apart costs no glyph size at all and simply fills the face.
        //
        // Solved rather than hard-coded: measure at two spacings, work out how
        // much height one unit of spacing buys, and ask for exactly the height
        // that makes the block the same shape as the panel. Resize or reshape
        // the panel and this re-solves on the next load.
        float panelAspect = Safe(transform.localScale.x) / Safe(transform.localScale.y);

        _tmp.lineSpacing = 0f;
        _tmp.ForceMeshUpdate();
        Vector2 r0 = _tmp.GetRenderedValues(false);

        _tmp.lineSpacing = 100f;
        _tmp.ForceMeshUpdate();
        Vector2 r1 = _tmp.GetRenderedValues(false);

        float heightPerUnit = (r1.y - r0.y) / 100f;
        if (heightPerUnit > 0.00001f && r0.x > 0.0001f)
        {
            float wantHeight = r0.x / Mathf.Max(0.01f, panelAspect);
            _tmp.lineSpacing = Mathf.Clamp((wantHeight - r0.y) / heightPerUnit, -20f, 400f);
        }
        else _tmp.lineSpacing = 0f;

        _tmp.ForceMeshUpdate();
        Vector2 rendered = _tmp.GetRenderedValues(false);
        if (rendered.x < 0.0001f || rendered.y < 0.0001f) rendered = new Vector2(1f, 1f);

        // One uniform factor that makes the measured text fill the face, then the
        // panel's own non-uniform scale divided back out so nothing is stretched.
        // A unit cube's face is 1x1 in its local space, so the face is just
        // fillFraction across, scaled by the panel.
        var s = transform.localScale;
        float fitW = (fillFraction * Safe(s.x)) / rendered.x;
        float fitH = (fillFraction * Safe(s.y)) / rendered.y;
        float fit  = Mathf.Min(fitW, fitH);

        go.transform.localScale = new Vector3(fit / Safe(s.x), fit / Safe(s.y), fit / Safe(s.z));

        // Sit just proud of the face. A unit cube's face is at z = +0.5, and one
        // local Z unit is the panel's WORLD thickness — so converting a fixed
        // gap in metres through that keeps the text the same hair's breadth off
        // the glass whatever the panel's size. (Expressing the gap as a fraction
        // of thickness, as this first did, floated it ~5 cm off a 20 cm-thick
        // panel: it read as hovering in front of the screen rather than on it.)
        float thicknessM = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.z));
        go.transform.localPosition = new Vector3(0f, 0f, 0.5f + surfaceGapMetres / thicknessM);
        go.transform.localRotation = faceTheOtherWay
            ? Quaternion.Euler(0f, 180f, 0f)
            : Quaternion.identity;

        _shown = null;
        Refresh(true);
    }

    void Darken()
    {
        var mr = GetComponent<MeshRenderer>();
        if (mr == null) return;
        // .material instantiates, so this never leaks into Default-Material and
        // repaints half the shuttle.
        var m = mr.material;
        if (m == null) return;
        if (m.HasProperty("_Color"))      m.color = screenTint;
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.75f);
        if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", 0.1f);
    }

    void Update()
    {
        // Twice a second is plenty for a gauge and keeps string churn off the
        // per-frame path.
        if (Time.unscaledTime < _nextPoll) return;
        _nextPoll = Time.unscaledTime + 0.5f;
        Refresh(false);
    }

    void Refresh(bool force)
    {
        if (_tmp == null) return;
        var t = Tank();

        string body;
        if (t == null)
        {
            body = Compose(-1, WidestBar(), -1f);
            _tmp.color = inkLow;
        }
        else
        {
            float pct = Mathf.Clamp01(t.FuelPercent);
            _tmp.color = pct <= lowFuelAt ? inkLow : inkFull;
            body = Compose(Mathf.RoundToInt(pct * 100f), Bar(pct), t.RangeKm);
        }

        if (!force && body == _shown) return;
        _shown = body;
        _tmp.text = body;
    }

    /// <summary>TWO WIDE LINES, not three stacked ones.
    ///
    /// The panel is 2.4x wider than it is tall. Three lines make a nearly square
    /// block, and the fitter can then only scale it until its HEIGHT fills the
    /// face — measured at 94% of the height but just 45% of the width, which is
    /// exactly the "it isn't using the space" Sam is looking at. Two wide lines
    /// match the panel's own shape, so the same fit covers far more of the face
    /// AND draws the glyphs about half again as large.
    ///
    /// The reading and the range share the top line because they are the two
    /// numbers you glance at; the bar sits under them as the wordless version of
    /// the same thing.</summary>
    string Compose(int pct, string bar, float rangeKm)
    {
        string top = pct < 0 ? "--%" : pct + "%";
        string bot = rangeKm < 0f ? "NO TANK" : rangeKm.ToString("0.0") + " KM";
        return $"<b>{top}</b>   <size=80%>{bot}</size>\n<size=70%>{bar}</size>";
    }

    string Bar(float pct)
    {
        int on = Mathf.Clamp(Mathf.RoundToInt(pct * barCells), 0, barCells);
        var sb = new System.Text.StringBuilder(barCells + 2);
        sb.Append('[');
        // Plain ASCII: the project's TMP font is LiberationSans SDF, which carries
        // no block-drawing glyphs — a missing glyph renders as a hollow box.
        for (int i = 0; i < barCells; i++) sb.Append(i < on ? '|' : '.');
        sb.Append(']');
        return sb.ToString();
    }

    string WidestBar() => Bar(1f);

    static float Safe(float v) => Mathf.Abs(v) < 0.0001f ? 0.0001f : Mathf.Abs(v);
}
