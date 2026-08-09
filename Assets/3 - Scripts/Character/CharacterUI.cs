using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Every character screen in the main menu: the no-characters popup, the
/// create/edit screen, the CHARACTERS list, and the quick picker.
///
/// ── Built entirely in code, on purpose ───────────────────────────────────
/// MainMenu.unity holds four objects (EventSystem, MenuRoot, Cleanup, Main
/// Camera) — MainMenuController builds the whole menu at runtime. These screens
/// follow suit, so there is nothing to author in the scene and nothing that can
/// come unwired. The one exception is the astronaut prefab reference, which has
/// to be a real asset link; see MainMenuController's serialized field.
///
/// The palette and the modal shape deliberately mirror MainMenuController's
/// credits panel. Duplicated rather than shared, matching the precedent set by
/// AIChatScreen ("duplicated from PlayerPhoneUI so this screen is
/// self-contained") — keep in sync if the menu palette changes.
///
/// ── Flow ─────────────────────────────────────────────────────────────────
/// "Remembered identity": the menu never makes you pick a character. It uses
/// your last one until you deliberately change it via the chip under the title
/// or the CHARACTERS button. RequireCharacter is the only gate, and it only
/// fires when you own zero characters.
/// </summary>
public class CharacterUI : MonoBehaviour
{
    public static CharacterUI Instance { get; private set; }

    // ── palette (mirrors MainMenuController) ─────────────────────────────
    static readonly Color AccentCool  = new Color32(0x5B, 0xD8, 0xFF, 0xFF);
    static readonly Color AccentHot   = new Color32(0xC9, 0x4F, 0xFF, 0xFF);
    static readonly Color LabelWhite  = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
    static readonly Color SubtleText  = new Color32(0x8F, 0x89, 0xAD, 0xFF);
    static readonly Color CardBg      = new Color32(0x0B, 0x08, 0x1C, 0xF7);
    static readonly Color RowBg       = new Color32(0x14, 0x0E, 0x30, 0xBF);
    static readonly Color RowBgSel    = new Color32(0x2D, 0x1E, 0x5C, 0xE6);
    static readonly Color FieldBg     = new Color32(0x00, 0x00, 0x00, 0x73);
    static readonly Color Backdrop    = new Color32(0x00, 0x00, 0x00, 0xC7);
    static readonly Color DangerText  = new Color32(0xFF, 0xB3, 0xAC, 0xFF);
    static readonly Color DangerLine  = new Color32(0xD9, 0x4A, 0x3D, 0xFF);

    /// Supplied by MainMenuController from its serialized field. Null is
    /// tolerated everywhere — the create screen falls back to a flat colour
    /// plate, so a missing reference costs you the 3D model and nothing else.
    GameObject _astronautPrefab;

    /// One rig, reused across every open of the create screen. Building it costs
    /// an Instantiate plus a RenderTexture, so it is not worth doing twice.
    AstronautPreview _preview;

    GameObject _modalRoot;          // the currently open modal, if any

    /// "The character UI is now fully closed." Set ONCE by a public entry point
    /// and fired exactly once by CloseAll, whatever path got there.
    ///
    /// Screens navigate between themselves by calling each other's Build*
    /// methods directly — they must never touch this. An earlier version had
    /// each screen carry its own "what next" callback, and nesting create
    /// inside the popup silently overwrote the continuation, so creating your
    /// first character left you staring at the menu instead of launching. One
    /// callback, owned by the entry point, makes that unrepresentable.
    Action _onClosed;

    // Create/edit screen state
    string _editingId;              // null = creating a new character
    string _draftName = "";
    int    _draftSwatch;
    readonly List<Image> _swatchFrames = new List<Image>();
    TextMeshProUGUI _counterLabel, _errorLabel, _swatchNameLabel;
    Button          _confirmButton;
    TextMeshProUGUI _confirmLabel;

