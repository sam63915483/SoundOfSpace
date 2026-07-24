using System.Collections;
using UnityEngine;

public class AxeController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Icon shown in the hotbar slot when this item is in the bar. Assign on the Player prefab.")]
    public Sprite hotbarIcon;

    [Header("Axe Settings")]
    public GameObject axePrefab;
    public Transform axeHoldPosition;

    [Header("Hold Offset Adjuster")]
    [Tooltip("Local-space position offset relative to axeHoldPosition. This is where the GRIP / rotation pivot sits in the player's hand.")]
    public Vector3 holdPositionOffset = Vector3.zero;
    [Tooltip("Local-space resting rotation (Euler degrees) relative to axeHoldPosition. The equip / swing animations resolve to this rotation, pivoting around the grip.")]
    public Vector3 holdRotationOffset = Vector3.zero;
    [Tooltip("Local offset of the axe MODEL inside the grip pivot. Slide the axe down (e.g. negative Y) so the bottom of the handle lines up with the pivot — animations will then rotate around the grip rather than the model's centre.")]
    public Vector3 gripOffset = Vector3.zero;

    [Header("Equip Animation")]
    [Tooltip("Seconds to rotate from equipStartAngle into the rest rotation.")]
    public float equipDuration = 0.4f;
    [Tooltip("Starting rotation angle (degrees) before the equip animation lerps into rest.")]
    public float equipStartAngle = -120f;
    [Tooltip("Local axis the equip / unequip rotation pivots around.")]
    public Vector3 equipRotationAxis = Vector3.right;

    [Header("Swing Animation (left click)")]
    [Tooltip("Seconds for the forward chop motion.")]
    public float swingForwardDuration = 0.1f;
    [Tooltip("Seconds for the axe to return to rest after the chop.")]
    public float swingReturnDuration = 0.25f;
    [Tooltip("Forward chop rotation amount (degrees).")]
    public float swingForwardAngle = 60f;
    [Tooltip("Local axis the chop rotates around.")]
    public Vector3 swingAxis = Vector3.right;

    [Header("Mining")]
    [Tooltip("Maximum reach (metres) of the swing — anything within this in front of the camera takes a chop.")]
    public float swingRange = 3.5f;
    [Tooltip("Damage dealt to a tree per swing.")]
    public int damagePerSwing = 1;
    [Tooltip("Cone half-cosine for the fallback hit search. 0.5 ≈ 60° front cone.")]
    public float swingConeDot = 0.5f;

    [Header("Combat")]
    [Tooltip("Damage dealt to an enemy per swing. Enemies have 100 HP by default, so 34 = 3 hits to kill.")]
    public float enemyDamagePerSwing = 34f;
    [Tooltip("Distance (metres) the enemy is knocked back along the player's forward axis.")]
    public float knockbackDistance = 3f;
    [Tooltip("Duration (seconds) of the knockback slide.")]
    public float knockbackDuration = 0.2f;

    [Header("Sound Effects")]
    [SerializeField] AudioClip swingClip;
    [SerializeField, Range(0, 1)] float swingVolume = 0.7f;
    [SerializeField] AudioClip hitClip;
    [SerializeField, Range(0, 1)] float hitVolume = 0.7f;

    // Appended at the end of the serialized block (serialization-order rule).
    [Header("Physics Swing (spike)")]
    [Tooltip("Fallback to the classic click-chop tween + camera-cone hit search. Insurance + future accessibility option.")]
    [SerializeField] bool useClassicSwing = false;
    [Tooltip("Length multiplier on the spawned axe model's LONG axis (the handle) — taller, not fatter. Applied before BladeSweep calibrates, so detection matches the visual.")]
    [SerializeField] float axeScale = 2.55f;   // 1.7 didn't read; Sam asked for another 1.5x on top

    GameObject _currentAxeInstance;
    GameObject _rigRoot;    // AxeMotorRig — top of the equip chain, driven by AxeMotor (carry sway)
    GameObject _swingRoot;  // AxeSwingRig — between motor rig and pivot, driven by AxeSwing
    Transform _pivot;
    bool _axeUnlocked;
    bool _isSwinging;
    Coroutine _equipCoroutine;
    Coroutine _swingCoroutine;
    AudioSource _audioSource;

    // Pivots whose unequip-animation coroutine is still running. EquipAxe
    // drains this list synchronously so a stopped unequip coroutine can't
    // leak its pivot+axe permanently on screen.
    readonly System.Collections.Generic.List<GameObject> _pendingDestroyPivots = new System.Collections.Generic.List<GameObject>();

    FishingRodController _fishingRodController;
    GuitarController _guitarController;
    WaterBottleController _waterBottleController;
    PistolController _pistolController;
    PlayerPickup _playerPickup;
    Ship _ship;

    public bool IsEquipped => _currentAxeInstance != null;
    public bool IsUnlocked => _axeUnlocked;

    // Physics-axe spike accessors.
    public AudioClip HitClip => hitClip;
    public float HitVolume => hitVolume;
    public AudioClip SwingClip => swingClip;
    public float SwingVolume => swingVolume;
    /// True while the mouse-driven swing layer may consume LMB — same guards
    /// the classic path uses (equipped, no equip anim, not piloting, tutorial
    /// gate passed, no modal slot UI open).
    public bool PhysicsSwingAllowed =>
        !useClassicSwing
        && _currentAxeInstance != null
        && _equipCoroutine == null
        && !_isSwinging
        && (_ship == null || !_ship.IsPiloted)
        && TutorialGate.IsUnlocked(TutorialAbility.ChopAxe)
        && !PlayerController.isInModalSlotUI;

    void Start()
    {
        _fishingRodController  = GetComponent<FishingRodController>();
        _guitarController      = GetComponent<GuitarController>();
        _waterBottleController = GetComponent<WaterBottleController>();
        _pistolController      = GetComponent<PistolController>();
        _playerPickup          = GetComponent<PlayerPickup>();
        _ship                  = FindObjectOfType<Ship>();

        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;

        // Physics-axe spike M1: reactive carry layer. Auto-added so no editor
        // setup is needed; tune its inspector values in Play mode, then bake
        // keepers back into AxeMotor's defaults.
        if (GetComponent<AxeMotor>() == null) gameObject.AddComponent<AxeMotor>();
    }

    void Update()
    {
        if (_ship != null && _ship.IsPiloted) return;
        if (_currentAxeInstance == null) return;

        // Live-apply the three offset fields so the inspector values can be tuned
        // in Play mode without re-equipping. Skip while the equip animation is
        // running (it drives the pivot itself) and skip rotation during the swing
        // so SwingRoutine isn't overwritten mid-frame.
        if (_equipCoroutine == null && _pivot != null)
        {
            _pivot.localPosition = holdPositionOffset;
            _currentAxeInstance.transform.localPosition = gripOffset;
            if (!_isSwinging) _pivot.localRotation = Quaternion.Euler(holdRotationOffset);
        }

        if (_isSwinging || _equipCoroutine != null) return;
        // Classic path only: LMB or right-trigger pull (controller), gated on
        // TutorialAbility.ChopAxe. The physics swing (default) is driven by
        // AxeSwing reading LMB-held directly — see PhysicsSwingAllowed.
        if (useClassicSwing && TutorialGate.FirePressed() && TutorialGate.IsUnlocked(TutorialAbility.ChopAxe)) TriggerSwing();
    }

    // Which of the model root's local axes spans the longest — the handle
    // direction. Measured from mesh bounds corners in root-local space, so it
    // is pose-independent and survives the model's odd authoring orientation.
    static int LongestLocalAxis(Transform root)
    {
        var filters = root.GetComponentsInChildren<MeshFilter>();
        bool any = false;
        Bounds bounds = default;
        foreach (var f in filters)
        {
            if (f.sharedMesh == null) continue;
            Bounds mb = f.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 local = root.InverseTransformPoint(f.transform.TransformPoint(corner));
                if (!any) { bounds = new Bounds(local, Vector3.zero); any = true; }
                else bounds.Encapsulate(local);
            }
        }
        if (!any) return 1;   // no meshes: assume Y
        Vector3 e = bounds.extents;
        return (e.x >= e.y && e.x >= e.z) ? 0 : (e.y >= e.z ? 1 : 2);
    }

    void EquipAxe()
    {
        if (axePrefab == null || axeHoldPosition == null) return;
        if (_fishingRodController  != null && _fishingRodController.IsEquipped) return;
        if (_waterBottleController != null && _waterBottleController.IsEquipped) return;
        if (_guitarController      != null && _guitarController.IsEquipped) return;
        if (_pistolController      != null && _pistolController.IsEquipped) return;
        if (_playerPickup          != null && _playerPickup.IsHoldingObject) return;

        if (_equipCoroutine != null) StopCoroutine(_equipCoroutine);
        if (_swingCoroutine != null) { StopCoroutine(_swingCoroutine); _swingCoroutine = null; _isSwinging = false; }

        for (int i = 0; i < _pendingDestroyPivots.Count; i++)
            if (_pendingDestroyPivots[i] != null) Destroy(_pendingDestroyPivots[i]);
        _pendingDestroyPivots.Clear();

        // AxeMotorRig sits between axeHoldPosition and the pivot: AxeMotor
        // sways the rig while the equip/swing tweens keep driving the pivot.
        var rigGo = new GameObject("AxeMotorRig");
        rigGo.transform.SetParent(axeHoldPosition, false);
        _rigRoot = rigGo;

        // AxeSwingRig between the motor rig and the pivot: the mouse-driven
        // swing layer (AxeSwing) drives it; carry sway and the equip tween
        // stay on their own transforms.
        var swingGo = new GameObject("AxeSwingRig");
        swingGo.transform.SetParent(rigGo.transform, false);

        var pivotGo = new GameObject("AxePivot");
        pivotGo.transform.SetParent(swingGo.transform, false);
        _pivot = pivotGo.transform;

        // GetComponent-or-add here too: a save-load ForceEquipAxe can run
        // before Start() has had a chance to add the motor.
        var motor = GetComponent<AxeMotor>();
        if (motor == null) motor = gameObject.AddComponent<AxeMotor>();
        motor.Attach(rigGo.transform, holdPositionOffset);

        _currentAxeInstance = Instantiate(axePrefab, _pivot);
        _currentAxeInstance.transform.localPosition = gripOffset;
        _currentAxeInstance.transform.localRotation = Quaternion.identity;
        // Taller only, not uniformly bigger: stretch the model's LONG axis
        // (the handle) by axeScale. The model's authoring axes are nonstandard,
        // so the long axis is measured from its mesh bounds instead of assumed.
        {
            int longAxis = LongestLocalAxis(_currentAxeInstance.transform);
            Vector3 ls = _currentAxeInstance.transform.localScale;
            ls[longAxis] *= axeScale;
            _currentAxeInstance.transform.localScale = ls;
        }

        var rb = _currentAxeInstance.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        foreach (var col in _currentAxeInstance.GetComponentsInChildren<Collider>()) col.enabled = false;

        var sweep = GetComponent<BladeSweep>();
        if (sweep == null) sweep = gameObject.AddComponent<BladeSweep>();
        sweep.Attach(_currentAxeInstance.transform, this);

        var swing = GetComponent<AxeSwing>();
        if (swing == null) swing = gameObject.AddComponent<AxeSwing>();
        swing.Attach(swingGo.transform, this, sweep);
        _swingRoot = swingGo;

        Quaternion rest    = Quaternion.Euler(holdRotationOffset);
        Quaternion startRot = rest * Quaternion.AngleAxis(equipStartAngle, equipRotationAxis);
        _pivot.localPosition = holdPositionOffset;
        _pivot.localRotation = startRot;

        _equipCoroutine = StartCoroutine(AnimateRotation(startRot, rest, equipDuration, null));
    }

    void UnequipAxe()
    {
        if (_currentAxeInstance == null || _pivot == null) return;
        if (_swingCoroutine != null) { StopCoroutine(_swingCoroutine); _swingCoroutine = null; _isSwinging = false; }
        if (_equipCoroutine != null) StopCoroutine(_equipCoroutine);

        Quaternion startRot = _pivot.localRotation;
        Quaternion endRot   = startRot * Quaternion.AngleAxis(180f, equipRotationAxis);
        var pivot = _pivot;
        // Destroy the rig root (pivot's parent) so the AxeMotorRig can't leak.
        GameObject rigGo = _rigRoot != null ? _rigRoot : pivot.gameObject;
        _pendingDestroyPivots.Add(rigGo);
        var motor = GetComponent<AxeMotor>();
        if (motor != null && _rigRoot != null) motor.Detach(_rigRoot.transform);
        var swing = GetComponent<AxeSwing>();
        if (swing != null && _swingRoot != null) swing.Detach(_swingRoot.transform);
        var sweep = GetComponent<BladeSweep>();
        if (sweep != null && _currentAxeInstance != null) sweep.Detach(_currentAxeInstance.transform);
        _currentAxeInstance = null;
        _pivot = null;
        _rigRoot = null;
        _swingRoot = null;
        _equipCoroutine = StartCoroutine(AnimateRotationOn(pivot, startRot, endRot, equipDuration, () =>
        {
            if (rigGo != null) Destroy(rigGo);
            _pendingDestroyPivots.Remove(rigGo);
            _equipCoroutine = null;
        }));
    }

    void TriggerSwing()
    {
        if (_swingCoroutine != null) StopCoroutine(_swingCoroutine);
        if (swingClip != null && _audioSource != null) _audioSource.PlayOneShot(swingClip, swingVolume);
        DetectSwingHit();
        _swingCoroutine = StartCoroutine(SwingRoutine());
    }

    void DetectSwingHit()
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 origin = cam.transform.position;
        Vector3 forward = cam.transform.forward;

        if (Physics.Raycast(origin, forward, out RaycastHit hit, swingRange, ~0, QueryTriggerInteraction.Ignore))
        {
            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null) { ApplyHit(damageable, forward); return; }
            var tree = hit.collider.GetComponentInParent<SpawnedTree>();
            if (tree != null) { ApplyHit(tree); return; }
            // Temporary axe-mines-crystal hook. When the pickaxe lands, lift
            // this whole block (plus the cone fallback below and the ApplyHit
            // overload) into PickaxeController and delete from here.
            var crystal = hit.collider.GetComponentInParent<SpawnedCrystal>();
            if (crystal != null) { ApplyHit(crystal); return; }
        }

        SpawnedTree bestTree = null;
        SpawnedCrystal bestCrystal = null;
        IDamageable bestDamageable = null;
        float bestDist = swingRange;

        var enemies = EnemyController.ActiveEnemies;
        for (int i = 0; i < enemies.Count; i++)
        {
            var e = enemies[i];
            if (e == null) continue;
            Vector3 toE = e.transform.position - origin;
            float dist = toE.magnitude;
            if (dist > swingRange || dist < 0.001f) continue;
            float dot = Vector3.Dot(forward, toE / dist);
            if (dot < swingConeDot) continue;
            if (dist < bestDist) { bestDist = dist; bestDamageable = e; bestTree = null; }
        }

        // Spawned alien NPCs are damageable too — iterate AllAliens for the
        // cone fallback the same way enemies are handled.
        var aliens = SpawnedAlienNPC.AllAliens;
        for (int i = 0; i < aliens.Count; i++)
        {
            var a = aliens[i];
            if (a == null) continue;
            var d = a.GetComponent<AlienNPCDamageable>();
            if (d == null) continue;
            Vector3 toA = a.transform.position - origin;
            float dist = toA.magnitude;
            if (dist > swingRange || dist < 0.001f) continue;
            float dot = Vector3.Dot(forward, toA / dist);
            if (dot < swingConeDot) continue;
            if (dist < bestDist) { bestDist = dist; bestDamageable = d; bestTree = null; }
        }

        var trees = SpawnedTree.AllTrees;
        for (int i = 0; i < trees.Count; i++)
        {
            var t = trees[i];
            if (t == null || t.IsDead) continue;
            Vector3 toTree = t.transform.position - origin;
            float dist = toTree.magnitude;
            if (dist > swingRange || dist < 0.001f) continue;
            float dot = Vector3.Dot(forward, toTree / dist);
            if (dot < swingConeDot) continue;
            if (dist < bestDist) { bestDist = dist; bestTree = t; bestDamageable = null; bestCrystal = null; }
        }

        // Temporary axe-mines-crystal cone fallback. Moves to PickaxeController later.
        var crystals = SpawnedCrystal.AllCrystals;
        for (int i = 0; i < crystals.Count; i++)
        {
            var c = crystals[i];
            if (c == null || c.IsDead) continue;
            Vector3 toCrystal = c.transform.position - origin;
            float dist = toCrystal.magnitude;
            if (dist > swingRange || dist < 0.001f) continue;
            float dot = Vector3.Dot(forward, toCrystal / dist);
            if (dot < swingConeDot) continue;
            if (dist < bestDist) { bestDist = dist; bestCrystal = c; bestTree = null; bestDamageable = null; }
        }

        if (bestDamageable != null) ApplyHit(bestDamageable, forward);
        else if (bestTree != null) ApplyHit(bestTree);
        else if (bestCrystal != null) ApplyHit(bestCrystal);
    }

    void ApplyHit(SpawnedTree tree)
    {
        if (tree == null || tree.IsDead) return;
        tree.TakeDamage(damagePerSwing);
        if (hitClip != null && _audioSource != null) _audioSource.PlayOneShot(hitClip, hitVolume);
        GamepadRumble.Pulse(0.6f, 0.35f, 0.15f);
    }

    // Temporary axe-mines-crystal hook. Move to PickaxeController.cs when added.
    void ApplyHit(SpawnedCrystal crystal)
    {
        if (crystal == null || crystal.IsDead) return;
        crystal.TakeDamage(damagePerSwing);
        if (hitClip != null && _audioSource != null) _audioSource.PlayOneShot(hitClip, hitVolume);
        GamepadRumble.Pulse(0.6f, 0.35f, 0.15f);
    }

    void ApplyHit(IDamageable target, Vector3 forward)
    {
        if (target == null) return;
        // Knockback first so the cached direction reaches Die() on the kill swing.
        target.ApplyKnockback(forward, knockbackDistance, knockbackDuration);
        target.TakeDamage(enemyDamagePerSwing);
        if (hitClip != null && _audioSource != null) _audioSource.PlayOneShot(hitClip, hitVolume);
        GamepadRumble.Pulse(0.6f, 0.35f, 0.15f);

        var mgr = CameraEffectsManager.Instance;
        if (mgr != null && mgr.MasterEnabled && mgr.Input != null && mgr.Input.fxEnemyHitMicroShake
            && CameraShake.Instance != null)
        {
            CameraShake.Instance.TriggerShake(0.05f, 0.1f, 4f);
        }
    }

    IEnumerator SwingRoutine()
    {
        _isSwinging = true;
        Transform t = _pivot;
        Quaternion rest    = Quaternion.Euler(holdRotationOffset);
        Quaternion forward = rest * Quaternion.AngleAxis(swingForwardAngle, swingAxis);

        float elapsed = 0f;
        while (elapsed < swingForwardDuration && _pivot != null && t != null)
        {
            t.localRotation = Quaternion.Slerp(rest, forward, elapsed / swingForwardDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (_pivot != null && t != null) t.localRotation = forward;

        elapsed = 0f;
        while (elapsed < swingReturnDuration && _pivot != null && t != null)
        {
            t.localRotation = Quaternion.Slerp(forward, rest, elapsed / swingReturnDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (_pivot != null && t != null) t.localRotation = rest;
        _swingCoroutine = null;
        _isSwinging = false;
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

    public void ForceEquipAxe()
    {
        _axeUnlocked = true;
        if (_currentAxeInstance == null) EquipAxe();
    }

    public void ForceUnequipAxe()
    {
        if (_currentAxeInstance != null) UnequipAxe();
    }

    public void Unlock()
    {
        _axeUnlocked = true;
    }
}
