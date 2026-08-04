using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared sell panel for the mushroom economy — the panel every NPC opens when
/// the player picks "Sell mushrooms". Modelled on SpaceDustSellUI, with two
/// deliberate differences:
///
///   • NO accept-chance gamble. This NPC's price is this NPC's price (see
///     NPCMushroomPrice) and they always buy. The decision the player makes is
///     which buyer to walk to, not whether the roll lands.
///   • Stock comes from the HOTBAR, not a side inventory, and it's SPECIES-PURE:
///     the panel sells one species at a time (the leftmost stack), because that
///     is how the stacks themselves work.
///
/// Auto-singleton with no MainMenu skip — see SpaceDustSellUI.AutoCreate for why
/// (the panel is inactive at build so the canvas is invisible there anyway).
/// </summary>
public class MushroomSellUI : MonoBehaviour
{
    public static MushroomSellUI Instance { get; private set; }

    static readonly Color C_Bg      = new Color32(10, 24, 40, 240);
    static readonly Color C_Border  = new Color32(120, 200, 255, 220);
    static readonly Color C_Header  = new Color32(226, 120, 126, 255);   // cap red
    static readonly Color C_Label   = new Color32(234, 246, 255, 255);
    static readonly Color C_Value   = new Color32(255, 215, 50, 255);
    static readonly Color C_BtnSell = new Color32(60, 145, 70, 255);
    static readonly Color C_BtnBack = new Color32(140, 60, 60, 255);
    static readonly Color C_Ok      = new Color32(110, 220, 130, 255);
    static readonly Color C_Err     = new Color32(255, 110, 110, 255);

    Canvas _canvas;
    RectTransform _panelRT;
    TextMeshProUGUI _header, _priceText, _speciesText, _totalText, _resultText;
    Slider _slider;
    TMP_InputField _qtyInput;
    Button _sellBtn, _cancelBtn;

    string _npcName;
    int _pricePerMushroom;
    string _species;             // the species currently being sold
    Action _onClose;
    Action<int> _onSold;         // credits-worth of mushrooms actually sold
    Coroutine _resultRoutine;
    bool _suppressInputCallback;
    bool _open;
    GameObject _dim;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("MushroomSellUI");
        DontDestroyOnLoad(go);
        go.AddComponent<MushroomSellUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    public bool IsOpen => _open;

    /// <param name="onSold">Called with the NUMBER OF MUSHROOMS sold each time a
    /// sale goes through — Tev's onboarding counts his three through this.</param>
    public void Open(string npcName, int pricePerMushroom, Action onClose, Action<int> onSold = null)
    {
        _npcName = npcName;
        _pricePerMushroom = Mathf.Max(1, pricePerMushroom);
        _onClose = onClose;
        _onSold = onSold;
        _open = true;
        if (_dim != null) _dim.SetActive(true);
        _panelRT.gameObject.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (_header != null) _header.text = $"// {npcName.ToUpperInvariant()} — WILL BUY MUSHROOMS";
        if (_priceText != null) _priceText.text = $"{_pricePerMushroom} credits / mushroom";
        RefreshStock();
        SetResult("", default);
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;
        if (_dim != null) _dim.SetActive(false);
        _panelRT.gameObject.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        var cb = _onClose;
        _onClose = null;
        _onSold = null;
        cb?.Invoke();
    }

    void Update()
    {
        if (!_open) return;
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;
    }

    // Stock = the leftmost mushroom stack's species, and everything the player
    // holds OF THAT SPECIES. Selling it empties those stacks and the panel rolls
    // on to the next species the player is carrying.
    void RefreshStock()
    {
        var hb = Hotbar.Instance;
        _species = hb != null ? hb.FirstMushroomSpecies(Hotbar.ItemId.Mushroom) : null;
        int total = (hb != null && _species != null)
            ? hb.GetMushroomTotal(Hotbar.ItemId.Mushroom, _species)
            : 0;

        if (_speciesText != null)
            _speciesText.text = total > 0
                ? $"SELLING: {MushroomRegistry.DisplayName(_species).ToUpperInvariant()} ×{total}"
                : "NOTHING LEFT TO SELL";

        _slider.minValue = total > 0 ? 1 : 0;
        _slider.maxValue = total;
        _slider.wholeNumbers = true;
        _slider.SetValueWithoutNotify(total);
        _suppressInputCallback = true;
        _qtyInput.text = total.ToString();
        _suppressInputCallback = false;
        _sellBtn.interactable = total > 0;
        RefreshTotal();
    }

