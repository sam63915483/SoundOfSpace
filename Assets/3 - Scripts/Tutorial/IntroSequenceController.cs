using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Mission 1 cold open / wake-up sequence. See
// docs/superpowers/specs/2026-06-16-mission1-intro-wakeup-soul-design.md.
//
// Scene-placed (NOT an auto-singleton): drop one on an object in the gameplay
// scene. On a fresh New Game it builds a full-screen black overlay, runs the
// groggy click-to-wake + voiced briefing + heartbeat + reassurance, then unlocks
// control into the existing survival flow. On Load / respawn it disables itself.
//
// All spoken lines go through the existing HAL HUD + TTS pipeline. The line
// strings below are the EXACT TTS keys — they must match HALVoiceManifest.Lines
// byte-for-byte (em-dash, ellipsis included) or the line plays silently.
public class IntroSequenceController : MonoBehaviour
{
    // ── Authored lines (also the exact TTS manifest keys) ──────────────────
    const string LineWakeUp   = "Wake up";
    const string Line01 = "Good morning, astronaut. Vital signs stable.";
    const string Line02 = "You have been in transit for three years. You crash-landed on this world two days ago.";
    const string Line03 = "Memory loss is expected after stasis of this length. It will not affect the mission.";
    const string Line04 = "While you were unconscious, a local took you in. A native species. You are, currently, their guest.";
    const string Line05 = "Heart rate elevated. Vitals irregular.";
    const string Line06 = "It is normal for those emerging from stasis to have difficulty recalibrating. Remember — when the mission is complete, you will be returned home.";
    const string Line07 = "...For now, try not to think about it.";

    // ── Tunables (appended at END per convention) ──────────────────────────
    [Header("Timing")]
    [SerializeField] float wakePromptDelay   = 10f;   // seconds before "Press LMB" appears
    [SerializeField] float wakeLoopInterval  = 1.0f;  // gap between "Wake up" re-sends
    [SerializeField] int   clicksToWake      = 6;
    [SerializeField] float unfadePerClick    = 0.30f; // lerp time per click step
    [SerializeField] float interLineGap      = 0.45f; // beat between briefing lines
    [SerializeField] float photoBeatSilence  = 2.5f;  // held silence on the photo before vitals

    [Header("Audio (assigned in Task 5/7)")]
    [SerializeField] AudioClip heartbeatClip;
    [SerializeField] AudioClip roomToneClip;
    [SerializeField, Range(0f, 1f)] float heartbeatTargetVolume = 0.7f;
    [SerializeField, Range(0f, 1f)] float roomToneVolume        = 0.25f;
    [SerializeField] float heartbeatFadeIn  = 3f;
    [SerializeField] float heartbeatFadeOut = 2f;

    [Header("Grogginess (wake-up blur + double vision)")]
    [SerializeField] Material grogginessMaterial;     // uses the Hidden/Grogginess shader
    [SerializeField] float grogRecoverRate = 0.07f;   // intensity units/sec (~14s from 1 to 0)

    [Header("Phone")]
    [SerializeField] float phoneNagDelay = 60f;       // delay the first-open nag this long after control returns

    // ── Runtime ────────────────────────────────────────────────────────────
    Canvas _canvas;
    Image _black;
    TextMeshProUGUI _prompt;
    float _targetAlpha = 1f;
    float _currentAlpha = 1f;
    int _clicks;
    bool _clicksArmed;
    bool _running;
    AudioSource _heartbeat;
    AudioSource _roomTone;
    GrogginessImageEffect _grog;
    float _grogIntensity = 1f;
    float _grogTarget = 1f;

    void Awake()
    {
        // Load path positions the player from save data and must not see a black
        // flash or a replayed intro. PendingLoad.Data is set before scene load,
        // so this is reliable in Awake.
        if (PendingLoad.Data != null) { enabled = false; return; }

        BuildOverlay();          // black at alpha 1 immediately — hides the spawn frame
        if (roomToneClip != null) StartRoomTone();

        // Hold the whole "Incoming transmission" first-contact beat (red HAL line +
        // phone flash + "Press X to open your phone" nag) for the cold open; the intro
        // fires it a minute after control returns (see ReleaseFirstContact).
        PlayerPhoneUI.SuppressFirstNag = true;
        StoryDirector.HoldColdOpen = true;
    }

