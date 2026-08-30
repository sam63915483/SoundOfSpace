using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The PHOSPHOR dialogue box — Sam's pick from the four 2026-08-30 mockups
/// (prototypes/dialogue-ui/index.html, style A): a CRT-terminal panel in the
/// shuttle-computer family. Dark green-black plate, thin phosphor border,
/// scanlines, a blinking-block speaker header, and a body that WRAPS and grows
/// with the line — which is the structural fix for both reported bugs (text
/// wider than the 800×600 reference canvas, and tall lines overflowing the old
/// fixed 100 px rect).
///
/// ── How it stays zero-change for every NPC ───────────────────────────────
/// Ten different scripts speak through the one shared HUD label
/// (NPCDialogue.dialogueText) via DialogueTextStyling.RevealCharsTMP, and they
/// show/hide it with SetActive. Rewriting ten call sites is how regressions
/// happen, so instead this component ADOPTS the label at runtime: reparents it
/// into a styled panel, enforces the phosphor look (change-gated, so the old
/// ApplyOutline calls are simply overridden a frame later), and mirrors the
/// label's activeSelf onto the panel every frame. Callers keep doing exactly
/// what they always did.
///
/// The speaker name rides NPCConversationTracker.OnConversationStarted, which
/// every talking NPC already fires — story characters show their real name,
/// wanderers show their AlienNames identity (same name the sell panel uses).
/// </summary>
public class PhosphorDialogueBox : MonoBehaviour
{
    public static PhosphorDialogueBox Instance { get; private set; }

    const float PanelWidth = 720f;   // on the 800×600 reference canvas ≈ 90% of screen
    const float PanelBottom = 150f;
    const float PadX = 24f, PadTop = 44f, PadBottom = 16f;
    const float BodyFontSize = 26f;

    RectTransform _panel;
    RawImage _scanlines;
    TextMeshProUGUI _header, _headerBlock;
    TextMeshProUGUI _text;          // the adopted shared label
    string _speaker = "";

