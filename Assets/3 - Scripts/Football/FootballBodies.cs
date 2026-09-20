using System.Collections.Generic;
using UnityEngine;

/// One contact between two discs this tick. `normal` points from `a` to `b`
/// (field space, y = 0). `closing` is how fast they were approaching along
/// it before the momentum trade (+ = toward each other).
public struct BodyContact
{
    public FootballPlayer a, b;
    public Vector3 normal;
    public float closing;
    public float overlap;
    public FootballPlayer Other(FootballPlayer p) => p == a ? b : a;
    /// The normal as seen from `p` (pointing at the other man).
    public Vector3 NormalFrom(FootballPlayer p) => p == a ? normal : -normal;
}

/// The solid-body layer (design 2026-09-20): every man is a disc on the field
/// plane with a radius and a mass. Once per sim step, AFTER the brains have
/// moved everyone and BEFORE tackles are judged, overlapping pairs are pushed
/// apart (the heavier man moves less), closing momentum is traded (no bounce),
/// and every contact is recorded for the blocking and tackling rules. Nothing
/// else in the sim tests "touching" by distance any more.
public static class FootballBodies
{
    public const float DownRadius = 0.55f;        // a man on the ground: a wide, immovable obstacle

    // Blocking: two forces in an engagement (spec §2).
    public const float PushBase = 1.0f;
    public const float DriveGain = 2.4f;          // m/s per unit of (push − hold)
    public const float MaxDrive = 1.2f;           // m/s, the rusher driving the blocker back
    public const float MinDriveBack = -0.15f;     // m/s, a winning blocker stands him up, he doesn't carry him upfield
    public const float ContactSlop = 0.03f;       // a pair this close still counts as touching (a stable engagement)
    public const float MaxShiftPerTick = 0.12f;   // separation is a few cm a tick, never a pop (a man standing up out of a pile)
    public const float NonLinemanHold = 0.6f;     // a receiver's hold is weak
    public const float SwimDriveScale = 0.5f;     // swimming, he pushes at half strength

    // Tackling: hit quality (spec §3).
    public const float TackleBase = 2.4f;         // a tackler on him at no closing speed still has this much (a grab from behind drags him down ~3 times in 4)
    public const float WrapThreshold = 2.3f;      // hit ≥ this is a wrap; below is an arm tackle

    public static float RadiusFor(FootballRole role)
        => role == FootballRole.OL || role == FootballRole.DL || role == FootballRole.C ? 0.50f
         : role == FootballRole.LB || role == FootballRole.S ? 0.45f : 0.42f;

    public static float MassFor(FootballRole role)
        => role == FootballRole.OL || role == FootballRole.DL || role == FootballRole.C ? 1.25f
         : role == FootballRole.LB || role == FootballRole.S ? 1.10f : 1.00f;

    public static bool IsLineman(FootballPlayer p)
        => p.role == FootballRole.OL || p.role == FootballRole.DL || p.role == FootballRole.C;

    public static float Push(float stat, float mass, float swing) => PushBase * (0.6f + 0.8f * stat) * mass * swing;
    public static float Hold(float stat, float mass, float swing, bool lineman) => PushBase * (0.6f + 0.8f * stat) * mass * swing * (lineman ? 1f : NonLinemanHold);
    /// m/s the engaged pair moves along the rusher's line; + = the blocker is driven back.
    public static float BlockDrive(float push, float hold) => Mathf.Clamp((push - hold) * DriveGain, MinDriveBack, MaxDrive);

    /// `closing` m/s along the contact normal, `square` 0..1 (1 = met head-on
    /// or from straight behind, 0 = glancing), `mass` the tackler's.
    public static float TackleHit(float closing, float square, float mass)
        => (TackleBase + Mathf.Max(0f, closing)) * (0.2f + 0.8f * square) * mass;

    /// The pass. `contacts` is cleared and refilled.
    public static void Resolve(IReadOnlyList<FootballPlayer> players, List<BodyContact> contacts)
    {
        contacts.Clear();
        int n = players.Count;
        for (int i = 0; i < n; i++)
        {
            var a = players[i];
            if (a == null) continue;
            for (int j = i + 1; j < n; j++)
            {
                var b = players[j];
                if (b == null) continue;
                if (a.IsDown && b.IsDown) continue;                              // a pile doesn't shuffle
                if ((a.BodyLifted && b.IsDiving) || (b.BodyLifted && a.IsDiving)) continue;   // a hurdle clears a diver
                float ra = a.IsDown ? DownRadius : a.BodyRadius, rb = b.IsDown ? DownRadius : b.BodyRadius;
                Vector3 d = b.Pos - a.Pos; d.y = 0f;
                float dist = d.magnitude, minD = ra + rb;
                if (dist >= minD + ContactSlop) continue;
                Vector3 normal = dist > 1e-4f ? d / dist : Vector3.right;
                float overlap = Mathf.Max(0f, minD - dist);
                float ia = a.IsDown ? 0f : 1f / a.BodyMass, ib = b.IsDown ? 0f : 1f / b.BodyMass;
                float sum = ia + ib;
                if (sum > 0f && overlap > 0f)
                {
                    float step = Mathf.Min(overlap, MaxShiftPerTick);
                    a.ShiftBody(-normal * (step * ia / sum));
                    b.ShiftBody(normal * (step * ib / sum));
                }
                float va = Vector3.Dot(a.Vel, normal), vb = Vector3.Dot(b.Vel, normal);
                float closing = va - vb;
                if (closing > 0f && sum > 0f)
                {
                    // Perfectly inelastic along the normal: both take the common speed.
                    float common = ia > 0f && ib > 0f ? (va * a.BodyMass + vb * b.BodyMass) / (a.BodyMass + b.BodyMass) : (ia > 0f ? vb : va);
                    if (ia > 0f) a.SetVelAlong(normal, common);
                    if (ib > 0f) b.SetVelAlong(normal, common);
                }
                a.inContact = b.inContact = true;
                contacts.Add(new BodyContact { a = a, b = b, normal = normal, closing = closing, overlap = overlap });
            }
        }
    }
}
