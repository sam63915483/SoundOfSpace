using System.Collections.Generic;
using UnityEngine;

// Rider capture/release for the autopilot flight (handoff §4b).
//
// The mechanism is the intro's proven one, extended to free walking: the local
// player's rigidbody is frozen kinematic for the flight (which silently
// disables the whole n-body/AddForce pipeline — AddForce on a kinematic body
// is a no-op), the player TRANSFORM is parented under the shuttle so both the
// fixed-step pose and the render-rate smoothing carry them for free, and
// PlayerController runs its self-contained rider-movement block
// (PlayerController.RiderMode) in shuttle space.
//
// The player must be UNREGISTERED from EndlessManager while parented — the
// planet (their new ancestor) is registered, and a registered child of a
// registered parent gets double-shifted on an origin rebase (EndlessManager's
// own doc). PlayerPickup does exactly this for held items.
//
// Dropped physics props inside the cabin are frozen kinematic + parented for
// the flight (handoff v1 call) and thawed with the PlayerPickup drop recipe on
// landing (rb.position sync + SyncTransforms — autoSyncTransforms is off).
public static class ShuttleRiderFrame
{
    class FrozenItem
    {
        public Rigidbody rb;
        public Transform prevParent;
        public bool wasKinematic;
        public RigidbodyInterpolation interpolation;
    }

    static bool s_riding;
    static PlayerController s_player;
    static RigidbodyInterpolation s_playerInterpolation;
    // Where (shuttle-local) the player stood at capture — the safety-net
    // re-seat spot if they ever slip below the cabin floor mid-flight.
    static Vector3 s_playerCaptureLocal;
    static bool s_hasCaptureLocal;

    public static bool TryGetPlayerCaptureLocal(out Vector3 local)
    {
        local = s_playerCaptureLocal;
        return s_hasCaptureLocal;
    }
    static readonly List<FrozenItem> s_items = new List<FrozenItem>();

    // Interior volume: the ShuttleInteriorVolume trigger box if Sam has run the
    // prefab patch; otherwise a bounds fallback computed from the Interior
    // group's renderers.
    static BoxCollider s_volume;
    static Transform s_volumeShuttle;
    static Bounds s_fallbackLocalBounds;
    static bool s_fallbackComputed;

    public static bool Riding => s_riding;

    // ── Prefetched scene refs (playtest 17) ─────────────────────────────────
    // The capture frame (door close) ran FindObjectsOfType<Rigidbody> plus
    // several FindObjectOfType scans, and the release frame ran two more — a
    // deterministic multi-ms frame spike at exactly the two handover moments
    // the landing hitch was felt. Everything is prefetched at COUNTDOWN
    // (a button-press moment) instead; the finds below are cold fallbacks.
    static PlayerController s_pcCache;
    static EndlessManager s_endlessCache;
    static readonly List<Rigidbody> s_rbCandidates = new List<Rigidbody>();

    public static void Prefetch()
    {
        s_pcCache = Object.FindObjectOfType<PlayerController>();
        s_endlessCache = Object.FindObjectOfType<EndlessManager>();
        s_rbCandidates.Clear();
        foreach (var rb in Object.FindObjectsOfType<Rigidbody>())
        {
            if (rb == null || rb.isKinematic) continue;
            if (rb.GetComponent<CelestialBody>() != null) continue;
            if (rb.GetComponentInParent<Ship>() != null) continue;
            if (rb.GetComponentInParent<PlayerController>() != null) continue;
            s_rbCandidates.Add(rb);
        }
    }

    static PlayerController CachedPc()
        => s_pcCache != null ? s_pcCache : (s_pcCache = Object.FindObjectOfType<PlayerController>());

    static EndlessManager CachedEndless()
        => s_endlessCache != null ? s_endlessCache : (s_endlessCache = Object.FindObjectOfType<EndlessManager>());

