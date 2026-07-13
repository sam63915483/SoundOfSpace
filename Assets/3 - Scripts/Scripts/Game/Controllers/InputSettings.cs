using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu (menuName = "Settings/Input")]

public class InputSettings : ScriptableObject {

	// Quality preset for the GRAPHICS pause-menu tab. Non-Custom values
	// drive a fixed bundle of the streaming-cap + camera-FX fields below
	// (see ApplyQualityPreset). Touching any preset-controlled slider/toggle
	// in the pause menu snaps qualityPreset back to Custom so a user's manual
	// tuning isn't silently overwritten on the next preset-apply call.
	//
	// Default is Custom so first-run / pre-preset saves keep their existing
	// per-field values exactly. A preset is only applied when the user picks
	// one from the dropdown.
	public enum QualityPreset { Low, Medium, High, Ultra, Custom }

	// Physics tick rate. Decoupled from frame rate — see ApplyPhysicsRate
	// docstring. Ordered low→high so the pause-menu slider reads naturally
	// (far left = fewest ticks, far right = most). Explicit int values keep
	// PlayerPrefs round-trip stable if we ever reorder again.
	//   Low      = 40 Hz  — potato mode; slowest input-to-physics response
	//   Balanced = 50 Hz  — Unity's own default
	//   Ultra    = 100 Hz — original physicsTimeStep before this setting existed
	//   Max      = 144 Hz — matches typical high-refresh monitors, tightest feel
	//   Insane   = 240 Hz — only meaningful on 240 Hz+ monitors and beefy CPUs;
	//                       diminishing returns past monitor refresh rate
	public enum PhysicsRate {
		Low      = 0,
		Balanced = 1,
		Ultra    = 2,
		Max      = 3,
		Insane   = 4,
	}

	// GPU quality knobs that map onto Unity's built-in QualitySettings.
	// Bundled into the quality preset (Low/Medium/High/Ultra) and individually
	// adjustable from the pause-menu GRAPHICS tab. Pushed to QualitySettings
	// via ApplyGraphicsQuality (called from LoadSettings + after each preset
	// or individual slider change).
	//
	// Values use int casts that line up with Unity's enums where possible so
	// the cast is direct (no switch). antiAliasing values are 0/2/4/8 = MSAA
	// sample counts (Unity's expected raw int). textureQuality is Unity's
	// globalTextureMipmapLimit (0 = full, 3 = eighth).
	public enum AntiAliasingLevel    { Off = 0, MSAA2x = 2, MSAA4x = 4, MSAA8x = 8 }
	public enum TextureQualityLevel  { Full = 0, Half = 1, Quarter = 2, Eighth = 3 }
	public enum ShadowResolutionLevel { Low = 0, Medium = 1, High = 2, VeryHigh = 3 }
	public enum ShadowCascadeCount   { Zero = 0, Two = 2, Four = 4 }
	public enum AnisotropicLevel     { Disable = 0, Enable = 1, ForceEnable = 2 }

	// Phone-camera RT resolution scale. Drives PlayerPhoneUI.EnsureCameraRig
	// and therefore the per-frame cost of video recording (AsyncGPUReadback
	// + JPEG encode + AVI write are all O(pixels)). Smaller = pixelier but
	// far cheaper. Ordered low→high so the pause-menu slider reads naturally.
	//   Eighth       — 12.5% of screen res, potato-PC tier, very blocky
	//   Quarter      — 25%, very pixely
	//   Half         — 50%, noticeable softness
	//   ThreeQuarter — 75%, slight softness
	//   Full         — native screen res (default)
	// Enum values are persisted via PlayerPrefs key phoneResolutionScaleV2
	// (the V2 suffix lets us reorder freely; old saves are ignored).
	public enum PhoneResolutionScale {
		Eighth       = 0,
		Quarter      = 1,
		Half         = 2,
		ThreeQuarter = 3,
		Full         = 4,
	}

	// Active InputSettings asset — set in Begin() so code without an
	// inspector reference (e.g. PlayerPhoneUI, which is an auto-singleton)
	// can read user-tuned values directly. Last-wins if multiple Ship /
	// PlayerController instances Begin() — in practice they share the same
	// ScriptableObject asset so this is fine.
	public static InputSettings Active { get; private set; }

	// While the pause-menu settings panel is open, slider/toggle setters
	// still write to the field bundle on this asset (so live-polling
	// systems — spawners, camera FX — see the change for preview), but
	// the Apply* and SaveSettings calls become no-ops. The pause menu
	// flips this false and calls Apply* + SaveSettings explicitly when
	// the user clicks "Save and Apply"; flips back to true (or, on
	// cancel, reverts the field bundle to the snapshot it captured on
	// open). Not persisted — runtime state only.
	[System.NonSerialized] public bool deferApply = false;

