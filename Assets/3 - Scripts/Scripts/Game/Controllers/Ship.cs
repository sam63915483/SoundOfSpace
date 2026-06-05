using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Runs before PlayerController's default-order FixedUpdate so the direct
// rb.position write we do during auto-align (to carry the player around the
// ship CoM) commits before PlayerController.FixedUpdate's MovePosition reads
// rb.position as its starting point. Without forced order Unity's script
// scheduling between same-priority MonoBehaviours is not guaranteed, which
// is one of the things that made the previous (reverted) carry-player attempt
// unreliable.
[DefaultExecutionOrder(-50)]
public class Ship : GravityObject
{
    public InputSettings inputSettings;
    public Transform hatch;
    public float hatchAngle;
    public Transform camViewPoint;
    public Transform pilotSeatPoint;
    public LayerMask groundedMask;
    public GameObject window;


    [Header("Handling")]
    public float thrustStrength = 20;
    [Tooltip("Default thrust multiplier (no boost). 1.0 = legacy value — strong enough to dominate planet gravity at typical altitudes so WASD feels like real thrust (not a temporary translation). Boost (Shift) multiplies this for catching up to fast-moving targets.")]
    [Range(0.05f, 2f)]
    public float thrustPowerScale = 1.0f;
    [Tooltip("Time (seconds) for thruster input to ramp 0 → full when a key is pressed. Smooths the 'tap feels binary' problem so short presses produce proportional impulses.")]
    [Range(0f, 0.5f)]
    public float thrustRampSeconds = 0.15f;
    public float rotSpeed = 5;
    public float rollSpeed = 30;
    public float rotSmoothSpeed = 10;

    [Header("Power")]
    [Tooltip("Maximum ship power. Drains while piloted (flying rate) or idle (idle rate). Restored by SolarPanelCharger. Reaching 0 disables thrust AND LebronLight.")]
    public float powerMax = 50f;
    public float powerIdleDrainPerSec   = 0.25f;
    public float powerFlyingDrainPerSec = 1.5f;
    float powerCurrent;
    public float PowerPercent => powerMax > 0f ? powerCurrent / powerMax : 0f;
    public bool HasPower      => powerCurrent > 0f;
    public bool CanRunLebronLight => HasPower;

    [Header("Fuel (1 crystal = 5 fuel units; 20 fills a 100-unit tank)")]
    [Tooltip("Maximum reactor fuel. Drains ONLY while a thrust input is held (WASD / Space / Ctrl). Sitting in the pilot seat doing nothing does not drain fuel. Boost (Shift+thrust) drains at 2x the thrust rate.")]
    public float fuelMax = 100f;
    public float fuelPilotedDrainPerSec = 0f;       // no drain just sitting in the cockpit
    public float fuelThrustDrainPerSec  = 0.875f;   // WASD / Space / Ctrl held
    public float fuelBoostDrainPerSec   = 1.75f;    // Shift + thrust = 2x thrust rate
    float fuelCurrent;
    public float FuelPercent => fuelMax > 0f ? fuelCurrent / fuelMax : 0f;
    public bool HasFuel      => fuelCurrent > 0f;
    public bool CanThrust    => HasPower && HasFuel;

    // Set true in FixedUpdate when boost is actively engaged on any axis,
    // so Update can pick the right fuel drain rate for this tick.
    bool _isBoostingThisTick;

    public void DrainPower(float amount)
    {
        if (amount <= 0f) return;
        powerCurrent = Mathf.Clamp(powerCurrent - amount, 0f, powerMax);
    }

    public void RestorePower(float amount)
    {
        if (amount <= 0f) return;
        powerCurrent = Mathf.Clamp(powerCurrent + amount, 0f, powerMax);
    }

    public void DrainFuel(float amount)
    {
        if (amount <= 0f) return;
        fuelCurrent = Mathf.Clamp(fuelCurrent - amount, 0f, fuelMax);
    }

    public void RestoreFuel(float amount)
    {
        if (amount <= 0f) return;
        fuelCurrent = Mathf.Clamp(fuelCurrent + amount, 0f, fuelMax);
    }

    public void SetPower(float current)
    {
        powerCurrent = Mathf.Clamp(current, 0f, powerMax);
    }

    public void SetFuel(float current)
    {
        fuelCurrent = Mathf.Clamp(current, 0f, fuelMax);
    }

    [Header("Boost (mirrors player jetpack)")]
    [Tooltip("Boost multiplier applied to a thrust axis while LeftShift is held and that axis has fuel remaining. With thrustPowerScale=1.0 and boostMultiplier=1.923 the boosted thrust feels punchy without lurching the ship out of orbit.")]
    public float boostMultiplier = 1.923f;
    [Tooltip("Per-axis pool capacity. 1.0 = a full bar (matches PlayerController jetpack scale).")]
    public float boostFuelMax = 1f;
    [Tooltip("Fraction-of-max drained per second of continuous boost on that axis.")]
    public float boostDrainPerSec = 0.5f;
    [Tooltip("Fraction-of-max refilled per second when that axis is NOT actively boosting.")]
    public float boostRefillPerSec = 0.4f;

    // Three independent fuel pools, mirroring PlayerController's jetpack /
    // downThrust / dirThrust split. Up = Space, Down = Ctrl, Dir = WASD strafe/forward/back.
    float _boostFuelUp;
    float _boostFuelDown;
    float _boostFuelDir;
    // Once a pool drains to 0, lock it out of boosting until it refills back
    // to full. Without this, refill ticks immediately and the boost re-engages
    // every frame the player keeps Shift held, producing a barely-perceptible
    // "boost never stops" pulse. The jetpack uses a refuelDelay to dodge the
    // same problem; the ship's equivalent is this depleted-lockout.
    bool _boostUpDepleted;
    bool _boostDownDepleted;
    bool _boostDirDepleted;
    public float UpBoostFuelPercent  => boostFuelMax > 0f ? _boostFuelUp   / boostFuelMax : 0f;
    public float DownBoostFuelPercent=> boostFuelMax > 0f ? _boostFuelDown / boostFuelMax : 0f;
    public float DirBoostFuelPercent => boostFuelMax > 0f ? _boostFuelDir  / boostFuelMax : 0f;

    [Header("Match Velocity (V)")]
    [Tooltip("Relative speed (m/s) above which V will use boost on top of regular thrust, IF the DIR boost pool has fuel. Below this, V uses regular thrust only.")]
    public float matchVelocityBoostSpeedThreshold = 10f;
    [Tooltip("Relative speed below which IsVelocityMatched flips true (drives the 'VELOCITY MATCHED' status text).")]
    public float matchVelocityMatchedSpeed = 0.5f;
    [Tooltip("Key bound to Match Velocity. V by default. Reads SolarSystemMapController.FollowedShip first, then PendingHighlight body.")]
    public KeyCode matchVelocityKey = KeyCode.V;
    // True while V is actively firing thrust (key held + valid target + relVel > 0).
    public bool IsMatchingVelocity { get; private set; }
    // True when relative speed to the target is below matchVelocityMatchedSpeed.
    // Persists regardless of whether V is currently held.
    public bool IsVelocityMatched { get; private set; }
    // Local-space thrust direction V is requesting this frame, components in [-1, 1].
    // Read by GForceHUD to light the gimbal arrows red.
    public Vector3 MatchAssistInput { get; private set; }

    [Header("Circularize (O)")]
    [Tooltip("Magnitude (m/s) of needed deltaV above which O will use boost on top of regular thrust, IF the UP boost pool has fuel.")]
    public float circularizeBoostNeedThreshold = 8f;
    [Tooltip("Radial speed below which IsOrbitMatched flips true (drives 'ORBIT MATCHED' status text).")]
    public float orbitMatchedRadialSpeed = 0.5f;
    [Tooltip("Multiplier on the nearest body's radius for the trigger range. Beyond this distance, O does nothing.")]
    public float circularizeRangeMul = 3f;
    [Tooltip("Key bound to Circularize. O by default.")]
    public KeyCode circularizeKey = KeyCode.O;
    public bool IsCircularizing { get; private set; }
    public bool IsOrbitMatched { get; private set; }
    public Vector3 CircularizeAssistInput { get; private set; }

    [Header("Camera Shake")]
    public float shakeVelocityThreshold = 12f;

    [Header("Interact")]
    public Interactable flightControls;