    // Statics survive scene loads and the main menu — a run that ended
    // mid-flight would otherwise leak RiderMode into the next run and the
    // player would load with no movement pipeline (the ShuttleExitDoor
    // OpenedAtTime lesson). A ride never survives a scene load, so reset on
    // EVERY load; NewGameReset calls ResetStatics too.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void HookRunReset()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => ResetStatics();
    }

    public static void ResetStatics()
    {
        s_riding = false;
        s_player = null;
        s_items.Clear();
        s_volume = null;
        s_volumeShuttle = null;
        s_fallbackComputed = false;
        s_hasCaptureLocal = false;
        s_pcCache = null;
        s_endlessCache = null;
        s_rbCandidates.Clear();
        PlayerController.RiderMode = false;
        PlayerController.RiderPlatform = null;
        ShuttleAutopilot.ClientDriven = false;
        // Belt + braces for the stuck-up-override class: a shuttle-owned
        // override (the shuttle itself or its blend proxy) must never survive
        // a scene load. The intro re-sets its own after load, so this is safe.
        var over = PlayerController.UpOverrideTransform;
        if (over != null && (over.name == "ShuttleTravelUpBlendProxy" || over.GetComponent<ShuttleAutopilot>() != null))
            PlayerController.UpOverrideTransform = null;
    }

    // ── Occupancy ────────────────────────────────────────────────────────────
    public static bool AnyoneInside(ShuttleAutopilot pilot)
    {
        var pc = CachedPc();
        if (pc != null && IsInside(pilot, pc.transform.position)) return true;

        // Remote players: puppets never have enabled colliders, so a trigger
        // could not see them — test their replicated positions directly.
        var puppets = PlanetRelativeSync.AllPuppets;
        for (int i = 0; i < puppets.Count; i++)
        {
            var p = puppets[i];
            if (p == null || p.IsOwner) continue;   // owner's own puppet mirrors the real player
            if (IsInside(pilot, p.transform.position)) return true;
        }
        return false;
    }

    public static bool IsInside(ShuttleAutopilot pilot, Vector3 worldPos)
    {
        ResolveVolume(pilot);

        // The stasis pod counts as aboard REGARDLESS of the volume — it's its
        // own top-level group outside the Interior box, and a player standing
        // in it at countdown 0 was launching uncaptured (flight-recorder run 2:
        // physically bulldozed along by the moving hull, never parented).
        if (s_pod != null && (worldPos - s_pod.position).sqrMagnitude <= 3.5f * 3.5f)
            return true;

        if (s_volume != null)
        {
            Vector3 local = s_volume.transform.InverseTransformPoint(worldPos) - s_volume.center;
            Vector3 half = s_volume.size * 0.55f;   // 10% slack so a jump mid-count doesn't strand you
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }
        // Fallback: local-space AABB of the cabin groups.
        Vector3 lp = pilot.transform.InverseTransformPoint(worldPos);
        return s_fallbackLocalBounds.Contains(lp);
    }

    static Transform s_pod;

    static void ResolveVolume(ShuttleAutopilot pilot)
    {
        if (s_volumeShuttle == pilot.transform && (s_volume != null || s_fallbackComputed)) return;
        s_volumeShuttle = pilot.transform;
        s_volume = null;
        s_pod = null;
        foreach (var t in pilot.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "ShuttleInteriorVolume" && s_volume == null)
                s_volume = t.GetComponent<BoxCollider>();
            if (t.name == "StasisPod" && s_pod == null)
                s_pod = t;
        }
        if (s_volume != null || s_fallbackComputed) return;

        // Fallback bounds: FULL renderer bounds (corners, not centres — run 2
        // proved centre-only bounds miss real cabin space) of the Interior AND
        // StasisPod groups, in shuttle-local space, grown for wall clearance.
        s_fallbackComputed = true;
        var b = new Bounds(Vector3.zero, Vector3.one * 4f);
        bool first = true;
        foreach (var t in pilot.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "Interior" && t.name != "StasisPod") continue;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                Bounds wb = r.bounds;
                for (int cx = -1; cx <= 1; cx += 2)
                for (int cy = -1; cy <= 1; cy += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                {
                    Vector3 corner = wb.center + Vector3.Scale(wb.extents, new Vector3(cx, cy, cz));
                    Vector3 lp = pilot.transform.InverseTransformPoint(corner);
                    if (first) { b = new Bounds(lp, Vector3.zero); first = false; }
                    else b.Encapsulate(lp);
                }
            }
        }
        b.Expand(1.5f);
        s_fallbackLocalBounds = b;
    }

    // ── Capture (COUNTDOWN 0 → LIFTOFF) ─────────────────────────────────────
    public static void CaptureRiders(ShuttleAutopilot pilot)
    {
        if (s_riding) return;

        var pc = CachedPc();
        if (pc != null && IsInside(pilot, pc.transform.position))
        {
            s_player = pc;
            var rb = pc.Rigidbody;
            s_playerInterpolation = rb.interpolation;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;                     // no n-body gravity in the cabin (intro recipe)
            pc.transform.SetParent(pilot.transform, true);

            var endless = CachedEndless();
            if (endless != null) endless.UnregisterPhysicsObject(pc.transform);

            pilot.BlendRiderUpIn(1.2f);   // ease planet-up -> shuttle-up (no door-close snap)
            PlayerController.RiderMode = true;
            PlayerController.RiderPlatform = pilot.transform;
            FallDamage.Suppressed = true;
            s_playerCaptureLocal = pc.transform.localPosition;
            s_hasCaptureLocal = true;
        }

        CaptureLooseItems(pilot);
        s_riding = true;
    }

    static void CaptureLooseItems(ShuttleAutopilot pilot)
    {
        s_items.Clear();
        // Prefetched at countdown — a rigidbody spawned during the 10 s count
        // is missed (it just gets left on the pad), which beats the guaranteed
        // whole-scene scan spike on the door-close frame.
        var endless = CachedEndless();
        foreach (var rb in s_rbCandidates)
        {
            if (rb == null || rb.isKinematic) continue;
            if (s_player != null && rb == s_player.Rigidbody) continue;
            if (!IsInside(pilot, rb.position)) continue;

            var item = new FrozenItem
            {
                rb = rb,
                prevParent = rb.transform.parent,
                wasKinematic = rb.isKinematic,
                interpolation = rb.interpolation,
            };
            rb.interpolation = RigidbodyInterpolation.None;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.transform.SetParent(pilot.transform, true);
            if (endless != null) endless.UnregisterPhysicsObject(rb.transform);
            s_items.Add(item);
        }
    }

    /// An item dropped INSIDE the cabin mid-flight (PlayerPickup calls this
    /// from DropObject when RiderMode is on): freeze it onto the shuttle so it
    /// doesn't fly out the back the moment the frame moves. Settled onto the
    /// floor with a short local raycast so it doesn't hover where it was let go.
    public static void AdoptDroppedItem(Rigidbody rb)
    {
        if (!s_riding || rb == null) return;
        var pilot = ShuttleAutopilot.Instance;
        if (pilot == null) return;

        var item = new FrozenItem
        {
            rb = rb,
            prevParent = null,   // dropped items normally live at scene root
            wasKinematic = false,
            interpolation = rb.interpolation,
        };
        rb.interpolation = RigidbodyInterpolation.None;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;

        Vector3 up = pilot.transform.up;
        if (Physics.Raycast(rb.transform.position + up * 0.2f, -up, out RaycastHit hit, 4f,
                            ShuttleAutopilot.GroundMask, QueryTriggerInteraction.Ignore))
            rb.transform.position = hit.point + up * 0.15f;

        rb.transform.SetParent(pilot.transform, true);
        var endless = CachedEndless();
        if (endless != null) endless.UnregisterPhysicsObject(rb.transform);
        s_items.Add(item);
    }

    /// Playtest 25 — PRE-PAY the release's one-time physics cost UNDER MOTION.
    /// The probe cornered the felt pop to a single ~24 ms frame ~0.1 s after
    /// release: the freshly-dynamic capsule's FIRST CONTACT with the planet's
    /// 2M-triangle mesh collider (kinematic bodies keep no contact pairs, so
    /// the pair-creation + midphase work all lands in one frame). Moving WHEN
    /// the release fired moved the pop with it — causation proven — so now
    /// the rigidbody goes dynamic at DESCENT START instead: the contact pairs
    /// get built while the whole screen is moving, the rider cage still owns
    /// the pose (RiderFixedTick overwrites position and zeroes the integrator
    /// every step while dynamic), and the actual release then changes nothing
    /// PhysX has to pay for.
    public static void PrewarmPhysicalRelease()
    {
        if (s_player == null) return;
        var rb = s_player.Rigidbody;
        if (rb == null || !rb.isKinematic) return;
        rb.isKinematic = false;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    // ── Release (LANDING → PARKED) ───────────────────────────────────────────
    public static void ReleaseRiders(ShuttleAutopilot pilot)
    {
        // Release on EVIDENCE, not bookkeeping: playtest 6 had s_riding
        // cleared mid-flight by an outside caller, so the early-return here
        // left the player parented+kinematic inside the parked shuttle — and
        // whatever unfroze them later never gave them the planet's orbital
        // velocity (the "landed, then launched into space a second later").
        var evidencePc = s_player != null ? s_player : CachedPc();
        bool playerRiding = evidencePc != null
            && (PlayerController.RiderMode || evidencePc.transform.IsChildOf(pilot.transform));
        if (!s_riding && !playerRiding) return;
        if (s_player == null && playerRiding)
        {
            s_player = evidencePc;
            s_playerInterpolation = RigidbodyInterpolation.Interpolate;   // the player's standard mode
        }
        s_riding = false;

        var body = pilot.CurrentBody;
        Vector3 bodyVel = body != null ? body.velocity : Vector3.zero;
        var endless = CachedEndless();

        if (s_player != null)
        {
            var pc = s_player;
            var rb = pc.Rigidbody;
            PlayerController.RiderMode = false;
            PlayerController.RiderPlatform = null;

            // Seat from AUTHORITATIVE state, not the transform: the player's
            // shuttle-LOCAL pose (exact — locals are never stale) composed
            // with the autopilot's physics-frame shuttle pose. Reading
            // pc.transform.position here grabbed last frame's RENDER pose —
            // harmless on simulated planets, but a co-orbit follower (Icey)
            // teleports ~2.7 m per tick, so the stale seat landed inside the
            // floor/walls and PhysX blasted the player out (playtest 6).
            Vector3 seatLocal = pc.transform.localPosition;
            Quaternion seatLocalRot = pc.transform.localRotation;
            pilot.GetWorldPose(out Vector3 shuttleW, out Quaternion shuttleR);
            Vector3 seatFresh = shuttleW + shuttleR * seatLocal;
            // Playtest 30 (probe log 7 post-mortem): seating AT the rb was
            // wrong — the rb's vintage was one planet-step behind, and
            // trusting it released the player 62 cm in the air ("the pop was
            // terrible"). RiderFixedTick now aligns the rb with the planet's
            // POST-step pose (velocity·dt prediction), which is exactly the
            // vintage this fresh compose reads at order -50 — so seat and rb
            // agree to the millimetre and the write below never fires on a
            // normal release (no teleport, no interpolation reset).
            Vector3 seat = seatFresh;
            Quaternion seatRot = shuttleR * seatLocalRot;

            pc.transform.SetParent(null, true);
            pc.transform.SetPositionAndRotation(seat, seatRot);
            rb.isKinematic = false;
            rb.interpolation = s_playerInterpolation;
            // The rb has tracked the PHYSICS-frame pose for the whole ride
            // (RiderFixedTick converts through the shuttle's render→physics
            // offset — playtest 28), so the seat normally matches to the
            // millimetre and writing it again would only teleport-reset the
            // interpolation for nothing. Seat only a genuinely displaced rb
            // (edge paths: heals, reseats).
            if ((rb.position - seat).sqrMagnitude > 0.0025f)
            {
                rb.position = seat;
                rb.rotation = seatRot;
            }
            rb.angularVelocity = Vector3.zero;
            pc.SetVelocity(bodyVel);               // inherit the new planet's orbit
            // Hand the controller the LANDING planet before physics resumes —
            // its reference body froze at departure for the whole flight, and
            // one stale step poisoned FallDamage (~100 m/s phantom impact),
            // the grip and the twin exception (playtest 13). The load-grace
            // covers the transition frames the same way save-loads use it.
            pc.SetReferenceBodyOnRelease(body);
            FallDamage.LoadGraceUntil = Mathf.Max(FallDamage.LoadGraceUntil, Time.unscaledTime + 2f);
            Physics.SyncTransforms();
            if (endless != null) endless.RegisterPhysicsObject(pc.transform);
            // NO SnapToCurrentPlayer here (playtest 10's release hitch): the
            // intro needed it because its release TELEPORTS the seat; ours
            // seats the player exactly where they already stand, so resetting
            // the camera interpolation buffer only added a visible blip.

            FallDamage.Suppressed = false;
            // The up blend-out normally started at TOUCHDOWN (SetPhase Parked)
            // and has already finished — only start one here if some edge path
            // released with the override still parked on the shuttle itself.
            if (PlayerController.UpOverrideTransform == pilot.transform)
                pilot.BlendRiderUpOut(1.5f);
            RiderReleaseBleed.Mark("release");
            s_player = null;
        }

        foreach (var item in s_items)
        {
            if (item.rb == null) continue;
            // Picked back up mid-flight (PlayerPickup reparents it to the hold
            // position) — the pickup system owns it now; don't thaw it.
            if (!item.rb.transform.IsChildOf(pilot.transform)) continue;
            item.rb.transform.SetParent(item.prevParent, true);
            item.rb.isKinematic = item.wasKinematic;
            item.rb.interpolation = item.interpolation;
            if (!item.rb.isKinematic)
            {
                item.rb.position = item.rb.transform.position;
                item.rb.rotation = item.rb.transform.rotation;
                item.rb.velocity = bodyVel;
                item.rb.angularVelocity = Vector3.zero;
            }
            if (endless != null) endless.RegisterPhysicsObject(item.rb.transform);
        }
        if (s_items.Count > 0) Physics.SyncTransforms();
        s_items.Clear();
    }
}