	const float defaultMouseSensitivity = 100;
	const float defaultMouseSmoothing = 0.2f;
	const float defaultMasterVolume = 1f;
	const int defaultMaxTrees = 20;
	const int defaultMaxAlienNPCs = 10;
	const int defaultMaxMushrooms = 40;
	const int defaultMaxCrystals = 20;
	const int defaultMaxAudienceSize = 25;
	const float defaultViewDistance = 350f;

	const float defaultStickLookSensitivity = 1f;
	const float defaultShipStickSensitivity = 1f;
	const float defaultStickDeadzone = 0.19f;
	const bool  defaultInvertLookY = false;
	const bool  defaultControllerEnabled = true;    // auto-detected; pads work out of the box (toggle stays as an opt-out)
	const bool  defaultVibrationEnabled = true;

	public float mouseSensitivity;
	public float mouseSmoothing;
	[Range(0, 1)] public float masterVolume = defaultMasterVolume;
	[Range(20, 100)] public int maxTrees = defaultMaxTrees;
	[Range(5, 20)] public int maxAlienNPCs = defaultMaxAlienNPCs;
	[Range(0, 100)] public int maxMushrooms = defaultMaxMushrooms;
	[Range(0, 60)] public int maxCrystals = defaultMaxCrystals;
	[Range(10, 40)] public int maxAudienceSize = defaultMaxAudienceSize;
	[Range(100, 1000)] public float viewDistance = defaultViewDistance;
	public bool lockCursor = true;

	[Header("Controller")]
	// Stick look sensitivity. 1.0 is "normal", up to 10.0 for very fast feel.
	[Range(0f, 10f)] public float stickLookSensitivity = defaultStickLookSensitivity;
	// Independent slider for piloting — ship rotation feel needed its own
	// scaling because rotSpeed drives mouse-look at a different magnitude
	// than the on-foot stick path. Defaults match on-foot 1.0.
	[Range(0f, 10f)] public float shipStickSensitivity = defaultShipStickSensitivity;
	[Range(0f, 0.5f)] public float stickDeadzone = defaultStickDeadzone;
	public bool invertLookY = defaultInvertLookY;
	public bool controllerEnabled = defaultControllerEnabled;
	public bool vibrationEnabled = defaultVibrationEnabled;

	[Header("Camera Effects (master)")]
	public bool cameraEffectsEnabled = true;

	[Header("Camera Effects — Movement")]
	public bool fxStrafeTilt = true;
	public bool fxSprintFovKick = true;

	[Header("Camera Effects — Vehicle")]
	public bool fxJetpackFovKick = true;
	public bool fxShipBoostFov = true;
	public bool fxSpeedLines = true;

	[Header("Camera Effects — Combat")]
	public bool fxDamageFlash = true;
	public bool fxDamageVignette = true;
	public bool fxDirectionalHitShake = true;
	public bool fxEnemyHitMicroShake = true;
	public bool fxDeathTilt = true;
	public bool fxSlowmoOnKill = true;

	[Header("Camera Effects — Survival & Cinematic")]
	public bool fxLowHealthVignette = true;
	public bool fxDialogueVignette = true;
	public bool fxLetterboxBars = true;
	public bool fxMoodColorGrade = true;

	[Header("Display")]
	// 0/0 ⇒ "first run, use the display's current native resolution". Once
	// the user picks a resolution from the pause menu Graphics tab, these are
	// populated and re-applied on every game start (via LoadSettings →
	// ApplyDisplaySettings).
	public int displayWidth = 0;
	public int displayHeight = 0;
	public bool displayFullscreen = true;

	[Header("Camera Effects — Lens Character")]
	public bool fxSubtleVignette = true;
	public bool fxFilmGrain = false; // first-run default: OFF (shipped-build preference)
	public bool fxChromaticAberration = true;
	public bool fxLensFlares = true;
	public bool fxRadialMotionBlur = false; // default OFF — opt-in via pause menu
	// Skips the BloomEffect step in CustomPostProcessing when false. Diagnostic
	// switch for the "ground brightness changes when I look around" bug — bloom
	// catches the Sun's HDR mesh and adds a screen-space glow that brightens
	// the whole frame when the Sun is in view. Toggle off in CAMERA tab to A/B.
	public bool fxBloom = true;
	public bool fxSpaceDust = true;

		[Header("HUD")]
		// Hides the gameplay HUD (compass, vitals, jetpack/flight status, wallet)
		// for a clean, cinematic view. Applied through HudVisibility on load and
		// whenever the CAMERA-tab "HIDE HUD" toggle changes.
		public bool hudHidden = false;

