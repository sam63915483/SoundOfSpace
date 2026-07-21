using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using TMPro;

public class GhostPlacement : MonoBehaviour
{
    public Action onFinished;
    public static event Action<BuildableEntry> OnPlaced;

    // Set by an OnPlaced subscriber (e.g. MainBuildCabinStep) to request that
    // placement mode exits after the current placement instead of letting the
    // player chain another one. Auto-cleared on consume.
    public static bool s_finishAfterNextPlacement;

    // True while a placement ghost is active. The hotbar reads this to suppress
    // mouse-wheel slot cycling during build placement, where the wheel adjusts
    // the ghost's distance. Set in Begin (past the camera early-out), cleared in
    // OnDestroy — Finish() destroys this GameObject when placement ends.
    public static bool IsPlacing { get; private set; }

    // The active placement (if any) — so SaplingPlanter can drive/cancel the
    // sapling ghost as the hotbar selection changes.
    public static GhostPlacement Current { get; private set; }
    public bool IsSaplingPlacement => entry != null && entry.isSapling;

    // The hotbar suppresses wheel-cycling only when the wheel drives the
    // placement (building distance). Sapling placement doesn't use the wheel, so
    // the hotbar keeps cycling — and scrolling off the sapling slot exits it.
    public static bool WheelControlsPlacement => IsPlacing && (Current == null || !Current.IsSaplingPlacement);

    // Sapling ground-snap state. The ghost previews a short distance in front of
    // the player, snapped straight DOWN to the surface (a near-vertical ray is
    // stable, unlike a long grazing forward ray).
    int  _saplingGroundMask;
    bool _saplingHasValidSpot;
    bool _saplingBlocked;                 // spot is too close to another tree/sapling
    bool _saplingBarren;                  // the body under the crosshair is dead rock (no soil) — no planting
    bool _saplingPoseInit;                // first valid frame snaps; later frames smooth
    bool _saplingSnapNext;                // snap (not ease) the next apply
    CelestialBody _saplingBody;           // ghost is parented here so it rides the orbiting planet
    Vector3 _saplingTargetLocalPos;       // committed target, planet-local
    Quaternion _saplingTargetLocalRot;
    float _domeSpinYaw;                    // hold-R yaw (deg) spun onto the dome ghost about its up axis
    // Aim tracked in PLANET-LOCAL space so orbital motion doesn't count as
    // "aiming" — we only re-cast the ground when the player genuinely looks/moves.
    Vector3 _lastAimLocalPos;
    Vector3 _lastAimLocalFwd;
    const float SaplingReach = 14f;       // how far along your look direction the ground is sampled
    const float SaplingSmoothing = 22f;   // ease rate when the target moves (higher = snappier)
    const float SaplingGroundSink = 0.25f;// metres the trunk sinks INTO the ground so it isn't hovering
    const float SaplingMinSpacing = 8.33f; // min metres to any other tree/sapling (10→8.33 = another 1.2× closer, so more fit in a dome)
    static readonly Color C_GhostBlocked = new Color(1.00f, 0.35f, 0.30f, 0.55f); // red = can't place here
    const float SaplingReaimAngle = 0.3f; // degrees of view change (planet-local) before re-casting
    const float SaplingReaimMove  = 0.1f; // metres of player move (planet-local) before re-casting
    const float SaplingDeadzone   = 0.1f; // ignore sub-threshold hit changes on a re-cast
    const float DomeSpinSpeed = 90f;      // deg/sec the dome ghost spins while R (or LT) is held

    BuildableEntry entry;
    BuildMenuUI    menu;
    Transform      cam;

    GameObject ghost;
    float distance;
    bool  rotating;
    bool  rotateLockedDialogue;
    // Frame placement began — the same primary-action press that opens placement
    // (e.g. hotbar sapling planting) must not also place on that first frame.
    int   _beganFrame = -1;

    // Snap-mode state. Toggled with G; only effective when the active entry's
    // category is Floor/Wall/Roof. When actively snapping (target found),
    // ghost position + rotation are locked to align with the nearest existing
    // snappable piece, and RMB-rotation input is suppressed.
    // Static so the toggle persists across placements and across BuildMenuUI
    // sessions — once the player turns it on, it stays on until they tap G again.
    static bool s_snapMode;
    Bounds _ghostLocalBounds;     // axis-aligned bounds in ghost's own local frame
    Material _ghostMat;           // cached so we can tint based on snap state
    int _snapYawSteps;            // R presses while snapping; layered onto snap rotation as 90° steps
    static readonly Color C_GhostFree = new Color(0.30f, 0.90f, 1.00f, 0.45f); // cyan
    static readonly Color C_GhostSnap = new Color(0.40f, 1.00f, 0.45f, 0.55f); // green

    // Top-center "G to toggle snap mode" hint, visible only for snappable
    // categories. Built once on Begin and torn down on Finish.
    GameObject _hintCanvasGo;
    TextMeshProUGUI _hintText;
    GameObject _barrenBannerGo;           // top-center "barren rock" warning during sapling placement
    bool _lastHintSnapping;
    bool _lastHintMode;

