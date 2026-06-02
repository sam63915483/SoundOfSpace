using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Singleton coordinator for all camera / screen effects. Owns the FX modules
/// as child GameObjects and exposes shared refs (player camera, InputSettings)
/// so individual modules don't each FindObjectOfType.
///
/// Modules poll InputSettings every frame for their enable flag so toggles
/// from the pause menu take effect live.
/// </summary>
public class CameraEffectsManager : MonoBehaviour
{
    public static CameraEffectsManager Instance { get; private set; }

    public Camera PlayerCamera { get; private set; }
    public InputSettings Input { get; private set; }
    public bool MasterEnabled => Input != null && Input.cameraEffectsEnabled;
    public CameraTransformFX TransformFX { get; private set; }
    public CameraFOVFX FOVFX { get; private set; }
    public VignetteOverlay Vignette { get; private set; }
    public DamageFlashOverlay DamageFlash { get; private set; }
    public LetterboxBars Letterbox { get; private set; }
    public SpeedLinesOverlay SpeedLines { get; private set; }
    public FilmGrainOverlay FilmGrain { get; private set; }
    public ChromaticAberrationEffect ChromaticAberration { get; private set; }
    public CombatFX Combat { get; private set; }
    public SlowmoOnKill Slowmo { get; private set; }
    public MoodColorGrade Mood { get; private set; }
    public LensFlareRegistry LensFlares { get; private set; }
    public RadialMotionBlurEffect RadialBlur { get; private set; }

