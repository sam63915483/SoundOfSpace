using UnityEngine;

/// <summary>
/// One-shot trigger volume at the main village. Fires a static event the first time the
/// player enters. Attached (by the controller) to the existing "village" GameObject in
/// Humble Abode with an isTrigger SphereCollider.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class VillageReachTrigger : MonoBehaviour
{
    public static event System.Action OnVillageReached;
    bool _fired;

    void Reset()
    {
        var c = GetComponent<SphereCollider>();
        c.isTrigger = true;
        c.radius = 60f;
    }

    void OnTriggerEnter(Collider other)
    {
        if (_fired || !other.CompareTag("Player")) return;
        _fired = true;
        OnVillageReached?.Invoke();
        Debug.Log("[Story] VillageReachTrigger fired.");
    }
}
