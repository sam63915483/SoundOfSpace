using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Periodic autosave singleton. Auto-creates in any gameplay scene, sleeps in
// MainMenu / Cutscene / Flashback. The interval is configured via the pause
// menu (GalaxyPauseMenuStyler builds the slider) and persists in PlayerPrefs
// under IntervalPrefKey.
//
// Autosaves write to a single dedicated slot named "autosave" — they overwrite
// each tick rather than accumulating, so save folder size stays bounded. The
// slot shows up in the load list like any other save (sorted by timestamp,
// usually at the top because it's the most recent).
public class AutosaveManager : MonoBehaviour
{
    public static AutosaveManager Instance { get; private set; }

    public const string IntervalPrefKey      = "AutosaveIntervalMinutes";
    public const float  DefaultIntervalMinutes = 5f;
    public const float  MinIntervalMinutes     = 1f;
    public const float  MaxIntervalMinutes     = 30f;
    public const string AutosaveSlotName       = "autosave";

    public float IntervalMinutes
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(IntervalPrefKey, DefaultIntervalMinutes),
                           MinIntervalMinutes, MaxIntervalMinutes);
        set
        {
            float v = Mathf.Clamp(value, MinIntervalMinutes, MaxIntervalMinutes);
            PlayerPrefs.SetFloat(IntervalPrefKey, v);
            PlayerPrefs.Save();
        }
    }

    float lastAutosaveTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("AutosaveManager");
        DontDestroyOnLoad(go);
        go.AddComponent<AutosaveManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Debug.Log($"[Autosave] Manager created. Interval = {IntervalMinutes} min.");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        // Reset the timer on entry so we don't fire immediately after a fresh load.
        lastAutosaveTime = Time.realtimeSinceStartup;
        StartCoroutine(AutosaveLoop());
    }

    IEnumerator AutosaveLoop()
    {
        while (true)
        {
            // Wake every 5s to check — finer granularity than the interval itself
            // so that the moment a non-eligible state ends (e.g. pause closes),
            // we save promptly rather than waiting another full interval.
            yield return new WaitForSecondsRealtime(5f);
            if (ShouldAutosave()) Autosave();
        }
    }

    bool ShouldAutosave()
    {
        var sceneName = SceneManager.GetActiveScene().name;
        if (sceneName == "MainMenu") return false;
        if (sceneName.StartsWith("Cutscene") || sceneName.StartsWith("Flashback")) return false;
        // Pause menu open or any other timestop — don't capture a frozen state.
        if (Time.timeScale == 0) return false;
        // PendingLoad has scheduled but un-applied data — capture would race the apply.
        if (PendingLoad.Data != null) return false;

        float elapsed = Time.realtimeSinceStartup - lastAutosaveTime;
        return elapsed >= IntervalMinutes * 60f;
    }

    public void Autosave()
    {
        Debug.Log($"[Autosave] Saving to slot '{AutosaveSlotName}'.");
        var path = SaveSystem.Save(AutosaveSlotName);
        lastAutosaveTime = Time.realtimeSinceStartup;
        if (path != null) ShowToast();
    }

    // ── On-screen "AUTOSAVED" toast ────────────────────────────────────────────

    Canvas toastCanvas;
    CanvasGroup toastGroup;
    TextMeshProUGUI toastText;
    Coroutine toastRoutine;

    void ShowToast()
    {
        if (toastCanvas == null) BuildToast();
        if (toastRoutine != null) StopCoroutine(toastRoutine);
        toastRoutine = StartCoroutine(ToastFade());
    }

    void BuildToast()
    {
        toastCanvas = gameObject.AddComponent<Canvas>();
        toastCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        toastCanvas.sortingOrder = UILayer.Toast; // below pause menu
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        var rt = new GameObject("AutosaveToast", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -30f);
        rt.sizeDelta = new Vector2(300f, 60f);

        toastGroup = rt.gameObject.AddComponent<CanvasGroup>();
        toastGroup.alpha = 0f;
        toastGroup.blocksRaycasts = false;
        toastGroup.interactable = false;

        toastText = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null) toastText.font = font;
        toastText.text = "AUTOSAVED";
        toastText.fontSize = 28f;
        toastText.fontStyle = FontStyles.Bold;
        toastText.alignment = TextAlignmentOptions.Center;
        toastText.characterSpacing = 8f;
        toastText.color = new Color32(0xA8, 0xE6, 0xFF, 0xFF);
        toastText.raycastTarget = false;
        var glow = toastText.gameObject.AddComponent<Shadow>();
        glow.effectColor = new Color(0.36f, 0.85f, 1f, 0.5f);
        glow.effectDistance = new Vector2(0f, -2f);
    }

    IEnumerator ToastFade()
    {
        // Fade in (0.25s), hold (1.5s), fade out (0.6s) — uses unscaled time so
        // it still animates if the game is paused right after the save.
        float t = 0f;
        while (t < 0.25f) { t += Time.unscaledDeltaTime; toastGroup.alpha = t / 0.25f; yield return null; }
        toastGroup.alpha = 1f;
        yield return new WaitForSecondsRealtime(1.5f);
        t = 0f;
        while (t < 0.6f) { t += Time.unscaledDeltaTime; toastGroup.alpha = 1f - t / 0.6f; yield return null; }
        toastGroup.alpha = 0f;
        toastRoutine = null;
    }

    // Reset the elapsed counter — used after a manual save so the autosave
    // doesn't fire immediately afterwards.
    public void ResetTimer()
    {
        lastAutosaveTime = Time.realtimeSinceStartup;
    }
}
