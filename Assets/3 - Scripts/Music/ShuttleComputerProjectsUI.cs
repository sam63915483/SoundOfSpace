using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The TRAX project screen — Unity half of
/// <c>prototypes/shuttle-computer/ui/projects.js</c>, approved in the browser
/// before this was written.
///
///   TRAX  ->  [ NEW PROJECT ]   -> the instrument, on a blank track
///             [ LOAD PROJECT ]  -> the shelf, pick one, it opens
///
/// It is a screen inside the TRAX app rather than a fourth OS view, on purpose:
/// this menu belongs to one application, not to the desktop.
///
/// Every string here is ASCII. The LiberationSans SDF atlas this project uses
/// has no arrows or symbols and renders missing glyphs as boxes — a trap that
/// browsers hide with font fallback, so it only ever shows up in Unity.
/// </summary>
public partial class ShuttleComputerUI
{
    GameObject _projectsView, _shelfPane, _menuPane, _savePanel;
    RectTransform _shelfRows;
    TextMeshProUGUI _loadSub, _shelfCount, _shelfWarn;
    TMP_InputField _saveField;
    TextMeshProUGUI _saveNote, _projBarName, _projBarState;
    Image _saveConfirm;
    TextMeshProUGUI _saveConfirmLabel;

    readonly List<GameObject> _shelfRowObjects = new List<GameObject>();
    string _armedDeleteId;                  // the row showing SURE?

    /// The record being edited, or null for a project that has never been
    /// saved. Only its name and id are read.
    TraxLibrary.Record _project;
    uint _savedTrackId;
    int _shelfVersionShown = -1;

    public bool SaveOpen { get { return _savePanel != null && _savePanel.activeSelf; } }
    public bool ProjectsOpen { get { return _projectsView != null && _projectsView.activeSelf; } }

    // ── construction ─────────────────────────────────────────────────────

    void BuildProjects(RectTransform parent)
    {
        var view = MakeRect(parent, "ProjectsView");
        Stretch(view, SidePad, SidePad, ContentTop, ContentBottom);
        _projectsView = view.gameObject;

        BuildProjectMenu(view);
        BuildShelf(view);

        _projectsView.SetActive(false);
    }

    void BuildProjectMenu(RectTransform parent)
    {
        var pane = MakeRect(parent, "MenuPane");
        Stretch(pane, 0, 0, 0, 0);
        _menuPane = pane.gameObject;

        var brand = MakeText(pane, "Brand", "TRAX", 96, Accent, TextAlignmentOptions.Center);
        Box(brand.rectTransform, Centre, Centre, new Vector2(0, 210), new Vector2(700, 110));
        brand.characterSpacing = 26;

        var tag = MakeText(pane, "Tag", "PATTERN SYNTHESIS SUITE", 14, InkGhost,
                           TextAlignmentOptions.Center);
        Box(tag.rectTransform, Centre, Centre, new Vector2(0, 150), new Vector2(700, 22));
        tag.characterSpacing = 26;

        MakeMenuButton(pane, "NewProject", "NEW PROJECT", "start from a blank track",
                       new Vector2(0, 60), Accent, OnNewProject);

        Image loadBtn = MakeMenuButton(pane, "LoadProject", "LOAD PROJECT", "",
                                       new Vector2(0, -30), InkGhost, OnLoadProject);
        _loadSub = loadBtn.transform.Find("Sub").GetComponent<TextMeshProUGUI>();

        MakeFlatButton(pane, "Home", "HOME", new Vector2(0, -140), new Vector2(180, 44),
                       ShowHomeFromProjects);
    }

