using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Tracks the player's money and renders the bottom-right currency HUD as
/// three rounded neon chips (MONEY, WOOD, AMMO). Auto-creates itself and its
/// HUD canvas on game start — no scene setup required.
/// </summary>
public class PlayerWallet : MonoBehaviour
{
    public static PlayerWallet Instance { get; private set; }

    [Header("UI (auto-created)")]
    public TextMeshProUGUI moneyText;
    public TextMeshProUGUI ammoText;

    public int Money { get; private set; } = 0;

    // ── Palette ──────────────────────────────────────────────────────
    static readonly Color ChipBgTop   = new Color32(0x14, 0x2C, 0x48, 0xF2);
    static readonly Color ChipBgBot   = new Color32(0x0E, 0x1E, 0x34, 0xF2);
    static readonly Color ChipBorder  = new Color32(0x78, 0xC8, 0xFF, 0x8C);
    static readonly Color ChipGlow    = new Color(0.36f, 0.78f, 1f, 0.30f);
    static readonly Color LabelDim    = new Color32(0xA8, 0xD2, 0xEB, 0xCC);

    static readonly Color MoneyValueColor = new Color32(0xFF, 0xC2, 0x4A, 0xFF); // gold
    static readonly Color AmmoValueColor  = new Color32(0x88, 0xDC, 0xAA, 0xFF); // mint

    const float ChipMinWidth = 170f;
    const float ChipHeight   = 38f;
    const float ChipGap      = 8f;

