using UnityEngine;
using UnityEngine.UI;

/// <summary>Animated noise overlay — subtle film-grain feel.</summary>
public class FilmGrainOverlay : MonoBehaviour
{
    RawImage _image;
    Texture2D _noiseTex;
    float _scrollT;
    const int NoiseSize = 128;

    void Awake() { BuildCanvas(); }

    void LateUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled || mgr.Input == null || !mgr.Input.fxFilmGrain)
        { _image.color = new Color(1f, 1f, 1f, 0f); return; }

        _scrollT += Time.unscaledDeltaTime * 8f;
        if (_scrollT > 1000f) _scrollT -= 1000f;
        _image.uvRect = new Rect(_scrollT, _scrollT * 1.3f, 6f, 4f);
        _image.color = new Color(1f, 1f, 1f, mgr.Input.fxFilmGrainIntensity * 0.18f);
    }

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 815;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;

        var rt = new GameObject("Grain", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _image = rt.gameObject.AddComponent<RawImage>();
        _image.texture = GetNoise();
        _image.color = new Color(1f, 1f, 1f, 0f);
        _image.raycastTarget = false;
    }

    Texture2D GetNoise()
    {
        var tex = new Texture2D(NoiseSize, NoiseSize, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Repeat;
        var pixels = new Color[NoiseSize * NoiseSize];
        for (int i = 0; i < pixels.Length; i++)
        {
            float v = Random.value;
            pixels[i] = new Color(v, v, v, v);
        }
        tex.SetPixels(pixels); tex.Apply();
        _noiseTex = tex;
        return tex;
    }
}
