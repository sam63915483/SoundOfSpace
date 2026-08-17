using System;
using System.Collections;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Shared interact prompt. Minimalist helmet-HUD style (2026-08-16 redesign,
// Sam picked it from four mockups): an amber [F] key glyph framed by thin
// corner brackets + ONE or two words, uppercase and letterspaced, sitting just
// below the crosshair at a constant screen size. Callers still pass full
// sentences ("Press <b>F</b> to insert blank cassette") — ToVerb() reduces
// them to the short form here, so every existing call site works unchanged.
// Owner-based sticky API matching what GameUI.ShowInteractionPrompt did.
public class InteractPromptUI : MonoBehaviour
{
    public static InteractPromptUI Instance { get; private set; }

    /// <summary>True while a prompt is on screen. Read by CrosshairReticle to
    /// morph the center reticle into its lock-on state. Tracks the logical
    /// shown/hidden state (flips at the start of the fade-in / fade-out),
    /// not the animation midpoint.</summary>
    public static bool IsPromptVisible { get; private set; }

    /// <summary>The object whose prompt is on screen RIGHT NOW (null while
    /// hidden, and for owner-less one-shots). GazeHighlight outlines exactly
    /// this — one source, so the rim glow and the [F] prompt can never
    /// disagree about what the player is looking at.</summary>
    public static UnityEngine.Object CurrentOwner
        => Instance != null && Instance._shown && Instance._stickyOwner ? Instance._owner : null;

    [Tooltip("Seconds for the flicker-in / fade-out animation.")]
    public float slideDuration = 0.14f;
    [Tooltip("Pixels the prompt drifts up from when first revealed.")]
    public float slideOffset = 8f;
    [Tooltip("Vertical anchor — pixels BELOW the screen centre (the crosshair) at rest, so the eye never has to leave the reticle.")]
    public float belowCrosshair = 40f;

    // ── Palette — helmet-HUD amber ───────────────────────────────────
    static readonly Color HudAmber     = new Color32(0xFF, 0xC4, 0x6B, 0xFF);
    static readonly Color HudAmberGlow = new Color(1f, 0.77f, 0.42f, 0.5f);

    // ── Internal refs ────────────────────────────────────────────────
    Canvas _canvas;
    CanvasGroup _group;
    RectTransform _pillRoot;
    GameObject _keyBadge;
    TextMeshProUGUI _keyText;
    TextMeshProUGUI _bodyText;

    Coroutine _slideRoutine;
    Coroutine _oneShotRoutine;

    bool _shown;
    bool _stickyOwner;          // true if Show(owner, ...) set a sticky owner; false for ShowOneShot.
    UnityEngine.Object _owner;
    string _ownerText;          // latest text for the sticky owner; applied by Update when looked-at.
    string _lastAppliedText;    // guards per-frame text rebuilds while shown.

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("InteractPromptUI");
        DontDestroyOnLoad(go);
        go.AddComponent<InteractPromptUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildCanvas();
        if (_group != null) _group.alpha = 0f;
        if (_pillRoot != null) _pillRoot.anchoredPosition = OffScreenPos();
    }

    void OnDestroy()
    {
        if (Instance == this) { Instance = null; IsPromptVisible = false; }
    }

    void Update()
    {
        // Auto-hide if the sticky owner was destroyed without calling Clear.
        // Common case: a pickup destroys itself in Interact() — Interactable.Update
        // re-asserts the prompt one last time AFTER Interact returns, leaving
        // _owner pointing at a now-destroyed object that nothing will Clear.
        if (_stickyOwner && _owner == null)
        {
            _stickyOwner = false;
            HideInternal();
            return;
        }

        // Suppress the floating prompt whenever a modal UI is up (NPC dialogue,
        // cook panel, vendor shops — all set PlayerController.isInDialogue).
        if (PlayerController.isInDialogue)
        {
            if (_shown) HideInternal();
            return;
        }

        // Continuous gaze gate (#1): the gate is evaluated here every frame on
        // the current owner — NOT inside Show() — so it works regardless of how
        // often an owner re-asserts Show (e.g. ShipReactor calls it only once).
        // Looked-at → (re)show with the owner's latest text; looked-away → hide
        // but KEEP ownership so it reappears the moment the crosshair returns.
        if (_stickyOwner && _owner != null)
        {
            if (InteractGaze.IsLookingAt(_owner))
            {
                if (!_shown || _ownerText != _lastAppliedText)
                {
                    _lastAppliedText = _ownerText;
                    ShowInternal(_ownerText);
                }
            }
            else if (_shown)
            {
                HideInternal();
            }
        }
    }

    Vector2 RestPos()      => new Vector2(0f, -belowCrosshair);
    Vector2 OffScreenPos() => new Vector2(0f, -belowCrosshair - slideOffset);

    // ── Public API ───────────────────────────────────────────────────

    /// <summary>Sticky prompt; stays until <c>Clear(owner)</c> with the same owner.</summary>
    public static void Show(UnityEngine.Object owner, string text)
    {
        if (Instance == null) return;
        var inst = Instance;

        // Claim ownership with a look-to-select preference: a new candidate only
        // takes the prompt from the current owner if we're not already looking at
        // the current owner (or we ARE looking at the newcomer). The actual
        // show/hide + gaze gating happens continuously in Update().
        if (owner != inst._owner)
        {
            bool take = inst._owner == null
                     || InteractGaze.IsLookingAt(owner)
                     || !InteractGaze.IsLookingAt(inst._owner);
            if (!take) return;
            inst._owner = owner;
        }
        inst._stickyOwner = true;
        inst._ownerText = text;
    }

    /// <summary>Clears iff <paramref name="owner"/> matches the current owner. Idempotent.</summary>
    public static void Clear(UnityEngine.Object owner)
    {
        if (Instance == null) return;
        if (Instance._owner != owner) return;
        Instance._owner = null;
        Instance._stickyOwner = false;
        Instance._ownerText = null;
        Instance._lastAppliedText = null;
        Instance.HideInternal();
    }

    /// <summary>
    /// Clears when the current owner is ANY component on <paramref name="go"/>.
    /// For teardown paths that don't know which component claimed the prompt —
    /// the canonical case is killing an alien: AlienNPCDamageable.Die used to
    /// call Clear(this), but the prompt's owner was the RandomAlienDialogue
    /// component, so the owner check made the clear a silent no-op and the
    /// corpse kept a stuck "Press F to talk" (Sam's playtest).
    /// </summary>
    public static void ClearIfOwnedBy(GameObject go)
    {
        if (Instance == null || go == null) return;
        var owner = Instance._owner;
        if (owner == null) return;
        var comp = owner as Component;
        if ((comp != null && comp.gameObject == go) || ReferenceEquals(owner, go))
            Clear(owner);
    }

    /// <summary>Legacy: 3 s self-clearing prompt. Used by GameUI.DisplayInteractionInfo.</summary>
    public static void ShowOneShot(string text, float seconds = 3f)
    {
        if (Instance == null) return;
        Instance._owner = null;
        Instance._stickyOwner = false;
        Instance.ShowInternal(text);
        if (Instance._oneShotRoutine != null) Instance.StopCoroutine(Instance._oneShotRoutine);
        Instance._oneShotRoutine = Instance.StartCoroutine(Instance.OneShotRoutine(seconds));
    }

    void ShowInternal(string text)
    {
        // Drop Show calls while a modal UI owns the screen. See the matching
        // note in Update() — without this, an Interactable in range whose
        // Update() re-asserts "Press F" each frame would override the cook
        // panel's Clear(this) and the prompt would keep pulsing in.
        if (PlayerController.isInDialogue) return;

        string verb = ToVerb(text ?? "", out bool hasKey);
        if (_bodyText != null) _bodyText.text = verb;
        // Status lines with no key ("Someone else is in there") drop the badge.
        if (_keyBadge != null && _keyBadge.activeSelf != hasKey) _keyBadge.SetActive(hasKey);
        // Refresh the glyph every show — the player can switch between
        // keyboard and pad mid-session.
        if (hasKey && _keyText != null) _keyText.text = PromptGlyphs.InteractPlain;

        if (_shown) return;
        _shown = true;
        IsPromptVisible = true;
        if (_slideRoutine != null) StopCoroutine(_slideRoutine);
        _slideRoutine = StartCoroutine(SlideRoutine(true));
    }

    void HideInternal()
    {
        if (!_shown) return;
        _shown = false;
        IsPromptVisible = false;
        if (_slideRoutine != null) StopCoroutine(_slideRoutine);
        _slideRoutine = StartCoroutine(SlideRoutine(false));
    }

    IEnumerator OneShotRoutine(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (_owner == null) HideInternal();
        _oneShotRoutine = null;
    }

    IEnumerator SlideRoutine(bool show)
    {
        float t = 0f;
        float dur = Mathf.Max(0.01f, slideDuration);
        Vector2 from = (_pillRoot != null) ? _pillRoot.anchoredPosition : OffScreenPos();
        Vector2 to = show ? RestPos() : OffScreenPos();
        float fromAlpha = (_group != null) ? _group.alpha : 0f;
        float toAlpha = show ? 1f : 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float k = show ? 1f - Mathf.Pow(1f - u, 3f) : u * u * u;
            float alpha = Mathf.Lerp(fromAlpha, toAlpha, k);
            // Visor-readout flicker on the way IN: two dark steps in the first
            // 60% of the animation, then solid. Fade-out stays a plain fade.
            if (show && u < 0.6f)
                alpha *= (Mathf.FloorToInt(u * 8f) % 2 == 0) ? 0.35f : 1f;
            if (_pillRoot != null) _pillRoot.anchoredPosition = Vector2.Lerp(from, to, k);
            if (_group != null) _group.alpha = alpha;
            yield return null;
        }
        if (_pillRoot != null) _pillRoot.anchoredPosition = to;
        if (_group != null) _group.alpha = toAlpha;
        _slideRoutine = null;
    }

    // ── Build canvas ─────────────────────────────────────────────────

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200; // above hotbar (50), below tutorial pill (500), below pause (1000)
        HUDSceneGate.Register(canvas);
        _canvas = canvas;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        // Height-matched so the prompt is the same physical size at any aspect
        // ratio — one of the redesign's requirements was a constant-size prompt.
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;
        gameObject.AddComponent<GraphicRaycaster>();
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.interactable = false;
        _group.blocksRaycasts = false;

        // Root hangs just below the crosshair (screen centre), sized by content.
        _pillRoot = NewUI("PromptRoot", transform);
        _pillRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _pillRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _pillRoot.pivot = new Vector2(0.5f, 1f);
        _pillRoot.anchoredPosition = RestPos();
        var rootFitter = _pillRoot.gameObject.AddComponent<ContentSizeFitter>();
        rootFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        rootFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var hlg = _pillRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.spacing = 10f;

        // Key badge: the bare glyph framed by two thin corner brackets
        // (top-left and bottom-right), visor-readout style. No panel, no fill.
        var badgeRT = NewUI("KeyBadge", _pillRoot);
        _keyBadge = badgeRT.gameObject;
        var badgeLE = _keyBadge.AddComponent<LayoutElement>();
        badgeLE.preferredWidth = 26f;
        badgeLE.preferredHeight = 26f;

        _keyText = NewText(badgeRT, "Glyph", "F", 15f, FontStyles.Bold, HudAmber);
        Stretch(_keyText.rectTransform);
        _keyText.alignment = TextAlignmentOptions.Center;
        _keyText.enableWordWrapping = false;
        var glyphGlow = _keyText.gameObject.AddComponent<Shadow>();
        glyphGlow.effectColor = HudAmberGlow;
        glyphGlow.effectDistance = new Vector2(0f, 0f);

        AddCornerBracket(badgeRT, true);   // top-left
        AddCornerBracket(badgeRT, false);  // bottom-right

        // Verb — one or two words, uppercase, letterspaced.
        _bodyText = NewText(_pillRoot, "Body", "", 13f, FontStyles.Bold, HudAmber);
        _bodyText.alignment = TextAlignmentOptions.MidlineLeft;
        _bodyText.characterSpacing = 10f;
        _bodyText.enableWordWrapping = false;
        var bodyGlow = _bodyText.gameObject.AddComponent<Shadow>();
        bodyGlow.effectColor = HudAmberGlow;
        bodyGlow.effectDistance = new Vector2(0f, 0f);
        var bodyShadow = _bodyText.gameObject.AddComponent<Shadow>();
        bodyShadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        bodyShadow.effectDistance = new Vector2(0f, -2f);
    }

    // Two thin amber lines forming an L in the badge's corner.
    void AddCornerBracket(RectTransform badge, bool topLeft)
    {
        const float len = 9f, thick = 1.6f;
        Vector2 corner = topLeft ? new Vector2(0f, 1f) : new Vector2(1f, 0f);

        var h = NewUI(topLeft ? "BracketTL_H" : "BracketBR_H", badge);
        h.anchorMin = h.anchorMax = corner;
        h.pivot = corner;
        h.sizeDelta = new Vector2(len, thick);
        h.anchoredPosition = Vector2.zero;
        var hImg = h.gameObject.AddComponent<Image>();
        hImg.color = HudAmber;
        hImg.raycastTarget = false;
        h.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        var v = NewUI(topLeft ? "BracketTL_V" : "BracketBR_V", badge);
        v.anchorMin = v.anchorMax = corner;
        v.pivot = corner;
        v.sizeDelta = new Vector2(thick, len);
        v.anchoredPosition = Vector2.zero;
        var vImg = v.gameObject.AddComponent<Image>();
        vImg.color = HudAmber;
        vImg.raycastTarget = false;
        v.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
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

    static TextMeshProUGUI NewText(Transform parent, string name, string text,
                                    float size, FontStyles style, Color color)
    {
        var rt = NewUI(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(t);
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        return t;
    }

    // ── Sentence → verb normaliser ───────────────────────────────────
    //
    // Every prompt owner in the project still passes a full sentence —
    // "Press <b>F</b> to insert blank cassette", "Hold F to Eject", the three
    // ship prefabs' serialized "Press F to fly". Reducing them HERE means all
    // ~40 call sites get the minimalist form with zero edits and no site can
    // be missed. Strings that don't match the Press/Hold shape are status
    // lines ("Someone else is in there") and pass through key-less.

    static readonly Regex PressToRx = new Regex(
        @"^\s*(?:press|hold)\s+.*?\s+to\s+(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex TagRx = new Regex(@"<[^>]+>", RegexOptions.Compiled);

    // First words that read wrong alone and keep their second word instead
    // ("PICK UP", "CUT POWER", "RESTORE POWER").
    static readonly string[] TwoWordFirsts = { "pick", "cut", "restore" };

    static string ToVerb(string source, out bool hasKey)
    {
        hasKey = false;
        if (string.IsNullOrEmpty(source)) return "";

        // Only the part before any newline / pipe — multi-clause prompts keep
        // their first action.
        int cut = source.IndexOfAny(new[] { '\n', '|' });
        string head = cut >= 0 ? source.Substring(0, cut) : source;

        var m = PressToRx.Match(TagRx.Replace(head, ""));
        if (!m.Success)
        {
            // Status line — show as-is, minus markup, capped so the prompt
            // stays small.
            string plain = TagRx.Replace(head, "").Trim();
            return plain.Length > 28 ? plain.Substring(0, 28) : plain;
        }

        hasKey = true;
        string rest = m.Groups[1].Value;
        int stop = rest.IndexOfAny(new[] { '"', '(' });
        if (stop >= 0) rest = rest.Substring(0, stop);

        var words = rest.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        if (words.Length > 1 && (words[0].Equals("the", StringComparison.OrdinalIgnoreCase)
                              || words[0].Equals("a", StringComparison.OrdinalIgnoreCase)
                              || words[0].Equals("an", StringComparison.OrdinalIgnoreCase)))
            i = 1;
        if (i >= words.Length) return "INTERACT";

        string first = words[i];
        string result = first;
        if (i + 1 < words.Length)
        {
            for (int k = 0; k < TwoWordFirsts.Length; k++)
            {
                if (first.Equals(TwoWordFirsts[k], StringComparison.OrdinalIgnoreCase))
                {
                    result = first + " " + words[i + 1];
                    break;
                }
            }
        }
        return result.ToUpperInvariant();
    }
}
