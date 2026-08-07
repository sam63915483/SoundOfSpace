using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// Host-authoritative solar-system sync. Each machine runs its own n-body sim
/// (planets always move smoothly and never wait on the network); the host
/// streams every body's sun-relative state at a low rate and clients apply
/// corrections — velocity adopted fully, position nudged gently — so the two
/// sims converge and stay converged. Large mismatches (the initial join: the
/// two sims sit at different orbital phases) hard-snap, carrying every free
/// rigidbody near the snapped body by the same delta so planet-relative poses
/// are preserved — on screen nothing moves; the universe rearranges around you.
///
/// Wire frame is sun-relative: world coordinates are machine-specific and
/// floating-origin-shifted; sun-relative is invariant to both. The client's
/// own sun is the anchor and is never moved. Static attractors (black hole)
/// never move — not synced. Follower bodies are re-derived from their leader
/// by NBodySimulation every tick, so only satellites' orbital phase crosses
/// the wire; the client re-derives follower poses locally after leader snaps
/// (mirroring NBodySimulation's placement math) to carry their riders too.
public class SolarSystemSync : MonoBehaviour
{
    public float syncInterval = 1f;
    public float hardSnapDistance = 5f;  // meters of error before snap+carry
    public float softPosGain = 0.2f;     // fraction of position error corrected per update
    public float carryRadiusFactor = 4f; // riders carried if within radius*this of a snapped body

    const string MsgName = "SolarState";
    const byte KindBody = 0;
    const byte KindSatellitePhase = 1;

    bool handlerRegistered;
    float nextSendTime;

