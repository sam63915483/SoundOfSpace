using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The TRAX arranger — Unity half of the browser prototype's song strip
/// (prototypes/shuttle-computer/ui/trax.js, arranger sections), approved in the
/// browser before this was written.
///
///   ruler   — one clickable cell per bar, numbered song-wide; click = seek
///   strip   — one block per section, width ∝ bars, coloured by genre;
///             click = select for editing AND play from its first bar
///   ctl row — SEC tag, LENGTH stepper, named two-press DELETE
///   info    — genre mix meter + full-track value + per-fan offers
///
/// The playhead line runs down through ruler + strip: hot magenta while the
/// song plays, dimmed while stopped to show where PLAY TRACK will start.
///
/// Partial of ShuttleComputerUI so it shares the palette, the layout budget
/// and the UGUI helpers without exposing any of them.
/// </summary>
public partial class ShuttleComputerUI
{
    // ── arranger layout (heights; Y positions live in the main budget) ───
    // The header is its own row — the SONG label used to overlay the ruler
    // and sat on top of bar number 1 (Sam's playtest note).
    const float ArrHeaderH = 16f;
    const float ArrRulerH = 24f;
    const float ArrStripH = 44f;
    const float ArrCtlH = 34f;
    const float ArrGap = 4f;
    const float ArrH = ArrHeaderH + ArrRulerH + ArrStripH + ArrCtlH + ArrGap * 3;   // 122
    const float ArrAddW = 44f;
    const float ArrBlockGap = 4f;
    const float ArrRulerY = -(ArrHeaderH + ArrGap);
    const float ArrStripY = ArrRulerY - ArrRulerH - ArrGap;

    /// One fixed colour per genre so a section's block, the mix meter and the
    /// offer line all agree. UI concern only — the engine never sees colours.
    static readonly Dictionary<string, Color> GenreColors = new Dictionary<string, Color>
    {
        { "GLORP",    Hex("46d17aff") }, { "DRIFT",    Hex("6f86ffff") },
        { "SKITTER",  Hex("ff9f45ff") }, { "SLUDJ",    Hex("9a6a4aff") },
        { "CHIRP",    Hex("ffd94fff") }, { "NULLGAZE", Hex("8fa3b5ff") },
        { "THRUM",    Hex("b06fffff") }, { "VOLT",     Hex("3ae0ffff") },
        { "WARBLE",   Hex("2fb59aff") }, { "CLANG",    Hex("ff5a5aff") }
    };

    static Color GenreColorOf(string name)
    {
        Color c;
        return GenreColors.TryGetValue(name, out c) ? c : Ink;
    }

    // ── state ────────────────────────────────────────────────────────────

    /// The working song. Owned by this screen; the instrument's loaded track
    /// is always sections[_sel].track (reference-shared — every edit replaces
    /// the track immutably, so sharing is what lets dirty checks converge).
    TraxSong _song;
    int _sel;
    /// The engine's compiled copy is stale after an edit; recompiled lazily
    /// before song playback (or immediately while the song is audible).
    bool _songStale;

    RectTransform _arrRuler, _arrStrip;
    Image _arrPlayhead;
    TextMeshProUGUI _arrStats, _arrSecTag, _arrBarsLabel, _arrValue, _arrOffers;
    Image _arrDeleteBg;
    TextMeshProUGUI _arrDeleteLabel;
    Image _arrAddBg;
    TextMeshProUGUI _arrAddLabel;
    RectTransform _arrMeter;
    readonly List<Image> _meterSegs = new List<Image>();

    // Per-block bits, rebuilt on structural change, recoloured on refresh.
    readonly List<GameObject> _arrBlockObjs = new List<GameObject>();
    readonly List<Image> _arrBlockBgs = new List<Image>();
    readonly List<Image> _arrBlockBorders = new List<Image>();
    readonly List<TextMeshProUGUI> _arrBlockTops = new List<TextMeshProUGUI>();
    readonly List<TextMeshProUGUI> _arrBlockBots = new List<TextMeshProUGUI>();
    readonly List<float> _arrBlockX = new List<float>();
    readonly List<float> _arrBlockW = new List<float>();
    readonly List<GameObject> _arrRulerObjs = new List<GameObject>();

