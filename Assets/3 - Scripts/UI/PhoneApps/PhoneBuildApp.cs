using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-phone Build app: the desktop build menu's catalogue rebuilt for the
/// tablet screen. Left: category filter + buildable rows (name + wood
/// cost, red when unaffordable). Right: prefab preview (shared RT cache
/// from BuildMenuUI), spec lines, and PLACE — which closes the phone and
/// starts the normal GhostPlacement flow. All data/placement logic stays
/// in BuildMenuUI; this is only a compact front-end.
/// </summary>
public class PhoneBuildApp : PhoneAppBase
{
    protected override string Title => "BUILD";

    RawImage _preview;
    TMP_Text _name, _classVal, _costVal, _sizeVal, _desc;
    Button _placeBtn;
    Image _placeBtnBg;
    TMP_Text _placeLabel;
    Button _catBtn;
    TMP_Text _catLabel;

    BuildableEntry _selected;
    Button _selectedRow;
    int _catIndex;   // 0 = ALL, then BuildableCategory values
    int _lastWoodSeen = int.MinValue;
    readonly List<(Button row, TMP_Text cost, BuildableEntry entry)> _rows = new List<(Button, TMP_Text, BuildableEntry)>();

    static readonly BuildableCategory[] Cats =
        (BuildableCategory[])System.Enum.GetValues(typeof(BuildableCategory));

    protected override void BuildBody()
    {
        // Category cycle button sits above the detail pane's preview,
        // top-right of the screen (the list keeps its full height).
        _catBtn = MakeButton(Root, "ALL", new Vector2(96f, 18f), CycleCategory);
        var catRT = (RectTransform)_catBtn.transform;
        catRT.anchorMin = catRT.anchorMax = new Vector2(0f, 1f);
        catRT.pivot = new Vector2(0f, 1f);
        catRT.anchoredPosition = new Vector2(8f, -32f);
        _catLabel = _catBtn.GetComponentInChildren<TMP_Text>();

        // With the category button occupying the top strip, drop the list a
        // little to clear it.
        var listFrame = (RectTransform)ListContent.parent.parent;
        listFrame.offsetMax = new Vector2(listFrame.offsetMax.x, -54f);

        // Detail pane.
        _preview = NewUI("Preview", DetailPane).gameObject.AddComponent<RawImage>();
        var pRT = _preview.rectTransform;
        pRT.anchorMin = pRT.anchorMax = new Vector2(0.5f, 1f);
        pRT.pivot = new Vector2(0.5f, 1f);
        pRT.anchoredPosition = new Vector2(0f, -2f);
        pRT.sizeDelta = new Vector2(120f, 120f);
        _preview.raycastTarget = false;
        _preview.color = new Color(1f, 1f, 1f, 0f);

        _name = MakeText(DetailPane, "—", 12f, LabelWhite, TextAlignmentOptions.Center);
        _name.fontStyle = FontStyles.Bold;
        var nRT = _name.rectTransform;
        nRT.anchorMin = new Vector2(0f, 1f); nRT.anchorMax = new Vector2(1f, 1f);
        nRT.pivot = new Vector2(0.5f, 1f);
        nRT.sizeDelta = new Vector2(0f, 18f);
        nRT.anchoredPosition = new Vector2(0f, -126f);

        _classVal = AddSpecLine(DetailPane, "CLASS", -148f);
        _costVal  = AddSpecLine(DetailPane, "COST",  -166f);
        _sizeVal  = AddSpecLine(DetailPane, "SIZE",  -184f);

        _desc = MakeText(DetailPane, "", 8.5f, LabelDim, TextAlignmentOptions.TopLeft);
        var dRT = _desc.rectTransform;
        dRT.anchorMin = new Vector2(0f, 0f); dRT.anchorMax = new Vector2(1f, 1f);
        dRT.offsetMin = new Vector2(0f, 36f);
        dRT.offsetMax = new Vector2(0f, -204f);
        _desc.enableWordWrapping = true;
        _desc.overflowMode = TextOverflowModes.Ellipsis;

        _placeBtn = MakeButton(DetailPane, "PLACE", new Vector2(150f, 26f), OnPlace);
        var plRT = (RectTransform)_placeBtn.transform;
        plRT.anchorMin = plRT.anchorMax = new Vector2(0.5f, 0f);
        plRT.pivot = new Vector2(0.5f, 0f);
        plRT.anchoredPosition = new Vector2(0f, 2f);
        _placeBtnBg = _placeBtn.GetComponent<Image>();
        _placeLabel = _placeBtn.GetComponentInChildren<TMP_Text>();
    }

