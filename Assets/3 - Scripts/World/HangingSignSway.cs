using UnityEngine;

/// <summary>
/// A sign hanging from a bracket swings gently about the beam it hangs from.
/// Put this on the pivot node at the beam (the chains and board are its
/// children); <see cref="axis"/> is the beam's direction in that node's local
/// space. Pure local rotation, so it works at any orientation on a planet.
///
/// MeshCombineTool skips any subtree with this component — the same ghost trap
/// as doors and mill fans: baking a moving part welds a frozen copy in place.
/// </summary>
public class HangingSignSway : MonoBehaviour
{
    [Tooltip("Beam direction in this node's local space (the axis the sign swings about).")]
    public Vector3 axis = Vector3.forward;
    [Tooltip("Peak swing angle in degrees.")]
    public float amplitude = 3.5f;
    [Tooltip("Swings per second.")]
    public float frequency = 0.28f;
    [Tooltip("Slow gust that scales the swing over time (0 = steady).")]
    public float gustDepth = 0.5f;

    Quaternion _rest;
    float _phase;

    void Awake()
    {
        _rest = transform.localRotation;
        _phase = Random.Range(0f, 6.28f);
    }

    void LateUpdate()
    {
        float t = Time.time;
        float gust = 1f - gustDepth * 0.5f * (1f + Mathf.Sin(t * 0.37f + _phase));
        float a = Mathf.Sin(t * frequency * 6.2831853f + _phase) * amplitude * gust;
        transform.localRotation = _rest * Quaternion.AngleAxis(a, axis);
    }
}
