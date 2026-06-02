using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Subtle full-screen color tint that shifts with the player's situation:
/// combat (any enemy active near the player) → slight warm desaturation;
/// low health → green sickly tint; peaceful (default) → very subtle cool tint.
/// </summary>
public class MoodColorGrade : MonoBehaviour
{
    Image _image;
    Color _currentColor;

    static readonly Color Peaceful = new Color(0.55f, 0.7f, 1f, 0.05f);
    static readonly Color Combat   = new Color(1f, 0.6f, 0.35f, 0.10f);
    static readonly Color LowHP    = new Color(0.4f, 1f, 0.5f, 0.14f);

    void Awake() { BuildCanvas(); }

    void LateUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled || mgr.Input == null || !mgr.Input.fxMoodColorGrade)
        { Fade(new Color(0f, 0f, 0f, 0f)); return; }

        Color target = Peaceful;
        if (ResourceManager.Instance != null && ResourceManager.Instance.HealthPercent < 0.25f)
            target = LowHP;
        else if (AnyEnemyNearby(mgr))
            target = Combat;

        Fade(target);
    }

    bool AnyEnemyNearby(CameraEffectsManager mgr)
    {
        if (mgr.PlayerCamera == null) return false;
        Vector3 p = mgr.PlayerCamera.transform.position;
        var list = EnemyController.ActiveEnemies;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i]; if (e == null) continue;
            if ((e.transform.position - p).sqrMagnitude < 30f * 30f) return true;
        }
        return false;
    }

    void Fade(Color target)
    {
        _currentColor = Color.Lerp(_currentColor, target, Time.unscaledDeltaTime * 1.2f);
        _image.color = _currentColor;
    }

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 811;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;

        var rt = new GameObject("Tint", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _image = rt.gameObject.AddComponent<Image>();
        _image.color = new Color(0f, 0f, 0f, 0f);
        _image.raycastTarget = false;
    }
}