	[Header("Concert")]
	// Off by default — concert stages spawn ~20 real-time Lights between cone
	// lights, blinders, and strobes. Shadowed dynamic spotlights are one of the
	// most expensive things you can render on a tile-based or low-power GPU
	// (Steam Deck, integrated, mobile). Existing scene defaults already set
	// LightShadows.None in each light component; this toggle lets a user on a
	// beefy GPU opt back in to shadowed stage lighting. ConcertConeLight /
	// ConcertStrobeLight / ConcertBlinder all poll this flag and update
	// _light.shadows live.
	public bool fxConcertShadows = false;

	[Header("Quality Preset")]
	// See enum definition at top of file. Default Custom = honor whatever the
	// other fields say. Picking a non-Custom value in the pause menu calls
	// ApplyQualityPreset which overwrites the bundle below.
	public QualityPreset qualityPreset = QualityPreset.Custom;

	// Physics tick rate. Editable in the QUALITY pause-menu section.
	// Default Ultra = 100 Hz, matches the original Universe.physicsTimeStep.
	// Independent of qualityPreset (different axis — perf vs precision).
	public PhysicsRate physicsRate = PhysicsRate.Ultra;

	// Phone-camera RT resolution scale. Drives PlayerPhoneUI's RT size and
	// therefore the per-frame cost of video recording. Default Full = native
	// screen resolution. Bundled into the quality preset (Ultra→Full ...
	// Low→Quarter) — see ApplyQualityPreset.
	public PhoneResolutionScale phoneResolutionScale = PhoneResolutionScale.Full;

	[Header("AI / VRAM budget")]
	// When false, the phone AI never loads its language model — frees ~6 GB
	// VRAM on the GPU build (Hermes-3-Llama-3.1-8B at Q4_K_M). The phone's
	// AI app still opens but routes to a "disabled" stub. When true, the
	// model loads lazily the first time the player opens the AI chat and
	// unloads when they leave it, so most of the session has the VRAM free.
	// Default: true (AI available; load-on-open is the active strategy).
	public bool aiEnabled = true;

	[Header("GPU Quality (Unity QualitySettings)")]
	// Mapped 1:1 onto QualitySettings.* by ApplyGraphicsQuality. Bundled into
	// the quality preset (Low/Medium/High/Ultra) and individually editable
	// via the GRAPHICS pause-menu sliders. Defaults match "High" preset.
	public AntiAliasingLevel    antiAliasing         = AntiAliasingLevel.MSAA4x;
	public TextureQualityLevel  textureQuality       = TextureQualityLevel.Full;
	public ShadowResolutionLevel shadowResolution    = ShadowResolutionLevel.High;
	public ShadowCascadeCount   shadowCascades       = ShadowCascadeCount.Four;
	[Range(20f, 500f)] public float shadowDistance   = 150f;
	public AnisotropicLevel     anisotropicFiltering = AnisotropicLevel.Enable;

	public float GetPhoneResolutionMultiplier () {
		switch (phoneResolutionScale) {
			case PhoneResolutionScale.Eighth:       return 0.125f;
			case PhoneResolutionScale.Quarter:      return 0.25f;
			case PhoneResolutionScale.Half:         return 0.5f;
			case PhoneResolutionScale.ThreeQuarter: return 0.75f;
			case PhoneResolutionScale.Full:         return 1f;
			default:                                return 1f;
		}
	}

	[Header("Camera Effects — Intensities")]
	[Range(0f, 1f)] public float fxFilmGrainIntensity = 0.6f;
	[Range(0f, 1f)] public float fxSubtleVignetteIntensity = 0.45f;
	[Range(0f, 1f)] public float fxChromaticAberrationIntensity = 0.35f;

	[Header("World Detail")]
	// Grass render-distance multiplier. 0 = no grass at all, 1 = the
	// InstancedGrassRenderer's authored distance, up to 3× further (more grass on
	// screen). Read live each stream tick by InstancedGrassRenderer (scales its
	// spawnRadius). Persists via PlayerPrefs; independent of the quality preset.
	[Range(0f, 3f)] public float grassRenderScale = 1f;

	// Base (vertical) field of view for the player camera, in degrees. Unity's
	// Camera.fieldOfView is VERTICAL, so this is aspect-independent — raising it
	// zooms out, fitting more world on screen (useful for ultrawide players who
	// crop a narrow centre strip for vertical-video capture). Read live each
	// frame by CameraFOVFX, which adds its sprint/jetpack/boost kicks ON TOP of
	// this value. 0 = "unset": CameraFOVFX seeds it once from the scene-authored
	// camera FOV so the slider starts at the game's real default.
	[Range(50f, 110f)] public float cameraFov = 0f;

	// TODO: find better place to call this from
	public void Begin () {
		Active = this;
		LoadSettings ();
		AudioListener.volume = masterVolume;

		// Push controller values onto the static TutorialGate so input call
		// sites pick up the user's slider values immediately.
		PushControllerSettingsToGate ();

		if (lockCursor) {
			Cursor.visible = false;
			Cursor.lockState = CursorLockMode.Locked;
		}
	}

