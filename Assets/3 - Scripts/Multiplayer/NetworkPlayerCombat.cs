using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Player-versus-player gunfire: replicated tracers, hit detection against
/// other astronauts, and the damage that follows.
///
/// ── Who decides a hit ────────────────────────────────────────────────────
/// THE SHOOTER. They run the hit test locally against the puppets they can
/// actually see, then tell the server "I hit client N", which forwards it to
/// that client to apply to itself.
///
/// The alternative — server-side re-simulation — needs lag compensation
/// (rewinding every player's position to the shooter's timestamp) to avoid
/// making people lead their shots. For a two-friends co-op game that is a lot
/// of machinery to buy something nobody will notice, and it fails in the
/// direction players hate: shots that visibly connect but do nothing. Trusting
/// the shooter is what makes it feel right, and there is no competitive stake
/// here to protect.
///
/// ── Why the hit test is hand-rolled instead of a Physics raycast ─────────
/// Puppet colliders are DISABLED on every machine, deliberately: a solid
/// kinematic capsule swept around by network poses shoves the local player out
/// of any overlap, which is the "host randomly launched into space" bug
/// (NetworkPlayerSetup). Re-enabling them — even on a spare layer — puts that
/// back within reach.
///
/// So the ray is tested analytically against a capsule per remote player. No
/// colliders, no layers, no collision-matrix edits, and nothing that can push
/// anybody anywhere.
///
/// ── Nothing crosses the wire in world space ──────────────────────────────
/// Floating-origin rebases fire while standing still, so a world position is
/// stale on arrival (the four sync rules). The shot direction is therefore sent
/// in the SHOOTER'S LOCAL SPACE and rebuilt against their synced puppet
/// transform on the far side; the start point is taken from the receiver's own
/// copy of that player's muzzle. No coordinate is ever trusted across machines.
/// </summary>
[RequireComponent(typeof(PlanetRelativeSync))]
public class NetworkPlayerCombat : NetworkBehaviour
{
    /// Damage one bullet does to another player. Passed to
    /// ResourceManager.TakeDamage, which applies the global
    /// damageTakenMultiplier (currently 1/1.3) like every other damage source —
    /// so ~11.5 lands per shot and a full-health player takes 9 hits.
    public const float DamagePerHit = 15f;

    /// <summary>
    /// Capsule approximating an astronaut, in their own local space.
    ///
    /// GENEROUS ON PURPOSE, and more so since the first playtest reported shots
    /// that visibly connected doing nothing. Two lags stack up and both push the
    /// target away from where the shooter sees them:
    ///
    ///   • the network delay before a pose even arrives, and
    ///   • PlanetRelativeSync's own exponential smoothing, which at
    ///     remoteLerpSpeed 12 trails a moving player by roughly speed/12 metres
    ///     — about half a metre at a sprint, before latency.
    ///
    /// Against that, the old 0.45 m radius was thinner than the error, so a
    /// perfectly aimed shot at a running player missed more often than it hit.
    /// Widening the capsule is the invisible fix; the alternative is tightening
    /// the smoothing, which is Sam's playtested feel and costs a visible jitter.
    ///
    /// The span is a whole body plus slack, so leg and head shots both land.
    /// </summary>
    const float BodyRadius     = 0.75f;
    const float BodyFootHeight = 0.15f;
    const float BodyHeadHeight = 2.0f;

    /// Live instances, so a shooter can enumerate targets without a scene scan
    /// (the AllInstances convention — CLAUDE.md).
    static readonly List<NetworkPlayerCombat> All = new List<NetworkPlayerCombat>();

    NetworkAvatarDetail _detail;

    public override void OnNetworkSpawn()
    {
        All.Add(this);
        _detail = GetComponent<NetworkAvatarDetail>();

        // Only the machine that owns this puppet listens for local shots —
        // otherwise every puppet in the scene would report the same shot.
        if (IsOwner) PistolController.OnLocalShotFired += OnLocalShot;
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        if (IsOwner) PistolController.OnLocalShotFired -= OnLocalShot;
    }

    // ── shooting ─────────────────────────────────────────────────────────

    void OnLocalShot(PistolController.ShotInfo shot)
    {
        if (!IsSpawned || !IsOwner) return;

        // ONLY the length travels. Direction and origin are rebuilt on each
        // receiver from their own copy of this player's gun - see
        // NetworkAvatarDetail.TryGetAimRay for why sending them was wrong.
        float tracerLen = Mathf.Min(shot.WorldHitDistance, shot.MaxTracerLength);

        FireServerRpc(tracerLen);
        TryHitRemotePlayers(shot);
    }