    [Header("Headlight")]
    [Tooltip("Spot light at the front of the ship. Scroll wheel controls its intensity while piloted.")]
    public Light headlight;
    [Tooltip("Minimum intensity scroll-down can dim the headlight to.")]
    public float headlightMinBrightness = 0f;
    [Tooltip("Maximum intensity scroll-up can brighten the headlight to.")]
    public float headlightMaxBrightness = 10f;
    [Tooltip("Intensity change per scroll-wheel tick. Up brightens, down dims.")]
    public float headlightScrollSensitivity = 20f;
    [Tooltip("Minimum value the global QualitySettings.pixelLightCount is bumped to in Awake. The headlight's shadow gets evicted from the shadow atlas when other realtime lights (sun, planet shadow casters, point lights) compete for slots — bumping this so all realtime lights fit prevents the shadow from flickering on/off as the ship rotates and lights enter/exit the camera frustum.")]
    public int headlightMinPixelLightCount = 8;
    [Tooltip("Per-light shadow map resolution applied to the headlight in Awake. Higher gives a stable, dedicated atlas slot and overrides the prefab's lower setting. 1024 is a good balance; 2048 if you still see flicker. Set to 0 to leave the prefab value untouched.")]
    public int headlightShadowResolution = 1024;

    public bool canFly = true;
    public System.Action<Collision> OnShipCollision;
    public GameObject boostMeterUI;

    [Header("Intro Lock-out")]
    [Tooltip("Seconds after the first crash against a CelestialBody before ship input is restored. Game starts with the player piloting and falling — input is suppressed until this delay elapses post-crash.")]
    public float introPostCrashUnlockDelay = 10f;
    bool _introInputUnlocked;
    bool _introCrashTriggered;

    [Header("Sound Effects")]
    [SerializeField] AudioClip engineLoopClip;
    [SerializeField] AudioClip thrustLoopClip;
    [SerializeField] AudioClip crashLightClip;
    [SerializeField] AudioClip crashMediumClip;
    [SerializeField] AudioClip crashHardClip;
    [SerializeField] AudioClip hatchClip;
    [SerializeField, Range(0, 1)] float engineVolume = 0.4f;
    [SerializeField, Range(0, 1)] float thrustVolume = 0.6f;
    [SerializeField, Range(0, 1)] float crashVolume  = 0.8f;
    [SerializeField, Range(0, 1)] float hatchVolume  = 0.7f;

    [Header("Crash Impact Thresholds (m/s)")]
    [SerializeField] float crashLightThreshold  = 5f;
    [SerializeField] float crashMediumThreshold = 15f;
    [SerializeField] float crashHardThreshold   = 25f;

    AudioSource engineSource;
    AudioSource thrustSource;
    AudioSource crashSource;

    // Pilot start-up / shut-down SFX (loaded from StreamingAssets).
    AudioSource _pilotSfxSource;
    AudioClip _startupClip, _shutdownClip;

    // Hatch pressurizer FX: hiss + a fast smoke puff out each pressurizer's local
    // -Y when the hatch opens/closes. Pressurizer1/2 are child GameObjects on the
    // ship prefab; we anchor audio + a particle puff to them at runtime.
    Transform[] _pressurizers;
    AudioSource[] _pressAudio;
    ParticleSystem[] _pressPuff;
    AudioClip _pressurizerClip;
    bool _prevHatchForPuff;

    Rigidbody rb;
    Quaternion targetRot;
    Quaternion smoothedRot;

    Vector3 thrusterInput;
    Vector3 _smoothedThrusterInput;
    PlayerController pilot;
    bool shipIsPiloted;
    static Ship s_pilotedInstance;
    public static bool AnyShipPiloted => s_pilotedInstance != null && s_pilotedInstance.shipIsPiloted;

    public static Ship PilotedInstance => s_pilotedInstance;
    int numCollisionTouches;
    // Tracks contacts that AREN'T grounded-layer (planet / terrain) — i.e.
    // the player capsule resting on the cockpit floor, enemies bumping the
    // hull, etc. Rotating the ship even via direct rb.rotation assignment +
    // angularVelocity = 0 still triggers depenetration on these contacts in
    // the next physics step, which transfers linear momentum into rb.velocity
    // and slowly destroys the orbit. We can't safely run auto-align unless
    // every active non-grounded contact is something we can move WITH the
    // ship (so the contact has no relative motion → no depenetration impulse).
    // Currently we only do that for the player capsule (the common case —
    // player standing in the cockpit after exiting pilot mode); enemies and
    // props would still leak momentum, so any of those suppresses auto-align.
    int _nonGroundedContacts;
    // Subset of _nonGroundedContacts that are the player capsule. When this
    // equals _nonGroundedContacts, auto-align is safe to run because we
    // explicitly carry the player around the ship CoM in the rotation block,
    // eliminating contact relative motion.
    int _playerContacts;
    PlayerController _contactingPlayer;
    bool hatchOpen;

