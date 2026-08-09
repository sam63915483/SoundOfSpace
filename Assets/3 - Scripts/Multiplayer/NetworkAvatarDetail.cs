using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The two things a puppet was missing to read as a person: where they are
/// LOOKING, and what they are HOLDING.
///
/// ── Kept out of PlanetRelativeSync deliberately ──────────────────────────
/// That class owns the planet-local pose and is the most load-bearing, most
/// twice-broken file in the multiplayer stack. This is cosmetic detail on top;
/// it gets its own NetworkBehaviour so a bad tuning value here can never put a
/// player in the wrong place.
///
/// ── Head look ────────────────────────────────────────────────────────────
/// The Astronaut rig is GENERIC, not Humanoid (animationType 2), so
/// Animator.GetBoneTransform(HumanBodyBones.Head) is unavailable — the bone is
/// found by name: Armature/Torso/Chest/Head.
///
/// The pitch is applied in WORLD space about the body's right axis rather than
/// as a local euler. Bone local axes here are arbitrary (the Armature is rotated
/// 270° on X and Head's bind pose is a 10/6/360 euler), so "which local axis is
/// nod?" has no clean answer. Rotating about the ROOT's right axis is correct
/// regardless of how the rig was authored.
///
/// ⚠️ MUST be LateUpdate — the Animator writes the pose every frame and would
/// overwrite anything applied in Update (CLAUDE.md, the NPC bone rule).
///
/// ── Held item ────────────────────────────────────────────────────────────
/// The owner publishes an ItemId plus a variant tag (mushroom species / fish
/// rarity); every other machine builds that visual locally via
/// HeldItemResolver. Nothing but an int and a short string crosses the wire —
/// both machines run the same build, so both already own every prefab, icon and
/// mushroom model. Same trick ApplyRemoteFlashlight uses for the beam colour.
///
/// This covers BOTH families: the five equippables with controllers, and the
/// select-only resources (wood, crystal, dust, saplings, mushrooms, fish, fish
/// bags) which HeldItemViewmodel renders in the holder's hand as sprite slabs
/// and real models.
///
/// ⚠️ Placement is measured, not guessed, and it is anchored to the EYE — not
/// the hand. Hold points in this game are children of the CAMERA, so an item
/// orbits the eye and follows the look direction; Hand.R is at the hip and
/// slightly behind, about a metre from where its owner is really carrying it.
/// Each model is also size-normalised through ViewmodelMotor.NormalizeSize and
/// centred by its own renderer bounds, because prefab scales and pivots both
/// vary wildly and the first-person path hides that by normalising too.
/// </summary>
[RequireComponent(typeof(PlanetRelativeSync))]
public class NetworkAvatarDetail : NetworkBehaviour
{
    // ── tuning ───────────────────────────────────────────────────────────

    /// How much of the owner's actual look-pitch the neck reproduces. A real
    /// neck cannot do the ±80° a first-person camera can, but under-rotating
    /// defeats the point (reading where someone is looking), so this is high.
    const float HeadPitchScale = 0.85f;
    const float HeadPitchClamp = 65f;

    /// Degrees of change before the pitch is re-sent. NetworkVariables send on
    /// change, and an un-quantised camera angle changes every single frame.
    const float PitchQuantum = 2f;

    static readonly string HeadBoneName = "Head";

    // ── network state ────────────────────────────────────────────────────

    readonly NetworkVariable<float> netHeadPitch = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    /// (int)Hotbar.ItemId — 0 is None.
    readonly NetworkVariable<int> netHeldItem = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    /// Distinguishes two stacks that share one ItemId: the mushroom SPECIES, or
    /// the fish RARITY. Empty for everything else. Without it every remote
    /// mushroom would render as whichever species happened to be first.
    readonly NetworkVariable<FixedString32Bytes> netHeldVariant =
        new NetworkVariable<FixedString32Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // ── owner-side caches ────────────────────────────────────────────────

    /// 0 = hip, 1 = fully aimed down the sights. Quantised because the blend
    /// changes every frame during a 0.14s transition.
    readonly NetworkVariable<float> netAimBlend = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    Transform _ownerCam;
    float _nextOwnerScan;

