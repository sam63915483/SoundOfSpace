using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Single full-screen radial vignette overlay. Drivers push a
/// (color, intensity) tuple per frame; the overlay composites by picking
/// the strongest driver. Used by damage pulse, low-health pulse, dialogue
/// focus, death dim, and the always-on subtle baseline.
/// </summary>
public class VignetteOverlay : MonoBehaviour
{
    Image _image;

    struct Driver { public Color color; public float intensity; }
    List<Driver> _frame = new List<Driver>();

    void Awake() { BuildCanvas(); }

    public void Push(Color color, float intensity)
    {
        if (intensity <= 0f) return;
        _frame.Add(new Driver { color = color, intensity = Mathf.Clamp01(intensity) });
    }

    void LateUpdate()
    {
        if (_frame.Count == 0)
        {
            if (_image.color.a > 0f)
            {
                var c = _image.color;
                c.a = Mathf.MoveTowards(c.a, 0f, Time.unscaledDeltaTime * 4f);
                _image.color = c;
            }
            return;
        }
        Driver best = _frame[0];
        for (int i = 1; i < _frame.Count; i++)
            if (_frame[i].intensity > best.intensity) best = _frame[i];
        _image.color = new Color(best.color.r, best.color.g, best.color.b, best.intensity);
        _frame.Clear();
    }

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 800;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;

        var rt = new GameObject("VignetteImage", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _image = rt.gameObject.AddComponent<Image>();
        _image.sprite = GetVignetteSprite();
        _image.color = new Color(0f, 0f, 0f, 0f);
        _image.raycastTarget = false;
        _image.type = Image.Type.Simple;
    }

    static Sprite _cachedSprite;
    static Sprite GetVignetteSprite()
    {
        if (_cachedSprite != null) return _cachedSprite;
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - r) / r;
                float dy = (y - r) / r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(Mathf.Pow(d, 2.2f));
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(pixels);
        tex.Apply();
        _cachedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        _cachedSprite.name = "VignetteRadial";
        return _cachedSprite;
    }
}
