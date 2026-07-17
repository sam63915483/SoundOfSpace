using System.Collections;
using UnityEngine;

public class PistolController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Icon shown in the hotbar slot when this item is in the bar. Assign on the Player prefab.")]
    public Sprite hotbarIcon;

    [Header("Pistol Settings")]
    public GameObject pistolPrefab;
    public Transform pistolHoldPosition;

    [Header("Hold Offset Adjuster")]
    [Tooltip("Local-space position offset relative to pistolHoldPosition. This is where the GRIP / rotation pivot sits in the player's hand.")]
    public Vector3 holdPositionOffset = Vector3.zero;
    [Tooltip("Local-space resting rotation (Euler degrees) relative to pistolHoldPosition. The equip / recoil animations resolve to this rotation, pivoting around the grip.")]
    public Vector3 holdRotationOffset = Vector3.zero;
    [Tooltip("Local offset of the pistol MODEL inside the grip pivot. Slide the pistol so the grip lines up with the pivot — animations will then rotate around the grip rather than the model's centre.")]
    public Vector3 gripOffset = Vector3.zero;

    [Header("Equip Animation")]
    [Tooltip("Seconds to rotate from equipStartAngle into the rest rotation.")]
    public float equipDuration = 0.4f;
    [Tooltip("Starting rotation angle (degrees) before the equip animation lerps into rest. 0 = no rotation arc, just the position sweep (looks like a clean rise-into-view). Non-zero adds a flip — positive rotates muzzle down at start, negative rotates muzzle up at start.")]
    public float equipStartAngle = 0f;
    [Tooltip("Local axis the equip / unequip rotation pivots around.")]
    public Vector3 equipRotationAxis = Vector3.right;
    [Tooltip("Local-space offset ADDED to holdPositionOffset at the start of the equip animation. The pivot lerps from (holdPositionOffset + this) to holdPositionOffset over equipDuration so the pistol sweeps into the frame instead of rotating in place. Default (0,-0.4,0) starts below the camera. Negate / mirror for unequip.")]
    public Vector3 equipStartPositionOffset = new Vector3(0f, -0.4f, 0f);

    [Header("Shoot Settings")]
    [Tooltip("Damage dealt to an enemy per shot. Enemies have 100 HP by default, so 50 = 2 shots to kill.")]
    public float damagePerShot = 50f;
    [Tooltip("Maximum hitscan range (metres).")]
    public float range = 200f;
    [Tooltip("Minimum seconds between shots.")]
    public float fireRate = 0.25f;
    [Tooltip("Distance (metres) the enemy is knocked back along the shot direction.")]
    public float knockbackDistance = 1.5f;
    [Tooltip("Duration (seconds) of the knockback slide.")]
    public float knockbackDuration = 0.15f;

    [Header("Airborne Recoil Thrust")]
    [Tooltip("Velocity kick (m/s) applied to the PLAYER, opposite the aim direction, when firing while airborne (mid-jump or under jetpack). Aim down to launch up (rocket-jump), aim forward to fly backward. Grounded shots apply no push. 0 disables. 10 = half the player's jumpForce (20).")]
    public float airborneRecoilForce = 10f;

    [Header("Recoil Animation")]
    [Tooltip("Seconds for the recoil kick.")]
    public float recoilBackDuration = 0.05f;
    [Tooltip("Seconds for the pistol to settle back to rest after recoil.")]
    public float recoilReturnDuration = 0.15f;
    [Tooltip("Recoil kick angle (degrees).")]
    public float recoilBackAngle = 18f;
    [Tooltip("Local axis the recoil rotates around (use +X to kick the muzzle up).")]
    public Vector3 recoilAxis = Vector3.right;

    [Header("Slide / Barrel-guard Animation")]
    [Tooltip("Name of the child Transform inside the pistol PREFAB that slides back when firing (the slide / barrel guard). Resolved at equip time.")]
    public string barrelGuardChildName = "Pistol_B_BarrelGuard";
    [Tooltip("Distance (metres, in the barrel guard's local space) the slide moves back when firing.")]
    public float slideBackDistance = 0.065f;
    [Tooltip("Seconds for the slide to travel back.")]
    public float slideBackDuration = 0.04f;
    [Tooltip("Seconds for the slide to return to rest.")]
    public float slideReturnDuration = 0.12f;
    [Tooltip("Local-space direction the slide moves when firing. Default -Z (Vector3.back) is correct when the gun's barrel points along +Z.")]
    public Vector3 slideAxis = Vector3.back;

    [Header("Ammo")]
    [Tooltip("Magazine capacity. Player runs out at 0 and must press R to reload.")]
    public int maxAmmo = 10;

    [Header("Reload Animation")]
    [Tooltip("Name of the magazine child Transform inside the prefab. Resolved at equip time.")]
    public string magazineChildName = "Pistol_B_Mag";
    [Tooltip("Roll angle (degrees). Positive = clockwise from the player's POV when looking down the barrel.")]
    public float reloadRotateAngle = 30f;
    [Tooltip("Local axis to roll around. Default Vector3.forward = the barrel direction, so this rolls the gun rather than pitching the muzzle.")]
    public Vector3 reloadRotateAxis = Vector3.forward;
    [Tooltip("Seconds to roll the gun into the reload pose.")]
    public float reloadRotateInDuration = 0.18f;
    [Tooltip("Seconds to roll the gun back to rest after reload completes.")]
    public float reloadRotateOutDuration = 0.18f;
    [Tooltip("Local-space direction the magazine ejects. Slight forward/back component compensates for the grip's tilt — straight down (0,-1,0) clips through the handle on most prefabs. Tune in Play mode.")]
    public Vector3 magDropAxis = new Vector3(0f, -1f, 0.2f);
    [Tooltip("Distance (metres) the magazine travels along magDropAxis before being hidden.")]
    public float magDropDistance = 0.12f;
    [Tooltip("Seconds for the magazine to slide out.")]
    public float magDropDuration = 0.18f;
    [Tooltip("Seconds the magazine is hidden between drop-out and re-insert.")]
    public float magHiddenPause = 0.10f;
    [Tooltip("Seconds for the new magazine to slide in.")]
    public float magInsertDuration = 0.18f;

    [Header("Tracer (visual only — no physics)")]
    [Tooltip("Optional: prefab spawned at the muzzle alongside the tracer (e.g. a muzzle-flash effect).")]
    public GameObject tracerPrefab;
    [Tooltip("Total seconds the tracer effect lives. Head reaches the hit point in bulletFlightDuration; the tail then catches up over the remaining time.")]
    public float tracerDuration = 0.12f;
    [Tooltip("Seconds for the bullet head to travel muzzle→hit. Short = snappy. The tail follows over (tracerDuration - bulletFlightDuration).")]
    public float bulletFlightDuration = 0.03f;
    [Tooltip("Diameter (metres) of an optional bright sphere riding at the front of the tracer. 0 = no sphere (line-only, looks cleaner). Bump to 0.02–0.04 if you want a visible bullet head.")]
    public float bulletScale = 0f;
    [Tooltip("Width (metres) at the BRIGHT END (head) of the tracer streak. The tail tapers to 0 — the line is teardrop-shaped. Try 0.02–0.04 for a thin needle, 0.05+ for chunky.")]
    public float tracerWidth = 0.03f;
    [Tooltip("Outer halo colour. HDR values >1 (e.g. (1.4, 1.1, 0.4)) glow harder against bright skies.")]
    public Color tracerColor = new Color(1.4f, 1.1f, 0.4f, 1f);
    [Tooltip("Inner core colour, drawn as a thinner brighter line on TOP of the halo (additive). Near-white HDR makes the tracer readable against any background — including bright sky. Set alpha 0 (or all RGB to 0) to disable the core and use just the halo.")]
    public Color tracerCoreColor = new Color(1.6f, 1.5f, 1.2f, 1f);
    [Tooltip("Inner-core width as a fraction of tracerWidth. 0.35 = core is 35% of halo width. The core glows white-hot inside the colored halo.")]
    [Range(0.05f, 1f)]
    public float tracerCoreWidthRatio = 0.35f;
    [Tooltip("Maximum visual length of the tracer in metres, measured from the muzzle. The tracer extends along the shot direction and is CAPPED at this length even if the actual hit point is farther away. Without a cap, far-range shots produce a foreshortened streak (most of its world-space length points along your view direction, so it shrinks to a few pixels on screen). Capping keeps the tracer close to the muzzle = always large + visible. Hit detection / damage are unaffected. 12–18m reads well; lower for snappier, higher to see longer streaks at close range.")]
    public float maxTracerLength = 15f;

    [Header("Physics Knockback")]
    [Tooltip("Base impulse (N·s) applied to a non-enemy physics object when hit. Velocity change = impulse / mass, so light loose parts (thrusters, satellite dish, solar panel) get a visible nudge.")]
    public float bulletImpulse = 8f;
    [Tooltip("Minimum velocity change (m/s) applied to ANY physics object on hit, regardless of mass. Without this, very heavy bodies (the ship is 10,000kg) would receive a velocity change small enough to be invisible. 0.5 ≈ 50cm/sec — clearly visible push on the ship per shot. Lower for subtler nudges, higher to launch heavy objects. Friction with the ground will damp slow drifts on grounded ships, so values < 0.3 may not register visibly.")]
    public float minImpactVelocityChange = 0.5f;
    [Tooltip("Optional: a SCENE Transform on the equipped pistol model where the tracer starts. If null, falls back to muzzleChildName lookup, then to a point in front of the camera.")]
    public Transform muzzlePoint;
    [Tooltip("Name of a child Transform inside the pistol PREFAB to use as the muzzle (e.g. \"Pistol_B_Barrel\"). Resolved at equip time. Use this when the muzzle lives inside the prefab — Unity won't let you drag prefab children into muzzlePoint directly.")]
    public string muzzleChildName = "Pistol_B_Barrel";

    [Header("Sound Effects")]
    [SerializeField] AudioClip shootClip;
    [SerializeField, Range(0, 1)] float shootVolume = 0.7f;
    [Tooltip("Sound played during reload. Plays after reloadSoundDelay seconds.")]
    [SerializeField] AudioClip reloadClip;
    [SerializeField, Range(0, 1)] float reloadVolume = 0.7f;
    [Tooltip("Seconds after pressing R before the reload sound plays. Tune to match the visible mag-drop / mag-insert moment in the animation.")]
    public float reloadSoundDelay = 0.3f;

    GameObject _currentPistolInstance;
    Transform _pivot;
    Transform _resolvedMuzzle;
    Transform _resolvedBarrelGuard;
    Vector3 _barrelGuardRestLocalPos;
    Transform _resolvedMagazine;
    Vector3 _magazineRestLocalPos;
    int _currentAmmo;
    bool _isReloading;
    bool _pistolUnlocked;
    bool _isRecoiling;
    float _lastShotTime = -999f;
    Coroutine _equipCoroutine;
    Coroutine _recoilCoroutine;
    Coroutine _slideCoroutine;
    Coroutine _reloadCoroutine;
    Coroutine _reloadSoundCoroutine;
    AudioSource _audioSource;

    // Pivots whose unequip-animation coroutine is still running. If a new
    // EquipPistol stops that coroutine, its destroy callback never fires —
    // we'd leak a stuck pivot+pistol on screen. Instead, EquipPistol drains
    // this list synchronously before starting fresh.
    readonly System.Collections.Generic.List<GameObject> _pendingDestroyPivots = new System.Collections.Generic.List<GameObject>();

    AxeController _axeController;
    FishingRodController _fishingRodController;
    GuitarController _guitarController;
    WaterBottleController _waterBottleController;
    PlayerPickup _playerPickup;
    PlayerController _playerController;
    Ship _ship;

    public bool IsEquipped => _currentPistolInstance != null;
    public bool IsUnlocked => _pistolUnlocked;
    public int  CurrentAmmo => _currentAmmo;
    public int  MaxAmmo => maxAmmo;
    public bool IsReloading => _isReloading;
    public int  ShotsFiredCount { get; private set; }
    public void SetAmmo(int n) => _currentAmmo = Mathf.Clamp(n, 0, maxAmmo);

    void Start()
    {
        _axeController         = GetComponent<AxeController>();
        _fishingRodController  = GetComponent<FishingRodController>();
        _guitarController      = GetComponent<GuitarController>();
        _waterBottleController = GetComponent<WaterBottleController>();
        _playerPickup          = GetComponent<PlayerPickup>();
        _playerController      = GetComponent<PlayerController>();
        _ship                  = FindObjectOfType<Ship>();

        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;

        _currentAmmo = maxAmmo;
    }

    void Update()
    {
        if (_ship != null && _ship.IsPiloted) return;
        if (_currentPistolInstance == null) return;

        // Live-apply the three offset fields so the inspector values can be tuned
        // in Play mode without re-equipping. Skip while the equip animation is
        // running (it drives the pivot itself) and skip rotation during recoil
        // or reload so those animations aren't overwritten mid-frame.
        if (_equipCoroutine == null && _pivot != null)
        {
            _pivot.localPosition = holdPositionOffset;
            _currentPistolInstance.transform.localPosition = gripOffset;
            if (!_isRecoiling && !_isReloading) _pivot.localRotation = Quaternion.Euler(holdRotationOffset);
        }

        if (_equipCoroutine != null) return;

        // Phone open ⇒ R is being used to rotate the phone (PlayerPhoneUI),
        // so swallow it here so the pistol doesn't also reload on the same press.
        if (TutorialGate.ReloadPressed() && !_isReloading && _currentAmmo < maxAmmo
            && !PlayerPhoneUI.IsOpen)
        {
            StartReload();
            return;
        }
        if (_isReloading) return;
        if (Time.time - _lastShotTime < fireRate) return;
        if (_currentAmmo <= 0) return;
        if (TutorialGate.FirePressed()) TriggerShot();
    }

    void EquipPistol()
    {
        if (pistolPrefab == null || pistolHoldPosition == null) return;
        if (_axeController          != null && _axeController.IsEquipped) return;
        if (_fishingRodController   != null && _fishingRodController.IsEquipped) return;
        if (_waterBottleController  != null && _waterBottleController.IsEquipped) return;
        if (_guitarController       != null && _guitarController.IsEquipped) return;
        if (_playerPickup           != null && _playerPickup.IsHoldingObject) return;

        if (_equipCoroutine != null) StopCoroutine(_equipCoroutine);
        if (_recoilCoroutine != null) { StopCoroutine(_recoilCoroutine); _recoilCoroutine = null; _isRecoiling = false; }
        if (_slideCoroutine != null) { StopCoroutine(_slideCoroutine); _slideCoroutine = null; }
        if (_reloadCoroutine != null) { StopCoroutine(_reloadCoroutine); _reloadCoroutine = null; _isReloading = false; }
        if (_reloadSoundCoroutine != null) { StopCoroutine(_reloadSoundCoroutine); _reloadSoundCoroutine = null; }

        // Reap orphans from any prior unequip animations whose destroy
        // callback didn't get to run because we just stopped the coroutine.
        for (int i = 0; i < _pendingDestroyPivots.Count; i++)
            if (_pendingDestroyPivots[i] != null) Destroy(_pendingDestroyPivots[i]);
        _pendingDestroyPivots.Clear();

        var pivotGo = new GameObject("PistolPivot");
        pivotGo.transform.SetParent(pistolHoldPosition, false);
        _pivot = pivotGo.transform;

        _currentPistolInstance = Instantiate(pistolPrefab, _pivot);
        _currentPistolInstance.transform.localPosition = gripOffset;
        _currentPistolInstance.transform.localRotation = Quaternion.identity;

        var rb = _currentPistolInstance.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        foreach (var col in _currentPistolInstance.GetComponentsInChildren<Collider>()) col.enabled = false;

        _resolvedMuzzle = null;
        if (!string.IsNullOrEmpty(muzzleChildName))
            _resolvedMuzzle = FindChildByName(_currentPistolInstance.transform, muzzleChildName);

        _resolvedBarrelGuard = null;
        if (!string.IsNullOrEmpty(barrelGuardChildName))
        {
            _resolvedBarrelGuard = FindChildByName(_currentPistolInstance.transform, barrelGuardChildName);
            if (_resolvedBarrelGuard != null) _barrelGuardRestLocalPos = _resolvedBarrelGuard.localPosition;
        }

        _resolvedMagazine = null;
        if (!string.IsNullOrEmpty(magazineChildName))
        {
            _resolvedMagazine = FindChildByName(_currentPistolInstance.transform, magazineChildName);
            if (_resolvedMagazine != null) _magazineRestLocalPos = _resolvedMagazine.localPosition;
        }

        Quaternion rest    = Quaternion.Euler(holdRotationOffset);
        Quaternion startRot = rest * Quaternion.AngleAxis(-equipStartAngle, equipRotationAxis);
        Vector3 restPos  = holdPositionOffset;
        Vector3 startPos = holdPositionOffset + equipStartPositionOffset;
        _pivot.localPosition = startPos;
        _pivot.localRotation = startRot;

        _equipCoroutine = StartCoroutine(AnimateEquipPose(startPos, restPos, startRot, rest, equipDuration, null));
    }

    void UnequipPistol()
    {
        if (_currentPistolInstance == null || _pivot == null) return;
        if (_recoilCoroutine != null) { StopCoroutine(_recoilCoroutine); _recoilCoroutine = null; _isRecoiling = false; }
        if (_slideCoroutine != null) { StopCoroutine(_slideCoroutine); _slideCoroutine = null; }
        if (_reloadCoroutine != null) { StopCoroutine(_reloadCoroutine); _reloadCoroutine = null; _isReloading = false; }
        if (_reloadSoundCoroutine != null) { StopCoroutine(_reloadSoundCoroutine); _reloadSoundCoroutine = null; }
        if (_equipCoroutine != null) StopCoroutine(_equipCoroutine);

        Quaternion startRot = _pivot.localRotation;
        Quaternion endRot   = startRot * Quaternion.AngleAxis(-180f, equipRotationAxis);
        Vector3 startPos = _pivot.localPosition;
        Vector3 endPos   = holdPositionOffset + equipStartPositionOffset;
        var pivot = _pivot;
        GameObject pivotGo = pivot.gameObject;
        _pendingDestroyPivots.Add(pivotGo);
        _currentPistolInstance = null;
        _pivot = null;
        _resolvedMuzzle = null;
        _resolvedBarrelGuard = null;
        _resolvedMagazine = null;
        _equipCoroutine = StartCoroutine(AnimateEquipPoseOn(pivot, startPos, endPos, startRot, endRot, equipDuration, () =>
        {
            if (pivotGo != null) Destroy(pivotGo);
            _pendingDestroyPivots.Remove(pivotGo);
            _equipCoroutine = null;
        }));
    }

    /// <summary>Show/hide the equipped pistol viewmodel (KillShotCam hides it during the
    /// bullet cinematic so the gun doesn't fly along with the borrowed camera).</summary>
    public void SetViewmodelVisible(bool visible)
    {
        if (_currentPistolInstance == null) return;
        var rends = _currentPistolInstance.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
            if (rends[i] != null) rends[i].enabled = visible;
    }

    void TriggerShot()
    {
        _lastShotTime = Time.time;
        _currentAmmo--;
        ShotsFiredCount++;
        if (shootClip != null && _audioSource != null) _audioSource.PlayOneShot(shootClip, shootVolume);
        GamepadRumble.Pulse(0.25f, 0.9f, 0.1f);

        var cam = Camera.main;
        if (cam == null) return;
        Vector3 origin = cam.transform.position;
        Vector3 forward = cam.transform.forward;

        Vector3 endPoint = origin + forward * range;
        bool killCamTookShot = false;
        if (Physics.Raycast(origin, forward, out RaycastHit hit, range, ~0, QueryTriggerInteraction.Ignore))
        {
            endPoint = hit.point;
            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                // Knockback BEFORE damage so the kill shot's direction is
                // captured (TakeDamage may trigger Die which reads the cached
                // direction for the ragdoll's backwards momentum).
                damageable.ApplyKnockback(forward, knockbackDistance, knockbackDuration);

                // KILL-SHOT CINEMATIC: if this shot kills an enemy, hand the moment to
                // KillShotCam — it flies the camera after the bullet in slow-mo, paints the
                // target's real hit colliders + a targeting bracket, and applies the kill
                // (blood + damage, deferred via callback) when the bullet lands. Falls back
                // to the normal instant path if the cam declines (already active / piloting
                // / slow-mo disabled in settings).
                var ecKill = damageable as EnemyController;
                if (ecKill != null && !ecKill.IsDying && ecKill.CurrentHealth <= damagePerShot)
                {
                    Transform kcMuzzle = muzzlePoint != null ? muzzlePoint : _resolvedMuzzle;
                    Vector3 kcStart = kcMuzzle != null ? kcMuzzle.position : (origin + forward * 0.5f);
                    Vector3 kcPoint = hit.point;
                    Vector3 kcNormal = hit.normal;
                    Transform kcParent = hit.collider != null ? hit.collider.transform : null;
                    killCamTookShot = KillShotCam.TryPlay(kcStart, kcPoint, ecKill, () =>
                    {
                        BloodFX.Instance?.SpawnSpray(kcPoint, kcNormal, forward, kcParent);
                        // Wet squelch lands the same instant the blood pops — sound + spray together.
                        // Hand-rolled source instead of PlayClipAtPoint: volume clamps at 1, so
                        // "louder" comes from half-2D blend + a fat minDistance (full volume
                        // out to 10m) — reads ~2.5x the old default-rolloff loudness.
                        if (killImpactClip != null)
                        {
                            var sgo = new GameObject("KillSquelch");
                            sgo.transform.position = kcPoint;
                            var src = sgo.AddComponent<AudioSource>();
                            src.clip = killImpactClip;
                            src.volume = 1f;
                            src.spatialBlend = 0.5f;
                            src.minDistance = 10f;
                            src.maxDistance = 60f;
                            src.rolloffMode = AudioRolloffMode.Linear;
                            src.Play();
                            Destroy(sgo, killImpactClip.length + 0.1f);
                        }
                        ecKill.TakeDamage(damagePerShot);
                    }, this);
                }

                if (!killCamTookShot)
                {
                    // Blood burst out of the entry wound, back toward the shooter.
                    // Spawn BEFORE TakeDamage: a kill shot triggers death (ragdoll +
                    // collider disable) which otherwise suppresses the spray. Parent
                    // it to the hit collider so it rides with a moving enemy.
                    BloodFX.Instance?.SpawnSpray(hit.point, hit.normal, forward,
                        hit.collider != null ? hit.collider.transform : null);

                    damageable.TakeDamage(damagePerShot);
                }

                var mgr = CameraEffectsManager.Instance;
                if (mgr != null && mgr.MasterEnabled && mgr.Input != null && mgr.Input.fxEnemyHitMicroShake
                    && CameraShake.Instance != null)
                {
                    CameraShake.Instance.TriggerShake(0.05f, 0.1f, 4f);
                }
            }
            // Apply bullet physics impulse to the hit rigidbody, regardless of
            // whether a damageable was found. Three cases this serves:
            //   1. Ship parts / loose thrusters (no damageable)         → push
            //   2. Live alien (damageable on root, no rb)               → no-op (rb is null)
            //   3. Ragdoll corpse (damageable on root, bone has rb)     → push at hit.point
            // The kinematic check filters out enemies (kinematic rb), which
            // do their own kinematic knockback through the damageable path.
            if (bulletImpulse > 0f || minImpactVelocityChange > 0f)
            {
                var rb = hit.collider.attachedRigidbody;
                if (rb != null && !rb.isKinematic
                    && rb.GetComponentInParent<PlayerController>() == null)
                {
                    float massScaledV = bulletImpulse / Mathf.Max(rb.mass, 0.0001f);
                    float deltaV = Mathf.Max(massScaledV, minImpactVelocityChange);
                    rb.AddForceAtPosition(forward * deltaV, hit.point, ForceMode.VelocityChange);
                }
            }
        }

        // Airborne recoil thrust — shove the player opposite the aim direction
        // when firing mid-air (jump or jetpack). Aiming down launches them up
        // (rocket-jump); grounded shots get no push. VelocityChange is the same
        // mass-independent mode the jump uses, so the field reads directly as m/s.
        if (airborneRecoilForce > 0f && _playerController != null && !_playerController.IsOnGround)
            _playerController.Rigidbody.AddForce(-forward * airborneRecoilForce, ForceMode.VelocityChange);

        // Gunshot noise: every enemy within earshot locks onto the shooter and charges —
        // guns are LOUD (stealth revamp). Fires per shot, hit or miss.
        EnemyController.AlertNearby(origin, 33f);

        Transform muzzle = muzzlePoint != null ? muzzlePoint : _resolvedMuzzle;
        Vector3 tracerStart = muzzle != null ? muzzle.position : (origin + forward * 0.5f);
        // Cap the tracer's visual end-point in world distance from the muzzle.
        // Far-range tracers point along the camera's view direction, which
        // foreshortens them down to a few pixels on screen — invisible against
        // the sky. By keeping the visible streak short and close to the muzzle
        // it stays large + readable on screen at any aim direction.
        Vector3 toHit = endPoint - tracerStart;
        float hitDist = toHit.magnitude;
        Vector3 tracerDir = hitDist > 0.0001f ? toHit / hitDist : forward;
        float tracerLen = Mathf.Min(maxTracerLength, hitDist);
        Vector3 tracerEnd = tracerStart + tracerDir * tracerLen;
        // The kill-cam draws its own slow bullet along the full path — the normal fast
        // tracer would race ahead of it and read as two bullets.
        if (!killCamTookShot) SpawnTracer(tracerStart, tracerEnd, cam.transform);

        if (_recoilCoroutine != null) StopCoroutine(_recoilCoroutine);
        _recoilCoroutine = StartCoroutine(RecoilRoutine());

        if (_resolvedBarrelGuard != null)
        {
            if (_slideCoroutine != null) StopCoroutine(_slideCoroutine);
            _slideCoroutine = StartCoroutine(SlideRoutine());
        }
    }

    IEnumerator SlideRoutine()
    {
        yield return SlideBarrelGuardBack();
        yield return SlideBarrelGuardForward();
        _slideCoroutine = null;
    }

    IEnumerator SlideBarrelGuardBack()
    {
        Transform t = _resolvedBarrelGuard;
        if (t == null) yield break;
        Vector3 axis = slideAxis.sqrMagnitude > 0.0001f ? slideAxis.normalized : Vector3.back;
        Vector3 rest = _barrelGuardRestLocalPos;
        Vector3 back = rest + axis * slideBackDistance;

        float elapsed = 0f;
        while (elapsed < slideBackDuration && t != null)
        {
            t.localPosition = Vector3.Lerp(rest, back, elapsed / slideBackDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (t != null) t.localPosition = back;
    }

    IEnumerator SlideBarrelGuardForward()
    {
        Transform t = _resolvedBarrelGuard;
        if (t == null) yield break;
        Vector3 axis = slideAxis.sqrMagnitude > 0.0001f ? slideAxis.normalized : Vector3.back;
        Vector3 rest = _barrelGuardRestLocalPos;
        Vector3 back = rest + axis * slideBackDistance;

        float elapsed = 0f;
        while (elapsed < slideReturnDuration && t != null)
        {
            t.localPosition = Vector3.Lerp(back, rest, elapsed / slideReturnDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (t != null) t.localPosition = rest;
    }

    void StartReload()
    {
        if (_isReloading || _currentAmmo == maxAmmo || _pivot == null) return;
        if (_recoilCoroutine != null) { StopCoroutine(_recoilCoroutine); _recoilCoroutine = null; _isRecoiling = false; }
        if (_slideCoroutine != null)  { StopCoroutine(_slideCoroutine);  _slideCoroutine = null; }
        if (_reloadSoundCoroutine != null) { StopCoroutine(_reloadSoundCoroutine); _reloadSoundCoroutine = null; }
        if (_resolvedBarrelGuard != null) _resolvedBarrelGuard.localPosition = _barrelGuardRestLocalPos;
        _reloadCoroutine = StartCoroutine(ReloadRoutine());
        _reloadSoundCoroutine = StartCoroutine(PlayReloadClipAfterDelay());
    }

    IEnumerator PlayReloadClipAfterDelay()
    {
        if (reloadSoundDelay > 0f) yield return new WaitForSeconds(reloadSoundDelay);
        if (reloadClip != null && _audioSource != null) _audioSource.PlayOneShot(reloadClip, reloadVolume);
        _reloadSoundCoroutine = null;
    }

    IEnumerator ReloadRoutine()
    {
        _isReloading = true;
        Transform pivot = _pivot;
        Quaternion rest   = Quaternion.Euler(holdRotationOffset);
        Quaternion rolled = rest * Quaternion.AngleAxis(reloadRotateAngle, reloadRotateAxis);

        // Phase 1: roll the gun clockwise (from player's POV).
        yield return AnimateRotationOn(pivot, rest, rolled, reloadRotateInDuration, null);

        // Phase 2: rack the slide back and HOLD it open.
        if (_resolvedBarrelGuard != null) yield return SlideBarrelGuardBack();

        // Phase 3: magazine ejects along the angled drop axis, then hides.
        if (_resolvedMagazine != null)
        {
            Vector3 axis = magDropAxis.sqrMagnitude > 0.0001f ? magDropAxis.normalized : Vector3.down;
            Vector3 droppedPos = _magazineRestLocalPos + axis * magDropDistance;
            yield return AnimateLocalPos(_resolvedMagazine, _magazineRestLocalPos, droppedPos, magDropDuration);
            if (_resolvedMagazine != null) _resolvedMagazine.gameObject.SetActive(false);
        }

        yield return new WaitForSeconds(magHiddenPause);

        // Phase 4: fresh magazine slides in from the same drop offset.
        if (_resolvedMagazine != null)
        {
            Vector3 axis = magDropAxis.sqrMagnitude > 0.0001f ? magDropAxis.normalized : Vector3.down;
            Vector3 startPos = _magazineRestLocalPos + axis * magDropDistance;
            _resolvedMagazine.localPosition = startPos;
            _resolvedMagazine.gameObject.SetActive(true);
            yield return AnimateLocalPos(_resolvedMagazine, startPos, _magazineRestLocalPos, magInsertDuration);
        }

        // Phase 5: slide returns to rest.
        if (_resolvedBarrelGuard != null) yield return SlideBarrelGuardForward();

        // Phase 6: roll back to rest.
        if (pivot != null) yield return AnimateRotationOn(pivot, rolled, rest, reloadRotateOutDuration, null);

        _currentAmmo = maxAmmo;
        _isReloading = false;
        _reloadCoroutine = null;
    }

    IEnumerator AnimateLocalPos(Transform t, Vector3 from, Vector3 to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && t != null)
        {
            t.localPosition = Vector3.Lerp(from, to, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (t != null) t.localPosition = to;
    }

    void SpawnTracer(Vector3 start, Vector3 end, Transform parent)
    {
        Vector3 dir = end - start;
        if (dir.sqrMagnitude < 0.0001f) return;

        // Root for the tracer — a LineRenderer that explicitly connects the
        // streak's tail to its head, plus an optional bright bullet head sphere.
        // Both are camera-parented with local positions so floating-origin
        // shifts and player movement carry them cleanly during the short life.
        var tracer = new GameObject("PistolTracer");
        Vector3 muzzleLocal, hitLocal;
        if (parent != null)
        {
            tracer.transform.SetParent(parent, worldPositionStays: false);
            muzzleLocal = parent.InverseTransformPoint(start);
            hitLocal    = parent.InverseTransformPoint(end);
        }
        else
        {
            muzzleLocal = start;
            hitLocal    = end;
        }

        // Two-pass tracer: a coloured halo line + a thinner near-white core line
        // drawn on top. Both are additive, so they stack — the bright core stays
        // readable against any background (including sky) while the halo gives
        // the tracer its character.
        var halo = BuildTracerLine(tracer.transform, "TracerHalo", muzzleLocal, tracerWidth, tracerColor);
        LineRenderer core = null;
        bool coreEnabled = (tracerCoreColor.r + tracerCoreColor.g + tracerCoreColor.b) > 0.001f && tracerCoreColor.a > 0.001f;
        if (coreEnabled)
            core = BuildTracerLine(tracer.transform, "TracerCore", muzzleLocal, tracerWidth * tracerCoreWidthRatio, tracerCoreColor);

        GameObject head = null;
        if (bulletScale > 0f)
        {
            head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = head.GetComponent<Collider>();
            if (col != null) Destroy(col);
            head.transform.SetParent(tracer.transform, false);
            head.transform.localScale    = Vector3.one * bulletScale;
            head.transform.localPosition = muzzleLocal;
            var hr = head.GetComponent<Renderer>();
            if (hr != null)
            {
                hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                hr.receiveShadows    = false;
                hr.material          = MakeTracerMaterial(tracerColor);
            }
        }

        if (tracerPrefab != null)
        {
            var fx = Instantiate(tracerPrefab);
            foreach (var col in fx.GetComponentsInChildren<Collider>()) col.enabled = false;
            if (parent != null) fx.transform.SetParent(parent, worldPositionStays: false);
            fx.transform.localPosition = muzzleLocal;
            Vector3 fwd = hitLocal - muzzleLocal;
            if (fwd.sqrMagnitude > 0.0001f)
                fx.transform.localRotation = Quaternion.LookRotation(fwd.normalized);
            Destroy(fx, tracerDuration);
        }

        StartCoroutine(AnimateTracer(tracer, halo, core, head, muzzleLocal, hitLocal, bulletFlightDuration, tracerDuration));
    }

    LineRenderer BuildTracerLine(Transform parent, string name, Vector3 startLocal, float headWidth, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.positionCount = 2;
        lr.SetPosition(0, startLocal);
        lr.SetPosition(1, startLocal); // zero-length at muzzle on first frame
        // Position 0 = tail (sharp point), position 1 = head (full width).
        lr.startWidth = 0f;
        lr.endWidth   = headWidth;
        lr.material   = MakeTracerMaterial(color);
        lr.startColor = color;
        lr.endColor   = color;
        lr.numCornerVertices = 0;
        lr.numCapVertices    = 4;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows    = false;
        lr.alignment         = LineAlignment.View;
        lr.textureMode       = LineTextureMode.Stretch;
        return lr;
    }

    IEnumerator AnimateTracer(GameObject root, LineRenderer halo, LineRenderer core, GameObject head, Vector3 muzzleLocal, Vector3 hitLocal, float flightDur, float totalDur)
    {
        if (halo == null) yield break;
        flightDur = Mathf.Max(0.001f, flightDur);
        totalDur  = Mathf.Max(flightDur + 0.01f, totalDur);
        float tailDur = totalDur - flightDur;

        Color haloBase = halo.startColor;
        Color coreBase = core != null ? core.startColor : Color.clear;
        float elapsed = 0f;
        while (elapsed < totalDur && halo != null)
        {
            float headT = Mathf.Clamp01(elapsed / flightDur);
            float tailT = Mathf.Clamp01((elapsed - flightDur) / tailDur);

            Vector3 headPos = Vector3.Lerp(muzzleLocal, hitLocal, headT);
            Vector3 tailPos = Vector3.Lerp(muzzleLocal, hitLocal, tailT);

            halo.SetPosition(0, tailPos);
            halo.SetPosition(1, headPos);
            if (core != null)
            {
                core.SetPosition(0, tailPos);
                core.SetPosition(1, headPos);
            }

            if (head != null)
            {
                head.transform.localPosition = headPos;
                if (headT >= 1f)
                {
                    var hr = head.GetComponent<Renderer>();
                    if (hr != null) hr.enabled = false;
                }
            }

            // Hold full alpha during flight, then fade as the tail catches up.
            float alpha = elapsed < flightDur ? 1f : 1f - (elapsed - flightDur) / tailDur;
            var hc = haloBase; hc.a = alpha; halo.startColor = hc; halo.endColor = hc;
            if (core != null)
            {
                var cc = coreBase; cc.a = alpha; core.startColor = cc; core.endColor = cc;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }
        if (root != null) Destroy(root);
    }

    static Shader s_tracerShader;
    static Shader GetTracerShader()
    {
        if (s_tracerShader != null) return s_tracerShader;
        s_tracerShader = Shader.Find("Particles/Additive");
        if (s_tracerShader == null) s_tracerShader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (s_tracerShader == null) s_tracerShader = Shader.Find("Sprites/Default");
        if (s_tracerShader == null) s_tracerShader = Shader.Find("Unlit/Color");
        return s_tracerShader;
    }

    static Material MakeTracerMaterial(Color color)
    {
        var mat = new Material(GetTracerShader());
        var glow = GetSoftGlowTexture();
        if (mat.HasProperty("_MainTex"))   mat.SetTexture("_MainTex", glow);
        if (mat.HasProperty("_BaseMap"))   mat.SetTexture("_BaseMap", glow);
        if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", color);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        return mat;
    }

    static Texture2D s_softGlow;
    // Procedural radial-falloff texture used as the LineRenderer's _MainTex.
    // Mapped across the line's cross-section, this gives the streak feathered,
    // glowing edges instead of a hard rectangular block. Cached statically so
    // we only build it once per game session.
    static Texture2D GetSoftGlowTexture()
    {
        if (s_softGlow != null) return s_softGlow;
        const int size = 64;
        s_softGlow = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
        s_softGlow.wrapMode = TextureWrapMode.Clamp;
        s_softGlow.filterMode = FilterMode.Bilinear;
        s_softGlow.hideFlags = HideFlags.HideAndDontSave;
        var pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - center) / center;
                float dy = (y - center) / center;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                // Smoothstep-style falloff: bright core, soft halo, hard 0 at edge.
                float a = Mathf.Clamp01(1f - r);
                a = a * a * (3f - 2f * a); // smoothstep curve
                byte v = (byte)Mathf.RoundToInt(a * 255f);
                pixels[y * size + x] = new Color32(v, v, v, v);
            }
        }
        s_softGlow.SetPixels32(pixels);
        s_softGlow.Apply(false, true);
        return s_softGlow;
    }

    IEnumerator RecoilRoutine()
    {
        _isRecoiling = true;
        Transform t = _pivot;
        Quaternion rest    = Quaternion.Euler(holdRotationOffset);
        Quaternion kicked  = rest * Quaternion.AngleAxis(-recoilBackAngle, recoilAxis);

        float elapsed = 0f;
        while (elapsed < recoilBackDuration && _pivot != null && t != null)
        {
            t.localRotation = Quaternion.Slerp(rest, kicked, elapsed / recoilBackDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (_pivot != null && t != null) t.localRotation = kicked;

        elapsed = 0f;
        while (elapsed < recoilReturnDuration && _pivot != null && t != null)
        {
            t.localRotation = Quaternion.Slerp(kicked, rest, elapsed / recoilReturnDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (_pivot != null && t != null) t.localRotation = rest;
        _recoilCoroutine = null;
        _isRecoiling = false;
    }

    IEnumerator AnimateRotation(Quaternion from, Quaternion to, float duration, System.Action onComplete)
    {
        Transform t = _pivot;
        yield return AnimateRotationOn(t, from, to, duration, onComplete);
        _equipCoroutine = null;
    }

    IEnumerator AnimateRotationOn(Transform t, Quaternion from, Quaternion to, float duration, System.Action onComplete)
    {
        float elapsed = 0f;
        while (elapsed < duration && t != null)
        {
            t.localRotation = Quaternion.Slerp(from, to, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (t != null) t.localRotation = to;
        onComplete?.Invoke();
    }

    IEnumerator AnimateEquipPose(Vector3 fromPos, Vector3 toPos, Quaternion fromRot, Quaternion toRot, float duration, System.Action onComplete)
    {
        Transform t = _pivot;
        yield return AnimateEquipPoseOn(t, fromPos, toPos, fromRot, toRot, duration, onComplete);
        _equipCoroutine = null;
    }

    IEnumerator AnimateEquipPoseOn(Transform t, Vector3 fromPos, Vector3 toPos, Quaternion fromRot, Quaternion toRot, float duration, System.Action onComplete)
    {
        float elapsed = 0f;
        while (elapsed < duration && t != null)
        {
            float u = elapsed / duration;
            t.localPosition = Vector3.Lerp(fromPos, toPos, u);
            t.localRotation = Quaternion.Slerp(fromRot, toRot, u);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (t != null) { t.localPosition = toPos; t.localRotation = toRot; }
        onComplete?.Invoke();
    }

    public void ForceEquipPistol()
    {
        _pistolUnlocked = true;
        if (_currentPistolInstance == null) EquipPistol();
    }

    public void ForceUnequipPistol()
    {
        if (_currentPistolInstance != null) UnequipPistol();
    }

    public void Unlock()
    {
        _pistolUnlocked = true;
    }

    static Transform FindChildByName(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindChildByName(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    // (Appended at the END per the serialization convention in CLAUDE.md.)
    [Tooltip("Played at the hit point the moment a kill-cam bullet lands (synced with the blood spray). Assign 'wetsquelchy impact'.")]
    public AudioClip killImpactClip;
}
