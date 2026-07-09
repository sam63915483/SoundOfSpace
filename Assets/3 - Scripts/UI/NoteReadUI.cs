using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Singleton fullscreen panel for reading in-world notes. NotePickup calls
// ShowNote(title, body, onClose); the panel reveals the body via typewriter
// (DialogueTextStyling.RevealCharsTMP) and waits for TAB / Esc to close. While
// open, PlayerController.isInDialogue is true so movement/look is locked.
//
// Mirrors the BonusTutorial popup style — procedural canvas, screen-overlay,
// no scene wiring required.
public class NoteReadUI : MonoBehaviour
{
    public static NoteReadUI Instance { get; private set; }

    public bool IsOpen { get; private set; }

    [Tooltip("Seconds between revealed characters in the typewriter effect — same default as NPC dialogue.")]
    public float charDelay = 0.03f;

    Canvas _canvas;
    GameObject _root;
    TextMeshProUGUI _titleText;
    TextMeshProUGUI _bodyText;
    RectTransform _bodyRt;
    Image _photo;
    RectTransform _photoRt;
    TextMeshProUGUI _hintText;
    Coroutine _typewriterRoutine;
    bool _isTyping;
    bool _skipTyping;
    System.Action _onClose;
    int _openedFrame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("NoteReadUI");
        DontDestroyOnLoad(go);
        go.AddComponent<NoteReadUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
        _root.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void ShowNote(string title, string body, System.Action onClose = null)
        => ShowNote(title, body, null, onClose);

    // Overload with an optional photo shown above the body (used for the Cold Company
    // pod-crash surveillance still). Passing null image behaves exactly like the text-only path.
    public void ShowNote(string title, string body, Sprite image, System.Action onClose = null)
    {
        if (IsOpen) return;
        IsOpen = true;
        _onClose = onClose;
        _openedFrame = Time.frameCount;

        // Reveal/hide the photo and reflow the body area beneath it.
        bool hasImg = image != null && _photo != null;
        if (_photo != null)
        {
            _photo.gameObject.SetActive(hasImg);
            if (hasImg) _photo.sprite = image;
        }
        if (_bodyRt != null)
            _bodyRt.offsetMax = new Vector2(-60f, hasImg ? -500f : -140f);

        _titleText.text = title ?? "";

        // Pre-set body string but with maxVisibleCharacters = 0; the typewriter
        // coroutine reveals it character by character. Same pattern as
        // NPCDialogue / BonfireNPCDialogue / TutorialUI.
        _bodyText.text = body ?? "";
        _bodyText.maxVisibleCharacters = 0;
        _bodyText.ForceMeshUpdate();

        _hintText.text = "(reading…)";

        _root.SetActive(true);
        PlayerController.isInDialogue = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (_typewriterRoutine != null) StopCoroutine(_typewriterRoutine);
        _typewriterRoutine = StartCoroutine(RevealBody());
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (_typewriterRoutine != null) { StopCoroutine(_typewriterRoutine); _typewriterRoutine = null; }
        _isTyping = false;
        _skipTyping = false;

        _root.SetActive(false);
        PlayerController.isInDialogue = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        var cb = _onClose;
        _onClose = null;
        cb?.Invoke();
    }

    void Update()
    {
        if (AIChatScreen.IsTypingActive) return;
        if (!IsOpen) return;
        // Suppress same-frame TAB so the F press that opened the panel doesn't
        // also trigger close on a chord (the player might still be holding TAB
        // from advancing a tutorial tip when they walk up to the note).
        if (Time.frameCount <= _openedFrame) return;

        bool advance = Input.GetKeyDown(KeyCode.Tab) ||
                       TutorialGate.PadPressed(TutorialGate.PadButton.A) ||
                       Input.GetMouseButtonDown(0);

        if (advance)
        {
            if (_isTyping)
            {
                // Skip the typewriter — show the full body immediately.
                _skipTyping = true;
            }
            else
            {
                Close();
            }
            return;
        }

        // Esc / B always closes immediately.
        if (TutorialGate.CancelPressed())
        {
            Close();
        }
    }

    IEnumerator RevealBody()
    {
        _isTyping = true;
        _skipTyping = false;
        // RevealCharsTMP reads its `line` parameter once at start; we already
        // set _bodyText.text to the body, so passing the same string is safe.
        yield return DialogueTextStyling.RevealCharsTMP(_bodyText, _bodyText.text, charDelay, () => _skipTyping);
        _isTyping = false;
        _typewriterRoutine = null;
        if (_hintText != null)
            _hintText.text = $"Press {PromptGlyphs.AdvanceTip} to close";
    }

    // ── Procedural UI build (parchment-card style) ──

    static readonly Color32 C_Dim       = new Color32(0x00, 0x00, 0x00, 0xC8);
    static readonly Color32 C_Border    = new Color32(0x6B, 0x4A, 0x2A, 0xFF);
    static readonly Color32 C_Card      = new Color32(0xF5, 0xEB, 0xCF, 0xFF);
    static readonly Color32 C_Title     = new Color32(0x3B, 0x24, 0x10, 0xFF);
    static readonly Color32 C_Body      = new Color32(0x2C, 0x1B, 0x0C, 0xFF);
    static readonly Color32 C_Hint      = new Color32(0x70, 0x4E, 0x2A, 0xC8);