// Passive landing probe (playtest 19). The playtest-18 "equalizer" WROTE the
// player's transform every frame — but writing the transform of an
// interpolated rigidbody makes Unity treat it as externally moved and restart
// the very interpolation being measured, so the "fix" itself stuttered the
// player for its whole window (Sam: "worse than ever"). Lesson burned in:
// NEVER write per-frame to an interpolated rigidbody's transform.
//
// This component now only OBSERVES. From touchdown until past the release it
// records every rendered frame — real frame time, the camera's world step,
// the planet-vs-player render-lag imbalance — plus event marks (release,
// teardown, threshold restore), and prints ONE summary log 3 s AFTER the
// window closes, so the report cannot hitch the moment it measures.
// Editor / cheats only; inert in normal play.
[DefaultExecutionOrder(300)]   // last in LateUpdate — sees the final camera pose
public class RiderReleaseBleed : MonoBehaviour
{
    struct Sample { public float t, dtMs, camStep, lagCm, corrCm, rotDeg, altDeltaCm, rbAltDeltaCm; public int gc; }
    Quaternion _lastCamRot = Quaternion.identity;
    int _lastGcCount;
    int _gcTotal;
    // Altitude tracking (playtest 27, Sam: "snapped UP 2-3 inches"): the
    // orbital camera motion (~1.3 m/frame) drowns a 6 cm hop, but the
    // player's distance from the planet core is dead steady standing still —
    // a radial snap shows as a crisp altitude step. Tracked for BOTH the
    // rendered transform and the physics body: rb moved too = a physics
    // kick (depenetration/impulse); transform only = a render-side jump.
    float _lastAlt = -1f, _lastRbAlt = -1f;