    bool _delArmed;
    float _delArmUntil;
    int _lastPlayingSec = -1;

    // ── construction ─────────────────────────────────────────────────────

    void BuildArranger(RectTransform parent)
    {
        var holder = MakeRect(parent, "Arranger");
        holder.anchorMin = new Vector2(0, 1);
        holder.anchorMax = new Vector2(1, 1);
        holder.pivot = new Vector2(0.5f, 1);
        holder.sizeDelta = new Vector2(0, ArrH);
        holder.anchoredPosition = new Vector2(0, ArrY);

        const float rulerY = ArrRulerY;
        const float stripY = ArrStripY;

        var label = MakeText(holder, "Label", "SONG", 12, InkGhost, TextAlignmentOptions.TopLeft);
        Box(label.rectTransform, TopLeft, TopLeft, new Vector2(2, 0), new Vector2(90, ArrHeaderH));
        label.characterSpacing = 24;

        _arrStats = MakeText(holder, "Stats", "", 12, InkGhost, TextAlignmentOptions.TopRight);
        Box(_arrStats.rectTransform, TopRight, TopRight, new Vector2(-2, 0), new Vector2(360, ArrHeaderH));
        _arrStats.characterSpacing = 12;

        _arrRuler = MakeRect(holder, "Ruler");
        _arrRuler.anchorMin = new Vector2(0, 1);
        _arrRuler.anchorMax = new Vector2(1, 1);
        _arrRuler.pivot = new Vector2(0.5f, 1);
        _arrRuler.sizeDelta = new Vector2(0, ArrRulerH);
        _arrRuler.anchoredPosition = new Vector2(0, rulerY);

        _arrStrip = MakeRect(holder, "Strip");
        _arrStrip.anchorMin = new Vector2(0, 1);
        _arrStrip.anchorMax = new Vector2(1, 1);
        _arrStrip.pivot = new Vector2(0.5f, 1);
        _arrStrip.sizeDelta = new Vector2(0, ArrStripH);
        _arrStrip.anchoredPosition = new Vector2(0, stripY);

        // The [+] add button sits at the strip's right edge, outside the
        // proportional block area so it never squeezes the last section.
        _arrAddBg = MakePanel(holder, "Add", Panel);
        _arrAddBg.raycastTarget = true;
        Box(_arrAddBg.rectTransform, TopRight, TopRight,
            new Vector2(0, stripY), new Vector2(ArrAddW, ArrStripH));
        Outline(_arrAddBg.transform, InkGhost);
        _arrAddLabel = MakeText(_arrAddBg.rectTransform, "Label", "+", 26, InkDim,
                                TextAlignmentOptions.Center);
        Stretch(_arrAddLabel.rectTransform, 0, 0, 0, 0);
        var addBtn = _arrAddBg.gameObject.AddComponent<Button>();
        addBtn.targetGraphic = _arrAddBg;
        addBtn.onClick.AddListener(OnAddSection);

        // The playhead line spans ruler + strip. Hot while playing, dim while
        // stopped (= where PLAY TRACK will start). Positioned by x each frame.
        _arrPlayhead = MakePanel(holder, "Playhead", Accent);
        var pr = _arrPlayhead.rectTransform;
        pr.anchorMin = new Vector2(0, 1);
        pr.anchorMax = new Vector2(0, 1);
        pr.pivot = new Vector2(0.5f, 1);
        pr.sizeDelta = new Vector2(3, ArrRulerH + ArrGap + ArrStripH);
        pr.anchoredPosition = new Vector2(0, rulerY);
        _arrPlayhead.gameObject.SetActive(false);

        // Drag-reorder drop indicator: a warm line at the slot the grabbed
        // section will land in.
        _arrDropLine = MakePanel(holder, "DropLine", Warn);
        var dl = _arrDropLine.rectTransform;
        dl.anchorMin = new Vector2(0, 1);
        dl.anchorMax = new Vector2(0, 1);
        dl.pivot = new Vector2(0.5f, 1);
        dl.sizeDelta = new Vector2(3, ArrStripH);
        _arrDropLine.gameObject.SetActive(false);

        BuildArrangerCtl(holder);
    }

