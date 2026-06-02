using System.Collections.Generic;
using UnityEngine;

public class EndlessManager : MonoBehaviour
{
    public float distanceThreshold = 1000;

    private struct PhysicsEntry
    {
        public Transform transform;
        public Rigidbody rigidbody; // cached; null if no Rigidbody on this object
    }

    // Field initializers (not Awake) so the lists survive Unity's domain
    // reload — which fires on every script edit during Play mode. Awake does
    // NOT re-run after a domain reload, so without these initializers the
    // private fields would be null and LateUpdate would NRE every frame.
    // OnEnable's Bootstrap re-registers the scene-default entries instead.
    List<PhysicsEntry> physicsObjects = new List<PhysicsEntry>();
    // Two-stage pipeline: interpolation is restored 2 LateUpdates after the shift so that
    // BOTH prevPhysicsPos and currentPhysicsPos in Unity's interpolation buffer are
    // post-shift values before rendering resumes with interpolation on.
    List<(Rigidbody rb, RigidbodyInterpolation mode)> interpolationRestoreReady = new List<(Rigidbody, RigidbodyInterpolation)>();
    List<(Rigidbody rb, RigidbodyInterpolation mode)> interpolationRestorePending = new List<(Rigidbody, RigidbodyInterpolation)>();

    // During an origin shift we temporarily kinematicize every registered
    // ragdoll bone. Even though bones aren't individually shifted via the
    // entry loop (their wrapper rides the planet's hierarchy), Physics.
    // SyncTransforms at the end of the shift pushes each bone's new
    // hierarchy-derived transform.position into PhysX's rb.position — and
    // PhysX treats that as a 1000m+ teleport. For a non-kinematic body with
    // ContinuousSpeculative collision, that teleport can fire a depenetration
    // impulse the moment its collider overlaps the also-just-shifted terrain
    // mesh, which is what historically sent ragdolls flying through the
    // planet on each shift. Going kinematic for the shift + restoring velocity
    // one frame later lets PhysX settle the new contact pairs cleanly.
    struct BoneKinematicSnapshot
    {
        public Rigidbody rb;
        public Vector3 vel;
        public Vector3 angVel;
        public RigidbodyInterpolation savedInterpolation;
    }
    readonly List<BoneKinematicSnapshot> _bonesRestorePending = new List<BoneKinematicSnapshot>();
    int _restoreOnFrame = -1;

    Rigidbody playerRigidbody;

    public event System.Action PostFloatingOriginUpdate;

    void Awake() { Bootstrap(); }

    // OnEnable runs after every Unity domain reload; Awake does NOT. Both
    // call Bootstrap so the scene-default entries are always registered.
    void OnEnable() { Bootstrap(); }

    void Bootstrap()
    {
        // Field initializers handle the first construction; these guards
        // catch any edge case where a list got nulled (e.g. defensive reset).
        if (physicsObjects == null) physicsObjects = new List<PhysicsEntry>();
        if (interpolationRestoreReady == null) interpolationRestoreReady = new List<(Rigidbody, RigidbodyInterpolation)>();
        if (interpolationRestorePending == null) interpolationRestorePending = new List<(Rigidbody, RigidbodyInterpolation)>();

        // Null-guard every lookup. Ships may be spawned AFTER this runs (e.g.
        // by ShipMarketNPC or save load); they self-register later via
        // RegisterPhysicsObject. An unguarded NRE here would abort Bootstrap
        // before playerRigidbody was assigned, leaving UpdateFloatingOrigin
        // permanently disabled. RegisterPhysicsObject is idempotent (dedup
        // check on transform), so OnEnable-driven re-bootstraps are safe.
        var ship   = FindObjectOfType<Ship>();
        var player = FindObjectOfType<PlayerController>();
        var bodies = FindObjectsOfType<CelestialBody>();

        if (ship != null) RegisterPhysicsObject(ship.transform);
        if (player != null)
        {
            RegisterPhysicsObject(player.transform);
            playerRigidbody = player.GetComponent<Rigidbody>();
        }
        if (bodies != null)
            foreach (var c in bodies)
                RegisterPhysicsObject(c.transform);
    }