    void BuildUI()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 750;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        // Root with dimmed background (also blocks clicks behind the panel).
        _root = new GameObject("Root", typeof(RectTransform));
        _root.transform.SetParent(transform, false);
        var rootRt = _root.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;
        var dim = _root.AddComponent<Image>();
        dim.color = C_Dim;

        // Outer border — sits behind the card; brown wood-frame look.
        var borderGo = new GameObject("Border", typeof(RectTransform));
        borderGo.transform.SetParent(_root.transform, false);
        var brt = borderGo.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0.5f, 0.5f);
        brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(940f, 740f);
        brt.anchoredPosition = Vector2.zero;
        var borderImg = borderGo.AddComponent<Image>();
        borderImg.color = C_Border;
        borderImg.raycastTarget = false;

        // Card — parchment center.
        var cardGo = new GameObject("Card", typeof(RectTransform));
        cardGo.transform.SetParent(_root.transform, false);
        var cardRt = cardGo.GetComponent<RectTransform>();
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(900f, 700f);
        cardRt.anchoredPosition = Vector2.zero;
        var cardImg = cardGo.AddComponent<Image>();
        cardImg.color = C_Card;
        cardImg.raycastTarget = false;

        // Title at top.
        var titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(cardGo.transform, false);
        var titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.sizeDelta = new Vector2(-80f, 80f);
        titleRt.anchoredPosition = new Vector2(0f, -30f);
        _titleText = titleGo.AddComponent<TextMeshProUGUI>();
        _titleText.fontSize = 56f;
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.alignment = TextAlignmentOptions.Center;
        _titleText.color = C_Title;
        _titleText.raycastTarget = false;

        // Divider under title.
        var divGo = new GameObject("Divider", typeof(RectTransform));
        divGo.transform.SetParent(cardGo.transform, false);
        var divRt = divGo.GetComponent<RectTransform>();
        divRt.anchorMin = new Vector2(0f, 1f);
        divRt.anchorMax = new Vector2(1f, 1f);
        divRt.pivot = new Vector2(0.5f, 1f);
        divRt.sizeDelta = new Vector2(-100f, 2f);
        divRt.anchoredPosition = new Vector2(0f, -120f);
        var divImg = divGo.AddComponent<Image>();
        divImg.color = C_Border;
        divImg.raycastTarget = false;

        // Body — most of the card area, word-wrapping enabled.
        var bodyGo = new GameObject("Body", typeof(RectTransform));
        bodyGo.transform.SetParent(cardGo.transform, false);
        var bodyRt = bodyGo.GetComponent<RectTransform>();
        bodyRt.anchorMin = new Vector2(0f, 0f);
        bodyRt.anchorMax = new Vector2(1f, 1f);
        bodyRt.offsetMin = new Vector2(60f, 80f);
        bodyRt.offsetMax = new Vector2(-60f, -140f);
        _bodyText = bodyGo.AddComponent<TextMeshProUGUI>();
        _bodyText.fontSize = 32f;
        _bodyText.alignment = TextAlignmentOptions.TopLeft;
        _bodyText.color = C_Body;
        _bodyText.enableWordWrapping = true;
        _bodyText.lineSpacing = 6f;
        _bodyText.raycastTarget = false;
        _bodyRt = bodyRt;

        // Optional photo, shown above the body for image clues (hidden by default). Sits
        // just under the divider; ShowNote reflows the body beneath it when a sprite is set.
        var photoGo = new GameObject("Photo", typeof(RectTransform));
        photoGo.transform.SetParent(cardGo.transform, false);
        _photoRt = photoGo.GetComponent<RectTransform>();
        _photoRt.anchorMin = new Vector2(0.5f, 1f);
        _photoRt.anchorMax = new Vector2(0.5f, 1f);
        _photoRt.pivot = new Vector2(0.5f, 1f);
        _photoRt.sizeDelta = new Vector2(560f, 340f);
        _photoRt.anchoredPosition = new Vector2(0f, -140f);
        _photo = photoGo.AddComponent<Image>();
        _photo.preserveAspect = true;
        _photo.raycastTarget = false;
        photoGo.SetActive(false);

        // "Press TAB to close" hint at bottom.
        var hintGo = new GameObject("Hint", typeof(RectTransform));
        hintGo.transform.SetParent(cardGo.transform, false);
        var hintRt = hintGo.GetComponent<RectTransform>();
        hintRt.anchorMin = new Vector2(0f, 0f);
        hintRt.anchorMax = new Vector2(1f, 0f);
        hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.sizeDelta = new Vector2(-40f, 40f);
        hintRt.anchoredPosition = new Vector2(0f, 22f);
        _hintText = hintGo.AddComponent<TextMeshProUGUI>();
        _hintText.fontSize = 22f;
        _hintText.fontStyle = FontStyles.Italic;
        _hintText.alignment = TextAlignmentOptions.Center;
        _hintText.color = C_Hint;
        _hintText.raycastTarget = false;
    }
}