    PlayerController _pc;
    CelestialBody _body;
    Transform _cam;
    float _t0, _window;
    float _lastRealtime;
    Vector3 _lastCamPos;
    bool _hasLastCam;
    float _reportAt = -1f;
    float _releaseT = -1f;   // window-relative time of the release mark
    readonly List<Sample> _samples = new List<Sample>(512);
    static readonly List<string> s_marks = new List<string>(16);
    static RiderReleaseBleed s_active;

    public static void BeginWindow(PlayerController pc, CelestialBody body, float seconds)
    {
        if (pc == null) return;
        var b = pc.GetComponent<RiderReleaseBleed>();
        if (b == null) b = pc.gameObject.AddComponent<RiderReleaseBleed>();
        b._pc = pc;
        b._body = body;
        b._cam = pc.Camera != null ? pc.Camera.transform : null;
        b._t0 = Time.time;
        b._window = seconds;
        b._lastRealtime = Time.realtimeSinceStartup;
        b._hasLastCam = false;
        b._reportAt = -1f;
        b._releaseT = -1f;
        b._holdArmed = false;
        b._lastGcCount = System.GC.CollectionCount(0);
        b._gcTotal = 0;
        b._lastAlt = -1f;
        b._lastRbAlt = -1f;
        b._samples.Clear();
        s_marks.Clear();
        s_active = b;
        b.enabled = true;
    }