    void AddEntry(Transform t)
    {
        physicsObjects.Add(new PhysicsEntry
        {
            transform  = t,
            rigidbody  = t.GetComponent<Rigidbody>()
        });
    }

    void LateUpdate()
    {
        // ── Interpolation restore ──────────────────────────────────────────────
        foreach (var (rb, mode) in interpolationRestoreReady)
        {
            if (rb != null)
                rb.interpolation = mode;
        }
        interpolationRestoreReady.Clear();

        // Promote last frame's pending entries → restored next LateUpdate.
        interpolationRestoreReady.AddRange(interpolationRestorePending);
        interpolationRestorePending.Clear();

        // ── Bone kinematic restore (one full frame after the shift) ───────────
        // FixedUpdate runs BEFORE PhysX simulates each step, so draining in
        // FixedUpdate immediately after the shift would unkinematicize bones
        // before any PhysX step ran with them kinematic at their new
        // positions — defeating the entire point of the temp-kinematic guard.
        // Waiting for the next frame's LateUpdate guarantees one or more full
        // PhysX cycles have executed in between, so contacts and joints
        // re-resolve at the post-shift coords first.
        if (_restoreOnFrame >= 0 && Time.frameCount >= _restoreOnFrame)
        {
            for (int i = 0; i < _bonesRestorePending.Count; i++)
            {
                var snap = _bonesRestorePending[i];
                if (snap.rb == null) continue;
                snap.rb.isKinematic = false;
                snap.rb.velocity = snap.vel;
                snap.rb.angularVelocity = snap.angVel;
            }
            _bonesRestorePending.Clear();
            _restoreOnFrame = -1;
        }

        UpdateFloatingOrigin();

        PostFloatingOriginUpdate?.Invoke();
    }

