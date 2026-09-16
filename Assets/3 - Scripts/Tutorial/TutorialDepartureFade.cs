using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The black the tutorial ends on, and the fade back IN on the other side.
///
/// It has to outlive a scene load — the point is to cover the load — so it is
/// DontDestroyOnLoad. The first version stopped there, and that was the bug Sam
/// hit: "it loaded the main menu screen, but the screen stayed black, and as i
/// moved my cursor i could hear the sound of it moving over the buttons". A
/// full-screen opaque canvas at sorting order 32000 that nothing ever takes away
/// is exactly a working main menu you cannot see.
///
/// So this owns the whole life cycle: fade out, survive the load, fade back in
/// on the far side, destroy itself. Nothing else has to remember it exists, and
/// there is no path where it can be left behind.
///
/// Also self-limiting: if the scene never arrives it gives up after
/// <see cref="Failsafe"/> seconds and clears anyway, because a stuck black
/// screen is worse than a missed transition.
/// </summary>
public class TutorialDepartureFade : MonoBehaviour
{
    const float Failsafe = 12f;

    CanvasGroup _group;
    bool _armed;          // waiting for the scene we fade back in on
    float _fadeInTime = 0.8f;
    float _t;
    int _mode;            // 0 = driven by the caller, 1 = fading in, 2 = done
    float _armedAt;

    public static TutorialDepartureFade Create()
    {
        var go = new GameObject("TutorialDepartureFade");
        DontDestroyOnLoad(go);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        var f = go.AddComponent<TutorialDepartureFade>();
        f._group = go.AddComponent<CanvasGroup>();
        f._group.alpha = 0f;
        f._group.blocksRaycasts = false;
        f._group.interactable = false;

        var imgGo = new GameObject("Black", typeof(RectTransform));
        imgGo.transform.SetParent(go.transform, false);
        var rt = (RectTransform)imgGo.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = imgGo.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;

        return f;
    }

    /// 0..1 while the caller is fading out.
    public void SetAlpha(float a)
    {
        if (_group != null) _group.alpha = Mathf.Clamp01(a);
    }

    /// <summary>
    /// Hand over: from here the fade waits for the next scene, fades back in
    /// over <paramref name="fadeInSeconds"/>, and destroys itself. Call this
    /// immediately before LoadScene.
    /// </summary>
    public void ArmFadeInAfterLoad(float fadeInSeconds = 0.8f)
    {
        _fadeInTime = Mathf.Max(0.05f, fadeInSeconds);
        _armed = true;
        _armedAt = Time.unscaledTime;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!_armed) return;
        _armed = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _mode = 1;
        _t = 0f;
    }

    void Update()
    {
        // Failsafe: never leave the player staring at black because a load did
        // not fire the callback we were waiting on.
        if (_armed && Time.unscaledTime - _armedAt > Failsafe)
        {
            Debug.LogWarning("[TutorialFade] scene load never reported — clearing the fade anyway.");
            _armed = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _mode = 1;
            _t = 0f;
        }

        if (_mode != 1) return;

        _t += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(_t / _fadeInTime);
        SetAlpha(1f - k * k * (3f - 2f * k));    // smoothstep out
        if (k >= 1f)
        {
            _mode = 2;
            Destroy(gameObject);
        }
    }

    void OnDestroy()
    {
        if (_armed) SceneManager.sceneLoaded -= OnSceneLoaded;
    }
}
