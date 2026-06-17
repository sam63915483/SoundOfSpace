using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Center-top text overlay that surfaces the state of the V / O flight
/// assists: "VELOCITY UNMATCHED" → "VELOCITY MATCHED" (V), and
/// "ORBIT UNMATCHED" → "ORBIT MATCHED" (O). Two lines so both can be visible
/// at once if the player holds both keys.
///
/// Each line fades out ~1.5s after its key is released. Singleton, auto-
/// created like GForceHUD.
/// </summary>
public class FlightAssistStatusHUD : MonoBehaviour
{
    public static FlightAssistStatusHUD Instance { get; private set; }

    const float FadeOutDelay = 0.5f;
    const float FadeOutTime  = 1.0f;

    static readonly Color UnmatchedColor = new Color(1f, 0.55f, 0.25f, 1f); // amber
    static readonly Color MatchedColor   = new Color(0.36f, 1f, 0.55f, 1f); // green

    Canvas _canvas;
    TextMeshProUGUI _matchText;
    TextMeshProUGUI _orbitText;
    TextMeshProUGUI _toastText;
    float _matchAlpha, _orbitAlpha, _toastAlpha;
    float _matchLastSeen = -999f;
    float _orbitLastSeen = -999f;
    float _toastUntil = -999f;
    Ship _cachedShip;

    /// One-shot flash message at the center-top of the screen. Used for
    /// gameplay nudges that don't fit the V/O matched/unmatched flow —
    /// e.g. "Already piloting ship" when the player tries to teleport to
    /// the cockpit they're already in.
    public void ShowToast(string text, float seconds = 1.8f)
    {
        if (_toastText == null) return;
        _toastText.text = text;
        _toastText.color = new Color(1f, 0.8f, 0.4f, 1f); // warning amber
        _toastUntil = Time.unscaledTime + seconds;
        _toastAlpha = 1f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("FlightAssistStatusHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<FlightAssistStatusHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Build()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the teleport-to-pilot button so toast messages like
        // "Already piloting ship" aren't obscured by it.
        _canvas.sortingOrder = UILayer.Modal;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        // Toast sits below the teleport-to-pilot button (which occupies
        // y=[-32..-96] from screen top) so they don't share screen real
        // estate. Match/Orbit lines drop further down still.
        _toastText = BuildLine("ToastText", new Vector2(0f, -120f));
        _matchText = BuildLine("MatchText", new Vector2(0f, -160f));
        _orbitText = BuildLine("OrbitText", new Vector2(0f, -200f));
    }

    TextMeshProUGUI BuildLine(string name, Vector2 anchoredPos)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(640f, 36f);
        rt.anchoredPosition = anchoredPos;
        var txt = go.AddComponent<TextMeshProUGUI>();
        // Multi-step font fallback (same as TutorialUI / GForceHUD). The
        // earlier single-asset Resources.Load was failing in built games
        // depending on which TMP essentials were imported — leaving the
        // text with no font and invisible. HudFontResolver tries Techno,
        // then LiberationMono/CourierNew/LiberationSans, so SOMETHING
        // always resolves.
        HudFontResolver.Apply(txt);
        txt.fontSize = 22;
        txt.fontStyle = FontStyles.Bold;
        txt.alignment = TextAlignmentOptions.Center;
        txt.color = new Color(1f, 1f, 1f, 0f);
        txt.raycastTarget = false;
        return txt;
    }