    void UpdateFloatingOrigin()
    {
        // Lazily acquire the player if it wasn't present at Awake (spawned
        // later, or a save load rebuilt the scene). Runs only until found,
        // then never again — the sanctioned lazy-cache pattern.
        if (playerRigidbody == null)
        {
            var pc = FindObjectOfType<PlayerController>();
            if (pc != null)
            {
                playerRigidbody = pc.GetComponent<Rigidbody>();
                RegisterPhysicsObject(pc.transform);
            }
        }
        if (playerRigidbody == null) return;

        // Use the player's ACTUAL physics position (rb.position) as the reference,
        // NOT the interpolated transform.position. When RigidbodyInterpolation is on,
        // transform.position is the visually-smoothed value, slightly ahead of
        // the real physics position. Using it as the shift offset causes the player and
        // planet (each with their own interpolation offset) to be shifted by different
        // effective amounts — breaking the player-planet relative position and producing
        // the ~6-inch upward pop / depenetration physics correction.
        Vector3 originOffset = playerRigidbody.position;
        if (originOffset.magnitude <= distanceThreshold)
            return;

        Debug.Log($"[EndlessManager] Origin shift firing. " +
            $"playerPhysicsPos={playerRigidbody.position}  " +
            $"magnitude={originOffset.magnitude:F1}  " +
            $"frame={Time.frameCount}  time={Time.time:F3}");

        // Kinematicize every active ragdoll bone for the duration of the
        // shift + SyncTransforms + at least one physics step in the new
        // location. Bones are NOT individually registered — they ride their
        // wrapper/planet hierarchy — but Physics.SyncTransforms at the end
        // of the shift pushes each bone's hierarchy-derived transform into
        // its rb.position, and PhysX sees that as a teleport. Without the
        // kinematic guard, the teleport can fire a depenetration impulse
        // the instant the bone's collider overlaps the post-shift terrain
        // mesh. Restoration is deferred by one frame (see _bonesRestorePending
        // drain above) so PhysX settles the new contact pairs first.
        var bones = RagdollBoneRegistry.Bones;
        for (int i = 0; i < bones.Count; i++)
        {
            var b = bones[i];
            if (b == null) continue;
            if (b.isKinematic) continue;

            _bonesRestorePending.Add(new BoneKinematicSnapshot
            {
                rb = b,
                vel = b.velocity,
                angVel = b.angularVelocity,
                savedInterpolation = b.interpolation,
            });
            b.isKinematic = true;
            if (b.interpolation != RigidbodyInterpolation.None)
            {
                interpolationRestorePending.Add((b, b.interpolation));
                b.interpolation = RigidbodyInterpolation.None;
            }
        }

        int shifted = 0;
        // Pruning destroyed entries is done in place with `i--` so the
        // iteration stays linear AND forward.
        for (int i = 0; i < physicsObjects.Count; i++)
        {
            var entry = physicsObjects[i];
            // Unity's overloaded `==` treats a destroyed Transform as null,
            // but accessing `.position` on it throws MissingReferenceException
            // — which would abort this shift mid-list. Prune in place.
            if (entry.transform == null)
            {
                physicsObjects.RemoveAt(i);
                i--;
                continue;
            }

            Rigidbody rb = entry.rigidbody;

            if (rb != null && rb.interpolation != RigidbodyInterpolation.None)
            {
                interpolationRestorePending.Add((rb, rb.interpolation));
                rb.interpolation = RigidbodyInterpolation.None;
            }

            if (rb != null)
            {
                // Shift the physics body using its actual position, then sync the
                // transform so it matches exactly. This keeps all Rigidbodies in
                // consistent (actual, non-interpolated) coordinates after the shift.
                rb.position -= originOffset;
                entry.transform.position = rb.position;
            }
            else
            {
                entry.transform.position -= originOffset;
            }
            shifted++;
        }

        // CRITICAL: Force PhysX to sync all dirty Transforms immediately.
        //
        // Project has Physics.autoSyncTransforms = false (DynamicsManager.asset),
        // and the planet's terrain MeshCollider lives on a CHILD GameObject of
        // CelestialBody (terrainHolder), not on the body's Rigidbody. Setting
        // rb.position only auto-syncs colliders attached directly to that rb —
        // the child MeshCollider's PhysX position would otherwise lag until the
        // next physics step. During that lag, the player's capsule is at the
        // new shifted position but the terrain mesh is at the OLD world
        // position in PhysX. When the physics step finally runs and syncs the
        // terrain back, the previously-persistent player-ground contact is
        // treated as a FRESH contact and the solver fires a much larger
        // depenetration impulse than the gentle continuous one — visible as a
        // ~half-meter upward pop on the player.
        //
        // This SyncTransforms also pushes every dead-alien hierarchy (alien
        // wrapper + bones, still parented under their planet) into PhysX so
        // bone rb.positions match their new hierarchy-derived world positions
        // — the kinematic guard above keeps that teleport impulse-free.
        Physics.SyncTransforms();

        // Bone restoration is deferred — see LateUpdate's _restoreOnFrame drain.
        if (_bonesRestorePending.Count > 0) _restoreOnFrame = Time.frameCount + 1;
        Debug.Log($"[EndlessManager] Origin shift complete. Shifted {shifted} objects by {originOffset}. {_bonesRestorePending.Count} bones pending kinematic restore.");
    }

    public void RegisterPhysicsObject(Transform t)
    {
        if (t == null) return;
        foreach (var entry in physicsObjects)
        {
            if (entry.transform == t) return;
        }
        Debug.Log($"[EndlessManager] Registered physics object: {t.name}");
        AddEntry(t);
    }

    public void UnregisterPhysicsObject(Transform t)
    {
        int removed = physicsObjects.RemoveAll(e => e.transform == t);
        if (removed > 0)
            Debug.Log($"[EndlessManager] Unregistered physics object: {t?.name}");
    }
}
