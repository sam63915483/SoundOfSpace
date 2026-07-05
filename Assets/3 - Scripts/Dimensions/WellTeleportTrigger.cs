using UnityEngine;

/// <summary>Wrong-well punishment: jumping in teleports you to a random distant dune.</summary>
public class WellTeleportTrigger : MonoBehaviour
{
    [HideInInspector] public WellFieldController owner;
    bool _cooling;

    void OnTriggerEnter(Collider other)
    {
        if (_cooling || owner == null) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;
        _cooling = true;
        owner.TeleportPlayerToRandomDune();
        Invoke(nameof(EndCooldown), 1f);     // debounce multi-collider player rigs
    }

    void EndCooldown() { _cooling = false; }
}