    [ServerRpc]
    void FireServerRpc(float length)
    {
        // Straight relay. The server does not adjudicate the shot; it only
        // makes sure everyone else gets to see it.
        FireClientRpc(length);
    }

    [ClientRpc]
    void FireClientRpc(float length)
    {
        // The owner already drew their own tracer through the normal fire path.
        if (IsOwner) return;
        SpawnRemoteTracer(length);
    }

    /// Draws a streak down the barrel of this puppet's gun, as drawn on THIS
    /// machine - so it always leaves the muzzle the viewer can see, pointing
    /// where that gun visibly points.
    void SpawnRemoteTracer(float length)
    {
        if (_detail == null || !_detail.TryGetAimRay(out Vector3 start, out Vector3 dir))
            return;   // gun not built yet; no streak beats one from the feet
        // Parented to this puppet so the streak rides floating-origin rebases
        // and the planet's orbit instead of being left behind by them.
        RemoteTracer.Spawn(start, start + dir.normalized * Mathf.Max(0.5f, length), transform);
    }

    // ── hit detection ────────────────────────────────────────────────────

    void TryHitRemotePlayers(PistolController.ShotInfo shot)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return;

        NetworkPlayerCombat best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < All.Count; i++)
        {
            var other = All[i];
            if (other == null || other == this || other.IsOwner) continue;

            if (!other.RayHitsBody(shot.RayOrigin, shot.RayDirection, out float dist)) continue;

            // A wall between us stops the bullet. WorldHitDistance is whatever
            // the real raycast struck (or the weapon's range if nothing) — the
            // puppet has no collider, so it can never be what that raycast hit,
            // which is exactly why this comparison is meaningful.
            if (dist > shot.WorldHitDistance) continue;

            if (dist < bestDist) { bestDist = dist; best = other; }
        }

        if (best == null) return;

        // Tell the SHOOTER immediately that they connected, rather than waiting
        // for a round trip. Without this there was no feedback of any kind on a
        // player hit — you fired at somebody and nothing whatsoever happened on
        // screen, which reads exactly like the shot not registering even when it
        // did.
        best.SpawnHitSplash();

        ReportHitServerRpc(best.OwnerClientId);
    }

    /// <summary>
    /// The blood a PLAYER hit produces: a body-centre SPLASH, deliberately not
    /// the spray.
    ///
    /// SpawnSpray is the bullet-hole fountain — a long-lived, bone-attached
    /// gusher meant for an entry wound on a corpse-to-be. On a living astronaut
    /// who is going to run off and keep fighting it hangs in the air behind them.
    /// SpawnDamageSplash is the short, centred puff the whole damage pipeline
    /// already uses for "that hurt" (EnemyController.TakeDamage, AlienNPCDamageable),
    /// so a player hit now reads the same as every other hit in the game.
    /// </summary>
    void SpawnHitSplash()
    {
        var fx = BloodFX.Instance;
        if (fx == null) return;
        // Chest height, so it reads as hitting a body rather than the ground.
        fx.SpawnDamageSplash(transform.TransformPoint(new Vector3(0f, 1.1f, 0f)), transform);
    }

    /// Analytic ray-vs-capsule against this player's body.
    bool RayHitsBody(Vector3 origin, Vector3 dir, out float distance)
    {
        Vector3 a = transform.TransformPoint(new Vector3(0f, BodyFootHeight, 0f));
        Vector3 b = transform.TransformPoint(new Vector3(0f, BodyHeadHeight, 0f));
        return RayHitsCapsule(origin, dir, a, b, BodyRadius, out distance);
    }

    /// <summary>
    /// Ray vs. capsule (segment a-b with `radius`). Returns the distance along
    /// the ray to where the bullet ENTERS the body, not to the closest approach,
    /// so it can be compared directly against how far the world raycast reached.
    ///
    /// Public and static purely so it can be exercised directly. That paid for
    /// itself immediately: the first version of this used a closest-approach
    /// formulation with a sign error in the segment parameter and MISSED
    /// point-blank chest shots. Nothing on screen would have explained why.
    ///
    /// Standard quadratic form — the ray is solved against the infinite cylinder
    /// first, and falls through to a sphere test on whichever cap it passed if
    /// the hit lands outside the body span. That handles the awkward cases
    /// (shooting straight down onto someone's head, or straight up through them)
    /// that a closest-point approach gets wrong at the ends.
    /// </summary>
    public static bool RayHitsCapsule(Vector3 origin, Vector3 dir,
                                      Vector3 a, Vector3 b, float radius,
                                      out float distance)
    {
        distance = 0f;

        Vector3 d  = dir.normalized;
        Vector3 ba = b - a;
        Vector3 oa = origin - a;

        float baba = Vector3.Dot(ba, ba);
        if (baba < 1e-6f) return RayHitsSphere(origin, d, a, radius, out distance);

        float bard = Vector3.Dot(ba, d);
        float baoa = Vector3.Dot(ba, oa);
        float rdoa = Vector3.Dot(d,  oa);
        float oaoa = Vector3.Dot(oa, oa);

        float A = baba - bard * bard;
        float B = baba * rdoa - baoa * bard;
        float C = baba * oaoa - baoa * baoa - radius * radius * baba;

        // A == 0 means the ray runs parallel to the body axis, so it can only
        // ever enter through a cap. Solved explicitly rather than left to divide
        // by zero and rely on NaN comparisons falling the right way.
        if (A > 1e-6f)
        {
            float h = B * B - A * C;
            if (h >= 0f)
            {
                float t = (-B - Mathf.Sqrt(h)) / A;
                float y = baoa + t * bard;          // where it lands along the body
                if (y > 0f && y < baba && t >= 0f)  // through the cylindrical middle
                {
                    distance = t;
                    return true;
                }
            }
        }

        // Caps. Both are tested and the nearer valid hit wins — which end is
        // "first" depends on the firing direction, so picking one up front is
        // how the previous version put a head shot at the feet.
        bool hitA = RayHitsSphere(origin, d, a, radius, out float tA);
        bool hitB = RayHitsSphere(origin, d, b, radius, out float tB);

        if (hitA && hitB) { distance = Mathf.Min(tA, tB); return true; }
        if (hitA)         { distance = tA; return true; }
        if (hitB)         { distance = tB; return true; }
        return false;
    }

    /// Nearest non-negative ray-sphere intersection.
    static bool RayHitsSphere(Vector3 origin, Vector3 d, Vector3 centre, float radius,
                              out float distance)
    {
        distance = 0f;
        Vector3 oc = origin - centre;
        float b = Vector3.Dot(d, oc);
        float c = Vector3.Dot(oc, oc) - radius * radius;
        float h = b * b - c;
        if (h < 0f) return false;

        h = Mathf.Sqrt(h);
        float t = -b - h;                 // near intersection
        if (t < 0f) t = -b + h;           // inside the sphere: use the far one
        if (t < 0f) return false;         // sphere entirely behind the shooter

        distance = t;
        return true;
    }

    // ── damage ───────────────────────────────────────────────────────────

    [ServerRpc]
    void ReportHitServerRpc(ulong victimClientId)
    {
        // The AMOUNT is deliberately not a parameter. Trusting the shooter for
        // whether a hit landed is a design choice (see the class comment);
        // trusting them for how much it hurt is just an unnecessary value on
        // the wire, and DamagePerHit is a const both builds already share.
        var p = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { victimClientId } }
        };
        ApplyDamageClientRpc(p);
    }

    [ClientRpc]
    void ApplyDamageClientRpc(ClientRpcParams clientRpcParams = default)
    {
        // Runs on the VICTIM. ResourceManager is scene-placed and owns the death
        // check in its own Update, so simply taking damage is enough to die —
        // MultiplayerDeathRespawn picks it up from there.
        var rm = ResourceManager.Instance;
        if (rm != null) rm.TakeDamage(DamagePerHit);

        SpawnLocalPlayerSplash();
    }

    /// <summary>
    /// Blood at OUR OWN centre, so being shot is something you can see happening
    /// to you and not just a health bar quietly dropping.
    ///
    /// ⚠️ Deliberately NOT `transform.position`. This RPC was invoked on the
    /// SHOOTER'S puppet, so on our machine `this` is the person who shot us —
    /// spraying blood out of them, several metres away, would tell us precisely
    /// the wrong thing. The roster is how we find our own rig.
    /// </summary>
    static void SpawnLocalPlayerSplash()
    {
        var fx = BloodFX.Instance;
        if (fx == null) return;

        var all = PlayerRoster.All();
        for (int i = 0; i < all.Count; i++)
        {
            if (!all[i].IsLocal || all[i].Transform == null) continue;
            var t = all[i].Transform;
            fx.SpawnDamageSplash(t.TransformPoint(new Vector3(0f, 1.1f, 0f)), t);
            return;
        }
    }
}
