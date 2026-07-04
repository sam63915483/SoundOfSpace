using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Steins;Gate / Loki-style death cutscene. The player's life history is drawn as
/// a vertical branching "timeline tree" of glowing electric tendrils on a galaxy
/// nebula background, rendered by a dedicated HDR camera with bloom so the lines
/// glow. On death the timeline that just ended sprouts a short RED dead-end stub
/// (a pruned timeline), a new BLUE branch forks off from just below it (a dead
/// branch can't spawn a new one — it forks from the still-living trunk), grows
/// upward, and the camera dives into a spinning wormhole vortex at its tip. Then
/// the player's newest save is reloaded (restoring inventory / ships / position),
/// so they resume as a new clone.
///
/// Auto-singleton (mirrors SpaceDustInventory). Skips MainMenu and is seeded in
/// MainMenuController.EnsureGameplaySingletons (the MainMenu singleton trap). The
/// whole tree is reconstructed from ResourceManager.totalDeaths — no new save
/// state. OnDeath fires AFTER totalDeaths++, so at cutscene time totalDeaths == the
/// number of dead branches (red stubs), and the reborn timeline is totalDeaths+1.
///
/// Everything is built procedurally at a far world offset on a dedicated layer and
/// shown by its own camera over the game — it never touches the gameplay scene or
/// the forbidden atmosphere/planet code. Bloom reuses the project's Kino BloomEffect
/// (read-only use; not modified).
/// </summary>
public class DeathCutsceneController : MonoBehaviour
{
    public static DeathCutsceneController Instance { get; private set; }

    // ───────────────────────── TUNABLES (edit to taste) ─────────────────────────
    [Header("Pacing (seconds, unscaled)")]
    public float deathTransitionTime = 2.2f; // on-death lead-in: collapse + fade-out BEFORE the cutscene
    public float fadeInTime  = 1.0f;   // reveal from black, framed on the red death tip
    public float zoomOutTime = 3.0f;   // zoom out from the red tip where you died
    public float panDownTime = 2.6f;   // travel back down the branch to the fork
    public float branchTime  = 4.0f;   // the new branch grows; camera pans up it
    public float newLabelTime = 1.8f;  // hold while the new "Astronaut N+1" label appears
    public float vortexTime  = 5.0f;   // dive into the wormhole (matches the 5s vortex-dive SFX)
    public float fadeOutTime = 1.6f;   // blue→clear over the reloaded cabin

    [Header("Tree shape (world units)")]
    public float rise   = 2.3f;   // vertical gap between fork nodes
    public float wander = 1.0f;   // horizontal wander of the trunk
    public float stubLen = 2.4f;  // length of a red dead-end branch
    public int   maxVisibleBranches = 12;

    [Header("Branch thickness")]
    public float trunkWidth  = 0.55f;  // main surviving timeline — thickest, most readable
    public float branchWidth = 0.48f;  // the reborn timeline (hero)
    public float deathWidth  = 0.42f;  // red dead-end branches — clearly readable
    public float canopyWidth = 0.17f;  // recursive sub-branches (thinner, for density)

    [Header("Look (halo colours; bright core is derived). Glow is baked into the")]
    [Header("strands — NO post-process bloom — so it looks identical in builds.")]
    public Color colTrunk  = new Color(0.28f, 0.55f, 1.0f);   // blue
    public Color colAlive  = new Color(0.40f, 0.78f, 1.0f);   // bright blue (hero)
    public Color colDead   = new Color(1.0f, 0.13f, 0.15f);   // red (dead branches)
    public Color colVortex = new Color(0.45f, 0.85f, 1.0f);   // vortex blue
    [Range(1.5f, 4f)] public float haloWidthMul = 2.6f;       // halo width vs core
    [Range(0.2f, 1f)] public float haloAlpha = 0.5f;
    // ─────────────────────────────────────────────────────────────────────────────

    const string GameplayScene = "1.6.7.7.7";
    const int    StageLayer = 31;                 // dedicated render layer
    static readonly Vector3 StageOrigin = new Vector3(0f, 100000f, 0f); // far from the world

    static int _deathsToReassert = -1;  // live count carried across the save-reload

    bool _handlingDeath, _skipRequested;
    ResourceManager _subscribedRM;   // the RM instance we're currently subscribed to
    float _savedListenerVolume = -1f; // master volume saved while we mute for the cutscene

    // Cutscene audio. The game is muted via AudioListener.volume = 0 for the duration,
    // so these sources set ignoreListenerVolume = true to stay audible through the mute.
    // Clips live on the scene-placed DeathCutsceneAudio (no inspector exists on this
    // code-built singleton). Resolved once per death in PlayThenReload.
    DeathCutsceneAudio _audio;
    AudioSource _ambSource;  // looped ambience bed
    AudioSource _sfxSource;  // one-shot beats

    // Rendering
    Camera _cam;
    Transform _stage;          // root of all timeline geometry (at StageOrigin)
    RenderTexture _rt;         // cutscene cam renders here so it sits ABOVE game UI
    Canvas _canvas;            // overlay: stage RT + fade + skip hint
    RawImage _stageImage;      // shows _rt full-screen
    CanvasGroup _rootGroup;
    Image _fade;               // full-screen wipe (black in, blue out, clear at cabin)
    Image _barTop, _barBottom; // cinematic letterbox
    static Material _addMat;   // additive line/vortex material
    static Material _particleMat; // additive particle material (radial sprite)
    static Texture2D _glowTex, _nebulaTex, _lineTex;

    // Animatable pieces
    LineRenderer[] _newBranchLines; // the reborn timeline (halo + core, grows)
    Vector3[] _newBranchPts;
    Vector3[] _newRedStubPts;
    Transform _growthSpark;         // bright spark leading the emerging new branch
    Transform _deathOrb;            // orb at the death tip (flashes at the open)
    ParticleSystem _emberPS;        // embers off the dying branch (burst at the open)
    Transform _vortex;
    readonly List<(TMP_Text tmp, LineRenderer leader)> _deadLabels = new List<(TMP_Text, LineRenderer)>();
    TMP_Text _newLabelText; LineRenderer _newLabelLeader; // "Astronaut N+1" on the reborn branch
    readonly List<LineRenderer> _crackle = new List<LineRenderer>(); // live lines that jitter
    readonly List<Vector3[]> _crackleBase = new List<Vector3[]>();
    readonly List<Material> _ownedMats = new List<Material>();       // per-orb mats to free on teardown

    // Camera keyframes
    Vector3 _camDeathTip, _camZoomOut, _camDown, _camUpTip, _camDiveEnd;
    Vector3 _topNode, _newTip, _deathTip;

