using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlayerController : GravityObject
{

	/// <summary>Fires the single frame the player transitions from airborne to grounded. CameraTransformFX subscribes to drive the landing dip.</summary>
	public event System.Action OnLanded;

	// Exposed variables
	[Header("Movement settings")]
	public float walkSpeed = 8;
	public float runSpeed = 14;

	// True while the player is actively sprinting on the ground. Read by EnemyVision
	// for the stealth "sprint = instant spot" rule. Set in the grounded-move block
	// below (not a serialized field, so it never reorders the serialized block).
	public bool IsSprinting { get; private set; }
	public float jumpForce = 20;
	public float vSmoothTime = 0.1f;
	public float airSmoothTime = 0.5f;
	public float stickToGroundForce = 8;

	[Header("Upward Jetpack")]
	public float jetpackForce = 10;
	public float jetpackDuration = 2;
	public float jetpackRefuelTime = 2;
	public float jetpackRefuelDelay = 2;

	[Header("Jetpack Unlock")]
	[Tooltip("When false, all three thrust types (up/down/directional) are suppressed and the BoostMeters HUD is hidden. New games start with this false; player buys the jetpack from Alien7 to enable. Set true via UnlockJetpack().")]
	[SerializeField] bool jetpackUnlocked = false;

	[Header("Downward Thrust")]
	public float downThrustForce = 10;
	public float downThrustDuration = 2;
	public float downThrustRefuelTime = 2;
	public float downThrustRefuelDelay = 2;

	[Header("Directional Thrust")]
	public float dirThrustForce = 10;
	public float dirThrustDuration = 2;
	public float dirThrustRefuelTime = 2;
	public float dirThrustRefuelDelay = 2;

	[Header("Ship Proximity Velocity Matching")]
	[Tooltip("Within this radius of the nearest Ship (on foot, in air), the player's velocity is gently damped toward the ship's velocity. Makes boarding and space-net dust collection much easier without having to perfectly match orbital velocity. The effects also engage rotation alignment so the camera's up matches the ship's up. Gated on Ship.IsLanded (off-ground) — never engages near a parked ship.")]
	public float shipProximityRadius = 25f;
	[Tooltip("Per-second fraction of the player-relative-to-ship velocity that bleeds off inside the proximity zone. 1.0 = converge in ~1s; 0.5 = halfway in 1s. Keep small (0.5-2) for gentle drift; large values feel like being yanked.")]
	public float shipProximityDampRate = 1.5f;
	[Tooltip("Seconds for the player's up-direction to smoothly blend toward the ship's up-direction when entering the proximity zone, and back to gravity-up when leaving. Also drives the gravity-up smoothing when transitioning between planet gravity wells. 1 second feels organic; lower is snappier, higher is dreamier.")]
	public float shipUpBlendSeconds = 1.0f;
	// Current blend value 0..1 — 0 = full gravity-up, 1 = full ship-up.
	// MoveTowards-eased every FixedUpdate by the rotation block.
	float _shipUpBlend;
	// Smoothed gravity-up direction. The raw `-gravityOfNearestBody.normalized`
	// snaps instantly when the player crosses from one planet's gravity well
	// into another (referenceBody flips). This field tracks the target with
	// Vector3.RotateTowards at a rate of 180° per shipUpBlendSeconds so the
	// player's orientation eases across the boundary instead of jerking.
	// Initialised on first valid sample so it doesn't start at zero.
	//
	// Walking-jitter note: the smoothing was only ever needed for the
	// cross-body case. For normal walking on a SINGLE body the per-step
	// angular delta is far smaller than RotateTowards' maxRad cap so the
	// smoothing is functionally a no-op — but routing the rotation
	// alignment through _smoothedGravityUp every FixedUpdate was found
	// to introduce subtle frame-to-frame jitter at high refresh rates,
	// likely from the manual-interpolation camera FX slerp seeing tiny
	// non-identity rotations each step. We now drive the smoothing
	// ONLY during a body transition (_gravityUpBlending == true) and
	// snap to the raw direction in the steady state — equivalent to the
	// pre-b0e497b baseline that the user describes as previously smooth.
	Vector3 _smoothedGravityUp;
	bool    _smoothedGravityUpInit;
	bool    _gravityUpBlending;
	CelestialBody _lastReferenceBody;
	// Cached nearest ship within shipProximityRadius. Refreshed every
	// ShipProximityCheckInterval seconds — FindObjectsOfType is too
	// expensive to run per FixedUpdate (50 Hz) but cheap enough at 5 Hz.
	// THIS field is used by the velocity-damping block and follows the
	// proximity gate strictly: nulls the moment the player moves out of
	// the 25 m radius (FindNearestShipInRange returns null).
	Ship _cachedNearestShipInRange;
	float _nextShipProximityCheckTime;
	const float ShipProximityCheckInterval = 0.2f;
	// Separate reference for the rotation-blend slerp endpoint. Mirrors
	// _cachedNearestShipInRange while the player is in the zone but
	// LINGERS through the blend-out — only cleared once _shipUpBlend
	// reaches 0. Without this, the damping cache going null at the radius
	// boundary instantly killed the rotation slerp's ship-up endpoint
	// and the player snapped back to gravity-up.
	Ship _shipUpRotationRef;

	// While Time.fixedTime < this, the ship-proximity velocity damping below is
	// skipped. OxygenManager raises it during a hatch eject so the boarding-assist
	// damping doesn't instantly brake the player back to the ship's velocity and
	// cancel the ejection (which otherwise stops you ~1 m out). See SuppressShipProximityDamp.
	[System.NonSerialized] public float suppressShipDampUntil;

	// Called repeatedly by OxygenManager while the hatch is sucking the player out:
	// keeps the proximity damping suppressed for `seconds` from now — long enough
	// for the ejected player to coast clear of the ~25 m damping radius.
	public void SuppressShipProximityDamp(float seconds)
	{
		float until = Time.fixedTime + seconds;
		if (until > suppressShipDampUntil) suppressShipDampUntil = until;
	}

	[Header("Mouse settings")]
	public float mouseSensitivityMultiplier = 1;
	public float maxMouseSmoothTime = 0.3f;
	public Vector2 pitchMinMax = new Vector2(-89, 89);   // near-full look up/down (avoid gimbal flip at ±90)
	public InputSettings inputSettings;

	[Header("Wall Slide (anti-tunneling)")]
	[Tooltip("Surfaces with a normal whose dot product against the player's up vector is BELOW this threshold are treated as walls and the move is clamped + slid along them. Higher = more surfaces count as walls (more aggressive). Lower = only near-vertical walls. 0.5 ≈ 60°. Try 0.7 (≈ 45°) for stronger blocking.")]
	[Range(0f, 1f)] public float wallSlideMaxNormalUpDot = 0.5f;
	[Tooltip("How far past the desired move endpoint to sweep when checking for walls. A small overshoot detects near-misses. Higher = catches walls earlier (more aggressive).")]
	public float wallSlideOvershoot = 0.05f;
	[Tooltip("Gap left between the player and the wall after the move is clamped, in metres. Lower = sit closer to walls (more aggressive). Don't go below 0.005.")]
	public float wallSlideSkin = 0.02f;

	[Header("Boost UI")]
	public RectTransform upThrustFillRect;
	public RectTransform downThrustFillRect;
	public RectTransform dirThrustFillRect;

	[Header("Other")]
	public float mass = 70;
	public LayerMask walkableMask;
	public Transform feet;
	public Transform spawnPoint;

	[Header("Sound Effects")]
	[SerializeField] AudioClip footstepWalkClipA;
	[SerializeField] AudioClip footstepWalkClipB;
	[SerializeField] float sprintPitchMultiplier = 1.5f;
	[SerializeField] Vector2 footstepSwapInterval = new Vector2(0.5f, 1.5f);
	[SerializeField] AudioClip jumpClip;
	[SerializeField] AudioClip landClip;
	[SerializeField, Range(0, 1)] float footstepVolume = 0.5f;
	[SerializeField, Range(0, 1)] float jumpVolume     = 0.7f;
	[SerializeField, Range(0, 1)] float landVolume     = 0.6f;
	[SerializeField] float minAirborneForLandSound = 1.0f;

	[Header("Boost Sound Effects (looping while active)")]
	[SerializeField] AudioClip upBoostClip;
	[SerializeField] AudioClip downBoostClip;
	[SerializeField] AudioClip dirBoostClip;
	[SerializeField, Range(0, 1)] float upBoostVolume   = 0.5f;
	[SerializeField, Range(0, 1)] float downBoostVolume = 0.5f;
	[SerializeField, Range(0, 1)] float dirBoostVolume  = 0.5f;

	[Header("Water Sound Effects")]
	[SerializeField] AudioClip waterMoveClip;
	[SerializeField, Range(0, 1)] float waterMoveVolume = 0.5f;

	[Header("Water Buoyancy (jetpack-style)")]
	[Tooltip("Constant upward acceleration (m/s²) applied while submerged in water. Should be SLIGHTLY LESS than the planet's gravity so the player slowly sinks when not actively swimming. ~8 vs ~9.8 gravity = slow sink.")]
	public float waterBuoyancyForce = 8f;
	[Tooltip("Additional upward acceleration (m/s²) applied while Space (or A button) is held in water. Buoyancy + Swim should comfortably exceed gravity so the player actually rises toward the surface.")]
	public float waterSwimForce = 6f;
	[Tooltip("Maximum total velocity (m/s) the player's rigidbody can have while in water (relative to the local planet). Velocity is clamped to this each FixedUpdate, so jumping into water decelerates immediately, sinking has a terminal speed, and swim-up tops out gently — all from a single cap.")]
	public float waterMaxSpeed = 3.75f;

	[Header("Optional Sounds (slots only — not yet wired)")]
	[SerializeField] AudioClip itemPickupClip;
	[SerializeField] AudioClip deathClip;
	[SerializeField] AudioClip eatDrinkClip;

	[Header("Flat-Gravity Fallback (interior scenes with no CelestialBody)")]
	[Tooltip("When the scene has no NBodySimulation / CelestialBody (e.g. the Backrooms interior), apply a constant straight-down gravity and allow normal ground detection instead of the N-body gravity loop. Has NO effect in the real solar-system scenes, where a simulation always exists.")]
	[SerializeField] bool useFlatGravityFallback = true;
	[Tooltip("Downward acceleration (m/s²) used by the flat-gravity fallback. ~20 roughly matches the on-foot feel of standing on Humble Abode.")]
	[SerializeField] float flatGravity = 20f;

	// Private
	Rigidbody rb;
	Ship spaceship;
	CapsuleCollider capsuleCollider;

	float yaw;
	float pitch;
	float smoothYaw;
	float smoothPitch;

	float smoothYawOld;

	float yawSmoothV;
	float pitchSmoothV;

	Vector3 targetVelocity;
	Vector3 cameraLocalPos;
	Vector3 smoothVelocity;
	Vector3 smoothVRef;

	// Surface normal under the feet, captured by IsGrounded()'s spherecast and
	// used to project the walk vector onto the slope (see HandleMovement). Defaults
	// to transform.up so the projection is a no-op while airborne / on flat ground.
	Vector3 _groundNormal = Vector3.up;

	// Max ground steepness (degrees, measured against gravity-up) the player grips
	// without sliding. At or below this the grounded grip cancels the gravity-
	// induced down-slope drift; above it the player slides off (steep cliffs).
	// Non-serialized so it never reorders the serialized field block — tune here.
	float slopeLimitAngle = 35f;

	// Grounded-grip tuning. Below slideStopSpeed the along-slope velocity is the slow
	// gravity drift, cancelled outright so the player never creeps down a walkable hill.
	// Above it (real landing momentum — e.g. after a running jetpack jump) the velocity
	// is bled off over momentumFriction seconds so the player slides to a natural stop
	// instead of an instant clunk. Non-serialized — tune here.
	float slideStopSpeed = 1.5f;
	float momentumFriction = 0.3f;

	bool isGrounded;
	bool jumpQueued;
	bool jetpackQueued;

	// Grounded state at the end of the previous FixedUpdate — drives the
	// ground↔air momentum handoff in HandleMovement.
	bool _wasGroundedPhys;
	// Raw local-space move input stashed by HandleInput (Update) for the
	// velocity-based air-control block in HandleMovement (FixedUpdate).
	Vector3 _moveInputLocal;
	// Airborne WASD acceleration (m/s²) toward the input direction, capped at
	// walkSpeed along that direction. Non-serialized so it never reorders the
	// serialized field block — tune here. 24 ≈ 0 → walkSpeed in a third of a second.
	float airAcceleration = 24f;
	// Underwater locomotion. Swim speed is walkSpeed × this fraction — i.e. half
	// of normal ground walking (2× slower), per design. kSwimAcceleration is how
	// fast that swim speed is reached (m/s² of velocity change toward the look
	// direction). Non-serialized so they never reorder the serialized block.
	const float kSwimSpeedFraction = 0.5f;
	const float kSwimAcceleration  = 20f;
	// Zero-alloc scratch for the wall-overlap rescue's OverlapCapsuleNonAlloc.
	readonly Collider[] _overlapHits = new Collider[8];

	AudioSource sfxSource;
	AudioSource footstepsSource;
	AudioSource upBoostSource;
	AudioSource downBoostSource;
	AudioSource dirBoostSource;
	AudioSource waterSource;
	AudioClip _splashClip;   // one-shot splash on first water entry (StreamingAssets)
	float airborneTime = 0f;
	bool wasAirborne = false;
	int currentFootstepClipIndex = 0;
	float nextFootstepSwapTime = 0f;
	int waterTouches = 0;
	// Most recent waterline trigger the player is touching. Cached on
	// OnTriggerEnter so the half-body submersion check below doesn't need a
	// per-frame search. Cleared once waterTouches drops back to 0.
	SphereCollider waterCollider;
	Transform waterTransform;

	// Upward Jetpack
	bool usingJetpack;
	float jetpackFuelPercent = 1;
	float lastJetpackUseTime;

	// Downward Thrust
	bool usingDownThrust;
	float downThrustFuelPercent = 1;
	float lastDownThrustUseTime;

	// Directional Thrust
	float dirThrustFuelPercent = 1;
	float lastDirThrustUseTime;

	CelestialBody referenceBody;

	// Flat-gravity fallback state. _hasGravitySim is cached once in Awake so we
	// never call NBodySimulation.Bodies (which NREs when no simulation exists in
	// the scene). _flatActive is recomputed each FixedUpdate and read by IsGrounded.
	bool _hasGravitySim;
	bool _flatActive;

	// Magnitude (m/s²) of the gravity the FixedUpdate gravity step applied last
	// frame — flatGravity in interiors, the nearest body's pull on a planet.
	// Cached here so the swim/buoyancy code (which runs in HandleMovement, BEFORE
	// gravity is recomputed) can scale its upward forces to the ACTUAL gravity.
	// Without this, the flooded-interior 20 m/s² fallback buries the water forces
	// that were tuned against ~1 g and the player can't swim up. One-frame lag is
	// negligible. Seeded to standard gravity so the first frame isn't a no-op.
	float _lastGravityMag = 9.81f;

	Camera cam;
	bool readyToFlyShip;
	bool debug_playerFrozen;
	public static bool isInDialogue;
	public static bool isMapOpen;
	public static bool isInModalSlotUI;
	Animator animator;

	/// True while the player is holding O with the jetpack unlocked, off the
	/// ground, in range of a non-Sun body, and matching its circular orbit
	/// (radial speed < 0.5 m/s). Mirror of Ship.IsOrbitMatched — HAL polls
	/// both to fire "orbit matched / unmatched" lines.
	public bool IsOrbitMatched { get; private set; }

	/// True while the player is actively running the circularize routine
	/// (holding O, jetpack unlocked, airborne, in range of a non-Sun body).
	/// Mirror of Ship.IsCircularizing — drives FlightAssistStatusHUD's
	/// ORBIT MATCHED / UNMATCHED line for the player just like for ships.
	public bool IsCircularizing { get; private set; }

	// (JetpackUnlocked accessor already exists below near UnlockJetpack —
	//  no need to redeclare it.)

	void Awake()
	{
		isInDialogue = false;
		isMapOpen = false;
		isInModalSlotUI = false;
		// Force near-full vertical look on foot. The serialized pitchMinMax kept reverting
		// to a restrictive (-80, 65) via scene/prefab serialization, cutting off looking
		// up/down ~25-30°; setting it here guarantees the range no matter what's serialized.
		// (Piloting is unaffected — the ship has free 6DOF look.)
		pitchMinMax = new Vector2(-89f, 89f);
		if (dirThrustFillRect == null && upThrustFillRect != null)
			dirThrustFillRect = upThrustFillRect.parent.Find("DirBarFill") as RectTransform;
		cam = GetComponentInChildren<Camera>();
		cameraLocalPos = cam.transform.localPosition;
		// Push the eyes forward a little so looking straight down (now that full down-look
		// is allowed) doesn't clip the near plane into the astronaut's own body. Applied to
		// the live camera too so CameraTransformFX captures the pushed-out base.
		cameraLocalPos += Vector3.forward * 0.2f;
		cam.transform.localPosition = cameraLocalPos;
		spaceship = FindObjectOfType<Ship>();
		_hasGravitySim = FindObjectOfType<NBodySimulation>() != null;
		capsuleCollider = GetComponent<CapsuleCollider>();
		InitRigidbody();

		animator = GetComponentInChildren<Animator>();
		inputSettings.Begin();

		GameObject sfxObj = new GameObject("PlayerSFX");
		sfxObj.transform.SetParent(transform);
		sfxObj.transform.localPosition = Vector3.zero;
		sfxSource = sfxObj.AddComponent<AudioSource>();
		sfxSource.playOnAwake = false;

		GameObject stepsObj = new GameObject("PlayerFootsteps");
		stepsObj.transform.SetParent(transform);
		stepsObj.transform.localPosition = Vector3.zero;
		footstepsSource = stepsObj.AddComponent<AudioSource>();
		footstepsSource.playOnAwake = false;
		footstepsSource.loop = true;
		footstepsSource.volume = footstepVolume;

		upBoostSource   = CreateLoopAudioSource("PlayerUpBoost",   upBoostVolume);
		downBoostSource = CreateLoopAudioSource("PlayerDownBoost", downBoostVolume);
		dirBoostSource  = CreateLoopAudioSource("PlayerDirBoost",  dirBoostVolume);
		waterSource     = CreateLoopAudioSource("PlayerWater",     waterMoveVolume);
		// Splash one-shot for the first jump into water (loaded from StreamingAssets).
		StartCoroutine(StreamingAudio.Load("Audio/WaterSplash.wav", AudioType.WAV, c => _splashClip = c));
	}

	void OnTriggerEnter(Collider other)
	{
		if (other.CompareTag("Water"))
		{
			// Splash on the FIRST contact (0 → 1). It re-arms only once the player
			// has fully left the water (waterTouches back to 0), so wading at a
			// shoreline doesn't re-trigger it every step.
			if (waterTouches == 0 && _splashClip != null && waterSource != null)
				waterSource.PlayOneShot(_splashClip, 0.9f);
			waterTouches++;
			var sc = other as SphereCollider;
			if (sc != null) { waterCollider = sc; waterTransform = other.transform; }
		}
	}

	void OnTriggerExit(Collider other)
	{
		if (other.CompareTag("Water"))
		{
			waterTouches = Mathf.Max(0, waterTouches - 1);
			if (waterTouches == 0) { waterCollider = null; waterTransform = null; }
		}
	}

	// True only when the player's CENTRE (rb.position ≈ hip / chest height)
	// is below the water surface — i.e. ~half the body or more is in water.
	// Just touching the trigger with feet doesn't count, so wading at the
	// shoreline doesn't trigger swim physics or block jumping.
	bool IsHalfSubmerged()
	{
		// Flooding Poolrooms: a flat, rising water plane rather than the spherical
		// ocean model. Submerged once the capsule centre (rb.position ≈ chest) is below
		// the surface — this feeds the same buoyancy/swim/velocity-cap code below with
		// no need for a Water-tagged sphere.
		if (PoolFlood.Active && rb.position.y < PoolFlood.SurfaceY) return true;

		if (waterTouches == 0 || waterCollider == null || waterTransform == null) return false;
		float distFromCenter = (rb.position - waterTransform.position).magnitude;
		float waterRadius = waterCollider.radius * waterTransform.lossyScale.x;
		return distFromCenter < waterRadius;
	}

	AudioSource CreateLoopAudioSource(string name, float volume)
	{
		GameObject obj = new GameObject(name);
		obj.transform.SetParent(transform);
		obj.transform.localPosition = Vector3.zero;
		AudioSource src = obj.AddComponent<AudioSource>();
		src.playOnAwake = false;
		src.loop = true;
		src.volume = volume;
		return src;
	}

	void UpdateLoopAudio(AudioSource src, AudioClip clip, bool active, float volume)
	{
		if (src == null) return;
		if (active && clip != null)
		{
			if (!src.isPlaying || src.clip != clip)
			{
				src.clip = clip;
				src.volume = volume;
				src.Play();
			}
		}
		else if (src.isPlaying)
		{
			src.Stop();
		}
	}

	void InitRigidbody()
	{
		rb = GetComponent<Rigidbody>();
		rb.interpolation = RigidbodyInterpolation.Interpolate;
		rb.useGravity = false;
		rb.isKinematic = false;
		rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
		rb.mass = mass;
		rb.velocity = Vector3.zero;
	}

	void Start()
	{
		if (spawnPoint != null)
		{
			rb.position = spawnPoint.position;
			rb.rotation = spawnPoint.rotation;
		}
	}

	void Update()
	{
		if (Time.timeScale == 0)
		{
			return;
		}

		// Don't let typed letters double as movement input when the
		// phone's AI chat input field has focus.
		if (AIChatScreen.IsTypingActive) return;

		HandleInput();

		// Refuel upward jetpack
		if (Time.time - lastJetpackUseTime > jetpackRefuelDelay)
		{
			jetpackFuelPercent = Mathf.Clamp01(jetpackFuelPercent + Time.deltaTime / jetpackRefuelTime);
		}

		// Refuel downward thrust
		if (Time.time - lastDownThrustUseTime > downThrustRefuelDelay)
		{
			downThrustFuelPercent = Mathf.Clamp01(downThrustFuelPercent + Time.deltaTime / downThrustDuration);
		}

		// Refuel directional thrust
		if (Time.time - lastDirThrustUseTime > dirThrustRefuelDelay)
		{
			dirThrustFuelPercent = Mathf.Clamp01(dirThrustFuelPercent + Time.deltaTime / dirThrustRefuelTime);
		}

		if (upThrustFillRect) upThrustFillRect.localScale = new Vector3(jetpackFuelPercent, 1, 1);
		if (downThrustFillRect) downThrustFillRect.localScale = new Vector3(downThrustFuelPercent, 1, 1);
		if (dirThrustFillRect) dirThrustFillRect.localScale = new Vector3(dirThrustFuelPercent, 1, 1);

		// Handle animations
		float currentSpeed = smoothVelocity.magnitude;
		float animationSpeedPercent = (currentSpeed <= walkSpeed) ? currentSpeed / walkSpeed / 2 : currentSpeed / runSpeed;
		animator.SetBool("Grounded", isGrounded);
		animator.SetFloat("Speed", animationSpeedPercent);

		UpdateFootstepLoop();
		UpdateWaterLoop();
	}

	void UpdateWaterLoop()
	{
		bool inWater = waterTouches > 0;
		bool inputHeld = Mathf.Abs(TutorialGate.MoveAxisHorizontal(TutorialAbility.Move)) > 0.1f
		              || Mathf.Abs(TutorialGate.MoveAxisVertical(TutorialAbility.Move)) > 0.1f;
		bool active = inWater && inputHeld && !isInDialogue && !isMapOpen && !isInModalSlotUI;
		UpdateLoopAudio(waterSource, waterMoveClip, active, waterMoveVolume);
	}

	void UpdateFootstepLoop()
	{
		if (footstepsSource == null) return;

		bool moving = isGrounded && !isInDialogue && !isMapOpen && !isInModalSlotUI && smoothVelocity.magnitude > 0.5f && waterTouches == 0;
		if (!moving)
		{
			if (footstepsSource.isPlaying) footstepsSource.Stop();
			return;
		}

		bool hasA = footstepWalkClipA != null;
		bool hasB = footstepWalkClipB != null;
		if (!hasA && !hasB)
		{
			if (footstepsSource.isPlaying) footstepsSource.Stop();
			return;
		}

		// Sprint detection for footstep pitch — accept LeftShift OR L-stick click (controller).
		bool sprinting = Input.GetKey(KeyCode.LeftShift) ||
			TutorialGate.PadHeld(TutorialGate.PadButton.L3);
		footstepsSource.pitch  = sprinting ? sprintPitchMultiplier : 1f;
		footstepsSource.volume = footstepVolume;

		bool needsStart = !footstepsSource.isPlaying;
		bool timeToSwap = Time.time >= nextFootstepSwapTime;

		if (needsStart || timeToSwap)
		{
			// Pick the next clip — alternate when both are available, else stick with the one we have
			if (hasA && hasB)
				currentFootstepClipIndex = needsStart ? Random.Range(0, 2) : 1 - currentFootstepClipIndex;
			else
				currentFootstepClipIndex = hasA ? 0 : 1;

			AudioClip target = (currentFootstepClipIndex == 0) ? footstepWalkClipA : footstepWalkClipB;
			float startTime = (target.length > 0.05f) ? Random.Range(0f, target.length - 0.05f) : 0f;
			footstepsSource.clip = target;
			footstepsSource.time = startTime;
			footstepsSource.Play();

			float minSwap = Mathf.Max(0.05f, footstepSwapInterval.x);
			float maxSwap = Mathf.Max(minSwap, footstepSwapInterval.y);
			nextFootstepSwapTime = Time.time + Random.Range(minSwap, maxSwap);
		}
	}

	void HandleInput()
	{
		HandleEditorInput();

		// Block look + movement when ANY UI Selectable is focused — otherwise the
		// controller's left stick would both navigate the menu AND walk the player,
		// and the right stick would rotate the camera while the player is meant to
		// be reading a panel.
		bool uiHasFocus = TutorialGate.UISelectionActive();

		// Phone home screen explicitly clears EventSystem selection on open, so
		// uiHasFocus is false even while the phone is up — without this extra
		// gate the player's mouse would still rotate the camera under the phone.
		// PlayerPhoneUI.LookBlocked is true on the home screen and false in
		// camera mode (where the player IS meant to aim the lens).
		bool phoneBlocksLook = PlayerPhoneUI.LookBlocked;

		// Look input — blocked during dialogue or map so camera stays still while UI panels are open.
		// Mouse and right-stick are accumulated separately because each has its own sensitivity slider.
		if (!isInDialogue && !isMapOpen && !isInModalSlotUI && !uiHasFocus && !phoneBlocksLook)
		{
			yaw   += TutorialGate.GetAxisRaw("Mouse X", TutorialAbility.MouseLook) * inputSettings.mouseSensitivity / 10 * mouseSensitivityMultiplier;
			pitch -= TutorialGate.GetAxisRaw("Mouse Y", TutorialAbility.MouseLook) * inputSettings.mouseSensitivity / 10 * mouseSensitivityMultiplier;

			if (TutorialGate.ControllerEnabled && TutorialGate.IsUnlocked(TutorialAbility.MouseLook))
			{
				// Right-stick produces a steady -1..1 reading; multiply by an
				// absolute degrees-per-second base so the value the player sees
				// in the StickLookSensitivity slider corresponds directly to
				// "how snappy" the look feels — independent of the (much smaller)
				// mouse-sensitivity slider.
				const float kStickDegreesPerSecond = 360f;
				float gain = TutorialGate.StickLookSensitivity * kStickDegreesPerSecond * Time.unscaledDeltaTime;
				yaw   += TutorialGate.RightStickX() * gain;
				pitch -= TutorialGate.RightStickY() * gain * (TutorialGate.InvertLookY ? -1f : 1f);
			}

			pitch = Mathf.Clamp(pitch, pitchMinMax.x, pitchMinMax.y);
		}
		float mouseSmoothTime = Mathf.Lerp(0.01f, maxMouseSmoothTime, inputSettings.mouseSmoothing);
		smoothPitch = Mathf.SmoothDampAngle(smoothPitch, pitch, ref pitchSmoothV, mouseSmoothTime);
		smoothYawOld = smoothYaw;
		smoothYaw = Mathf.SmoothDampAngle(smoothYaw, yaw, ref yawSmoothV, mouseSmoothTime);

		// Movement input — blocked during dialogue
		isGrounded = IsGrounded();

		// Landing SFX: only when transitioning from airborne to grounded after >= threshold airborne time.
		// Suppressed when touching water — jumping in from the bank registers as a
		// "landing", but the water-entry splash (OnTriggerEnter) should play instead.
		bool justLanded = isGrounded && wasAirborne;
		if (justLanded && airborneTime >= minAirborneForLandSound && landClip != null && sfxSource != null && waterTouches == 0)
			sfxSource.PlayOneShot(landClip, landVolume);
		if (justLanded)
		{
			OnLanded?.Invoke();
			if (airborneTime >= minAirborneForLandSound)
				GamepadRumble.Pulse(Mathf.Clamp01(airborneTime * 0.4f), 0.15f, 0.15f);
		}
		if (isGrounded) airborneTime = 0f;
		else airborneTime += Time.deltaTime;
		wasAirborne = !isGrounded;

		// AIChatScreen.IsTypingActive — phone AI chat is open and the
		// player is typing into the input field. W/A/S/D/Q/E etc. are
		// being typed as TEXT, not movement; jump (Space) is being typed
		// as a literal space. Gate every input read in this method on the
		// typing flag so the player doesn't walk, jetpack, sprint, or
		// fire while composing a message.
		if (isInDialogue || isMapOpen || isInModalSlotUI || uiHasFocus || AIChatScreen.IsTypingActive || TutorialGate.WasUIFocusedThisFrameStart())
		{
			targetVelocity = Vector3.zero;
			smoothVelocity = Vector3.zero;
			smoothVRef     = Vector3.zero;
			_moveInputLocal = Vector3.zero;
			return;
		}
		// Jump button = Space OR Xbox A. Ability gating depends on grounded state
		// (Jump while on the ground, Boost while airborne). Suppressed while
		// half-submerged in water — Space is reclaimed by the swim-up logic
		// in HandleMovement so neither jump nor jetpack should fire.
		bool jumpButtonDown = Input.GetKeyDown(KeyCode.Space) ||
			TutorialGate.PadPressed(TutorialGate.PadButton.A);
		if (jumpButtonDown && !IsHalfSubmerged())
		{
			if (isGrounded)
			{
				if (TutorialGate.IsUnlocked(TutorialAbility.Jump)) jumpQueued = true;
			}
			else
			{
				if (TutorialGate.IsUnlocked(TutorialAbility.Boost)) jetpackQueued = true;
			}
		}

		Vector3 input = new Vector3(
			TutorialGate.MoveAxisHorizontal(TutorialAbility.Move),
			0,
			TutorialGate.MoveAxisVertical(TutorialAbility.Move));
		bool running = TutorialGate.SprintHeld(TutorialAbility.Move) && isGrounded;
		IsSprinting = running;
		// Grounded movement stays on the MovePosition walk pipeline
		// (smoothVelocity, below). AIRBORNE movement is now VELOCITY-based:
		// the walk pipeline's target drops to zero in air, and HandleMovement's
		// air-control block accelerates rb.velocity toward the stashed input
		// instead. This is the momentum fix — on leaving the ground the walk
		// velocity is transferred into rb.velocity (see the ground↔air handoff
		// in HandleMovement), so releasing the keys mid-jump keeps you coasting
		// exactly like letting go of the jetpack does, and holding back
		// genuinely brakes. The old air-WASD-via-MovePosition displacement
		// (a per-tick teleport that vanished on key release) is gone; it was
		// also the main CCD bypass that let sprint-jumps tunnel through walls.
		_moveInputLocal = input;
		targetVelocity = isGrounded
			? transform.TransformDirection(input.normalized) * ((running) ? runSpeed : walkSpeed) * introMoveScale
			: Vector3.zero;
		smoothVelocity = Vector3.SmoothDamp(smoothVelocity, targetVelocity, ref smoothVRef, (isGrounded) ? vSmoothTime : airSmoothTime);
	}

	// Runtime walk/run-speed multiplier the Mission 1 intro uses to hand control
	// back at half pace (groggy first steps) before clearing it to full speed at
	// the end of the briefing. Left at 1 in all normal play. NonSerialized so it
	// never persists to scene/prefab data — it's a pure runtime knob.
	[System.NonSerialized] public float introMoveScale = 1f;

	// Cinematic up override (e.g. the shuttle-landing intro): while set, the
	// FixedUpdate alignment uses this transform's up instead of the gravity /
	// ship-up blend, so the whole camera pipeline (which snapshots the
	// FixedUpdate rotation) stays consistent with the ride. Static so a
	// prefab-resident sequence can set it without a scene reference. The
	// setter MUST clear it when done.
	[System.NonSerialized] public static Transform UpOverrideTransform;

	// Snaps the look so the camera aims at a world point. Used by the Mission 1
	// wake-up intro to hold the player's gaze on the cabin photo while look is
	// locked. Horizontal heading rotates the body; vertical is the camera pitch.
	// Safe to call every frame — it recomputes absolute aim each call, so it
	// self-corrects against planet rotation / settling and never drifts.
	public void ForceLookAt(Vector3 worldPoint)
	{
		if (cam == null) return;
		Vector3 up = transform.up;
		Vector3 toTarget = worldPoint - cam.transform.position;
		if (toTarget.sqrMagnitude < 1e-6f) return;

		// Horizontal: rotate the body so its forward faces the target in the up-plane.
		Vector3 flatTo = Vector3.ProjectOnPlane(toTarget, up);
		if (flatTo.sqrMagnitude > 1e-6f)
		{
			Vector3 flatFwd = Vector3.ProjectOnPlane(transform.forward, up);
			float headingDelta = Vector3.SignedAngle(flatFwd, flatTo, up);
			transform.Rotate(up * headingDelta, Space.World);
		}

		// Vertical: camera pitch = -elevation (localEulerAngles.x positive looks down).
		Vector3 dir = (worldPoint - cam.transform.position).normalized;
		float elevationDeg = Mathf.Asin(Mathf.Clamp(Vector3.Dot(dir, up), -1f, 1f)) * Mathf.Rad2Deg;
		pitch = Mathf.Clamp(-elevationDeg, pitchMinMax.x, pitchMinMax.y);
		smoothPitch = pitch;
		pitchSmoothV = 0f;
	}

	// Like ForceLookAt, but eases the aim toward the target instead of snapping.
	// Call every frame. Returns true once the aim is on-target (within ~1°), so
	// callers can hand control back at the moment the gaze lands.
	//
	// Yaw and pitch move by the SAME fraction of their remaining error each frame,
	// so the camera sweeps in a straight diagonal onto the target rather than
	// finishing one axis then the other (the "up, then over" L-shape). The cap is
	// applied to whichever axis has further to go, so the turn never exceeds
	// degreesPerSecond.
	//
	// IMPORTANT: this nudges the SAME yaw/pitch look targets the player's own
	// input drives, so the existing smoothing pipeline (smoothYaw → body heading
	// in HandleMovement, smoothPitch → camera pitch) does the actual turning.
	// Writing transform.rotation / smoothPitch directly here instead would fight
	// that per-frame pipeline and produce only a tiny jitter.
	public bool ForceLookAtSmooth(Vector3 worldPoint, float degreesPerSecond)
	{
		if (cam == null) return false;
		Vector3 up = transform.up;
		Vector3 toTarget = worldPoint - cam.transform.position;
		if (toTarget.sqrMagnitude < 1e-6f) return true;

		// Heading error (in the up-plane) and pitch error to the target.
		float headingDelta = 0f;
		Vector3 flatTo = Vector3.ProjectOnPlane(toTarget, up);
		if (flatTo.sqrMagnitude > 1e-6f)
		{
			Vector3 flatFwd = Vector3.ProjectOnPlane(transform.forward, up);
			headingDelta = Vector3.SignedAngle(flatFwd, flatTo, up);
		}
		Vector3 dir = toTarget.normalized;
		float elevationDeg = Mathf.Asin(Mathf.Clamp(Vector3.Dot(dir, up), -1f, 1f)) * Mathf.Rad2Deg;
		float targetPitch = Mathf.Clamp(-elevationDeg, pitchMinMax.x, pitchMinMax.y);
		float pitchDelta = Mathf.DeltaAngle(pitch, targetPitch);

		float maxAbs = Mathf.Max(Mathf.Abs(headingDelta), Mathf.Abs(pitchDelta));
		const float alignedDeg = 1f;
		if (maxAbs < 0.01f) return true;

		float maxStep = degreesPerSecond * Time.unscaledDeltaTime;
		float frac = Mathf.Min(1f, maxStep / maxAbs);   // same fraction on both axes → straight sweep
		yaw   += headingDelta * frac;
		pitch += pitchDelta  * frac;

		return maxAbs <= alignedDeg;
	}

	void HandleMovement()
	{
		if (!debug_playerFrozen && Time.timeScale > 0)
		{
			cam.transform.localEulerAngles = Vector3.right * smoothPitch;
			transform.Rotate(Vector3.up * Mathf.DeltaAngle(smoothYawOld, smoothYaw), Space.Self);
		}

		if (isInDialogue) return;

		// Typing-active gate. The HandleInput early-return already zeros
		// targetVelocity and skips queueing jump/jetpack, but HandleMovement
		// reads several inputs directly (DownThrust Ctrl, DirThrust Shift,
		// fine-thrust WASD, etc). Compute this once and AND it into each
		// input-read site below so a chat user pressing Ctrl/Shift/Space/
		// W/A/S/D as text doesn't trigger thrust, FOV, or boost.
		bool typing = AIChatScreen.IsTypingActive;

		// ── Ground ↔ air momentum handoff ─────────────────────────────────
		// Walking moves via rb.MovePosition (displacement that never enters
		// rb.velocity), so historically a running jump had NO horizontal
		// momentum — release the key mid-air and you stopped dead. On the
		// grounded→airborne transition (ledge walk-off; the jump branch below
		// does its own inline transfer) we convert the walk velocity into real
		// rb.velocity so physics conserves it. On the airborne→grounded
		// transition we do the reverse: seed the walk pipeline from the
		// tangential landing momentum (clamped to run speed) and remove that
		// share from rb.velocity, so landing while holding W continues at full
		// stride with no double-counted speed surge; any excess above run
		// speed stays in rb.velocity for the grounded grip to bleed off
		// naturally (momentumFriction slide).
		if (_wasGroundedPhys != isGrounded)
		{
			if (!isGrounded)
			{
				rb.AddForce(smoothVelocity, ForceMode.VelocityChange);
				targetVelocity = Vector3.zero;
				smoothVelocity = Vector3.zero;
				smoothVRef     = Vector3.zero;
			}
			else
			{
				Vector3 refVelLand = referenceBody != null ? referenceBody.velocity : Vector3.zero;
				Vector3 tangLand   = Vector3.ProjectOnPlane(rb.velocity - refVelLand, _groundNormal);
				Vector3 seed       = Vector3.ClampMagnitude(tangLand, runSpeed);
				smoothVelocity = seed;
				smoothVRef     = Vector3.zero;
				rb.velocity   -= seed;
			}
		}

		// Grounded state
		if (isGrounded)
		{
			if (jumpQueued)
			{
				if (jumpClip != null && sfxSource != null)
					sfxSource.PlayOneShot(jumpClip, jumpVolume);
				// Jump impulse + the walk velocity in the same VelocityChange —
				// the horizontal stride becomes real momentum the instant the
				// feet leave the ground (see handoff comment above).
				rb.AddForce(transform.up * jumpForce + smoothVelocity, ForceMode.VelocityChange);
				targetVelocity = Vector3.zero;
				smoothVelocity = Vector3.zero;
				smoothVRef     = Vector3.zero;
				isGrounded = false;
			}
			else if (!IsHalfSubmerged())
			{
				// Grounded grip — replaces the old constant downward "stick"
				// VelocityChange impulse, which felt heavy/"locked" on touchdown
				// (a per-tick downward yank) AND still let the player slide downhill.
				//
				// On a walkable slope (steepness <= slopeLimitAngle) we cancel ONLY
				// the ALONG-slope component of the body-relative velocity — the
				// gravity-induced slide — while keeping the into-surface component so
				// ground contact and landings stay natural (gravity does that, not an
				// impulse). Walking is unaffected: it runs through MovePosition, not
				// rb.velocity. On slopes steeper than slopeLimitAngle we DON'T grip,
				// so steep cliffs still slide the player off as before.
				//
				// Skipped while half-submerged so it can't fight the swim/buoyancy
				// velocity handling on the seabed.
				float slopeAngle = Vector3.Angle(_groundNormal, transform.up);
				if (slopeAngle <= slopeLimitAngle)
				{
					Vector3 refVel = referenceBody != null ? referenceBody.velocity : Vector3.zero;
					Vector3 relVel = rb.velocity - refVel;
					// Split into into-surface (normal) and along-slope (tangential).
					Vector3 normalComp = Vector3.Project(relVel, _groundNormal);
					Vector3 tangComp   = relVel - normalComp;
					// Keep the normal component untouched — gravity/collision own the
					// vertical, so the player settles onto the ground the same frame they
					// land (no hover-and-sink). Only the ALONG-slope velocity is managed:
					//   • slow (gravity drift) → cancel, so you don't creep down hills;
					//   • fast (real momentum) → bleed off over momentumFriction seconds,
					//     so a fast landing slides to a natural stop, not an instant clunk.
					if (tangComp.magnitude < slideStopSpeed)
						tangComp = Vector3.zero;
					else
						tangComp *= Mathf.Exp(-Time.fixedDeltaTime / Mathf.Max(0.0001f, momentumFriction));
					rb.velocity = refVel + normalComp + tangComp;
				}
			}
		}
		else
		{
			// Press (and hold) spacebar while above ground to engage jetpack
			if (jetpackQueued)
			{
				usingJetpack = true;
			}
			// Press (and hold) Ctrl OR Left-Trigger while above ground to engage downward thrust
			if (!typing && TutorialGate.DownThrustPressed(TutorialAbility.DownThrust))
			{
				usingDownThrust = true;
			}

			// Plain air control — velocity-based so momentum persists (parity
			// with jetpack thrust). Accelerates the frame-relative velocity
			// toward the input direction, but never past walkSpeed ALONG that
			// direction: a running jump keeps its 14 m/s launch speed (holding
			// W adds nothing on top), releasing every key keeps you coasting,
			// and holding S genuinely brakes at airAcceleration. Quake-style
			// air accelerate, replacing the old MovePosition air-WASD teleport.
			// Frame of reference: the nearby orbiting ship while the proximity
			// assist is engaged (so WASD near a ship trims RELATIVE to the
			// ship — orbital speeds would otherwise saturate the cap), else
			// the reference body.
			if (!typing && !IsHalfSubmerged() && _moveInputLocal.sqrMagnitude > 0.01f)
			{
				Vector3 wishDir = transform.TransformDirection(_moveInputLocal.normalized);
				Vector3 frameVel;
				if (_cachedNearestShipInRange != null && !_cachedNearestShipInRange.IsLanded)
				{
					var frameRb = _cachedNearestShipInRange.GetComponent<Rigidbody>();
					frameVel = frameRb != null ? frameRb.velocity : Vector3.zero;
				}
				else
				{
					frameVel = referenceBody != null ? referenceBody.velocity : Vector3.zero;
				}
				float curAlongWish = Vector3.Dot(rb.velocity - frameVel, wishDir);
				float cap = walkSpeed * introMoveScale;
				float add = Mathf.Min(airAcceleration * Time.fixedDeltaTime, Mathf.Max(0f, cap - curAlongWish));
				if (add > 0f)
					rb.AddForce(wishDir * add, ForceMode.VelocityChange);
			}
		}
		jumpQueued = false;
		jetpackQueued = false;

		bool upBoostActive = false;
		bool downBoostActive = false;
		bool dirBoostActive = false;

		// Upward Jetpack — gated by jetpackUnlocked (purchased from Alien7).
		if (jetpackUnlocked && usingJetpack && !typing && TutorialGate.JumpHeld(TutorialAbility.Boost) && jetpackFuelPercent > 0)
		{
			lastJetpackUseTime = Time.time;
			jetpackFuelPercent -= Time.deltaTime / jetpackDuration;
			rb.AddForce(transform.up * jetpackForce, ForceMode.Acceleration);
			upBoostActive = true;
		}
		else
		{
			usingJetpack = false;
		}

		// Downward Thrust — gated by jetpackUnlocked.
		if (jetpackUnlocked && usingDownThrust && !typing && TutorialGate.DownThrustHeld(TutorialAbility.DownThrust) && downThrustFuelPercent > 0)
		{
			lastDownThrustUseTime = Time.time;
			downThrustFuelPercent -= Time.deltaTime / downThrustDuration;
			rb.AddForce(-transform.up * downThrustForce, ForceMode.Acceleration);
			downBoostActive = true;
		}
		else
		{
			usingDownThrust = false;
		}

		// Directional Thrust (airborne + Shift OR Right-Trigger + WASD/left-stick) — gated by jetpackUnlocked.
		bool dirThrustHeld = !typing && TutorialGate.DirectionalThrustHeld(TutorialAbility.DirectionalThrust);
		if (jetpackUnlocked && !isGrounded && dirThrustHeld && dirThrustFuelPercent > 0)
		{
			// Left stick + keyboard only — the legacy Horizontal/Vertical axes
			// leak the D-pad / right stick via a stray InputManager axis-5/6 map.
			Vector3 inputVec = new Vector3(
				TutorialGate.MoveAxisHorizontal(TutorialAbility.DirectionalThrust),
				0,
				TutorialGate.MoveAxisVertical(TutorialAbility.DirectionalThrust));
			if (inputVec.magnitude > 0.1f)
			{
				lastDirThrustUseTime = Time.time;
				dirThrustFuelPercent -= Time.deltaTime / dirThrustDuration;
				rb.AddForce(transform.TransformDirection(inputVec.normalized) * dirThrustForce, ForceMode.Acceleration);
				dirBoostActive = true;
			}
		}

		// Ship-proximity velocity matching. When the player is on foot,
		// airborne, AT LEAST shipProximityAltitudeMin metres above the
		// nearest body's surface, AND within shipProximityRadius of a
		// Ship, gently damp the player's velocity toward the ship's
		// velocity. Altitude gate keeps the effect from triggering on
		// the ground next to a parked ship — only fires in actual orbit
		// where it makes net-collection trivial. Cache refreshed every
		// 0.2s — FindObjectsOfType is too expensive at 50 Hz but cheap
		// at 5 Hz. Damping uses ForceMode.Acceleration so it stacks
		// linearly with WASD/jetpack thrust — the player can still
		// thrust against it, the damping just bleeds the relative-
		// velocity residual.
		//
		if (!isGrounded)
		{
			// Refresh on schedule (0.2s) OR immediately if the cache is
			// null (player just re-entered range after a previous fade-out
			// fully decayed). The null-check force-refresh keeps damping
			// resumption snappy without polling per FixedUpdate.
			if (_cachedNearestShipInRange == null || Time.fixedTime >= _nextShipProximityCheckTime)
			{
				_nextShipProximityCheckTime = Time.fixedTime + ShipProximityCheckInterval;
				_cachedNearestShipInRange = FindNearestShipInRange();
			}
			// Activate damping + rotation alignment only when the cached
			// ship is in range AND actually orbiting (not landed on a body).
			// The old altitude-min gate keyed on PLAYER altitude failed in
			// two ways: (1) on small moons close orbits sit below the gate
			// and alignment never engaged, and (2) when altitude oscillated
			// around the threshold the gate flickered and the rotation
			// blend reversed direction each frame, which looked disorienting.
			// Ship.IsLanded is the binary, jitter-free signal we actually
			// want — it asks "is this ship sitting on a body" rather than
			// "is the player high enough off the ground."
			if (_cachedNearestShipInRange != null && !_cachedNearestShipInRange.IsLanded)
			{
				// Mirror the in-range ship into the rotation reference so
				// the blend-out can continue to read its up-vector after
				// the player drifts outside the 25 m radius (the damping
				// cache nulls instantly; the rotation ref lingers until
				// _shipUpBlend reaches 0).
				_shipUpRotationRef = _cachedNearestShipInRange;
				var shipRb = _cachedNearestShipInRange.GetComponent<Rigidbody>();
				// Skip the velocity braking while an eject is in progress (see
				// SuppressShipProximityDamp) — otherwise it cancels the blowout.
				if (shipRb != null && Time.fixedTime >= suppressShipDampUntil)
				{
					Vector3 deltaV = shipRb.velocity - rb.velocity;
					// AddForce with Acceleration adds force*dt to velocity.
					// Multiplying deltaV by shipProximityDampRate gives a
					// per-second convergence-fraction behaviour.
					rb.AddForce(deltaV * shipProximityDampRate, ForceMode.Acceleration);
				}
			}
		}
		// Note: when out of the activation envelope we DO NOT null
		// _cachedNearestShipInRange here. The rotation block in the outer
		// Update() owns the cache lifetime — it keeps the reference until
		// the smooth-blend fully decays so the fade-out slerp can still
		// read the ship's current up vector. The cache will refresh on
		// the next 0.2s tick once the envelope is active again.

		// Orbit-match (O) — player jetpack equivalent of the ship's
		// circularize. Hold O while airborne (off-ground) with the
		// jetpack unlocked to thrust toward the perfect-circular orbital
		// velocity at the current altitude. Drains dir-thrust fuel.
		// Mirrors Ship.cs's circularize block but on the player's rb.
		bool playerOrbitNowMatched = false;
		bool playerCircularizing = false;
		// Gate the orbit-match O key on chat typing — the user pressing 'o'
		// while writing a message must NOT engage circularize.
		bool oHeld = Input.GetKey(KeyCode.O) && !AIChatScreen.IsTypingActive;
		if (_hasGravitySim && jetpackUnlocked && !isGrounded && oHeld && dirThrustFuelPercent > 0f)
		{
			// Pick the closest body the player is in valid orbit-match range
			// of (rangeMul × radius outer, 1.05 × radius inner). The two-pass
			// pick (closest first, then check range) used to mis-fire when the
			// player was nearest to a body they were OUT of range of while
			// ALSO in range of a different body — the closer-but-OOR body won
			// the selection and orbit-match silently refused to engage. Fold
			// the range check INTO the selection loop so the closest IN-range
			// body always wins. With rangeMul=9 (was 3, user wanted 3× bigger
			// orbit envelopes), small moons like Constant Companion and large
			// planets can have overlapping ranges and this guard matters.
			const float rangeMul     = 9f;
			const float surfaceBuf   = 1.05f;
			CelestialBody best  = null;
			float bestSqr       = float.MaxValue;
			var bodies = NBodySimulation.Bodies;
			if (bodies != null)
			{
				for (int i = 0; i < bodies.Length; i++)
				{
					var b = bodies[i];
					if (b == null) continue;
					if (b.bodyType == CelestialBody.BodyType.Sun) continue;
					float dsq      = (b.Position - rb.position).sqrMagnitude;
					float maxRange = b.radius * rangeMul;
					float minRange = b.radius * surfaceBuf;
					if (dsq > maxRange * maxRange) continue; // out of orbit-match envelope
					if (dsq < minRange * minRange) continue; // too close (clipping surface)
					if (dsq < bestSqr) { bestSqr = dsq; best = b; }
				}
			}
			if (best != null)
			{
				Vector3 r = rb.position - best.Position;
				float rMag = r.magnitude;
				{
					Vector3 v = rb.velocity - best.velocity;
					Vector3 radialDir = r / rMag;
					Vector3 vRadial = Vector3.Dot(v, radialDir) * radialDir;
					Vector3 vTang = v - vRadial;
					float orbitRadialMag = vRadial.magnitude;

					float vCirc = Mathf.Sqrt(Universe.gravitationalConstant * best.mass / rMag);
					Vector3 tangDir = vTang.sqrMagnitude > 0.01f
						? vTang.normalized
						: Vector3.Cross(radialDir, Vector3.up).normalized;
					if (tangDir.sqrMagnitude < 0.01f)
						tangDir = Vector3.Cross(radialDir, Vector3.right).normalized;
					Vector3 vTargetWorld = tangDir * vCirc + best.velocity;
					Vector3 needed = vTargetWorld - rb.velocity;
					float needMag = needed.magnitude;

					if (needMag > 0.01f)
					{
						lastDirThrustUseTime = Time.time;
						dirThrustFuelPercent -= Time.deltaTime / dirThrustDuration;
						Vector3 force = needed.normalized * dirThrustForce;
						// Clamp so we don't overshoot zero in a single tick.
						float plannedDV = force.magnitude * Time.deltaTime;
						if (plannedDV > needMag) force *= needMag / plannedDV;
						rb.AddForce(force, ForceMode.Acceleration);
						dirBoostActive = true;
					}

					// In-range + holding O + jetpack unlocked = actively
					// circularizing (same definition Ship.cs uses).
					playerCircularizing = true;

					// Same threshold the ship uses (0.5 m/s radial speed).
					playerOrbitNowMatched = orbitRadialMag < 0.5f;
				}
			}
		}
		IsOrbitMatched   = playerOrbitNowMatched;
		IsCircularizing  = playerCircularizing;

		// ── Swimming ──────────────────────────────────────────────────────
		// Kicks in once at least half the body is below the water surface
		// (rb.position is the capsule centre). Three parts:
		//   1. WASD swims in the direction you're LOOKING (camera basis, so
		//      pitch counts — look straight up + W rises toward the surface),
		//      at roughly half ground walk speed (heavy-water feel).
		//   2. Constant buoyancy = a slow sink when idle; Space / pad-A adds a
		//      swim-up boost that overcomes gravity so you can climb out.
		//   3. Everything is clamped to a slow terminal speed RELATIVE to the
		//      reference body so an orbiting planet doesn't fly out from under
		//      the player while the cap zeroes their world-space velocity.
		//
		// Buoyancy + swim-up are scaled to the ACTUAL gravity (_lastGravityMag)
		// rather than being absolute: the serialized forces were tuned against
		// ~1 g planet seas, but the flooded Poolrooms interior runs a 20 m/s²
		// flat-gravity fallback that would otherwise bury them (net downward
		// even with Space held). On a planet _lastGravityMag ≈ 9.81 so gScale ≈
		// 1 and the feel is unchanged.
		if (IsHalfSubmerged())
		{
			Vector3 refVel = referenceBody != null ? referenceBody.velocity : Vector3.zero;

			// 1. Look-relative swim thrust, capped at half walk speed.
			float swimSpeed = walkSpeed * kSwimSpeedFraction * introMoveScale;
			if (!typing && cam != null && _moveInputLocal.sqrMagnitude > 0.01f)
			{
				Vector3 swimDir = cam.transform.TransformDirection(_moveInputLocal.normalized);
				float curAlong = Vector3.Dot(rb.velocity - refVel, swimDir);
				float add = Mathf.Min(kSwimAcceleration * Time.fixedDeltaTime, Mathf.Max(0f, swimSpeed - curAlong));
				if (add > 0f)
					rb.AddForce(swimDir * add, ForceMode.VelocityChange);
			}

			// 3. Heavy-water terminal-speed clamp (relative to the reference body).
			float cap = Mathf.Max(waterMaxSpeed, swimSpeed);
			Vector3 relV = rb.velocity - refVel;
			if (relV.sqrMagnitude > cap * cap)
				rb.velocity = refVel + relV.normalized * cap;

			// 2. Gravity-scaled buoyancy (slow sink) + Space/A swim-up boost.
			float gScale = _lastGravityMag / 9.81f;
			rb.AddForce(transform.up * waterBuoyancyForce * gScale, ForceMode.Acceleration);
			bool swimUpHeld = !typing && (Input.GetKey(KeyCode.Space) || TutorialGate.PadHeld(TutorialGate.PadButton.A));
			if (swimUpHeld)
				rb.AddForce(transform.up * waterSwimForce * gScale, ForceMode.Acceleration);
		}

		UpdateLoopAudio(upBoostSource,   upBoostClip,   upBoostActive,   upBoostVolume);
		UpdateLoopAudio(downBoostSource, downBoostClip, downBoostActive, downBoostVolume);
		UpdateLoopAudio(dirBoostSource,  dirBoostClip,  dirBoostActive,  dirBoostVolume);
	}

	bool IsGrounded()
	{
		// Sphere must not overlay terrain at origin otherwise no collision will be detected
		// so rayRadius should not be larger than controller's capsule collider radius
		const float rayRadius = .3f;
		const float groundedRayDst = .2f;
		bool grounded = false;
		_groundNormal = transform.up;   // default: flat relative to gravity-up (no slope)

		if (referenceBody != null || _flatActive)
		{
			Vector3 refVel = referenceBody != null ? referenceBody.velocity : Vector3.zero;
			var relativeVelocity = rb.velocity - refVel;
			// Don't cast ray down if player is jumping up from surface
			if (Vector3.Dot(relativeVelocity, transform.up) <= jumpForce * .5f)
			{
				RaycastHit hit;
				Vector3 offsetToFeet = (feet.position - transform.position);
				Vector3 rayOrigin = rb.position + offsetToFeet + transform.up * rayRadius;
				Vector3 rayDir = -transform.up;

				grounded = Physics.SphereCast(rayOrigin, rayRadius, rayDir, out hit, groundedRayDst, walkableMask);
				// Reject ground-hits on ships that aren't themselves landed.
				// The ship hull's collider sits on the walkable layer so the
				// player can walk on a parked ship's roof, but when the same
				// ship is orbiting in space we don't want IsGrounded firing —
				// it puts the player into the walking-movement code path and
				// "locks" them to the ship's surface, fighting against the
				// jetpack and the 20-25 m proximity damping. Walking back
				// onto the ship is fine once it lands again (IsLanded flips
				// to true on the first surface contact).
				if (grounded)
				{
					var hitShip = hit.collider != null
						? hit.collider.GetComponentInParent<Ship>()
						: null;
					if (hitShip != null && !hitShip.IsLanded)
						grounded = false;
					else
						_groundNormal = hit.normal;   // real walkable ground — keep its slope
				}
			}
		}

		return grounded;
	}

	void FixedUpdate()
	{
		if (Time.timeScale == 0)
		{
			return;
		}

		HandleMovement();

		Vector3 gravityOfNearestBody = Vector3.zero;
		float nearestSurfaceDst = float.MaxValue;

		// Flat-gravity fallback: interior scenes (e.g. the Backrooms) have no
		// NBodySimulation, so NBodySimulation.Bodies would NRE. Apply a constant
		// straight-down gravity, leave referenceBody null, and let gravity-up
		// resolve to world up below (rawGravityUp = -down = up).
		_flatActive = useFlatGravityFallback && !_hasGravitySim;
		if (_flatActive)
		{
			rb.AddForce(Vector3.down * flatGravity, ForceMode.Acceleration);
			gravityOfNearestBody = Vector3.down * flatGravity;
			referenceBody = null;
			_lastGravityMag = flatGravity;
		}
		else
		{
			CelestialBody[] bodies = NBodySimulation.Bodies;

			// Gravity
			foreach (CelestialBody body in bodies)
			{
				float sqrDst = (body.Position - rb.position).sqrMagnitude;
				Vector3 forceDir = (body.Position - rb.position).normalized;
				Vector3 acceleration = forceDir * Universe.gravitationalConstant * body.mass / sqrDst;
				rb.AddForce(acceleration, ForceMode.Acceleration);

				float dstToSurface = Mathf.Sqrt(sqrDst) - body.radius;

				// Find body with strongest gravitational pull
				if (dstToSurface < nearestSurfaceDst)
				{
					nearestSurfaceDst = dstToSurface;
					gravityOfNearestBody = acceleration;
					referenceBody = body;
				}
			}
			_lastGravityMag = gravityOfNearestBody.magnitude;
		}

		// Rotate to align with up — usually the nearest body's gravity-up,
		// but when the player is in the ship-proximity zone (off-ground,
		// within shipProximityRadius of a non-piloted Ship that is itself
		// orbiting, i.e. !Ship.IsLanded), gradually blend toward the ship's
		// up vector over shipUpBlendSeconds. Blends back to gravity-up over
		// the same duration when any condition breaks.
		//
		// CRITICAL: the gravity-up itself is also smoothed. The raw
		// -gravityOfNearestBody.normalized SNAPS instantly when the player
		// crosses from one planet's gravity well to another (referenceBody
		// flips). Without smoothing the raw, the chosenUp Slerp endpoint
		// would jerk at the boundary and the visible effect was "snap, no
		// blend" — which is what the user kept seeing. Smoothing the
		// gravity-up with Vector3.RotateTowards at 180°/shipUpBlendSeconds
		// makes the crossing take roughly shipUpBlendSeconds for a
		// half-circle transition (linear with angle for smaller ones).
		//
		// The cache reference is held throughout the fade-out so the
		// ship-up slerp can read the ship's CURRENT up (the ship keeps
		// orbiting while the blend decays); cache only nulls after both:
		// out of envelope AND _shipUpBlend == 0.
		Vector3 rawGravityUp = -gravityOfNearestBody.normalized;
		bool rawValid        = rawGravityUp.sqrMagnitude > 0.001f;
		if (!_smoothedGravityUpInit)
		{
			_smoothedGravityUp     = rawValid ? rawGravityUp : transform.up;
			_smoothedGravityUpInit = true;
			_gravityUpBlending     = false;
		}
		else
		{
			// Detect a cross-body transition (planet → planet, planet → ship's gravity,
			// deep-space → planet). The blend stays armed until smoothedGravityUp has
			// ratcheted all the way to the new rawGravityUp, then we drop back into
			// the snap-to-raw steady state so per-step walking is identical to the
			// pre-smoothing baseline (no per-frame indirection that can interact with
			// the camera FX slerp and look like jitter).
			if (rawValid && referenceBody != _lastReferenceBody)
				_gravityUpBlending = true;

			if (_gravityUpBlending && rawValid)
			{
				float maxRad = (Mathf.PI / Mathf.Max(0.0001f, shipUpBlendSeconds)) * Time.fixedDeltaTime;
				_smoothedGravityUp = Vector3.RotateTowards(_smoothedGravityUp, rawGravityUp, maxRad, 0f);
				if (Vector3.Angle(_smoothedGravityUp, rawGravityUp) < 0.05f)
				{
					_smoothedGravityUp = rawGravityUp;
					_gravityUpBlending = false;
				}
			}
			else if (rawValid)
			{
				_smoothedGravityUp = rawGravityUp;
			}
		}
		_lastReferenceBody = referenceBody;
		Vector3 gravityUp = _smoothedGravityUp.sqrMagnitude > 0.001f
			? _smoothedGravityUp
			: transform.up;
		Vector3 chosenUp  = gravityUp;
		// "Zone active" = player off-ground AND a non-piloted ship is
		// within 25 m AND that ship is currently orbiting (not landed on a
		// body). _shipUpRotationRef is mirrored from _cachedNearestShipInRange
		// in the damping block while the player is in range, and persists
		// through the fade-out after they leave so this slerp endpoint stays
		// valid for the entire decay.
		bool zoneActive = !isGrounded
		               && _cachedNearestShipInRange != null
		               && !_cachedNearestShipInRange.IsLanded;
		float blendTarget = zoneActive ? 1f : 0f;
		float blendStep   = shipUpBlendSeconds > 0.0001f
			? Time.fixedDeltaTime / shipUpBlendSeconds
			: 1f;
		_shipUpBlend = Mathf.MoveTowards(_shipUpBlend, blendTarget, blendStep);
		if (_shipUpBlend > 0f && _shipUpRotationRef != null)
		{
			Vector3 shipUp = _shipUpRotationRef.transform.up;
			if (shipUp.sqrMagnitude > 0.001f)
				chosenUp = Vector3.Slerp(gravityUp, shipUp, _shipUpBlend);
		}
		// Cinematic up override (shuttle-landing intro): the ride owns the up.
		if (UpOverrideTransform != null)
			chosenUp = UpOverrideTransform.up;
		transform.rotation = Quaternion.FromToRotation(transform.up, chosenUp) * transform.rotation;
		// Clear the rotation reference once the blend has fully decayed
		// AND we're out of the zone. The damping cache nulls instantly
		// when the player crosses the 25 m boundary; the rotation ref
		// hangs on until the slerp lerps all the way back to gravityUp.
		if (!zoneActive && _shipUpBlend == 0f)
			_shipUpRotationRef = null;

		if (isInDialogue)
		{
			smoothVelocity = Vector3.zero;
			_wasGroundedPhys = isGrounded;
		}
		else
		{
			// Walk displacement applies ONLY while grounded — airborne motion is
			// fully velocity-based now (air-control block in HandleMovement), so
			// continuous collision detection actually protects the flight path.
			// The old airborne MovePosition teleports (0.28 m per tick at sprint)
			// bypassed CCD entirely and were the main way sprint-jumps punched
			// through walls.
			// Submerged movement is fully velocity/force-based (the swim block in
			// HandleMovement), so suppress the grounded walk displacement while
			// half-submerged — otherwise standing on the pool floor would walk at
			// full ground speed instead of swimming.
			Vector3 move = (isGrounded && !IsHalfSubmerged()) ? smoothVelocity * Time.fixedDeltaTime : Vector3.zero;

			// Slope projection. smoothVelocity is built in the gravity-HORIZONTAL
			// plane (perpendicular to transform.up). Walking that flat vector down
			// a hill steps you off the descending surface, so you go briefly
			// airborne and the stickToGround impulse yanks you back — the "bumpy
			// downhill" feel. Re-projecting the move onto the actual ground plane
			// (perpendicular to _groundNormal, captured by IsGrounded's spherecast)
			// makes you follow the slope smoothly. Magnitude is preserved so walk
			// speed is constant up/down hills. No-op while airborne / on flat
			// ground (_groundNormal == transform.up there, so the projection
			// returns `move` unchanged).
			if (isGrounded && move.sqrMagnitude > 1e-10f)
			{
				Vector3 slopeMove = Vector3.ProjectOnPlane(move, _groundNormal);
				if (slopeMove.sqrMagnitude > 1e-10f)
					move = slopeMove.normalized * move.magnitude;
			}

			move = ResolveWallSlide(move);

			// Rescue pass: if the capsule already overlaps a wall, sweeps are
			// blind (casts started inside a collider report no hit) — the exact
			// state that used to end with depenetration ejecting the player out
			// the FAR side of the wall. Depenetrate back out along the wall
			// normal this tick instead.
			move += ResolveWallOverlap();

			// Speculative velocity clamp: rb.velocity (jump arcs, transferred
			// momentum, thrust) moves the capsule OUTSIDE the swept walk move —
			// cap its into-wall component so the integrator can never press us
			// into (and eventually through) a wall.
			ClampVelocityAgainstWalls();

			if (isGrounded || move.sqrMagnitude > 0f)
				rb.MovePosition(rb.position + move);

			_wasGroundedPhys = isGrounded;
		}
	}

	// Anti-tunneling: sweep the rigidbody along the desired movement vector and
	// if we'd hit a wall (steep surface), clamp the move to stop just short of
	// it, then slide the remaining motion along the wall plane. ITERATES up to
	// kMaxIterations times so that sliding into a *second* wall (e.g. inside a
	// corner) is also caught instead of letting the slid motion punch through.
	//
	// Why: even with CollisionDetectionMode.ContinuousDynamic, Unity's MovePosition
	// can tunnel through walls when (a) the input speed is high (sprint), (b) the
	// rigidbody is also depenetrating from a recent contact, or (c) the wall is a
	// compound/curved collider where the solver "loses track" between adjacent
	// shapes. SweepTest is a deterministic forward check that catches all three.
	//
	// rb.SweepTest always sweeps from the rigidbody's CURRENT position (not the
	// iterated tentative position), so we offset the available distance budget
	// by the projection of the result-so-far onto the current sweep direction —
	// that gives correct multi-wall accounting in axis-aligned and most generic
	// corner geometries.
	//
	// Floors/slopes (normal mostly aligned with our up direction) are excluded
	// so this doesn't break walking up terrain or onto the ship's interior.

	// Distance from the player's rigidbody position to the nearest
	// CelestialBody's surface, in metres. Mirrors the gravity loop's
	// nearestSurfaceDst computation but available from inside
	// HandleMovement (where the outer Update()'s local isn't visible).
	// Returns float.MaxValue if no bodies exist (sane "far from
	// everything" fallback).
	// Finds the nearest Ship to the player within `shipProximityRadius`.
	// Called from HandleMovement every ShipProximityCheckInterval seconds
	// (default 0.2s) to power the velocity-matching damping while floating
	// near a ship. Cheap — FindObjectsOfType<Ship>() walks the scene once
	// per 0.2s with a fleet of 1-8 ships in practice. Returns null if no
	// ship is in range.
	Ship FindNearestShipInRange()
	{
		var ships = FindObjectsOfType<Ship>();
		if (ships == null || ships.Length == 0) return null;
		Ship best = null;
		float bestSqr = shipProximityRadius * shipProximityRadius;
		Vector3 myPos = rb != null ? rb.position : transform.position;
		for (int i = 0; i < ships.Length; i++)
		{
			var s = ships[i];
			if (s == null) continue;
			// Skip ships the player is currently piloting — the player's
			// gameObject is inactive during pilot anyway, so this branch
			// is mostly defensive in case the disable timing changes.
			if (s.IsPiloted) continue;
			float dSqr = (s.transform.position - myPos).sqrMagnitude;
			if (dSqr < bestSqr) { bestSqr = dSqr; best = s; }
		}
		return best;
	}

	Vector3 ResolveWallSlide(Vector3 desiredMove)
	{
		if (desiredMove.sqrMagnitude < 1e-6f) return desiredMove;

		const int kMaxIterations = 4;
		Vector3 result = Vector3.zero;
		Vector3 remaining = desiredMove;

		for (int i = 0; i < kMaxIterations; i++)
		{
			if (remaining.sqrMagnitude < 1e-8f) break;

			Vector3 dir = remaining.normalized;
			float dist = remaining.magnitude;

			// Sweep needs to extend past what we've already tentatively moved
			// in this direction, otherwise walls beyond that point go unseen.
			float alreadyAlongDir = Mathf.Max(0f, Vector3.Dot(result, dir));
			float sweepDist = alreadyAlongDir + dist + wallSlideOvershoot;

			if (!rb.SweepTest(dir, out RaycastHit hit, sweepDist, QueryTriggerInteraction.Ignore))
			{
				// Path is clear — take the remainder.
				result += remaining;
				break;
			}

			// Walkable surface — let normal physics handle it; don't clamp.
			if (Vector3.Dot(hit.normal, transform.up) >= wallSlideMaxNormalUpDot)
			{
				result += remaining;
				break;
			}

			// Wall hit. Translate hit.distance (measured from rb.position) into
			// the budget available *from the iterated position* and clamp.
			float availableDist = hit.distance - alreadyAlongDir - wallSlideSkin;
			float safeDist = Mathf.Clamp(availableDist, 0f, dist);

			Vector3 forward = dir * safeDist;
			result += forward;
			Vector3 leftover = remaining - forward;
			remaining = Vector3.ProjectOnPlane(leftover, hit.normal);
		}

		return result;
	}

	// Speculative-contact velocity clamp. rb.velocity carries jump arcs,
	// transferred run momentum, jetpack thrust and gravity — motion the
	// ResolveWallSlide sweep (which only covers the walk MovePosition) never
	// sees. Each tick, sweep along the frame-relative velocity for the
	// distance the integrator will travel this step; if a WALL (steep normal)
	// would be reached, cap the into-wall component of the velocity so the
	// capsule arrives flush at wallSlideSkin and stops — tangential velocity
	// is untouched, so you slide along walls naturally. Floors/slopes are
	// excluded (normal physics owns landings). Two iterations handle inside
	// corners. This is the other half of the anti-tunneling fix: without it,
	// velocity pressed the capsule slightly into the wall, the next tick's
	// sweeps started INSIDE the collider (blind), and depenetration would
	// eventually pop the player out the far side.
	void ClampVelocityAgainstWalls()
	{
		Vector3 refVel = referenceBody != null ? referenceBody.velocity : Vector3.zero;
		Vector3 relVel = rb.velocity - refVel;
		float dt = Time.fixedDeltaTime;
		bool changed = false;

		for (int i = 0; i < 2; i++)
		{
			float speed = relVel.magnitude;
			if (speed < 0.01f) break;

			Vector3 dir = relVel / speed;
			float travel = speed * dt + wallSlideOvershoot;
			if (!rb.SweepTest(dir, out RaycastHit hit, travel, QueryTriggerInteraction.Ignore)) break;
			// Walkable surface — let normal physics handle the landing.
			if (Vector3.Dot(hit.normal, transform.up) >= wallSlideMaxNormalUpDot) break;

			float intoSpeed = -Vector3.Dot(relVel, hit.normal);
			if (intoSpeed <= 0f) break;
			// Fastest approach speed that still stops at the skin gap this tick.
			float maxIntoSpeed = Mathf.Max(0f, hit.distance - wallSlideSkin) / dt;
			if (intoSpeed <= maxIntoSpeed) break;

			relVel += hit.normal * (intoSpeed - maxIntoSpeed);
			changed = true;
		}

		if (changed) rb.velocity = refVel + relVel;
	}

	// Overlap rescue: if the capsule is ALREADY intersecting a wall-like
	// collider (sweeps can't see it — casts that start inside a collider
	// report nothing), compute the depenetration vector and return it as an
	// extra MovePosition nudge that pushes the player back out along the wall
	// normal. Floor-ish overlaps (normal mostly up) are skipped — PhysX's own
	// contact solver owns standing depenetration and fighting it causes
	// jitter. Clamped so a bad frame can never fling the player.
	Vector3 ResolveWallOverlap()
	{
		if (capsuleCollider == null) return Vector3.zero;

		// World-space capsule from the collider (assumes Y-axis capsule, which
		// the player is; lossyScale is ~1 but respected anyway).
		Vector3 lossy = transform.lossyScale;
		float radius = capsuleCollider.radius * Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z));
		float half = Mathf.Max(0f, capsuleCollider.height * 0.5f * Mathf.Abs(lossy.y) - radius);
		Vector3 center = transform.TransformPoint(capsuleCollider.center);
		Vector3 p0 = center + transform.up * half;
		Vector3 p1 = center - transform.up * half;

		int count = Physics.OverlapCapsuleNonAlloc(p0, p1, radius, _overlapHits, Physics.AllLayers, QueryTriggerInteraction.Ignore);
		Vector3 nudge = Vector3.zero;
		for (int i = 0; i < count; i++)
		{
			Collider col = _overlapHits[i];
			if (col == null || col.attachedRigidbody == rb) continue;
			if (!Physics.ComputePenetration(
				capsuleCollider, transform.position, transform.rotation,
				col, col.transform.position, col.transform.rotation,
				out Vector3 depenDir, out float depenDist)) continue;
			// Floor-ish: leave to the physics contact solver.
			if (Vector3.Dot(depenDir, transform.up) >= wallSlideMaxNormalUpDot) continue;
			nudge += depenDir * (depenDist + 0.001f);
		}
		return Vector3.ClampMagnitude(nudge, 0.5f);
	}

	void HandleEditorInput()
	{
		if (Application.isEditor && !AIChatScreen.IsTypingActive)
		{
			if (Input.GetKeyDown(KeyCode.O))
			{
				Debug.Log("Debug mode: Toggle freeze player");
				debug_playerFrozen = !debug_playerFrozen;
			}
		}
	}

	public void SetVelocity(Vector3 velocity)
	{
		rb.velocity = velocity;
	}

	// Auto-yaw (e.g. the black hole dragging you into a spin): feeds the look yaw so the
	// existing smoothing turns the body + camera naturally — exactly like mouse-look right.
	public void AddAutoYaw(float degrees)
	{
		yaw += degrees;
	}

	public void ExitFromSpaceship()
	{
		cam.transform.parent = transform;
		cam.transform.localPosition = cameraLocalPos;
		smoothYaw = 0;
		yaw = 0;
		smoothPitch = cam.transform.localEulerAngles.x;
		pitch = smoothPitch;
	}

	/// Called by Ship.ForceExitPilot after teleporting the player to the
	/// pilot seat. The player has just been positioned at
	/// pilotSeatPoint.rotation (already aligned with the ship's up). We
	/// want them to STAY at that orientation — no smooth tilt animation
	/// over the next second as the smoothed_gravity_up catches up.
	///
	/// Resets _smoothedGravityUpInit so the next FixedUpdate re-seeds
	/// smoothed_gravity_up from the current gravity, and if the ship is
	/// airborne we pre-arm the ship-up blend at 1 (instead of letting it
	/// ramp from 0) so the slerp endpoint is the ship's up from the very
	/// first frame.
	public void SnapOrientationOnExitPilot(Ship exitedShip)
	{
		_smoothedGravityUpInit = false;
		if (exitedShip != null && !exitedShip.IsLanded)
		{
			// In space: ship-up is the right rest pose. Snap blend to 1
			// and prime the rotation/damping caches so the player stays
			// aligned with the ship from frame 1.
			_shipUpBlend = 1f;
			_shipUpRotationRef        = exitedShip;
			_cachedNearestShipInRange = exitedShip;
			_nextShipProximityCheckTime = Time.fixedTime + ShipProximityCheckInterval;
		}
		else
		{
			// On a planet: gravity-up is the rest pose. Zero the blend so
			// nothing pulls the player toward ship-up while they're stood
			// on the cockpit roof.
			_shipUpBlend = 0f;
			_shipUpRotationRef        = null;
			_cachedNearestShipInRange = null;
		}
	}
	public Camera Camera
	{
		get
		{
			return cam;
		}
	}

	public Rigidbody Rigidbody
	{
		get
		{
			return rb;
		}
	}

	public float JetpackFuelPercent => jetpackFuelPercent;
	public float DownThrustFuelPercent => downThrustFuelPercent;
	public float DirectionalThrustFuelPercent => dirThrustFuelPercent;
	public bool IsOnGround => isGrounded;
	/// True while the player is touching any Water trigger (even just the
	/// feet). FallDamage uses this to discard pre-impact speed — water is the
	/// landing, so wading ashore afterwards must never spend a stale fall.
	public bool IsInWater => waterTouches > 0;
	public bool JetpackUnlocked => jetpackUnlocked;

	/// <summary>
	/// Player's velocity relative to the celestial body they're aligned to.
	/// Used by camera FX (speed lines) — `rb.velocity` in world space includes
	/// the reference body's orbital motion, which is nonzero even when the
	/// player is standing still on the surface.
	/// </summary>
	public Vector3 RelativeVelocity
	{
		get
		{
			if (rb == null) return Vector3.zero;
			return referenceBody != null ? rb.velocity - referenceBody.velocity : rb.velocity;
		}
	}

	/// <summary>
	/// Player's total surface-relative velocity for the speedometer HUD. Combines
	/// the walking-input vector (smoothVelocity, which is applied via
	/// rb.MovePosition and therefore does NOT show up in rb.velocity) with the
	/// physics-driven RelativeVelocity. Use this for any "how fast is the player
	/// moving" readout; RelativeVelocity alone reads ~0 while walking because
	/// MovePosition bypasses the velocity integrator.
	/// </summary>
	public Vector3 SurfaceVelocity
	{
		get
		{
			if (rb == null) return Vector3.zero;
			Vector3 phys = referenceBody != null ? rb.velocity - referenceBody.velocity : rb.velocity;
			return smoothVelocity + phys;
		}
	}

	/// <summary>
	/// Player's true world-space velocity — rb.velocity PLUS the walking
	/// input that rb.MovePosition doesn't put into the velocity integrator.
	/// Use this for Kepler orbit math (MapOrbitLines), trajectory projection,
	/// or anything else that needs the player's actual world motion.
	/// rb.velocity alone is wrong because the player moves via MovePosition.
	/// </summary>
	public Vector3 WorldVelocity
	{
		get
		{
			if (rb == null) return Vector3.zero;
			return rb.velocity + smoothVelocity;
		}
	}

	/// <summary>
	/// The celestial body the player is currently gravity-aligned to (the
	/// strongest gravitational pull). Null in deep space. Camera FX use it
	/// to compute altitude / planet-local frames.
	/// </summary>
	public CelestialBody ReferenceBody => referenceBody;
	public void UnlockJetpack() { jetpackUnlocked = true; }

	public void ApplyFuel(float jetpack, float downThrust, float dirThrust)
	{
		jetpackFuelPercent = Mathf.Clamp01(jetpack);
		downThrustFuelPercent = Mathf.Clamp01(downThrust);
		dirThrustFuelPercent = Mathf.Clamp01(dirThrust);
	}
}
