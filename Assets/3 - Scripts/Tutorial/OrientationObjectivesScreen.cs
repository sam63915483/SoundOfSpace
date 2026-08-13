using TMPro;
using UnityEngine;

/// <summary>
/// Puts the seven orientation objectives on the shuttle's orientation TV — the
/// arm-mounted screen that swings round to face you (see OrientationTVSpin).
///
/// This replaces the standalone whiteboard. Same objective data, same
/// per-character progress, but it rides a screen the player is already looking
/// at, already turns toward them, and which the orientation film has just
/// finished using — so the list reads as the next thing the briefing says
/// rather than as set dressing bolted to a wall.
///
/// Attach to the TV rig (anywhere at or above TVScreen; the screen is found by
/// name). The text is a TextMeshPro parented to TVScreen and scaled to it, so it
/// inherits every bit of the arm's yaw and tilt for free — no tracking code, and
/// it can never drift off the screen when Sam re-poses the arm.
///
/// Timing: hidden until <see cref="ShuttleArrivalSequence.OrientationFilmFinished"/>.
/// The film owns that screen while it plays, and that flag starts true, so any
/// boot without an arrival (loading a save, Play straight into the scene) shows
/// the list at once.
/// </summary>
[ExecuteAlways]
public class OrientationObjectivesScreen : MonoBehaviour
{
    [Header("Screen")]
    [Tooltip("The TV face. Auto-found by the name 'TVScreen' if left empty.")]
    public Transform screen;

    [Tooltip("Fraction of the screen the text block fills. 0.86 leaves a bezel.")]
    [Range(0.4f, 1f)] public float fillFraction = 0.86f;

    [Tooltip("Metres to float the text in front of the screen face. Too small z-fights, too large detaches.")]
    public float surfaceOffset = 0.012f;

    [Tooltip("Tick if the objectives come out mirrored or end up behind the screen.")]
    public bool flipFacing = false;

    [Header("Look")]
    // DARK text. The TV's idle material is a fully saturated cyan emissive, so
    // light text on it is invisible — the first pass was near-white and vanished
    // completely. Dark-on-glow reads like an LCD and holds up at any distance.
    [Tooltip("Header colour. Near-black, because the screen behind it is a bright emissive.")]
    public Color headerColor = new Color(0.02f, 0.10f, 0.12f);
    [Tooltip("An objective still to do.")]
    public Color lineColor = new Color(0.05f, 0.17f, 0.19f);
    [Tooltip("An objective already crossed off — faded toward the screen colour, still readable.")]
    public Color doneColor = new Color(0.34f, 0.55f, 0.58f);
    [Tooltip("Header size as a percentage of the auto-fitted line size.")]
    public float headerScalePercent = 150f;

    [Header("Strike-through")]
    [Tooltip("Seconds for the line to draw itself across a completed objective.")]
    public float strikeDrawSeconds = 0.45f;

    [Tooltip("Leave empty to use the project HUD font.")]
    public TMP_FontAsset fontOverride;

    const float TextScale = 0.001f;

    TextMeshPro _text;
    int _paintedMask = -1;
    bool _visible;

    // The objective currently having its line drawn, and how far along (0..1).
    // -1 = nothing animating. Only ever one at a time; objectives complete
    // seconds apart at the very least.
    int _strikingIndex = -1;
    float _strikeT;

    void OnEnable()
    {
        Build();
        Repaint(true);
        OrientationObjectives.Completed += OnObjectiveCompleted;
    }

    void OnDisable()
    {
        OrientationObjectives.Completed -= OnObjectiveCompleted;
    }

    void OnObjectiveCompleted(OrientationObjectives.Objective o)
    {
        // Start the line drawing rather than snapping it on — the whole point of
        // the beat is that the player SEES it get crossed off.
        _strikingIndex = (int)o;
        _strikeT = 0f;
        Repaint(true);
    }

