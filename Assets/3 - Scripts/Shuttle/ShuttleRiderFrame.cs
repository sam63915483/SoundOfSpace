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
            Vector3 seat = shuttleW + shuttleR * seatLocal;
            Quaternion seatRot = shuttleR * seatLocalRot;

            pc.transform.SetParent(null, true);
            pc.transform.SetPositionAndRotation(seat, seatRot);
            rb.isKinematic = false;
            rb.interpolation = s_playerInterpolation;
            rb.position = seat;
            rb.rotation = seatRot;
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
            RiderReleaseBleed.Begin(pc, body);
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

// Post-release handover guard (rewritten, playtest 17 — the one-shot
// measure-and-decay version demonstrably didn't kill the hitch). Two jobs,
// both measurement-based so they are no-ops when nothing is wrong:
//
//  1. RENDER-LAG EQUALIZER. Every interpolated rigidbody is drawn at
//     (transform − rb.position) behind its physics pose. While the player
//     and the planet lag in step, their difference is ~zero; right after
//     the release teleport the player's interpolation has no history and
//     any imbalance is exactly the visible pop. Each frame the imbalance is
//     recomputed FRESH from the live transforms and added to the player's
//     RENDER transform only — next frame's interpolation rewrites the
//     transform, so nothing accumulates, and physics never sees it
//     (autoSyncTransforms is off; rb pose untouched).
//
//  2. RELEASE PROBE (editor / cheats only). Records every frame in the 1 s
//     window — real frame time and the measured lag imbalance — and prints
//     one summary log. If a hitch survives this round, the log names the
//     guilty frame instead of another theory.
//
// Lives in this file so no new .meta is needed (AddComponent-only class).
[DefaultExecutionOrder(60)]   // LateUpdate after ShuttleRenderSmoother (50), before CameraTransformFX (100)
public class RiderReleaseBleed : MonoBehaviour
{
    const float WindowSeconds = 1f;

    PlayerController _pc;
    CelestialBody _body;
    float _t0;
    float _lastRealtime;
    float _worstDt, _worstOffset;
    int _frames;
    static readonly System.Text.StringBuilder s_log = new System.Text.StringBuilder(2048);

    public static void Begin(PlayerController pc, CelestialBody body)
    {
        if (pc == null) return;
        var b = pc.GetComponent<RiderReleaseBleed>();
        if (b == null) b = pc.gameObject.AddComponent<RiderReleaseBleed>();
        b._pc = pc;
        b._body = body;
        b._t0 = Time.time;
        b._lastRealtime = Time.realtimeSinceStartup;
        b._worstDt = 0f;
        b._worstOffset = 0f;
        b._frames = 0;
        s_log.Clear();
        b.enabled = true;
    }

    void LateUpdate()
    {
        if (_pc == null || _pc.Rigidbody == null) { enabled = false; return; }
        float since = Time.time - _t0;
        float dtMs = (Time.realtimeSinceStartup - _lastRealtime) * 1000f;
        _lastRealtime = Time.realtimeSinceStartup;

        Vector3 offset = Vector3.zero;
        if (_body != null && !PlayerController.RiderMode)
        {
            Vector3 planetLag = _body.transform.position - _body.Position;
            Vector3 playerLag = _pc.transform.position - _pc.Rigidbody.position;
            offset = planetLag - playerLag;
            // Fade authority near the window's end; reject rebase-sized jumps.
            float w = 1f - Mathf.SmoothStep(0.7f, 1f, since / WindowSeconds);
            if (offset.sqrMagnitude < 9f)
                _pc.transform.position += offset * w;
        }

        bool probe = Application.isEditor || Universe.cheatsEnabled;
        if (probe)
        {
            _frames++;
            float offM = offset.magnitude;
            if (dtMs > _worstDt) _worstDt = dtMs;
            if (offM > _worstOffset) _worstOffset = offM;
            if (dtMs > 25f || offM > 0.02f)
                s_log.AppendLine("  t+" + (since * 1000f).ToString("0") + "ms  frame "
                    + dtMs.ToString("0.0") + "ms  lagImbalance " + (offM * 100f).ToString("0.0") + "cm");
        }

        if (since >= WindowSeconds)
        {
            if (probe)
                Debug.Log("[ReleaseProbe] " + _frames + " frames / " + WindowSeconds + "s — worst frame "
                    + _worstDt.ToString("0.0") + "ms, worst lag imbalance "
                    + (_worstOffset * 100f).ToString("0.0") + "cm\n"
                    + (s_log.Length > 0 ? s_log.ToString() : "  (no anomalous frames)"));
            enabled = false;
        }
    }
}