	public void LoadSettings () {
		// FIX: previously the GetFloat results for these two were read but
		// not assigned, so PlayerPrefs effectively did nothing for them.
		mouseSensitivity = PlayerPrefs.GetFloat (nameof (mouseSensitivity), defaultMouseSensitivity);
		mouseSmoothing   = PlayerPrefs.GetFloat (nameof (mouseSmoothing),   defaultMouseSmoothing);
		masterVolume = PlayerPrefs.GetFloat (nameof (masterVolume), defaultMasterVolume);
		maxTrees = PlayerPrefs.GetInt (nameof (maxTrees), defaultMaxTrees);
		maxAlienNPCs = PlayerPrefs.GetInt (nameof (maxAlienNPCs), defaultMaxAlienNPCs);
		maxMushrooms = PlayerPrefs.GetInt (nameof (maxMushrooms), defaultMaxMushrooms);
		maxCrystals = PlayerPrefs.GetInt (nameof (maxCrystals), defaultMaxCrystals);
		maxAudienceSize = PlayerPrefs.GetInt (nameof (maxAudienceSize), defaultMaxAudienceSize);
		viewDistance = PlayerPrefs.GetFloat (nameof (viewDistance), defaultViewDistance);
		grassRenderScale = PlayerPrefs.GetFloat (nameof (grassRenderScale), 1f);
		cameraFov = PlayerPrefs.GetFloat (nameof (cameraFov), 0f);   // 0 = seed from authored FOV in CameraFOVFX

		stickLookSensitivity = PlayerPrefs.GetFloat (nameof (stickLookSensitivity), defaultStickLookSensitivity);
		shipStickSensitivity = PlayerPrefs.GetFloat (nameof (shipStickSensitivity), defaultShipStickSensitivity);
		stickDeadzone        = PlayerPrefs.GetFloat (nameof (stickDeadzone),        defaultStickDeadzone);
		invertLookY          = PlayerPrefs.GetInt   (nameof (invertLookY),          defaultInvertLookY ? 1 : 0) != 0;
		// "_v2" key: the pre-revamp build stored an opt-IN default (false), so
		// every existing profile has a saved 0. Bumping the key makes the new
		// auto-detect default win once; the toggle still persists after that.
		controllerEnabled    = PlayerPrefs.GetInt   ("controllerEnabled_v2",        defaultControllerEnabled ? 1 : 0) != 0;
		vibrationEnabled     = PlayerPrefs.GetInt   (nameof (vibrationEnabled),     defaultVibrationEnabled ? 1 : 0) != 0;

		cameraEffectsEnabled        = PlayerPrefs.GetInt   (nameof (cameraEffectsEnabled),        1) != 0;
		fxStrafeTilt                = PlayerPrefs.GetInt   (nameof (fxStrafeTilt),                1) != 0;
		fxSprintFovKick             = PlayerPrefs.GetInt   (nameof (fxSprintFovKick),             1) != 0;
		fxJetpackFovKick            = PlayerPrefs.GetInt   (nameof (fxJetpackFovKick),            1) != 0;
		fxShipBoostFov              = PlayerPrefs.GetInt   (nameof (fxShipBoostFov),              1) != 0;
		fxSpeedLines                = PlayerPrefs.GetInt   (nameof (fxSpeedLines),                1) != 0;
		fxDamageFlash               = PlayerPrefs.GetInt   (nameof (fxDamageFlash),               1) != 0;
		fxDamageVignette            = PlayerPrefs.GetInt   (nameof (fxDamageVignette),            1) != 0;
		fxDirectionalHitShake       = PlayerPrefs.GetInt   (nameof (fxDirectionalHitShake),       1) != 0;
		fxEnemyHitMicroShake        = PlayerPrefs.GetInt   (nameof (fxEnemyHitMicroShake),        1) != 0;
		fxDeathTilt                 = PlayerPrefs.GetInt   (nameof (fxDeathTilt),                 1) != 0;
		fxSlowmoOnKill              = PlayerPrefs.GetInt   (nameof (fxSlowmoOnKill),              1) != 0;
		fxLowHealthVignette         = PlayerPrefs.GetInt   (nameof (fxLowHealthVignette),         1) != 0;
		fxDialogueVignette          = PlayerPrefs.GetInt   (nameof (fxDialogueVignette),          1) != 0;
		fxLetterboxBars             = PlayerPrefs.GetInt   (nameof (fxLetterboxBars),             1) != 0;
		fxMoodColorGrade            = PlayerPrefs.GetInt   (nameof (fxMoodColorGrade),            1) != 0;
		fxSubtleVignette            = PlayerPrefs.GetInt   (nameof (fxSubtleVignette),            1) != 0;
		fxFilmGrain                 = PlayerPrefs.GetInt   (nameof (fxFilmGrain),                 0) != 0;
		fxChromaticAberration       = PlayerPrefs.GetInt   (nameof (fxChromaticAberration),       1) != 0;
		fxLensFlares                = PlayerPrefs.GetInt   (nameof (fxLensFlares),                1) != 0;
		fxBloom                     = PlayerPrefs.GetInt   (nameof (fxBloom),                     1) != 0;
		fxSpaceDust                 = PlayerPrefs.GetInt   (nameof (fxSpaceDust),                 1) != 0;
		fxRadialMotionBlur          = PlayerPrefs.GetInt   (nameof (fxRadialMotionBlur),          0) != 0;
		fxHelmetOverlay             = PlayerPrefs.GetInt   (nameof (fxHelmetOverlay),             1) != 0;
		fxHelmetCondensation        = PlayerPrefs.GetInt   (nameof (fxHelmetCondensation),        1) != 0;
		fxHelmetBob                 = PlayerPrefs.GetInt   (nameof (fxHelmetBob),                 1) != 0;
		fxFilmGrainIntensity            = PlayerPrefs.GetFloat (nameof (fxFilmGrainIntensity),            0.6f);
		fxSubtleVignetteIntensity       = PlayerPrefs.GetFloat (nameof (fxSubtleVignetteIntensity),       0.45f);
		fxChromaticAberrationIntensity  = PlayerPrefs.GetFloat (nameof (fxChromaticAberrationIntensity),  0.35f);

		displayWidth      = PlayerPrefs.GetInt (nameof (displayWidth),      0);
		displayHeight     = PlayerPrefs.GetInt (nameof (displayHeight),     0);
		displayFullscreen = PlayerPrefs.GetInt (nameof (displayFullscreen), 1) != 0;

		hudHidden = PlayerPrefs.GetInt (nameof (hudHidden), 0) != 0;
		HudVisibility.SetUserHidden (hudHidden);

		fxConcertShadows = PlayerPrefs.GetInt (nameof (fxConcertShadows), 0) != 0;
		mirror60Hz       = PlayerPrefs.GetInt (nameof (mirror60Hz), 0) != 0;
		qualityPreset    = (QualityPreset) PlayerPrefs.GetInt (nameof (qualityPreset), (int) QualityPreset.Custom);
		// physicsRateV2 — bumped from physicsRate after the enum was reordered
		// (low→high). Old saved key is intentionally ignored so a previous
		// Ultra=0 doesn't read back as the new Low=0.
		physicsRate      = (PhysicsRate)   PlayerPrefs.GetInt ("physicsRateV2",        (int) PhysicsRate.Ultra);
		// phoneResolutionScaleV2 — bumped from phoneResolutionScale after the
		// enum was reordered to put Eighth at the front. Old saved key is
		// intentionally ignored so a previous Quarter=0 doesn't read back
		// as the new Eighth=0.
		phoneResolutionScale = (PhoneResolutionScale) PlayerPrefs.GetInt ("phoneResolutionScaleV2", (int) PhoneResolutionScale.Full);

		aiEnabled = PlayerPrefs.GetInt (nameof (aiEnabled), 1) != 0;

		// GPU quality knobs — load BEFORE ApplyGraphicsQuality so the push
		// to Unity's QualitySettings reflects the saved values.
		antiAliasing         = (AntiAliasingLevel)    PlayerPrefs.GetInt (nameof (antiAliasing),         (int) AntiAliasingLevel.MSAA4x);
		textureQuality       = (TextureQualityLevel)  PlayerPrefs.GetInt (nameof (textureQuality),       (int) TextureQualityLevel.Full);
		shadowResolution     = (ShadowResolutionLevel)PlayerPrefs.GetInt (nameof (shadowResolution),     (int) ShadowResolutionLevel.High);
		shadowCascades       = (ShadowCascadeCount)   PlayerPrefs.GetInt (nameof (shadowCascades),       (int) ShadowCascadeCount.Four);
		shadowDistance       =                        PlayerPrefs.GetFloat (nameof (shadowDistance),     150f);
		anisotropicFiltering = (AnisotropicLevel)     PlayerPrefs.GetInt (nameof (anisotropicFiltering), (int) AnisotropicLevel.Enable);

		// Push the loaded rate into Universe + Time. NBodySimulation.Awake
		// also assigns Time.fixedDeltaTime once at scene start, but that runs
		// BEFORE LoadSettings, so we re-apply here to honor the saved value.
		ApplyPhysicsRate (physicsRate);
		ApplyGraphicsQuality ();

		ApplyDisplaySettings ();
	}