    void RefreshTotal()
    {
        int qty = Mathf.RoundToInt(_slider.value);
        if (_totalText != null) _totalText.text = $"Payout: {qty * _pricePerMushroom} credits";
    }

    void OnSliderChanged(float v)
    {
        _suppressInputCallback = true;
        _qtyInput.text = Mathf.RoundToInt(v).ToString();
        _suppressInputCallback = false;
        RefreshTotal();
    }

    void OnQtyInputChanged(string text)
    {
        if (_suppressInputCallback) return;
        if (!int.TryParse(text, out int v)) v = 1;
        v = Mathf.Clamp(v, (int)_slider.minValue, (int)_slider.maxValue);
        _slider.SetValueWithoutNotify(v);
        _suppressInputCallback = true;
        if (text != v.ToString()) _qtyInput.text = v.ToString();
        _suppressInputCallback = false;
        RefreshTotal();
    }

    void OnSellClicked()
    {
        int qty = Mathf.RoundToInt(_slider.value);
        if (qty <= 0) return;
        var hb = Hotbar.Instance;
        if (hb == null || string.IsNullOrEmpty(_species)) return;

        int have = hb.GetMushroomTotal(Hotbar.ItemId.Mushroom, _species);
        qty = Mathf.Min(qty, have);
        if (qty <= 0) { SetResult("You've got none on you.", C_Err); RefreshStock(); return; }

        if (!hb.SpendResource(Hotbar.ItemId.Mushroom, qty, _species))
        {
            SetResult("You've got none on you.", C_Err);
            RefreshStock();
            return;
        }

        int credits = qty * _pricePerMushroom;
        if (PlayerWallet.Instance != null) PlayerWallet.Instance.AddMoney(credits);
        // Central hook: ANY alien buying mushrooms advances Tev's onboarding, so
        // no NPC has to remember to wire it up (it no-ops outside the quest).
        MushroomQuest.NotifySold(qty);
        _onSold?.Invoke(qty);
        SetResult($"+{credits} credits!", C_Ok);
        RefreshStock();
    }

    void SetResult(string text, Color color)
    {
        if (_resultText == null) return;
        if (_resultRoutine != null) StopCoroutine(_resultRoutine);
        _resultText.text = text;
        _resultText.color = color;
        if (!string.IsNullOrEmpty(text))
            _resultRoutine = StartCoroutine(FadeResult());
    }

    IEnumerator FadeResult()
    {
        yield return new WaitForSecondsRealtime(2.5f);
        if (_resultText != null) _resultText.text = "";
    }

    void BuildUI()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = UILayer.Vendor;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        _dim = new GameObject("Dim", typeof(RectTransform));
        _dim.transform.SetParent(transform, false);
        var dimRT = (RectTransform)_dim.transform;
        dimRT.anchorMin = Vector2.zero; dimRT.anchorMax = Vector2.one;
        dimRT.offsetMin = Vector2.zero; dimRT.offsetMax = Vector2.zero;
        var dimImg = _dim.AddComponent<Image>();
        dimImg.color = new Color(0, 0, 0, 0.55f);
        dimImg.raycastTarget = true;
        _dim.SetActive(false);

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        _panelRT = (RectTransform)panel.transform;
        _panelRT.anchorMin = _panelRT.anchorMax = _panelRT.pivot = new Vector2(0.5f, 0.5f);
        _panelRT.sizeDelta = new Vector2(640, 460);
        var bg = panel.AddComponent<Image>();
        bg.color = C_Bg;

        _header      = MkText(_panelRT, "// VENDOR — WILL BUY MUSHROOMS", new Vector2(0, -16), 22, C_Header, FontStyles.Bold);
        _priceText   = MkText(_panelRT, "0 credits / mushroom",           new Vector2(0, -70), 30, C_Value,  FontStyles.Bold);
        _speciesText = MkText(_panelRT, "",                               new Vector2(0, -110), 22, C_Label, FontStyles.Bold);
        _totalText   = MkText(_panelRT, "Payout: 0 credits",              new Vector2(0, -240), 18, C_Label, FontStyles.Normal);
        _resultText  = MkText(_panelRT, "",                               new Vector2(0, -280), 22, C_Ok,    FontStyles.Bold);

        var sliderGO = new GameObject("Slider", typeof(RectTransform));
        sliderGO.transform.SetParent(_panelRT, false);
        var sRT = (RectTransform)sliderGO.transform;
        sRT.anchorMin = sRT.anchorMax = new Vector2(0.5f, 1f);
        sRT.pivot = new Vector2(0.5f, 1f);
        sRT.sizeDelta = new Vector2(420, 24);
        sRT.anchoredPosition = new Vector2(0, -160);