    // End-transition: async background reload + dive/buildup polish.
    AsyncOperation _reloadOp;     // gameplay scene loading in the background during the dive
    SaveData _reloadData;         // newest save, read at dive start, applied on activation
    float _camRoll;               // accelerating barrel-roll through the dive + buildup
    float _buildupCharge;         // 0→1 over the wake buildup; intensifies the vortex (read in Update)
    float _vortexSpin;            // accumulated spin angle so spin speed can ramp smoothly
    ParticleSystem _vortexSwirl;  // swirl PS, emission ramped during the buildup
    Transform _vortexCore;        // bright throat-core bloom; grows with _buildupCharge + pulses

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("DeathCutsceneController");
        DontDestroyOnLoad(go);
        go.AddComponent<DeathCutsceneController>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        ResourceManager.LegacyRespawnSuppressed = () => _handlingDeath;
        SceneManager.sceneLoaded += OnSceneLoadedResub;
        StartCoroutine(EnsureSubscribed());
    }

    void OnDestroy()
    {
        RestoreAudio(); // never leave the game muted if we're torn down mid-cutscene
        if (Instance == this)
        {
            Instance = null;
            ResourceManager.LegacyRespawnSuppressed = null;
            SceneManager.sceneLoaded -= OnSceneLoadedResub;
        }
    }

    void OnSceneLoadedResub(Scene s, LoadSceneMode m)
    {
        if (s.name == "MainMenu") return;
        StartCoroutine(EnsureSubscribed());
    }

    // ResourceManager is SCENE-PLACED — it's destroyed and recreated on every
    // gameplay scene load (our death-reload, a save load, a portal return). So we
    // must (re-)subscribe to the NEW instance each time, or only the first death
    // ever triggers the cutscene and every later death falls through to the legacy
    // in-place respawn.
    IEnumerator EnsureSubscribed()
    {
        float t = 0f;
        while (ResourceManager.Instance == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
        var rm = ResourceManager.Instance;
        if (rm != null && rm != _subscribedRM)
        {
            rm.OnDeath += HandleDeath;
            _subscribedRM = rm;
        }
    }

    void Update()
    {
        if (_handlingDeath && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape)
            || TutorialGate.PadPressed(TutorialGate.PadButton.A) || TutorialGate.PadPressed(TutorialGate.PadButton.Start)))
            _skipRequested = true;

        // Per-frame electric crackle on the living lines.
        if (_handlingDeath && _crackle.Count > 0)
        {
            float t = Time.unscaledTime;
            for (int i = 0; i < _crackle.Count; i++)
            {
                var lr = _crackle[i]; var basePts = _crackleBase[i];
                if (lr == null || basePts == null) continue;
                int n = lr.positionCount;
                for (int k = 0; k < n && k < basePts.Length; k++)
                {
                    if (k == 0 || k == basePts.Length - 1) { lr.SetPosition(k, basePts[k]); continue; }
                    float j = Mathf.Sin(t * 13f + k * 1.7f + i * 4f) * 0.05f
                            + Mathf.Sin(t * 31f + k * 0.6f) * 0.03f;
                    lr.SetPosition(k, basePts[k] + new Vector3(j, 0f, j * 0.5f));
                }
            }
        }
        if (_vortex != null)
        {
            // Accumulate so the buildup can ramp spin speed without the angle snapping.
            _vortexSpin += Time.unscaledDeltaTime * (80f + _buildupCharge * 520f);
            _vortex.localRotation = Quaternion.Euler(0f, 0f, _vortexSpin);
        }
        if (_vortexCore != null)
        {
            // Core blooms as the buildup charges, with a live pulse so it never sits static.
            float baseS = 3f + _buildupCharge * 4f;
            _vortexCore.localScale = Vector3.one * baseS * (1f + 0.12f * Mathf.Sin(Time.unscaledTime * 9f));
        }
    }

    // ───────────────────────────── Death entry ──────────────────────────────────

    void HandleDeath()
    {
        if (_handlingDeath) return;
        if (SaveSystem.ListSaves().Count == 0)
        {
            Debug.LogWarning("[DeathCutscene] No save on disk — falling back to in-place respawn.");
            return;
        }
        _handlingDeath = true;
        _skipRequested = false;
        _deathsToReassert = ResourceManager.Instance != null ? ResourceManager.Instance.TotalDeaths : -1;
        StartCoroutine(DeathSequence());
    }

    // A short on-death beat BEFORE the timeline cutscene: the player's view tips over
    // (the CombatFX death tilt, already firing on OnDeath), the live scene audio fades
    // out, and the screen fades to black — then the cutscene begins from that black.
    IEnumerator DeathSequence()
    {
        yield return DeathIntro();
        yield return PlayThenReload();
    }

    IEnumerator DeathIntro()
    {
        // Capture the true master volume now, before we fade it, so the cutscene's mute
        // and the eventual RestoreAudio use the real pre-death level (not 0).
        _savedListenerVolume = AudioListener.volume;

        // Build the overlay the cutscene will REUSE; start fully transparent so the
        // player watches their view tip over before the black creeps in.
        BuildOverlayCanvasOnly();
        SetBars(0f);
        if (_fade != null) _fade.color = new Color(0f, 0f, 0f, 0f);

        float v0 = _savedListenerVolume;
        yield return Anim(deathTransitionTime, p =>
        {
            // Hold clear + full audio while the collapse plays, then fade the sound out
            // and the screen to black over the back half ("fall over, THEN fade").
            float fade = Mathf.Clamp01((p - 0.4f) / 0.6f);
            AudioListener.volume = Mathf.Lerp(v0, 0f, fade);
            if (_fade != null) _fade.color = new Color(0f, 0f, 0f, EaseInOut(fade));
        });
        AudioListener.volume = 0f;
        if (_fade != null) _fade.color = Color.black;
    }

    IEnumerator PlayThenReload()
    {
        int deadCount = Mathf.Max(1, _deathsToReassert); // # red dead-ends to show
        _camRoll = 0f; _buildupCharge = 0f; _vortexSpin = 0f;
        _reloadOp = null; _reloadData = null;

        // Silence all game audio (enemies, concert, ambience) for the duration of the
        // cutscene — the game keeps running behind the overlay. Restored when control
        // returns. (When cutscene audio is added later, play it on a path that bypasses
        // this global mute, or duck the game's mixer group instead.)
        // DeathIntro already captured the real volume and faded it out — don't clobber it.
        if (_savedListenerVolume < 0f) _savedListenerVolume = AudioListener.volume;
        AudioListener.volume = 0f;

        // Cutscene audio plays through the mute via ignoreListenerVolume. Ambience
        // bed starts now and loops under the whole sequence.
        BeginCutsceneAudio();

        BuildOverlay();
        BuildStageAndCamera();
        BuildTree(deadCount);

        // The procedural build above is a heavy one-frame hitch. Flush it with a single
        // black frame (the overlay is already opaque black) so its giant deltaTime isn't
        // absorbed by the fade-in's first step — otherwise the fade jumps in almost
        // instantly. After this, the fade-in below runs on clean frame times.
        FrameCamera(_camDeathTip);
        yield return null;

        // Open framed tight on the RED tip where this life ended (already present).
        // Punctuate "you died here": a burst of embers + the death orb flaring down.
        FrameCamera(_camDeathTip);
        if (_emberPS != null) _emberPS.Emit(45);
        PlaySfx(_audio != null ? _audio.deathImpact : null);
        yield return Anim(fadeInTime, p =>
        {
            var c = _fade.color; c.a = Mathf.Lerp(1f, 0f, p); _fade.color = c;
            SetBars(Mathf.Lerp(0f, 100f, EaseOut(p)));
            if (_deathOrb != null) _deathOrb.localScale = Vector3.one * Mathf.Lerp(2.6f, 0.8f, EaseOut(p));
            FrameCamera(_camDeathTip);
        });

        // Zoom out from the death tip to reveal the branch + tree; the dead timelines'
        // "ASTRONAUT i" labels fade in with leader lines to each red tip.
        PlaySfx(_audio != null ? _audio.revealSwell : null);
        PlaySfx(_audio != null ? _audio.labelBlip : null);
        yield return Anim(zoomOutTime, p =>
        {
            FrameCamera(Vector3.Lerp(_camDeathTip, _camZoomOut, EaseInOut(p)));
            float a = Mathf.Clamp01(p * 1.4f);
            foreach (var l in _deadLabels) SetLabelAlpha(l.tmp, l.leader, a);
        });
        foreach (var l in _deadLabels) SetLabelAlpha(l.tmp, l.leader, 1f);

        // Travel back down the branch to the fork point on the living trunk.
        yield return Anim(panDownTime, p => FrameCamera(Vector3.Lerp(_camZoomOut, _camDown, EaseInOut(p))));

        // The new timeline grows up from the fork; a bright spark leads its tip and
        // the camera pans up following it.
        if (_growthSpark != null) _growthSpark.gameObject.SetActive(true);
        PlaySfx(_audio != null ? _audio.branchGrowth : null);
        yield return Anim(branchTime, p =>
        {
            float e = EaseInOut(p);
            RevealLines(_newBranchLines, _newBranchPts, e);
            FrameCamera(Vector3.Lerp(_camDown, _camUpTip, e));
            if (_growthSpark != null && _newBranchPts != null && _newBranchPts.Length > 0)
            {
                int idx = Mathf.Clamp(Mathf.CeilToInt(e * (_newBranchPts.Length - 1)), 0, _newBranchPts.Length - 1);
                _growthSpark.localPosition = _newBranchPts[idx];
                _growthSpark.localScale = Vector3.one * (1.1f * (1f + 0.3f * Mathf.Sin(Time.unscaledTime * 14f)));
            }
        });
        RevealLines(_newBranchLines, _newBranchPts, 1f);
        if (_growthSpark != null) _growthSpark.gameObject.SetActive(false);

        // The reborn timeline's "ASTRONAUT N+1" label emerges ONLY after the branch
        // has fully grown — then hold so the player can read it.
        PlaySfx(_audio != null ? _audio.rebirthChime : null);
        yield return Anim(newLabelTime, p =>
        {
            SetLabelAlpha(_newLabelText, _newLabelLeader, EaseOut(p));
            FrameCamera(_camUpTip);
        });

        // Vortex forms at the new tip; dive in (live, with an accelerating barrel roll).
        // The next scene streams in IN THE BACKGROUND during the dive so the load
        // overlaps the animation instead of freezing between the cutscene and the cabin.
        BuildVortex(_newTip);
        BeginReloadLoad();
        PlaySfx(_audio != null ? _audio.vortexDive : null);
        yield return Anim(vortexTime, p =>
        {
            float e = EaseIn(p);
            FrameCamera(Vector3.Lerp(_camUpTip, _camDiveEnd, e));
            _camRoll = Mathf.Lerp(0f, 60f, e);
            if (_vortex != null) _vortex.localScale = Vector3.one * Mathf.Lerp(0.1f, 2.2f, EaseOut(p));
        });

        // Wake-up buildup: the stinger plays while the LIVE vortex INTENSIFIES toward the
        // boom — faster spin (via _buildupCharge), more roll, scaling up, pushing deeper,
        // ramping swirl. Still running in the old scene; the new one is loaded and waiting.
        PlayWakeStinger();
        if (_audio != null && _audio.wakeStinger != null)
        {
            yield return Anim(Mathf.Max(0.01f, _audio.wakeBoomOffset), p =>
            {
                float e = EaseIn(p);
                _buildupCharge = e;
                _camRoll = 60f + e * 200f;
                FrameCamera(Vector3.Lerp(_camDiveEnd, _camDiveEnd + Vector3.forward * 3.2f, e));
                if (_vortex != null) _vortex.localScale = Vector3.one * Mathf.Lerp(2.2f, 3.6f, e);
                if (_vortexSwirl != null) { var em = _vortexSwirl.emission; em.rateOverTime = Mathf.Lerp(260f, 900f, e); }
            });
        }
        _buildupCharge = 1f;

        // BOOM → white flash masks the scene activation + save-apply hitch and gives the
        // boom its punch; then activate the preloaded scene behind the white.
        yield return FlashWhite(0.10f);
        ActivateReload();
    }

    // ──────────────────────────── Reload handoff ────────────────────────────────

    // Start loading the gameplay scene in the BACKGROUND at the dive's start, with
    // activation deferred. By the boom it's sitting ready at 0.9, so the only remaining
    // cost is activation + save-apply — which the white flash hides.
    void BeginReloadLoad()
    {
        var saves = SaveSystem.ListSaves();
        if (saves.Count == 0) return;
        _reloadData = SaveSystem.LoadFromDisk(saves[0].fileName);
        if (_reloadData == null) { Debug.LogError("[DeathCutscene] Newest save failed to load."); return; }
        _reloadOp = SceneManager.LoadSceneAsync(GameplayScene);
        _reloadOp.allowSceneActivation = false; // hold at 0.9 until the boom
    }

    // Fired on the BOOM, behind the white flash. Tears the live vortex off-screen,
    // schedules the save apply, and activates the preloaded scene — any residual
    // activation/apply hitch is hidden by the white, then PostReload fades to the cabin.
    void ActivateReload()
    {
        if (_cam != null) _cam.enabled = false;            // vortex no longer needed (white covers)
        if (_stageImage != null) _stageImage.enabled = false; // so the fade is a clean white→cabin
        if (_ambSource != null) _ambSource.Stop();         // silence the bed before the mute lifts
        Time.timeScale = 1f;

        if (_reloadOp == null || _reloadData == null)
        {
            Debug.LogWarning("[DeathCutscene] No preloaded save to reload — finishing in place.");
            FinishWithoutReload();
            return;
        }

        Debug.Log($"[DeathCutscene] Activating reload (clone #{_deathsToReassert + 1}).");
        PendingLoad.ScheduleLoad(_reloadData);
        SceneManager.sceneLoaded += OnReloadSceneLoaded; // subscribe BEFORE activation fires it
        _reloadOp.allowSceneActivation = true;
    }

    // Quick full-screen white flash on the boom (covers the vortex + the load hitch).
    IEnumerator FlashWhite(float dur)
    {
        if (_fade == null) yield break;
        float a0 = _fade.color.a;
        yield return Anim(dur, p => _fade.color = new Color(1f, 1f, 1f, Mathf.Lerp(a0, 1f, p)));
        _fade.color = Color.white;
    }

    void OnReloadSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MainMenu") return;
        SceneManager.sceneLoaded -= OnReloadSceneLoaded;
        StartCoroutine(PostReload());
    }

    IEnumerator PostReload()
    {
        yield return null;
        yield return new WaitForFixedUpdate();
        yield return null;
        yield return null;   // Start() + SaveLoadRunner.Apply run here, hidden by the white

        if (_deathsToReassert >= 0 && ResourceManager.Instance != null)
            ResourceManager.Instance.SetTotalDeaths(_deathsToReassert);
        // Clear the on-death camera tilt so the reloaded cabin is never left rolled over
        // (the FX may be DontDestroyOnLoad and otherwise lingers until health restores).
        if (CameraEffectsManager.Instance != null && CameraEffectsManager.Instance.TransformFX != null)
            CameraEffectsManager.Instance.TransformFX.ClearDeathTilt();
        PlayerController.isInDialogue = false; // regain control as we wake
        RestoreAudio();                        // live game audio returns from behind the white

        // White → cabin. The vortex stage is already hidden, so this is a clean
        // bright-flash-to-cabin reveal, riding out on the stinger's quiet tail.
        yield return Fade(_rootGroup, 1f, 0f, fadeOutTime);
        TearDown();
    }

    void FinishWithoutReload()
    {
        PlayerController.isInDialogue = false;
        TearDown();
    }

    void RestoreAudio()
    {
        if (_savedListenerVolume >= 0f)
        {
            AudioListener.volume = _savedListenerVolume;
            _savedListenerVolume = -1f;
        }
    }

    // ───────────────────────────── Cutscene audio ───────────────────────────────
    // Two 2D AudioSources on this DontDestroyOnLoad singleton, reused across deaths.
    // ignoreListenerVolume = true so they play THROUGH the AudioListener.volume = 0
    // mute that silences the live game behind the overlay.

    AudioSource MakeCutsceneSource(bool loop)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = 0f;            // 2D — position doesn't matter
        src.ignoreListenerVolume = true;  // bypass the cutscene's global mute
        src.ignoreListenerPause = true;
        return src;
    }

    void BeginCutsceneAudio()
    {
        _audio = DeathCutsceneAudio.Instance;
        if (_audio == null)
        {
            _audio = FindObjectOfType<DeathCutsceneAudio>(); // once per death; not in Update
            if (_audio == null) return; // no clips assigned yet — cutscene is silent, no error
        }
        if (_ambSource == null) _ambSource = MakeCutsceneSource(true);
        if (_sfxSource == null) _sfxSource = MakeCutsceneSource(false);

        if (_audio.ambienceBed != null)
        {
            _ambSource.clip = _audio.ambienceBed;
            _ambSource.volume = _audio.ambienceVolume;
            _ambSource.Play();
        }
    }

    void PlaySfx(AudioClip clip)
    {
        if (_audio == null || clip == null || _sfxSource == null) return;
        _sfxSource.PlayOneShot(clip, _audio.sfxVolume);
    }

    void StopCutsceneAudio()
    {
        if (_ambSource != null) _ambSource.Stop();
        if (_sfxSource != null) _sfxSource.Stop();
    }

    // The end-of-cutscene stinger (buildup → boom → quiet). Played AFTER the reload,
    // over the blue void; PostReload holds for wakeBoomOffset so the boom lands exactly
    // as the cabin is revealed. Re-resolves _audio from the freshly reloaded scene (the
    // old DeathCutsceneAudio was destroyed with the previous scene).
    void PlayWakeStinger()
    {
        _audio = DeathCutsceneAudio.Instance != null
            ? DeathCutsceneAudio.Instance
            : FindObjectOfType<DeathCutsceneAudio>();
        if (_audio == null || _audio.wakeStinger == null) return;
        if (_sfxSource == null) _sfxSource = MakeCutsceneSource(false);
        _sfxSource.PlayOneShot(_audio.wakeStinger, _audio.sfxVolume);
    }

    void TearDown()
    {
        RestoreAudio(); // safety net if control didn't pass through PostReload
        // Stop the looped ambience (prevent bleed into gameplay) but let the one-shot
        // wake stinger's quiet tail ring out naturally over the revealed cabin.
        if (_ambSource != null) _ambSource.Stop();
        _handlingDeath = false; _skipRequested = false;
        _crackle.Clear(); _crackleBase.Clear();
        foreach (var m in _ownedMats) if (m != null) Destroy(m);
        _ownedMats.Clear();
        _newBranchLines = null; _vortex = null; _vortexSwirl = null; _vortexCore = null; _growthSpark = null;
        _deathOrb = null; _emberPS = null; _barTop = null; _barBottom = null;
        _deadLabels.Clear(); _newLabelText = null; _newLabelLeader = null;
        if (_cam != null) { _cam.targetTexture = null; Destroy(_cam.gameObject); }
        if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
        if (_stage != null) Destroy(_stage.gameObject);
        if (_canvas != null) Destroy(_canvas.gameObject);
        _cam = null; _stage = null; _canvas = null; _rootGroup = null; _fade = null; _stageImage = null;
    }

    // ─────────────────────────────── Camera ─────────────────────────────────────

    void FrameCamera(Vector3 camLocalPos)
    {
        if (_cam == null) return;
        // Subtle cinematic sway so the shot feels alive (not locked-off).
        float t = Time.unscaledTime;
        Vector3 sway = new Vector3(Mathf.Sin(t * 0.6f) * 0.12f, Mathf.Cos(t * 0.47f) * 0.10f, 0f);
        _cam.transform.position = StageOrigin + camLocalPos + sway;
        // Look toward +Z, biased to look slightly up the tree, with an optional roll
        // (0 except during the wormhole dive/buildup, where it accelerates).
        _cam.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up)
                                * Quaternion.Euler(0f, 0f, _camRoll);
    }

    void BuildStageAndCamera()
    {
        var stageGO = new GameObject("DeathCutsceneStage");
        DontDestroyOnLoad(stageGO);
        stageGO.transform.position = StageOrigin;
        _stage = stageGO.transform;

        var camGO = new GameObject("DeathCutsceneCam", typeof(Camera));
        DontDestroyOnLoad(camGO);
        _cam = camGO.GetComponent<Camera>();
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.01f, 0.015f, 0.03f, 1f);
        _cam.cullingMask = 1 << StageLayer;
        _cam.depth = 100;                       // render over the game
        _cam.allowHDR = false;
        _cam.fieldOfView = 50f;                 // VERTICAL fov → vertical framing is identical at any aspect
        _cam.nearClipPlane = 0.05f;
        _cam.farClipPlane = 500f;
        // No post-process bloom: the glow is baked into layered additive strands,
        // so the look is IDENTICAL in editor and build, independent of resolution.

        // Render the camera into a RenderTexture shown on the overlay, so the
        // cutscene sits ABOVE all the game's ScreenSpace-Overlay HUD/debug canvases
        // (a plain higher-depth camera renders UNDER overlay UI).
        BuildOverlayCanvasOnly();
        int w = Mathf.Max(256, Screen.width), h = Mathf.Max(256, Screen.height);
        _rt = new RenderTexture(w, h, 24, RenderTextureFormat.Default) { name = "DeathCutsceneRT" };
        _rt.Create();
        _cam.targetTexture = _rt;
        _stageImage = NewRawImage(_canvas.transform, "StageRT");
        Stretch(_stageImage.rectTransform);
        _stageImage.texture = _rt;
        _stageImage.transform.SetAsFirstSibling(); // behind fade + hint
    }

    // ─────────────────────────────── Overlay ────────────────────────────────────

    void BuildOverlay()
    {
        // Fade starts opaque-black so the first frame hides the dying world before
        // the stage camera exists; BuildOverlayCanvasOnly creates the actual canvas.
        BuildOverlayCanvasOnly();
        if (_fade != null) _fade.color = Color.black;
    }

    void BuildOverlayCanvasOnly()
    {
        if (_canvas != null) return;
        var go = new GameObject("DeathCutsceneCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        go.transform.SetParent(transform, false);
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 32760;
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        _rootGroup = go.GetComponent<CanvasGroup>();
        _rootGroup.alpha = 1f;

        // Cinematic letterbox bars (created BEFORE the fade so the blue-out wipe
        // covers them during the dive). Slide in during fade-in via SetBars().
        _barTop = NewImage(_canvas.transform, "BarTop");
        var bt = _barTop.rectTransform;
        bt.anchorMin = new Vector2(0f, 1f); bt.anchorMax = new Vector2(1f, 1f); bt.pivot = new Vector2(0.5f, 1f);
        bt.sizeDelta = Vector2.zero;
        _barTop.color = Color.black;
        _barBottom = NewImage(_canvas.transform, "BarBottom");
        var bb = _barBottom.rectTransform;
        bb.anchorMin = new Vector2(0f, 0f); bb.anchorMax = new Vector2(1f, 0f); bb.pivot = new Vector2(0.5f, 0f);
        bb.sizeDelta = Vector2.zero;
        _barBottom.color = Color.black;

        _fade = NewImage(_canvas.transform, "Fade");
        Stretch(_fade.rectTransform);
        _fade.color = Color.black;
        _fade.raycastTarget = true;

        var hint = NewText(_canvas.transform, "SkipHint", "[ space / esc ] skip", 22, new Color(1, 1, 1, 0.4f));
        var hrt = hint.rectTransform;
        hrt.anchorMin = hrt.anchorMax = new Vector2(1f, 0f);
        hrt.pivot = new Vector2(1f, 0f);
        hrt.anchoredPosition = new Vector2(-34, 122); // above the bottom letterbox bar
        hrt.sizeDelta = new Vector2(340, 40);
    }

    void SetBars(float h)
    {
        if (_barTop != null) _barTop.rectTransform.sizeDelta = new Vector2(0f, h);
        if (_barBottom != null) _barBottom.rectTransform.sizeDelta = new Vector2(0f, h);
    }

    // ─────────────────────────── Tree construction ──────────────────────────────

    Vector3 NodeLocal(int i)
    {
        float x = 0f, y = 0f;
        for (int k = 1; k <= i; k++) { y += rise; x += Mathf.Sin(k * 1.7f) * wander; }
        return new Vector3(x, y, 0f);
    }

    void BuildTree(int deadCount)
    {
        // Nebula backdrop + stars (far behind the tree).
        BuildBackdrop();

        int firstNode = Mathf.Max(1, deadCount - maxVisibleBranches + 1);
        Color canopyTrunk = new Color(colTrunk.r, colTrunk.g, colTrunk.b, 0.5f);
        Color canopyDead  = new Color(colDead.r, colDead.g, colDead.b, 0.5f);

        // One continuous, THICK glowing blue trunk (halo + bright core) through every
        // fork node — the surviving timeline — with a recursive sub-branch canopy.
        var trunkCtrl = new List<Vector3>();
        for (int i = firstNode - 1; i <= deadCount; i++) trunkCtrl.Add(NodeLocal(i));
        GlowTendril(trunkCtrl, colTrunk, trunkWidth, 0.14f, 7, true, out var trunkPts);
        SproutCanopy(trunkPts, canopyTrunk, 4, 1.4f, canopyWidth, 3, 7);

        // Past deaths: thick RED dead-end branches + a red orb + an "ASTRONAUT i"
        // label (the astronaut who died on that timeline) with a leader to its tip.
        Color labelDead = new Color(1f, 0.55f, 0.55f, 1f);
        for (int i = firstNode; i < deadCount; i++)
        {
            RedBranch(i, out var sp);
            var t = sp[sp.Length - 1];
            SproutCanopy(sp, canopyDead, 2, 0.6f, canopyWidth * 0.7f, 1, 300 + i);
            GlowOrb(t, 0.55f, new Color(1f, 0.2f, 0.22f, 0.95f));
            _deadLabels.Add(MakeLabel(t, (i % 2 == 0) ? 1f : -1f, $"ASTRONAUT {i}", labelDead, colDead));
        }

        // This death's RED dead-end is ALREADY present (camera opens on its tip),
        // punctuated with a bright red orb so "where you died" is unmistakable.
        _topNode = NodeLocal(deadCount);
        RedBranch(deadCount, out _newRedStubPts);
        SproutCanopy(_newRedStubPts, canopyDead, 2, 0.6f, canopyWidth * 0.7f, 1, 360);
        _deathTip = _newRedStubPts[_newRedStubPts.Length - 1];
        _deathOrb = GlowOrb(_deathTip, 0.8f, new Color(1f, 0.25f, 0.27f, 1f)).transform;
        _emberPS = BuildEmbers(_deathTip, new Color(1f, 0.3f, 0.3f, 1f)); // this timeline is dying
        _deadLabels.Add(MakeLabel(_deathTip, (deadCount % 2 == 0) ? 1f : -1f, $"ASTRONAUT {deadCount}", labelDead, colDead));

        // The reborn timeline forks from the LIVE trunk top (node D) — never from the
        // red dead-end — and rises further than any before. Grown during branchTime;
        // kept a clean hero strand (no canopy) so it reads clearly as it grows.
        _newTip = _topNode + new Vector3(Mathf.Sin((deadCount + 1) * 1.7f) * wander * 0.7f, rise * 1.7f, 0f);
        var nbCtrl = new List<Vector3>
        {
            _topNode,
            Vector3.Lerp(_topNode, _newTip, 0.5f) + new Vector3(wander * 0.35f, 0f, 0f),
            _newTip
        };
        _newBranchLines = GlowTendril(nbCtrl, colAlive, branchWidth, 0.18f, 99, true, out _newBranchPts);
        RevealLines(_newBranchLines, _newBranchPts, 0f);

        // Bright spark that leads the emerging timeline as it grows (a new one being born).
        _growthSpark = GlowOrb(_topNode, 1.1f, new Color(0.78f, 0.93f, 1f, 1f)).transform;
        _growthSpark.gameObject.SetActive(false);

        // "ASTRONAUT N+1" label for the reborn timeline — hidden until it emerges.
        float nside = (_newTip.x >= _topNode.x) ? 1f : -1f;
        var nl = MakeLabel(_newTip, nside, $"ASTRONAUT {deadCount + 1}", new Color(0.72f, 0.92f, 1f, 1f), colAlive);
        _newLabelText = nl.Item1; _newLabelLeader = nl.Item2;

        // Camera keyframes (local to StageOrigin; camera looks +Z).
        _camDeathTip = new Vector3(_deathTip.x, _deathTip.y, -5.5f);              // on the death tip
        _camZoomOut  = new Vector3(_deathTip.x, _deathTip.y - 1.5f, -13f);        // pulled back, tree context
        _camDown     = new Vector3(_topNode.x, _topNode.y - rise * 0.4f, -12f);   // down at the fork
        _camUpTip    = new Vector3(_newTip.x, _newTip.y, -9f);                    // up at the new tip
        _camDiveEnd  = new Vector3(_newTip.x, _newTip.y, 6f);                     // through the throat
    }

    void RedBranch(int i, out Vector3[] pts)
    {
        Vector3 a = NodeLocal(i);
        float side = (i % 2 == 0) ? 1f : -1f;
        Vector3 b = a + new Vector3(side * stubLen * 0.75f, stubLen * 0.8f, 0f);
        Vector3 mid = Vector3.Lerp(a, b, 0.5f) + new Vector3(side * 0.3f, 0f, 0f);
        GlowTendril(new List<Vector3> { a, mid, b }, colDead, deathWidth, 0.12f, 200 + i, false, out pts);
    }

    // ────────────────────────── Smooth strand builders ──────────────────────────

    // Single glowing strand through the control points (Catmull-Rom + gentle noise).
    // Used for the thin canopy sub-branches.
    LineRenderer Tendril(List<Vector3> ctrl, Color color, float wStart, float wEnd, float noiseAmp, int seed, out Vector3[] pts)
    {
        pts = SmoothPath(ctrl, 12, noiseAmp, seed);
        return MakeLine(pts, color, wStart, wEnd, false);
    }

    // Two-layer glowing strand: a wide soft COLOURED halo + a thin bright near-white
    // CORE. The glow is baked into the geometry (no post-process bloom) so it renders
    // identically in editor and build at any resolution/aspect.
    LineRenderer[] GlowTendril(List<Vector3> ctrl, Color halo, float width, float noiseAmp, int seed, bool crackle, out Vector3[] pts)
    {
        pts = SmoothPath(ctrl, 12, noiseAmp, seed);
        Color core = Color.Lerp(halo, Color.white, 0.72f);
        // wide faint outer glow → mid halo → thin bright core (soft, bloom-like, but baked).
        var g = MakeLine(pts, new Color(halo.r, halo.g, halo.b, haloAlpha * 0.45f), width * haloWidthMul * 1.7f, width * haloWidthMul * 0.9f, false);
        var h = MakeLine(pts, new Color(halo.r, halo.g, halo.b, haloAlpha), width * haloWidthMul, width * haloWidthMul * 0.55f, false);
        var c = MakeLine(pts, new Color(core.r, core.g, core.b, 0.97f), width * 0.7f, width * 0.3f, crackle);
        return new[] { g, h, c };
    }

    LineRenderer MakeLine(Vector3[] pts, Color color, float w0, float w1, bool crackle)
    {
        var go = new GameObject("strand", typeof(LineRenderer));
        go.transform.SetParent(_stage, false);
        go.layer = StageLayer;
        var lr = go.GetComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.material = AdditiveMat();
        lr.textureMode = LineTextureMode.Stretch;
        lr.numCapVertices = 8;
        lr.numCornerVertices = 6;
        lr.alignment = LineAlignment.View;
        lr.widthCurve = new AnimationCurve(new Keyframe(0f, w0), new Keyframe(1f, w1));
        lr.startColor = lr.endColor = color;
        lr.positionCount = pts.Length;
        lr.SetPositions(pts);
        if (crackle) RegisterCrackle(lr);
        return lr;
    }

    void RevealLines(LineRenderer[] lines, Vector3[] pts, float p)
    {
        if (lines == null) return;
        foreach (var lr in lines) SetRevealed(lr, pts, p);
    }

    // Glowing additive orb that marks a death point so it's unmistakable.
    GameObject GlowOrb(Vector3 pos, float size, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(go.GetComponent<Collider>());
        go.name = "orb";
        go.transform.SetParent(_stage, false);
        go.transform.localPosition = pos;
        go.transform.localScale = Vector3.one * size;
        go.layer = StageLayer;
        var mr = go.GetComponent<MeshRenderer>();
        var m = new Material(AdditiveMat());   // own instance so each orb keeps its tint
        m.mainTexture = GlowTex();
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", color);
        m.color = color;
        _ownedMats.Add(m);                     // freed in TearDown (avoid per-death leak)
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go;
    }

    // A world-space "ASTRONAUT N" label offset to `side` (+1 right / -1 left) of a
    // branch tip, with a glowing leader line pointing at the tip. Starts hidden
    // (alpha 0); fade in with SetLabelAlpha.
    (TMP_Text, LineRenderer) MakeLabel(Vector3 tip, float side, string text, Color textCol, Color leaderCol)
    {
        var go = new GameObject("label");
        go.transform.SetParent(_stage, false);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = 5f;
        tmp.color = textCol;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.alpha = 0f;
        tmp.rectTransform.sizeDelta = new Vector2(13f, 3f);
        Vector3 labelPos = tip + new Vector3(side * 3.2f, 0.3f, 0f);
        go.transform.localPosition = labelPos;
        // Face the camera (which looks +Z from -Z): turn 180° around Y so the front
        // faces the camera, then flip X so the text reads correctly (not mirrored).
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        go.transform.localScale = new Vector3(-1f, 1f, 1f);
        go.layer = StageLayer;
        SetLayerRecursive(go, StageLayer);

        Vector3 inner = labelPos + new Vector3(-side * 1.5f, -0.15f, 0f);
        var leader = MakeLine(new[] { inner, tip }, new Color(leaderCol.r, leaderCol.g, leaderCol.b, 0f), 0.06f, 0.06f, false);
        return (tmp, leader);
    }

    void SetLabelAlpha(TMP_Text tmp, LineRenderer leader, float a)
    {
        if (tmp != null) tmp.alpha = a;
        if (leader != null)
        {
            var c = leader.startColor; c.a = a;
            leader.startColor = leader.endColor = c;
        }
    }

    static Vector3[] SmoothPath(List<Vector3> ctrl, int per, float noiseAmp, int seed)
    {
        if (ctrl.Count == 1) return new[] { ctrl[0] };
        var pad = new List<Vector3> { ctrl[0] };
        pad.AddRange(ctrl);
        pad.Add(ctrl[ctrl.Count - 1]);

        var pts = new List<Vector3>();
        for (int i = 1; i < pad.Count - 2; i++)
        {
            Vector3 p0 = pad[i - 1], p1 = pad[i], p2 = pad[i + 1], p3 = pad[i + 2];
            for (int s = 0; s < per; s++) pts.Add(Catmull(p0, p1, p2, p3, s / (float)per));
        }
        pts.Add(pad[pad.Count - 2]);

        // Gentle perpendicular noise (smooth, low-frequency) for an organic, electric feel.
        float off = seed * 13.13f;
        for (int i = 1; i < pts.Count - 1; i++)
        {
            Vector3 dir = pts[i + 1] - pts[i - 1];
            if (dir.sqrMagnitude < 1e-6f) continue;
            Vector3 perp = new Vector3(-dir.y, dir.x, 0f).normalized;
            float n = (Mathf.PerlinNoise(i * 0.16f + off, off) - 0.5f) * 2f;
            float taper = Mathf.Sin((i / (float)pts.Count) * Mathf.PI); // less at ends → clean joins
            pts[i] += perp * n * noiseAmp * taper;
        }
        return pts.ToArray();
    }

    static Vector3 Catmull(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    // Recursive sub-branching for a dense, tree-like canopy. Each sprout spawns a
    // child strand that itself sprouts smaller children, depth levels deep.
    void SproutCanopy(Vector3[] along, Color color, int count, float baseLen, float baseWidth, int depth, int seed)
    {
        if (along == null || along.Length < 3 || depth <= 0) return;
        var rng = new System.Random(seed * 131 + depth * 7);
        for (int c = 0; c < count; c++)
        {
            int idx = rng.Next(1, along.Length - 1);
            Vector3 p = along[idx];
            Vector3 d = along[Mathf.Min(idx + 1, along.Length - 1)] - along[idx - 1];
            if (d.sqrMagnitude < 1e-6f) continue;
            d.Normalize();
            Vector3 perp = new Vector3(-d.y, d.x, 0f).normalized;
            float side = (rng.NextDouble() < 0.5) ? 1f : -1f;
            float ang = 0.45f + (float)rng.NextDouble() * 0.6f;          // splay from the parent
            Vector3 outDir = (perp * side * Mathf.Sin(ang) + d * Mathf.Cos(ang)).normalized;
            float len = baseLen * (0.7f + (float)rng.NextDouble() * 0.6f);
            Vector3 tip = p + outDir * len;
            Vector3 mid = Vector3.Lerp(p, tip, 0.5f) + perp * side * len * 0.16f;
            Tendril(new List<Vector3> { p, mid, tip }, color, baseWidth, baseWidth * 0.4f, 0.08f, seed * 7 + c, out var cpts);
            SproutCanopy(cpts, color, Mathf.Max(1, count - 1), len * 0.62f, baseWidth * 0.6f, depth - 1, seed * 13 + c + 1);
        }
    }

    void RegisterCrackle(LineRenderer lr)
    {
        var pts = new Vector3[lr.positionCount];
        lr.GetPositions(pts);
        _crackle.Add(lr); _crackleBase.Add(pts);
    }

    // Reveal a line progressively (grow from start). pts is the full point set.
    void RevealLine(LineRenderer lr, Vector3[] pts, float p) => SetRevealed(lr, pts, p);
    void SetRevealed(LineRenderer lr, Vector3[] pts, float p)
    {
        if (lr == null || pts == null) return;
        int n = Mathf.Clamp(Mathf.CeilToInt(p * pts.Length), 0, pts.Length);
        lr.positionCount = n;
        for (int i = 0; i < n; i++) lr.SetPosition(i, pts[i]);
    }

    // ───────────────────────────── Vortex / tunnel ──────────────────────────────

    void BuildVortex(Vector3 atLocal)
    {
        var go = new GameObject("Vortex", typeof(RectTransform));
        _vortex = go.transform;
        _vortex.SetParent(_stage, false);
        _vortex.localPosition = atLocal;
        _vortex.localScale = Vector3.one * 0.1f;

        const int rings = 18;       // a deep, dense funnel (was a sparse 9)
        const float depth = 0.8f;   // z-gap between rings
        float throatZ = rings * depth;

        // Colour-graded GLOWING rings (halo + bright core each) forming the funnel — they
        // bloom like the timeline branches instead of reading as thin wireframe loops.
        for (int r = 0; r < rings; r++)
        {
            float f = r / (float)(rings - 1);
            float rad = Mathf.Lerp(2.8f, 0.16f, f);                            // narrow inward
            Color c  = Color.Lerp(colVortex, new Color(0.85f, 0.97f, 1f), f);  // blue → cyan-white
            float a  = Mathf.Lerp(0.85f, 0.30f, f);
            GlowRing(_vortex, r * depth, rad, 44, new Color(c.r, c.g, c.b, a), Mathf.Lerp(0.13f, 0.05f, f));
        }

        // Spinning spiral energy arms twisting down the throat — the galaxy-funnel
        // richness that makes the dive feel like descending into something alive.
        const int arms = 5;
        for (int s = 0; s < arms; s++)
            SpiralArm(_vortex, (s / (float)arms) * Mathf.PI * 2f, throatZ, 2.8f);

        // Bright pulsing core bloom at the throat — the "light at the end" the dive races
        // toward. Grown by _buildupCharge + pulsed in Update so it blooms into the boom.
        _vortexCore = NewGlowQuad(_vortex, new Vector3(0f, 0f, throatZ + 0.4f), 3.0f,
                                  new Color(1.6f, 1.9f, 2.4f, 1f)).transform;
        // Soft volumetric halo behind the core so the throat glows, not just a dot.
        NewGlowQuad(_vortex, new Vector3(0f, 0f, throatZ + 0.7f), 7.0f,
                    new Color(colVortex.r, colVortex.g, colVortex.b, 0.5f));

        // Dense energy spiralling inward toward the core — the wormhole "pull".
        var ps = NewPS("VortexSwirl");
        _vortexSwirl = ps; // emission ramped during the wake buildup
        ps.transform.SetParent(_vortex, false);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 2.2f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.34f);
        main.startColor = new Color(colVortex.r, colVortex.g, colVortex.b, 1f);
        main.maxParticles = 1000;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.useUnscaledTime = true;
        var em = ps.emission; em.rateOverTime = 260f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 2.8f; sh.radiusThickness = 0.55f; sh.arc = 360f;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(3.4f);  // spin around the throat
        vel.radial   = new ParticleSystem.MinMaxCurve(-1.7f); // pull inward
        vel.z        = new ParticleSystem.MinMaxCurve(1.6f);  // stream down toward the core
        FadeOverLife(ps, 0.18f);

        SetLayerRecursive(_vortex.gameObject, StageLayer);
    }

    // Two-layer glowing ring: wide soft halo + thin bright core (matches the branch look).
    void GlowRing(Transform parent, float z, float radius, int seg, Color color, float width)
    {
        Color core = Color.Lerp(color, Color.white, 0.7f);
        Ring(parent, z, radius, seg, new Color(color.r, color.g, color.b, color.a * 0.5f), width * 2.4f);
        Ring(parent, z, radius, seg, new Color(core.r, core.g, core.b, Mathf.Min(1f, color.a + 0.25f)), width);
    }

    void Ring(Transform parent, float z, float radius, int seg, Color color, float width)
    {
        var go = new GameObject("ring", typeof(LineRenderer));
        go.transform.SetParent(parent, false);
        go.layer = StageLayer;
        var lr = go.GetComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.material = AdditiveMat();
        lr.widthMultiplier = width;
        lr.numCapVertices = 2;
        lr.startColor = lr.endColor = color;
        lr.positionCount = seg;
        for (int i = 0; i < seg; i++)
        {
            float t = i / (float)seg * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(t) * radius, Mathf.Sin(t) * radius, z));
        }
    }

    // A glowing spiral arm winding from the funnel mouth inward to the throat (halo +
    // bright core, like the timeline tendrils). Parented to the vortex so it spins with it.
    void SpiralArm(Transform parent, float phase, float throatZ, float startRadius)
    {
        const int n = 64;
        const float turns = 2.3f;
        var pts = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);                       // 0 mouth → 1 throat
            float ang = phase + t * turns * Mathf.PI * 2f;
            float rad = Mathf.Lerp(startRadius, 0.12f, t);
            pts[i] = new Vector3(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad, t * throatZ);
        }
        Color core = Color.Lerp(colVortex, Color.white, 0.7f);
        MakeLine(pts, new Color(colVortex.r, colVortex.g, colVortex.b, 0.55f), 0.22f, 0.04f, false)
            .transform.SetParent(parent, false);
        MakeLine(pts, new Color(core.r, core.g, core.b, 0.95f), 0.09f, 0.02f, false)
            .transform.SetParent(parent, false);
    }

    // View-facing additive glow billboard (like GlowOrb) parented under an arbitrary node.
    GameObject NewGlowQuad(Transform parent, Vector3 localPos, float size, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(go.GetComponent<Collider>());
        go.name = "glow";
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * size;
        go.layer = StageLayer;
        var mr = go.GetComponent<MeshRenderer>();
        var m = new Material(AdditiveMat());
        m.mainTexture = GlowTex();
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", color);
        m.color = color;
        _ownedMats.Add(m);
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go;
    }

    // ─────────────────────────────── Backdrop ───────────────────────────────────

    void BuildBackdrop()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(go.GetComponent<Collider>());
        go.name = "Nebula";
        go.transform.SetParent(_stage, false);
        go.transform.localPosition = new Vector3(0f, NodeLocal(maxVisibleBranches / 2).y, 70f);
        go.transform.localScale = new Vector3(260f, 260f, 1f);
        go.layer = StageLayer;
        var mr = go.GetComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Unlit/Texture"));
        mat.mainTexture = NebulaTex();
        _ownedMats.Add(mat);
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        BuildStars();        // crisp billboard stars (not baked texels)
        BuildEnergyMotes();  // ambient rising energy
    }

    static Texture2D NebulaTex()
    {
        if (_nebulaTex != null) return _nebulaTex;
        const int S = 1024;  // higher-res so it isn't blurry when stretched
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var rng = new System.Random(1234);
        float ox = (float)rng.NextDouble() * 100f, oy = (float)rng.NextDouble() * 100f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S, v = y / (float)S;
                float n  = FBM(u * 3.2f + ox, v * 3.2f + oy, 6);
                float n2 = FBM(u * 6.5f + 40f, v * 6.5f + 12f, 5);
                float n3 = FBM(u * 1.5f + 7f, v * 1.5f + 19f, 4);  // large soft structure
                float cloud = Mathf.Pow(Mathf.Clamp01(n), 1.25f);
                Color c = new Color(0.02f, 0.04f, 0.10f);
                c += new Color(0.16f, 0.28f, 0.66f) * cloud;                             // blue body
                c += new Color(0.42f, 0.10f, 0.55f) * Mathf.Clamp01(n2 - 0.42f) * 2.4f;  // violet wisps
                c += new Color(0.00f, 0.45f, 0.50f) * Mathf.Clamp01(n - 0.5f) * 1.5f;    // teal
                c += new Color(0.10f, 0.16f, 0.40f) * Mathf.Clamp01(n3 - 0.45f) * 1.2f;  // soft depth
                float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f));
                c *= Mathf.Clamp01(1.3f - d * 0.8f);
                tex.SetPixel(x, y, new Color(c.r, c.g, c.b, 1f));
            }
        tex.Apply();
        _nebulaTex = tex;
        return tex;
    }

    // ───────────────────────────── Particles ────────────────────────────────────

    ParticleSystem NewPS(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_stage, false);
        go.layer = StageLayer;
        var ps = go.AddComponent<ParticleSystem>();
        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.material = ParticleMat();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.alignment = ParticleSystemRenderSpace.View;
        return ps;
    }

    // Crisp starfield: real additive billboards (sharp at any resolution).
    void BuildStars()
    {
        var ps = NewPS("Stars");
        var main = ps.main;
        main.loop = true;            // keep the system "alive" so static stars render…
        main.playOnAwake = true;
        main.startSpeed = 0f;
        main.startLifetime = 1e8f;
        main.maxParticles = 2000;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var em = ps.emission; em.enabled = false;   // …but it never auto-emits; we Emit manually

        var rng = new System.Random(777);
        float cy = NodeLocal(maxVisibleBranches / 2).y;
        for (int i = 0; i < 600; i++)
        {
            var ep = new ParticleSystem.EmitParams();
            ep.position = new Vector3((float)(rng.NextDouble() * 2 - 1) * 150f,
                                      cy + (float)(rng.NextDouble() * 2 - 1) * 80f,
                                      25f + (float)rng.NextDouble() * 100f);
            float b = 0.35f + (float)rng.NextDouble() * 0.9f;
            bool big = rng.NextDouble() > 0.93;
            ep.startSize = (big ? 1.0f : 0.28f) * (0.6f + (float)rng.NextDouble() * 0.8f);
            ep.startColor = new Color(b, b, b * 1.05f, 1f);
            ep.startLifetime = 1e8f;
            ps.Emit(ep, 1);
        }
    }

    // Ambient rising blue energy motes around the tree (gentle life/motion).
    void BuildEnergyMotes()
    {
        var ps = NewPS("EnergyMotes");
        var main = ps.main;
        main.startLifetime = 5f;
        main.startSpeed = 0.25f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.22f);
        main.startColor = new Color(0.4f, 0.7f, 1f, 1f);
        main.maxParticles = 400;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.useUnscaledTime = true;
        var em = ps.emission; em.rateOverTime = 28f;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.position = new Vector3(0f, NodeLocal(maxVisibleBranches / 2).y, 0f);
        sh.scale = new Vector3(8f, 24f, 4f);
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.y = new ParticleSystem.MinMaxCurve(0.6f);
        FadeOverLife(ps, 0.25f);
    }

    // Embers peeling off a dying timeline.
    ParticleSystem BuildEmbers(Vector3 pos, Color color)
    {
        var ps = NewPS("Embers");
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.26f);
        main.startColor = color;
        main.maxParticles = 120;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.useUnscaledTime = true;
        ps.transform.localPosition = pos;
        var em = ps.emission; em.rateOverTime = 12f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.y = new ParticleSystem.MinMaxCurve(0.35f);
        FadeOverLife(ps, 0.15f);
        return ps;
    }

    static void FadeOverLife(ParticleSystem ps, float holdIn)
    {
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, holdIn), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sol = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));
    }

    static Material ParticleMat()
    {
        if (_particleMat != null) return _particleMat;
        Shader sh = Shader.Find("Particles/Additive")
                 ?? Shader.Find("Legacy Shaders/Particles/Additive")
                 ?? Shader.Find("Mobile/Particles/Additive")
                 ?? Shader.Find("Sprites/Default");
        _particleMat = new Material(sh);
        if (_particleMat.HasProperty("_TintColor")) _particleMat.SetColor("_TintColor", Color.white);
        _particleMat.mainTexture = GlowTex();
        return _particleMat;
    }

    static float FBM(float x, float y, int oct)
    {
        float v = 0f, amp = 0.5f, freq = 1f;
        for (int i = 0; i < oct; i++)
        {
            v += amp * Mathf.PerlinNoise(x * freq, y * freq);
            freq *= 2f; amp *= 0.5f;
        }
        return v;
    }

    // ──────────────────────── Shared materials / helpers ─────────────────────────

    static Material AdditiveMat()
    {
        if (_addMat != null) return _addMat;
        Shader sh = Shader.Find("Particles/Additive")
                 ?? Shader.Find("Legacy Shaders/Particles/Additive")
                 ?? Shader.Find("Mobile/Particles/Additive")
                 ?? Shader.Find("Sprites/Default");
        _addMat = new Material(sh);
        if (_addMat.HasProperty("_TintColor")) _addMat.SetColor("_TintColor", Color.white);
        _addMat.mainTexture = LineGlowTex();
        return _addMat;
    }

    // Texture for line strands: U (along length) is solid so lines never break;
    // V (across width) has a bright core fading to soft edges → a clean glowing strand.
    static Texture2D LineGlowTex()
    {
        if (_lineTex != null) return _lineTex;
        const int W = 4, H = 64;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < H; y++)
        {
            float v = Mathf.Abs(y - (H - 1) / 2f) / ((H - 1) / 2f); // 0 center → 1 edge
            float a = Mathf.Pow(Mathf.Clamp01(1f - v), 1.5f);       // bright core, soft edge
            for (int x = 0; x < W; x++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        _lineTex = tex;
        return tex;
    }

    static Texture2D GlowTex()
    {
        if (_glowTex != null) return _glowTex;
        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var c = new Vector2(S / 2f, S / 2f);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / (S / 2f);
                float a = Mathf.Clamp01(1f - d); a *= a;
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        tex.Apply();
        _glowTex = tex;
        return tex;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
    }

    Image NewImage(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    RawImage NewRawImage(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<RawImage>();
        img.raycastTarget = false;
        return img;
    }

    Text NewText(Transform parent, string name, string content, int size, Color col)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.text = content; t.fontSize = size; t.color = col;
        t.alignment = TextAnchor.LowerRight; t.raycastTarget = false;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
              ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        return t;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    // ────────────────────────────── Tweening ────────────────────────────────────

    IEnumerator Anim(float dur, System.Action<float> step)
    {
        float t = 0f;
        while (t < dur)
        {
            if (_skipRequested) { step(1f); yield break; }
            step(Mathf.Clamp01(t / dur));
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        step(1f);
    }

    IEnumerator Fade(Graphic g, float from, float to, float dur)
    {
        if (g == null) yield break;
        yield return Anim(dur, p => { var c = g.color; c.a = Mathf.Lerp(from, to, p); g.color = c; });
    }

    IEnumerator Fade(CanvasGroup grp, float from, float to, float dur)
    {
        if (grp == null) yield break;
        yield return Anim(dur, p => grp.alpha = Mathf.Lerp(from, to, p));
    }

    static float EaseIn(float p) => p * p;
    static float EaseOut(float p) => 1f - (1f - p) * (1f - p);
    static float EaseInOut(float p) => p < 0.5f ? 2f * p * p : 1f - Mathf.Pow(-2f * p + 2f, 2f) / 2f;
}
