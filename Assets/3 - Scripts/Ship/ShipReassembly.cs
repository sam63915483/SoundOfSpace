using UnityEngine;

public class ShipReassembly : MonoBehaviour
{
    public void UnfreezeShip()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            Debug.Log("Ship unfrozen – ready to fly!");
        }
    }
}