    float _nextAdoptTry;
    float _blinkAt;
    bool _blockOn = true;
    float _crtT = 1f;               // 0..1 through the turn-on animation
    string _measuredText;
    float _measuredHeight = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        // Trap #1: never fires in a build — also seeded from
        // MainMenuController.EnsureGameplaySingletons.
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("PhosphorDialogueBox");
        DontDestroyOnLoad(go);
        go.AddComponent<PhosphorDialogueBox>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        NPCConversationTracker.OnConversationStarted += OnConversationStarted;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        NPCConversationTracker.OnConversationStarted -= OnConversationStarted;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        // The HUD canvas (and the label we adopted) died with the old scene;
        // the panel went with it because it was parented there. Re-adopt lazily.
        _panel = null;
        _text = null;
        _speaker = "";
    }

    void OnConversationStarted(MonoBehaviour npc)
    {
        _speaker = SpeakerNameFor(npc);
        if (_header != null) _header.text = _speaker;
    }

    /// Story characters keep their real names. The wandering aliens get their
    /// AlienNames identity — the same name the sell panel remembers them by —
    /// because "ALIEN3" on a nameplate would undo what AlienNames exists for.
    static string SpeakerNameFor(MonoBehaviour npc)
    {
        if (npc == null) return "";
        if (npc is TevMushroomOnboarding || npc is TevDialogue) return "TEV";
        string n = npc.gameObject.name.Trim();
        if (npc.GetComponent<SpawnedAlienNPC>() != null ||
            n.StartsWith("Alien", System.StringComparison.OrdinalIgnoreCase))
            return AlienNames.For(npc).ToUpperInvariant();
        return n.ToUpperInvariant();
    }

    void Update()
    {
        if (_text == null)
        {
            if (Time.unscaledTime < _nextAdoptTry) return;
            _nextAdoptTry = Time.unscaledTime + 1f;
            TryAdopt();
            if (_text == null) return;
        }

        // Visibility follows the label. RevealCharsTMP sets the label active
        // before typing; StopConversation / the choice panel set it inactive.
        // The empty-text guard covers the boot window where the scene label is
        // active-but-blank before the NPC Starts run — no empty plate flash.
        bool want = _text.gameObject.activeSelf && !string.IsNullOrEmpty(_text.text);
        if (_panel.gameObject.activeSelf != want)
        {
            _panel.gameObject.SetActive(want);
            if (want) _crtT = 0f;                        // CRT turn-on
            else { _speaker = ""; if (_header != null) _header.text = ""; }
        }
        if (!want) return;

        // CRT turn-on: vertical un-squash with a small overshoot, mirroring the
        // mockup's crtOn keyframes. Bottom pivot, so it grows up off the floor.
        if (_crtT < 1f)
        {
            _crtT = Mathf.Min(1f, _crtT + Time.unscaledDeltaTime / 0.28f);
            float y = _crtT < 0.55f
                ? Mathf.Lerp(0.06f, 1.02f, _crtT / 0.55f)
                : Mathf.Lerp(1.02f, 1f, (_crtT - 0.55f) / 0.45f);
            _panel.localScale = new Vector3(1f, y, 1f);
        }

        EnforceBodyStyle();
        FitHeight();
        BlinkHeader();
    }

    // ── adoption ─────────────────────────────────────────────────────────

    void TryAdopt()
    {
        var owner = FindObjectOfType<NPCDialogue>(true);
        if (owner == null || owner.dialogueText == null) return;
        _text = owner.dialogueText;

        var canvas = _text.GetComponentInParent<Canvas>(true);
        if (canvas == null) { _text = null; return; }

        // The panel takes the label's place in the hierarchy (same canvas, same
        // sibling index, so nothing draws in a different order than before).
        int sibling = _text.transform.GetSiblingIndex();
        var go = new GameObject("PhosphorDialoguePanel", typeof(RectTransform));
        _panel = (RectTransform)go.transform;
        _panel.SetParent(_text.transform.parent, false);
        _panel.SetSiblingIndex(sibling);
        _panel.anchorMin = new Vector2(0.5f, 0f);
        _panel.anchorMax = new Vector2(0.5f, 0f);
        _panel.pivot     = new Vector2(0.5f, 0f);
        _panel.anchoredPosition = new Vector2(0f, PanelBottom);
        _panel.sizeDelta = new Vector2(PanelWidth, 120f);

        var bg = go.AddComponent<Image>();
        bg.color = PhosphorUI.Plate;
        bg.raycastTarget = false;

        PhosphorUI.AddBorder(_panel);
        _scanlines = PhosphorUI.AddScanlines(_panel);

        // Header: blinking block + speaker name.
        _headerBlock = PhosphorUI.MakeLabel(_panel, "Block", "▮", 15f, PhosphorUI.Phosphor);
        var brt = _headerBlock.rectTransform;
        brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(0, 1);
        brt.pivot = new Vector2(0, 1);
        brt.anchoredPosition = new Vector2(PadX, -10f);
        brt.sizeDelta = new Vector2(20f, 24f);

        _header = PhosphorUI.MakeLabel(_panel, "Speaker", _speaker, 15f, PhosphorUI.Phosphor);
        _header.characterSpacing = 22f;
        var hrt = _header.rectTransform;
        hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1);
        hrt.pivot = new Vector2(0, 1);
        hrt.offsetMin = new Vector2(PadX + 20f, -34f);
        hrt.offsetMax = new Vector2(-PadX, -10f);

        // Adopt the label into the plate. Its activeSelf is preserved — the
        // panel mirrors it, not the other way round.
        var trt = _text.rectTransform;
        trt.SetParent(_panel, false);
        trt.anchorMin = new Vector2(0, 1);
        trt.anchorMax = new Vector2(1, 1);
        trt.pivot     = new Vector2(0.5f, 1f);
        trt.offsetMin = new Vector2(PadX, 0f);
        trt.offsetMax = new Vector2(-PadX, 0f);
        trt.anchoredPosition = new Vector2(0f, -PadTop);
        trt.sizeDelta = new Vector2(-PadX * 2f, 60f);

        _measuredText = null;
        _measuredHeight = -1f;
        _panel.gameObject.SetActive(_text.gameObject.activeSelf);
    }

    // ── per-frame upkeep, all change-gated ───────────────────────────────

    /// Ten NPC scripts still call DialogueTextStyling.ApplyOutline (white,
    /// bold, size 44) on this label at their own Start. Rather than edit ten
    /// files, the skin just wins: re-assert the phosphor body style whenever
    /// anything knocked it off.
    void EnforceBodyStyle()
    {
        if (_text.color != PhosphorUI.Body) _text.color = PhosphorUI.Body;
        if (!Mathf.Approximately(_text.fontSize, BodyFontSize))
        {
            _text.enableAutoSizing = false;
            _text.fontSize = BodyFontSize;
        }
        if (_text.fontStyle != FontStyles.Normal) _text.fontStyle = FontStyles.Normal;
        if (_text.alignment != TextAlignmentOptions.TopLeft) _text.alignment = TextAlignmentOptions.TopLeft;
        if (!_text.enableWordWrapping) _text.enableWordWrapping = true;
    }

    /// The whole overflow fix: the plate is as tall as the wrapped line needs.
    void FitHeight()
    {
        if (ReferenceEquals(_text.text, _measuredText) && _measuredHeight > 0f) return;
        _measuredText = _text.text;

        float bodyW = PanelWidth - PadX * 2f;
        float bodyH = Mathf.Max(30f, _text.GetPreferredValues(_text.text, bodyW, 0f).y);
        _measuredHeight = bodyH;

        var trt = _text.rectTransform;
        trt.sizeDelta = new Vector2(trt.sizeDelta.x, bodyH);
        _panel.sizeDelta = new Vector2(PanelWidth, PadTop + bodyH + PadBottom);
        if (_scanlines != null)
            _scanlines.uvRect = new Rect(0, 0, 1, _panel.sizeDelta.y / 3f);
    }

    void BlinkHeader()
    {
        if (Time.unscaledTime < _blinkAt) return;
        _blinkAt = Time.unscaledTime + 0.5f;
        _blockOn = !_blockOn;
        if (_headerBlock != null) _headerBlock.alpha = _blockOn ? 1f : 0.15f;
    }
}