    IEnumerator Start()
    {
        if (!enabled) yield break;

        // Defer past NewGameReset.Apply (which runs after one frame + one
        // FixedUpdate and resets EarlyGameProgress.IntroPlayed to false on a
        // fresh game) so we read the post-reset flag, not a stale value from a
        // previous in-process session.
        yield return null;
        yield return new WaitForFixedUpdate();
        yield return null;

        if (EarlyGameProgress.IntroPlayed) { yield return FadeOutAndCleanup(); yield break; }

        EarlyGameProgress.IntroPlayed = true;
        yield return RunSequence();
    }

    // ── Overlay construction (programmatic; no scene-UI authoring) ─────────
    void BuildOverlay()
    {
        var go = new GameObject("IntroBlackOverlay");
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 32760;                 // above everything
        go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        var blackGO = new GameObject("Black");
        blackGO.transform.SetParent(go.transform, false);
        _black = blackGO.AddComponent<Image>();
        _black.color = Color.black;
        _black.raycastTarget = false;                 // clicks read via Input, not UI
        var brt = _black.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;

        var promptGO = new GameObject("PressLMBPrompt");
        promptGO.transform.SetParent(go.transform, false);
        _prompt = promptGO.AddComponent<TextMeshProUGUI>();
        _prompt.text = "Press " + PromptGlyphs.PrimaryClickCap;
        _prompt.alignment = TextAlignmentOptions.Center;
        _prompt.fontSize = 42;
        _prompt.color = new Color(1f, 1f, 1f, 0.85f);
        var prt = _prompt.rectTransform;
        prt.anchorMin = new Vector2(0.5f, 0.5f); prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(800f, 120f);
        _prompt.gameObject.SetActive(false);          // shown at wakePromptDelay

        _currentAlpha = _targetAlpha = 1f;
        ApplyAlpha();
    }

    void ApplyAlpha()
    {
        if (_black != null) _black.color = new Color(0f, 0f, 0f, _currentAlpha);
    }

    void Update()
    {
        // Smoothly lerp the black toward its per-click target (grogginess).
        if (_black != null && !Mathf.Approximately(_currentAlpha, _targetAlpha))
        {
            float step = (unfadePerClick > 0f ? Time.unscaledDeltaTime / unfadePerClick : 1f);
            _currentAlpha = Mathf.MoveTowards(_currentAlpha, _targetAlpha, step);
            ApplyAlpha();
        }

        // Count wake clicks only once armed (after the prompt appears).
        if (_running && _clicksArmed && Input.GetMouseButtonDown(0))
        {
            _clicks++;
            _targetAlpha = Mathf.Clamp01(1f - (float)_clicks / clicksToWake);
        }

        // Grogginess recovery — blur + double vision fade as the player comes to.
        if (_grog != null)
        {
            _grogIntensity = Mathf.MoveTowards(_grogIntensity, _grogTarget, grogRecoverRate * Time.unscaledDeltaTime);
            _grog.intensity = _grogIntensity;
        }
    }

    // ── The sequence ───────────────────────────────────────────────────────
    IEnumerator RunSequence()
    {
        _running = true;
        TutorialGate.LockAll();                       // freeze movement + look
        AttachGrogginess();                           // fuzzy + double vision until the player comes to

        // Phase 1: black + "Wake up" loop, arm the prompt at the delay.
        var wakeLoop = StartCoroutine(WakeUpLoop());
        float t = 0f;
        while (t < wakePromptDelay) { t += Time.unscaledDeltaTime; yield return null; }
        if (_prompt != null) _prompt.gameObject.SetActive(true);
        _clicksArmed = true;

        // Phase 2: wait for the player to click the screen clear.
        yield return new WaitUntil(() => _clicks >= clicksToWake);
        StopCoroutine(wakeLoop);
        if (_prompt != null) _prompt.gameObject.SetActive(false);
        _targetAlpha = 0f;
        yield return new WaitUntil(() => _currentAlpha <= 0.001f);
        if (HALLineHUD.Instance != null) HALLineHUD.Instance.ClearAll();   // drop any lingering "Wake up"

        // Scene is now visible — begin coming to (grogginess recovers through the briefing).
        _grogTarget = 0f;

        // Phase 3: briefing.
        yield return Speak(Line01);
        yield return Speak(Line02);
        yield return Speak(Line03);
        yield return Speak(Line04);

        // Phase 4: the body reacts — heartbeat fades in, held silence on the photo.
        StartHeartbeat();
        yield return new WaitForSecondsRealtime(photoBeatSilence);

        // Phase 5: vitals re-read + reassurance.
        yield return Speak(Line05);
        yield return Speak(Line06);
        yield return Speak(Line07);

        // Phase 6: hand off to survival.
        TutorialGate.UnlockAll();
        // Vision fully clears exactly as control returns; remove the effect.
        _grogTarget = _grogIntensity = 0f;
        if (_grog != null) { _grog.intensity = 0f; Destroy(_grog); _grog = null; }
        StartCoroutine(ReleaseFirstContact());
        yield return FadeHeartbeat(heartbeatTargetVolume * 0.25f, heartbeatFadeOut);
        _running = false;
    }

