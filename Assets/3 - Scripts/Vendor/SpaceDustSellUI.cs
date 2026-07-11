using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared sell panel used by every NPC. Open(...) takes the rolled values for
/// the active conversation; SELL rolls Random.value against acceptChance.
/// Success awards qty * pricePerDust credits and deducts dust. Fail leaves
/// dust untouched and shows a refusal line; the same rolled values persist
/// so the player can adjust quantity and try again.
/// </summary>
public class SpaceDustSellUI : MonoBehaviour
{
    public static SpaceDustSellUI Instance { get; private set; }

    static readonly Color C_Bg       = new Color32(10, 24, 40, 240);
    static readonly Color C_Border   = new Color32(120, 200, 255, 220);
    static readonly Color C_Header   = new Color32(184, 140, 255, 255);
    static readonly Color C_Label    = new Color32(234, 246, 255, 255);
    static readonly Color C_Value    = new Color32(255, 215, 50, 255);
    static readonly Color C_BtnSell  = new Color32(60, 145, 70, 255);
    static readonly Color C_BtnBack  = new Color32(140, 60, 60, 255);
    static readonly Color C_Ok       = new Color32(110, 220, 130, 255);
    static readonly Color C_Err      = new Color32(255, 110, 110, 255);

    Canvas _canvas;
    RectTransform _panelRT;
    TextMeshProUGUI _header, _priceText, _chanceText, _totalText, _resultText;
    Slider _slider;
    TMP_InputField _qtyInput;
    Button _sellBtn, _cancelBtn;

    string _npcName;
    float _acceptChance;
    int   _pricePerDust;
    // NPC's "comfortable" max quantity for this conversation. Offering at-or-
    // below this gets full _acceptChance; offering more scales chance down by
    // (_preferredMaxQty / qty). Recomputed live as the player drags the slider.
    int   _preferredMaxQty;
    Action _onClose;
    Coroutine _resultRoutine;
    bool _suppressInputCallback;
    bool _open;
    GameObject _dim;

    string[] _refusalLines = {
        "Hmm, not today.",
        "Pass.",
        "Eh, doesn't speak to me.",
        "I'll think about it... nope.",
        "Not feeling it."
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        // No MainMenu skip: RuntimeInitializeOnLoadMethod only fires once at
        // game start. In a build that starts in MainMenu, skipping there would
        // mean Instance is never created when the gameplay scene later loads
        // (NRE the first time an NPC tries to open the sell UI). The dim is
        // SetActive(false) at build, so the canvas is invisible in MainMenu.
        if (Instance != null) return;
        var go = new GameObject("SpaceDustSellUI");
        DontDestroyOnLoad(go);
        go.AddComponent<SpaceDustSellUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool IsOpen => _open;

    public void Open(string npcName, float acceptChance, int pricePerDust, int preferredMaxQty, Action onClose)
    {
        _npcName = npcName;
        _acceptChance = Mathf.Clamp01(acceptChance);
        _pricePerDust = Mathf.Max(1, pricePerDust);
        _preferredMaxQty = Mathf.Max(1, preferredMaxQty);
        _onClose = onClose;
        _open = true;
        if (_dim != null) _dim.SetActive(true);
        _panelRT.gameObject.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (_header != null) _header.text = $"// {npcName.ToUpperInvariant()} — WILL BUY DUST";
        if (_priceText != null) _priceText.text = $"{_pricePerDust} credits / dust";
        RefreshSliderBounds();
        // Chance text is set inside RefreshTotal so it tracks the current qty.
        RefreshTotal();
        SetResult("", default);
    }

    /// <summary>
    /// Quantity-scaled chance: full _acceptChance at qty ≤ preferredMaxQty,
    /// then linear falloff. Mirrors NPCSellDustOption.EffectiveAcceptChance —
    /// kept in this file too so the UI can compute the live display value
    /// without a back-reference to the source component.
    /// </summary>
    float EffectiveAcceptChance(int qty)
    {
        if (qty <= 0) return 0f;
        if (_preferredMaxQty <= 0) return _acceptChance;
        float scale = Mathf.Min(1f, (float)_preferredMaxQty / qty);
        return Mathf.Clamp01(_acceptChance * scale);
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
        cb?.Invoke();
    }

    void Update()
    {
        if (!_open) return;
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;
    }

    void RefreshSliderBounds()
    {
        int total = SpaceDustInventory.Instance != null ? SpaceDustInventory.Instance.Count : 0;
        _slider.minValue = total > 0 ? 1 : 0;
        _slider.maxValue = total;
        _slider.wholeNumbers = true;
        _slider.SetValueWithoutNotify(total);
        _suppressInputCallback = true;
        _qtyInput.text = total.ToString();
        _suppressInputCallback = false;
        _sellBtn.interactable = total > 0;
    }

    void RefreshTotal()
    {
        int qty = Mathf.RoundToInt(_slider.value);
        if (_totalText != null) _totalText.text = $"Payout if accepted: {qty * _pricePerDust} credits";
        if (_chanceText != null)
        {
            float chance = EffectiveAcceptChance(qty);
            _chanceText.text = $"{Mathf.RoundToInt(chance * 100f)}% ACCEPT CHANCE";
        }
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
        if (SpaceDustInventory.Instance == null) return;
        if (SpaceDustInventory.Instance.Count < qty) { qty = SpaceDustInventory.Instance.Count; }
        // Roll against the QUANTITY-SCALED chance, not the base — matches what
        // we display in _chanceText so the player's expectations track the roll.
        bool accepted = UnityEngine.Random.value < EffectiveAcceptChance(qty);
        if (accepted)
        {
            int credits = qty * _pricePerDust;
            SpaceDustInventory.Instance.Spend(qty);
            if (PlayerWallet.Instance != null) PlayerWallet.Instance.AddMoney(credits);
            SetResult($"+{credits} credits!", C_Ok);
        }
        else
        {
            SetResult(_refusalLines[UnityEngine.Random.Range(0, _refusalLines.Length)], C_Err);
        }
        RefreshSliderBounds();
        RefreshTotal();
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

        _header    = MkText(_panelRT, "// VENDOR — WILL BUY DUST", new Vector2(0, -16), 22, C_Header, FontStyles.Bold);
        _priceText = MkText(_panelRT, "0 credits / dust",          new Vector2(0, -70), 30, C_Value,  FontStyles.Bold);
        _chanceText= MkText(_panelRT, "0% ACCEPT CHANCE",          new Vector2(0, -110), 24, C_Header, FontStyles.Bold);
        _totalText = MkText(_panelRT, "Payout if accepted: 0",     new Vector2(0, -240), 18, C_Label,  FontStyles.Normal);
        _resultText= MkText(_panelRT, "",                          new Vector2(0, -280), 22, C_Ok,     FontStyles.Bold);

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

        _cancelBtn = MkBtn(rRT, "CANCEL", C_BtnBack, Close);
        _sellBtn   = MkBtn(rRT, "SELL",   C_BtnSell, OnSellClicked);

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
