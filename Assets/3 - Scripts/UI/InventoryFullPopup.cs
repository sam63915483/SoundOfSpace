using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Brief floating pill that fades in above the hotbar to warn the player that
// mined resources didn't all fit. Auto-creates on first Show() call (no
// RuntimeInitializeOnLoadMethod — sidesteps the MainMenu trap entirely).
// Calling Show() while visible restarts the timer rather than stacking pills.
public class InventoryFullPopup : MonoBehaviour
{
    public static InventoryFullPopup Instance { get; private set; }

    Canvas _canvas;
    CanvasGroup _group;
    RectTransform _pill;
    TextMeshProUGUI _label;
    Coroutine _running;

    const float FadeIn  = 0.15f;
    const float Hold    = 1.20f;
    const float FadeOut = 0.40f;

    public static void Show()
    {
        if (Instance == null)
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
            var go = new GameObject("InventoryFullPopup");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<InventoryFullPopup>();
            Instance.Build();
        }
        Instance.ShowImpl();
    }

    void Build()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 835; // above Hotbar (830)
        HUDSceneGate.Register(_canvas);

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        _pill = new GameObject("Pill", typeof(RectTransform)).GetComponent<RectTransform>();
        _pill.SetParent(transform, false);
        _pill.anchorMin = new Vector2(0.5f, 0f);
        _pill.anchorMax = new Vector2(0.5f, 0f);
        _pill.pivot = new Vector2(0.5f, 0f);
        _pill.anchoredPosition = new Vector2(0f, 220f); // above hotbar bar
        _pill.sizeDelta = new Vector2(260f, 44f);

        var bg = _pill.gameObject.AddComponent<Image>();
        bg.sprite = GalaxyHudKit.RoundedSprite();
        bg.type = Image.Type.Sliced;
        bg.color = new Color32(0x3C, 0x15, 0x18, 0xF0);
        bg.raycastTarget = false;

        var outline = _pill.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color32(0xFF, 0x6F, 0x70, 0xC0);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        var glow = _pill.gameObject.AddComponent<Shadow>();
        glow.effectColor = new Color(1f, 0.4f, 0.4f, 0.35f);
        glow.effectDistance = new Vector2(0f, 0f);

        var labelRT = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
        labelRT.SetParent(_pill, false);
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;
        _label = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_label);
        _label.text = "INVENTORY FULL";
        _label.alignment = TextAlignmentOptions.Center;
        _label.fontSize = 18f;
        _label.fontStyle = FontStyles.Bold;
        _label.characterSpacing = 4f;
        _label.color = new Color32(0xFF, 0xE6, 0xE6, 0xFF);
        _label.raycastTarget = false;

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;
    }

    public void ShowImpl()
    {
        if (_running != null) StopCoroutine(_running);
        _running = StartCoroutine(RunFade());
    }

    IEnumerator RunFade()
    {
        float t = 0f;
        while (t < FadeIn)
        {
            t += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Clamp01(t / FadeIn);
            yield return null;
        }
        _group.alpha = 1f;

        yield return new WaitForSecondsRealtime(Hold);

        t = 0f;
        while (t < FadeOut)
        {
            t += Time.unscaledDeltaTime;
            _group.alpha = 1f - Mathf.Clamp01(t / FadeOut);
            yield return null;
        }
        _group.alpha = 0f;
        _running = null;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }
}
