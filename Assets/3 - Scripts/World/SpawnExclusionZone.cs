using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A spherical keep-out volume for procedural spawners. Drop one on a place that should stay
/// clear of random clutter (e.g. the ship school) and the alien / crystal / mushroom spawners
/// will reject any candidate position inside it.
///
/// Spawners call <see cref="IsExcluded"/> with their computed surface position before placing.
/// </summary>
public class SpawnExclusionZone : MonoBehaviour
{
    [Tooltip("World-space radius (metres) to keep clear of spawned objects.")]
    public float radius = 15f;

    static readonly List<SpawnExclusionZone> _zones = new List<SpawnExclusionZone>();

    void OnEnable()  { if (!_zones.Contains(this)) _zones.Add(this); }
    void OnDisable() { _zones.Remove(this); }

    /// <summary>True if the world position falls inside any active exclusion zone.</summary>
    public static bool IsExcluded(Vector3 worldPos)
    {
        for (int i = 0; i < _zones.Count; i++)
        {
            var z = _zones[i];
            if (z == null) continue;
            float r = z.radius;
            if ((worldPos - z.transform.position).sqrMagnitude < r * r) return true;
        }
        return false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
