using UnityEngine;
using UnityEngine.UI;

/// <summary>Top + bottom black bars that animate in during dialogue / cutscenes.</summary>
public class LetterboxBars : MonoBehaviour
{
    RectTransform _top, _bottom;
    float _t;

    const float TargetHeight = 80f;
    const float Speed = 220f;

    void Awake() { BuildCanvas(); }

    void LateUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        bool active = mgr != null && mgr.MasterEnabled
                      && mgr.Input != null && mgr.Input.fxLetterboxBars
                      && PlayerController.isInDialogue;
        _t = Mathf.MoveTowards(_t, active ? 1f : 0f, Time.unscaledDeltaTime * Speed / TargetHeight);
        float h = _t * TargetHeight;
        _top.sizeDelta = new Vector2(0f, h);
        _bottom.sizeDelta = new Vector2(0f, h);
    }

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 820;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;

        _top = NewBar("Top", 1f);
        _bottom = NewBar("Bottom", 0f);
    }

    RectTransform NewBar(string name, float anchorPivotY)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = new Vector2(0f, anchorPivotY);
        rt.anchorMax = new Vector2(1f, anchorPivotY);
        rt.pivot = new Vector2(0.5f, anchorPivotY);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, 0f);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
        return rt;
    }
}
