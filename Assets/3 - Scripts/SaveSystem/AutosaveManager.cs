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
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ⚠️ THE PERIODIC AUTOSAVE IS GONE (Sam, 2026-08-18).
    //
    // The stasis pod is the only save point. A timer writing the world every few
    // minutes contradicted that outright — you could die and reload into a state
    // you never chose — and in co-op it was worse than untidy: nothing gated it
    // on being the host, so a guest idling for five minutes wrote a full capture
    // of a world it only renders, enemies as pose puppets and all.
    //
    // What survives is the SLOT and the one-shot below, because the backrooms
    // round trip uses them as a TRANSFER: PortalManager writes the world on the
    // way out and reads it back on the way in. That is scene plumbing, not a
    // save point, and losing it would strand the player in the poolrooms.

    /// <summary>
    /// Write the world to the transfer slot, right now.
    ///
    /// Only PortalManager should call this, and only as one half of a scene
    /// round trip. Anything that wants to record player progress belongs in the
    /// stasis pod ritual instead.
    /// </summary>
    public void Autosave()
    {
        // In co-op only the host holds a world worth writing (a guest's copy has
        // pose-puppet enemies and no timer state), and the portal round trip is
        // single-player scene plumbing anyway.
        if (!WorldSync.IsAuthority)
        {
            Debug.LogWarning("[Autosave] Skipped on a guest — the host owns the world.");
            return;
        }

        Debug.Log($"[Autosave] Writing the transfer slot '{AutosaveSlotName}'.");
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
