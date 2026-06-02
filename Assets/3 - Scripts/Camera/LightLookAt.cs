using UnityEngine;

public class LightLookAt : MonoBehaviour
{
    public float turnSpeed = 5f;
    public Vector3 rotationOffset = Vector3.zero;

    Transform player;
    Quaternion smoothedRot;
    float _nextFindAttemptTime;
    const float FindRetryInterval = 1f; // throttle FindWithTag retries to once a second

    void Start()
    {
        smoothedRot = transform.rotation;
    }

    void LateUpdate()
    {
        if (player == null)
        {
            // Throttle: retrying FindWithTag every LateUpdate forever (e.g. in a
            // scene without a Player tag) is a wasted lookup every frame.
            if (Time.unscaledTime < _nextFindAttemptTime) return;
            _nextFindAttemptTime = Time.unscaledTime + FindRetryInterval;

            GameObject p = GameObject.FindWithTag("Player");
            if (p != null) player = p.transform;
            else return;
        }

        Vector3 dir = (player.position - transform.position).normalized;
        Quaternion targetRot = Quaternion.LookRotation(dir, transform.up)
                               * Quaternion.Euler(rotationOffset);

        smoothedRot = Quaternion.Slerp(smoothedRot, targetRot, turnSpeed * Time.deltaTime);
        transform.rotation = smoothedRot;
    }
}