    void BuildArrangerCtl(RectTransform holder)
    {
        var row = MakeRect(holder, "Ctl");
        row.anchorMin = new Vector2(0, 1);
        row.anchorMax = new Vector2(1, 1);
        row.pivot = new Vector2(0.5f, 1);
        row.sizeDelta = new Vector2(0, ArrCtlH);
        row.anchoredPosition = new Vector2(0,
            -(ArrHeaderH + ArrGap + ArrRulerH + ArrGap + ArrStripH + ArrGap));

        _arrSecTag = MakeText(row, "SecTag", "SEC A", 18, Accent, TextAlignmentOptions.Left);
        Box(_arrSecTag.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(2, 0), new Vector2(110, 26));
        _arrSecTag.characterSpacing = 14;

        // LENGTH mirrors the transport's KEY control — a name, then arrows
        // around a value — so the player already knows how to read it.
        var lenLbl = MakeText(row, "LenLabel", "LENGTH", 12, InkGhost, TextAlignmentOptions.Left);
        Box(lenLbl.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(112, 0), new Vector2(76, 20));
        lenLbl.characterSpacing = 18;

        var lenBox = MakePanel(row, "LenBox", Hex("08161aff"));
        Box(lenBox.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(190, 0), new Vector2(170, 30));
        Outline(lenBox.transform, Grid);
        MakeArrow(lenBox.rectTransform, "Back", true, OnBarsStep);
        MakeArrow(lenBox.rectTransform, "Fwd", false, OnBarsStep);
        _arrBarsLabel = MakeText(lenBox.rectTransform, "Label", "4 BARS", 16, Accent,
                                 TextAlignmentOptions.Center);
        Stretch(_arrBarsLabel.rectTransform, 22, 22, 0, 0);
        _arrBarsLabel.characterSpacing = 8;

        // DELETE names its target and takes two presses, same SURE? vocabulary
        // as the project shelf. Arming disarms on selection change and after a
        // few seconds.
        _arrDeleteBg = MakePanel(row, "Delete", Panel);
        _arrDeleteBg.raycastTarget = true;
        Box(_arrDeleteBg.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(378, 0), new Vector2(190, 30));
        Outline(_arrDeleteBg.transform, Warn);
        _arrDeleteLabel = MakeText(_arrDeleteBg.rectTransform, "Label", "DELETE SEC A", 13, Warn,
                                   TextAlignmentOptions.Center);
        Stretch(_arrDeleteLabel.rectTransform, 0, 0, 0, 0);
        var delBtn = _arrDeleteBg.gameObject.AddComponent<Button>();
        delBtn.targetGraphic = _arrDeleteBg;
        delBtn.onClick.AddListener(OnDeleteSection);

        // Right side: the song's worth. Meter + value + per-fan offers.
        _arrMeter = MakeRect(row, "Meter");
        Box(_arrMeter, new Vector2(1, 0.5f), new Vector2(1, 0.5f),
            new Vector2(-560, 0), new Vector2(140, 10));
        var meterFrame = Outline(_arrMeter, Grid);
        meterFrame.raycastTarget = false;
        for (int i = 0; i < 10; i++)
        {
            var seg = MakePanel(_arrMeter, "Seg" + i, Ink);
            var srt = seg.rectTransform;
            srt.anchorMin = new Vector2(0, 0);
            srt.anchorMax = new Vector2(0, 1);
            srt.pivot = new Vector2(0, 0.5f);
            srt.sizeDelta = new Vector2(0, 0);
            seg.gameObject.SetActive(false);
            _meterSegs.Add(seg);
        }

        _arrValue = MakeText(row, "Value", "", 13, Warn, TextAlignmentOptions.Right);
        Box(_arrValue.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f),
            new Vector2(-330, 0), new Vector2(220, 22));
        _arrValue.characterSpacing = 8;

