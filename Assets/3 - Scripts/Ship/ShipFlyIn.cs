using UnityEngine;

public class ShipFlyIn : MonoBehaviour
{
    public float spawnDistance = 1000f;
    public float flySpeed = 50f;

    public System.Action onArrived;

    Vector3 targetLocalPosition;
    bool arrived;

    void Start()
    {
        targetLocalPosition = transform.localPosition;
        transform.localPosition = targetLocalPosition + Vector3.forward * spawnDistance;
    }

    void Update()
    {
        if (arrived) return;

        transform.localPosition = Vector3.MoveTowards(
            transform.localPosition,
            targetLocalPosition,
            flySpeed * Time.deltaTime
        );

        if (transform.localPosition == targetLocalPosition)
        {
            arrived = true;
            onArrived?.Invoke();
        }
    }
}