    void Awake()
    {
        InitRigidbody();
        targetRot = transform.rotation;
        smoothedRot = transform.rotation;
        inputSettings.Begin();

        // Pilot start-up / shut-down SFX. 2D — you're inside the cockpit.
        _pilotSfxSource = gameObject.AddComponent<AudioSource>();
        _pilotSfxSource.playOnAwake = false;
        _pilotSfxSource.spatialBlend = 0f;
        StartCoroutine(StreamingAudio.Load("Audio/ShipStartup.wav",  AudioType.WAV, c => _startupClip = c));
        StartCoroutine(StreamingAudio.Load("Audio/ShipShutdown.wav", AudioType.WAV, c => _shutdownClip = c));

        SetupPressurizers();

        // Window glass casts shadows by default, which blocks the Sun's
        // shadow rays from passing through the cockpit — interior turns
        // pitch-black whenever the window is enabled (i.e. whenever the
        // player isn't piloting). User wants sunlight to always come
        // through. Disabling shadow casting on the window's renderer(s)
        // makes the mesh visible but skipped during shadow-map rendering,
        // so the Sun's shadow rays pass through it. Covers both a single
        // MeshRenderer on the window root and a parent with child
        // renderers (sub-panes).
        if (window != null)
        {
            var windowRenderers = window.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < windowRenderers.Length; i++)
                windowRenderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // Boost fuel pools start full — first interaction with the cockpit
        // should give the player a usable burst budget on every axis.
        _boostFuelUp = boostFuelMax;
        _boostFuelDown = boostFuelMax;
        _boostFuelDir = boostFuelMax;

        // Auto-resolve the headlight if the inspector field wasn't assigned
        // in the prefab — none of the runtime fixes below run when this is
        // null. Mirrors PlayerFlashlight's auto-find pattern so the ship
        // damage swap (Full → MissingLeft / MissingRight / NoThrusters) picks
        // up whichever variant's spotlight is in the active prefab.
        bool autoFound = false;
        if (headlight == null)
        {
            var lights = GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].type == LightType.Spot)
                {
                    headlight = lights[i];
                    autoFound = true;
                    break;
                }
            }
        }
        Debug.Log($"[Ship.Awake] headlight={(headlight == null ? "NULL" : headlight.name)} autoFound={autoFound} " +
                  $"shadows={(headlight == null ? "n/a" : headlight.shadows.ToString())} " +
                  $"intensity={(headlight == null ? 0f : headlight.intensity)} " +
                  $"range={(headlight == null ? 0f : headlight.range)}");

        // DIAGNOSTIC — enumerate every component on the original Spot Light
        // GameObject in case there's a hidden script (cookie animator, light
        // controller, etc.) we missed in the prefab YAML scan.
        if (headlight != null)
        {
            var components = headlight.gameObject.GetComponents<Component>();
            string list = "";
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                    list += components[i].GetType().Name + " ";
            }
            Debug.Log($"[Ship.Awake] Spot Light GameObject components: {list}");
        }

        // REPLACEMENT — destroy the original Spot Light's Light component and
        // spawn a fresh one in a sibling GameObject at the same world pose.
        // This rules out any hidden corruption in the original's serialized
        // state. If the fresh Light flickers identically, the cause is
        // external (camera / render path / atmosphere). If the fresh Light
        // works, the original had something wrong.
        if (headlight != null)
        {
            Light old = headlight;
            Transform oldT = old.transform;
            Transform shipT = transform;

            // Snapshot the values we care about BEFORE destroying it.
            LightType type = old.type;
            Color color = old.color;
            float intensity = old.intensity;
            float range = old.range;
            float spotAngle = old.spotAngle;
            float innerSpotAngle = old.innerSpotAngle;
            LightShadows shadowsType = old.shadows;
            float shadowStrength = old.shadowStrength;
            float shadowBias = old.shadowBias;
            float shadowNormalBias = old.shadowNormalBias;
            float shadowNearPlane = old.shadowNearPlane;
            int cullingMask = old.cullingMask;
            Vector3 oldLocalPos = oldT.localPosition;
            Quaternion oldLocalRot = oldT.localRotation;
            Transform oldParent = oldT.parent;

            // Disable the original GameObject so its Light no longer renders.
            // (Disabling rather than destroying preserves the prefab tree for
            // the save system / damage swap to find on next instantiation.)
            old.gameObject.SetActive(false);

            // Spawn fresh sibling GameObject + Light.
            var freshGo = new GameObject("ShipHeadlight_Runtime");
            freshGo.transform.SetParent(oldParent != null ? oldParent : shipT, false);
            freshGo.transform.localPosition = oldLocalPos;
            freshGo.transform.localRotation = oldLocalRot;

            var fresh = freshGo.AddComponent<Light>();
            fresh.type = type;
            fresh.color = color;
            fresh.intensity = intensity;
            fresh.range = range;
            fresh.spotAngle = spotAngle;
            fresh.innerSpotAngle = innerSpotAngle;
            // SOFT shadows use PCF filtering. They mask the sub-pixel z-fighting
            // flicker that hard shadows show when both the camera and the light
            // are moving (which is exactly the piloting case). The original
            // Hard shadows leave visible on/off transitions when shadow-map
            // texel boundaries cross casters during ship motion.
            fresh.shadows = LightShadows.Soft;
            fresh.shadowStrength = shadowStrength;
            // Bias defaults tuned for moving spotlights: lower normal bias
            // reduces peter-panning, slightly higher near plane improves
            // z-precision distribution for nearby casters (the 15m tree case).
            fresh.shadowBias = 0.05f;
            fresh.shadowNormalBias = 0.05f;
            fresh.shadowNearPlane = 1.0f;
            fresh.cullingMask = cullingMask;

            headlight = fresh;
            Debug.Log($"[Ship.Awake] Replaced original Spot Light with fresh runtime light. localPos={oldLocalPos} range={range} spot={spotAngle}");
        }

        // Force per-pixel rendering so the headlight's cone and shadows stay
        // stable. Without this, Unity's built-in pipeline can demote the
        // spotlight to vertex shading at certain ship angles when other scene
        // lights (sun, bonfires, etc.) push it out of the limited pixel-light
        // budget — the demotion drops shadows and flattens the cone. Same fix
        // as PlayerFlashlight.
        if (headlight != null)
        {
            headlight.renderMode = LightRenderMode.ForcePixel;
            // ForcePixel only protects shading. The shadow atlas is a separate
            // budget — when other realtime lights compete for slots, the
            // headlight's shadow can be evicted on small ship rotations,
            // causing the cast shadow to flash on/off. A high custom shadow
            // resolution pins a dedicated atlas slot and bypasses the prefab's
            // low default (m_Resolution: 2 = 512×512).
            if (headlightShadowResolution > 0)
                headlight.shadowCustomResolution = headlightShadowResolution;

            // ROOT CAUSE of the on/off flicker: by default Unity culls shadow
            // casters that aren't in the camera's view frustum (perf opt).
            // The camera is parented to the ship, so the frustum rotates with
            // the ship — small rotations push casters across the frustum
            // boundary, Unity drops them from shadow rendering, and the
            // shadow vanishes. Disabling this flag forces Unity to render
            // shadows for every caster inside the light's own frustum,
            // regardless of camera view. Cheap because the headlight's range
            // (50) and cone (43°) keep its frustum small.
            headlight.useViewFrustumForShadowCasterCull = false;
        }

        // Bump the global pixel-light cap so every realtime light in the
        // scene (sun + planet directionals + point lights + this headlight)
        // fits without eviction pressure. Default Ultra is 4, scene has ~6.
        if (QualitySettings.pixelLightCount < headlightMinPixelLightCount)
            QualitySettings.pixelLightCount = headlightMinPixelLightCount;

        GameObject engineObj = new GameObject("ShipEngineAudio");
        engineObj.transform.SetParent(transform);
        engineObj.transform.localPosition = Vector3.zero;
        engineSource = engineObj.AddComponent<AudioSource>();
        engineSource.playOnAwake = false;
        engineSource.loop = true;
        engineSource.volume = engineVolume;
        engineSource.clip = engineLoopClip;

        GameObject thrustObj = new GameObject("ShipThrustAudio");
        thrustObj.transform.SetParent(transform);
        thrustObj.transform.localPosition = Vector3.zero;
        thrustSource = thrustObj.AddComponent<AudioSource>();
        thrustSource.playOnAwake = false;
        thrustSource.loop = true;
        thrustSource.volume = thrustVolume;
        thrustSource.clip = thrustLoopClip;

        GameObject crashObj = new GameObject("ShipCrashAudio");
        crashObj.transform.SetParent(transform);
        crashObj.transform.localPosition = Vector3.zero;
        crashSource = crashObj.AddComponent<AudioSource>();
        crashSource.playOnAwake = false;

        OnShipCollision += HandleIntroCrashCollision;

        // Each ship starts at full power and full fuel by default. Vendor /
        // debug-spawned ships get overridden to 50% by ShipMarketNPC.SpawnShipInstance.
        // Saved ships get overridden by SaveCollector.ApplyExtraShips.
        powerCurrent = powerMax;
        fuelCurrent  = fuelMax;
    }

    void HandleIntroCrashCollision(Collision other)
    {
        if (_introCrashTriggered) return;
        if (other.gameObject.GetComponentInParent<CelestialBody>() == null) return;
        _introCrashTriggered = true;
        OnShipCollision -= HandleIntroCrashCollision;
        StartCoroutine(IntroUnlockAfterDelay());
    }

    IEnumerator IntroUnlockAfterDelay()
    {
        yield return new WaitForSeconds(introPostCrashUnlockDelay);
        _introInputUnlocked = true;
    }

    // Called by the save system on load — saved games are past the intro,
    // so the lock-out shouldn't apply when restoring a session.
    public void MarkIntroComplete()
    {
        _introCrashTriggered = true;
        _introInputUnlocked = true;
    }

    // Cached child colliders + original PhysicMaterial per collider so we
    // can restore the inspector-assigned material on landing. Built lazily
    // on the first frictionless-swap pass.
    Collider[] _myColliders;
    PhysicMaterial[] _myOriginalMats;
    // Shared frictionless material applied to ship colliders while the
    // ship is in space. Built once per scene-load (lazy).
    static PhysicMaterial s_frictionlessMat;
    // Tracks the current friction state so we only swap when the landed
    // state flips, not every frame.
    bool? _shipFrictionlessState;

    void Update()
    {
        // While the ship is in space, swap every hull collider's friction
        // material to a zero-friction one so the player can still BUMP
        // against the hull (not pass through it) but doesn't get stuck
        // walking on it / having their feet catch on it while trying to
        // float past for space-net dust collection. On landing we swap
        // back to the original materials so walking on the hull works as
        // normal.
        //
        // Previous attempt used Physics.IgnoreCollision to disable hull
        // collision entirely — the player could see the ship but flew
        // straight through it. Friction swap keeps the visible solid
        // contact while removing the "catching" feel.
        UpdateShipFrictionState();

        if (shipIsPiloted && canFly && !PlayerController.isMapOpen)
        {
            HandleMovement();
        }

        // Per-ship drain — power drains while piloted (faster) OR idle (slower).
        // Fuel drains ONLY while piloted, faster under thrust, faster still with boost.
        // Damage-disabled ships (canFly=false) don't drain either resource.
        if (canFly)
        {
            float dt = Time.deltaTime;
            if (shipIsPiloted)
            {
                powerCurrent = Mathf.Clamp(powerCurrent - powerFlyingDrainPerSec * dt, 0f, powerMax);
                // V (match velocity) and O (circularize) apply thrust directly
                // without going through thrusterInput, so include their public
                // active flags in the drain decision.
                bool thrusting = thrusterInput.sqrMagnitude > 0.01f || IsMatchingVelocity || IsCircularizing;
                float fuelRate = _isBoostingThisTick ? fuelBoostDrainPerSec
                                : thrusting         ? fuelThrustDrainPerSec
                                                     : fuelPilotedDrainPerSec;
                fuelCurrent = Mathf.Clamp(fuelCurrent - fuelRate * dt, 0f, fuelMax);
            }
            else
            {
                powerCurrent = Mathf.Clamp(powerCurrent - powerIdleDrainPerSec * dt, 0f, powerMax);
            }
        }

        if (shipIsPiloted && headlight != null)
        {
            // Mouse scroll OR D-pad up/down edge.
            float step = TutorialGate.HeadlightStep();
            if (Mathf.Abs(step) > 0.0001f)
                headlight.intensity = Mathf.Clamp(headlight.intensity + step * headlightScrollSensitivity, headlightMinBrightness, headlightMaxBrightness);

            // Watchdog — log if anything mutates the headlight's shadow state
            // away from what we set in Awake. If the shadows enum is flipping
            // at runtime, that's the cause of the on/off flicker.
            if (headlight.shadows != LightShadows.Soft)
            {
                Debug.LogWarning($"[Ship.Update] Headlight shadows changed to {headlight.shadows}! Was Soft. enabled={headlight.enabled}");
                headlight.shadows = LightShadows.Soft;
            }
            if (!headlight.enabled)
            {
                Debug.LogWarning($"[Ship.Update] Headlight was disabled! Re-enabling.");
                headlight.enabled = true;
            }
        }

        // Engine loop SFX
        if (engineSource != null)
        {
            bool shouldEngineRun = shipIsPiloted && canFly && CanThrust && !PlayerController.isMapOpen && engineLoopClip != null;
            if (shouldEngineRun && !engineSource.isPlaying)
            {
                engineSource.clip = engineLoopClip;
                engineSource.volume = engineVolume;
                engineSource.Play();
            }
            else if (!shouldEngineRun && engineSource.isPlaying)
            {
                engineSource.Stop();
            }
        }

        // Thrust loop SFX (any WASD/up/down input while piloted)
        if (thrustSource != null)
        {
            bool thrustActive = shipIsPiloted && canFly && CanThrust && !PlayerController.isMapOpen && thrustLoopClip != null
                                && (thrusterInput.sqrMagnitude > 0.01f || IsMatchingVelocity || IsCircularizing);
            if (thrustActive && !thrustSource.isPlaying)
            {
                thrustSource.clip = thrustLoopClip;
                thrustSource.volume = thrustVolume;
                thrustSource.Play();
            }
            else if (!thrustActive && thrustSource.isPlaying)
            {
                thrustSource.Stop();
            }
        }

        // Pressurizer FX whenever the hatch opens/closes (any path that flips
        // hatchOpen — outside trigger, interior button, etc.). Skip the first 2s
        // so a load-time hatch restore doesn't puff.
        if (hatchOpen != _prevHatchForPuff)
        {
            _prevHatchForPuff = hatchOpen;
            if (Time.timeSinceLevelLoad > 2f) FirePressurizers();
        }

        // Animate hatch
        float hatchTargetAngle = (hatchOpen) ? hatchAngle : 0;
        hatch.localEulerAngles = Vector3.right * Mathf.LerpAngle(hatch.localEulerAngles.x, hatchTargetAngle, Time.deltaTime);

        HandleCheats();
    }

    void HandleMovement()
    {
        if (!_introInputUnlocked)
        {
            thrusterInput = Vector3.zero;
            return;
        }
        // Thruster input — each axis is gated by its own tutorial ability.
        // X (strafe) and Z (forward/back) read from the InputManager's combined
        // Horizontal/Vertical axes, which already aggregate WASD + arrow keys
        // + the Xbox left stick (joystick axes 0/1). Y (ascend/descend) is the
        // only axis that needs explicit composite digital handling because
        // there's no equivalent pre-aggregated axis for "Space + A button".
        float thrustInputX = TutorialGate.GetAxisRaw("Horizontal", TutorialAbility.ShipMove);
        float thrustInputZ = TutorialGate.GetAxisRaw("Vertical",   TutorialAbility.ShipMove);
        int thrustInputY = 0;
        if (TutorialGate.JumpHeld(TutorialAbility.ShipUpThrust))         thrustInputY++;  // Space / A
        if (TutorialGate.DownThrustHeld(TutorialAbility.ShipDownThrust)) thrustInputY--;  // LeftCtrl / LT
        thrusterInput = new Vector3(thrustInputX, thrustInputY, thrustInputZ);
        // Smoothed input drives the AddForce; raw input still drives boost-axis
        // detection so Shift kicks in instantly without waiting for the ramp.
        float rampRate = thrustRampSeconds > 0.001f ? Time.deltaTime / thrustRampSeconds : 1f;
        _smoothedThrusterInput = Vector3.MoveTowards(_smoothedThrusterInput, thrusterInput, rampRate);

        // Rotation input — mouse delta scaled by mouseSensitivity, plus right-stick
        // contribution scaled by StickLookSensitivity (independent slider).
        float yawInput   = TutorialGate.GetAxisRaw("Mouse X", TutorialAbility.ShipMouseLook) * rotSpeed * inputSettings.mouseSensitivity / 100f;
        float pitchInput = TutorialGate.GetAxisRaw("Mouse Y", TutorialAbility.ShipMouseLook) * rotSpeed * inputSettings.mouseSensitivity / 100f;
        if (TutorialGate.ControllerEnabled && TutorialGate.IsUnlocked(TutorialAbility.ShipMouseLook))
        {
            // Mirror the on-foot stick-look formula (360 °/s × slider) so a
            // sensitivity of 1.0 feels comparable to walking around. The
            // previous 6 °/s base scaled by rotSpeed=5 produced ~30 °/s — about
            // 12× slower than on-foot, hence the "feels sluggish" complaint.
            // Uses ShipStickLookSensitivity (separate slider in the pause menu)
            // so players who like a fast on-foot feel can still tune ship feel
            // independently — flying tolerates less twitch than walking.
            const float kShipStickDegreesPerSecond = 360f;
            float gain = TutorialGate.ShipStickLookSensitivity * kShipStickDegreesPerSecond * Time.unscaledDeltaTime;
            yawInput   += TutorialGate.RightStickX() * gain;
            pitchInput += TutorialGate.RightStickY() * gain * (TutorialGate.InvertLookY ? -1f : 1f);
        }

        // Roll: Q/E OR LB/RB.
        int rollDir = 0;
        if (TutorialGate.RollLeftHeld(TutorialAbility.ShipRoll))  rollDir--;
        if (TutorialGate.RollRightHeld(TutorialAbility.ShipRoll)) rollDir++;
        float rollInput = rollDir * rollSpeed * Time.deltaTime;

        // Calculate rotation
        if (numCollisionTouches == 0)
        {
            var yaw = Quaternion.AngleAxis(yawInput, transform.up);
            var pitch = Quaternion.AngleAxis(-pitchInput, transform.right);
            var roll = Quaternion.AngleAxis(-rollInput, transform.forward);

            targetRot = yaw * pitch * roll * targetRot;
            smoothedRot = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotSmoothSpeed);
            // Auto-align used to live here (Update + Time.deltaTime + FromTo-
            // Rotation), but per-frame variable rate + an unstable rotation
            // axis fed back through Unity's solver and showed up as slow
            // orbit drift. Moved into FixedUpdate with the same stable
            // RotateTowards+LookRotation approach the unpiloted branch uses —
            // see the auto-align block at the bottom of FixedUpdate.
        }
        else
        {
            targetRot = transform.rotation;
            smoothedRot = transform.rotation;
        }
    }

    void FixedUpdate()
    {
        // Gravity always applied
        Vector3 gravity = NBodySimulation.CalculateAcceleration(rb.position);
        rb.AddForce(gravity, ForceMode.Acceleration);

        // Thrusters only if allowed to fly and piloted. Boost (Shift) scales
        // each axis independently so the player can punch up to reach orbit
        // altitude without sacrificing fine horizontal precision (or vice
        // versa). Fuel pools mirror PlayerController's jetpack/down/dir split
        // — same UX vocabulary, same HUD bars.
        if (shipIsPiloted && canFly && CanThrust && !PlayerController.isMapOpen)
        {
            bool boostKey = Input.GetKey(KeyCode.LeftShift);
            float dt = Time.fixedDeltaTime;

            // Per-axis scale: default thrustPowerScale; boostMultiplier×that
            // when (a) boost key held, (b) input on this axis, (c) fuel left.
            float scaleY = thrustPowerScale;
            float scaleZ = thrustPowerScale;
            float scaleX = thrustPowerScale;

            // Boost engages only when fuel > 0 AND the pool isn't in the
            // post-depletion lockout. Once a pool drains to 0, _boostXxxDepleted
            // flips true and stays true until the pool refills back to full —
            // so the boost can't pulse on/off as fuel oscillates while the
            // player keeps Shift held.
            bool boostUp   = boostKey && thrusterInput.y > 0.01f  && _boostFuelUp   > 0f && !_boostUpDepleted;
            bool boostDown = boostKey && thrusterInput.y < -0.01f && _boostFuelDown > 0f && !_boostDownDepleted;
            bool boostDir  = boostKey && (Mathf.Abs(thrusterInput.x) > 0.01f || Mathf.Abs(thrusterInput.z) > 0.01f) && _boostFuelDir > 0f && !_boostDirDepleted;
            _isBoostingThisTick = boostUp || boostDown || boostDir;

            if (boostUp)   scaleY = thrustPowerScale * boostMultiplier;
            if (boostDown) scaleY = thrustPowerScale * boostMultiplier;
            if (boostDir) { scaleX = thrustPowerScale * boostMultiplier; scaleZ = thrustPowerScale * boostMultiplier; }

            // Apply force with potentially different per-axis scales. Use the
            // ramp-smoothed input so a 50ms tap doesn't deliver the same delta-v
            // as a 500ms hold — proportional impulses, not binary thrust.
            Vector3 scaledLocal = new Vector3(_smoothedThrusterInput.x * scaleX, _smoothedThrusterInput.y * scaleY, _smoothedThrusterInput.z * scaleZ);
            Vector3 thrustDir = transform.TransformVector(scaledLocal);
            rb.AddForce(thrustDir * thrustStrength, ForceMode.Acceleration);

            // Reset assist flags + axes at the top of the tick. The blocks
            // below set them back to true / non-zero when active.
            IsMatchingVelocity = false;
            IsCircularizing    = false;
            MatchAssistInput   = Vector3.zero;
            CircularizeAssistInput = Vector3.zero;
            // Gate both flight-assist keys on AI chat typing as a defence-
            // in-depth measure. In practice the player gameObject (and
            // their phone) is disabled while piloting, so the chat can't
            // be open here — but if that invariant ever changes, the
            // typing player must not accidentally engage ship flight
            // assist by hitting V or O in their message.
            bool typing = AIChatScreen.IsTypingActive;
            bool vHeld  = !typing && Input.GetKey(matchVelocityKey);
            bool oHeld  = !typing && Input.GetKey(circularizeKey);

            // ── Match Velocity (V) ──────────────────────────────────────
            // Hold V to fire the ship's thrusters in whatever directions
            // cancel relative velocity to the focused map target. Extra
            // ship takes priority over a marked body (more recent intent).
            // Boost (DIR pool) auto-engages above matchVelocityBoostSpeed-
            // Threshold while fuel remains; once fuel runs out, regular
            // thrust continues (slower) — releasing V lets DIR refill.
            float matchRelMag = 0f;
            if (vHeld)
            {
                Vector3 targetVel = Vector3.zero;
                bool hasTarget = false;
                var map = SolarSystemMapController.Instance;
                if (map != null)
                {
                    if (map.FollowedShip != null && map.FollowedShip != this)
                    {
                        var trb = map.FollowedShip.GetComponent<Rigidbody>();
                        if (trb != null) { targetVel = trb.velocity; hasTarget = true; }
                    }
                    else if (map.PendingHighlight != null)
                    {
                        targetVel = map.PendingHighlight.velocity;
                        hasTarget = true;
                    }
                }
                if (hasTarget)
                {
                    Vector3 relVel = rb.velocity - targetVel;
                    matchRelMag = relVel.magnitude;
                    if (matchRelMag > 0.01f)
                    {
                        // Local-space thrust direction = opposite of relVel.
                        Vector3 thrustLocal = transform.InverseTransformDirection(-relVel.normalized);
                        // Use boost if going fast and DIR pool has fuel (and
                        // isn't in the post-depletion refill lockout).
                        bool useBoost = matchRelMag > matchVelocityBoostSpeedThreshold && _boostFuelDir > 0f && !_boostDirDepleted;
                        float axisScale = useBoost ? thrustPowerScale * boostMultiplier : thrustPowerScale;
                        Vector3 force = transform.TransformDirection(thrustLocal * axisScale) * thrustStrength;
                        // Clamp so we don't overshoot zero in a single tick.
                        float plannedDV = force.magnitude * dt;
                        if (plannedDV > matchRelMag) force *= matchRelMag / plannedDV;
                        rb.AddForce(force, ForceMode.Acceleration);
                        if (useBoost)
                        {
                            _boostFuelDir = Mathf.Clamp(_boostFuelDir - boostDrainPerSec * dt, 0f, boostFuelMax);
                            _isBoostingThisTick = true;
                        }
                        MatchAssistInput = thrustLocal;
                        IsMatchingVelocity = true;
                    }
                }
            }
            IsVelocityMatched = vHeld && matchRelMag > 0f && matchRelMag < matchVelocityMatchedSpeed;

            // ── Circularize (O) ─────────────────────────────────────────
            // Hold near a planet/moon to drive the ship onto a perfect
            // circular orbit at the current altitude. Target velocity:
            //   v_circ_mag = sqrt(G * M / r), direction = current tangential
            //   (or any axis perpendicular to radial if currently purely radial).
            // Apply ship thrust toward (vTarget - vCurrent). Boost (UP pool)
            // engages when the needed deltaV is large; depleted → regular
            // thrust continues. Releasing O lets UP refill.
            float orbitRadialMag = 0f;
            bool orbitInRange = false;
            if (oHeld)
            {
                CelestialBody best = null;
                float bestSqr = float.MaxValue;
                var bodies = NBodySimulation.Bodies;
                if (bodies != null)
                {
                    for (int i = 0; i < bodies.Length; i++)
                    {
                        var b = bodies[i];
                        if (b == null) continue;
                        if (b.bodyType == CelestialBody.BodyType.Sun) continue;
                        float dsq = (b.Position - rb.position).sqrMagnitude;
                        if (dsq < bestSqr) { bestSqr = dsq; best = b; }
                    }
                }
                if (best != null)
                {
                    float maxRange = circularizeRangeMul * best.radius;
                    Vector3 r = rb.position - best.Position;
                    float rMag = r.magnitude;
                    if (rMag <= maxRange && rMag > best.radius * 1.05f)
                    {
                        orbitInRange = true;
                        Vector3 v = rb.velocity - best.velocity;
                        Vector3 radialDir = r / rMag;
                        Vector3 vRadial = Vector3.Dot(v, radialDir) * radialDir;
                        Vector3 vTang = v - vRadial;
                        orbitRadialMag = vRadial.magnitude;

                        // Perfect-circle target speed at this altitude.
                        float vCirc = Mathf.Sqrt(Universe.gravitationalConstant * best.mass / rMag);
                        Vector3 tangDir = vTang.sqrMagnitude > 0.01f
                            ? vTang.normalized
                            : Vector3.Cross(radialDir, Vector3.up).normalized;
                        if (tangDir.sqrMagnitude < 0.01f) tangDir = Vector3.Cross(radialDir, Vector3.right).normalized;
                        Vector3 vTargetLocalFrame = tangDir * vCirc;
                        Vector3 vTargetWorld = vTargetLocalFrame + best.velocity;
                        Vector3 needed = vTargetWorld - rb.velocity;
                        float needMag = needed.magnitude;
                        if (needMag > 0.01f)
                        {
                            Vector3 thrustLocal = transform.InverseTransformDirection(needed.normalized);
                            bool useBoost = needMag > circularizeBoostNeedThreshold && _boostFuelUp > 0f && !_boostUpDepleted;
                            float axisScale = useBoost ? thrustPowerScale * boostMultiplier : thrustPowerScale;
                            Vector3 force = transform.TransformDirection(thrustLocal * axisScale) * thrustStrength;
                            float plannedDV = force.magnitude * dt;
                            if (plannedDV > needMag) force *= needMag / plannedDV;
                            rb.AddForce(force, ForceMode.Acceleration);
                            if (useBoost)
                            {
                                _boostFuelUp = Mathf.Clamp(_boostFuelUp - boostDrainPerSec * dt, 0f, boostFuelMax);
                                _isBoostingThisTick = true;
                            }
                            CircularizeAssistInput = thrustLocal;
                            IsCircularizing = true;
                        }
                    }
                }
            }
            IsOrbitMatched = oHeld && orbitInRange && orbitRadialMag < orbitMatchedRadialSpeed;

            // Fuel bookkeeping — drain the active axes, refill the idle ones.
            // Each pool refills whenever it isn't actively draining (matches the
            // UP/DOWN/DIR jetpack rhythm — depletion lockout below keeps a held
            // Shift from pulsing the boost back on as fuel ticks up).
            _boostFuelUp   = Mathf.Clamp(_boostFuelUp   + (boostUp   ? -boostDrainPerSec : boostRefillPerSec) * dt, 0f, boostFuelMax);
            _boostFuelDown = Mathf.Clamp(_boostFuelDown + (boostDown ? -boostDrainPerSec : boostRefillPerSec) * dt, 0f, boostFuelMax);
            _boostFuelDir  = Mathf.Clamp(_boostFuelDir  + (boostDir  ? -boostDrainPerSec : boostRefillPerSec) * dt, 0f, boostFuelMax);

            // Depletion lockouts — set when a pool reaches 0, cleared only
            // when it refills all the way back to boostFuelMax. While locked,
            // the matching boostXxx flag above stays false, so the player
            // gets regular thrust (not boosted thrust) for the entire refill
            // period — matching the jetpack's "deplete-then-recharge" rhythm.
            if (_boostFuelUp   <= 0f)            _boostUpDepleted   = true;
            if (_boostFuelDown <= 0f)            _boostDownDepleted = true;
            if (_boostFuelDir  <= 0f)            _boostDirDepleted  = true;
            if (_boostFuelUp   >= boostFuelMax)  _boostUpDepleted   = false;
            if (_boostFuelDown >= boostFuelMax)  _boostDownDepleted = false;
            if (_boostFuelDir  >= boostFuelMax)  _boostDirDepleted  = false;
        }
        else
        {
            // Idle ship — passive trickle refill so leaving and coming back
            // gives full boost capacity.
            float dt = Time.fixedDeltaTime;
            _boostFuelUp   = Mathf.Clamp(_boostFuelUp   + boostRefillPerSec * dt, 0f, boostFuelMax);
            _boostFuelDown = Mathf.Clamp(_boostFuelDown + boostRefillPerSec * dt, 0f, boostFuelMax);
            _boostFuelDir  = Mathf.Clamp(_boostFuelDir  + boostRefillPerSec * dt, 0f, boostFuelMax);
        }

        if (numCollisionTouches == 0)
        {
            rb.MoveRotation(smoothedRot);
        }
    }

    void TeleportToBody(CelestialBody body)
    {
        rb.velocity = body.velocity;
        rb.MovePosition(body.transform.position + (transform.position - body.transform.position).normalized * body.radius * 2);
    }

    int GetInputAxis(KeyCode negativeAxis, KeyCode positiveAxis)
    {
        int axis = 0;
        if (Input.GetKey(positiveAxis)) axis++;
        if (Input.GetKey(negativeAxis)) axis--;
        return axis;
    }

    int GatedAxis(KeyCode negativeAxis, KeyCode positiveAxis, TutorialAbility negAbility, TutorialAbility posAbility)
    {
        int axis = 0;
        if (TutorialGate.GetKey(positiveAxis, posAbility)) axis++;
        if (TutorialGate.GetKey(negativeAxis, negAbility)) axis--;
        return axis;
    }

    void HandleCheats()
    {
        if (Universe.cheatsEnabled)
        {
            if (Input.GetKeyDown(KeyCode.Return) && IsPiloted && Time.timeScale != 0)
            {
                var shipHud = FindObjectOfType<ShipHUD>();
                if (shipHud.LockedBody)
                {
                    TeleportToBody(shipHud.LockedBody);
                }
            }
        }
    }

    void InitRigidbody()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.useGravity = false;
        rb.isKinematic = false;
        rb.centerOfMass = Vector3.zero;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        // Ship is "massive" relative to the player (70 kg) so contact
        // depenetration impulses (player capsule on cockpit floor while
        // auto-aligning) transfer almost entirely to the player, not the
        // ship — preserving the orbit. Prefab values currently sit at
        // 10000 (ratio 143:1) which is NOT enough: cumulative sub-step
        // depenetration over thousands of fixed steps still pushes the
        // ship into the planet. 1,000,000 is ~14000:1 — the ship feels
        // like a wall to the player.
        //
        // Safe to override programmatically because every Ship force
        // application uses ForceMode.Acceleration (gravity, thrust, V/O
        // assists) — those are mass-INVARIANT, so mass only changes one
        // thing: how much velocity the ship picks up from a contact
        // impulse. Planet rigidbodies are mass-computed from surface
        // gravity (typically 10^9+), so the ship still bounces off
        // terrain decisively. Set every ship variant (Full / MissingLeft
        // / MissingRight / NoThrusters / extras) consistently rather
        // than relying on prefab values that can drift.
        rb.mass = 1000000f;
    }

    // ── Hatch pressurizer FX ───────────────────────────────────────────────
    void SetupPressurizers()
    {
        var list = new List<Transform>(2);
        var p1 = FindChildByName(transform, "Pressurizer1");
        var p2 = FindChildByName(transform, "Pressurizer2");
        if (p1 != null) list.Add(p1);
        if (p2 != null) list.Add(p2);
        _pressurizers = list.ToArray();
        _pressAudio = new AudioSource[_pressurizers.Length];
        _pressPuff  = new ParticleSystem[_pressurizers.Length];
        for (int i = 0; i < _pressurizers.Length; i++)
        {
            var a = _pressurizers[i].gameObject.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.spatialBlend = 1f;          // 3D — comes from the valve
            a.minDistance = 2f;
            a.maxDistance = 20f;
            _pressAudio[i] = a;
            _pressPuff[i] = BuildPressurizerPuff(_pressurizers[i]);
        }
        if (_pressurizers.Length > 0)
            StartCoroutine(StreamingAudio.Load("Audio/Pressurizer.wav", AudioType.WAV, c => _pressurizerClip = c));
        _prevHatchForPuff = hatchOpen;
    }

    static Transform FindChildByName(Transform root, string name)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && all[i].name == name) return all[i];
        return null;
    }

    // A small, fast-dissipating smoke puff parented to a pressurizer. Direction is
    // driven explicitly by a LOCAL -Y velocity (not the cone's axis, whose
    // convention is ambiguous) so it reliably shoots out the pressurizer's local
    // -Y, which Sam set as the exit direction.
    ParticleSystem BuildPressurizerPuff(Transform parent)
    {
        var go = new GameObject("PressurizerPuff");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        // Align the puff to the SHIP's axes (NOT the rotated pressurizer's). The
        // smoke vents along local -Y = ship-down = toward the floor (the direction
        // that lowering the pressurizer's Y position moves it). The pressurizers'
        // own local -Y pointed inward, so the two puffs were converging BETWEEN
        // them instead of venting out of each one.
        go.transform.rotation = transform.rotation;

        var ps = go.AddComponent<ParticleSystem>();
        if (Application.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        var main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 1.2f;     // lingers; with sustained emission the vent reads ~2s+
        main.startSpeed = 0f;          // direction comes from velocityOverLifetime (-Y)
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
        main.startColor = new Color(0.95f, 0.95f, 1f, 0.55f);
        main.maxParticles = 300;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;  // rides with the ship
        main.gravityModifier = 0f;

        var emission = ps.emission;
        emission.rateOverTime = 0f;    // bursts only (we call Emit)

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;          // tiny spawn cluster at the valve

        // Gentle vent out the ship's -Y (toward the floor) with a little lateral
        // spread. Kept SMALL so the puff hugs the pressurizer instead of streaming
        // several units down into the cabin. Tune here if it should travel more/less.
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
        vel.y = new ParticleSystem.MinMaxCurve(-1.2f, -0.5f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        var sc = new AnimationCurve();
        sc.AddKey(0f, 0.6f);
        sc.AddKey(1f, 1.8f);
        size.size = new ParticleSystem.MinMaxCurve(1f, sc);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            var m = ConcertParticleAssets.GetAlphaBlendCloudMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = m;
            renderer.sharedMaterial = m;
        }
        return ps;
    }

    void FirePressurizers()
    {
        if (_pressurizers == null) return;
        for (int i = 0; i < _pressurizers.Length; i++)
        {
            if (_pressPuff[i] != null) StartCoroutine(EmitPuffOverTime(_pressPuff[i]));
            if (_pressAudio[i] != null && _pressurizerClip != null)
                _pressAudio[i].PlayOneShot(_pressurizerClip, 0.7f);
        }
    }

    // Sustained ~1.5s vent (plus the ~1.2s particle-lifetime tail) so the puff
    // reads as 2s+ of escaping air, not a single instantaneous burst.
    static IEnumerator EmitPuffOverTime(ParticleSystem ps)
    {
        const float dur = 1.5f, step = 0.1f;
        float t = 0f;
        while (t < dur)
        {
            if (ps == null) yield break;
            ps.Emit(8);
            t += step;
            yield return new WaitForSeconds(step);
        }
    }

    public void ToggleHatch()
    {
        hatchOpen = !hatchOpen;
        if (hatchClip != null && crashSource != null)
            crashSource.PlayOneShot(hatchClip, hatchVolume);
        // Pressurizer hiss + smoke puff is driven by the hatchOpen state-change
        // poll in the per-frame update, so it fires for every toggle path.
    }

    public void TogglePiloting()
    {
        if (shipIsPiloted)
        {
            if (!TutorialGate.IsUnlocked(TutorialAbility.ExitPilot)) return;
            StopPilotingShip();
        }
        else
        {
            if (!TutorialGate.IsUnlocked(TutorialAbility.EnterPilot)) return;
            // Refuse to enter the pilot seat when the ship can't fly. Show a
            // brief HUD message so the player knows why nothing happened.
            if (!HasPower)
            {
                GameUI.DisplayInteractionInfo("Ship has no power — wait for solar charge");
                return;
            }
            if (!HasFuel)
            {
                GameUI.DisplayInteractionInfo("Reactor is dry — insert crystals into the reactor");
                return;
            }
            PilotShip();
        }
    }

    public void PilotShip()
    {
        // Single-ship-piloted invariant. TeleportToFollowedShipPilot moves the
        // player from one cockpit directly into another without going through
        // StopPilotingShip; without this loop the abandoned ship retains
        // shipIsPiloted=true and keeps triggering camera shake on its own
        // collisions (e.g. an unmanned ship crashing into a planet while the
        // player is calmly orbiting in a different ship).
        var allShips = FindObjectsOfType<Ship>();
        for (int i = 0; i < allShips.Length; i++)
        {
            var s = allShips[i];
            if (s == null || s == this || !s.shipIsPiloted) continue;
            s.shipIsPiloted = false;
            if (s_pilotedInstance == s) s_pilotedInstance = null;
            if (s.window != null) s.window.SetActive(true);
        }

        // Include inactive: when teleport-to-pilot moves the player from one
        // cockpit to another, the player gameobject is currently disabled
        // (the previous ship's PilotShip turned it off). Without (true) here
        // FindObjectOfType returns null, the pilot field stays null, and the
        // ship ends up in a half-piloted state — player gets stuck on exit
        // because StopPilotingShip can't activate them or restore the camera.
        pilot = FindObjectOfType<PlayerController>(true);
        if (pilot == null) return; // truly no player in scene — bail
        shipIsPiloted = true;
        s_pilotedInstance = this;
        // Power-up SFX. Gated past the first 2s so the load-time auto-pilot during
        // scene/save setup doesn't fire a spurious start-up sound.
        if (_startupClip != null && _pilotSfxSource != null && Time.timeSinceLevelLoad > 2f)
            _pilotSfxSource.PlayOneShot(_startupClip, 0.8f);
        pilot.Camera.transform.parent = camViewPoint;
        pilot.Camera.transform.localPosition = Vector3.zero;
        pilot.Camera.transform.localRotation = Quaternion.identity;
        pilot.gameObject.SetActive(false);
        // The hatch is intentionally NOT forced closed here. The oxygen system
        // (OxygenManager) makes hatch state meaningful — flying with it open
        // bleeds the hull, sealing it before launch is the "do it right" path.
        // Slamming it shut on pilot-enter made that mechanic unreachable and
        // wiped the player's deliberate hatch state. Hatch is now fully
        // player-controlled (HatchButton inside / OutsideHatchTrigger outside).
        window.SetActive(false);
        if (boostMeterUI) boostMeterUI.SetActive(false);

        // Disabling the player gameobject means Unity's physics will NOT
        // fire OnTriggerExit on any interactable we were currently inside —
        // the inside-cockpit BackHatchButton, the OutsideHatchTrigger of
        // another ship we just teleported away from, the previous ship's
        // flightControls. Their playerInInteractionZone stays stuck true,
        // so once the player reactivates the prompt re-asserts globally
        // and pressing F triggers an action from anywhere on the planet.
        // Clear every interactable's in-zone flag, then re-force THIS
        // ship's flightControls so F-to-exit-pilot still works whether we
        // entered via walking-through-trigger OR teleport.
        var allInteractables = FindObjectsOfType<Interactable>(true);
        for (int i = 0; i < allInteractables.Length; i++)
        {
            if (allInteractables[i] == null) continue;
            allInteractables[i].ClearPlayerInInteractionZone();
        }
        if (flightControls != null)
        {
            flightControls.ForcePlayerInInteractionZone();
            GameUI.ClearInteractionPrompt(flightControls);
        }

        // Kick the headlight's shadow-caster state. On a FIRST pilot of a
        // freshly-spawned/teleported ship the shadow flickers as the ship
        // rotates — even with useViewFrustumForShadowCasterCull=false set
        // in Awake. Doing pilot → exit → pilot fixes it for the session,
        // so something settles or is re-asserted on subsequent pilots.
        // Re-applying the shadow settings here (and toggling the cull flag
        // through a true→false transition) forces Unity to refresh its
        // internal shadow culling state for the headlight from the new
        // camera POV.
        if (headlight != null) StartCoroutine(KickHeadlightShadowsAfterPilot());
    }

    System.Collections.IEnumerator KickHeadlightShadowsAfterPilot()
    {
        // Wait one frame so the camera reparent + player deactivation have
        // settled before we poke the shadow system.
        yield return null;
        if (headlight == null) yield break;
        // Re-assert every shadow-related property in one go. The cull flag
        // toggle (true → false next frame) is the key bit — Unity recomputes
        // shadow caster lists when this property is written.
        headlight.renderMode = LightRenderMode.ForcePixel;
        if (headlightShadowResolution > 0)
            headlight.shadowCustomResolution = headlightShadowResolution;
        headlight.shadows = LightShadows.Soft;
        headlight.useViewFrustumForShadowCasterCull = true;
        yield return null;
        if (headlight == null) yield break;
        headlight.useViewFrustumForShadowCasterCull = false;
    }

    void StopPilotingShip()
    {
        shipIsPiloted = false;
        if (s_pilotedInstance == this) s_pilotedInstance = null;
        // Power-down SFX (reverse-feel shutdown). Same 2s gate as start-up so
        // the load-time force-exit during setup stays silent.
        if (_shutdownClip != null && _pilotSfxSource != null && Time.timeSinceLevelLoad > 2f)
            _pilotSfxSource.PlayOneShot(_shutdownClip, 0.5f);
        // Always drop the player at the ship's own pilotSeatPoint. (We used to
        // fall back to pilot.spawnPoint = the starting cabin; removed because
        // it teleported players away from any ship bought far from the cabin.)
        Transform exitPoint = pilotSeatPoint;
        pilot.transform.position = exitPoint.position;
        pilot.transform.rotation = exitPoint.rotation;
        pilot.Rigidbody.velocity = rb.velocity;
        pilot.gameObject.SetActive(true);
        window.SetActive(true);
        // The hatch is intentionally LEFT AS-IS on exit (was force-opened here).
        // Force-opening wiped the player's sealed-hull state every time they
        // stood up — breaking the oxygen sanctuary (e.g. EVAing on a vacuum moon
        // while keeping the hull pressurised). If the player sealed the hatch for
        // flight they now exit into a sealed cockpit and re-open it with the
        // interior HatchButton (TutorialGate is unlocked post-tutorial, so it
        // always works) — you open the door to step out, like an airlock. The
        // common case (entered through an open hatch, never closed it) exits with
        // the hatch already open, so there's no trap.
        pilot.ExitFromSpaceship();
        // Snap orientation smoothing so the player doesn't see a 1-second
        // tilt animation as smoothed_gravity_up catches up after re-entry.
        // In space: pre-arm ship-up blend at 1; on a planet: zero it. Then
        // snap CameraTransformFX's interpolation buffer so the camera
        // doesn't briefly slerp through the pre-pilot rotation.
        pilot.SnapOrientationOnExitPilot(this);
        if (CameraEffectsManager.Instance != null
            && CameraEffectsManager.Instance.TransformFX != null)
        {
            CameraEffectsManager.Instance.TransformFX.SnapToCurrentPlayer();
        }
        if (boostMeterUI) boostMeterUI.SetActive(true);
    }

    void OnCollisionEnter(Collision other)
    {
        if (groundedMask == (groundedMask | (1 << other.gameObject.layer)))
        {
            numCollisionTouches++;
        }
        else
        {
            _nonGroundedContacts++;
            // Subset-track player contacts: if this is the player capsule,
            // remember the controller so the auto-align block can teleport it
            // around the ship CoM in lockstep with the rotation. Look first on
            // the contact's rigidbody, then walk up parents — the visible
            // capsule collider can live on a child of the PlayerController
            // GameObject in some setups.
            var pc = other.rigidbody != null ? other.rigidbody.GetComponent<PlayerController>() : null;
            if (pc == null && other.collider != null) pc = other.collider.GetComponentInParent<PlayerController>();
            if (pc != null)
            {
                _playerContacts++;
                _contactingPlayer = pc;
            }
        }

        // Camera shake (only when piloted)
        if (shipIsPiloted && CameraShake.Instance != null)
        {
            CameraShake.Instance.ShakeFromImpact(other.relativeVelocity.magnitude);
        }

        OnShipCollision?.Invoke(other);

        // Crash SFX based on impact severity. While the ship is unpiloted (idle
        // or being shoved around by enemies / physics glitches), cap the severity
        // at "light" — a parked ship shouldn't get full-volume crash audio for
        // an enemy bump.
        if (crashSource != null)
        {
            float v = other.relativeVelocity.magnitude;
            AudioClip clip;
            if (shipIsPiloted)
            {
                clip = v >= crashHardThreshold   ? crashHardClip   :
                       v >= crashMediumThreshold ? crashMediumClip :
                       v >= crashLightThreshold  ? crashLightClip  : null;
            }
            else
            {
                clip = v >= crashLightThreshold ? crashLightClip : null;
            }
            if (clip != null)
                crashSource.PlayOneShot(clip, crashVolume);
        }
    }

    void OnCollisionExit(Collision other)
    {
        if (groundedMask == (groundedMask | (1 << other.gameObject.layer)))
        {
            numCollisionTouches--;
        }
        else if (_nonGroundedContacts > 0)
        {
            _nonGroundedContacts--;
            var pc = other.rigidbody != null ? other.rigidbody.GetComponent<PlayerController>() : null;
            if (pc == null && other.collider != null) pc = other.collider.GetComponentInParent<PlayerController>();
            if (pc != null && _playerContacts > 0)
            {
                _playerContacts--;
                if (_playerContacts == 0) _contactingPlayer = null;
            }
        }
    }

    public void SetVelocity(Vector3 velocity)
    {
        rb.velocity = velocity;
    }

    public bool ShowHUD => shipIsPiloted;
    public bool HatchOpen => hatchOpen;
    public bool IsPiloted => shipIsPiloted;
    public bool IsLanded => numCollisionTouches > 0;

    // Swaps every ship-hull collider's PhysicMaterial between its original
    // (set in the prefab inspector) and a zero-friction stand-in, based on
    // whether this ship is currently landed.
    //   Landed   → original material restored (walk on hull normally).
    //   Airborne → frictionless material applied (player can bump but
    //              doesn't catch or get stuck while floating past).
    // Cheap: only swaps on a state transition. Originals cached on first
    // call so the inspector material is preserved even after multiple
    // takeoff/landing cycles.
    void UpdateShipFrictionState()
    {
        bool shouldBeFrictionless = !IsLanded;
        if (_shipFrictionlessState.HasValue && _shipFrictionlessState.Value == shouldBeFrictionless)
            return;

        if (_myColliders == null || _myColliders.Length == 0)
        {
            _myColliders = GetComponentsInChildren<Collider>(true);
            // Snapshot original materials so we can restore them later. Done
            // on first call so we never lose the prefab-assigned material
            // to our own frictionless override.
            _myOriginalMats = new PhysicMaterial[_myColliders.Length];
            for (int i = 0; i < _myColliders.Length; i++)
                _myOriginalMats[i] = _myColliders[i] != null ? _myColliders[i].sharedMaterial : null;
        }

        if (shouldBeFrictionless && s_frictionlessMat == null)
        {
            s_frictionlessMat = new PhysicMaterial("ShipHullAirborne_Frictionless")
            {
                dynamicFriction       = 0f,
                staticFriction        = 0f,
                bounciness            = 0f,
                frictionCombine       = PhysicMaterialCombine.Minimum,
                bounceCombine         = PhysicMaterialCombine.Minimum,
            };
        }

        for (int i = 0; i < _myColliders.Length; i++)
        {
            var c = _myColliders[i];
            if (c == null || c.isTrigger) continue;
            c.sharedMaterial = shouldBeFrictionless ? s_frictionlessMat : _myOriginalMats[i];
        }
        _shipFrictionlessState = shouldBeFrictionless;
    }

    /// Returns the Ship currently being piloted by the player, or null if
    /// the player is on foot. Walks all ships — FindObjectOfType<Ship>() is
    /// unsafe with multiple ships in the scene because it returns the first
    /// found regardless of piloting state, so FX that read "the ship's
    /// velocity" would keep tracking an abandoned ship after teleport.
    public static Ship FindPilotedShip()
    {
        var ships = FindObjectsOfType<Ship>(true);
        if (ships == null) return null;
        for (int i = 0; i < ships.Length; i++)
            if (ships[i] != null && ships[i].IsPiloted) return ships[i];
        return null;
    }
    public Rigidbody Rigidbody => rb;

    // --- §5 ship-specific-prompt proximity gate --------------------------------
    // True if the player is piloting THIS ship, or is within `radius` metres of
    // it. Per-instance (multi-ship safe), throttled, and null-safe. Used to gate
    // ship-specific prompts/SFX (reactor, hatch, hull VO, vitals warnings) so they
    // don't fire when the player is nowhere near the ship they refer to.
    const float DefaultPromptRadius = 25f;
    const float NearCheckInterval = 0.2f;
    [System.NonSerialized] PlayerController _proximityPlayer;
    [System.NonSerialized] float _nearCheckTime;
    [System.NonSerialized] bool _nearCached;

    public bool PlayerIsNearOrPiloting(float radius = DefaultPromptRadius)
    {
        if (shipIsPiloted) return true;                 // this ship is being flown
        if (rb == null) return false;
        if (Time.unscaledTime < _nearCheckTime) return _nearCached;
        _nearCheckTime = Time.unscaledTime + NearCheckInterval;

        // Lazy-refind the player only when we don't already have it (never per
        // frame — once found it persists for the session).
        if (_proximityPlayer == null) _proximityPlayer = pilot;
        if (_proximityPlayer == null) _proximityPlayer = FindObjectOfType<PlayerController>(true);
        if (_proximityPlayer == null || _proximityPlayer.Rigidbody == null)
        {
            _nearCached = false;
            return false;
        }

        float r = radius > 0f ? radius : DefaultPromptRadius;
        Vector3 playerPos = _proximityPlayer.Rigidbody.position;
        _nearCached = (playerPos - rb.position).sqrMagnitude <= r * r;
        return _nearCached;
    }

    // World velocity minus the nearest CelestialBody's velocity. Used by
    // speed-driven camera FX (SpeedLinesOverlay, RadialMotionBlurEffect) so
    // a ship sitting still on Humble Abode reads ~0 — without this, the
    // ship's velocity always carries Humble Abode's orbital speed around the
    // sun (thousands of u/s), triggering the FX as soon as the ship spawns.
    public Vector3 RelativeVelocity
    {
        get
        {
            if (rb == null) return Vector3.zero;
            var bodies = NBodySimulation.Bodies;
            if (bodies == null) return rb.velocity;
            CelestialBody nearest = null;
            float bestSqr = float.MaxValue;
            Vector3 myPos = rb.position;
            for (int i = 0; i < bodies.Length; i++)
            {
                var b = bodies[i];
                if (b == null) continue;
                float d = (b.Position - myPos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; nearest = b; }
            }
            return nearest != null ? rb.velocity - nearest.velocity : rb.velocity;
        }
    }

    public void SetHatchOpen(bool open) { hatchOpen = open; }

    // Realign internal rotation targets to the current transform. Used after
    // SaveSystem applies a saved ship rotation — otherwise Ship.FixedUpdate
    // would snap rb back to the scene-default rotation cached in Awake.
    public void SyncRotationToTransform()
    {
        targetRot = transform.rotation;
        smoothedRot = transform.rotation;
    }

    // Exit pilot mode unconditionally — bypasses the TutorialGate check that
    // TogglePiloting enforces. Used by SaveSystem to undo GameSetUp's auto-pilot
    // when the saved state was not piloting. Also clears the flightControls
    // interaction-zone flag so the next interact-button press doesn't snap the
    // player back into the pilot seat — see Interactable.ClearPlayerInInteractionZone
    // for the trigger-vs-teleport rationale.
    public void ForceExitPilot()
    {
        if (!shipIsPiloted) return;
        if (pilot == null) pilot = FindObjectOfType<PlayerController>(true);
        StopPilotingShip();
        if (flightControls != null) flightControls.ClearPlayerInInteractionZone();
    }
}