    public void Begin(BuildableEntry entry, BuildMenuUI menu)
    {
        this.entry = entry;
        this.menu  = menu;

        var cameraComp = Camera.main;
        if (cameraComp == null) { Debug.LogError("GhostPlacement: no Camera.main"); Cancel(); return; }
        cam = cameraComp.transform;
        IsPlacing = true;
        Current = this;
        _beganFrame = Time.frameCount;

        distance = menu.ghostStartDistance;

        ghost = Instantiate(entry.prefab);
        ghost.name = entry.prefab.name + "_Ghost";

        // Saplings preview at their planted (small) size and snap to the ground,
        // so scale the ghost down and prepare the surface-cast mask.
        if (entry.isSapling || entry.isBubbleDome)
        {
            if (entry.isSapling) ghost.transform.localScale *= SaplingGrowth.DefaultPlantedScale;
            _saplingGroundMask = ~0 & ~SpawnerCubeface.WorldSpawnExcludeMask;
            var pc = FindObjectOfType<PlayerController>();
            if (pc != null) _saplingGroundMask &= ~(1 << pc.gameObject.layer);
            // Parent the ghost to the planet so it moves WITH the orbiting/rotating
            // surface — otherwise smoothing lags a moving world-space target and
            // the ghost sinks through the ground.
            _saplingBody = FindClosestBody(cam.position);
            if (_saplingBody != null) ghost.transform.SetParent(_saplingBody.transform, worldPositionStays: true);
        }

        // Disable any colliders, rigidbodies and behaviours on the ghost so it
        // doesn't physically interact or run gameplay scripts while previewing.
        foreach (var c in ghost.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var rb in ghost.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.detectCollisions = false; }
        foreach (var mb in ghost.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;

        // Apply translucent ghost material to every renderer.
        _ghostMat = MakeGhostMaterial(C_GhostFree);
        foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
        {
            int n = r.sharedMaterials.Length;
            var mats = new Material[n];
            for (int i = 0; i < n; i++) mats[i] = _ghostMat;
            r.materials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // Activate now (in case the prefab was a runtime-built inactive template,
        // e.g. the placeholder dome). Components were disabled above first, so
        // nothing gameplay-relevant runs on the ghost.
        if (!ghost.activeSelf) ghost.SetActive(true);

        _ghostLocalBounds = ComputeLocalBounds(ghost);

        if (IsSnappableCategory(entry.category)) BuildHintUI();

        // Initial pose: free placement at cam-forward × distance. Update() will
        // re-evaluate every frame (including snap mode if the user toggles G).
        if (cam != null) ghost.transform.position = cam.position + cam.forward * distance;
    }

    void Update()
    {
        if (AIChatScreen.IsTypingActive) return;
        if (ghost == null) return;

        // Cancel: Esc OR controller B button. Also the BuildMenuUI toggle key
        // (default N) — placement is a sub-mode of the build menu, so the same
        // key that opens/closes the menu also exits placement.
        if (TutorialGate.CancelPressed()) { Cancel(); return; }
        if (menu != null && Input.GetKeyDown(menu.toggleKey)) { Cancel(); return; }

        // Saplings AND bubble domes use a dedicated ground-snap mode: no free
        // move / rotate / distance / snapping — the ghost sits on the surface
        // where the player looks, oriented to the planet normal. (Saplings add a
        // no-overlap spacing gate; domes don't.)
        if (entry != null && (entry.isSapling || entry.isBubbleDome))
        {
            // Hold R (or LT on a controller) to spin the dome ghost about its up
            // axis — the surface normal — and release when it looks right.
            if (entry.isBubbleDome &&
                (Input.GetKey(KeyCode.R)
                 || (TutorialGate.ControllerEnabled && TutorialGate.LTValue() > TutorialGate.TriggerThreshold)))
                _domeSpinYaw += DomeSpinSpeed * Time.deltaTime;

            UpdateSaplingPose();
            SetBarrenBanner(entry.isSapling && _saplingBarren && _saplingHasValidSpot);
            bool blocked = entry.isSapling && _saplingBlocked;
            if (Time.frameCount > _beganFrame && _saplingHasValidSpot && !blocked
                && TutorialGate.PrimaryActionPressed())
                Place();
            return;
        }

        // G toggles snap mode (only meaningful for Floor/Wall/Roof; ignored for
        // other categories so users don't get a no-op key). Pad: Y — this
        // overlaps the flashlight toggle (also Y on foot), which is harmless
        // enough during placement to not justify a rarer button.
        if ((Input.GetKeyDown(KeyCode.G) || TutorialGate.PadPressed(TutorialGate.PadButton.Y))
            && entry != null && IsSnappableCategory(entry.category))
        {
            s_snapMode = !s_snapMode;
        }

        // Distance: scroll wheel (mouse) + D-pad up/down (controller). The
        // D-pad step is +/-0.1 (matches a scroll-notch) so the existing
        // sensitivity multiplier gives a similar feel.
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (TutorialGate.ControllerEnabled)
        {
            if (TutorialGate.DPadDirectionPressed(0)) scroll += 0.1f; // D-pad up = farther
            if (TutorialGate.DPadDirectionPressed(2)) scroll -= 0.1f; // D-pad down = closer
        }
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            distance = Mathf.Clamp(distance + scroll * menu.scrollSensitivity * 4f,
                                   menu.ghostMinDistance, menu.ghostMaxDistance);
        }

        // Resolve snap target (if any) BEFORE rotation/pose so the rest of
        // Update can branch on whether we're actively snapping.
        Vector3 rawTarget = (cam != null) ? cam.position + cam.forward * distance : ghost.transform.position;
        Transform snapTarget = null;
        BuildableCategory snapTargetCat = BuildableCategory.General;
        if (s_snapMode && entry != null && IsSnappableCategory(entry.category))
        {
            snapTarget = FindNearestSnapTarget(rawTarget, out snapTargetCat);
        }
        bool snapping = snapTarget != null;

        // Tint the ghost so the user can see whether snapping is engaged.
        if (_ghostMat != null)
        {
            var c = snapping ? C_GhostSnap : C_GhostFree;
            if (_ghostMat.color != c) _ghostMat.color = c;
        }

        // D-pad left/right: 15-degree yaw step on the ghost (controller-only;
        // mouse uses the RMB continuous rotation below). Suppressed while
        // snapping — the snap calculation owns rotation.
        if (!snapping && TutorialGate.ControllerEnabled && cam != null)
        {
            if (TutorialGate.DPadDirectionPressed(1))
                ghost.transform.Rotate(cam.forward, -15f, Space.World); // right
            else if (TutorialGate.DPadDirectionPressed(3))
                ghost.transform.Rotate(cam.forward,  15f, Space.World); // left
        }

        // R: 90° step rotation. In free placement, rotates the ghost's transform
        // directly around the gravity-up axis (planet-radial direction). In snap
        // mode, increments a yaw-offset step that ComputeSnap multiplies onto the
        // computed snap rotation — so R lets you spin the snap by 90° increments
        // around the target's local Y axis.
        // Pad: while snapping, D-pad left/right is freed up (the 15° free-
        // rotation above is suppressed), so it drives the 90° snap-yaw steps.
        if (snapping && TutorialGate.ControllerEnabled)
        {
            if (TutorialGate.DPadDirectionPressed(1)) _snapYawSteps = (_snapYawSteps + 1) & 3;
            else if (TutorialGate.DPadDirectionPressed(3)) _snapYawSteps = (_snapYawSteps + 3) & 3;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            if (snapping)
            {
                _snapYawSteps = (_snapYawSteps + 1) & 3; // 0..3
            }
            else
            {
                var body = FindClosestBody(ghost.transform.position);
                Vector3 yawAxis = (body != null)
                    ? (ghost.transform.position - body.Position).normalized
                    : Vector3.up;
                ghost.transform.Rotate(yawAxis, 90f, Space.World);
            }
        }

        // Continuous rotation while either rotate input is held:
        //   • Mouse RMB + mouse delta              (KBM)
        //   • LT held   + right stick              (controller)
        // Both freeze the player's camera (via isInDialogue) so look-axis
        // motion only rotates the ghost, not the player view.
        // Skipped entirely while actively snapping so the ghost stays locked
        // to the snap target's orientation.
        bool mouseHold = !snapping && Input.GetMouseButton(1);
        bool padHold   = !snapping && TutorialGate.ControllerEnabled
                                   && TutorialGate.LTValue() > TutorialGate.TriggerThreshold;
        bool wantRotate = mouseHold || padHold;
        if (wantRotate && !rotating)
        {
            rotating = true;
            rotateLockedDialogue = !PlayerController.isInDialogue;
            if (rotateLockedDialogue) PlayerController.isInDialogue = true;
        }
        else if (!wantRotate && rotating)
        {
            rotating = false;
            if (rotateLockedDialogue) { PlayerController.isInDialogue = false; rotateLockedDialogue = false; }
        }

        if (rotating && cam != null)
        {
            // Rotate relative to the camera's current axes so the controls feel the
            // same regardless of which direction the player is facing.
            float dx = 0f, dy = 0f;
            if (mouseHold)
            {
                dx += Input.GetAxisRaw("Mouse X") * menu.rotationSensitivity;
                dy += Input.GetAxisRaw("Mouse Y") * menu.rotationSensitivity;
            }
            if (padHold)
            {
                // Right stick is a steady -1..+1 reading, so scale by deltaTime
                // to convert to per-frame magnitude comparable to mouse delta.
                // The ×60 brings full-stick rotation feel into the same ballpark
                // as a brisk RMB drag at the default sensitivity.
                float gain = menu.rotationSensitivity * Time.unscaledDeltaTime * 60f;
                dx += TutorialGate.RightStickX() * gain;
                dy += TutorialGate.RightStickY() * gain;
            }
            ghost.transform.Rotate(cam.forward, -dx, Space.World);
            ghost.transform.Rotate(cam.right,    dy, Space.World);
        }

        ApplyGhostPose(rawTarget, snapTarget, snapTargetCat);
        UpdateHintUI(snapping);

        // Place: LMB OR controller A button. Skip the frame placement began so
        // the opening press (hotbar plant) doesn't place instantly.
        if (Time.frameCount > _beganFrame && TutorialGate.PrimaryActionPressed())
        {
            Place();
        }
    }

    void ApplyGhostPose(Vector3 rawTarget, Transform snapTarget, BuildableCategory snapTargetCat)
    {
        if (ghost == null) return;
        if (snapTarget != null)
        {
            var targetLocal = ComputeLocalBounds(snapTarget.gameObject);
            var (pos, rot) = ComputeSnap(snapTarget, snapTargetCat, targetLocal, rawTarget);
            ghost.transform.SetPositionAndRotation(pos, rot);
            return;
        }
        ghost.transform.position = rawTarget;
        // Rotation persists across frames in free mode — managed by RMB rotate.
    }

    // Slide the sapling ghost along the planet surface under the crosshair,
    // always oriented so its up axis points away from the planet centre. When
    // the player looks at the sky (no ground hit) the ghost holds its last spot
    // and placement is blocked (_saplingHasValidSpot=false).
    void UpdateSaplingPose()
    {
        if (ghost == null || cam == null) return;

        // Only re-sample the ground when the aim genuinely changes — measured in
        // the PLANET'S frame so the planet's orbital drift doesn't register as
        // aiming. Standing still ⇒ no raycast ⇒ the ghost stays pinned to its
        // planet-local spot (parented, static like a placed tree) ⇒ no shake.
        Vector3 camLocalPos = _saplingBody != null
            ? _saplingBody.transform.InverseTransformPoint(cam.position) : cam.position;
        Vector3 camLocalFwd = _saplingBody != null
            ? _saplingBody.transform.InverseTransformDirection(cam.forward) : cam.forward;

        bool aimChanged = !_saplingPoseInit
            || Vector3.Angle(camLocalFwd, _lastAimLocalFwd) > SaplingReaimAngle
            || (camLocalPos - _lastAimLocalPos).sqrMagnitude > SaplingReaimMove * SaplingReaimMove;

        if (aimChanged)
        {
            _lastAimLocalPos = camLocalPos;
            _lastAimLocalFwd = camLocalFwd;
            ReaimSapling();
        }

        if (!_saplingPoseInit) return;

        // Apply the committed planet-local pose. Snap the first commit; ease after
        // a re-aim so the ghost glides to the new spot instead of popping. Domes
        // add the hold-R yaw about their up axis on top of the surface alignment.
        Quaternion targetRot = _saplingTargetLocalRot;
        if (entry != null && entry.isBubbleDome)
            targetRot = _saplingTargetLocalRot * Quaternion.AngleAxis(_domeSpinYaw, Vector3.up);

        if (_saplingSnapNext)
        {
            ghost.transform.localPosition = _saplingTargetLocalPos;
            ghost.transform.localRotation = targetRot;
            _saplingSnapNext = false;
        }
        else
        {
            float t = 1f - Mathf.Exp(-SaplingSmoothing * Time.deltaTime);
            ghost.transform.localPosition = Vector3.Lerp(ghost.transform.localPosition, _saplingTargetLocalPos, t);
            ghost.transform.localRotation = Quaternion.Slerp(ghost.transform.localRotation, targetRot, t);
        }

        // Spacing (saplings only) is checked EVERY frame against the ghost's
        // just-applied spot (not only on aim change) — otherwise it goes stale the
        // instant you plant one and lets you stack a second on top. Cheap.
        _saplingBlocked = entry != null && entry.isSapling && _saplingHasValidSpot
            && (_saplingBarren || IsTooCloseToFlora(ghost.transform.position));

        // Red tint when the spot is blocked (too close to another tree/sapling) so
        // the player can see they can't plant here.
        if (_ghostMat != null)
        {
            Color want = (_saplingBlocked || !_saplingHasValidSpot) ? C_GhostBlocked : C_GhostFree;
            if (_ghostMat.color != want) _ghostMat.color = want;
        }
    }

    // Cast once (only called when the aim changed): straight along the look
    // direction to the terrain, so the ghost sits exactly where the crosshair
    // meets the ground — always touching. Committed in planet-local space.
    void ReaimSapling()
    {
        if (Physics.Raycast(cam.position, cam.forward, out RaycastHit hit,
                             SaplingReach, _saplingGroundMask, QueryTriggerInteraction.Ignore))
        {
            var nearBody = _saplingBody != null ? _saplingBody : FindClosestBody(hit.point);
            // Saplings can't root in barren rock (moons / sun) — a dome adds air but
            // never soil, so this blocks planting even inside a dome on a moon.
            _saplingBarren = entry != null && entry.isSapling && nearBody != null
                && TreeSpawner.Instance != null && !TreeSpawner.Instance.CanGrowTreesOn(nearBody);
            Vector3 gUp = nearBody != null ? (hit.point - nearBody.Position).normalized : hit.normal;
            Quaternion worldRot = Quaternion.FromToRotation(Vector3.up, gUp);
            // Saplings sink slightly into the ground so the trunk isn't hovering;
            // domes sit right on the surface.
            float sink = (entry != null && entry.isSapling) ? SaplingGroundSink : 0f;
            Vector3 worldSpot = hit.point - gUp * sink;

            Vector3 candLocalPos = _saplingBody != null
                ? _saplingBody.transform.InverseTransformPoint(worldSpot) : worldSpot;
            Quaternion candLocalRot = _saplingBody != null
                ? Quaternion.Inverse(_saplingBody.transform.rotation) * worldRot : worldRot;

            if (!_saplingPoseInit)
            {
                _saplingTargetLocalPos = candLocalPos;
                _saplingTargetLocalRot = candLocalRot;
                _saplingSnapNext = true;
                _saplingPoseInit = true;
            }
            else if ((candLocalPos - _saplingTargetLocalPos).sqrMagnitude > SaplingDeadzone * SaplingDeadzone)
            {
                _saplingTargetLocalPos = candLocalPos;
                _saplingTargetLocalRot = candLocalRot;
            }
            _saplingHasValidSpot = true;
        }
        else
        {
            _saplingHasValidSpot = false;
            _saplingBlocked = false;
            _saplingBarren = false;
        }
    }

    // True if a mature tree or another sapling stands within SaplingMinSpacing of
    // the spot — so freshly-grown trees never overlap. Covers both seed/planted
    // mature trees (SpawnedTree.AllTrees) and still-growing saplings.
    static bool IsTooCloseToFlora(Vector3 worldSpot)
    {
        float minSq = SaplingMinSpacing * SaplingMinSpacing;
        var trees = SpawnedTree.AllTrees;
        for (int i = 0; i < trees.Count; i++)
        {
            var t = trees[i];
            if (t != null && !t.IsDead && (t.transform.position - worldSpot).sqrMagnitude < minSq) return true;
        }
        var saps = SaplingGrowth.AllSaplings;
        for (int i = 0; i < saps.Count; i++)
        {
            var s = saps[i];
            if (s != null && (s.transform.position - worldSpot).sqrMagnitude < minSq) return true;
        }
        return false;
    }

    // Cancel from outside (SaplingPlanter, when the player deselects the sapling
    // slot or runs out). Safe no-op if already finishing.
    public void CancelPlacement() => Cancel();

    static bool IsSnappableCategory(BuildableCategory c) =>
        c == BuildableCategory.Floor || c == BuildableCategory.Wall || c == BuildableCategory.Roof;

    Transform FindNearestSnapTarget(Vector3 worldPos, out BuildableCategory targetCategory)
    {
        targetCategory = BuildableCategory.General;
        var body = FindClosestBody(worldPos);
        if (body == null) return null;

        Transform best = null;
        BuildableCategory bestCat = BuildableCategory.General;
        float bestSqr = float.MaxValue;
        const string Suffix = "_Placed";

        for (int i = 0; i < body.transform.childCount; i++)
        {
            var child = body.transform.GetChild(i);
            if (!child.name.EndsWith(Suffix)) continue;
            string prefabName = child.name.Substring(0, child.name.Length - Suffix.Length);
            var be = FindBuildableByPrefabName(prefabName);
            if (be == null || !IsSnappableCategory(be.category)) continue;

            float d = (child.position - worldPos).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = child; bestCat = be.category; }
        }
        targetCategory = bestCat;
        return best;
    }

