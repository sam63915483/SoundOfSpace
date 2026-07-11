using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "MONEY  $123" chip floating above a vendor panel's top-right corner —
/// the player's live balance, visible exactly where buying/selling happens
/// (replaces the old always-on top-left wallet HUD). Attach() is idempotent
/// per panel; the badge lives as a child of the panel so it shows/hides
/// with it. Change-detected text updates, and the value flashes green on a
/// gain / red on a spend before settling back to gold.
/// </summary>
public class VendorMoneyBadge : MonoBehaviour
{
    static readonly Color32 BgColor     = new Color32(0x0A, 0x18, 0x28, 0xF6);
    static readonly Color32 BorderColor = new Color32(0xFF, 0xC2, 0x4A, 0x8C);
    static readonly Color32 LabelColor  = new Color32(0xEB, 0xD9, 0xA8, 0xCC);
    static readonly Color32 GoldColor   = new Color32(0xFF, 0xC2, 0x4A, 0xFF);
    static readonly Color32 GainColor   = new Color32(0x7A, 0xE8, 0x9C, 0xFF);
    static readonly Color32 SpendColor  = new Color32(0xFF, 0x6B, 0x6B, 0xFF);
    const float FlashTime = 0.8f;

    TMP_Text _value;
    int _lastMoney = int.MinValue;
    float _flashT = 1f;
    Color _flashFrom;

    /// Adds the badge to a vendor panel (no-op if it already has one).
    public static void Attach(RectTransform panel)
    {
        if (panel == null || panel.Find("MoneyBadge") != null) return;

        var go = new GameObject("MoneyBadge", typeof(RectTransform));
        go.transform.SetParent(panel, false);
        var rt = (RectTransform)go.transform;
        // Floats just above the panel's top-right corner, clear of titles.
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(0f, 10f);
        rt.sizeDelta = new Vector2(190f, 42f);
        go.AddComponent<LayoutElement>().ignoreLayout = true;   // some vendor panels run a VerticalLayoutGroup

        var bg = go.AddComponent<Image>();
        bg.sprite = GalaxyHudKit.RoundedSprite();
        bg.type = Image.Type.Sliced;
        bg.color = BgColor;
        bg.raycastTarget = false;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = BorderColor;
        outline.effectDistance = new Vector2(1f, -1f);

        var label = NewText(rt, "Label", "MONEY", 11f, LabelColor);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.characterSpacing = 3f;
        var labelRT = (RectTransform)label.transform;
        labelRT.anchorMin = new Vector2(0f, 0f);
        labelRT.anchorMax = new Vector2(0.4f, 1f);
        labelRT.offsetMin = new Vector2(14f, 0f);
        labelRT.offsetMax = Vector2.zero;

        var value = NewText(rt, "Value", "$0", 21f, GoldColor);
        value.alignment = TextAlignmentOptions.MidlineRight;
        value.fontStyle = FontStyles.Bold;
        var valueRT = (RectTransform)value.transform;
        valueRT.anchorMin = new Vector2(0.3f, 0f);
        valueRT.anchorMax = new Vector2(1f, 1f);
        valueRT.offsetMin = Vector2.zero;
        valueRT.offsetMax = new Vector2(-14f, 0f);
        var glow = value.gameObject.AddComponent<Shadow>();
        glow.effectColor = new Color(1f, 0.76f, 0.29f, 0.45f);
        glow.effectDistance = new Vector2(0f, 0f);

        var badge = go.AddComponent<VendorMoneyBadge>();
        badge._value = value;
    }

    static TMP_Text NewText(RectTransform parent, string name, string text, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(t);
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        return t;
    }

    void OnEnable()
    {
        // Refresh immediately (money may have changed while the panel was
        // closed) without triggering the gain/spend flash.
        if (PlayerWallet.Instance != null)
        {
            _lastMoney = PlayerWallet.Instance.Money;
            if (_value != null) _value.text = $"${_lastMoney:N0}";
        }
        _flashT = 1f;
    }

    void Update()
    {
        if (_value == null || PlayerWallet.Instance == null) return;
        int money = PlayerWallet.Instance.Money;
        if (money != _lastMoney)
        {
            _flashFrom = money > _lastMoney ? (Color)GainColor : (Color)SpendColor;
            _flashT = 0f;
            _lastMoney = money;
            _value.text = $"${money:N0}";
        }
        if (_flashT < 1f)
        {
            _flashT = Mathf.Min(1f, _flashT + Time.unscaledDeltaTime / FlashTime);
            _value.color = Color.Lerp(_flashFrom, GoldColor, _flashT);
        }
    }
}
