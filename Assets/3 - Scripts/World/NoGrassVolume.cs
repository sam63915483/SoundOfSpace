using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A sphere where grass must not grow, tested by InstancedGrassRenderer.
///
/// WHY IT'S NEEDED SEPARATELY FROM GrassBlocker
/// GrassBlocker works by being hit by the grass renderer's surface raycast — but
/// Humble Abode's grass is BAKED (bakedGrass = Humble_Abode_Grass), and baked
/// mode does no raycasting at all. Frozen positions are the whole point of the
/// bake, so anything placed after the bake — like a cave — is invisible to it and
/// its grass keeps growing on terrain that no longer exists.
///
/// This is a straight positional test applied when the baked blob is loaded, so
/// it works in both modes and needs no re-bake.
///
/// The test happens ONCE at load, not per frame.
/// </summary>
public class NoGrassVolume : MonoBehaviour
{
    static readonly List<NoGrassVolume> s_all = new List<NoGrassVolume>();
    public static IReadOnlyList<NoGrassVolume> All => s_all;

    [Tooltip("World-space radius of the bald patch, measured from this transform.")]
    public float radius = 11f;

    void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }
    void OnDisable() { s_all.Remove(this); }

    public bool Contains(Vector3 worldPoint)
        => (worldPoint - transform.position).sqrMagnitude <= radius * radius;

    /// True if any live volume covers the point. Cheap — there are only ever a
    /// handful of these, and the grass loader calls it once per blade at load.
    public static bool AnyContains(Vector3 worldPoint)
    {
        for (int i = 0; i < s_all.Count; i++)
            if (s_all[i] != null && s_all[i].Contains(worldPoint)) return true;
        return false;
    }

    /// True if any volume covers the point, OR any punched TerrainHole does.
    /// Grass over a cut hole floats in mid-air, so the hole itself is always a
    /// no-grass volume whether or not anyone remembered to add one.
    public static bool AnyContainsOrHole(Vector3 worldPoint)
    {
        if (AnyContains(worldPoint)) return true;
        var holes = TerrainHole.All;
        for (int i = 0; i < holes.Count; i++)
            if (holes[i] != null && holes[i].Contains(worldPoint)) return true;
        return false;
    }

    /// True when there's nothing to test — lets the grass loader skip its whole
    /// filtering pass on planets that have no caves.
    public static bool AnyExist => s_all.Count > 0 || TerrainHole.All.Count > 0;

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