    // ── Objective 1 ────────────────────────────────────────────────────────
    //
    // "Take the axe and bottle from the locker" has no single event to hook: the
    // locker's withdrawal path is generic slot movement, and both items can also
    // arrive by other routes. What the objective means is "you're carrying both",
    // so that's what's checked — polled twice a second, and it stops once ticked.
    float _nextPoll;
    const float PollInterval = 0.5f;

    void Update()
    {
        if (!Application.isPlaying) return;

        bool shouldShow = ShuttleArrivalSequence.OrientationFilmFinished;
        if (shouldShow != _visible)
        {
            _visible = shouldShow;
            if (_text != null) _text.enabled = shouldShow;
        }
        if (!shouldShow) return;

        // Advance the strike animation.
        if (_strikingIndex >= 0)
        {
            _strikeT += Time.deltaTime / Mathf.Max(0.01f, strikeDrawSeconds);
            if (_strikeT >= 1f) { _strikeT = 1f; _strikingIndex = -1; }
            Repaint(true);
        }

        if (Time.unscaledTime < _nextPoll) return;
        _nextPoll = Time.unscaledTime + PollInterval;

        if (!OrientationObjectives.IsComplete(OrientationObjectives.Objective.TakeAxeAndBottle))
        {
            var hb = Hotbar.Instance;
            if (hb != null
                && hb.HasItem(Hotbar.ItemId.Axe)
                && hb.HasItem(Hotbar.ItemId.WaterBottle))
            {
                OrientationObjectives.Complete(OrientationObjectives.Objective.TakeAxeAndBottle);
            }
        }

        // The active character can change without an objective firing (character
        // switch, or the store finishing its load after this woke), so repaint
        // whenever the mask stops matching what's drawn.
        Repaint(false);
    }