    /// Timestamp a discrete event into the current window (no-op without one).
    public static void Mark(string label)
    {
        if (s_active != null && s_active.enabled && s_active._reportAt < 0f)
        {
            float t = Time.time - s_active._t0;
            s_marks.Add("t+" + (t * 1000f).ToString("0") + "ms " + label);
            if (label == "release")
            {
                s_active._releaseT = t;
                // Planet-relative seat, so orbital motion never counts as walking.
                if (s_active._pc != null && s_active._body != null)
                    s_active._seatPos = s_active._pc.transform.position - s_active._body.transform.position;
                // Camera hold anchor (playtest 31): where the camera sits
                // RELATIVE TO THE PLANET on the last pre-release frame.
                if (s_active._cam != null && s_active._body != null)
                {
                    s_active._heldCamLocal = s_active._body.transform.InverseTransformPoint(s_active._cam.position);
                    s_active._holdArmed = true;
                }
                s_active._walkMarked = 0;
            }
        }
    }

    Vector3 _seatPos;
    int _walkMarked;
    Vector3 _heldCamLocal;
    bool _holdArmed;

    void LateUpdate()
    {
        if (_reportAt > 0f)
        {
            if (Time.time >= _reportAt) { Report(); enabled = false; }
            return;
        }
        float since = Time.time - _t0;
        float dtMs = (Time.realtimeSinceStartup - _lastRealtime) * 1000f;
        _lastRealtime = Time.realtimeSinceStartup;

        // ── THE FIX (playtest 21) — camera-side warmup bridge ────────────────
        // At the release, the player's render source switches from the
        // shuttle hierarchy (drawn in the planet's interpolated frame) to
        // their own rigidbody interpolation — which has NO history for the
        // first frame or two, so Unity draws the player at the RAW physics
        // pose while the ground is still drawn ~v·dt behind it: an apparent
        // forward-and-back step of up to a metre-plus against the cabin,
        // like clockwork, scaling with orbital speed. The player's correct
        // ground-consistent render pose is always rb.position + the ground's
        // own render lag — so for a short window after release, nudge the
        // CAMERA (order 300, after CameraTransformFX re-set it at 100 — so
        // this can never accumulate) by the measured difference. The term is
        // identically zero once interpolation is warm and in sync, touches
        // no rigidbody or player transform (the playtest-18 trap), and runs
        // in every build.
        _lastCorrCm = 0f;
        // ── CAMERA HOLD across the release seam (playtest 31) ───────────────
        // Three probe logs proved the final truth: the ~20-30 cm one-frame
        // render dip at release is IDENTICAL whether the rigidbody seat moves
        // 13 cm or 150 cm — it is the render-path switch itself (parent
        // compose → rigidbody interpolation), and no bookkeeping fix renders
        // the seam frame cleanly. So stop accounting and PIN the view: hold
        // the camera at its pre-release pose RELATIVE TO THE RENDERED PLANET
        // (rock-solid against the cabin by construction) while the handover
        // turbulence plays out underneath, then ease to the live camera.
        // Absolute lerp-to-target — cannot compound (unlike the old additive
        // nudge), touches no rigidbody, no player transform, no interpolation
        // (the pt-18 trap). Over before the door lets the player walk.
        if (_holdArmed && _releaseT >= 0f && _cam != null && _body != null)
        {
            // 1.2 s window (playtest 32): the 0.6 s version held perfectly
            // through the seam (probe log 8: view-vs-cabin stable, corr
            // 0.4→15 cm) but released its accumulated correction over just
            // 0.27 s — a visible glide. Full hold for 0.6 s, then the
            // correction bleeds out over 0.6 s (<25 cm/s — sub-noticeable).
            float u = (since - _releaseT) / 1.2f;
            if (u >= 0f && u < 1f)
            {
                Vector3 held = _body.transform.TransformPoint(_heldCamLocal);
                if ((held - _cam.position).sqrMagnitude < 25f)   // rebase/teleport abort
                {
                    float w = 1f - Mathf.SmoothStep(0.5f, 1f, u);
                    Vector3 before = _cam.position;
                    _cam.position = Vector3.Lerp(_cam.position, held, w);
                    _lastCorrCm = (_cam.position - before).magnitude * 100f;
                }
                else _holdArmed = false;
            }
            else if (u >= 1f) _holdArmed = false;
        }

        bool probe = Application.isEditor || Universe.cheatsEnabled;
        if (probe)
        {
            float camStep = 0f, rotDeg = 0f;
            if (_cam != null)
            {
                if (_hasLastCam)
                {
                    camStep = (_cam.position - _lastCamPos).magnitude;
                    rotDeg = Quaternion.Angle(_lastCamRot, _cam.rotation);
                }
                _lastCamPos = _cam.position;
                _lastCamRot = _cam.rotation;
                _hasLastCam = true;
            }
            float lagCm = 0f;
            if (_body != null && _pc != null && _pc.Rigidbody != null)
            {
                Vector3 planetLag = _body.transform.position - _body.Position;
                Vector3 playerLag = _pc.transform.position - _pc.Rigidbody.position;
                lagCm = (planetLag - playerLag).magnitude * 100f;
            }
            // Walk-out breadcrumbs: does the spike track how far the player
            // has walked from the seat (streamers/cell systems), or the clock?
            if (_releaseT >= 0f && _walkMarked < 3 && _pc != null && _body != null)
            {
                float walked = ((_pc.transform.position - _body.transform.position) - _seatPos).magnitude;
                if (_walkMarked == 0 && walked > 1.5f) { _walkMarked = 1; Mark("walked-1.5m"); }
                else if (_walkMarked == 1 && walked > 5f) { _walkMarked = 2; Mark("walked-5m"); }
                else if (_walkMarked == 2 && walked > 10f) { _walkMarked = 3; Mark("walked-10m"); }
            }

            int gcNow = System.GC.CollectionCount(0);
            int gcD = gcNow - _lastGcCount;
            _lastGcCount = gcNow;
            _gcTotal += gcD;

            float altDelta = 0f, rbAltDelta = 0f;
            if (_body != null && _pc != null && _pc.Rigidbody != null)
            {
                float alt = (_pc.transform.position - _body.transform.position).magnitude;
                float rbAlt = (_pc.Rigidbody.position - _body.Position).magnitude;
                if (_lastAlt >= 0f) altDelta = (alt - _lastAlt) * 100f;
                if (_lastRbAlt >= 0f) rbAltDelta = (rbAlt - _lastRbAlt) * 100f;
                _lastAlt = alt;
                _lastRbAlt = rbAlt;
            }

            if (_samples.Count < 500)
                _samples.Add(new Sample { t = since, dtMs = dtMs, camStep = camStep, lagCm = lagCm, corrCm = _lastCorrCm, rotDeg = rotDeg, altDeltaCm = altDelta, rbAltDeltaCm = rbAltDelta, gc = gcD });
        }

        if (since >= _window) _reportAt = Time.time + 3f;   // report OUTSIDE the sensitive window
    }