    BuildableEntry FindBuildableByPrefabName(string prefabName)
    {
        if (menu == null || menu.buildables == null) return null;
        foreach (var be in menu.buildables)
            if (be != null && be.prefab != null && be.prefab.name == prefabName) return be;
        return null;
    }

    // Snap math, executed in the target's local frame.
    //
    // Default rule (Floor→Floor, Wall→Wall, Roof→Roof, etc.):
    //   1. Convert raw target world pos → target-local; subtract target's bounds center
    //      to get a vector pointing from target's centroid to where the user is aiming.
    //   2. Choose the dominant axis of that vector. Floor→Floor / Roof→Roof restrict
    //      to ±X/±Z (lateral); Wall→Wall also allows ±Y so walls can stack vertically.
    //   3. Place the ghost's bounds center one (target half-extent + ghost half-extent)
    //      along that axis from the target's centroid — face-to-face contact.
    //
    // Special rule A (tall-on-flat — Wall→Floor, Wall→Roof, Roof→Floor): the tall
    // piece must SIT ON the slab's edge, not extend beside it. So:
    //   - Vertical: ghost.center.y = slab.top + ghostHalfHeight (bottom touches slab top).
    //   - Snap-axis lateral: ghost.center sits AT the slab edge (centered on the edge line),
    //     so half the ghost is above the slab and half hangs off.
    //   - Other lateral axis: centered on the slab.
    //   - Yaw auto-rotation: detect the ghost's thickness axis (smaller of X/Z) and
    //     rotate 90° around Y so the ghost's thickness aligns with the snap axis and
    //     its length runs along the slab's edge. (Walls have an obvious thin axis;
    //     for roofs the "thin" axis is whichever of X/Z is shorter — this keeps the
    //     roof's slope direction running along the edge.)
    //
    // Special rule B (Floor→Wall, Roof→Wall): the slab goes ON TOP of the wall — used
    // for second stories / ceilings. So:
    //   - Vertical: slab.center.y = wall.top + slabHalfHeight (bottom touches wall top).
    //   - Lateral (along wall's thickness axis): slab's NEAR edge aligns with wall's
    //     midline, slab extends toward whichever side the player is standing on.
    //     Walls placed by Wall→Floor straddle their floor's edge (wall midline = floor
    //     edge), so this puts the second-story slab directly above the original floor
    //     when both pieces are the same size.
    //   - Lateral (along wall's length axis): centered on the wall.
    //
    // Convert local → world via TransformPoint, which applies the target's scale.
    // Assumes target & ghost share scale (true kit-to-kit since fixup applied 1.5×).
    (Vector3 worldPos, Quaternion worldRot) ComputeSnap(Transform target, BuildableCategory targetCat, Bounds targetLocal, Vector3 rawTargetWorld)
    {
        Vector3 rawLocal = target.InverseTransformPoint(rawTargetWorld);
        Vector3 d = rawLocal - targetLocal.center;

        // R-press accumulator, applied as a 90° yaw on top of the snap orientation.
        Quaternion userYaw = Quaternion.Euler(0f, 90f * _snapYawSteps, 0f);

        // "Tall-on-flat" stack: Wall on Floor/Roof, OR Roof on Floor.
        // Roof→Roof stays on the default (lateral edge-to-edge) path so adjacent
        // roof tiles chain along the same slope plane.
        bool wallOnSlab = (entry.category == BuildableCategory.Wall &&
                           (targetCat == BuildableCategory.Floor || targetCat == BuildableCategory.Roof))
                       || (entry.category == BuildableCategory.Roof &&
                           targetCat == BuildableCategory.Floor);
        bool slabOnWall = (entry.category == BuildableCategory.Floor || entry.category == BuildableCategory.Roof) &&
                          targetCat == BuildableCategory.Wall;

        // ─── Slab on top of a wall ────────────────────────────────────────────
        // Floors center their near edge on the wall's midline (so a second story
        // sits directly above the first floor, since walls placed via Wall→Floor
        // straddle the floor edge). Roofs put their near edge at the wall's outer
        // face — no overlap with the wall, so roof eaves don't poke through.
        if (slabOnWall)
        {
            int wallThinAxis = targetLocal.extents.x <= targetLocal.extents.z ? 0 : 2;

            Vector3 camLocal = (cam != null) ? target.InverseTransformPoint(cam.position) : Vector3.zero;
            float camComp        = (wallThinAxis == 0) ? camLocal.x          : camLocal.z;
            float wallCenterComp = (wallThinAxis == 0) ? targetLocal.center.x : targetLocal.center.z;
            float slabSign = (camComp >= wallCenterComp) ? 1f : -1f;

            float ghostHalfOnAxis = (wallThinAxis == 0) ? _ghostLocalBounds.extents.x : _ghostLocalBounds.extents.z;
            float wallHalfOnAxis  = (wallThinAxis == 0) ? targetLocal.extents.x       : targetLocal.extents.z;

            bool isRoof = entry.category == BuildableCategory.Roof;
            float lateralOffset = isRoof
                ? slabSign * (wallHalfOnAxis + ghostHalfOnAxis) // face-to-face, no overlap
                : slabSign * ghostHalfOnAxis;                   // floor edge at wall midline

            Vector3 slabCenterLocal = targetLocal.center;
            slabCenterLocal.y += targetLocal.extents.y + _ghostLocalBounds.extents.y; // bottom on top
            if (wallThinAxis == 0) slabCenterLocal.x += lateralOffset;
            else                   slabCenterLocal.z += lateralOffset;

            Vector3 slabPosLocal = slabCenterLocal - (userYaw * _ghostLocalBounds.center);
            return (target.TransformPoint(slabPosLocal), target.rotation * userYaw);
        }

        // Pick the snap axis. Wall→Wall is the only case Y is allowed (vertical stacking).
        bool allowY = entry.category == BuildableCategory.Wall && targetCat == BuildableCategory.Wall;
        float ax = Mathf.Abs(d.x);
        float ay = allowY ? Mathf.Abs(d.y) : -1f;
        float az = Mathf.Abs(d.z);

        int axis = 0;
        float bestAxisVal = ax;
        if (ay > bestAxisVal) { axis = 1; bestAxisVal = ay; }
        if (az > bestAxisVal) { axis = 2; bestAxisVal = az; }

        float comp = (axis == 0) ? d.x : (axis == 1 ? d.y : d.z);
        float sign = comp >= 0f ? 1f : -1f;

        // ─── Tall piece (wall or roof) on top of a flat slab ─────────────────
        // Walls straddle the slab's edge (wall center on edge — half on slab, half
        // hanging off, like a perimeter wall). Roofs sit beside the slab edge with
        // no overlap (face-to-face) so the roof eaves don't sink into the slab.
        // Both auto-yaw to align the ghost's thin axis with the snap axis.
        if (wallOnSlab)
        {
            int ghostThinAxis = _ghostLocalBounds.extents.x <= _ghostLocalBounds.extents.z ? 0 : 2;
            float yawDeg = (ghostThinAxis == axis) ? 0f : 90f;
            Quaternion autoYaw = Quaternion.Euler(0f, yawDeg, 0f);

            // Effective extents in target-local after the auto-yaw (Y unchanged; X/Z swap on 90°).
            Vector3 ghostExt = _ghostLocalBounds.extents;
            Vector3 effExt = (yawDeg == 0f) ? ghostExt : new Vector3(ghostExt.z, ghostExt.y, ghostExt.x);

            bool isRoof = entry.category == BuildableCategory.Roof;
            float targetHalfOnAxis = (axis == 0) ? targetLocal.extents.x : targetLocal.extents.z;
            float ghostHalfOnAxis  = (axis == 0) ? effExt.x              : effExt.z;
            float lateralOffset = isRoof
                ? sign * (targetHalfOnAxis + ghostHalfOnAxis) // face-to-face, no overlap
                : sign * targetHalfOnAxis;                    // wall straddles edge

            Vector3 ghostCenterLocal = targetLocal.center;
            if (axis == 0) ghostCenterLocal.x += lateralOffset;
            else           ghostCenterLocal.z += lateralOffset;
            ghostCenterLocal.y += targetLocal.extents.y + effExt.y; // bottom on top

            Quaternion combinedYaw = autoYaw * userYaw;
            Vector3 ghostPosLocal = ghostCenterLocal - (combinedYaw * _ghostLocalBounds.center);
            return (target.TransformPoint(ghostPosLocal), target.rotation * combinedYaw);
        }

        // ─── Default: lateral face-to-face on the dominant snap axis ─────────
        float targetHalf = (axis == 0) ? targetLocal.extents.x : (axis == 1 ? targetLocal.extents.y : targetLocal.extents.z);
        float ghostHalf  = (axis == 0) ? _ghostLocalBounds.extents.x : (axis == 1 ? _ghostLocalBounds.extents.y : _ghostLocalBounds.extents.z);

        Vector3 dCenterLocal = targetLocal.center;
        if (axis == 0)      dCenterLocal.x += sign * (targetHalf + ghostHalf);
        else if (axis == 1) dCenterLocal.y += sign * (targetHalf + ghostHalf);
        else                dCenterLocal.z += sign * (targetHalf + ghostHalf);

        Vector3 dPosLocal = dCenterLocal - (userYaw * _ghostLocalBounds.center);
        return (target.TransformPoint(dPosLocal), target.rotation * userYaw);
    }