    protected override void OnOpened()
    {
        RebuildList();
    }

    void Update()
    {
        if (Root == null || !Root.gameObject.activeInHierarchy) return;
        int wood = WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;
        if (wood != _lastWoodSeen)
        {
            _lastWoodSeen = wood;
            if (TopRightText != null) TopRightText.text = "WOOD " + wood;
            RefreshAffordability();
        }
    }

    void CycleCategory()
    {
        _catIndex = (_catIndex + 1) % (Cats.Length + 1);
        if (_catLabel != null)
            _catLabel.text = _catIndex == 0 ? "ALL" : Cats[_catIndex - 1].ToString().ToUpper();
        RebuildList();
    }

    void RebuildList()
    {
        ClearRows();
        _rows.Clear();
        _selectedRow = null;

        var menu = BuildMenuUI.Instance;
        if (menu == null || menu.Buildables == null)
        {
            Select(null);
            return;
        }

        BuildableEntry first = null;
        foreach (var entry in menu.Buildables)
        {
            if (entry == null) continue;
            if (_catIndex != 0 && entry.category != Cats[_catIndex - 1]) continue;
            var e = entry;   // capture
            string cost = e.woodCost <= 0 ? "FREE" : e.woodCost + " W";
            Button row = null;
            row = AddRow(e.displayName, cost, LabelDim, () => Select(e, row), out _, out var costText);
            _rows.Add((row, costText, e));
            if (first == null) first = e;
        }
        RefreshAffordability();
        Select(first, _rows.Count > 0 ? _rows[0].row : null);
    }

    void RefreshAffordability()
    {
        int wood = WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;
        foreach (var (row, cost, entry) in _rows)
        {
            if (cost == null) continue;
            bool affordable = entry.woodCost <= 0 || wood >= entry.woodCost;
            cost.color = affordable ? LabelDim : WarnRed;
        }
        UpdatePlaceButton();
    }

    void Select(BuildableEntry entry, Button row = null)
    {
        SetRowSelected(_selectedRow, false);
        _selectedRow = row;
        SetRowSelected(row, true);
        _selected = entry;

        var menu = BuildMenuUI.Instance;
        if (entry == null || menu == null)
        {
            _preview.color = new Color(1f, 1f, 1f, 0f);
            _name.text = "—";
            _classVal.text = _costVal.text = _sizeVal.text = "—";
            _desc.text = menu == null ? "BUILD SYSTEM OFFLINE." : "NOTHING IN THIS CATEGORY.";
            UpdatePlaceButton();
            return;
        }

        var rt = menu.GetPreviewFor(entry);
        _preview.texture = rt;
        _preview.color = rt != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        _name.text = entry.displayName.ToUpper();
        _classVal.text = entry.category.ToString().ToUpper();
        _costVal.text = entry.woodCost <= 0 ? "FREE" : entry.woodCost + " WOOD";
        Vector3 size = menu.GetSizeFor(entry);
        _sizeVal.text = size == Vector3.zero ? "—" : $"{size.x:0.#}×{size.y:0.#}×{size.z:0.#} M";
        _desc.text = entry.description;
        UpdatePlaceButton();
    }

    void UpdatePlaceButton()
    {
        if (_placeBtn == null) return;
        int wood = WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;
        bool can = _selected != null && _selected.prefab != null
                   && (_selected.woodCost <= 0 || wood >= _selected.woodCost);
        _placeBtn.interactable = can;
        if (_placeLabel != null)
        {
            _placeLabel.text = can || _selected == null ? "PLACE" : "NEED WOOD";
            _placeLabel.color = can ? AccentCyan : WarnRed;
        }
        if (_placeBtnBg != null)
            _placeBtnBg.color = new Color(AccentCyan.r, AccentCyan.g, AccentCyan.b, can ? 0.14f : 0.05f);
    }

    void OnPlace()
    {
        if (_selected == null || BuildMenuUI.Instance == null) return;
        // Order matters: start the placement FIRST, then close the phone —
        // the ghost fades in as the tablet drops out of view.
        BuildMenuUI.Instance.StartPlacementFromPhone(_selected);
        if (PlayerPhoneUI.Instance != null) PlayerPhoneUI.Instance.Close();
    }
}
