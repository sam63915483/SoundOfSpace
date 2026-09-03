using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BOUNTY WATER. Put this on Sam's sphere collider in the lake (GRULABUSPOT).
/// While the parked bobber is inside the sphere, every bite roll has
/// <see cref="chance"/> of being <see cref="speciesId"/> (a bounty row in
/// FishingRules.Species) instead of the ordinary table. One per world: once the
/// bounty is landed the caught flag is set and the zone stops rolling.
///
/// Purely geometric -- the bobber has no rigidbody while parked, so trigger
/// callbacks would be unreliable; Bobber asks <see cref="TryRoll"/> at the
/// moment of the bite instead. The collider is forced to isTrigger and the
/// object onto Ignore Raycast at Awake, so a saved scene where the sphere was
/// left solid on the Body layer can never become an invisible 30 m ball in
/// the lake.
/// </summary>
public class BountyZone : MonoBehaviour
{
    [Tooltip("Species id from FishingRules.Species (a bounty row).")]
    public string speciesId = "grulabu";
    [Range(0f, 1f)] public float chance = 0.28f;   // 0.2 x 1.4 (Sam, 2026-09-03: the fight is hard, more tries)
    [Tooltip("Story flag set when the bounty is landed; the zone is dead once it is true.")]
    public string caughtFlag = "grulabu_caught";

    static readonly List<BountyZone> s_all = new List<BountyZone>();
    SphereCollider _sphere;

    void Awake()
    {
        _sphere = GetComponent<SphereCollider>();
        if (_sphere != null) _sphere.isTrigger = true;
        gameObject.layer = 2;   // Ignore Raycast -- a trigger this size must never be a ground hit
    }

    void OnEnable()  { if (!s_all.Contains(this)) s_all.Add(this); }
    void OnDisable() { s_all.Remove(this); }

    public bool Armed =>
        StoryDirector.Instance == null || !StoryDirector.Instance.GetFlag(caughtFlag);

    public bool Contains(Vector3 worldPos)
    {
        Vector3 c = _sphere != null ? transform.TransformPoint(_sphere.center) : transform.position;
        Vector3 s = transform.lossyScale;
        float r = (_sphere != null ? _sphere.radius : 10f)
                * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        return (worldPos - c).sqrMagnitude <= r * r;
    }

    /// <summary>At bite time: is this bite the bounty? Returns the species index if so.</summary>
    public static bool TryRoll(Vector3 bobberWorldPos, float rand01, out int speciesIndex)
    {
        speciesIndex = -1;
        for (int i = 0; i < s_all.Count; i++)
        {
            var z = s_all[i];
            if (z == null || !z.Armed || !z.Contains(bobberWorldPos)) continue;
            int idx = FishingRules.IndexOfId(z.speciesId);
            if (idx < 0) continue;
            if (rand01 < z.chance) { speciesIndex = idx; return true; }
        }
        return false;
    }

    /// <summary>A bounty species was landed: retire every zone that offers it.</summary>
    public static void NoteCaught(int speciesIndex)
    {
        if (speciesIndex < 0 || speciesIndex >= FishingRules.Species.Length) return;
        string id = FishingRules.Species[speciesIndex].id;
        for (int i = 0; i < s_all.Count; i++)
        {
            var z = s_all[i];
            if (z != null && z.speciesId == id && StoryDirector.Instance != null)
                StoryDirector.Instance.SetFlag(z.caughtFlag, true);
        }
    }
}