    int _lastAmmoSeen = int.MinValue;
    bool _ammoChipVisible;
    GameObject _moneyChip;
    GameObject _ammoChip;
    PistolController _pistolCached;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        GameObject go = new GameObject("PlayerWallet");
        DontDestroyOnLoad(go);
        go.AddComponent<PlayerWallet>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (moneyText == null) CreateCornerHUD();
        RefreshMoney();
    }

    void Update()
    {
        if (_pistolCached == null) _pistolCached = FindObjectOfType<PistolController>(true);
        bool show = _pistolCached != null && _pistolCached.IsEquipped;
        if (show != _ammoChipVisible)
        {
            _ammoChipVisible = show;
            if (_ammoChip != null) _ammoChip.SetActive(show);
        }
        if (show && _pistolCached.CurrentAmmo != _lastAmmoSeen)
        {
            _lastAmmoSeen = _pistolCached.CurrentAmmo;
            if (ammoText != null) ammoText.text = _lastAmmoSeen.ToString();
        }
    }

    public void AddMoney(int amount)
    {
        Money += amount;
        RefreshMoney();
        Debug.Log($"[PlayerWallet] +${amount}. Total: ${Money}");
    }

    public bool SpendMoney(int amount)
    {
        if (amount < 0 || Money < amount) return false;
        Money -= amount;
        RefreshMoney();
        return true;
    }

    public void SetMoney(int amount)
    {
        Money = amount;
        RefreshMoney();
    }

    void RefreshMoney()
    {
        if (moneyText != null) moneyText.text = $"${Money}";
    }

    // ── Canvas build ─────────────────────────────────────────────────

    void CreateCornerHUD()
    {
        var canvasGO = new GameObject("WalletHUDCanvas");
        DontDestroyOnLoad(canvasGO);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 830; // above LetterboxBars (820) — stays visible during dialogue / cook UI
        HUDSceneGate.Register(canvas);
        HudVisibility.RegisterHideable(canvas);   // honours the "HIDE HUD" setting / pod cinematic

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // Stack root anchored top-left, expands downward.
        var stack = NewUI("ChipStack", canvasGO.transform);
        stack.anchorMin = new Vector2(0f, 1f);
        stack.anchorMax = new Vector2(0f, 1f);
        stack.pivot = new Vector2(0f, 1f);
        stack.anchoredPosition = new Vector2(24f, -24f);
        stack.sizeDelta = new Vector2(ChipMinWidth, 0f);
        stack.localScale = Vector3.one * 1.4f; // 1.4× HUD scale per design

        var vlg = stack.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        vlg.spacing = ChipGap;
        vlg.padding = new RectOffset(0, 0, 0, 0);

        var fitter = stack.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // MONEY is always visible. AMMO appears only while pistol is equipped.
        // Resource counts (wood/crystal/dust) now live in the hotbar.
        _moneyChip = BuildChip(stack, "MoneyChip", "MONEY", MoneyValueColor, out moneyText);
        _ammoChip  = BuildChip(stack, "AmmoChip",  "AMMO",  AmmoValueColor,  out ammoText);
        _ammoChip.SetActive(false);

        moneyText.text = "$0";
        ammoText.text  = "0";
    }

    GameObject BuildChip(RectTransform parent, string name, string labelText, Color valueColor, out TextMeshProUGUI valueText)
    {
        var chip = NewUI(name, parent);
        chip.sizeDelta = new Vector2(ChipMinWidth, ChipHeight);
        var chipLE = chip.gameObject.AddComponent<LayoutElement>();
        chipLE.preferredWidth = ChipMinWidth;
        chipLE.preferredHeight = ChipHeight;
        chipLE.flexibleWidth = 0f;
        chipLE.flexibleHeight = 0f;

        // Background: rounded gradient via GalaxyHudKit.RoundedSprite (filled
        // rounded rect — works fine as the chip body since it's a positive
        // shape rather than a ring).
        var bg = chip.gameObject.AddComponent<Image>();
        bg.sprite = GalaxyHudKit.RoundedSprite();
        bg.type = Image.Type.Sliced;
        bg.color = ChipBgBot;
        bg.raycastTarget = false;

        // Soft cyan glow under the chip (acts as the "halo" without needing
        // a second sprite).
        var glow = chip.gameObject.AddComponent<Shadow>();
        glow.effectColor = ChipGlow;
        glow.effectDistance = new Vector2(0f, 0f);

        // 1 px border outline on the bg using a UI Outline component (simpler
        // than a second rounded-rect sprite).
        var bgOutline = chip.gameObject.AddComponent<Outline>();
        bgOutline.effectColor = ChipBorder;
        bgOutline.effectDistance = new Vector2(1f, -1f);

        // Layout: label LEFT, value RIGHT.
        var hl = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childControlWidth = true;
        hl.childControlHeight = true;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = false;
        hl.padding = new RectOffset(18, 18, 0, 0);
        hl.spacing = 8f;

        // Label — flex-fills the left side so the value gets pushed right.
        var lbl = NewText(chip, "Label", labelText, 10f, FontStyles.Bold, LabelDim);
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        lbl.characterSpacing = 3f;
        var lblLE = lbl.gameObject.AddComponent<LayoutElement>();
        lblLE.preferredWidth = 60f;
        lblLE.preferredHeight = ChipHeight;
        lblLE.flexibleWidth = 1f;
        lblLE.flexibleHeight = 0f;

        // Value — fixed width on the right.
        var val = NewText(chip, "Value", "0", 22f, FontStyles.Bold, valueColor);
        val.alignment = TextAlignmentOptions.MidlineRight;
        var valLE = val.gameObject.AddComponent<LayoutElement>();
        valLE.preferredWidth = 70f;
        valLE.preferredHeight = ChipHeight;
        valLE.flexibleWidth = 0f;
        valLE.flexibleHeight = 0f;
        // Soft glow on the value text.
        var valGlow = val.gameObject.AddComponent<Shadow>();
        valGlow.effectColor = new Color(valueColor.r, valueColor.g, valueColor.b, 0.55f);
        valGlow.effectDistance = new Vector2(0f, 0f);
        // Hard drop shadow for legibility.
        var valDrop = val.gameObject.AddComponent<Shadow>();
        valDrop.effectColor = new Color(0f, 0f, 0f, 0.85f);
        valDrop.effectDistance = new Vector2(0f, -1.5f);

        valueText = val;
        return chip.gameObject;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static TextMeshProUGUI NewText(Transform parent, string name, string text, float size, FontStyles style, Color color)
    {
        var rt = NewUI(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(t);
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        return t;
    }
}