    // Attaches the standalone grogginess post effect to the gameplay camera for
    // the wake-up. Added at runtime so it appends AFTER the existing post-process
    // stack (CustomPostProcessing) — it only ever processes the final image and
    // touches none of the planet/atmosphere effects. Removed at handoff.
    void AttachGrogginess()
    {
        if (grogginessMaterial == null) return;
        var cam = Camera.main;
        if (cam == null) return;
        _grog = cam.gameObject.AddComponent<GrogginessImageEffect>();
        _grog.material = grogginessMaterial;
        _grogIntensity = _grogTarget = 1f;
        _grog.intensity = 1f;
    }

    // The "Incoming transmission" first-contact beat (red HAL line + phone flash +
    // first-open nag) was held for the whole intro; fire it a minute after control
    // returns so it lands once the player has settled, not over the wake-up lines.
    IEnumerator ReleaseFirstContact()
    {
        yield return new WaitForSecondsRealtime(phoneNagDelay);
        PlayerPhoneUI.SuppressFirstNag = false;
        StoryDirector.HoldColdOpen = false;
        if (StoryDirector.Instance != null) StoryDirector.Instance.TriggerFirstContact();
        else if (PlayerPhoneUI.Instance != null) PlayerPhoneUI.Instance.RequestFirstOpenNag();
    }

    IEnumerator WakeUpLoop()
    {
        while (true)
        {
            SpeakRaw(LineWakeUp);
            // Wait for this instance to finish + fade (HUD clears _activeKey),
            // so the next send isn't deduped, then a small gap.
            yield return null;
            yield return new WaitWhile(() => HALLineHUD.Instance != null && !HALLineHUD.Instance.IsIdle);
            yield return new WaitForSecondsRealtime(wakeLoopInterval);
        }
    }

    // Send a line and wait until the HUD has fully shown + faded it. The HUD
    // holds a voiced line for its narration length and a voiceless line for the
    // default read time, so this paces correctly whether or not a clip exists.
    IEnumerator Speak(string line)
    {
        SpeakRaw(line);
        yield return null;   // let Enqueue start ProcessQueue (sets IsIdle false)
        yield return new WaitWhile(() => HALLineHUD.Instance != null && !HALLineHUD.Instance.IsIdle);
        yield return new WaitForSecondsRealtime(interLineGap);
    }

    void SpeakRaw(string line)
    {
        if (HALCommentator.Instance != null) HALCommentator.Instance.VolunteerExternal(line, true);
        else if (HALLineHUD.Instance != null) HALLineHUD.Instance.Show(line);   // fallback: text only
    }

    // ── Audio ──────────────────────────────────────────────────────────────
    void StartRoomTone()
    {
        _roomTone = gameObject.AddComponent<AudioSource>();
        _roomTone.clip = roomToneClip;
        _roomTone.loop = true;
        _roomTone.spatialBlend = 0f;
        _roomTone.volume = roomToneVolume;
        _roomTone.playOnAwake = false;
        _roomTone.Play();
    }

    void StartHeartbeat()
    {
        if (heartbeatClip == null) return;
        _heartbeat = gameObject.AddComponent<AudioSource>();
        _heartbeat.clip = heartbeatClip;
        _heartbeat.loop = true;
        _heartbeat.spatialBlend = 0f;
        _heartbeat.volume = 0f;
        _heartbeat.playOnAwake = false;
        _heartbeat.Play();
        StartCoroutine(FadeHeartbeat(heartbeatTargetVolume, heartbeatFadeIn));
    }

    IEnumerator FadeHeartbeat(float target, float seconds)
    {
        if (_heartbeat == null) yield break;
        float from = _heartbeat.volume, t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            _heartbeat.volume = Mathf.Lerp(from, target, seconds > 0f ? t / seconds : 1f);
            yield return null;
        }
        _heartbeat.volume = target;
    }

    IEnumerator FadeOutAndCleanup()
    {
        // Used when we abort (flag already set): clear the black we put up in Awake.
        _targetAlpha = 0f;
        yield return new WaitUntil(() => _currentAlpha <= 0.001f);
        if (_canvas != null) Destroy(_canvas.gameObject);
    }
}
