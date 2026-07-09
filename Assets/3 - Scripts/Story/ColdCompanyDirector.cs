using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Auto-singleton glue for "Cold Company" (Main Mission 1). Its one job right now is to keep the
/// mission's compass marker correct: closure-based waypoints aren't serialized, so after a
/// save/load, scene transition, or New Game the marker is gone. A cheap once-a-second
/// <see cref="ColdCompany.EnsureCompass"/> re-establishes whatever the current step needs (and
/// clears our markers when the mission isn't active).
///
/// Follows the standard auto-singleton pattern (RuntimeInitializeOnLoadMethod + MainMenu
/// early-return + Instance guard) AND is seeded in MainMenuController.EnsureGameplaySingletons —
/// see CLAUDE.md trap #1 (MainMenu-skipping singletons never auto-create in builds).
/// </summary>
public class ColdCompanyDirector : MonoBehaviour
{
    public static ColdCompanyDirector Instance { get; private set; }

    const float EnsureInterval = 1f;
    float _nextEnsure;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        if (Instance != null) return;
        var go = new GameObject("ColdCompanyDirector");
        DontDestroyOnLoad(go);
        go.AddComponent<ColdCompanyDirector>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (Time.unscaledTime < _nextEnsure) return;
        _nextEnsure = Time.unscaledTime + EnsureInterval;
        ColdCompany.EnsureCompass();
        ColdCompany.PollArrival();
        ColdCompany.AnnounceReturnIfReady();
    }
}
