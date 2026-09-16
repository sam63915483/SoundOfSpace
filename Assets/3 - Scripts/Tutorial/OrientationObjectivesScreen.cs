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

    // -- appended; keep new fields at the END (serialization) --

    [Header("Which lines this board shows")]
    [Tooltip("Leave EMPTY for the full board in enum order — that is what the gameplay shuttle wants. " +
             "Fill it in to show a subset, IN THE ORDER GIVEN: the tutorial box's shuttle lists five " +
             "(catch a fish / chop a tree / plant a sapling / place a bonfire / cook a fish) and leaves out " +
             "the axe, the bottle and the stasis pod, none of which the box asks for.")]
    public OrientationObjectives.Objective[] shownObjectives;

    [Tooltip("Optional per-line wording, matched to shownObjectives BY POSITION. Leave an entry blank " +
             "to use the shared label from OrientationObjectives.Label. This exists because a few of " +
             "those labels carry a hint that is only true in the gameplay scene \u2014 \"rod's in Tev's cabin\" " +
             "would send a tutorial player looking for a cabin the box hasn't got. Retyping a line here " +
             "changes only this board.")]
    public string[] lineOverrides;

    /// <summary>
    /// One screen's worth of objectives. The tutorial box runs three in
    /// sequence (Sam, 2026-09-16): survive, then refuel, then leave.
    ///
    /// A phase is shown until every line on it is crossed off, then the board
    /// waits <see cref="phaseHoldSeconds"/> - long enough for the player to SEE
    /// the last line struck through - and switches to the next. There is no way
    /// back: the objectives themselves are one-way, so a completed phase can
    /// never un-complete.
    /// </summary>
    [System.Serializable]
    public class BoardPhase
    {
        [Tooltip("Heading above the list. Leave blank for ORIENTATION OBJECTIVES.")]
        public string header;
        [Tooltip("The lines, in the order they should read.")]
        public OrientationObjectives.Objective[] lines;
        [Tooltip("Optional per-line wording, positional against 'lines'. Blank = the shared label.")]
        public string[] overrides;
    }

    [Tooltip("Leave EMPTY to show one fixed board (shownObjectives above). Fill it in for a board that " +
             "ADVANCES: each phase is shown until all of its lines are crossed off, then the next takes over.")]
    public BoardPhase[] phases;

    [Tooltip("Seconds the finished phase stays up after its last line is struck through, before the next " +
             "one replaces it. Long enough to read what you just finished.")]
    public float phaseHoldSeconds = 2.5f;

    const float TextScale = 0.001f;

    /// <summary>
    /// The lines this board draws, top to bottom.
    ///
    /// EVERYTHING below indexes by ROW, not by objective: the strike quads, the
    /// TMP line numbers and the painted-mask cache. Keeping one ordered array as
    /// the single source of that mapping is what lets the tutorial reorder its
    /// five lines without the strike-through landing on the wrong one — the bug
    /// you get for free if any of them goes back to assuming row == (int)enum.
    ///
    /// Rebuilt whenever the inspector array changes, which in [ExecuteAlways]
    /// means while Sam is editing it.
    /// </summary>
    OrientationObjectives.Objective[] _rows;
    string[] _rowOverrides;
    int _builtPhase = -2;          // -1 = the flat board; >= 0 = that phase index

    /// <summary>
    /// Which phase the board is showing: the first one with an unfinished line.
    /// Once every phase is done it stays on the last, which then reads as a
    /// fully crossed-off list rather than vanishing.
    ///
    /// While a phase is being held (its last line was just struck) this stays on
    /// the FINISHED phase, so the player watches the strike land instead of the
    /// screen changing under them.
    /// </summary>
    int ActivePhase
    {
        get
        {
            if (phases == null || phases.Length == 0) return -1;
            if (Application.isPlaying && _holdUntil > 0f && Time.unscaledTime < _holdUntil)
                return Mathf.Clamp(_heldPhase, 0, phases.Length - 1);
            for (int i = 0; i < phases.Length; i++)
            {
                var ph = phases[i];
                if (ph == null || ph.lines == null || ph.lines.Length == 0) continue;
                if (!OrientationObjectives.AllCompleteOf(ph.lines)) return i;
            }
            return phases.Length - 1;
        }
    }

    float _holdUntil;
    int _heldPhase = -1;

    OrientationObjectives.Objective[] Rows
    {
        get
        {
            int phase = ActivePhase;
            if (_rows == null || _builtPhase != phase) BuildRows(phase);
            return _rows;
        }
    }

    void BuildRows(int phase)
    {
        if (phase >= 0 && phases != null && phase < phases.Length && phases[phase] != null
            && phases[phase].lines != null && phases[phase].lines.Length > 0)
        {
            var ph = phases[phase];
            _rows = (OrientationObjectives.Objective[])ph.lines.Clone();
            _rowOverrides = ph.overrides;
        }
        else if (shownObjectives != null && shownObjectives.Length > 0)
        {
            _rows = (OrientationObjectives.Objective[])shownObjectives.Clone();
            _rowOverrides = lineOverrides;
        }
        else
        {
            _rows = new OrientationObjectives.Objective[OrientationObjectives.Count];
            for (int i = 0; i < _rows.Length; i++) _rows[i] = (OrientationObjectives.Objective)i;
            _rowOverrides = null;
        }
        _builtPhase = phase;
        // The strike pool is sized to the row count, so it has to be rebuilt with
        // it. Dropping the reference is enough: EnsureStrikePool reuses the quads
        // it finds under "Strikes" and only adds what is missing.
        _strikes = null;
        _strikingIndex = -1;
        _paintedMask = -1;         // force a repaint onto the new list
    }

    /// Which row draws this objective, or -1 if this board does not list it.
    int RowOf(OrientationObjectives.Objective o)
    {
        var rows = Rows;
        for (int i = 0; i < rows.Length; i++) if (rows[i] == o) return i;
        return -1;
    }

    /// Does this board list it at all? The axe/bottle poll asks before running.
    bool Shows(OrientationObjectives.Objective o) => RowOf(o) >= 0;

    /// What row `r` actually reads. An override wins; otherwise the shared label.
    /// Matched BY POSITION against shownObjectives, so a short overrides array is
    /// fine \u2014 the rows past its end just use their normal wording.
    string LineText(int r, OrientationObjectives.Objective o)
    {
        var ov = _rowOverrides;
        if (ov != null && r < ov.Length && !string.IsNullOrWhiteSpace(ov[r]))
            return ov[r];
        return OrientationObjectives.Label(o);
    }

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
        // A board that does not list this one has nothing to animate. The
        // objective still completed and still saves - it just is not on this TV.
        int row = RowOf(o);
        if (row < 0) return;

        // Start the line drawing rather than snapping it on — the whole point of
        // the beat is that the player SEES it get crossed off.
        _strikingIndex = row;
        _strikeT = 0f;

        // If that was the last line of the phase, PIN the board to this phase
        // for a beat. Without it ActivePhase would flip the instant the mask
        // changed and the player would never see the strike they just earned.
        int phase = _builtPhase;
        if (phase >= 0 && phases != null && phase < phases.Length
            && phases[phase] != null && OrientationObjectives.AllCompleteOf(phases[phase].lines))
        {
            _heldPhase = phase;
            _holdUntil = Time.unscaledTime + Mathf.Max(strikeDrawSeconds, phaseHoldSeconds);
        }

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

        if (Shows(OrientationObjectives.Objective.GatherCrystals)
            && !OrientationObjectives.IsComplete(OrientationObjectives.Objective.GatherCrystals))
        {
            // FOUR (Sam, 2026-09-16: "just make it so you need 4 and it will
            // complete but gathering more will help, 4 should be enough to get
            // them off the ground"). A flat number, not the tank's full deficit:
            // filling the tank is ~20 crystals, and standing between the player
            // and the rest of the tutorial for 20 is a chore, where 4 is a trip.
            // More is still better - the reactor takes everything you carry and
            // the fuel buys range - the line just stops nagging at 4.
            const int CrystalsForLaunch = 4;
            int have = Hotbar.Instance != null
                ? Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.Crystal) : 0;
            if (have >= CrystalsForLaunch)
                OrientationObjectives.Complete(OrientationObjectives.Objective.GatherCrystals);
        }

        if (Shows(OrientationObjectives.Objective.TakeAxeAndBottle)
            && !OrientationObjectives.IsComplete(OrientationObjectives.Objective.TakeAxeAndBottle))
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
          .Append(HeaderText())
          .Append("</color></b></size>\n\n");

        var rows = Rows;
        for (int r = 0; r < rows.Length; r++)
        {
            var o = rows[r];
            bool done = (mask & (1 << (int)o)) != 0;
            bool striking = r == _strikingIndex;
            string col = ColorUtility.ToHtmlStringRGB(done && !striking ? doneColor : lineColor);

            sb.Append("<color=#").Append(col).Append('>');
            sb.Append("- ").Append(LineText(r, o));
            sb.Append("</color>");
            if (r < rows.Length - 1) sb.Append('\n');
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

    string HeaderText()
    {
        int phase = _builtPhase;
        if (phase >= 0 && phases != null && phase < phases.Length
            && phases[phase] != null && !string.IsNullOrWhiteSpace(phases[phase].header))
            return phases[phase].header.ToUpperInvariant();
        return "ORIENTATION OBJECTIVES";
    }

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

        var rows = Rows;
        for (int r = 0; r < rows.Length && r < _strikes.Length; r++)
        {
            var tr = _strikes[r];
            if (tr == null) continue;

            bool done = OrientationObjectives.IsComplete(rows[r]);
            float progress = done ? (r == _strikingIndex ? Mathf.Clamp01(_strikeT) : 1f) : 0f;
            if (progress <= 0f) { tr.gameObject.SetActive(false); continue; }

            // Header, blank spacer, then one line per ROW — word wrap is off, so
            // that mapping can't drift.
            int line = 2 + r;
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

        // Sized to the ROW COUNT. The quads live under "Strikes" and are reused
        // by name, so a board that shrinks from eight lines to five leaves
        // Strike5..7 parked in the hierarchy - hidden here so a stale rule from a
        // previous row count can never be left drawn across the screen.
        int rowCount = Rows.Length;
        for (int i = rowCount; i < OrientationObjectives.Count; i++)
        {
            var stale = _strikeRoot.Find("Strike" + i);
            if (stale != null) stale.gameObject.SetActive(false);
        }

        _strikes = new Transform[rowCount];
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
