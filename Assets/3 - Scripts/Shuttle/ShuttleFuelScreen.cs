using TMPro;
using UnityEngine;

/// <summary>
/// The little readout on the front of the shuttle's reactor: how full the tank
/// is, and — the number that actually decides anything — how far that gets you.
/// (docs/Handoff_PlanetEconomy_Fuel_Fishing_v2.md, Phase 2.)
///
/// Goes on the flat cube Sam modelled onto the reactor face. <b>The screen is
/// drawn on the +Z side — the blue arrow — of whatever object this sits on</b>,
/// so point the blue arrow out of the reactor and the display faces the room.
///
/// Everything is measured from the cube itself, so Sam can move, rotate or
/// resize it and the readout re-fits on the next load rather than needing
/// numbers typed in here. The one wrinkle that needs care: a flat cube is
/// non-uniformly scaled (0.46 × 0.19 × 0.13 here), and text parented straight
/// to it would be squashed by that scale — so the text root counter-scales by
/// the inverse, which puts it back in the reactor's own undistorted space.
/// </summary>
[DisallowMultipleComponent]
public class ShuttleFuelScreen : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Fraction of the panel face the text is allowed to fill.")]
    [Range(0.5f, 1f)] public float fillFraction = 0.88f;

    [Tooltip("How far off the +Z face the text floats, as a fraction of the panel's " +
             "thickness. Just enough to beat z-fighting with the panel itself.")]
    public float lift = 0.62f;

    [Tooltip("Characters wide the fuel bar is drawn with.")]
    [Range(6, 24)] public int barCells = 12;

    [Tooltip("Darken the panel so the glow reads as a screen rather than white plastic.")]
    public bool darkenPanel = true;

    [Header("Colours")]
    public Color screenTint = new Color(0.02f, 0.05f, 0.07f);
    public Color inkFull    = new Color(0.35f, 0.85f, 1f);      // reactor blue
    public Color inkLow     = new Color(1f,    0.45f, 0.2f);    // running dry

    [Tooltip("Below this fraction of a tank the readout turns warning-coloured.")]
    [Range(0f, 1f)] public float lowFuelAt = 0.25f;

    ShuttleFuel _tank;
    TextMeshPro _tmp;
    string      _shown;
    float       _nextPoll;

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
            // DestroyImmediate is an editor-only call; at runtime it is unsafe
            // inside Awake. Only reachable if a Readout was ever baked into the
            // prefab, but cheap to get right.
            if (Application.isPlaying) Destroy(existing.gameObject);
            else                       DestroyImmediate(existing.gameObject);
        }

        var go = new GameObject("Readout");
        go.transform.SetParent(transform, false);

        // Undo the panel's non-uniform scale so glyphs aren't stretched, then sit
        // just proud of the +Z face. A unit cube's face is at z = +0.5.
        var s = transform.localScale;
        var inv = new Vector3(1f / Safe(s.x), 1f / Safe(s.y), 1f / Safe(s.z));
        go.transform.localScale    = inv;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localPosition = new Vector3(0f, 0f, 0.5f + lift * 0.5f);

        // In that corrected space the face measures s.x by s.y.
        float w = Safe(s.x) * fillFraction;
        float h = Safe(s.y) * fillFraction;

        _tmp = go.AddComponent<TextMeshPro>();
        _tmp.alignment          = TextAlignmentOptions.Center;
        _tmp.enableWordWrapping = false;
        _tmp.richText           = true;
        _tmp.color              = inkFull;
        _tmp.fontSize           = h * 0.30f;     // four short lines inside the face
        _tmp.lineSpacing        = -8f;
        _tmp.characterSpacing   = 4f;
        _tmp.rectTransform.sizeDelta = new Vector2(w, h);

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
        // Twice a second is plenty for a gauge, and keeps the string churn off
        // the per-frame path.
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
            body = "<b>REACTOR</b>\n<size=80%>NO TANK</size>";
            _tmp.color = inkLow;
        }
        else
        {
            float pct   = Mathf.Clamp01(t.FuelPercent);
            float range = t.RangeKm;

            // Plain ASCII on purpose: the project's TMP font is LiberationSans
            // SDF, which does not carry the block-drawing glyphs, and a missing
            // glyph renders as a hollow box - a broken-looking gauge.
            int on = Mathf.Clamp(Mathf.RoundToInt(pct * barCells), 0, barCells);
            var bar = new System.Text.StringBuilder(barCells + 2);
            bar.Append('[');
            for (int i = 0; i < barCells; i++) bar.Append(i < on ? '|' : '.');
            bar.Append(']');

            _tmp.color = pct <= lowFuelAt ? inkLow : inkFull;

            // Range is the honest number — it already subtracts the launch charge,
            // so it reads a little under the tank percentage would suggest.
            // Three SHORT lines. The auto-sizer fits the LONGEST one, so every
            // extra word shrinks all of them - the REACTOR title was costing
            // legibility for a label the panel position already makes obvious.
            // Percentage first because it is the glanceable one; range under it
            // because that is the number that decides where you can go.
            body = $"<b>{Mathf.RoundToInt(pct * 100f)}%</b>\n" +
                   $"<size=70%>{bar}</size>\n" +
                   $"<size=85%>{range:0.0} KM</size>";
        }

        if (!force && body == _shown) return;
        _shown = body;
        _tmp.text = body;
    }

    static float Safe(float v) => Mathf.Abs(v) < 0.0001f ? 0.0001f : Mathf.Abs(v);
}
