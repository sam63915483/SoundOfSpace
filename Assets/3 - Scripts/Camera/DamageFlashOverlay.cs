using UnityEngine;
using UnityEngine.UI;

/// <summary>Full-screen red flash on damage. Driven by external Flash() calls.</summary>
public class DamageFlashOverlay : MonoBehaviour
{
    Image _image;
    float _alpha;

    void Awake() { BuildCanvas(); }

    public void Flash(float intensity = 0.55f)
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled) return;
        var input = mgr.Input;
        if (input != null && !input.fxDamageFlash) return;
        _alpha = Mathf.Max(_alpha, intensity);
    }

    void LateUpdate()
    {
        if (_alpha > 0f)
            _alpha = Mathf.MoveTowards(_alpha, 0f, Time.unscaledDeltaTime * 2.2f);
        var c = _image.color; c.a = _alpha; _image.color = c;
    }

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 810;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;

        var rt = new GameObject("RedFlash", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _image = rt.gameObject.AddComponent<Image>();
        _image.color = new Color(1f, 0.1f, 0.15f, 0f);
        _image.raycastTarget = false;
    }
}
