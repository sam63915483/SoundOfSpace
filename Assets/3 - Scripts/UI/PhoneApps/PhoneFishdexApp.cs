using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-phone Fishingdex app: browse every caught fish on the tablet screen.
/// Left: rows of catches ("SALMON — 3 LB"). Right: live 3D render of the
/// selected fish (FishingdexManager.RenderFish), rarity/mass/value/caught
/// spec lines and the rarity blurb. Browse-only — selling and cooking stay
/// with the market/bonfire flows that already use the fullscreen dex in
/// their own context modes.
/// </summary>
public class PhoneFishdexApp : PhoneAppBase
{
    protected override string Title => "FISHINGDEX";

    RawImage _preview;
    TMP_Text _name, _classVal, _massVal, _valueVal, _caughtVal, _desc;
    RenderTexture _detailRT;
    Button _selectedRow;

    protected override void BuildBody()
    {
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

        _classVal  = AddSpecLine(DetailPane, "CLASS",  -148f);
        _massVal   = AddSpecLine(DetailPane, "MASS",   -166f);
        _valueVal  = AddSpecLine(DetailPane, "VALUE",  -184f);
        _caughtVal = AddSpecLine(DetailPane, "CAUGHT", -202f);

        _desc = MakeText(DetailPane, "", 8.5f, LabelDim, TextAlignmentOptions.TopLeft);
        var dRT = _desc.rectTransform;
        dRT.anchorMin = new Vector2(0f, 0f); dRT.anchorMax = new Vector2(1f, 1f);
        dRT.offsetMin = new Vector2(0f, 4f);
        dRT.offsetMax = new Vector2(0f, -222f);
        _desc.enableWordWrapping = true;
        _desc.overflowMode = TextOverflowModes.Ellipsis;
    }

    protected override void OnOpened()
    {
        RebuildList();
    }

    protected override void OnClosed()
    {
        ReleaseDetailRT();
    }

    void RebuildList()
    {
        ClearRows();
        _selectedRow = null;

        var inv = FishInventory.Instance;
        int count = inv != null ? inv.AllFish.Count : 0;
        if (TopRightText != null) TopRightText.text = count + " ENTRIES";

        if (inv == null || count == 0)
        {
            Select(null);
            return;
        }

        FishEntry first = null;
        foreach (var fish in inv.AllFish)
        {
            var f = fish;   // capture
            Button row = null;
            row = AddRow(f.fishType.ToUpper(), f.weightLbs + " LB", LabelDim, () => Select(f, row), out _, out _);
            if (first == null) first = f;
        }
        Select(first, ListContent.childCount > 0 ? ListContent.GetChild(0).GetComponent<Button>() : null);
    }

    void Select(FishEntry entry, Button row = null)
    {
        SetRowSelected(_selectedRow, false);
        _selectedRow = row;
        SetRowSelected(row, true);

        var dex = FishingdexManager.Instance;
        if (entry == null || dex == null)
        {
            ReleaseDetailRT();
            _preview.color = new Color(1f, 1f, 1f, 0f);
            _name.text = "—";
            _classVal.text = _massVal.text = _valueVal.text = _caughtVal.text = "—";
            _desc.text = "NO ENTRIES YET. CATCH SOMETHING.";
            return;
        }

        ReleaseDetailRT();
        _detailRT = dex.RenderFish(entry, 192, 192);
        _preview.texture = _detailRT;
        _preview.color = _detailRT != null ? Color.white : new Color(1f, 1f, 1f, 0f);

        _name.text = entry.fishType.ToUpper();
        _classVal.text = dex.GetRarityLabel(entry.fishType);
        _massVal.text = entry.weightLbs + " LB";
        _valueVal.text = "$" + entry.GetValue();
        _caughtVal.text = "×" + CountByType(entry.fishType);
        _desc.text = dex.GetRarityDescription(entry.fishType);
    }

    static int CountByType(string fishType)
    {
        if (FishInventory.Instance == null) return 0;
        int c = 0;
        foreach (var f in FishInventory.Instance.AllFish)
            if (f.fishType == fishType) c++;
        return c;
    }

    void ReleaseDetailRT()
    {
        if (_detailRT == null) return;
        _detailRT.Release();
        Destroy(_detailRT);
        _detailRT = null;
        if (_preview != null) _preview.texture = null;
    }
}