/// <summary>
/// The PHOSPHOR palette + tiny builders, shared by the dialogue plate and
/// PostGreetingChoicePanel so the two halves of a conversation can't drift
/// apart visually.
/// </summary>
public static class PhosphorUI
{
    public static readonly Color Plate    = new Color32(0x05, 0x12, 0x0D, 238); // green-black
    public static readonly Color Border   = new Color32(0x1D, 0x4D, 0x38, 255);
    public static readonly Color Phosphor = new Color32(0x39, 0xE0, 0x8B, 255); // headers, accents
    public static readonly Color Body     = new Color32(0xC9, 0xFF, 0xE6, 255); // spoken line
    public static readonly Color RowText  = new Color32(0x7D, 0xDB, 0xA9, 255);
    public static readonly Color RowHot   = new Color32(0xEA, 0xFF, 0xF3, 255);
    public static readonly Color RowDim   = new Color32(0x4A, 0x6E, 0x59, 200); // disabled rows
    public static readonly Color RowHoverBg = new Color32(0x0D, 0x2A, 0x1E, 255);

    public static TextMeshProUGUI MakeLabel(RectTransform parent, string name, string text,
                                            float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.raycastTarget = false;
        return tmp;
    }

    /// Four one-unit edges rather than an Outline effect — an Outline component
    /// quadruples the fill geometry; four slivers don't.
    public static void AddBorder(RectTransform parent)
    {
        const float w = 1.5f;
        Edge(parent, "BorderT", new Vector2(0, 1), Vector2.one, new Vector2(0, -w), Vector2.zero);
        Edge(parent, "BorderB", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, w));
        Edge(parent, "BorderL", Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(w, 0));
        Edge(parent, "BorderR", new Vector2(1, 0), Vector2.one, new Vector2(-w, 0), Vector2.zero);
    }

    static void Edge(RectTransform parent, string name, Vector2 aMin, Vector2 aMax,
                     Vector2 oMin, Vector2 oMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = oMin; rt.offsetMax = oMax;
        var img = go.AddComponent<Image>();
        img.color = Border;
        img.raycastTarget = false;
        // The choice panel is a VerticalLayoutGroup; decoration must not be
        // stacked as a row. Harmless on parents with no layout group.
        go.AddComponent<LayoutElement>().ignoreLayout = true;
    }

    static Texture2D _scanTex;

    /// A 1×3 repeating strip: two clear rows, one faint dark row. Tiled via the
    /// RawImage uvRect (set by the owner whenever the panel height changes).
    public static RawImage AddScanlines(RectTransform parent)
    {
        if (_scanTex == null)
        {
            _scanTex = new Texture2D(1, 3, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            };
            _scanTex.SetPixels32(new[]
            {
                new Color32(0, 0, 0, 38),
                new Color32(0, 0, 0, 0),
                new Color32(0, 0, 0, 0),
            });
            _scanTex.Apply();
        }
        var go = new GameObject("Scanlines", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var raw = go.AddComponent<RawImage>();
        raw.texture = _scanTex;
        raw.color = Color.white;
        raw.raycastTarget = false;
        go.AddComponent<LayoutElement>().ignoreLayout = true;   // see Edge
        return raw;
    }
}