	// Applies displayWidth / displayHeight / displayFullscreen to Screen.
	// Safe to call any time. First-run (0/0) uses the display's current
	// native resolution so we don't downgrade the user the very first time
	// they launch the build. Subsequent runs honor the saved values.
	public void ApplyDisplaySettings () {
		if (deferApply) return;
		var mode = displayFullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
		if (displayWidth <= 0 || displayHeight <= 0) {
			var cur = Screen.currentResolution;
			Screen.SetResolution (cur.width, cur.height, mode);
		} else {
			Screen.SetResolution (displayWidth, displayHeight, mode);
		}
	}

	public void SaveSettings () {
		if (deferApply) return;
		PlayerPrefs.SetFloat (nameof (mouseSensitivity), mouseSensitivity);
		PlayerPrefs.SetFloat (nameof (mouseSmoothing), mouseSmoothing);
		PlayerPrefs.SetFloat (nameof (masterVolume), masterVolume);
		PlayerPrefs.SetInt (nameof (maxTrees), maxTrees);
		PlayerPrefs.SetInt (nameof (maxAlienNPCs), maxAlienNPCs);
		PlayerPrefs.SetInt (nameof (maxMushrooms), maxMushrooms);
		PlayerPrefs.SetInt (nameof (maxCrystals), maxCrystals);
		PlayerPrefs.SetInt (nameof (maxAudienceSize), maxAudienceSize);
		PlayerPrefs.SetFloat (nameof (viewDistance), viewDistance);
		PlayerPrefs.SetFloat (nameof (grassRenderScale), grassRenderScale);
		PlayerPrefs.SetFloat (nameof (cameraFov), cameraFov);

		PlayerPrefs.SetFloat (nameof (stickLookSensitivity), stickLookSensitivity);
		PlayerPrefs.SetFloat (nameof (shipStickSensitivity), shipStickSensitivity);
		PlayerPrefs.SetFloat (nameof (stickDeadzone),        stickDeadzone);
		PlayerPrefs.SetInt   (nameof (invertLookY),          invertLookY ? 1 : 0);
		PlayerPrefs.SetInt   ("controllerEnabled_v2",        controllerEnabled ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (vibrationEnabled),     vibrationEnabled ? 1 : 0);

		PlayerPrefs.SetInt   (nameof (cameraEffectsEnabled),        cameraEffectsEnabled        ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxStrafeTilt),                fxStrafeTilt                ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxSprintFovKick),             fxSprintFovKick             ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxJetpackFovKick),            fxJetpackFovKick            ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxShipBoostFov),              fxShipBoostFov              ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxSpeedLines),                fxSpeedLines                ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxDamageFlash),               fxDamageFlash               ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxDamageVignette),            fxDamageVignette            ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxDirectionalHitShake),       fxDirectionalHitShake       ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxEnemyHitMicroShake),        fxEnemyHitMicroShake        ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxDeathTilt),                 fxDeathTilt                 ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxSlowmoOnKill),              fxSlowmoOnKill              ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxLowHealthVignette),         fxLowHealthVignette         ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxDialogueVignette),          fxDialogueVignette          ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxLetterboxBars),             fxLetterboxBars             ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxMoodColorGrade),            fxMoodColorGrade            ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxSubtleVignette),            fxSubtleVignette            ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxFilmGrain),                 fxFilmGrain                 ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxChromaticAberration),       fxChromaticAberration       ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxLensFlares),                fxLensFlares                ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxBloom),                     fxBloom                     ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxSpaceDust),                 fxSpaceDust                 ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxRadialMotionBlur),          fxRadialMotionBlur          ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxHelmetOverlay),             fxHelmetOverlay             ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxHelmetCondensation),        fxHelmetCondensation        ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (fxHelmetBob),                 fxHelmetBob                 ? 1 : 0);
		PlayerPrefs.SetFloat (nameof (fxFilmGrainIntensity),            fxFilmGrainIntensity);
		PlayerPrefs.SetFloat (nameof (fxSubtleVignetteIntensity),       fxSubtleVignetteIntensity);
		PlayerPrefs.SetFloat (nameof (fxChromaticAberrationIntensity),  fxChromaticAberrationIntensity);

		PlayerPrefs.SetInt (nameof (displayWidth),      displayWidth);
		PlayerPrefs.SetInt (nameof (displayHeight),     displayHeight);
		PlayerPrefs.SetInt (nameof (displayFullscreen), displayFullscreen ? 1 : 0);

		PlayerPrefs.SetInt (nameof (hudHidden), hudHidden ? 1 : 0);

		PlayerPrefs.SetInt (nameof (fxConcertShadows), fxConcertShadows ? 1 : 0);
		PlayerPrefs.SetInt (nameof (mirror60Hz),       mirror60Hz ? 1 : 0);
		PlayerPrefs.SetInt (nameof (qualityPreset),    (int) qualityPreset);
		PlayerPrefs.SetInt ("physicsRateV2",            (int) physicsRate);
		PlayerPrefs.SetInt ("phoneResolutionScaleV2",      (int) phoneResolutionScale);

		PlayerPrefs.SetInt   (nameof (aiEnabled),            aiEnabled ? 1 : 0);
		PlayerPrefs.SetInt   (nameof (antiAliasing),         (int) antiAliasing);
		PlayerPrefs.SetInt   (nameof (textureQuality),       (int) textureQuality);
		PlayerPrefs.SetInt   (nameof (shadowResolution),     (int) shadowResolution);
		PlayerPrefs.SetInt   (nameof (shadowCascades),       (int) shadowCascades);
		PlayerPrefs.SetFloat (nameof (shadowDistance),       shadowDistance);
		PlayerPrefs.SetInt   (nameof (anisotropicFiltering), (int) anisotropicFiltering);

		PlayerPrefs.Save ();

		PushControllerSettingsToGate ();
	}

	// Apply a quality preset by overwriting the bundle of streaming-cap +
	// camera-FX + concert-shadow fields with this preset's values. Caller is
	// responsible for refreshing pause-menu rows + saving.
	//
	// Values picked so:
	//   • Low — Steam Deck / integrated GPUs. Caps low, expensive lens FX
	//           off (CA / lens dirt / motion blur / grain), shadows off.
	//   • Medium — current scene defaults; the "safe" preset.
	//   • High — a step above medium for desktop. Lens FX on, shadows still
	//           off (they're expensive and the concert looks fine without).
	//   • Ultra — max caps + concert shadows on + every lens FX on.
	// Anything the player has manually edited is preserved by re-picking
	// Custom afterwards.
	public void ApplyQualityPreset (QualityPreset preset) {
		qualityPreset = preset;
		switch (preset) {
			// NOTE: fxFilmGrain is NOT in any preset bundle — it stays off
			// unless the player explicitly enables it in CAMERA tab.
			// LensDirt has been removed entirely.
			case QualityPreset.Low:
				viewDistance     = 200f;
				maxTrees         = 30;
				maxAlienNPCs     = 6;
				maxMushrooms     = 20;
				maxCrystals      = 10;
				maxAudienceSize  = 15;
				fxConcertShadows       = false;
				fxChromaticAberration  = false;
				fxRadialMotionBlur     = false;
				fxLensFlares           = false;
				fxSubtleVignette       = true;
				phoneResolutionScale   = PhoneResolutionScale.Quarter;
				antiAliasing           = AntiAliasingLevel.Off;
				textureQuality         = TextureQualityLevel.Quarter;
				shadowResolution       = ShadowResolutionLevel.Low;
				shadowCascades         = ShadowCascadeCount.Zero;
				shadowDistance         = 50f;
				anisotropicFiltering   = AnisotropicLevel.Disable;
				break;
			case QualityPreset.Medium:
				viewDistance     = 350f;
				maxTrees         = defaultMaxTrees;      // 20
				maxAlienNPCs     = defaultMaxAlienNPCs;  // 10
				maxMushrooms     = defaultMaxMushrooms;  // 40
				maxCrystals      = defaultMaxCrystals;   // 20
				maxAudienceSize  = defaultMaxAudienceSize; // 25
				fxConcertShadows       = false;
				fxChromaticAberration  = true;
				fxRadialMotionBlur     = false;
				fxLensFlares           = true;
				fxSubtleVignette       = true;
				phoneResolutionScale   = PhoneResolutionScale.Half;
				antiAliasing           = AntiAliasingLevel.MSAA2x;
				textureQuality         = TextureQualityLevel.Half;
				shadowResolution       = ShadowResolutionLevel.Medium;
				shadowCascades         = ShadowCascadeCount.Two;
				shadowDistance         = 100f;
				anisotropicFiltering   = AnisotropicLevel.Enable;
				break;
			case QualityPreset.High:
				viewDistance     = 500f;
				maxTrees         = 70;
				maxAlienNPCs     = 14;
				maxMushrooms     = 60;
				maxCrystals      = 30;
				maxAudienceSize  = 32;
				fxConcertShadows       = false;
				fxChromaticAberration  = true;
				fxRadialMotionBlur     = false;
				fxLensFlares           = true;
				fxSubtleVignette       = true;
				phoneResolutionScale   = PhoneResolutionScale.ThreeQuarter;
				antiAliasing           = AntiAliasingLevel.MSAA4x;
				textureQuality         = TextureQualityLevel.Full;
				shadowResolution       = ShadowResolutionLevel.High;
				shadowCascades         = ShadowCascadeCount.Four;
				shadowDistance         = 200f;
				anisotropicFiltering   = AnisotropicLevel.Enable;
				break;
			case QualityPreset.Ultra:
				viewDistance     = 800f;
				maxTrees         = 100;
				maxAlienNPCs     = 20;
				maxMushrooms     = 100;
				maxCrystals      = 50;
				maxAudienceSize  = 40;
				fxConcertShadows       = true;
				fxChromaticAberration  = true;
				fxRadialMotionBlur     = true;
				fxLensFlares           = true;
				fxSubtleVignette       = true;
				phoneResolutionScale   = PhoneResolutionScale.Full;
				antiAliasing           = AntiAliasingLevel.MSAA8x;
				textureQuality         = TextureQualityLevel.Full;
				shadowResolution       = ShadowResolutionLevel.VeryHigh;
				shadowCascades         = ShadowCascadeCount.Four;
				shadowDistance         = 400f;
				anisotropicFiltering   = AnisotropicLevel.ForceEnable;
				break;
			case QualityPreset.Custom:
				// No-op — preserve whatever the user has edited.
				break;
		}
		// Push the new GPU values to Unity. Skip for Custom so a user toggling
		// to Custom doesn't unexpectedly snap shadows / AA back to defaults.
		if (preset != QualityPreset.Custom) ApplyGraphicsQuality ();
	}

	// Push the InputSettings GPU-quality fields onto Unity's QualitySettings.
	// Called from LoadSettings (to honor saved values), from ApplyQualityPreset
	// (after each preset bundle), and from the pause-menu sliders after any
	// individual change. Cheap — every setter is a single field assignment on
	// the global QualitySettings struct.
	public void ApplyGraphicsQuality () {
		if (deferApply) return;
		QualitySettings.antiAliasing         = (int) antiAliasing;
		QualitySettings.shadowCascades       = (int) shadowCascades;
		QualitySettings.shadowDistance       = shadowDistance;
		QualitySettings.shadowResolution     = (UnityEngine.ShadowResolution)     (int) shadowResolution;
		QualitySettings.anisotropicFiltering = (UnityEngine.AnisotropicFiltering) (int) anisotropicFiltering;
		// masterTextureLimit drops mip levels from texture sampling — 0 = full
		// quality, 1 = half (1 mip dropped), 2 = quarter, 3 = eighth. Cheap
		// way to cut VRAM + sample bandwidth without re-importing textures.
		QualitySettings.globalTextureMipmapLimit   = (int) textureQuality;
	}

	// Apply a physics tick rate by updating BOTH Universe.physicsTimeStep
	// (read by NBodySimulation each FixedUpdate for the velocity integration
	// step size) AND Time.fixedDeltaTime (Unity's solver tick). They must stay
	// in lockstep or the simulation desyncs from Unity's solver.
	//
	// Frame rate (FPS) is independent: Rigidbody interpolation smooths the
	// rendered position between physics ticks, so a 50 Hz physics game still
	// renders at whatever the GPU + monitor allow.
	public void ApplyPhysicsRate (PhysicsRate rate) {
		physicsRate = rate;
		if (deferApply) return;
		float dt;
		switch (rate) {
			case PhysicsRate.Low:      dt = 0.025f;       break; //  40 Hz
			case PhysicsRate.Balanced: dt = 0.02f;        break; //  50 Hz (Unity default)
			case PhysicsRate.Ultra:    dt = 0.01f;        break; // 100 Hz
			case PhysicsRate.Max:      dt = 1f / 144f;    break; // 144 Hz — matches high-refresh monitors
			case PhysicsRate.Insane:   dt = 1f / 240f;    break; // 240 Hz — beefy PCs only; ~6× the work of Low
			default:                   dt = 0.01f;        break;
		}
		Universe.physicsTimeStep = dt;
		Time.fixedDeltaTime      = dt;
	}

	public void PushControllerSettingsToGate () {
		TutorialGate.ControllerEnabled        = controllerEnabled;
		TutorialGate.StickLookSensitivity     = stickLookSensitivity;
		TutorialGate.ShipStickLookSensitivity = shipStickSensitivity;
		TutorialGate.StickDeadzone            = stickDeadzone;
		TutorialGate.InvertLookY              = invertLookY;
		GamepadRumble.Enabled                 = vibrationEnabled;
		// Deadzone finally applies: the Input System's default stick processor
		// reads this project-wide setting on every stick ReadValue().
		UnityEngine.InputSystem.InputSystem.settings.defaultDeadzoneMin =
			Mathf.Clamp (stickDeadzone, 0.01f, 0.5f);
	}

	// ── Helmet HUD overlay (APPEND-ONLY: serialized fields stay at class end) ──
	[Header("Helmet HUD Overlay")]
	public bool fxHelmetOverlay = true;       // helmet frame + visor glass + sway
	public bool fxHelmetCondensation = true;  // low-O2 visor fog (functional feedback — recommended on)
	public bool fxHelmetBob = true;           // walk/run helmet bob (stride-matched, stronger when sprinting)

	[Header("Ship Screens")]
	public bool mirror60Hz = false;           // rear-view screen refresh: off = 30 Hz, on = 60 Hz (costs an extra camera render per frame)

}
