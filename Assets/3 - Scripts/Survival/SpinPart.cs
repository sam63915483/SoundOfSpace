using UnityEngine;

/// <summary>
/// Continuously rotates this transform about a local axis — used to animate the
/// static generated dome pieces (the gyro ring, the core) so the assembled
/// generator has moving parts.
/// </summary>
public class SpinPart : MonoBehaviour
{
    public Vector3 axis = Vector3.up;
    public float speed = 40f;   // deg/sec

    // Optional gyroscopic precession: on top of the fast self-spin above, the whole
    // object slowly cones around a SECOND axis (measured in the parent's frame, so it
    // tracks the dome's up even on a tilted planet surface). With this on, a tilted
    // ring tumbles in all directions instead of spinning flat like a wheel. Leave
    // precessSpeed at 0 for a plain single-axis spin (unchanged behaviour).
    public Vector3 precessAxis = Vector3.up;
    public float precessSpeed = 0f;   // deg/sec; 0 = no precession

    void Update()
    {
        transform.Rotate(axis.normalized, speed * Time.deltaTime, Space.Self);

        if (precessSpeed != 0f)
        {
            Vector3 worldAxis = transform.parent != null
                ? transform.parent.TransformDirection(precessAxis.normalized)
                : precessAxis.normalized;
            transform.Rotate(worldAxis, precessSpeed * Time.deltaTime, Space.World);
        }
    }
}
