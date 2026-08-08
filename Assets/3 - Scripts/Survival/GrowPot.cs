using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Grow Pot — the Industry path's tier 1, and the first thing a player builds
/// that makes mushrooms instead of just housing them.
///
/// Plant spores inside its radius and they grow faster and drop more spores
/// than they would in open dirt, so a pot farm never dead-ends the way wild
/// foraging does.
///
/// ── Radius, not a socket ─────────────────────────────────────────────────
/// The spec called for "one shroom socket". This is a small radius-of-effect
/// planter instead, and it is a deliberate simplification: a socket needs a new
/// placement mode, an occupancy model, and its own UI, whereas a radius reuses
/// the planting flow the player already knows and matches how BubbleDome
/// already works (BubbleDome.DomeContaining is the exact same shape). One pot
/// realistically fits one or two mushrooms at the default radius, so it plays
/// like a socket without being one.
///
/// Growth and spore effects live in MushroomGrowth / SpawnedMushroom — this
/// class only answers "is this point in a pot".
/// </summary>
public class GrowPot : MonoBehaviour
{
    static readonly List<GrowPot> s_all = new List<GrowPot>();
    public static IReadOnlyList<GrowPot> AllPots => s_all;

    void OnEnable()  { if (!s_all.Contains(this)) s_all.Add(this); }
    void OnDisable() { s_all.Remove(this); }

    public float Radius => radius;

    public bool IsInside(Vector3 worldPos)
        => (worldPos - transform.position).sqrMagnitude < radius * radius;

    /// The pot containing this point, or null. Mirrors BubbleDome.DomeContaining
    /// so callers can treat the two the same way. Linear scan over a list that
    /// is bounded by how many pots the player has actually built — no spatial
    /// structure needed, and it's only queried on the growth sample tick, not
    /// per frame.
    public static GrowPot PotContaining(Vector3 worldPos)
    {
        for (int i = 0; i < s_all.Count; i++)
        {
            var p = s_all[i];
            if (p != null && p.IsInside(worldPos)) return p;
        }
        return null;
    }

    // ── Tunables ─────────────────────────────────────────────────────────

    [Tooltip("Radius (m) of the pot's soil. Small on purpose — big enough to plant one or two caps in, not a field. Raise it if planting inside the pot feels fiddly.")]
    [SerializeField] float radius = 1.6f;
}