    void Build()
    {
        if (screen == null)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "TVScreen") { screen = t; break; }
        }
        if (screen == null) return;

        // The text hangs off the screen's PARENT, not off the screen itself.
        // TVScreen is a cube squashed to (1.2, 0.624, 0.018) — parenting text
        // under a non-uniform scale like that stretches every glyph. The parent
        // is uniform, so the text lives there and is placed to line up with the
        // screen face. It still inherits all of the arm's yaw and tilt, which is
        // the only reason to be in this hierarchy at all.
        Transform mount = screen.parent != null ? screen.parent : screen;

        if (_text == null)
        {
            // Search the whole rig, not mount.Find() — that only sees direct
            // children, and an earlier build of this component parented the text
            // under TVScreen. A missed lookup there doesn't just orphan the old
            // object, it leaves every offset below being applied in the SCREEN's
            // squashed local space (0.018 on Z), which buries the text inside the
            // panel where it renders but can never be seen.
            foreach (var t in GetComponentsInChildren<TextMeshPro>(true))
                if (t.name == "OrientationObjectivesText") { _text = t; break; }
        }
        if (_text == null)
        {
            var go = new GameObject("OrientationObjectivesText");
            go.transform.SetParent(mount, false);
            _text = go.AddComponent<TextMeshPro>();
        }
        // Enforce the parent every build: everything below is expressed in the
        // mount's space and is silently wrong anywhere else.
        if (_text.transform.parent != mount) _text.transform.SetParent(mount, false);
        _text.gameObject.layer = screen.gameObject.layer;

        // Face size and the offset out to it, both in the MOUNT's space.
        var mesh = screen.GetComponent<MeshFilter>();
        Vector3 meshSize = mesh != null && mesh.sharedMesh != null
            ? mesh.sharedMesh.bounds.size
            : Vector3.one;
        Vector3 s = screen.localScale;
        Vector2 face = new Vector2(Mathf.Max(0.01f, meshSize.x * s.x),
                                   Mathf.Max(0.01f, meshSize.y * s.y));
        // A cube's front face is at its own local ±0.5, not at its centre — sit
        // the text just outside that, or it renders buried inside the panel.
        float halfDepth = meshSize.z * 0.5f * Mathf.Abs(s.z);

        // Which way the screen actually faces. OrientationTVSpin already owns
        // that answer for this rig; flipFacing is copied from its
        // flipScreenNormal so the two can never disagree.
        Vector3 outward = screen.localRotation * (flipFacing ? Vector3.back : Vector3.forward);

        var rt = _text.rectTransform;
        rt.localPosition = screen.localPosition + outward * (halfDepth + surfaceOffset);
        // TMP glyphs read correctly from the -Z side of their own transform in
        // this hierarchy (verified in-scene — mounted the other way up the whole
        // block comes out mirrored), so the text's forward points INTO the panel.
        rt.localRotation = Quaternion.LookRotation(-outward, screen.localRotation * Vector3.up);

        // The mount is uniformly scaled today (1.2 on every axis), so this is a
        // no-op — it's here because TVScreen ITSELF is squashed to (1, 0.52,
        // 0.015) and parenting the text under that stretched every glyph. If the
        // rig ever picks up a non-uniform scale above the mount, this keeps the
        // letters square and shrinks the rect to match so the block still covers
        // exactly the screen face.
        Vector3 mountLossy = mount.lossyScale;
        float aspectFix = Mathf.Abs(mountLossy.y) > 1e-6f
            ? Mathf.Abs(mountLossy.x / mountLossy.y)
            : 1f;
        rt.localScale = new Vector3(TextScale, TextScale * aspectFix, TextScale);
        rt.sizeDelta = new Vector2(face.x * fillFraction / TextScale,
                                   face.y * fillFraction / (TextScale * aspectFix));

        _text.alignment = TextAlignmentOptions.TopLeft;
        // Wrapping OFF is what gives one line per objective. With it on, TMP's
        // auto-fit grows the font until the WRAPPED text fills the screen, so it
        // always settles on a size that wraps — and a wrapped line restarts at
        // the left margin looking like an extra bullet.
        _text.enableWordWrapping = false;
        _text.richText = true;
        // Auto-fit rather than a point size: TMP sizes are points, and what a
        // point maps to depends on the font asset's sampling metrics — the HUD
        // font is built at runtime from a TTF, where nine lines at "46 pt"
        // measured 17 units tall in a 660-unit box. Auto-fit sidesteps all of it
        // and stays correct if the font, the wording or the TV size changes.
        _text.enableAutoSizing = true;
        _text.fontSizeMin = 1f;
        _text.fontSizeMax = 5000f;
        _text.color = lineColor;
        // Visible in edit mode so the layout can actually be reviewed; at
        // runtime Update owns this and holds it off until the film is done.
        _visible = !Application.isPlaying || ShuttleArrivalSequence.OrientationFilmFinished;
        _text.enabled = _visible;

        var f = fontOverride != null ? fontOverride : HudFontResolver.Default;
        if (f != null) _text.font = f;
    }

    void Repaint(bool force)
    {
        if (_text == null) return;
        int mask = CurrentMask();
        if (!force && mask == _paintedMask) return;
        _paintedMask = mask;

        var sb = new System.Text.StringBuilder(640);
        // Percentage, not an absolute size: the base is whatever auto-fit lands
        // on, so the header has to be relative to it.
        sb.Append("<size=").Append(headerScalePercent.ToString("0.#")).Append("%>")
          .Append("<b><color=#").Append(ColorUtility.ToHtmlStringRGB(headerColor)).Append('>')
          .Append("ORIENTATION OBJECTIVES")
          .Append("</color></b></size>\n\n");

        for (int i = 0; i < OrientationObjectives.Count; i++)
        {
            var o = (OrientationObjectives.Objective)i;
            bool done = (mask & (1 << i)) != 0;
            bool striking = i == _strikingIndex;
            string col = ColorUtility.ToHtmlStringRGB(done && !striking ? doneColor : lineColor);

            sb.Append("<color=#").Append(col).Append('>');
            sb.Append("- ").Append(OrientationObjectives.Label(o));
            sb.Append("</color>");
            if (i < OrientationObjectives.Count - 1) sb.Append('\n');
        }

        _text.text = sb.ToString();
        LayoutStrikes();
    }

    // ── Strike-through ─────────────────────────────────────────────────────
    //
    // Drawn as real quads, not with TMP's <s> tag. <s> was the obvious choice and
    // it produced NOTHING on this screen: the tag needs a strikethrough glyph in
    // the font atlas, and the project HUD font is a dynamic asset built at
    // runtime from a TTF that hasn't got one. The markup was correct and the
    // lines simply never appeared.
    //
    // Quads placed from TMP's own per-line metrics are font-independent, and they
    // make the draw-on animation exact — the line's width IS the progress, rather
    // than a character count approximating it.

    Transform _strikeRoot;
    Transform[] _strikes;
    Material _strikeMat;

    void LayoutStrikes()
    {
        if (_text == null) return;
        _text.ForceMeshUpdate();
        var info = _text.textInfo;

        EnsureStrikePool();
        if (_strikes == null) return;
        if (_strikeMat != null) _strikeMat.color = doneColor;

        for (int i = 0; i < OrientationObjectives.Count; i++)
        {
            var tr = _strikes[i];
            if (tr == null) continue;

            bool done = OrientationObjectives.IsComplete((OrientationObjectives.Objective)i);
            float progress = done ? (i == _strikingIndex ? Mathf.Clamp01(_strikeT) : 1f) : 0f;
            if (progress <= 0f) { tr.gameObject.SetActive(false); continue; }

            // Header, blank spacer, then one line per objective — word wrap is
            // off, so that mapping can't drift.
            int line = 2 + i;
            if (line >= info.lineCount) { tr.gameObject.SetActive(false); continue; }
            var li = info.lineInfo[line];

            float x0 = li.lineExtents.min.x;
            float x1 = li.lineExtents.max.x;
            float width = Mathf.Max(0f, x1 - x0) * progress;
            if (width <= 0f) { tr.gameObject.SetActive(false); continue; }

            // Sit the rule a third of the way up the cap height — through the
            // middle of the letters rather than along the baseline.
            float y = li.baseline + (li.ascender - li.baseline) * 0.32f;
            float thickness = Mathf.Max(1f, (li.ascender - li.descender) * 0.055f);

            tr.gameObject.SetActive(true);
            tr.localScale = new Vector3(width, thickness, 1f);
            // Quad pivots at its centre, so the left edge stays pinned at x0 and
            // the line grows rightward as it draws.
            tr.localPosition = new Vector3(x0 + width * 0.5f, y, -1f);
        }
    }

    void EnsureStrikePool()
    {
        if (_strikes != null && _strikeRoot != null) return;
        if (_text == null) return;

        var existing = _text.transform.Find("Strikes");
        _strikeRoot = existing != null ? existing : new GameObject("Strikes").transform;
        if (existing == null) _strikeRoot.SetParent(_text.transform, false);
        _strikeRoot.localPosition = Vector3.zero;
        _strikeRoot.localRotation = Quaternion.identity;
        _strikeRoot.localScale = Vector3.one;

        if (_strikeMat == null)
        {
            var sh = Shader.Find("Unlit/Color");
            _strikeMat = new Material(sh) { name = "ObjectiveStrike" };
            _strikeMat.color = doneColor;
        }

        _strikes = new Transform[OrientationObjectives.Count];
        for (int i = 0; i < _strikes.Length; i++)
        {
            var name = "Strike" + i;
            var found = _strikeRoot.Find(name);
            Transform tr;
            if (found != null) tr = found;
            else
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = name;
                var col = quad.GetComponent<Collider>();
                if (col != null) DestroyImmediate(col);
                tr = quad.transform;
                tr.SetParent(_strikeRoot, false);
            }
            tr.localRotation = Quaternion.identity;
            var mr = tr.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = _strikeMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            tr.gameObject.layer = _text.gameObject.layer;
            tr.gameObject.SetActive(false);
            _strikes[i] = tr;
        }
    }

    static int CurrentMask()
    {
        int m = 0;
        for (int i = 0; i < OrientationObjectives.Count; i++)
            if (OrientationObjectives.IsComplete((OrientationObjectives.Objective)i)) m |= 1 << i;
        return m;
    }
}