    // Computes axis-aligned bounds of every MeshFilter under root, expressed in
    // root's own local frame (independent of root's world transform/scale).
    static Bounds ComputeLocalBounds(GameObject root)
    {
        var filters = root.GetComponentsInChildren<MeshFilter>(true);
        bool any = false;
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        Matrix4x4 worldToRoot = root.transform.worldToLocalMatrix;
        foreach (var mf in filters)
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            Vector3 c = mb.center;
            Vector3 e = mb.extents;
            Matrix4x4 mfToRoot = worldToRoot * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    ((i & 1) == 0 ? -e.x : e.x),
                    ((i & 2) == 0 ? -e.y : e.y),
                    ((i & 4) == 0 ? -e.z : e.z));
                Vector3 inRoot = mfToRoot.MultiplyPoint3x4(corner);
                if (!any) { b = new Bounds(inRoot, Vector3.zero); any = true; }
                else b.Encapsulate(inRoot);
            }
        }
        return b;
    }

    void Place()
    {
        if (entry == null || entry.prefab == null) { Cancel(); return; }

        // Barren guard: never plant a sapling on dead rock (moon / sun), even if a
        // caller reaches Place() past the Update gate. Stay in placement mode.
        if (entry.isSapling && _saplingBarren) return;

        // Cost. Saplings spend a Sapling from the hotbar; everything else spends wood.
        if (entry.isSapling)
        {
            if (Hotbar.Instance == null || !Hotbar.Instance.SpendResource(Hotbar.ItemId.Sapling, 1))
            {
                // Out of saplings: stay in placement mode so the player can chop a
                // tree for more and come back. Just skip this click.
                Debug.Log("[GhostPlacement] No saplings to plant; skipped this placement.");
                return;
            }
        }
        else if (entry.woodCost > 0 || entry.crystalCost > 0)
        {
            int wood = WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;
            int crystals = Hotbar.Instance != null ? Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.Crystal) : 0;
            if (wood < entry.woodCost || crystals < entry.crystalCost)
            {
                // Can't afford: stay in placement mode so the player can gather more.
                Debug.Log($"[GhostPlacement] Not enough resources (need {entry.woodCost} wood + {entry.crystalCost} crystals; have {wood} + {crystals}); skipped.");
                return;
            }
            if (entry.woodCost > 0) WoodInventory.Instance.SpendWood(entry.woodCost);
            if (entry.crystalCost > 0) Hotbar.Instance.SpendResource(Hotbar.ItemId.Crystal, entry.crystalCost);
        }

        Vector3 pos = ghost.transform.position;
        Quaternion rot = ghost.transform.rotation;

        var real = Instantiate(entry.prefab, pos, rot);
        if (!real.activeSelf) real.SetActive(true);   // runtime templates ship inactive
        // Saplings are named "_Sapling" (NOT "_Placed") so the generic building
        // save skips them — they persist through their own growth-aware hook.
        real.name = entry.prefab.name + (entry.isSapling ? "_Sapling" : "_Placed");
        OnPlaced?.Invoke(entry);

        // Parent to the closest celestial body so the placement rotates / moves with the
        // planet. Without this, the planet drifts away while the prop stays in world space.
        CelestialBody parentBody = FindClosestBody(pos);
        if (parentBody != null)
            real.transform.SetParent(parentBody.transform, worldPositionStays: true);

        if (entry.isSapling)
        {
            // Same layer as seed trees: keeps the ground-snap ray from landing on
            // an existing sapling, and makes it chop identically once mature.
            SpawnerCubeface.SetLayerRecursively(real, SpawnerCubeface.WorldPropLayer);
            // Growing tree: attach the grower, which scales it down and grows it
            // back up over time based on the ambient O2 at its spot.
            var sg = real.GetComponent<SaplingGrowth>();
            if (sg == null) sg = real.AddComponent<SaplingGrowth>();
            sg.Init(parentBody, 0);
        }
        else if (entry.addBonfireInteractionOnPlace)
        {
            var bf = real.GetComponent<BonfireInteraction>();
            if (bf == null) bf = real.AddComponent<BonfireInteraction>();

            // Prefer the registry (populated by the scene's source bonfire in
            // its Start) — survives source-bonfire destruction. Fall back to
            // scanning the scene for another live bonfire as a safety net.
            if (BonfireUIRegistry.CookPanel != null)
            {
                bf.cookPanel  = BonfireUIRegistry.CookPanel;
                bf.promptText = BonfireUIRegistry.PromptText;
            }
            else
            {
                var template = FindSceneBonfireTemplate(bf);
                if (template != null)
                {
                    bf.cookPanel  = template.cookPanel;
                    bf.promptText = template.promptText;
                }
                else
                {
                    Debug.LogWarning("GhostPlacement: no BonfireUIRegistry entry and no scene bonfire template — placed bonfire won't be cookable.");
                }
            }

            // Ensure there's a trigger collider so OnTriggerEnter fires for the player.
            if (real.GetComponentInChildren<Collider>() == null)
            {
                var sc = real.AddComponent<SphereCollider>();
                sc.isTrigger = true;
                sc.radius = 2f;
            }
            else
            {
                // Make sure at least one collider on the placed bonfire is a trigger.
                bool anyTrigger = false;
                foreach (var c in real.GetComponentsInChildren<Collider>(true))
                    if (c.isTrigger) { anyTrigger = true; break; }
                if (!anyTrigger)
                {
                    var sc = real.AddComponent<SphereCollider>();
                    sc.isTrigger = true;
                    sc.radius = 2f;
                }
            }
        }

        // Stay in placement mode so the player can chain placements of the same item.
        // They exit by pressing N (the build menu toggle) or Esc.

        // One-shot exit: a subscriber to OnPlaced (e.g. MainBuildCabinStep) may
        // set this flag to force placement to end after this single placement.
        if (s_finishAfterNextPlacement)
        {
            s_finishAfterNextPlacement = false;
            Finish();
        }
        // Otherwise keep the ghost ONLY while the player can still afford another.
        // The moment a placement empties them below the cost, end placement so the
        // ghost vanishes instead of dangling as an un-placeable preview.
        else if (!CanAffordAnother(entry))
        {
            Finish();
        }
    }

    // Can the player pay for one more of this entry right now? Mirrors the cost
    // logic in Place() (sapling stack / wood + crystals). Free entries always pass.
    static bool CanAffordAnother(BuildableEntry e)
    {
        if (e == null) return false;
        if (e.isSapling)
            return Hotbar.Instance != null && Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.Sapling) >= 1;
        if (e.woodCost > 0 || e.crystalCost > 0)
        {
            int wood = WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;
            int crystals = Hotbar.Instance != null ? Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.Crystal) : 0;
            return wood >= e.woodCost && crystals >= e.crystalCost;
        }
        return true; // no material cost — never auto-exit
    }

    static BonfireInteraction FindSceneBonfireTemplate(BonfireInteraction self)
    {
        var all = FindObjectsOfType<BonfireInteraction>(true);
        foreach (var b in all)
        {
            if (b == self) continue;
            if (b.cookPanel != null) return b;
        }
        return null;
    }

    static CelestialBody FindClosestBody(Vector3 worldPos)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        CelestialBody closest = null;
        float bestSurfaceDst = float.MaxValue;
        foreach (var b in bodies)
        {
            if (b == null) continue;
            float dst = (b.Position - worldPos).magnitude - b.radius;
            if (dst < bestSurfaceDst) { bestSurfaceDst = dst; closest = b; }
        }
        return closest;
    }

    void Cancel()
    {
        Finish();
    }

    void Finish()
    {
        if (rotateLockedDialogue) { PlayerController.isInDialogue = false; rotateLockedDialogue = false; }
        if (ghost != null) Destroy(ghost);
        if (_hintCanvasGo != null) Destroy(_hintCanvasGo);
        if (_barrenBannerGo != null) Destroy(_barrenBannerGo);
        var cb = onFinished;
        onFinished = null;
        cb?.Invoke();
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        IsPlacing = false;
        if (Current == this) Current = null;
    }

    // Top-center red warning shown while the sapling ghost is over barren rock, so
    // the player understands the whole body (not just this spot) can't be planted.
    void SetBarrenBanner(bool show)
    {
        if (!show)
        {
            if (_barrenBannerGo != null && _barrenBannerGo.activeSelf) _barrenBannerGo.SetActive(false);
            return;
        }
        if (_barrenBannerGo == null) BuildBarrenBanner();
        if (_barrenBannerGo != null && !_barrenBannerGo.activeSelf) _barrenBannerGo.SetActive(true);
    }

    void BuildBarrenBanner()
    {
        var canvasGo = new GameObject("GhostPlacement_Barren", typeof(RectTransform));
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 199; // just under BuildMenuUI's 200
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        _barrenBannerGo = canvasGo;

        var bgGo = new GameObject("Bg", typeof(RectTransform));
        bgGo.transform.SetParent(canvasGo.transform, false);
        var bgRt = bgGo.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0.5f, 1f);
        bgRt.anchorMax = new Vector2(0.5f, 1f);
        bgRt.pivot     = new Vector2(0.5f, 1f);
        bgRt.sizeDelta = new Vector2(620f, 56f);
        bgRt.anchoredPosition = new Vector2(0f, -30f);
        var bgImg = bgGo.AddComponent<Image>();
        bgImg.color = new Color32(46, 12, 12, 220);
        bgImg.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(bgGo.transform, false);
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(16f, 0f);
        trt.offsetMax = new Vector2(-16f, 0f);
        var t = textGo.AddComponent<TextMeshProUGUI>();
        t.fontSize = 22;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.color = new Color32(255, 150, 150, 255);
        t.raycastTarget = false;
        t.text = "Barren rock — nothing will grow here";
    }

    void BuildHintUI()
    {
        var canvasGo = new GameObject("GhostPlacement_Hint", typeof(RectTransform));
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 199; // just under BuildMenuUI's 200
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        _hintCanvasGo = canvasGo;

        var bgGo = new GameObject("Bg", typeof(RectTransform));
        bgGo.transform.SetParent(canvasGo.transform, false);
        var bgRt = bgGo.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0.5f, 1f);
        bgRt.anchorMax = new Vector2(0.5f, 1f);
        bgRt.pivot     = new Vector2(0.5f, 1f);
        bgRt.sizeDelta = new Vector2(560f, 56f);
        bgRt.anchoredPosition = new Vector2(0f, -30f);
        var bgImg = bgGo.AddComponent<Image>();
        bgImg.color = new Color32(10, 15, 28, 220);
        bgImg.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(bgGo.transform, false);
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(16f, 0f);
        trt.offsetMax = new Vector2(-16f, 0f);
        _hintText = textGo.AddComponent<TextMeshProUGUI>();
        _hintText.fontSize = 22;
        _hintText.fontStyle = FontStyles.Bold;
        _hintText.alignment = TextAlignmentOptions.Center;
        _hintText.color = new Color32(220, 230, 255, 255);
        _hintText.raycastTarget = false;
        _lastHintSnapping = false;
        _lastHintMode = false;
        RefreshHintText(false, false);
    }

    void UpdateHintUI(bool snapping)
    {
        if (_hintText == null) return;
        if (s_snapMode != _lastHintMode || snapping != _lastHintSnapping)
        {
            _lastHintMode = s_snapMode;
            _lastHintSnapping = snapping;
            RefreshHintText(s_snapMode, snapping);
        }
    }

    void RefreshHintText(bool mode, bool snapping)
    {
        // Compose with TMP rich-text color tags so the on/off state pops.
        string state;
        if (!mode) state = "<color=#A0A8B8>OFF</color>";
        else if (snapping) state = "<color=#66FF7A>ON</color>";
        else state = "<color=#FFC850>ON (no target nearby)</color>";
        _hintText.text = $"G snap — {state}    R rotate 90°";
    }

    // Public so other scripts (ThrusterMount placement preview) can reuse the
    // same translucent cyan look without duplicating the shader-keyword setup.
    public static Material MakeGhostMaterial() =>
        MakeGhostMaterial(new Color(0.3f, 0.9f, 1f, 0.45f));

    public static Material MakeGhostMaterial(Color color)
    {
        // Try the built-in Standard shader first (works in default Built-in pipeline).
        Shader shader = Shader.Find("Standard");
        Material m;
        if (shader != null)
        {
            m = new Material(shader);
            // Configure for transparent rendering.
            m.SetFloat("_Mode", 3f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
            m.color = color;
            return m;
        }

        // Fallback: simple unlit transparent.
        shader = Shader.Find("Sprites/Default");
        m = new Material(shader);
        m.color = color;
        return m;
    }
}