    float _lastCorrCm;

    void Report()
    {
        if (_samples.Count < 5) return;
        float sumStep = 0f, sumDt = 0f;
        for (int i = 1; i < _samples.Count; i++) { sumStep += _samples[i].camStep; sumDt += _samples[i].dtMs; }
        float avgPerMs = sumDt > 0.01f ? sumStep / sumDt : 0f;   // camera speed baseline (per real ms)
        int worstCamI = 1, worstDtI = 1;
        float worstCamDev = 0f;
        for (int i = 1; i < _samples.Count; i++)
        {
            float dev = Mathf.Abs(_samples[i].camStep - avgPerMs * _samples[i].dtMs);
            if (dev > worstCamDev) { worstCamDev = dev; worstCamI = i; }
            if (_samples[i].dtMs > _samples[worstDtI].dtMs) worstDtI = i;
        }
        var sb = new System.Text.StringBuilder(2048);
        sb.Append("[ReleaseProbe] ").Append(_samples.Count).Append(" frames / ").Append(_window)
          .Append("s from touchdown.  Events: ")
          .Append(s_marks.Count > 0 ? string.Join(", ", s_marks) : "none").AppendLine();
        sb.Append("worst frame time ").Append(_samples[worstDtI].dtMs.ToString("0.0"))
          .Append("ms at t+").Append((_samples[worstDtI].t * 1000f).ToString("0"))
          .Append("ms · worst camera pop ").Append((worstCamDev * 100f).ToString("0.0"))
          .Append("cm at t+").Append((_samples[worstCamI].t * 1000f).ToString("0"))
          .Append("ms · GC collections in window: ").Append(_gcTotal).AppendLine();
        // The vertical snap has its own dedicated window: biggest single-frame
        // altitude step (radial — immune to the orbital motion's noise).
        int worstAltI = 1;
        for (int i = 2; i < _samples.Count; i++)
            if (Mathf.Abs(_samples[i].altDeltaCm) > Mathf.Abs(_samples[worstAltI].altDeltaCm)) worstAltI = i;
        sb.Append("worst ALTITUDE step ").Append(_samples[worstAltI].altDeltaCm.ToString("+0.0;-0.0"))
          .Append("cm at t+").Append((_samples[worstAltI].t * 1000f).ToString("0")).AppendLine("ms");
        AppendAround(sb, "frames around the ALTITUDE step:", worstAltI, avgPerMs);
        if (Mathf.Abs(worstCamI - worstAltI) > 8)
            AppendAround(sb, "frames around the camera pop:", worstCamI, avgPerMs);
        if (Mathf.Abs(worstDtI - worstCamI) > 8 && Mathf.Abs(worstDtI - worstAltI) > 8)
            AppendAround(sb, "frames around the slow frame:", worstDtI, avgPerMs);
        // ALWAYS show the release moment (playtest 20 lesson: the felt pop
        // hid outside the two "worst" windows and never got printed).
        if (_releaseT >= 0f)
        {
            int relI = 1;
            for (int i = 1; i < _samples.Count; i++)
                if (Mathf.Abs(_samples[i].t - _releaseT) < Mathf.Abs(_samples[relI].t - _releaseT)) relI = i;
            if (Mathf.Abs(relI - worstCamI) > 8 && Mathf.Abs(relI - worstDtI) > 8)
                AppendAround(sb, "frames around the RELEASE:", relI, avgPerMs);
        }
        Debug.Log(sb.ToString());
    }

