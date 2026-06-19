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
    const string Line07 = "The alien left a note for you on the table. Try walking to it and give it a read.";

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

    [Header("Grogginess hold / look")]
    [SerializeField] float grogTalkFloor  = 0.45f;    // woozy level held through the briefing (never fully clears early)
    [SerializeField] float grogHandoffFade = 3f;      // seconds to clear the residual woozy as control returns
    [SerializeField] Transform lookTarget;            // photo prop — gaze is held here until control returns

    [Header("Wake-up gaze pan")]
    [SerializeField] float lookTurnDelay = 2f;        // beat after the eyes open before the head starts turning to the photo
    [SerializeField] float lookPanSpeed  = 20f;       // deg/sec the head turns onto the photo — slow (groggy)

    [Header("Eyelids (click-to-open wake)")]
    [SerializeField] float lidTopClosed   = 0.56f;    // fraction of screen the UPPER lid covers when shut
    [SerializeField] float lidBottomClosed = 0.50f;   // fraction the LOWER lid covers when shut (they overlap when closed)
    [SerializeField] float lidOpenOvershoot = 0.06f;  // how far past the edge each lid retracts when fully open
    [SerializeField] float veilStartAlpha = 1f;       // full blackout at rest (also hides the soft lid seam); lifts to 0 as the eyes open → vision brightens per click
    [SerializeField] float lidFeather     = 0.30f;    // soft-edge fraction of each lid (0 = hard line)
    [SerializeField] float woozeAmp       = 0.022f;   // idle lid drift amplitude (screen fraction) — the "woozy" tremble
    [SerializeField] float woozeSpeed     = 1.6f;     // idle lid drift speed (rad/sec)

    [Header("Staged control handoff")]
    [SerializeField] float groggyMoveScale = 0.5f;    // walk speed after the first (post-Line03) unlock, before the final line

    [Header("Heartbeat spike (vitals irregular)")]
    [SerializeField] float heartbeatFastPitch    = 1.4f; // elevated beat SPEED at the spike — we pitch up the SAME clip (no separate fast loop)
    [SerializeField] float heartbeatSpeedUpTime  = 2.5f; // seconds to ramp from the calm beat up to the elevated rate
    [SerializeField] float heartbeatEaseDelay    = 5f;   // seconds into the "returned home" line before it eases back
    [SerializeField] float heartbeatEaseTime     = 5f;   // seconds to ease back down to the calm beat

    [Header("Double-vision breathing")]
    [SerializeField] float breatheGrogMax   = 2f;   // peak multiplier of the base woozy level (2 = twice as intense)
    [SerializeField] float breatheGrogSpeed = 1.1f; // breathing rhythm (rad/sec) — ~5.7s per worse→better→worse cycle

    // True while the wake-up is running so the camera FX modules (strafe tilt,
    // sprint FOV kick) stay muted during the groggy first steps. Static so those
    // modules can read it without a reference. Cleared at full handoff + OnDestroy.
    public static bool SuppressGroggyCameraFx;

    // ── Runtime ────────────────────────────────────────────────────────────
    Canvas _canvas;
    Image _veil;          // full-screen blackout behind the lids; lifts to clear as the eyes open
    Image _topLid;        // upper eyelid (retracts up)
    Image _botLid;        // lower eyelid (retracts down)
    TextMeshProUGUI _prompt;
    float _openness;        // 0 = eyes shut, 1 = fully open (smoothed)
    float _opennessTarget;  // stepped up by clicksToWake clicks
    int _clicks;
    bool _clicksArmed;
    bool _running;
    AudioSource _heartbeat;       // the heartbeat (pitched up for the vitals spike, eased back after)
    Coroutine _heartbeatXfade;    // active pitch-ramp coroutine
    AudioSource _roomTone;
    GrogginessImageEffect _grog;
    float _grogIntensity = 1f;
    float _grogTarget = 1f;
    bool _grogHandoff;
    PlayerController _pc;
    bool _forceLook;

    void Awake()
    {
        // Load path positions the player from save data and must not see a black
        // flash or a replayed intro. PendingLoad.Data is set before scene load,
        // so this is reliable in Awake.
        if (PendingLoad.Data != null) { enabled = false; return; }

        BuildOverlay();          // eyes shut (full blackout) immediately — hides the spawn frame
        if (roomToneClip != null) StartRoomTone();

        // Hold the whole "Incoming transmission" first-contact beat (red HAL line +
        // phone flash + "Press X to open your phone" nag) for the cold open; the intro
        // fires it a minute after control returns (see ReleaseFirstContact).
        PlayerPhoneUI.SuppressFirstNag = true;
        StoryDirector.HoldColdOpen = true;
    }

    void OnDestroy()
    {
        // Never leave the camera-FX mute latched on if the intro is torn down
        // (scene reload / abort) before its normal phase-6 handoff.
        SuppressGroggyCameraFx = false;
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

        // Pod arrival cinematic runs first, under the black overlay. Hide our
        // overlay while it owns the screen (it manages its own fade), then
        // restore it (black, eyes shut) so the wake-up takes over seamlessly.
        if (_podArrival != null)
        {
            if (_canvas != null) _canvas.enabled = false;
            yield return _podArrival.Play();
            if (_canvas != null) _canvas.enabled = true;
            _openness = _opennessTarget = 0f;   // eyes shut again for the wake-up
            ApplyEyelids(0f);
        }

        yield return RunSequence();
    }

    // ── Overlay construction (programmatic; no scene-UI authoring) ─────────
    // Two black eyelid panels (upper + lower) over a dark veil. Closed, the lids
    // overlap and cover the whole screen; each click retracts the upper lid up and
    // the lower lid down, prising the eyes open from the middle outward while the
    // veil lifts. Between clicks the lids drift slightly (the "woozy" tremble).
    void BuildOverlay()
    {
        var go = new GameObject("IntroBlackOverlay");
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 32760;                 // above everything
        go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        // Veil (drawn first → behind the lids): full blackout at rest that lifts as
        // the eyes open, so the gap brightens per click and the soft lid seam never
        // shows the scene through before the first click.
        _veil = MakeFullScreenImage(go.transform, "Veil", null);
        _veil.color = new Color(0f, 0f, 0f, veilStartAlpha);

        // Eyelids — soft-edged so the inner (lash) edge feathers instead of a hard
        // line. Top lid is opaque at the screen top; bottom lid opaque at the bottom.
        _topLid = MakeFullScreenImage(go.transform, "TopLid", MakeLidSprite(true));
        _botLid = MakeFullScreenImage(go.transform, "BottomLid", MakeLidSprite(false));
        _topLid.color = Color.black;
        _botLid.color = Color.black;

        var promptGO = new GameObject("PressLMBPrompt");
        promptGO.transform.SetParent(go.transform, false);   // last child → on top of the lids
        _prompt = promptGO.AddComponent<TextMeshProUGUI>();
        _prompt.text = "Press " + PromptGlyphs.PrimaryFire;   // "LMB" on M&K (RT / R2 on controller)
        _prompt.alignment = TextAlignmentOptions.Center;
        _prompt.fontSize = 42;
        _prompt.color = new Color(1f, 1f, 1f, 0.85f);
        var prt = _prompt.rectTransform;
        prt.anchorMin = new Vector2(0.5f, 0.5f); prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(800f, 120f);
        _prompt.gameObject.SetActive(false);          // shown at wakePromptDelay

        _openness = _opennessTarget = 0f;             // eyes shut
        ApplyEyelids(0f);
    }

    // Creates a full-screen stretched Image child (optionally with a sprite).
    Image MakeFullScreenImage(Transform parent, string name, Sprite sprite)
    {
        var imgGO = new GameObject(name);
        imgGO.transform.SetParent(parent, false);
        var img = imgGO.AddComponent<Image>();
        if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Simple; }
        img.raycastTarget = false;                    // clicks read via Input, not UI
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return img;
    }

    // Builds a 1×N vertical-gradient sprite: opaque at the lid's outer (screen)
    // edge, feathering to transparent over the inner `lidFeather` fraction so the
    // lash line reads as a soft eyelid edge rather than a hard rectangle.
    Sprite MakeLidSprite(bool opaqueAtTop)
    {
        const int h = 64;
        var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float feather = Mathf.Clamp(lidFeather, 0.01f, 0.95f);
        for (int y = 0; y < h; y++)
        {
            float v = y / (float)(h - 1);             // 0 = bottom, 1 = top of the sprite
            float fromOuter = opaqueAtTop ? v : 1f - v; // 1 at the opaque (outer) edge, 0 at the inner edge
            float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fromOuter / feather));
            tex.SetPixel(0, y, new Color(0f, 0f, 0f, a));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f), 100f);
    }

    // Positions both lids for the given openness (0 shut → 1 open), plus an idle
    // woozy drift that fades out as the eyes finish opening.
    void ApplyEyelids(float openness)
    {
        if (_topLid == null || _botLid == null) return;

        float wooze = (woozeAmp > 0f)
            ? (Mathf.Sin(Time.unscaledTime * woozeSpeed) * woozeAmp
               + Mathf.Sin(Time.unscaledTime * woozeSpeed * 2.3f + 1.7f) * woozeAmp * 0.4f) * (1f - openness)
            : 0f;

        // Upper lid drifts more than the lower (it carries the "heavy eyelid" feel).
        float topCover = Mathf.Lerp(lidTopClosed, -lidOpenOvershoot, openness) + wooze;
        float botCover = Mathf.Lerp(lidBottomClosed, -lidOpenOvershoot, openness) + wooze * 0.5f;

        var trt = _topLid.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f - topCover); trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

        var brt = _botLid.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = new Vector2(1f, botCover);
        brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;

        if (_veil != null) _veil.color = new Color(0f, 0f, 0f, Mathf.Lerp(veilStartAlpha, 0f, openness));
    }

    void Update()
    {
        // Ease the eyes toward their per-click open target, then position the lids
        // (with the idle woozy drift). Each click opens over ~unfadePerClick seconds.
        if (_topLid != null && !Mathf.Approximately(_openness, _opennessTarget))
        {
            float perClickRange = clicksToWake > 0 ? 1f / clicksToWake : 1f;
            float step = (unfadePerClick > 0f ? perClickRange * Time.unscaledDeltaTime / unfadePerClick : 1f);
            _openness = Mathf.MoveTowards(_openness, _opennessTarget, step);
        }
        if (_topLid != null) ApplyEyelids(_openness);

        // Count wake clicks only once armed (after the prompt appears). The first
        // click cracks the eyes open in the middle; each further click prises them
        // open another notch. (The gaze turn onto the photo is triggered separately,
        // a beat after the eyes are fully open — see RunSequence.)
        if (_running && _clicksArmed && Input.GetMouseButtonDown(0))
        {
            _clicks++;
            _opennessTarget = Mathf.Clamp01((float)_clicks / clicksToWake);
        }

        // Grogginess eases toward its target (full → woozy floor); the handoff fade
        // owns the final clear, so skip this lerp once that's running. The displayed
        // intensity breathes — pulsing up to breatheGrogMax× the base and back —
        // so the double vision gets worse, then better, then worse on a slow rhythm.
        if (_grog != null && !_grogHandoff)
        {
            _grogIntensity = Mathf.MoveTowards(_grogIntensity, _grogTarget, grogRecoverRate * Time.unscaledDeltaTime);
            _grog.intensity = BreathingGrog(_grogIntensity);
        }

        // Slowly turn the player's head onto the cabin photo (groggy straight pan).
        // The instant it lands, hand look/cursor control back to the player.
        if (_forceLook && lookTarget != null)
        {
            if (_pc == null) _pc = FindObjectOfType<PlayerController>();
            if (_pc != null && _pc.ForceLookAtSmooth(lookTarget.position, lookPanSpeed))
            {
                _forceLook = false;                                 // stop holding the gaze
                TutorialGate.Unlock(TutorialAbility.MouseLook);     // player can look around now
            }
        }
    }

    // ── The sequence ───────────────────────────────────────────────────────
    IEnumerator RunSequence()
    {
        _running = true;
        TutorialGate.LockAll();                       // freeze movement + look
        SuppressGroggyCameraFx = true;                // mute strafe tilt + sprint FOV kick while groggy
        AttachGrogginess();                           // fuzzy + double vision until the player comes to
        if (_pc == null) _pc = FindObjectOfType<PlayerController>();
        // The gaze pan onto the photo begins a beat after the eyes are fully open
        // (see BeginGazeAfterDelay), so the slow turn plays out where it's visible.

        // Phase 1: black + "Wake up" loop. Accept clicks IMMEDIATELY so a player who
        // instinctively clicks early starts opening their eyes right away; the
        // "Press LMB" tip only appears at wakePromptDelay if they haven't woken yet.
        _clicksArmed = true;
        var wakeLoop = StartCoroutine(WakeUpLoop());
        StartCoroutine(ShowWakePromptAfter(wakePromptDelay));

        // Phase 2: wait for the player to click the eyes fully open.
        yield return new WaitUntil(() => _clicks >= clicksToWake);
        StopCoroutine(wakeLoop);
        if (_prompt != null) _prompt.gameObject.SetActive(false);
        _opennessTarget = 1f;
        yield return new WaitUntil(() => _openness >= 0.999f);
        if (HALLineHUD.Instance != null) HALLineHUD.Instance.ClearAll();   // drop any lingering "Wake up"

        // Scene is now visible — ease partway out of the grogginess to a woozy floor
        // and hold it there through the briefing (it stays double/blurry until handoff).
        _grogTarget = grogTalkFloor;

        // A beat after the eyes open, the head slowly turns to find the family photo.
        StartCoroutine(BeginGazeAfterDelay(lookTurnDelay));

        // Phase 3: briefing.
        yield return Speak(Line01);
        yield return Speak(Line02);

        // The realization sets in — the body reacts to "three years". The heart
        // begins CLIMBING here, a line before "vitals irregular", so HAL appears to
        // be monitoring it rise and only then announce it — we pitch up the same
        // beat the player likes rather than switching to a separate fast clip.
        StartHeartbeat();
        if (_heartbeatXfade != null) StopCoroutine(_heartbeatXfade);
        _heartbeatXfade = StartCoroutine(RampHeartbeatPitch(heartbeatFastPitch, heartbeatSpeedUpTime));

        yield return Speak(Line03);

        // Soft handoff: right after "memory loss is expected" the player gets
        // movement + look back — but at a crawl (15%, still groggy) and free of the
        // forced gaze. The rest of the briefing plays as voiceover over their first
        // wobbly steps; the pace steps up to 50% after the reassurance line and to
        // full speed after the final line.
        TutorialGate.Unlock(TutorialAbility.Move);
        TutorialGate.Unlock(TutorialAbility.MouseLook);
        if (_pc != null) _pc.introMoveScale = moveScaleStart;
        _forceLook = false;

        yield return Speak(Line05);   // "Heart rate elevated. Vitals irregular." — already climbing

        // Reassurance lands right after the spike — the heart eases back to normal
        // partway through this line.
        StartCoroutine(EaseHeartbeatBackAfter(heartbeatEaseDelay));
        yield return Speak(Line06);   // "It is normal... you will be returned home."

        // Steadier now that the reassurance has landed — bump the walk pace up to
        // the mid step (50%) for the rest of the briefing.
        if (_pc != null) _pc.introMoveScale = groggyMoveScale;

        // Held silence before the softer reveal that a local took you in.
        yield return new WaitForSecondsRealtime(photoBeatSilence);
        yield return Speak(Line04);   // "While you were unconscious, a local took you in..."

        yield return Speak(Line07);   // "The alien left a note for you on the table..."

        // Phase 6: full handoff to survival — restore full walk speed + everything else.
        if (_pc != null) _pc.introMoveScale = 1f;
        TutorialGate.UnlockAll();
        SuppressGroggyCameraFx = false;                // strafe tilt + sprint FOV kick return
        _forceLook = false;                            // (already released after Line03)
        StartCoroutine(FadeGrogAndRemove());           // residual woozy vision clears over a few seconds
        StartCoroutine(ReleaseFirstContact());
        // The heartbeat carried the whole wake-up; bring it down to a faint beat at
        // the handoff, then fully fade it out + stop it heartbeatStopDelay (15s)
        // after this final line so it doesn't loop under the ambient mix forever.
        if (_heartbeatXfade != null) StopCoroutine(_heartbeatXfade);
        StartCoroutine(FadeHeartbeatOutAfter(heartbeatStopDelay, heartbeatStopFade));
        yield return FadeHeartbeat(heartbeatTargetVolume * 0.25f, heartbeatFadeOut);
        _running = false;
    }

    // Waits a groggy beat after the eyes open, then lets Update slowly turn the
    // head onto the photo (held until control returns after Line03).
    IEnumerator BeginGazeAfterDelay(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        _forceLook = true;
    }

    // Shows the "Press LMB" tip after `delay` — but only if the player hasn't
    // already clicked their eyes open by then. Clicks are accepted from the very
    // start; this is just a hint for players who wait.
    IEnumerator ShowWakePromptAfter(float delay)
    {
        float t = 0f;
        while (t < delay && _clicks < clicksToWake) { t += Time.unscaledDeltaTime; yield return null; }
        if (_clicks < clicksToWake && _prompt != null) _prompt.gameObject.SetActive(true);
    }

    // Base woozy level pulsed up to breatheGrogMax× and back on a slow breathing
    // rhythm (worse → better → worse). As the base eases to 0 at handoff, the
    // whole pulse winds down with it, so the double vision fully clears at the end.
    float BreathingGrog(float baseLevel)
    {
        float s = 0.5f * (1f - Mathf.Cos(Time.unscaledTime * breatheGrogSpeed)); // 0..1
        return Mathf.Clamp01(baseLevel * (1f + s * (breatheGrogMax - 1f)));
    }

    // Clears the residual grogginess from its woozy floor to fully sharp over
    // grogHandoffFade seconds as the player regains control, then removes the effect.
    IEnumerator FadeGrogAndRemove()
    {
        _grogHandoff = true;
        if (_grog == null) yield break;
        float from = _grogIntensity, t = 0f;
        while (t < grogHandoffFade)
        {
            t += Time.unscaledDeltaTime;
            _grogIntensity = Mathf.Lerp(from, 0f, grogHandoffFade > 0f ? t / grogHandoffFade : 1f);
            if (_grog != null) _grog.intensity = BreathingGrog(_grogIntensity);  // keep breathing while it winds down
            yield return null;
        }
        _grogIntensity = 0f;
        if (_grog != null) { _grog.intensity = 0f; Destroy(_grog); _grog = null; }
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
        _heartbeat.pitch = 1f;        // calm rate; RampHeartbeatPitch raises it for the spike
        _heartbeat.playOnAwake = false;
        _heartbeat.Play();
        StartCoroutine(FadeHeartbeat(heartbeatTargetVolume, heartbeatFadeIn));
    }

    // Ramps the heartbeat's PITCH (beat SPEED) toward targetPitch over `seconds`,
    // keeping the same clip the player likes — higher pitch = a faster, more
    // anxious beat. Replaces the old crossfade to a separate "fast" loop.
    IEnumerator RampHeartbeatPitch(float targetPitch, float seconds)
    {
        if (_heartbeat == null) yield break;
        float from = _heartbeat.pitch;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            if (_heartbeat != null) _heartbeat.pitch = Mathf.Lerp(from, targetPitch, seconds > 0f ? t / seconds : 1f);
            yield return null;
        }
        if (_heartbeat != null) _heartbeat.pitch = targetPitch;
    }

    IEnumerator FadeHeartbeat(float target, float seconds)
    {
        if (_heartbeat == null) yield break;
        float from = _heartbeat.volume;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            float k = seconds > 0f ? t / seconds : 1f;
            _heartbeat.volume = Mathf.Lerp(from, target, k);
            yield return null;
        }
        _heartbeat.volume = target;
    }

    // After `delay` seconds, eases the heartbeat's pitch back down to the calm rate.
    IEnumerator EaseHeartbeatBackAfter(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (_heartbeatXfade != null) StopCoroutine(_heartbeatXfade);
        _heartbeatXfade = StartCoroutine(RampHeartbeatPitch(1f, heartbeatEaseTime));
    }

    // Fully fades the heartbeat out and stops it `delay` seconds after the final
    // briefing line. Without this the faint beat left after the handoff loops under
    // the ambient mix for the rest of the session.
    IEnumerator FadeHeartbeatOutAfter(float delay, float fade)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (_heartbeatXfade != null) { StopCoroutine(_heartbeatXfade); _heartbeatXfade = null; }
        yield return FadeHeartbeat(0f, fade);
        if (_heartbeat != null) { _heartbeat.Stop(); Destroy(_heartbeat); _heartbeat = null; }
    }

    IEnumerator FadeOutAndCleanup()
    {
        // Used when we abort (flag already set): open the eyes we put up in Awake.
        _opennessTarget = 1f;
        yield return new WaitUntil(() => _openness >= 0.999f);
        if (_canvas != null) Destroy(_canvas.gameObject);
    }

    [Header("Pod arrival intro (plays before the wake-up)")]
    [SerializeField] PodArrivalSequence _podArrival;   // optional; if null, the pod intro is skipped

    [Header("Staged move-speed ramp")]
    [SerializeField] float moveScaleStart = 0.15f;     // walk speed at the first unlock, when cursor/look returns (post-Line03). groggyMoveScale (0.5) is the mid step after the reassurance line; 100% comes at the final handoff.

    [Header("Heartbeat fade-out (after the final line)")]
    [SerializeField] float heartbeatStopDelay = 15f;   // seconds after the last briefing line before the heartbeat fully fades out
    [SerializeField] float heartbeatStopFade  = 4f;    // seconds to fade the faint heartbeat down to silence
}
