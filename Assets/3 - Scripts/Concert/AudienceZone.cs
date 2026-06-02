using System.Collections.Generic;
using UnityEngine;

// Designer-placed rectangular zone in front of a concert stage. Drop on an empty
// GameObject parented to the planet's CelestialBody, position it where the crowd
// should stand, drag the speaker Transform onto `speaker`, and tune `size`.
//
// AudienceSpawner asks this zone for scattered spawn poses. Each pose is
// snapped to the terrain via a downward raycast along the planet's gravity-up
// direction, so the crowd stands on the ground rather than floating.
public class AudienceZone : MonoBehaviour
{
    [Tooltip("Audience members face this Transform. Usually a speaker on the stage.")]
    public Transform speaker;

    [Tooltip("Local-space size of the spawn box. X = width along stage front, Z = depth back into the crowd.")]
    public Vector3 size = new Vector3(20f, 4f, 12f);

    [Tooltip("Layers the surface raycast should hit. Should include terrain, exclude water/ship/player.")]
    public LayerMask groundMask = ~0;

    [Tooltip("Height above the planet surface where the downward raycast originates.")]
    public float surfaceRayHeight = 50f;

    [Tooltip("Maximum slope (degrees from radial-up) where audience may spawn. Above this the candidate is rejected.")]
    [Range(0f, 90f)] public float maxSurfaceAngle = 35f;

    [Tooltip("Push the alien this far into the ground along the planet-radial axis. Positive = into the ground.")]
    public float groundOffset = 0f;

    [Tooltip("Minimum distance between two spawn points (world units). Prevents audience members from spawning inside each other.")]
    public float minSpacing = 2.5f;

    [Tooltip("Fraction of samples that may extend beyond the nominal box (0..1). 0 = strict box. 0.15 = ~15% of aliens are stragglers in a halo around the box, breaking the rectangular outline.")]
    [Range(0f, 0.5f)] public float stragglerFraction = 0.15f;

    [Tooltip("How far stragglers can extend past the box edge, as a multiplier on the box size. 1.4 = up to 40% beyond.")]
    [Range(1f, 2f)] public float stragglerReach = 1.4f;

    CelestialBody _body;

    public CelestialBody Body
    {
        get
        {
            if (_body == null) _body = GetComponentInParent<CelestialBody>();
            return _body;
        }
    }

    Vector3 GravityUp()
    {
        var body = Body;
        if (body == null) return transform.up;
        return (transform.position - body.Position).normalized;
    }

    // Returns up to `count` poses inside the zone, raycast-snapped to terrain
    // and rotated to face the speaker (with up = local gravity up). Candidates
    // are rejected if they fall within `minSpacing` of an existing audience
    // member or another already-picked candidate, so members don't spawn
    // inside each other. Up to `count * 8` candidates are tried; if the zone
    // is too small for the requested count, fewer poses are returned.
    public List<Pose> SamplePositions(int count, System.Random rng, IReadOnlyList<Vector3> existingWorldPositions = null)
    {
        var results = new List<Pose>(count);
        if (speaker == null || count <= 0) return results;

        Vector3 up = GravityUp();
        float minSpacingSq = minSpacing * minSpacing;
        int attempts = count * 8;
        for (int i = 0; i < attempts && results.Count < count; i++)
        {
            // Triangular distribution (sum of two uniforms) for a soft, dense-
            // in-the-middle / sparse-at-the-edges crowd shape. No hard
            // rectangular boundary — density falls off naturally toward the
            // perimeter.
            float tx = (float)(rng.NextDouble() - rng.NextDouble()); // -1..1, peaks at 0
            float tz = (float)(rng.NextDouble() - rng.NextDouble());

            // Small chance the candidate is a "straggler" outside the nominal
            // box (real crowds have a few people standing further back / off
            // to the side).
            float reachX = 0.5f, reachZ = 0.5f;
            if (rng.NextDouble() < stragglerFraction)
            {
                reachX = 0.5f * stragglerReach;
                reachZ = 0.5f * stragglerReach;
            }

            float lx = tx * size.x * reachX;
            float lz = tz * size.z * reachZ;
            Vector3 localPoint = new Vector3(lx, 0f, lz);
            Vector3 worldPoint = transform.TransformPoint(localPoint);

            Vector3 rayOrigin = worldPoint + up * surfaceRayHeight;
            if (!Physics.Raycast(rayOrigin, -up, out RaycastHit hit, surfaceRayHeight * 2f, groundMask, QueryTriggerInteraction.Ignore))
                continue;

            if (Vector3.Angle(hit.normal, up) > maxSurfaceAngle) continue;

            Vector3 pos = hit.point - up * groundOffset;

            // Reject if too close to an already-picked candidate or to an
            // already-spawned audience member.
            bool tooClose = false;
            for (int j = 0; j < results.Count && !tooClose; j++)
                if ((results[j].position - pos).sqrMagnitude < minSpacingSq) tooClose = true;
            if (!tooClose && existingWorldPositions != null)
            {
                for (int j = 0; j < existingWorldPositions.Count && !tooClose; j++)
                    if ((existingWorldPositions[j] - pos).sqrMagnitude < minSpacingSq) tooClose = true;
            }
            if (tooClose) continue;

            // Face the speaker along the tangent plane (project the speaker
            // vector onto the surface, so audience aliens stand upright).
            Vector3 toSpeaker = speaker.position - pos;
            Vector3 facing = Vector3.ProjectOnPlane(toSpeaker, up);
            if (facing.sqrMagnitude < 0.0001f) facing = transform.forward;
            Quaternion rot = Quaternion.LookRotation(facing.normalized, up);

            results.Add(new Pose(pos, rot));
        }
        return results;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        Gizmos.DrawCube(Vector3.zero, new Vector3(size.x, 0.1f, size.z));
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, size.z));
        Gizmos.matrix = Matrix4x4.identity;

        if (speaker != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, speaker.position);
            Gizmos.DrawSphere(speaker.position, 0.5f);
        }
    }
}