        _slider = sliderGO.AddComponent<Slider>();
        var sliderBg = new GameObject("Bg", typeof(RectTransform));
        sliderBg.transform.SetParent(sliderGO.transform, false);
        var bgRT = (RectTransform)sliderBg.transform;
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
        var bgImg = sliderBg.AddComponent<Image>();
        bgImg.color = new Color32(20, 40, 60, 255);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGO.transform, false);
        var faRT = (RectTransform)fillArea.transform;
        faRT.anchorMin = new Vector2(0, 0.25f); faRT.anchorMax = new Vector2(1, 0.75f);
        faRT.offsetMin = new Vector2(8, 0); faRT.offsetMax = new Vector2(-8, 0);
        var fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(fillArea.transform, false);
        var fillRT = (RectTransform)fill.transform;
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = Vector2.zero; fillRT.offsetMax = Vector2.zero;
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = C_Border;
        _slider.fillRect = fillRT;
        _slider.targetGraphic = bgImg;
        _slider.direction = Slider.Direction.LeftToRight;
        _slider.onValueChanged.AddListener(OnSliderChanged);

        var inputGO = new GameObject("QtyInput", typeof(RectTransform));
        inputGO.transform.SetParent(_panelRT, false);
        var inRT = (RectTransform)inputGO.transform;
        inRT.anchorMin = inRT.anchorMax = new Vector2(0.5f, 1f);
        inRT.pivot = new Vector2(0.5f, 1f);
        inRT.sizeDelta = new Vector2(120, 32);
        inRT.anchoredPosition = new Vector2(0, -200);
        var inImg = inputGO.AddComponent<Image>();
        inImg.color = new Color32(8, 16, 24, 255);

        var inputTextGO = new GameObject("Text", typeof(RectTransform));
        inputTextGO.transform.SetParent(inputGO.transform, false);
        var itRT = (RectTransform)inputTextGO.transform;
        itRT.anchorMin = Vector2.zero; itRT.anchorMax = Vector2.one;
        itRT.offsetMin = new Vector2(8, 4); itRT.offsetMax = new Vector2(-8, -4);
        var itTmp = inputTextGO.AddComponent<TextMeshProUGUI>();
        itTmp.fontSize = 18;
        itTmp.color = C_Label;
        itTmp.alignment = TextAlignmentOptions.Center;
        itTmp.raycastTarget = false;

        _qtyInput = inputGO.AddComponent<TMP_InputField>();
        _qtyInput.textComponent = itTmp;
        _qtyInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        _qtyInput.onValueChanged.AddListener(OnQtyInputChanged);

        var rowGO = new GameObject("ButtonRow", typeof(RectTransform));
        rowGO.transform.SetParent(_panelRT, false);
        var rRT = (RectTransform)rowGO.transform;
        rRT.anchorMin = new Vector2(0, 0); rRT.anchorMax = new Vector2(1, 0);
        rRT.pivot = new Vector2(0.5f, 0);
        rRT.sizeDelta = new Vector2(0, 60);
        rRT.anchoredPosition = new Vector2(0, 16);
        var hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 20;
        hlg.padding = new RectOffset(40, 40, 0, 0);
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        _cancelBtn = MkBtn(rRT, "DONE", C_BtnBack, Close);
        _sellBtn   = MkBtn(rRT, "SELL", C_BtnSell, OnSellClicked);

        VendorMoneyBadge.Attach(_panelRT);   // live balance while selling

        _panelRT.gameObject.SetActive(false);
    }

    static TextMeshProUGUI MkText(RectTransform parent, string text, Vector2 anchoredPos, int size, Color color, FontStyles style)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(600, 40);
        rt.anchoredPosition = anchoredPos;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }

    static Button MkBtn(Transform parent, string label, Color color, Action onClick)
    {
        var go = new GameObject($"Btn_{label}", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var btn = go.AddComponent<Button>();
        btn.onClick.AddListener(() => onClick?.Invoke());
        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(go.transform, false);
        var lblRT = (RectTransform)lblGO.transform;
        lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = Vector2.zero; lblRT.offsetMax = Vector2.zero;
        var lbl = lblGO.AddComponent<TextMeshProUGUI>();
        lbl.text = label;
        lbl.fontSize = 22;
        lbl.fontStyle = FontStyles.Bold;
        lbl.alignment = TextAlignmentOptions.Center;
        lbl.color = Color.white;
        lbl.raycastTarget = false;
        return btn;
    }
}