        _arrOffers = MakeText(row, "Offers", "", 12, InkDim, TextAlignmentOptions.Right);
        Box(_arrOffers.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f),
            new Vector2(-2, 0), new Vector2(320, 22));
        _arrOffers.richText = true;
    }

    // ── song lifecycle ───────────────────────────────────────────────────

    /// Point the arranger at a new working song (NEW PROJECT / OPEN). The
    /// instrument gets section 0 and the compiled song in one move.
    void ResetSong(TraxSong song)
    {
        _song = song;
        _sel = 0;
        _delArmed = false;
        _lastPlayingSec = -1;
        _inst.LoadTrack(song.sections[0].track);
        // Share the reference so edits converge (LoadTrack clones).
        song.sections[0].track = _inst.Track;
        _inst.SetSong(song);
        _songStale = false;
        RebuildArranger();
    }

    /// inst.Track is replaced (immutably) on every edit; pull it back into the
    /// selected section. Reference-shared, so the check converges.
    void SyncSection()
    {
        if (_song == null) return;
        if (!ReferenceEquals(_song.sections[_sel].track, _inst.Track))
        {
            _song.sections[_sel].track = _inst.Track;
            SongEdited();
        }
    }

    void SongEdited()
    {
        _songStale = true;
        if (_inst.IsPlayingSong) { _inst.SetSong(_song); _songStale = false; }
    }

    void EnsureSongFresh()
    {
        if (!_songStale) return;
        _inst.SetSong(_song);
        _songStale = false;
    }

    void SelectSection(int i)
    {
        if (_song == null) return;
        _sel = Mathf.Clamp(i, 0, _song.sections.Count - 1);
        _delArmed = false;                  // SURE? must never carry over
        _inst.LoadTrack(_song.sections[_sel].track);
        _song.sections[_sel].track = _inst.Track;
        RefreshAllControls();
    }

    // Clicking a block only SELECTS it for editing — the song keeps playing
    // wherever it is, so you can tweak section C while the music is still in
    // A. Reverted from click-auditions (Sam, 2026-08-17): the ruler is the
    // one and only way to move the playhead.

    /// Ruler seek: live jump while the song plays; while stopped it just moves
    /// the cursor PLAY TRACK will start from.
    void SeekToStep(int stepPos)
    {
        if (_song == null) return;
        if (_songStale && _inst.IsPlayingSong) EnsureSongFresh();
        _inst.SeekSong(stepPos);
        UpdateArrPlayhead();
    }

    // ── section surgery ──────────────────────────────────────────────────

    void OnAddSection()
    {
        if (_song == null) return;
        int idx = _song.AddSection(_sel);
        if (idx < 0) { Toast("A SONG HOLDS AT MOST " + TraxSong.MaxSections + " SECTIONS"); return; }
        SongEdited();
        RebuildArranger();
        SelectSection(idx);
        RefreshReadouts();
    }

    void OnBarsStep(int delta)
    {
        if (_song == null) return;
        if (!_song.SetSectionBars(_sel, _song.sections[_sel].bars + delta)) return;
        _delArmed = false;
        SongEdited();
        RebuildArranger();
        RefreshArranger();
        RefreshReadouts();
    }

    void OnDeleteSection()
    {
        if (_song == null) return;
        if (_song.sections.Count <= 1) { Toast("A SONG NEEDS AT LEAST ONE SECTION"); return; }
        if (!_delArmed)
        {
            _delArmed = true;
            _delArmUntil = Time.unscaledTime + 3.5f;
            RefreshArrangerCtl();
            return;
        }
        _delArmed = false;
        string label = TraxSong.SectionLabel(_sel);
        if (!_song.RemoveSection(_sel)) return;
        SongEdited();
        RebuildArranger();
        SelectSection(Mathf.Min(_sel, _song.sections.Count - 1));
        RefreshReadouts();
        Toast("SEC " + label + " DELETED");
    }

    // ── rebuild / refresh ────────────────────────────────────────────────

    float ArrangerWidth { get { return ScreenW - SidePad * 2; } }

    /// Structural rebuild: blocks and ruler cells. Called when the section
    /// count or a length changes — never per knob tick.
    void RebuildArranger()
    {
        if (_song == null || _arrStrip == null) return;

        for (int i = 0; i < _arrBlockObjs.Count; i++) Destroy(_arrBlockObjs[i]);
        _arrBlockObjs.Clear(); _arrBlockBgs.Clear(); _arrBlockBorders.Clear();
        _arrBlockTops.Clear(); _arrBlockBots.Clear();
        _arrBlockX.Clear(); _arrBlockW.Clear();
        for (int i = 0; i < _arrRulerObjs.Count; i++) Destroy(_arrRulerObjs[i]);
        _arrRulerObjs.Clear();

        int n = _song.sections.Count;
        int totalBars = _song.TotalBars();
        float usable = ArrangerWidth - ArrAddW - ArrBlockGap - ArrBlockGap * (n - 1);
        float perBar = totalBars > 0 ? usable / totalBars : 0;

        float x = 0;
        int barNo = 1;
        for (int i = 0; i < n; i++)
        {
            TraxSection sec = _song.sections[i];
            float w = perBar * sec.bars;
            int captured = i;

            var block = MakePanel(_arrStrip, "Sec" + i, Panel);
            block.raycastTarget = true;
            var brt = block.rectTransform;
            brt.anchorMin = new Vector2(0, 0);
            brt.anchorMax = new Vector2(0, 1);
            brt.pivot = new Vector2(0, 0.5f);
            brt.sizeDelta = new Vector2(w, 0);
            brt.anchoredPosition = new Vector2(x, 0);
            var border = Outline(block.transform, Grid);

            var top = MakeText(brt, "Top", "", 15, Ink, TextAlignmentOptions.Center);
            var trt = top.rectTransform;
            trt.anchorMin = new Vector2(0, 0.5f);
            trt.anchorMax = new Vector2(1, 1);
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            top.characterSpacing = 8;

            var bot = MakeText(brt, "Bot", "", 10, InkDim, TextAlignmentOptions.Center);
            var btrt = bot.rectTransform;
            btrt.anchorMin = new Vector2(0, 0);
            btrt.anchorMax = new Vector2(1, 0.5f);
            btrt.offsetMin = Vector2.zero;
            btrt.offsetMax = Vector2.zero;
            bot.characterSpacing = 10;

            // Click AND drag on the same block: a plain click selects +
            // auditions; past the EventSystem's drag threshold it becomes a
            // reorder drag (which suppresses the click automatically —
            // eligibleForClick clears when the threshold is crossed).
            var handle = block.gameObject.AddComponent<SectionDragHandler>();
            handle.owner = this;
            handle.index = captured;

            _arrBlockObjs.Add(block.gameObject);
            _arrBlockBgs.Add(block);
            _arrBlockBorders.Add(border);
            _arrBlockTops.Add(top);
            _arrBlockBots.Add(bot);
            _arrBlockX.Add(x);
            _arrBlockW.Add(w);

            // The timescale over this block: one clickable cell per BEAT
            // (four per bar), so playback can start on the exact beat. Bar
            // lines run full height and carry the song-wide number (section
            // start + every 4th bar); beat ticks sit short and dim.
            float barW = w / sec.bars;
            float beatW = barW / 4f;
            for (int b = 0; b < sec.bars; b++)
            {
                for (int q = 0; q < 4; q++)
                {
                    int stepPos = (barNo - 1) * TraxPhrase.Steps + q * 4;
                    bool isBar = q == 0;

                    // The cell's colour IS the hover highlight (faint phosphor
                    // wash, matching the browser's :hover). A Button's tint
                    // MULTIPLIES the image colour, so a transparent image can
                    // never light up — instead the image carries the highlight
                    // and the NORMAL tint is the invisible state.
                    var cell = MakePanel(_arrRuler, "Beat", new Color(Ink.r, Ink.g, Ink.b, 0.10f));
                    cell.raycastTarget = true;
                    var crt = cell.rectTransform;
                    crt.anchorMin = new Vector2(0, 0);
                    crt.anchorMax = new Vector2(0, 1);
                    crt.pivot = new Vector2(0, 0.5f);
                    crt.sizeDelta = new Vector2(beatW, 0);
                    crt.anchoredPosition = new Vector2(x + b * barW + q * beatW, 0);

                    var tick = MakePanel(crt, "Tick", isBar ? InkGhost : Grid);
                    var tkrt = tick.rectTransform;
                    tkrt.anchorMin = new Vector2(0, 0);
                    tkrt.anchorMax = isBar ? new Vector2(0, 1) : new Vector2(0, 0.4f);
                    tkrt.pivot = new Vector2(0, 0);
                    tkrt.sizeDelta = new Vector2(1, 0);
                    tkrt.anchoredPosition = Vector2.zero;

                    if (isBar && b % 4 == 0)
                    {
                        var num = MakeText(crt, "Num", barNo.ToString(), 10, InkGhost,
                                           TextAlignmentOptions.Left);
                        Box(num.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                            new Vector2(4, 0), new Vector2(40, 14));
                    }

                    var cellBtn = cell.gameObject.AddComponent<Button>();
                    cellBtn.targetGraphic = cell;
                    var cb = cellBtn.colors;
                    cb.normalColor = new Color(1, 1, 1, 0);        // invisible at rest
                    cb.highlightedColor = Color.white;             // the wash shows
                    cb.pressedColor = new Color(1.8f, 1.8f, 1.8f, 1f);
                    cb.selectedColor = new Color(1, 1, 1, 0);      // don't stay lit after a click
                    cellBtn.colors = cb;
                    cellBtn.onClick.AddListener(delegate { SeekToStep(stepPos); });

                    _arrRulerObjs.Add(cell.gameObject);
                }
                barNo++;
            }

            x += w + ArrBlockGap;
        }

        _lastPlayingSec = -1;
        RefreshArranger();
    }

    /// Cheap refresh: colours, labels, selection, stats, value. Safe to call
    /// per edit.
    void RefreshArranger()
    {
        if (_song == null || _arrBlockBgs.Count != _song.sections.Count) return;

        for (int i = 0; i < _song.sections.Count; i++)
        {
            TraxSection sec = _song.sections[i];
            Color gc = GenreColorOf(TraxClassifier.Classify(sec.track.dials).primary.name);
            bool selected = i == _sel;
            bool playing = i == _lastPlayingSec;
            _arrBlockBgs[i].color = new Color(gc.r, gc.g, gc.b, selected ? 0.22f : 0.10f);
            _arrBlockBorders[i].color = selected ? Accent : playing ? Color.white : gc;
            _arrBlockTops[i].text = TraxSong.SectionLabel(i) + " - " + sec.bars;
            _arrBlockBots[i].text = TraxClassifier.Classify(sec.track.dials).primary.name;
            _arrBlockBots[i].color = gc;
        }

        _arrStats.text = _song.sections.Count + " SEC  -  " + _song.TotalBars() + " BARS";
        bool full = _song.sections.Count >= TraxSong.MaxSections;
        _arrAddLabel.color = full ? Locked : InkDim;

        RefreshArrangerCtl();
        RefreshArrangerValue();
    }

    void RefreshArrangerCtl()
    {
        if (_arrSecTag == null || _song == null) return;
        string label = TraxSong.SectionLabel(_sel);
        _arrSecTag.text = "SEC " + label;
        _arrBarsLabel.text = _song.sections[_sel].bars + " BARS";

        bool canDelete = _song.sections.Count > 1;
        _arrDeleteLabel.text = _delArmed ? "SURE?" : "DELETE SEC " + label;
        _arrDeleteLabel.color = !canDelete ? Locked : _delArmed ? Hex("1a1200ff") : Warn;
        _arrDeleteBg.color = _delArmed ? Warn : Panel;
        var border = _arrDeleteBg.transform.Find("Border");
        if (border != null) border.GetComponent<Image>().color = canDelete ? Warn : Locked;
    }

    void RefreshArrangerValue()
    {
        var mix = _song.GenreMix();

        float mw = _arrMeter.rect.width;
        if (mw <= 0) mw = 140f;
        float mx = 0;
        for (int i = 0; i < _meterSegs.Count; i++)
        {
            if (i < mix.Count)
            {
                float w = (float)(mix[i].share * mw);
                var rt = _meterSegs[i].rectTransform;
                rt.sizeDelta = new Vector2(w, 0);
                rt.anchoredPosition = new Vector2(mx, 0);
                _meterSegs[i].color = GenreColorOf(mix[i].name);
                _meterSegs[i].gameObject.SetActive(true);
                mx += w;
            }
            else _meterSegs[i].gameObject.SetActive(false);
        }

        _arrValue.text = "FULL TRACK x" + _song.ValueMult().ToString("0.00") + " DEMO BASE";

        // Per-fan ×N previews died with OfferMult: under the weighted-sat
        // model the real number depends on the listener, and a number we
        // cannot keep is a promise we must not print. Names only.
        var sb = new System.Text.StringBuilder();
        sb.Append("SELLS TO ");
        int shown = mix.Count < 3 ? mix.Count : 3;
        for (int i = 0; i < shown; i++)
        {
            if (i > 0) sb.Append(" · ");
            Color gc = GenreColorOf(mix[i].name);
            sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(gc)).Append('>');
            sb.Append(mix[i].name).Append(" FANS</color>");
        }
        if (mix.Count > 3) sb.Append(" +").Append(mix.Count - 3);
        _arrOffers.text = sb.ToString();
    }

    // ── drag-reorder ─────────────────────────────────────────────────────
    // Grab a block, drop it in a new slot: 1-2-3, grab 3, drop between 1 and
    // 2, get 1-3-2. Same behaviour as the browser prototype.

    class SectionDragHandler : MonoBehaviour, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public ShuttleComputerUI owner;
        public int index;
        public void OnPointerClick(PointerEventData e) { owner.OnSectionPointerClick(index); }
        public void OnBeginDrag(PointerEventData e) { owner.OnSectionBeginDrag(index, e); }
        public void OnDrag(PointerEventData e) { owner.OnSectionDrag(e); }
        public void OnEndDrag(PointerEventData e) { owner.OnSectionEndDrag(); }
    }

    int _dragFrom = -1, _dragDest = -1;
    Image _arrDropLine;

    void OnSectionPointerClick(int i)
    {
        // A real drag never lands here — the EventSystem clears
        // eligibleForClick once the drag threshold is crossed.
        SelectSection(i);
    }

    void OnSectionBeginDrag(int i, PointerEventData e)
    {
        if (_song == null || _song.sections.Count < 2) return;
        _dragFrom = i;
        _dragDest = i;
        if (i < _arrBlockBgs.Count)
        {
            Color c = _arrBlockBgs[i].color;
            c.a = 0.5f;
            _arrBlockBgs[i].color = c;
        }
        OnSectionDrag(e);
    }

    void OnSectionDrag(PointerEventData e)
    {
        if (_dragFrom < 0 || _arrDropLine == null || _song == null) return;
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _arrStrip, e.position, e.pressEventCamera, out local)) return;
        float xLeft = local.x + _arrStrip.rect.width * 0.5f;

        int dest = _song.sections.Count;
        for (int i = 0; i < _arrBlockX.Count; i++)
            if (xLeft < _arrBlockX[i] + _arrBlockW[i] * 0.5f) { dest = i; break; }
        _dragDest = dest;

        float lineX = dest < _arrBlockX.Count
            ? _arrBlockX[dest] - ArrBlockGap * 0.5f
            : _arrBlockX[_arrBlockX.Count - 1] + _arrBlockW[_arrBlockW.Count - 1] + ArrBlockGap * 0.5f;
        _arrDropLine.gameObject.SetActive(true);
        _arrDropLine.rectTransform.anchoredPosition = new Vector2(lineX, ArrStripY);
    }

    void OnSectionEndDrag()
    {
        if (_dragFrom < 0) return;
        int from = _dragFrom, to = _dragDest;
        _dragFrom = -1;
        _dragDest = -1;
        if (_arrDropLine != null) _arrDropLine.gameObject.SetActive(false);

        if (_song != null && _song.MoveSection(from, to))
        {
            SongEdited();
            RebuildArranger();
            // Selection follows the grabbed section to where it landed —
            // an edit, not an audition, so no seek and no autoplay.
            SelectSection(to > from ? to - 1 : to);
            RefreshReadouts();
        }
        else RefreshArranger();          // restore the dimmed block
    }

    // ── playhead ─────────────────────────────────────────────────────────

    void SetPlayingSec(int i)
    {
        if (i == _lastPlayingSec) return;
        _lastPlayingSec = i;
        // Border flips only — a full refresh eight times a bar would churn.
        for (int b = 0; b < _arrBlockBorders.Count && b < _song.sections.Count; b++)
        {
            Color gc = GenreColorOf(TraxClassifier.Classify(_song.sections[b].track.dials).primary.name);
            _arrBlockBorders[b].color = b == _sel ? Accent : b == i ? Color.white : gc;
        }
    }

    /// The line over the timeline: live position while the song plays, dimmed
    /// cursor (where PLAY TRACK will start) while stopped.
    void UpdateArrPlayhead()
    {
        if (_arrPlayhead == null || _song == null || _arrBlockX.Count == 0) return;

        int pos;
        bool live = _inst.IsPlayingSong && _inst.CurrentStep >= 0;
        if (live) pos = _inst.CurrentStep;
        else pos = _inst.SongCursor;

        int total = _song.TotalSteps();
        if (total <= 0) { _arrPlayhead.gameObject.SetActive(false); return; }
        pos = ((pos % total) + total) % total;

        int idx, stepInSection, barInSection;
        _song.SectionAtStep(pos, out idx, out stepInSection, out barInSection);
        if (idx >= _arrBlockX.Count) { _arrPlayhead.gameObject.SetActive(false); return; }

        float f = (float)stepInSection / (_song.sections[idx].bars * TraxPhrase.Steps);
        float lineX = _arrBlockX[idx] + f * _arrBlockW[idx];

        _arrPlayhead.gameObject.SetActive(true);
        _arrPlayhead.color = live ? Accent : InkDim;
        _arrPlayhead.rectTransform.anchoredPosition =
            new Vector2(lineX, -(ArrHeaderH + ArrGap));

        SetPlayingSec(live ? idx : -1);
    }

    /// Everything the editor shows about the selected section, refreshed in
    /// one sweep — used when the selection changes and the whole surface has
    /// to snap to a different track.
    void RefreshAllControls()
    {
        for (int i = 0; i < _knobs.Count; i++)
            _knobs[i].SetSilent(_inst.Dials.Get(_knobs[i].DialIndex));
        RefreshRack();
        RefreshReadouts();
        RefreshArranger();
        _lastBarShown = -1;
    }

    void ArrangerUpdate()
    {
        if (_delArmed && Time.unscaledTime > _delArmUntil)
        {
            _delArmed = false;
            RefreshArrangerCtl();
        }
        UpdateArrPlayhead();
    }
}
