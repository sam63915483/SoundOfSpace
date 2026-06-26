using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Top-centre screen banner that fades in when the player kills a story-
// significant NPC. Self-instantiates on first Show() — no scene wiring
// required. DontDestroyOnLoad so a single instance carries between scenes.
public class StoryImpactNotice : MonoBehaviour
{
    static StoryImpactNotice _instance;

    public static void Show(string message, float duration = 7f)
    {
        if (string.IsNullOrEmpty(message)) return;
        if (_instance == null)
        {
            var go = new GameObject("StoryImpactNotice");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<StoryImpactNotice>();
        }
        _instance.ShowMessage(message, duration);
    }

    Canvas _canvas;
    TextMeshProUGUI _text;
    Coroutine _routine;

    void Awake()
    {
        Build();
        gameObject.SetActive(false);
    }

    void Build()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above pause menu (1000) and the galaxy pause card (1000); below the
        // save/load dialog (2000). Visible during normal play and over HUD.
        _canvas.sortingOrder = UILayer.Toast;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(transform, false);
        var rt = textGO.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -90f);
        // Narrow box that wraps to several lines so the banner stays inside a
        // centred 9:16 vertical crop (TikTok) even when capturing on an ultra-
        // wide 3440x1080 display. A wide box reads fine on 16:9 but spills off
        // both sides of a vertical crop. ~480 logical px keeps it in the middle.
        rt.sizeDelta = new Vector2(480f, 360f);

        _text = textGO.AddComponent<TextMeshProUGUI>();
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null) _text.font = font;
        _text.fontSize = 36f;
        _text.fontStyle = FontStyles.Bold | FontStyles.Italic;
        _text.alignment = TextAlignmentOptions.Center;
        _text.color = new Color(1f, 0.85f, 0.35f, 1f);
        _text.raycastTarget = false;
        _text.enableWordWrapping = true;
        _text.characterSpacing = 1f;

        var shadow = textGO.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(2f, -2f);
    }

    void ShowMessage(string message, float duration)
    {
        gameObject.SetActive(true);
        _text.text = message;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(BannerRoutine(duration));
    }

    IEnumerator BannerRoutine(float holdDuration)
    {
        yield return Fade(0.4f, 0f, 1f);
        yield return new WaitForSecondsRealtime(holdDuration);
        yield return Fade(0.8f, 1f, 0f);
        gameObject.SetActive(false);
        _routine = null;
    }

    IEnumerator Fade(float fadeDuration, float from, float to)
    {
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / fadeDuration);
            var c = _text.color;
            c.a = Mathf.Lerp(from, to, u);
            _text.color = c;
            yield return null;
        }
        var final = _text.color;
        final.a = to;
        _text.color = final;
    }
}
