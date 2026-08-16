using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Paying Tev back. Your money slot on the right, his on the left, the
/// outstanding balance between them, and a scroll wheel that decides how much
/// crosses the gap.
///
/// The interaction is deliberately the SAME one the locker uses (see
/// SlotOps.AdjustCursorAmount): click the stack, scroll to pick an amount, both
/// numbers moving live. Nothing new to learn, and it's why this panel is small —
/// the split mechanic already existed by the time this was written.
///
/// Pre-filled to exactly the outstanding balance, because that's what the player
/// means 95% of the time; the wheel is there for the other 5% — paying part of
/// it, or paying over to build bond faster.
///
/// Money moves as ITEMS: it leaves hotbar slot 8 and is gone. Nothing here
/// touches the sync layer, so the "PlayerWallet stays out of multiplayer"
/// invariant is untouched.
///
/// Auto-creates like the other vendor panels; no scene wiring.
/// </summary>
public class TevPaymentUI : MonoBehaviour
{
    public static TevPaymentUI Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("TevPaymentUI");
        DontDestroyOnLoad(go);
        go.AddComponent<TevPaymentUI>();
    }

    // The panel does not know WHAT the debt is — only how to read it and how to
    // settle it. That indirection is what let the rent revamp point it at the
    // lawn ledger without touching a line of the layout below, and it is what
    // keeps the vaulted fronting path one call away from working again.
    Func<int> _owed;
    Func<int, int> _pay;      // takes an amount, OWNS the money movement, returns what actually moved
    bool _allowOverpay;
    string _title = "OUTSTANDING";

    Action<int> _onClosed;
    int _give;
    int _walletAtOpen;
    bool _wasInDialogue;
    CursorLockMode _prevCursorLock;
    bool _prevCursorVisible;

    Canvas _canvas;
    GameObject _root;
    TextMeshProUGUI _owedText, _yoursText, _theirsText, _hintText;
    Button _doneButton;
    TextMeshProUGUI _doneLabel;

    public bool IsOpen => _root != null && _root.activeSelf;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>
    /// Pay the RENT. No overpaying: unlike a dealer's tab there is no bond to
    /// build by slipping him extra, and a landlord holding your credit is a
    /// ledger nobody asked for. MushroomQuest.PayRent caps at the balance too,
    /// so this is belt and braces.
    /// </summary>
    /// <param name="onClosed">Paid amount, or 0 if cancelled.</param>
    public void OpenForRent(Action<int> onClosed)
    {
        Open(() => MushroomQuest.RentBalance,
             MushroomQuest.PayRent,
             false, "RENT DUE", onClosed);
    }

    /// <summary>
    /// VAULTED PATH — paying down a fronting debt (FeatureVault.TevFrontingEconomy).
    /// Kept as a thin wrapper so restoring the flag needs no change here.
    /// </summary>
    /// <param name="onClosed">Paid amount, or 0 if cancelled.</param>
    public void Open(TevFronting.PlayerState state, Action<int> onClosed)
    {
        if (state == null) { onClosed?.Invoke(0); return; }
        Open(() => state.owed,
             amount =>
             {
                 if (PlayerWallet.Instance == null) return 0;
                 if (!PlayerWallet.Instance.SpendMoney(amount)) return 0;
                 TevFronting.Pay(state, amount);
                 return amount;
             },
             true, "OUTSTANDING", onClosed);
    }

    /// <param name="onClosed">Paid amount, or 0 if cancelled.</param>
    public void Open(Func<int> owed, Func<int, int> pay, bool allowOverpay, string title,
                     Action<int> onClosed)
    {
        if (owed == null || pay == null) { onClosed?.Invoke(0); return; }
        _owed = owed;
        _pay = pay;
        _allowOverpay = allowOverpay;
        _title = string.IsNullOrEmpty(title) ? "OUTSTANDING" : title;
        _onClosed = onClosed;
        _walletAtOpen = PlayerWallet.Instance != null ? PlayerWallet.Instance.Money : 0;
        // Pre-fill to the debt, capped by what you actually have.
        _give = Mathf.Clamp(owed(), 0, _walletAtOpen);

        Build();
        _root.SetActive(true);
        // This panel opens from INSIDE a Tev conversation, which has already set
        // isInDialogue. Remember the state and restore it rather than forcing
        // false on close — otherwise the player is handed back movement and fire
        // control while Tev is still speaking his reaction line.
        _wasInDialogue = PlayerController.isInDialogue;
        PlayerController.isInDialogue = true;
        _prevCursorLock = Cursor.lockState;
        _prevCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Refresh();
    }

    void Update()
    {
        if (!IsOpen) return;

        // Same gesture as the locker: wheel adjusts, Shift makes it ten at a
        // time so a four-figure debt isn't a hundred clicks.
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.01f)
        {
            bool fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int step = (wheel > 0f ? 1 : -1) * (fast ? 10 : 1);
            SetGive(_give + step);
        }

        if (Input.GetKeyDown(KeyCode.Escape)) Close(0);
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Commit();
    }

    void SetGive(int v)
    {
        // Never more than you're carrying. On the fronting path you CAN go over
        // the debt — that's the overpay path and it builds bond faster. Rent
        // stops at the balance.
        int ceiling = _walletAtOpen;
        if (!_allowOverpay) ceiling = Mathf.Min(ceiling, _owed != null ? _owed() : 0);
        _give = Mathf.Clamp(v, 0, ceiling);
        Refresh();
    }

    void Commit()
    {
        if (_give <= 0) { Close(0); return; }
        // The pay delegate owns the wallet, so a rejected payment leaves both
        // sides untouched rather than debiting money into a debt that didn't
        // move.
        int paid = _pay != null ? _pay(_give) : 0;
        Close(paid);
    }

    void Close(int paid)
    {
        if (_root != null) _root.SetActive(false);
        PlayerController.isInDialogue = _wasInDialogue;
        Cursor.lockState = _prevCursorLock;
        Cursor.visible = _prevCursorVisible;
        var cb = _onClosed;
        _onClosed = null;
        cb?.Invoke(paid);
    }

    void Refresh()
    {
        if (_owedText == null) return;
        int owed = _owed != null ? _owed() : 0;
        int keep = Mathf.Max(0, _walletAtOpen - _give);

        _owedText.text = owed > 0 ? $"{_title}  ${owed:N0}" : "SETTLED";
        _yoursText.text = $"YOU KEEP\n${keep:N0}";
        _theirsText.text = $"TEV GETS\n${_give:N0}";

        if (_give <= 0)          _hintText.text = "Scroll to choose an amount";
        else if (_give < owed)   _hintText.text = $"${owed - _give:N0} will still be owed";
        else if (_give == owed)  _hintText.text = "Squares the debt";
        else                     _hintText.text = $"${_give - owed:N0} over — he'll remember that";

        if (_doneLabel != null) _doneLabel.text = _give > 0 ? "HAND IT OVER" : "CANCEL";
    }

    // ── UI construction (procedural — matches the other vendor panels) ────

    void Build()
    {
        if (_root != null) return;

        var canvasGO = new GameObject("TevPaymentCanvas");
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 220;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGO.AddComponent<GraphicRaycaster>();

        _root = NewPanel(canvasGO.transform, "Root", new Vector2(760, 380));
        var bg = _root.GetComponent<Image>();
        bg.color = new Color32(0x08, 0x14, 0x22, 0xF2);

        var border = NewPanel(_root.transform, "Border", new Vector2(764, 384));
        border.transform.SetAsFirstSibling();
        border.GetComponent<Image>().color = (Color)CyanScannerPalette.PanelBorder;

        _owedText  = NewText(_root.transform, "Owed",  new Vector2(0, 140), 40, TextAlignmentOptions.Center);
        _yoursText = NewText(_root.transform, "Yours", new Vector2(-200, 10), 32, TextAlignmentOptions.Center);
        _theirsText= NewText(_root.transform, "Theirs",new Vector2( 200, 10), 32, TextAlignmentOptions.Center);
        _hintText  = NewText(_root.transform, "Hint",  new Vector2(0, -80), 24, TextAlignmentOptions.Center);
        _hintText.color = new Color32(0x9F, 0xB4, 0xC7, 0xFF);

        var arrow = NewText(_root.transform, "Arrow", new Vector2(0, 10), 44, TextAlignmentOptions.Center);
        arrow.text = "→";
        arrow.color = (Color)CyanScannerPalette.PanelBorder;

        var btnGO = new GameObject("Done", typeof(RectTransform), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(_root.transform, false);
        var brt = btnGO.GetComponent<RectTransform>();
        brt.sizeDelta = new Vector2(280, 62);
        brt.anchoredPosition = new Vector2(0, -142);
        btnGO.GetComponent<Image>().color = new Color32(0x14, 0x50, 0x46, 0xFF);
        _doneButton = btnGO.GetComponent<Button>();
        _doneButton.onClick.AddListener(Commit);
        _doneLabel = NewText(btnGO.transform, "Label", Vector2.zero, 26, TextAlignmentOptions.Center);

        _root.SetActive(false);
    }

    GameObject NewPanel(Transform parent, string name, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
        return go;
    }

    TextMeshProUGUI NewText(Transform parent, string name, Vector2 pos, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(340, 120);
        rt.anchoredPosition = pos;
        var t = go.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(t);
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.raycastTarget = false;
        return t;
    }
}
