using UnityEngine;

/// <summary>
/// Mission 1 explore beat (GDD §4). A one-shot trigger volume seeded in the
/// start→village zone. The first time the player enters it, it records the
/// discoverable on <see cref="Mission1"/> and fires a short "observed it" line
/// through the phone AI's HAL strip. Once the player has seen enough of these,
/// Tev's report + the three-way fork unlock.
///
/// Setup (CP-A): drop this on an empty GameObject with a trigger collider near the
/// thing worth noticing, set the id (one of Mission1's Disc* constants) and the
/// observed line. Mirrors VillageReachTrigger's one-shot pattern.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class Discoverable : MonoBehaviour
{
    [Tooltip("Use one of the Mission1.Disc* ids: m1_disc_vista, m1_disc_structure, m1_disc_fishing.")]
    [SerializeField] string discoverableId = Mission1.DiscStructure;

    [Tooltip("The short line the phone AI volunteers when the player first sees this.")]
    [TextArea(2, 4)]
    [SerializeField] string observedLine = "Logging that. Worth mentioning to Tev.";

    bool _fired;

    void Reset()
    {
        var c = GetComponent<SphereCollider>();
        c.isTrigger = true;
        c.radius = 15f;
    }

    void OnTriggerEnter(Collider other)
    {
        if (_fired || !other.CompareTag("Player")) return;
        _fired = true;

        Mission1.MarkSeen(discoverableId);

        if (!string.IsNullOrEmpty(observedLine) && HALCommentator.Instance != null)
            HALCommentator.Instance.VolunteerExternal(observedLine);

        // Nudge the player back to Tev once they've seen enough.
        if (Mission1.ExploredEnough() && HALCommentator.Instance != null)
            HALCommentator.Instance.VolunteerExternal("You've seen enough to report back. Tev will want to hear it.");
    }
}
