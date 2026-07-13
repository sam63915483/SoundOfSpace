using UnityEngine;

/// Sits on the SCARE object (sphere trigger just inside the Tevsship entrance).
/// Forwards player entry to the mission director on the ship root.
[RequireComponent(typeof(Collider))]
public class TevScareTrigger : MonoBehaviour
{
    public TevSmugglingMission mission;

    void Awake()
    {
        if (mission == null) mission = GetComponentInParent<TevSmugglingMission>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (mission == null) return;
        var pc = other.GetComponentInParent<PlayerController>();
        if (pc == null) return;
        mission.OnPlayerEnteredShip(pc);
    }
}