    void AppendAround(System.Text.StringBuilder sb, string title, int center, float avgPerMs)
    {
        sb.AppendLine(title);
        for (int i = Mathf.Max(1, center - 8); i < Mathf.Min(_samples.Count, center + 9); i++)
        {
            var s = _samples[i];
            sb.Append(i == center ? "> " : "  ")
              .Append("t+").Append((s.t * 1000f).ToString("0000"))
              .Append("ms  dt ").Append(s.dtMs.ToString("00.0"))
              .Append("ms  cam ").Append((s.camStep * 100f).ToString("000.0"))
              .Append("cm (expected ").Append((avgPerMs * s.dtMs * 100f).ToString("000.0"))
              .Append(")  lag ").Append(s.lagCm.ToString("0.0"))
              .Append("cm  corr ").Append(s.corrCm.ToString("0.0"))
              .Append("cm  rot ").Append(s.rotDeg.ToString("0.00"))
              .Append("deg  alt ").Append(s.altDeltaCm.ToString("+0.0;-0.0"))
              .Append("cm  rbAlt ").Append(s.rbAltDeltaCm.ToString("+0.0;-0.0")).Append("cm")
              .AppendLine(s.gc > 0 ? "  <-- GC RAN THIS FRAME" : "");
        }
    }
}