    void LateUpdate()
    {
        // Self-enable our canvas every frame. SolarSystemMapController.OpenMap
        // hides every canvas except the legend / tutorial when the map opens,
        // and we need to keep firing toasts (e.g. "Already piloting ship"
        // when the user clicks teleport-to-pilot on a ship they're already
        // in) while the map is open.
        if (_canvas != null && !_canvas.enabled) _canvas.enabled = true;

        float now = Time.unscaledTime;

        // Toast line — independent of piloting state so the player can be
        // shown "Already piloting ship" while on foot opening the map.
        if (_toastText != null)
        {
            float toastTarget;
            if (now < _toastUntil) toastTarget = 1f;
            else
            {
                float since = now - _toastUntil;
                toastTarget = Mathf.Clamp01(1f - since / FadeOutTime);
            }
            _toastAlpha = Mathf.MoveTowards(_toastAlpha, toastTarget, Time.unscaledDeltaTime * 4f);
            var c = _toastText.color; c.a = _toastAlpha; _toastText.color = c;
        }

        // Use the cached static instead of FindObjectsOfType<Ship> — the
        // static is set/cleared by Ship.PilotShip / ExitFromSpaceship so it
        // already reflects the currently-piloted instance.
        if (_cachedShip == null || !_cachedShip.IsPiloted)
            _cachedShip = Ship.PilotedInstance;

        // When no ship is piloted, fall back to the player jetpack —
        // PlayerController exposes the same IsCircularizing / IsOrbitMatched
        // flags now, so this HUD doubles as the player's flight-assist
        // display when they're orbit-matching on foot via the jetpack.
        if (_cachedShip == null && (_cachedPC == null))
            _cachedPC = FindObjectOfType<PlayerController>();

        bool circularizing;
        bool orbitMatched;
        bool matchingVel;
        bool velMatched;
        var drone = DroneController.Active;
        if (drone != null)
        {
            // Mission 1 pilot test drone — drives the same V/O status display as the ship.
            circularizing = drone.IsCircularizing;
            orbitMatched  = drone.IsOrbitMatched;
            matchingVel   = drone.IsMatchingVelocity;
            velMatched    = drone.IsVelocityMatched;
        }
        else if (_cachedShip != null)
        {
            circularizing = _cachedShip.IsCircularizing;
            orbitMatched  = _cachedShip.IsOrbitMatched;
            matchingVel   = _cachedShip.IsMatchingVelocity;
            velMatched    = _cachedShip.IsVelocityMatched;
        }
        else if (_cachedPC != null)
        {
            // Player jetpack — only circularize is implemented for the
            // player, not the V-key match-velocity. matchingVel stays false
            // so the upper "VELOCITY MATCHED/UNMATCHED" line fades out
            // while on foot.
            circularizing = _cachedPC.IsCircularizing;
            orbitMatched  = _cachedPC.IsOrbitMatched;
            matchingVel   = false;
            velMatched    = false;
        }
        else
        {
            FadeLine(_matchText, ref _matchAlpha, 0f);
            FadeLine(_orbitText, ref _orbitAlpha, 0f);
            return;
        }

        // Match line — show while V is held; freeze on last state when released, then fade.
        if (matchingVel)
        {
            _matchLastSeen = now;
            _matchText.text = velMatched ? "VELOCITY MATCHED" : "VELOCITY UNMATCHED";
            _matchText.color = ApplyAlpha(velMatched ? MatchedColor : UnmatchedColor, 1f);
            _matchAlpha = 1f;
        }
        else
        {
            FadeLine(_matchText, ref _matchAlpha, FadeAlphaFor(now - _matchLastSeen));
        }

        // Orbit line — same shape, now also driven by the player jetpack
        // when no ship is piloted.
        if (circularizing)
        {
            _orbitLastSeen = now;
            _orbitText.text = orbitMatched ? "ORBIT MATCHED" : "ORBIT UNMATCHED";
            _orbitText.color = ApplyAlpha(orbitMatched ? MatchedColor : UnmatchedColor, 1f);
            _orbitAlpha = 1f;
        }
        else
        {
            FadeLine(_orbitText, ref _orbitAlpha, FadeAlphaFor(now - _orbitLastSeen));
        }
    }

    PlayerController _cachedPC;

    static float FadeAlphaFor(float elapsedSinceRelease)
    {
        if (elapsedSinceRelease < FadeOutDelay) return 1f;
        float t = (elapsedSinceRelease - FadeOutDelay) / FadeOutTime;
        return Mathf.Clamp01(1f - t);
    }

    void FadeLine(TextMeshProUGUI t, ref float currentAlpha, float targetAlpha)
    {
        currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, Time.unscaledDeltaTime * 4f);
        Color c = t.color; c.a = currentAlpha; t.color = c;
    }

    static Color ApplyAlpha(Color c, float a) { c.a = a; return c; }
}