    /// A big left-bar button: title on top, one line of state underneath. The
    /// bar is what carries the accent, so a disabled button reads as dimmed
    /// rather than as a different shape.
    Image MakeMenuButton(RectTransform parent, string name, string title, string sub,
                         Vector2 pos, Color bar, UnityEngine.Events.UnityAction onClick)
    {
        var panel = MakePanel(parent, name, PanelHi);
        panel.raycastTarget = true;
        Box(panel.rectTransform, Centre, Centre, pos, new Vector2(520, 78));
        Outline(panel.transform, InkGhost);

        var edge = MakePanel(panel.rectTransform, "Edge", bar);
        Box(edge.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(2, 0), new Vector2(3, 70));

        var t = MakeText(panel.rectTransform, "Title", title, 22, Ink, TextAlignmentOptions.Left);
        Box(t.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -14),
            new Vector2(460, 30));
        t.characterSpacing = 14;

        var s = MakeText(panel.rectTransform, "Sub", sub, 13, InkGhost, TextAlignmentOptions.Left);
        Box(s.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -46),
            new Vector2(460, 20));
        s.characterSpacing = 6;

        var btn = panel.gameObject.AddComponent<Button>();
        btn.targetGraphic = panel;
        btn.onClick.AddListener(onClick);
        return panel;
    }

    Image MakeFlatButton(RectTransform parent, string name, string label, Vector2 pos,
                         Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        var panel = MakePanel(parent, name, Panel);
        panel.raycastTarget = true;
        Box(panel.rectTransform, Centre, Centre, pos, size);
        Outline(panel.transform, InkGhost);
        var t = MakeText(panel.rectTransform, "Label", label, 16, InkDim, TextAlignmentOptions.Center);
        Stretch(t.rectTransform, 0, 0, 0, 0);
        t.characterSpacing = 12;
        var btn = panel.gameObject.AddComponent<Button>();
        btn.targetGraphic = panel;
        btn.onClick.AddListener(onClick);
        return panel;
    }

    void BuildShelf(RectTransform parent)
    {
        var pane = MakeRect(parent, "ShelfPane");
        Stretch(pane, 0, 0, 0, 0);
        _shelfPane = pane.gameObject;

        var title = MakeText(pane, "Title", "SAVED PROJECTS", 22, Accent, TextAlignmentOptions.Left);
        Box(title.rectTransform, TopLeft, TopLeft, new Vector2(4, -6), new Vector2(400, 30));
        title.characterSpacing = 20;

        _shelfCount = MakeText(pane, "Count", "", 13, InkGhost, TextAlignmentOptions.Right);
        Box(_shelfCount.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-4, -10),
            new Vector2(300, 22));
        _shelfCount.characterSpacing = 12;

        var rule = MakePanel(pane, "Rule", Grid);
        Box(rule.rectTransform, TopLeft, TopLeft, new Vector2(0, -40), new Vector2(0, 1));
        rule.rectTransform.anchorMax = new Vector2(1, 1);
        rule.rectTransform.sizeDelta = new Vector2(0, 1);

        // Viewport clips; content grows downward and scrolls inside it.
        var viewport = MakeRect(pane, "Viewport");
        Stretch(viewport, 0, 0, 48, 56);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = MakeRect(viewport, "Content");
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.sizeDelta = new Vector2(0, 0);
        content.anchoredPosition = Vector2.zero;
        _shelfRows = content;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        // Bottom-left rather than centred, so re-anchor after MakeFlatButton.
        Image back = MakeFlatButton(pane, "Back", "BACK", Vector2.zero, new Vector2(160, 40), ShowMenuPane);
        Box(back.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(4, 8),
            new Vector2(160, 40));

        _shelfWarn = MakeText(pane, "Warn", "", 12, InkGhost, TextAlignmentOptions.Left);
        Box(_shelfWarn.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(176, 18),
            new Vector2(700, 20));

        _shelfPane.SetActive(false);
    }

    // ── the shelf rows ───────────────────────────────────────────────────

    const float RowH = 60f, RowGap = 7f;

    void RebuildShelf()
    {
        for (int i = 0; i < _shelfRowObjects.Count; i++) Destroy(_shelfRowObjects[i]);
        _shelfRowObjects.Clear();
        _armedDeleteId = null;

        List<TraxLibrary.Record> recs = TraxLibrary.SortedRecent();
        _shelfCount.text = recs.Count + (recs.Count == 1 ? " PROJECT" : " PROJECTS");
        _shelfWarn.text = "";

        if (recs.Count == 0)
        {
            var empty = MakeText(_shelfRows, "Empty", "THE SHELF IS EMPTY", 16, InkGhost,
                                 TextAlignmentOptions.Center);
            Box(empty.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0, -60), new Vector2(600, 24));
            empty.characterSpacing = 18;
            _shelfRowObjects.Add(empty.gameObject);
            _shelfRows.sizeDelta = new Vector2(0, 120);
            return;
        }

        for (int i = 0; i < recs.Count; i++) BuildShelfRow(recs[i], i);
        _shelfRows.sizeDelta = new Vector2(0, recs.Count * (RowH + RowGap));
    }

    void BuildShelfRow(TraxLibrary.Record rec, int index)
    {
        TraxLibrary.Record captured = rec;

        var row = MakePanel(_shelfRows, "Row" + index, Panel);
        row.raycastTarget = true;
        var rt = row.rectTransform;
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(-8, RowH);
        rt.anchoredPosition = new Vector2(0, -index * (RowH + RowGap));
        Outline(row.transform, Grid);
        _shelfRowObjects.Add(row.gameObject);

        var edge = MakePanel(rt, "Edge", InkGhost);
        Box(edge.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(1, 0),
            new Vector2(3, RowH - 8));

        var nm = MakeText(rt, "Name", rec.name.ToUpperInvariant(), 20, Ink, TextAlignmentOptions.Left);
        Box(nm.rectTransform, TopLeft, TopLeft, new Vector2(16, -8), new Vector2(560, 26));
        nm.characterSpacing = 12;

        // Genre, identity and when it was saved — the same three facts the
        // browser row shows, so the screens read as the same product.
        string meta = TraxClassifier.Classify(rec.track.dials).label +
                      "   ID " + rec.trackId.ToString("X8") +
                      "   " + StampOf(rec.savedAt);
        var mt = MakeText(rt, "Meta", meta, 12, InkGhost, TextAlignmentOptions.Left);
        Box(mt.rectTransform, TopLeft, TopLeft, new Vector2(16, -34), new Vector2(620, 20));

        var open = MakePanel(rt, "Open", Ink);
        open.raycastTarget = true;
        Box(open.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-116, 0),
            new Vector2(96, 34));
        var openTxt = MakeText(open.rectTransform, "Label", "OPEN", 14, Hex("04120eff"),
                               TextAlignmentOptions.Center);
        Stretch(openTxt.rectTransform, 0, 0, 0, 0);
        var openBtn = open.gameObject.AddComponent<Button>();
        openBtn.targetGraphic = open;
        openBtn.onClick.AddListener(delegate { OpenProject(captured); });

        // Two-step delete rather than a modal: the second press confirms, and
        // opening anything else disarms it. No blocking dialogs.
        var del = MakePanel(rt, "Delete", Panel);
        del.raycastTarget = true;
        Box(del.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0),
            new Vector2(96, 34));
        Outline(del.transform, Warn);
        var delTxt = MakeText(del.rectTransform, "Label", "DELETE", 14, Warn,
                              TextAlignmentOptions.Center);
        Stretch(delTxt.rectTransform, 0, 0, 0, 0);
        var delBtn = del.gameObject.AddComponent<Button>();
        delBtn.targetGraphic = del;
        delBtn.onClick.AddListener(delegate
        {
            if (_armedDeleteId != captured.id)
            {
                _armedDeleteId = captured.id;
                RefreshDeleteLabels();
                return;
            }
            TraxLibrary.Delete(captured.id);
            if (_project != null && _project.id == captured.id) _project = null;
            RebuildShelf();
            RefreshMenuPane();
        });

        // The whole row opens it, so the buttons are shortcuts rather than the
        // only targets.
        var rowBtn = row.gameObject.AddComponent<Button>();
        rowBtn.targetGraphic = row;
        rowBtn.onClick.AddListener(delegate { OpenProject(captured); });
    }

    void RefreshDeleteLabels()
    {
        // Rows were built in SortedRecent order, so index maps straight back to
        // it. Sorted once, outside the loop.
        List<TraxLibrary.Record> recs = TraxLibrary.SortedRecent();
        for (int i = 0; i < _shelfRowObjects.Count && i < recs.Count; i++)
        {
            Transform del = _shelfRowObjects[i].transform.Find("Delete");
            if (del == null) continue;
            var label = del.Find("Label").GetComponent<TextMeshProUGUI>();
            var img = del.GetComponent<Image>();
            bool armed = recs[i].id == _armedDeleteId;
            label.text = armed ? "SURE?" : "DELETE";
            label.color = armed ? Hex("1a1200ff") : Warn;
            img.color = armed ? Warn : Panel;
        }
    }

    static string StampOf(long unix)
    {
        var utc = new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
        System.DateTime d = utc.AddSeconds(unix).ToLocalTime();
        return d.ToString("yyyy-MM-dd  HH:mm");
    }

    // ── navigation ───────────────────────────────────────────────────────

    /// TRAX now opens HERE, not on the dials.
    void ShowProjects()
    {
        _homeView.SetActive(false);
        _traxView.SetActive(false);
        _projectsView.SetActive(true);
        CloseSaveDialog();
        _statusText.text = "TRAX  -  PROJECTS";
        ShowMenuPane();
    }

    void ShowMenuPane()
    {
        _menuPane.SetActive(true);
        _shelfPane.SetActive(false);
        RefreshMenuPane();
    }

    void ShowShelfPane()
    {
        RebuildShelf();
        _shelfVersionShown = TraxLibrary.Version;
        _menuPane.SetActive(false);
        _shelfPane.SetActive(true);
    }

    void RefreshMenuPane()
    {
        int n = TraxLibrary.Count;
        _loadSub.text = n == 0 ? "nothing saved yet"
                      : n == 1 ? "1 project on the shelf"
                      : n + " projects on the shelf";
        _loadSub.color = n == 0 ? Locked : InkGhost;
    }

    void ShowHomeFromProjects()
    {
        _inst.Stop();
        ShowHome();
    }

    void OnNewProject()
    {
        _project = null;
        _inst.LoadTrack(TraxInstrument.NewTrack());
        _savedTrackId = 0;                       // "never saved", not "clean"
        ShowTrax();
    }

    void OnLoadProject()
    {
        if (TraxLibrary.Count == 0) return;      // the button is dead, not silently clickable
        ShowShelfPane();
    }

    void OpenProject(TraxLibrary.Record rec)
    {
        if (rec == null) return;
        _project = rec;
        _inst.LoadTrack(rec.track);
        _savedTrackId = _inst.TrackId;
        ShowTrax();
    }

    // ── the project bar on the instrument ────────────────────────────────

    void BuildProjectBar(RectTransform parent)
    {
        var bar = MakeRect(parent, "ProjectBar");
        bar.anchorMin = new Vector2(0, 1);
        bar.anchorMax = new Vector2(1, 1);
        bar.pivot = new Vector2(0.5f, 1);
        bar.sizeDelta = new Vector2(0, 26);
        bar.anchoredPosition = Vector2.zero;

        var label = MakeText(bar, "Label", "PROJECT", 11, InkGhost, TextAlignmentOptions.Left);
        Box(label.rectTransform, TopLeft, TopLeft, new Vector2(2, -4), new Vector2(90, 18));
        label.characterSpacing = 22;

        _projBarName = MakeText(bar, "Name", "UNTITLED", 16, Ink, TextAlignmentOptions.Left);
        Box(_projBarName.rectTransform, TopLeft, TopLeft, new Vector2(84, -3), new Vector2(500, 20));
        _projBarName.characterSpacing = 14;

        _projBarState = MakeText(bar, "State", "", 11, InkGhost, TextAlignmentOptions.Right);
        Box(_projBarState.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-2, -4),
            new Vector2(320, 18));
        _projBarState.characterSpacing = 14;

        var rule = MakePanel(bar, "Rule", Grid);
        rule.rectTransform.anchorMin = new Vector2(0, 0);
        rule.rectTransform.anchorMax = new Vector2(1, 0);
        rule.rectTransform.pivot = new Vector2(0.5f, 0);
        rule.rectTransform.sizeDelta = new Vector2(0, 1);
        rule.rectTransform.anchoredPosition = Vector2.zero;
    }

    /// Dirtiness is DERIVED from the track identity, not bookkept — so undoing
    /// an edit correctly goes back to SAVED instead of staying dirty forever.
    bool ProjectDirty { get { return _project == null || _inst.TrackId != _savedTrackId; } }

    void RefreshProjectBar()
    {
        if (_projBarName == null) return;
        _projBarName.text = _project != null ? _project.name.ToUpperInvariant() : "UNTITLED";
        _projBarName.color = _project != null ? Ink : InkGhost;

        bool dirty = ProjectDirty;
        _projBarState.text = _project == null ? "NEVER SAVED" : dirty ? "UNSAVED CHANGES" : "SAVED";
        _projBarState.color = dirty ? Warn : InkGhost;
    }

    // ── the save dialog ──────────────────────────────────────────────────

    void BuildSaveDialog(RectTransform parent)
    {
        var scrim = MakePanel(parent, "SaveScrim", new Color(0, 0, 0, 0.72f));
        scrim.raycastTarget = true;
        Stretch(scrim.rectTransform, 0, 0, 0, 0);
        _savePanel = scrim.gameObject;

        var panel = MakePanel(scrim.rectTransform, "Panel", PanelHi);
        var prt = panel.rectTransform;
        Box(prt, Centre, Centre, Vector2.zero, new Vector2(620, 300));
        Outline(panel.transform, Accent);

        var title = MakeText(prt, "Title", "SAVE PROJECT", 30, Accent, TextAlignmentOptions.Top);
        Box(title.rectTransform, TopCentre, TopCentre, new Vector2(0, -26), new Vector2(580, 38));
        title.characterSpacing = 14;

        var sub = MakeText(prt, "Sub", "NAME THIS TRACK", 15, InkDim, TextAlignmentOptions.Top);
        Box(sub.rectTransform, TopCentre, TopCentre, new Vector2(0, -68), new Vector2(580, 22));
        sub.characterSpacing = 18;

        // The field itself. TMP_InputField needs a real text child and a
        // caret-safe viewport, so it is assembled rather than styled in place.
        var fieldBg = MakePanel(prt, "Field", Bg);
        fieldBg.raycastTarget = true;
        Box(fieldBg.rectTransform, TopCentre, TopCentre, new Vector2(0, -104), new Vector2(540, 56));
        Outline(fieldBg.transform, InkGhost);

        var textArea = MakeRect(fieldBg.rectTransform, "TextArea");
        Stretch(textArea, 12, 12, 6, 6);
        textArea.gameObject.AddComponent<RectMask2D>();

        var text = MakeText(textArea, "Text", "", 26, Ink, TextAlignmentOptions.Center);
        Stretch(text.rectTransform, 0, 0, 0, 0);
        text.characterSpacing = 12;
        text.raycastTarget = false;

        var placeholder = MakeText(textArea, "Placeholder", "UNTITLED", 26, Locked,
                                   TextAlignmentOptions.Center);
        Stretch(placeholder.rectTransform, 0, 0, 0, 0);
        placeholder.characterSpacing = 12;

        _saveField = fieldBg.gameObject.AddComponent<TMP_InputField>();
        _saveField.textViewport = textArea;
        _saveField.textComponent = text;
        _saveField.placeholder = placeholder;
        _saveField.characterLimit = TraxLibrary.NameMax;
        _saveField.lineType = TMP_InputField.LineType.SingleLine;
        _saveField.customCaretColor = true;
        _saveField.caretColor = Accent;
        _saveField.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
        _saveField.onValueChanged.AddListener(delegate { RefreshSaveNote(); });
        _saveField.onSubmit.AddListener(delegate { CommitSave(); });

        _saveNote = MakeText(prt, "Note", "", 13, InkGhost, TextAlignmentOptions.Center);
        Box(_saveNote.rectTransform, TopCentre, TopCentre, new Vector2(0, -176), new Vector2(580, 20));

        var cancel = MakePanel(prt, "Cancel", Panel);
        cancel.raycastTarget = true;
        Box(cancel.rectTransform, Centre, Centre, new Vector2(-92, -104), new Vector2(160, 44));
        Outline(cancel.transform, InkGhost);
        var cancelTxt = MakeText(cancel.rectTransform, "Label", "CANCEL", 17, InkDim,
                                 TextAlignmentOptions.Center);
        Stretch(cancelTxt.rectTransform, 0, 0, 0, 0);
        var cancelBtn = cancel.gameObject.AddComponent<Button>();
        cancelBtn.targetGraphic = cancel;
        cancelBtn.onClick.AddListener(CloseSaveDialog);

        _saveConfirm = MakePanel(prt, "Confirm", Ink);
        _saveConfirm.raycastTarget = true;
        Box(_saveConfirm.rectTransform, Centre, Centre, new Vector2(92, -104), new Vector2(160, 44));
        _saveConfirmLabel = MakeText(_saveConfirm.rectTransform, "Label", "SAVE", 17,
                                     Hex("04120eff"), TextAlignmentOptions.Center);
        Stretch(_saveConfirmLabel.rectTransform, 0, 0, 0, 0);
        var okBtn = _saveConfirm.gameObject.AddComponent<Button>();
        okBtn.targetGraphic = _saveConfirm;
        okBtn.onClick.AddListener(CommitSave);

        _savePanel.SetActive(false);
    }

    void OpenSaveDialog()
    {
        if (_savePanel == null) return;
        _savePanel.SetActive(true);
        _saveField.text = _project != null ? _project.name : "";
        RefreshSaveNote();
        _saveField.Select();
        _saveField.ActivateInputField();
    }

    void CloseSaveDialog()
    {
        if (_savePanel == null || !_savePanel.activeSelf) return;
        _saveField.DeactivateInputField();
        _savePanel.SetActive(false);
    }

    void RefreshSaveNote()
    {
        string typed = TraxLibrary.NormalizeName(_saveField.text);
        bool valid = typed.Length > 0;

        if (!valid)
        {
            _saveNote.text = "a name is required";
            _saveNote.color = Warn;
        }
        else
        {
            TraxLibrary.Record clash = TraxLibrary.FindByName(typed);
            bool mine = _project != null && clash != null && clash.id == _project.id;
            if (clash != null && !mine) { _saveNote.text = "overwrites the project already called that"; _saveNote.color = Warn; }
            else if (mine) { _saveNote.text = "saves over this project"; _saveNote.color = InkGhost; }
            else { _saveNote.text = "saves as a new project"; _saveNote.color = InkGhost; }
        }

        _saveConfirm.color = valid ? Ink : Locked;
        _saveConfirmLabel.color = valid ? Hex("04120eff") : InkGhost;
    }

    void CommitSave()
    {
        string typed = TraxLibrary.NormalizeName(_saveField.text);
        if (typed.Length == 0) return;

        TraxLibrary.Record rec = TraxLibrary.Save(typed, _inst.Track, NowUnix());
        if (rec == null) return;

        _project = rec;
        _savedTrackId = _inst.TrackId;
        CloseSaveDialog();
        RefreshProjectBar();
        _statusText.text = "TRAX  -  " + rec.name.ToUpperInvariant();
        Toast("SAVED - " + rec.name.ToUpperInvariant());
    }

    /// Wall-clock seconds. Only ever used as a sort key and a display stamp —
    /// nothing about the engine or a track depends on it.
    static long NowUnix()
    {
        return (long)(System.DateTime.UtcNow - new System.DateTime(1970, 1, 1, 0, 0, 0,
                      System.DateTimeKind.Utc)).TotalSeconds;
    }
}