    // ── remote-side caches ───────────────────────────────────────────────

    Transform  _headBone;
    Transform  _holdAnchor;       // view frame, parked on the eye
    Vector3    _camInRoot = HeldItemResolver.DefaultCamInRoot;
    Vector3    _itemInCam = HeldItemResolver.DefaultItemInCam;
    bool       _viewGeometryResolved;
    float      _nextGeometryScan;
    GameObject _heldVisual;
    // Hip and aimed poses for the held pistol, both in anchor (= view) space.
    // Cached at build time so LateUpdate only has to lerp.
    bool       _heldIsPistol;
    Vector3    _hipLocalPos,  _aimLocalPos;
    Quaternion _hipLocalRot,  _aimLocalRot;
    int        _builtItem = -1;   // -1 = nothing built yet
    string     _builtVariant = "";
    bool       _bonesResolved;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            netHeldItem.OnValueChanged += OnHeldItemChanged;
            netHeldVariant.OnValueChanged += OnHeldVariantChanged;
            // Read the spawn snapshot, not just future changes — a player who
            // was already holding an axe when we joined never raises a change
            // event. Same late-join rule as NetworkPlayerIdentity.
            RebuildFromNet();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner)
        {
            netHeldItem.OnValueChanged -= OnHeldItemChanged;
            netHeldVariant.OnValueChanged -= OnHeldVariantChanged;
        }
        DestroyHeldVisual();
    }

    void OnHeldItemChanged(int _, int __) => RebuildFromNet();
    void OnHeldVariantChanged(FixedString32Bytes _, FixedString32Bytes __) => RebuildFromNet();

    void RebuildFromNet() => RebuildHeldVisual(netHeldItem.Value, netHeldVariant.Value.ToString());

    /// <summary>
    /// The barrel of this player's gun and the way it is pointing, both taken
    /// from THIS machine's own copy of them.
    ///
    /// ⚠️ The direction is deliberately NOT sent over the wire. The first
    /// version transmitted the shot direction in the shooter's local space and
    /// rebuilt it against their puppet transform — the tracer came out of the
    /// puppet's FEET pointing the wrong way, because the puppet root is neither
    /// where the gun is nor aligned with where its owner is looking.
    ///
    /// Everything needed is already here and already correct: the hold anchor
    /// sits on the eye and carries the synced look direction, and it is what
    /// positions the visible gun. Deriving the ray from it means the streak
    /// always leaves the barrel the viewer can see, pointing exactly where that
    /// gun is pointing — the two cannot disagree, because they are the same
    /// transform. Only the LENGTH still crosses the wire.
    /// </summary>
    public bool TryGetAimRay(out Vector3 origin, out Vector3 direction)
    {
        EnsureViewGeometry();

        // COMPUTED, NOT READ OFF THE SCENE.
        //
        // This deliberately does not touch _holdAnchor, _heldVisual, renderer
        // bounds or a named muzzle child. All of those are only correct once
        // LateUpdate has driven them, and an RPC arrives during the NETWORK
        // update, which runs first. _holdAnchor is created parented at
        // localPosition zero - the puppet ROOT, i.e. the FEET - so a tracer
        // spawned before its first LateUpdate came out of the ankles. Adding
        // more fallbacks did nothing, because every one of them was derived
        // from that same anchor.
        //
        // The puppet's transform and the synced pitch are enough on their own,
        // and both are already correct by the time any RPC can arrive: the body
        // renders in the right place, so this does too. Same maths
        // DriveHoldAnchor uses, just evaluated on demand.
        Quaternion view = transform.rotation * Quaternion.Euler(netHeadPitch.Value, 0f, 0f);
        Vector3    eye  = transform.TransformPoint(_camInRoot);

        direction = view * Vector3.forward;
        origin    = eye + view * (_itemInCam + Vector3.forward * MuzzleForwardOffset);
        return true;
    }

    /// How far past the gun's CENTRE the barrel tip is. _itemInCam places the
    /// model's centre; a pistol is roughly 0.28 m long normalised, so half of
    /// that plus a little clearance puts the streak at the end of the barrel
    /// rather than inside the slide.
    const float MuzzleForwardOffset = 0.18f;

    /// Matches PistolController.muzzleChildName, which resolves the same child
    /// inside the same prefab for the first-person view.
    const string MuzzleChildName = "Pistol_B_Barrel";

    // ── owner: publish ───────────────────────────────────────────────────

    void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        PublishHeadPitch();
        PublishHeldItem();
        PublishAim();
    }

    /// Mirrors the pistol's own ADS blend rather than a raw "is aiming" bool, so
    /// the remote gun slides up over the same 0.14s the owner sees instead of
    /// snapping to the aimed pose.
    void PublishAim()
    {
        float blend = HeldItemResolver.LocalAimBlend();
        // Quantised, but always landing exactly on 0 and 1 so the gun fully
        // settles at both ends instead of stopping a few percent short.
        bool atEndpoint = (blend <= 0f && netAimBlend.Value > 0f)
                       || (blend >= 1f && netAimBlend.Value < 1f);
        if (atEndpoint || Mathf.Abs(blend - netAimBlend.Value) >= 0.04f)
            netAimBlend.Value = blend;
    }

    void PublishHeadPitch()
    {
        if (_ownerCam == null)
        {
            // Throttled re-find: the rig may not exist yet on the first frames,
            // and FindObjectOfType every frame is banned (CLAUDE.md).
            if (Time.unscaledTime < _nextOwnerScan) return;
            _nextOwnerScan = Time.unscaledTime + 0.5f;
            var pc = FindObjectOfType<PlayerController>();
            if (pc == null) return;
            var cam = pc.GetComponentInChildren<Camera>(true);
            if (cam == null) return;
            _ownerCam = cam.transform;
        }

        // localEulerAngles is 0..360; fold to a signed pitch. PlayerController
        // writes cam.localEulerAngles = right * smoothPitch, so POSITIVE is
        // looking DOWN — which is also the sign convention the remote applies.
        float pitch = _ownerCam.localEulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        pitch = Mathf.Clamp(pitch, -HeadPitchClamp, HeadPitchClamp);

        if (Mathf.Abs(pitch - netHeadPitch.Value) >= PitchQuantum)
            netHeadPitch.Value = pitch;
    }

    void PublishHeldItem()
    {
        int id = (int)HeldItemResolver.LocalHeld(out string variant);
        if (netHeldItem.Value != id) netHeldItem.Value = id;

        // Mushroom species keys are short, but a hand-authored one could
        // overflow the 32-byte buffer and FixedString32Bytes THROWS on overflow,
        // which would take the whole publish down.
        if (variant == null) variant = "";
        while (System.Text.Encoding.UTF8.GetByteCount(variant) > 29 && variant.Length > 0)
            variant = variant.Substring(0, variant.Length - 1);

        if (netHeldVariant.Value.ToString() != variant)
            netHeldVariant.Value = new FixedString32Bytes(variant);
    }

    // ── remote: apply ────────────────────────────────────────────────────

    void LateUpdate()
    {
        if (!IsSpawned || IsOwner) return;

        ResolveBones();

        // The NECK is limited (HeadPitchScale) because a real one is. The HANDS
        // are not — you point a gun exactly where you look. Using the throttled
        // value for both left the barrel aiming ~15% shallow of the shot it
        // fired, so the aim ray and the tracer are driven by the raw pitch.
        float lookPitch = netHeadPitch.Value;
        float pitch     = lookPitch * HeadPitchScale;

        if (_headBone != null && Mathf.Abs(pitch) >= 0.01f)
        {
            // Additive, in world space, about the BODY's right axis — see the
            // class comment for why not a local euler. Applied after the
            // Animator has written this frame's pose, which is the whole reason
            // this is LateUpdate and not Update.
            _headBone.rotation = Quaternion.AngleAxis(pitch, transform.right) * _headBone.rotation;
        }

        DriveHoldAnchor(lookPitch);
        DriveAimPose();
    }

    /// Slides the held pistol between the hip pose and the aimed pose.
    ///
    /// Both poses live in ANCHOR space, and the anchor IS the owner's view
    /// frame — which is the same space PistolController.GetAimPose works in
    /// ("dead ahead of the camera, nudged down so the sights land on the
    /// crosshair"). So the aimed pose is just the ADS offsets read straight off
    /// the controller, with no conversion.
    void DriveAimPose()
    {
        if (_heldVisual == null || !_heldIsPistol) return;
        float t = Mathf.Clamp01(netAimBlend.Value);
        _heldVisual.transform.localPosition = Vector3.Lerp(_hipLocalPos, _aimLocalPos, t);
        _heldVisual.transform.localRotation = Quaternion.Slerp(_hipLocalRot, _aimLocalRot, t);
    }

    /// Parks the hold anchor on the puppet's EYE, oriented to their view.
    ///
    /// ⚠️ NOT the hand. Every hold point in this game is a child of the CAMERA,
    /// so a held item orbits the eye and follows the look direction — which is
    /// exactly what its owner sees and reports: "out in front and to the right,
    /// and it moves with my camera". Hand.R is at the hip and slightly behind
    /// the body, so anchoring there put the item about a metre from where its
    /// owner was actually carrying it. See HeldItemResolver.TryGetViewGeometry.
    ///
    /// Reproducing the camera frame is also what lets the authored
    /// holdRotationOffset values transfer verbatim — they are expressed relative
    /// to a forward-looking holder, which is precisely this frame.
    void DriveHoldAnchor(float pitch)
    {
        if (_holdAnchor == null) return;
        EnsureViewGeometry();
        // Rotates about the eye, so looking up swings the item up — the same
        // arc the owner sees, and it makes remote players visibly aim.
        _holdAnchor.position = transform.TransformPoint(_camInRoot);
        _holdAnchor.rotation = transform.rotation * Quaternion.Euler(pitch, 0f, 0f);
    }

    /// Measured once off this machine's own player rig — both machines run the
    /// same rig, so the numbers are the same for everyone. Retried while the
    /// scene player is still missing (a puppet can spawn first).
    void EnsureViewGeometry()
    {
        if (_viewGeometryResolved) return;
        if (Time.unscaledTime < _nextGeometryScan) return;
        _nextGeometryScan = Time.unscaledTime + 0.5f;
        _viewGeometryResolved =
            HeldItemResolver.TryGetViewGeometry(out _camInRoot, out _itemInCam);
    }

    void ResolveBones()
    {
        if (_bonesResolved) return;
        _bonesResolved = true;   // one attempt; the rig is present from spawn
        _headBone = FindDeep(transform, HeadBoneName);
        if (_headBone == null)
            Debug.LogWarning($"[MP] No '{HeadBoneName}' bone under the player puppet — " +
                             "head look sync is off. Did the Astronaut rig change?");

        // Child of the ROOT, not of the hand — see DriveHoldAnchor.
        var anchorGO = new GameObject("HeldItemAnchor");
        anchorGO.transform.SetParent(transform, false);
        _holdAnchor = anchorGO.transform;
        // Drive it to the eye IMMEDIATELY. Parenting leaves it at localPosition
        // zero, which is the puppet's feet, and the held model is parented to
        // it — so without this the gun spawns at the ankles and only jumps up
        // on the first LateUpdate.
        EnsureViewGeometry();
        DriveHoldAnchor(netHeadPitch.Value);
    }

    /// Breadth-agnostic recursive find by exact name. The rig is small, and this
    /// runs once per puppet.
    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    // ── remote: the held item ────────────────────────────────────────────


    void RebuildHeldVisual(int itemId, string variant)
    {
        if (itemId == _builtItem && variant == _builtVariant) return;
        _builtItem = itemId;
        _builtVariant = variant;

        DestroyHeldVisual();
        var id = (Hotbar.ItemId)itemId;
        if (id == Hotbar.ItemId.None) return;

        ResolveBones();
        Transform anchor = _holdAnchor != null ? _holdAnchor : transform;

        var built = HeldItemResolver.BuildVisual(id, variant);
        if (built == null) return;   // nothing to show for this item

        _heldVisual = built;
        _heldVisual.transform.SetParent(anchor, false);

        // ORDER MATTERS. Rotate, then scale, then centre — the centring step
        // measures world bounds, which both of the others change.
        _heldVisual.transform.localPosition = Vector3.zero;
        _heldVisual.transform.localRotation = Quaternion.Euler(HeldItemResolver.RotationFor(id));

        // ⚠️ Do not skip this. Item prefabs are authored at wildly different
        // scales — the first-person view hides that by normalising every one of
        // them (WaterBottleController calls NormalizeSize directly, AxeController
        // applies axeScale, HeldItemViewmodel scales icons and models to a target
        // edge). A raw Instantiate inherits none of it, which is exactly why the
        // synced water bottle came out enormous.
        ViewmodelMotor.NormalizeSize(_heldVisual, HeldItemResolver.WorldSizeFor(id));

        // Put the model's centre exactly on the measured hold point.
        EnsureViewGeometry();
        CentreOnHoldPoint(_heldVisual.transform, anchor, _itemInCam);

        // Cache the hip pose, and — for the pistol — the aimed one, so
        // DriveAimPose can lerp between them without recomputing bounds.
        //
        // The pivot correction baked in by CentreOnHoldPoint carries over to the
        // aimed pose unchanged, which is only valid because the pistol's
        // holdRotationOffset and adsRotationOffset are BOTH zero — the gun does
        // not rotate as it comes up, so its bounds centre does not move. If a
        // future pistol gains an ADS rotation, recompute the correction there.
        _hipLocalPos = _heldVisual.transform.localPosition;
        _hipLocalRot = _heldVisual.transform.localRotation;
        _heldIsPistol = id == Hotbar.ItemId.Pistol;
        if (_heldIsPistol)
        {
            Vector3 pivotCorrection = _hipLocalPos - _itemInCam;
            _aimLocalPos = HeldItemResolver.AimPointInView() + pivotCorrection;
            _aimLocalRot = Quaternion.Euler(HeldItemResolver.AimRotation());
        }

        // Shadows off, colliders off, physics off — the same treatment the
        // first-person path gives every viewmodel.
        ViewmodelMotor.MakeViewmodel(_heldVisual);

        // It is scenery on someone else's avatar: nothing on it should tick, and
        // nothing looking for a real item should find it.
        foreach (var mb in _heldVisual.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null) mb.enabled = false;
        foreach (var l in _heldVisual.GetComponentsInChildren<Light>(true)) l.enabled = false;

        // Match the puppet's CURRENT visibility. PlanetRelativeSync.SetRemoteVisible
        // re-collects child renderers on each transition so this self-corrects
        // later, but an item equipped while the puppet is hidden would otherwise
        // pop into view on its own until the next transition.
        bool visible = PuppetBodyVisible();
        foreach (var r in _heldVisual.GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
    }

    /// Puts the model's visual CENTRE on `holdPointLocal` within the anchor.
    ///
    /// Two separate corrections, and both are needed:
    ///   • WHERE the item goes — holdPointLocal, the measured eye-relative spot
    ///     every item rests at in first person.
    ///   • WHERE THE MODEL'S ORIGIN IS — arbitrary. Prefab pivots are wherever
    ///     the artist left them; the axe's is off by enough that the
    ///     first-person path carries a (0.5, -0.5, 0) gripOffset to compensate.
    ///     Those compensations live in the camera-rig frame and are also
    ///     invalidated by the size normalisation, so rather than porting a
    ///     second set of magic numbers this measures the model that is actually
    ///     there and cancels its pivot outright.
    static void CentreOnHoldPoint(Transform model, Transform anchor, Vector3 holdPointLocal)
    {
        var rends = model.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) { model.localPosition = holdPointLocal; return; }

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        Vector3 centreLocal = anchor.InverseTransformPoint(b.center);
        model.localPosition += holdPointLocal - centreLocal;
    }

    /// Reads visibility off the puppet's own skinned mesh rather than tracking a
    /// second copy of the state.
    bool PuppetBodyVisible()
    {
        var smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
        return smr != null && smr.enabled;
    }

    void DestroyHeldVisual()
    {
        if (_heldVisual == null) return;
        Destroy(_heldVisual);
        _heldVisual = null;
    }
}