    bool _modulesAttached;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("CameraEffectsManager");
        DontDestroyOnLoad(go);
        go.AddComponent<CameraEffectsManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        TryAcquireRefs();
        AttachModules();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m) { _modulesAttached = false; TryAcquireRefs(); AttachModules(); }

    void AttachModules()
    {
        if (TransformFX == null) TransformFX = gameObject.AddComponent<CameraTransformFX>();
        if (FOVFX == null) FOVFX = gameObject.AddComponent<CameraFOVFX>();
        if (Vignette == null)
        {
            var go = new GameObject("VignetteOverlay");
            go.transform.SetParent(transform, false);
            Vignette = go.AddComponent<VignetteOverlay>();
        }
        if (DamageFlash == null)
        {
            var go = new GameObject("DamageFlashOverlay");
            go.transform.SetParent(transform, false);
            DamageFlash = go.AddComponent<DamageFlashOverlay>();
        }
        if (Letterbox == null)
        {
            var go = new GameObject("LetterboxBars");
            go.transform.SetParent(transform, false);
            Letterbox = go.AddComponent<LetterboxBars>();
        }
        if (SpeedLines == null)
        {
            var go = new GameObject("SpeedLinesOverlay");
            go.transform.SetParent(transform, false);
            SpeedLines = go.AddComponent<SpeedLinesOverlay>();
        }
        if (FilmGrain == null)
        {
            var go = new GameObject("FilmGrainOverlay");
            go.transform.SetParent(transform, false);
            FilmGrain = go.AddComponent<FilmGrainOverlay>();
        }
        // Chromatic aberration is now a real shader-based post-effect on
        // the camera (channel-split via fragment shader), not a UI overlay.
        // Attach in the camera-component block below alongside RadialBlur.
        if (Combat == null) Combat = gameObject.AddComponent<CombatFX>();
        if (Slowmo == null) Slowmo = gameObject.AddComponent<SlowmoOnKill>();
        if (Mood == null)
        {
            var go = new GameObject("MoodColorGrade");
            go.transform.SetParent(transform, false);
            Mood = go.AddComponent<MoodColorGrade>();
        }
        if (LensFlares == null) LensFlares = gameObject.AddComponent<LensFlareRegistry>();
        // Radial motion blur lives on the camera GameObject itself —
        // OnRenderImage only fires for components attached to a camera.
        // Also: re-check each frame because the "active" camera can change
        // across scene transitions (MainMenu → gameplay scene in builds).
        if (PlayerCamera != null)
        {
            if (RadialBlur == null || RadialBlur.gameObject != PlayerCamera.gameObject)
            {
                var existing = PlayerCamera.GetComponent<RadialMotionBlurEffect>();
                if (existing != null)
                {
                    RadialBlur = existing;
                }
                else
                {
                    RadialBlur = PlayerCamera.gameObject.AddComponent<RadialMotionBlurEffect>();
                    Debug.Log("[CameraEffectsManager] Attached RadialMotionBlurEffect to camera: "
                        + PlayerCamera.name);
                }
            }
            // Same pattern for ChromaticAberrationEffect — camera component.
            if (ChromaticAberration == null || ChromaticAberration.gameObject != PlayerCamera.gameObject)
            {
                var existing = PlayerCamera.GetComponent<ChromaticAberrationEffect>();
                ChromaticAberration = existing != null
                    ? existing
                    : PlayerCamera.gameObject.AddComponent<ChromaticAberrationEffect>();
            }
        }
        _modulesAttached =
            TransformFX != null && FOVFX != null && Vignette != null && DamageFlash != null &&
            Letterbox != null && SpeedLines != null && FilmGrain != null &&
            Combat != null && Slowmo != null && Mood != null && LensFlares != null &&
            PlayerCamera != null && RadialBlur != null && ChromaticAberration != null;
    }

    void Update()
    {
        if (PlayerCamera == null || Input == null) TryAcquireRefs();
        // Re-run attachment until all modules (including camera-dependent ones)
        // are in place. Catches the case where PlayerCamera becomes valid AFTER
        // Awake + OnSceneLoaded — e.g., when the build path seeds the manager
        // in MainMenu before the gameplay scene's camera exists.
        if (!_modulesAttached) AttachModules();

        // Gate the camera-component effects' .enabled state on their flags.
        // OnRenderImage is only called for ENABLED components, so disabling
        // here actually skips the GPU work — previously the effects would
        // pass-through with Graphics.Blit(source, destination), which is
        // still a measurable GPU cost at high resolution (~1 ms each). This
        // closes the "FPS doesn't recover after dropping the preset" bug:
        // flipping the flag off now removes the cost entirely.
        GateCameraFxEnabled();

        if (MasterEnabled && Input != null && Vignette != null && Input.fxSubtleVignette)
        {
            Vignette.Push(new Color(0f, 0f, 0f, 1f), Input.fxSubtleVignetteIntensity);
        }

        if (MasterEnabled && Input != null)
        {
            // Low-health pulse (red, slow sine).
            if (Input.fxLowHealthVignette && Vignette != null && ResourceManager.Instance != null)
            {
                float hp = ResourceManager.Instance.HealthPercent;
                if (hp < 0.25f)
                {
                    float t = (Mathf.Sin(Time.unscaledTime * 2f) + 1f) * 0.5f;
                    float strength = Mathf.Lerp(0.25f, 0.6f, (0.25f - hp) / 0.25f) * Mathf.Lerp(0.6f, 1f, t);
                    Vignette.Push(new Color(1f, 0.15f, 0.2f, 1f), strength);
                }
            }

            // Dialogue focus (soft black).
            if (Input.fxDialogueVignette && Vignette != null && PlayerController.isInDialogue)
            {
                Vignette.Push(new Color(0f, 0f, 0f, 1f), 0.4f);
            }
        }
    }

    // Cached Canvas refs on overlay FX child GameObjects. Looked up on first
    // call to GateCameraFxEnabled and reused — GetComponent is fast but not
    // free, and this method runs every Update.
    Canvas _filmGrainCanvas, _damageFlashCanvas, _letterboxCanvas;
    Canvas _moodCanvas, _speedLinesCanvas, _lensFlaresCanvas;

    void GateCameraFxEnabled()
    {
        bool master = MasterEnabled;
        if (RadialBlur != null)
        {
            bool want = master && Input != null && Input.fxRadialMotionBlur;
            if (RadialBlur.enabled != want) RadialBlur.enabled = want;
        }
        if (ChromaticAberration != null)
        {
            bool want = master && Input != null && Input.fxChromaticAberration;
            if (ChromaticAberration.enabled != want) ChromaticAberration.enabled = want;
        }
        // Canvas-based overlay FX. Each module creates a Canvas on its own
        // GameObject and runs LateUpdate animation every frame regardless of
        // flag — leaving the Canvas enabled with zero-alpha content still
        // costs Canvas-batcher + renderer time. Disabling the Canvas
        // component skips the rendering entirely. The MonoBehaviour's
        // Update/LateUpdate still ticks but is harmless (cheap math) when
        // there's no visible target to push to.
        GateOverlayCanvas(FilmGrain,   ref _filmGrainCanvas,   master && Input != null && Input.fxFilmGrain);
        GateOverlayCanvas(DamageFlash, ref _damageFlashCanvas, master && Input != null && Input.fxDamageFlash);
        GateOverlayCanvas(Letterbox,   ref _letterboxCanvas,   master && Input != null && Input.fxLetterboxBars);
        GateOverlayCanvas(Mood,        ref _moodCanvas,        master && Input != null && Input.fxMoodColorGrade);
        GateOverlayCanvas(SpeedLines,  ref _speedLinesCanvas,  master && Input != null && Input.fxSpeedLines);
        GateOverlayCanvas(LensFlares,  ref _lensFlaresCanvas,  master && Input != null && Input.fxLensFlares);
        // Also disable the MonoBehaviour itself for these — stops LateUpdate
        // animation cost on top of the Canvas render. Safe because each
        // module's state is re-initialised lazily on re-enable.
        if (FilmGrain   != null) { bool w = master && Input != null && Input.fxFilmGrain;       if (FilmGrain.enabled   != w) FilmGrain.enabled   = w; }
        if (DamageFlash != null) { bool w = master && Input != null && Input.fxDamageFlash;     if (DamageFlash.enabled != w) DamageFlash.enabled = w; }
        if (Letterbox   != null) { bool w = master && Input != null && Input.fxLetterboxBars;   if (Letterbox.enabled   != w) Letterbox.enabled   = w; }
        if (Mood        != null) { bool w = master && Input != null && Input.fxMoodColorGrade;  if (Mood.enabled        != w) Mood.enabled        = w; }
        if (SpeedLines  != null) { bool w = master && Input != null && Input.fxSpeedLines;      if (SpeedLines.enabled  != w) SpeedLines.enabled  = w; }
        if (LensFlares  != null) { bool w = master && Input != null && Input.fxLensFlares;      if (LensFlares.enabled  != w) LensFlares.enabled  = w; }
    }

    static void GateOverlayCanvas(MonoBehaviour module, ref Canvas cached, bool want)
    {
        if (module == null) return;
        if (cached == null) cached = module.GetComponent<Canvas>();
        if (cached == null) return;
        if (cached.enabled != want) cached.enabled = want;
    }

    void TryAcquireRefs()
    {
        if (PlayerCamera == null)
        {
            var cam = Camera.main;
            if (cam != null) PlayerCamera = cam;
        }
        if (Input == null)
        {
            var settingsMenu = FindObjectOfType<SettingsMenu>(true);
            if (settingsMenu != null) Input = settingsMenu.inputSettings;
        }
    }
}