    public static CharacterUI Ensure(Transform parent, GameObject astronautPrefab)
    {
        if (Instance == null)
        {
            var go = new GameObject("CharacterUI");
            go.transform.SetParent(parent, false);
            Instance = go.AddComponent<CharacterUI>();
        }
        Instance._astronautPrefab = astronautPrefab;
        return Instance;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_preview != null) _preview.Dispose();
    }

    // ── entry points ─────────────────────────────────────────────────────

    /// The only gate in the flow. If a character exists, `onReady` runs
    /// immediately and the player never sees a screen. If not, they are walked
    /// through creating one and `onReady` runs afterwards.
    public void RequireCharacter(Action onReady)
    {
        var store = CharacterStore.Instance;
        if (store != null && store.HasAny) { onReady?.Invoke(); return; }

        // Run the continuation only if they actually went through with it.
        // Backing out of the create screen closes the UI and launches nothing.
        _onClosed = () =>
        {
            var s = CharacterStore.Instance;
            if (s != null && s.HasAny) onReady?.Invoke();
        };
        BuildNoCharacterPopup();
    }

    public void OpenList(Action onClosed)
    {
        _onClosed = onClosed;
        BuildListScreen();
    }

    public void OpenPicker(Action onClosed)
    {
        _onClosed = onClosed;
        BuildPickerScreen();
    }

    /// Straight to the create screen, and `onClosed` fires whichever way it
    /// ends — saved OR cancelled.
    ///
    /// This is why it is not RequireCharacter: that one runs its callback only
    /// on success, which is right for "…and then start the game" but wrong for
    /// a caller whose callback also puts the menu back together. Cancelling
    /// there left the menu buttons hidden with no way to bring them back.
    public void OpenCreate(Action onClosed)
    {
        _onClosed = onClosed;
        BuildCreateScreen(null, onSaved: CloseAll, onCancel: CloseAll);
    }

    // ── screen: no characters yet ────────────────────────────────────────

    void BuildNoCharacterPopup()
    {
        var card = BeginModal(760f, 420f);
        AddHeader(card, "WHO ARE YOU?", "You need a character before you can play");

        var body = AddBody(card, 118f, 96f);

        // Colour plate on the left — deliberately NOT the 3D rig. This popup can
        // appear before the player has ever seen the create screen, and building
        // the render rig here would spend the cost twice.
        var plate = NewUI("Plate", body);
        plate.anchorMin = new Vector2(0f, 0.5f);
        plate.anchorMax = new Vector2(0f, 0.5f);
        plate.pivot     = new Vector2(0f, 0.5f);
        plate.anchoredPosition = new Vector2(0f, 0f);
        plate.sizeDelta = new Vector2(120f, 120f);
        var plateImg = plate.gameObject.AddComponent<Image>();
        plateImg.color = SuitPalette.ColorAt(0);

        var text = AddText(body, "Body",
            "A character carries your <b>name and suit colour</b> between every world you play — " +
            "and into your friends' sessions.\n\n" +
            "<color=#8F89AD>Later it will carry your level, money and hotbar too.</color>",
            22f, TextAlignmentOptions.TopLeft, LabelWhite);
        text.rectTransform.anchorMin = new Vector2(0f, 0f);
        text.rectTransform.anchorMax = new Vector2(1f, 1f);
        text.rectTransform.offsetMin = new Vector2(148f, 0f);
        text.rectTransform.offsetMax = new Vector2(0f, 0f);
        text.lineSpacing = 8f;

        var footer = AddFooter(card);
        // Saving closes the whole UI, which fires _onClosed and — since a
        // character now exists — runs the continuation the player was after.
        // Cancelling also closes it, but the HasAny check means nothing launches.
        AddButton(footer, "CREATE A CHARACTER", 340f, true,
            () => BuildCreateScreen(null, onSaved: CloseAll, onCancel: CloseAll));
    }

    // ── screen: the CHARACTERS list ──────────────────────────────────────

    void BuildListScreen()
    {
        var store = CharacterStore.Instance;
        var card = BeginModal(940f, 720f);
        AddHeader(card, "CHARACTERS", "These travel with you between worlds");

        var body = AddBody(card, 118f, 96f);

        if (store == null || !store.HasAny)
        {
            var empty = AddText(body, "Empty",
                "No characters yet.\nCreate one to get started.",
                24f, TextAlignmentOptions.Center, SubtleText);
            Stretch(empty.rectTransform, 0f, 0f, 0f, 0f);
        }
        else
        {
            var content = MakeScrollList(body, out _);
            foreach (var c in store.All)
                AddCharacterRow(content, c, showActions: true, onClick: id =>
                {
                    store.Select(id);
                    BuildListScreen();   // redraw so the selection marker moves
                });
        }

        var footer = AddFooter(card);
        AddButton(footer, "BACK", 200f, false, CloseAll);
        AddButton(footer, "+ NEW CHARACTER", 300f, true,
            () => BuildCreateScreen(null, onSaved: BuildListScreen, onCancel: BuildListScreen));
    }

    // ── screen: quick picker (from the chip) ─────────────────────────────

    void BuildPickerScreen()
    {
        var store = CharacterStore.Instance;
        var card = BeginModal(840f, 660f);
        AddHeader(card, "PLAY AS", "This stays your character until you change it");

        var body = AddBody(card, 118f, 96f);
        var content = MakeScrollList(body, out _);

        if (store != null)
        {
            foreach (var c in store.All)
                AddCharacterRow(content, c, showActions: false, onClick: id =>
                {
                    store.Select(id);
                    CloseAll();   // picking IS the confirm — no second click
                });
        }

        var footer = AddFooter(card);
        AddButton(footer, "BACK", 200f, false, CloseAll);
        // A character made here is obviously the one you want, and Create
        // already selects it — so close straight out rather than back to a list.
        AddButton(footer, "+ NEW", 200f, true,
            () => BuildCreateScreen(null, onSaved: CloseAll, onCancel: BuildPickerScreen));
    }

    /// One row: colour chip, name, swatch name, and (in the list) EDIT/DELETE.
    void AddCharacterRow(RectTransform parent, CharacterProfile c, bool showActions, Action<string> onClick)
    {
        bool isActive = CharacterStore.ActiveProfile != null && CharacterStore.ActiveProfile.id == c.id;

        // 84, not 76: the name line needs ~33px of its own and the sub-line ~20px
        // once padding is accounted for. See the Ellipsis note below.
        var row = NewUI("Row_" + c.id, parent);
        row.sizeDelta = new Vector2(0f, 84f);
        var le = row.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 84f;
        le.flexibleHeight = 0f;

        var bg = row.gameObject.AddComponent<Image>();
        bg.color = isActive ? RowBgSel : RowBg;

        var btn = row.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(() => onClick(c.id));
        UiSfxPlayer.Attach(btn);

        // Selected marker — a cyan bar down the left edge.
        if (isActive)
        {
            var mark = NewUI("Marker", row);
            mark.anchorMin = new Vector2(0f, 0f);
            mark.anchorMax = new Vector2(0f, 1f);
            mark.pivot     = new Vector2(0f, 0.5f);
            mark.anchoredPosition = Vector2.zero;
            mark.sizeDelta = new Vector2(4f, 0f);
            var m = mark.gameObject.AddComponent<Image>();
            m.color = AccentCool;
            m.raycastTarget = false;
        }

        // Suit colour chip.
        var chip = NewUI("Chip", row);
        chip.anchorMin = new Vector2(0f, 0.5f);
        chip.anchorMax = new Vector2(0f, 0.5f);
        chip.pivot     = new Vector2(0f, 0.5f);
        chip.anchoredPosition = new Vector2(20f, 0f);
        chip.sizeDelta = new Vector2(44f, 44f);
        var chipImg = chip.gameObject.AddComponent<Image>();
        chipImg.color = SuitPalette.ColorAt(c.swatchIndex);
        chipImg.raycastTarget = false;

        float rightInset = showActions ? -260f : -20f;

        // Name.
        //
        // ⚠️ NOT TextOverflowModes.Ellipsis. Ellipsis renders NOTHING AT ALL when
        // the rect is too short for one line — it does not clip, it blanks. The
        // name rect used to be 30px for a 28pt font (which needs ~33px), so every
        // character in this list showed its colour chip and no name. The rect is
        // now generous, and overflow is plain Overflow so a future tweak that
        // tightens it degrades to a clipped name instead of an invisible one.
        // A name is capped at 16 characters in a ~800px row, so it cannot
        // realistically overflow anyway.
        var name = AddText(row, "Name", c.name, 28f, TextAlignmentOptions.Left, LabelWhite);
        name.fontStyle = FontStyles.Bold;
        name.rectTransform.anchorMin = new Vector2(0f, 0.42f);
        name.rectTransform.anchorMax = new Vector2(1f, 1f);
        name.rectTransform.offsetMin = new Vector2(80f, 0f);
        name.rectTransform.offsetMax = new Vector2(rightInset, -6f);
        name.overflowMode = TextOverflowModes.Overflow;
        name.enableWordWrapping = false;

        // Sub-line.
        var sub = AddText(row, "Sub", SuitPalette.NameAt(c.swatchIndex) + " suit"
            + (isActive ? "   ·   <color=#5BD8FF>PLAYING AS</color>" : ""),
            17f, TextAlignmentOptions.Left, SubtleText);
        sub.rectTransform.anchorMin = new Vector2(0f, 0f);
        sub.rectTransform.anchorMax = new Vector2(1f, 0.42f);
        sub.rectTransform.offsetMin = new Vector2(80f, 8f);
        sub.rectTransform.offsetMax = new Vector2(rightInset, 0f);
        sub.overflowMode = TextOverflowModes.Overflow;
        sub.enableWordWrapping = false;

        if (!showActions) return;

        AddSmallButton(row, "EDIT", -140f, false,
            () => BuildCreateScreen(c.id, onSaved: BuildListScreen, onCancel: BuildListScreen));
        AddSmallButton(row, "DELETE", -20f, true,
            () => BuildDeleteConfirm(c));
    }

    // ── screen: delete confirm ───────────────────────────────────────────

    void BuildDeleteConfirm(CharacterProfile c)
    {
        var card = BeginModal(680f, 380f);
        AddHeader(card, "DELETE " + c.name.ToUpperInvariant() + "?", "This cannot be undone", DangerLine);

        var body = AddBody(card, 118f, 96f);
        var text = AddText(body, "Body",
            "The character is gone for good.\n\n" +
            "<color=#8F89AD>Your worlds are not touched — saves live separately.</color>",
            22f, TextAlignmentOptions.TopLeft, LabelWhite);
        Stretch(text.rectTransform, 0f, 0f, 0f, 0f);
        text.lineSpacing = 8f;

        var footer = AddFooter(card);
        AddButton(footer, "KEEP", 200f, false, BuildListScreen);
        var del = AddButton(footer, "DELETE", 220f, false, () =>
        {
            CharacterStore.Instance?.Delete(c.id);
            var store = CharacterStore.Instance;
            if (store != null && !store.HasAny)
            {
                // Deleting the last one drops you back to the no-characters
                // popup rather than an empty list you cannot leave usefully.
                BuildNoCharacterPopup();
                return;
            }
            BuildListScreen();
        });
        TintButton(del, DangerText, DangerLine);
    }

    // ── screen: create / edit ────────────────────────────────────────────

    /// `onSaved` and `onCancel` are navigation, not continuations — each just
    /// says which screen to show next. Neither fires _onClosed; only CloseAll
    /// does, and callers pass CloseAll explicitly when leaving is the intent.
    void BuildCreateScreen(string editId, Action onSaved, Action onCancel)
    {
        var store = CharacterStore.Instance;
        _editingId = editId;

        var existing = editId != null && store != null ? store.Find(editId) : null;
        _draftName   = existing != null ? existing.name : "";
        _draftSwatch = existing != null ? SuitPalette.Clamp(existing.swatchIndex) : 0;

        var card = BeginModal(1120f, 700f);
        AddHeader(card,
            existing != null ? "EDIT CHARACTER" : "NEW CHARACTER",
            "Drag the astronaut to spin it");

        var body = AddBody(card, 118f, 110f);

        BuildPreviewPane(body);
        BuildFormPane(body);

        var footer = AddFooter(card);
        AddButton(footer, "CANCEL", 200f, false, () => onCancel?.Invoke());
        _confirmButton = AddButton(footer, existing != null ? "SAVE" : "CREATE", 260f, true, () =>
        {
            string clean = CharacterProfile.Sanitize(_draftName);
            if (string.IsNullOrEmpty(clean)) { ShowNameError(true); return; }

            if (_editingId != null) store?.Edit(_editingId, clean, _draftSwatch);
            else                    store?.Create(clean, _draftSwatch);

            onSaved?.Invoke();
        });
        _confirmLabel = _confirmButton.GetComponentInChildren<TextMeshProUGUI>();

        RefreshCreateState();
    }

    void BuildPreviewPane(RectTransform body)
    {
        var pane = NewUI("PreviewPane", body);
        pane.anchorMin = new Vector2(0f, 0f);
        pane.anchorMax = new Vector2(0f, 1f);
        pane.pivot     = new Vector2(0f, 0.5f);
        pane.anchoredPosition = Vector2.zero;
        pane.sizeDelta = new Vector2(330f, 0f);

        var paneBg = pane.gameObject.AddComponent<Image>();
        paneBg.color = new Color32(0x12, 0x0A, 0x2E, 0xC0);

        // Build (or reuse) the 3D rig.
        if (_preview == null && _astronautPrefab != null)
            _preview = AstronautPreview.Build(_astronautPrefab, 512, 720);

        if (_preview != null && _preview.Texture != null)
        {
            var viewRT = NewUI("PreviewView", pane);
            viewRT.anchorMin = new Vector2(0f, 0f);
            viewRT.anchorMax = new Vector2(1f, 1f);
            viewRT.offsetMin = new Vector2(10f, 52f);
            viewRT.offsetMax = new Vector2(-10f, -10f);
            var raw = viewRT.gameObject.AddComponent<RawImage>();
            raw.texture = _preview.Texture;
            raw.raycastTarget = true;                    // required for the drag
            var drag = viewRT.gameObject.AddComponent<AstronautPreviewDrag>();
            drag.Target = _preview;

            _preview.SetSwatch(_draftSwatch);

            var hint = AddText(pane, "Hint", "◄  DRAG TO SPIN  ►", 15f,
                TextAlignmentOptions.Center, new Color(AccentCool.r, AccentCool.g, AccentCool.b, 0.6f));
            hint.rectTransform.anchorMin = new Vector2(0f, 0f);
            hint.rectTransform.anchorMax = new Vector2(1f, 0f);
            hint.rectTransform.pivot     = new Vector2(0.5f, 0f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, 30f);
            hint.rectTransform.sizeDelta = new Vector2(0f, 24f);
            hint.characterSpacing = 6f;
        }
        else
        {
            // No prefab assigned — a flat plate still shows the colour choice,
            // so the screen remains fully usable.
            var plate = NewUI("Plate", pane);
            plate.anchorMin = new Vector2(0.5f, 0.5f);
            plate.anchorMax = new Vector2(0.5f, 0.5f);
            plate.pivot     = new Vector2(0.5f, 0.5f);
            plate.anchoredPosition = new Vector2(0f, 26f);
            plate.sizeDelta = new Vector2(190f, 190f);
            var img = plate.gameObject.AddComponent<Image>();
            img.color = SuitPalette.ColorAt(_draftSwatch);
            _flatPlate = img;

            var note = AddText(pane, "Note",
                "3D preview unavailable\n<size=13>(assign Astronaut on MainMenuController)</size>",
                14f, TextAlignmentOptions.Center, SubtleText);
            note.rectTransform.anchorMin = new Vector2(0f, 0f);
            note.rectTransform.anchorMax = new Vector2(1f, 0f);
            note.rectTransform.pivot = new Vector2(0.5f, 0f);
            note.rectTransform.anchoredPosition = new Vector2(0f, 24f);
            note.rectTransform.sizeDelta = new Vector2(0f, 48f);
        }

        // Swatch name under the model.
        _swatchNameLabel = AddText(pane, "SwatchName", SuitPalette.NameAt(_draftSwatch),
            18f, TextAlignmentOptions.Center, LabelWhite);
        _swatchNameLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
        _swatchNameLabel.rectTransform.anchorMax = new Vector2(1f, 0f);
        _swatchNameLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
        _swatchNameLabel.rectTransform.anchoredPosition = new Vector2(0f, 6f);
        _swatchNameLabel.rectTransform.sizeDelta = new Vector2(0f, 24f);
        _swatchNameLabel.characterSpacing = 8f;
    }

    Image _flatPlate;   // only used when there is no 3D rig

    void BuildFormPane(RectTransform body)
    {
        var pane = NewUI("FormPane", body);
        pane.anchorMin = new Vector2(0f, 0f);
        pane.anchorMax = new Vector2(1f, 1f);
        pane.offsetMin = new Vector2(370f, 0f);
        pane.offsetMax = new Vector2(0f, 0f);

        // ── name ─────────────────────────────────────────────────────────
        var nameLabel = AddText(pane, "NameLabel", "NAME", 17f, TextAlignmentOptions.Left, AccentCool);
        nameLabel.characterSpacing = 12f;
        nameLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        nameLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
        nameLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
        nameLabel.rectTransform.anchoredPosition = Vector2.zero;
        nameLabel.rectTransform.sizeDelta = new Vector2(0f, 24f);

        // TMP_InputField is a Selectable, not a Graphic — no `.rectTransform`.
        var field   = MakeInputField(pane, _draftName, "Zib");
        var fieldRT = (RectTransform)field.transform;
        fieldRT.anchorMin = new Vector2(0f, 1f);
        fieldRT.anchorMax = new Vector2(1f, 1f);
        fieldRT.pivot = new Vector2(0.5f, 1f);
        fieldRT.anchoredPosition = new Vector2(0f, -30f);
        fieldRT.sizeDelta = new Vector2(0f, 64f);

        _errorLabel = AddText(pane, "Error", "", 15f, TextAlignmentOptions.Left, DangerText);
        _errorLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        _errorLabel.rectTransform.anchorMax = new Vector2(0.6f, 1f);
        _errorLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
        _errorLabel.rectTransform.anchoredPosition = new Vector2(0f, -98f);
        _errorLabel.rectTransform.sizeDelta = new Vector2(0f, 22f);

        _counterLabel = AddText(pane, "Counter", "", 15f, TextAlignmentOptions.Right, SubtleText);
        _counterLabel.rectTransform.anchorMin = new Vector2(0.6f, 1f);
        _counterLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
        _counterLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
        _counterLabel.rectTransform.anchoredPosition = new Vector2(0f, -98f);
        _counterLabel.rectTransform.sizeDelta = new Vector2(0f, 22f);

        // ── swatches ─────────────────────────────────────────────────────
        var swatchLabel = AddText(pane, "SwatchLabel", "SUIT COLOUR", 17f, TextAlignmentOptions.Left, AccentCool);
        swatchLabel.characterSpacing = 12f;
        swatchLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        swatchLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
        swatchLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
        swatchLabel.rectTransform.anchoredPosition = new Vector2(0f, -140f);
        swatchLabel.rectTransform.sizeDelta = new Vector2(0f, 24f);

        var grid = NewUI("Swatches", pane);
        grid.anchorMin = new Vector2(0f, 1f);
        grid.anchorMax = new Vector2(1f, 1f);
        grid.pivot = new Vector2(0.5f, 1f);
        grid.anchoredPosition = new Vector2(0f, -172f);
        grid.sizeDelta = new Vector2(0f, 200f);

        var glg = grid.gameObject.AddComponent<GridLayoutGroup>();
        glg.cellSize = new Vector2(84f, 84f);
        glg.spacing  = new Vector2(14f, 16f);
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = 5;
        glg.childAlignment = TextAnchor.UpperLeft;

        _swatchFrames.Clear();
        for (int i = 0; i < SuitPalette.Count; i++)
        {
            int index = i;   // capture

            var cell = NewUI("Swatch" + i, grid);
            cell.sizeDelta = new Vector2(84f, 84f);

            // Outer frame doubles as the selection ring.
            var frame = cell.gameObject.AddComponent<Image>();
            frame.color = new Color(1f, 1f, 1f, 0.16f);
            _swatchFrames.Add(frame);

            var swatchRT = NewUI("Fill", cell);
            Stretch(swatchRT, 4f, 4f, -4f, -4f);
            var fill = swatchRT.gameObject.AddComponent<Image>();
            fill.color = SuitPalette.ColorAt(index);
            fill.raycastTarget = false;

            var btn = cell.gameObject.AddComponent<Button>();
            btn.targetGraphic = frame;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => SelectSwatch(index));
            UiSfxPlayer.Attach(btn);
        }
    }

    void SelectSwatch(int index)
    {
        _draftSwatch = SuitPalette.Clamp(index);
        if (_preview != null) _preview.SetSwatch(_draftSwatch);
        if (_flatPlate != null) _flatPlate.color = SuitPalette.ColorAt(_draftSwatch);
        RefreshCreateState();
    }

    void OnNameTyped(string value)
    {
        _draftName = value;
        if (!string.IsNullOrWhiteSpace(value)) ShowNameError(false);
        RefreshCreateState();
    }

    void ShowNameError(bool show)
    {
        if (_errorLabel != null) _errorLabel.text = show ? "Name required" : "";
    }

    /// Keeps the counter, the swatch ring, the swatch name and the confirm
    /// button's enabled state in agreement with the draft.
    void RefreshCreateState()
    {
        string clean = CharacterProfile.Sanitize(_draftName);

        if (_counterLabel != null)
            _counterLabel.text = $"{clean.Length}/{CharacterProfile.MaxNameLength}";

        if (_swatchNameLabel != null)
            _swatchNameLabel.text = SuitPalette.NameAt(_draftSwatch);

        for (int i = 0; i < _swatchFrames.Count; i++)
        {
            if (_swatchFrames[i] == null) continue;
            _swatchFrames[i].color = (i == _draftSwatch)
                ? Color.white
                : new Color(1f, 1f, 1f, 0.16f);
        }

        bool valid = !string.IsNullOrEmpty(clean);
        if (_confirmButton != null) _confirmButton.interactable = valid;
        if (_confirmLabel != null)
            _confirmLabel.color = valid ? LabelWhite : new Color(LabelWhite.r, LabelWhite.g, LabelWhite.b, 0.3f);
    }

    // ── modal plumbing ───────────────────────────────────────────────────

    /// Destroys any open modal and builds a fresh backdrop + card. Returns the
    /// CARD (not the backdrop) so callers only ever lay out inside it.
    ///
    /// Deliberately does NOT touch _onClosed — screens swap freely without
    /// disturbing what the entry point asked for.
    RectTransform BeginModal(float width, float height)
    {
        if (_modalRoot != null) Destroy(_modalRoot);
        ClearScreenRefs();

        var rootRT = NewUI("CharacterModal", transform);
        Stretch(rootRT, 0f, 0f, 0f, 0f);
        _modalRoot = rootRT.gameObject;

        // Full-screen blocker: stops clicks reaching the menu rows behind, the
        // same job MainMenuController does by deactivating mainMenuButtonsRoot.
        var block = rootRT.gameObject.AddComponent<Image>();
        block.color = Backdrop;
        block.raycastTarget = true;

        var cardRT = NewUI("Card", rootRT);
        cardRT.anchorMin = new Vector2(0.5f, 0.5f);
        cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        cardRT.pivot     = new Vector2(0.5f, 0.5f);
        cardRT.anchoredPosition = Vector2.zero;
        cardRT.sizeDelta = new Vector2(width, height);
        var cardBg = cardRT.gameObject.AddComponent<Image>();
        cardBg.color = CardBg;

        return cardRT;
    }

    /// The one and only exit. Tears the modal down and fires _onClosed exactly
    /// once — nulled before the call so a callback that re-opens the UI cannot
    /// re-enter this and fire it twice.
    void CloseAll()
    {
        var cb = _onClosed;
        _onClosed = null;

        if (_modalRoot != null) { Destroy(_modalRoot); _modalRoot = null; }
        ClearScreenRefs();

        cb?.Invoke();
    }

    /// Drops references into the modal that is about to be (or has been)
    /// destroyed, so nothing later writes to a dead component.
    void ClearScreenRefs()
    {
        _confirmButton   = null;
        _confirmLabel    = null;
        _flatPlate       = null;
        _counterLabel    = null;
        _errorLabel      = null;
        _swatchNameLabel = null;
        _swatchFrames.Clear();
    }

    void AddHeader(RectTransform card, string title, string subtitle) =>
        AddHeader(card, title, subtitle, AccentHot);

    void AddHeader(RectTransform card, string title, string subtitle, Color accent)
    {
        var t = AddText(card, "Title", title, 40f, TextAlignmentOptions.Left, LabelWhite);
        t.fontStyle = FontStyles.Bold;
        t.characterSpacing = 10f;
        t.enableVertexGradient = true;
        t.colorGradient = new VertexGradient(AccentCool, accent, AccentCool, accent);
        t.rectTransform.anchorMin = new Vector2(0f, 1f);
        t.rectTransform.anchorMax = new Vector2(1f, 1f);
        t.rectTransform.pivot = new Vector2(0.5f, 1f);
        t.rectTransform.anchoredPosition = new Vector2(0f, -26f);
        t.rectTransform.offsetMin = new Vector2(40f, t.rectTransform.offsetMin.y);
        t.rectTransform.offsetMax = new Vector2(-40f, t.rectTransform.offsetMax.y);
        t.rectTransform.sizeDelta = new Vector2(t.rectTransform.sizeDelta.x, 48f);

        var s = AddText(card, "Subtitle", subtitle, 18f, TextAlignmentOptions.Left, SubtleText);
        s.rectTransform.anchorMin = new Vector2(0f, 1f);
        s.rectTransform.anchorMax = new Vector2(1f, 1f);
        s.rectTransform.pivot = new Vector2(0.5f, 1f);
        s.rectTransform.anchoredPosition = new Vector2(0f, -74f);
        s.rectTransform.offsetMin = new Vector2(40f, s.rectTransform.offsetMin.y);
        s.rectTransform.offsetMax = new Vector2(-40f, s.rectTransform.offsetMax.y);
        s.rectTransform.sizeDelta = new Vector2(s.rectTransform.sizeDelta.x, 24f);

        var rule = NewUI("HeaderRule", card);
        rule.anchorMin = new Vector2(0f, 1f);
        rule.anchorMax = new Vector2(1f, 1f);
        rule.pivot = new Vector2(0.5f, 1f);
        rule.anchoredPosition = new Vector2(0f, -108f);
        rule.offsetMin = new Vector2(40f, rule.offsetMin.y);
        rule.offsetMax = new Vector2(-40f, rule.offsetMax.y);
        rule.sizeDelta = new Vector2(rule.sizeDelta.x, 2f);
        var ruleImg = rule.gameObject.AddComponent<Image>();
        ruleImg.color = new Color(accent.r, accent.g, accent.b, 0.35f);
        ruleImg.raycastTarget = false;
    }

    /// The scrollable/content region between header and footer.
    RectTransform AddBody(RectTransform card, float topInset, float bottomInset)
    {
        var body = NewUI("Body", card);
        body.anchorMin = new Vector2(0f, 0f);
        body.anchorMax = new Vector2(1f, 1f);
        body.offsetMin = new Vector2(40f, bottomInset);
        body.offsetMax = new Vector2(-40f, -topInset);
        return body;
    }

    RectTransform AddFooter(RectTransform card)
    {
        var footer = NewUI("Footer", card);
        footer.anchorMin = new Vector2(0f, 0f);
        footer.anchorMax = new Vector2(1f, 0f);
        footer.pivot = new Vector2(0.5f, 0f);
        footer.anchoredPosition = new Vector2(0f, 22f);
        footer.offsetMin = new Vector2(40f, footer.offsetMin.y);
        footer.offsetMax = new Vector2(-40f, footer.offsetMax.y);
        footer.sizeDelta = new Vector2(footer.sizeDelta.x, 60f);

        var hlg = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleRight;
        hlg.spacing = 14f;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        return footer;
    }

    // ── widgets ──────────────────────────────────────────────────────────

    Button AddButton(RectTransform parent, string label, float width, bool primary, Action onClick)
    {
        var rt = NewUI("Btn_" + label, parent);
        rt.sizeDelta = new Vector2(width, 60f);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = 60f;
        le.flexibleWidth = 0f;

        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = primary ? new Color32(0x6A, 0x34, 0xB8, 0xFF) : new Color32(0x10, 0x08, 0x2E, 0xE0);

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        var colors = btn.colors;
        colors.normalColor      = Color.white;
        colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
        colors.pressedColor     = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.disabledColor    = new Color(1f, 1f, 1f, 0.35f);
        btn.colors = colors;
        btn.onClick.AddListener(() => onClick());
        UiSfxPlayer.Attach(btn);

        var t = AddText(rt, "Label", label, 21f, TextAlignmentOptions.Center, LabelWhite);
        t.fontStyle = FontStyles.Bold;
        t.characterSpacing = 8f;
        Stretch(t.rectTransform, 0f, 0f, 0f, 0f);

        // A thin accent underline on the primary action, echoing the menu rows.
        if (primary)
        {
            var line = NewUI("Line", rt);
            line.anchorMin = new Vector2(0f, 0f);
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0.5f, 0f);
            line.anchoredPosition = Vector2.zero;
            line.sizeDelta = new Vector2(0f, 3f);
            var img = line.gameObject.AddComponent<Image>();
            img.color = AccentCool;
            img.raycastTarget = false;
        }
        return btn;
    }

    /// Right-anchored small button used inside a character row.
    void AddSmallButton(RectTransform row, string label, float xOffset, bool danger, Action onClick)
    {
        var rt = NewUI("Row_" + label, row);
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot     = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(xOffset, 0f);
        rt.sizeDelta = new Vector2(110f, 40f);

        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color32(0x00, 0x00, 0x00, 0x8C);

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(() => onClick());
        UiSfxPlayer.Attach(btn);

        var t = AddText(rt, "Label", label, 15f, TextAlignmentOptions.Center,
            danger ? DangerText : LabelWhite);
        t.characterSpacing = 6f;
        Stretch(t.rectTransform, 0f, 0f, 0f, 0f);
    }

    void TintButton(Button btn, Color textColor, Color bgColor)
    {
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = bgColor;
        var t = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.color = textColor;
    }

    /// Vertical scroll list. Returns the content RectTransform to parent rows to.
    RectTransform MakeScrollList(RectTransform parent, out ScrollRect scroll)
    {
        var viewport = NewUI("Viewport", parent);
        Stretch(viewport, 0f, 0f, 0f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var vpImage = viewport.gameObject.AddComponent<Image>();
        vpImage.color = new Color(0f, 0f, 0f, 0f);   // raycast surface for scrolling

        var content = NewUI("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot     = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 0f);

        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 10f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll = parent.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        return content;
    }

    TMP_InputField MakeInputField(RectTransform parent, string initial, string placeholderText)
    {
        var rt = NewUI("NameField", parent);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = FieldBg;

        var input = rt.gameObject.AddComponent<TMP_InputField>();

        // TMP_InputField needs a masked viewport plus separate text and
        // placeholder components; building it by hand means wiring all three.
        var area = NewUI("Text Area", rt);
        Stretch(area, 16f, 6f, -16f, -6f);
        area.gameObject.AddComponent<RectMask2D>();

        var phRT = NewUI("Placeholder", area);
        Stretch(phRT, 0f, 0f, 0f, 0f);
        var ph = phRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(ph);
        ph.text = placeholderText;
        ph.fontSize = 28f;
        ph.fontStyle = FontStyles.Italic;
        ph.color = new Color(SubtleText.r, SubtleText.g, SubtleText.b, 0.6f);
        ph.alignment = TextAlignmentOptions.Left;

        var txtRT = NewUI("Text", area);
        Stretch(txtRT, 0f, 0f, 0f, 0f);
        var txt = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(txt);
        txt.fontSize = 28f;
        txt.color = LabelWhite;
        txt.alignment = TextAlignmentOptions.Left;
        txt.richText = false;   // a name is literal text, not markup

        input.textViewport   = area;
        input.textComponent  = txt;
        input.placeholder    = ph;
        input.characterLimit = CharacterProfile.MaxNameLength;
        input.lineType       = TMP_InputField.LineType.SingleLine;
        input.caretColor     = AccentCool;
        input.customCaretColor = true;
        input.selectionColor = new Color(AccentCool.r, AccentCool.g, AccentCool.b, 0.3f);
        input.text           = initial ?? "";
        input.onValueChanged.AddListener(OnNameTyped);

        // Typing is the first thing you want to do on this screen.
        input.ActivateInputField();
        return input;
    }

    TextMeshProUGUI AddText(RectTransform parent, string name, string content,
                            float size, TextAlignmentOptions align, Color color)
    {
        var rt = NewUI(name, parent);
        Stretch(rt, 0f, 0f, 0f, 0f);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(t);
        t.text = content;
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        return t;
    }

    // ── helpers (mirrors MainMenuController's private set) ───────────────

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(right, top);
    }

    static void ApplyDefaultFont(TextMeshProUGUI t)
    {
        // The one sanctioned Resources.Load in user code (CLAUDE.md).
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null) t.font = font;
    }
}
