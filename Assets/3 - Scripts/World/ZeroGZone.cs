using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A sphere in which gravity is switched off and the player is treated as
/// being in open space (free-float: mouse pitches the body, Q/E roll), exactly
/// as past a planet's atmosphere line. Used for the cavern at the centre of
/// Constant Companion — gravity there is near zero anyway (it falls off
/// linearly inside a body), this makes it exactly zero and unlocks the player.
///
/// Read by Universe.GravityAcceleration (returns zero inside) and by
/// PlayerController.UpdateSpaceGate (counts as space inside). Live instances
/// are kept in a static list (OnEnable/OnDisable), CLAUDE.md style.
/// </summary>
public class ZeroGZone : MonoBehaviour
{
    static readonly List<ZeroGZone> s_all = new List<ZeroGZone>();
    public static IReadOnlyList<ZeroGZone> All => s_all;

    [Tooltip("Radius of the zero-g sphere, metres, around this transform.")]
    public float radius = 13f;

    void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }
    void OnDisable() { s_all.Remove(this); }

    public static bool Contains(Vector3 worldPoint)
    {
        for (int i = 0; i < s_all.Count; i++)
        {
            var z = s_all[i];
            if (z == null) continue;
            if ((worldPoint - z.transform.position).sqrMagnitude <= z.radius * z.radius) return true;
        }
        return false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