    void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (nm.IsListening && !handlerRegistered)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgName, OnStateMessage);
            nm.OnClientConnectedCallback += OnClientConnected;
            handlerRegistered = true;
        }
        else if (!nm.IsListening && handlerRegistered)
        {
            nm.OnClientConnectedCallback -= OnClientConnected;
            handlerRegistered = false;
        }

        if (nm.IsServer && nm.IsListening && Time.unscaledTime >= nextSendTime)
        {
            nextSendTime = Time.unscaledTime + syncInterval;
            foreach (var c in nm.ConnectedClientsList)
                if (c.ClientId != nm.LocalClientId)
                    SendState(c.ClientId);
        }
    }

    void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsServer && clientId != nm.LocalClientId)
            SendState(clientId); // immediate full state so the join snap happens right away
    }

    static CelestialBody FindSun()
    {
        foreach (var b in NBodySimulation.Bodies)
            if (b != null && b.bodyType == CelestialBody.BodyType.Sun) return b;
        return null;
    }

    static uint NameHash(string s)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (char c in s) { h ^= c; h *= 16777619u; }
            return h;
        }
    }

    // ── Server ───────────────────────────────────────────────────────────────

    void SendState(ulong clientId)
    {
        var sun = FindSun();
        if (sun == null) return;

        var entries = new List<(uint hash, byte kind, Vector3 a, Vector3 b)>();
        foreach (var body in NBodySimulation.Bodies)
        {
            if (body == null || body == sun || body.isStaticAttractor) continue;
            if (body.coOrbitLeader != null)
            {
                // Followers are derived from their leader; only a satellite's
                // locally-accumulated phase is real state.
                if (body.satelliteOrbitRadius > 0f)
                    entries.Add((NameHash(body.bodyName), KindSatellitePhase,
                        new Vector3(body.satellitePhase, 0f, 0f), Vector3.zero));
                continue;
            }
            entries.Add((NameHash(body.bodyName), KindBody,
                body.Position - sun.Position, body.velocity - sun.velocity));
        }

        int size = 8 + entries.Count * (4 + 1 + 12 + 12) + 64;
        using var writer = new FastBufferWriter(size, Allocator.Temp);
        writer.WriteValueSafe(entries.Count);
        foreach (var e in entries)
        {
            writer.WriteValueSafe(e.hash);
            writer.WriteValueSafe(e.kind);
            writer.WriteValueSafe(e.a);
            writer.WriteValueSafe(e.b);
        }
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            MsgName, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
    }

    // ── Client ───────────────────────────────────────────────────────────────

    void OnStateMessage(ulong senderId, FastBufferReader reader)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.IsServer) return; // host is the authority

        var sun = FindSun();
        if (sun == null) return;

        var byHash = new Dictionary<uint, CelestialBody>();
        foreach (var b in NBodySimulation.Bodies)
            if (b != null) byHash[NameHash(b.bodyName)] = b;

        reader.ReadValueSafe(out int count);
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out uint hash);
            reader.ReadValueSafe(out byte kind);
            reader.ReadValueSafe(out Vector3 a);
            reader.ReadValueSafe(out Vector3 b);
            if (!byHash.TryGetValue(hash, out var body) || body == null) continue;

            if (kind == KindSatellitePhase)
                body.satellitePhase = a.x;
            else
                ApplyBodyCorrection(body, sun.Position + a, sun.velocity + b);
        }

        // Second pass: followers. NBodySimulation re-derives their pose from
        // the leader next tick anyway; doing it here too lets us detect a big
        // jump (leader just snapped / phase just synced) and carry riders.
        foreach (var f in NBodySimulation.Bodies)
        {
            if (f == null || f.coOrbitLeader == null) continue;
            if (TryDeriveFollowerPose(f, sun, out Vector3 fPos, out Vector3 fVel))
            {
                Vector3 err = fPos - f.Position;
                if (err.magnitude > hardSnapDistance)
                {
                    Vector3 velDelta = fVel - f.velocity;
                    CarryRiders(f, err, velDelta);
                    f.ApplySavedState(fPos, f.transform.rotation, fVel);
                    Physics.SyncTransforms();
                    Debug.Log($"[MP][ORBIT] follower '{f.bodyName}' snapped {err.magnitude:F0}m with its leader");
                }
            }
        }
    }

    // Mirrors NBodySimulation.FixedUpdate's follower placement exactly — keep
    // in step with that code if it ever changes.
    static bool TryDeriveFollowerPose(CelestialBody f, CelestialBody sun, out Vector3 pos, out Vector3 vel)
    {
        pos = default; vel = default;
        var lead = f.coOrbitLeader;
        if (lead == null) return false;
        Vector3 origin = sun != null ? sun.Position : Vector3.zero;
        Vector3 r = lead.Position - origin;
        Vector3 v = lead.velocity;
        Vector3 normal = Vector3.Cross(r, v);
        if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
        normal = normal.normalized;

        if (f.satelliteOrbitRadius > 0f)
        {
            float w = f.satellitePeriod > 0.01f ? (2f * Mathf.PI / f.satellitePeriod) : 0f;
            Vector3 radialAxis = r.sqrMagnitude > 1e-6f ? r.normalized : Vector3.right;
            Vector3 tangentAxis = Vector3.Cross(normal, radialAxis);
            Vector3 offset = (radialAxis * Mathf.Cos(f.satellitePhase)
                            + tangentAxis * Mathf.Sin(f.satellitePhase)) * f.satelliteOrbitRadius;
            Vector3 offsetVel = (-radialAxis * Mathf.Sin(f.satellitePhase)
                               + tangentAxis * Mathf.Cos(f.satellitePhase)) * (f.satelliteOrbitRadius * w);
            pos = lead.Position + offset;
            vel = lead.velocity + offsetVel;
        }
        else
        {
            var rot = Quaternion.AngleAxis(f.coOrbitAngle, normal);
            pos = origin + rot * r;
            vel = rot * v;
        }
        return true;
    }

    void ApplyBodyCorrection(CelestialBody body, Vector3 targetPos, Vector3 targetVel)
    {
        Vector3 posErr = targetPos - body.Position;
        Vector3 velDelta = targetVel - body.velocity;

        if (posErr.magnitude > hardSnapDistance)
        {
            CarryRiders(body, posErr, velDelta);
            body.ApplySavedState(targetPos, body.transform.rotation, targetVel);
            Physics.SyncTransforms();
            Debug.Log($"[MP][ORBIT] '{body.bodyName}' hard-snapped {posErr.magnitude:F0}m to match host");
        }
        else
        {
            // Gentle convergence: adopt host velocity outright, walk position
            // error down a fraction per update. Sub-snap errors are cm-scale —
            // riders don't need carrying, contacts absorb it.
            body.ApplySavedState(body.Position + posErr * softPosGain, body.transform.rotation, targetVel);
        }
    }

    /// Carry every free rigidbody near the snapped body by the same delta so
    /// its body-relative pose (and therefore what's on screen) is unchanged.
    /// Skips: celestial bodies (corrected individually), network puppets
    /// (placed from net pose every frame), and anything parented under a
    /// celestial body (the hierarchy + Physics.SyncTransforms carries those).
    void CarryRiders(CelestialBody body, Vector3 posDelta, Vector3 velDelta)
    {
        float maxDist = body.radius * carryRadiusFactor;
        foreach (var rb in FindObjectsOfType<Rigidbody>(true))
        {
            if (rb == null) continue;
            if (rb.GetComponent<CelestialBody>() != null) continue;
            if (rb.GetComponentInParent<NetworkObject>() != null) continue;
            if (rb.GetComponentInParent<CelestialBody>() != null) continue;
            if ((rb.position - body.Position).magnitude > maxDist) continue;

            rb.position += posDelta;
            if (!rb.isKinematic) rb.velocity += velDelta;
            rb.transform.position = rb.position;
        }
    }
}
