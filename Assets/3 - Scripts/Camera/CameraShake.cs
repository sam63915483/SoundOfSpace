using UnityEngine;
using System.Collections;

public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    [Header("Shake Thresholds")]
    public float minShakeVelocity = 3f;        // No shake below this impact speed
    public float maxShakeVelocity = 15f;       // Impact speed that gives full intensity

    [Header("Shake Settings")]
    public float maxShakeDuration = 0.6f;      // How long the shake lasts
    public float maxShakeMagnitude = 0.8f;     // Maximum positional offset
    public float maxRoughness = 8f;            // How jagged the shake feels (higher = more jitter)
    public float dampingSpeed = 2.5f;          // How fast the shake settles

    private Vector3 originalLocalPosition;
    private float currentShakeDuration;
    private float currentShakeMagnitude;
    private float currentRoughness;

    private float shakeSeed;                   // Random seed for Perlin noise

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        originalLocalPosition = transform.localPosition;
        shakeSeed = Random.Range(0f, 1000f);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (currentShakeDuration > 0)
        {
            // Use Perlin noise to generate smooth, organic shake
            float x = (Mathf.PerlinNoise(shakeSeed, Time.time * currentRoughness) * 2f - 1f) * currentShakeMagnitude;
            float y = (Mathf.PerlinNoise(shakeSeed + 10f, Time.time * currentRoughness) * 2f - 1f) * currentShakeMagnitude;
            float z = (Mathf.PerlinNoise(shakeSeed + 20f, Time.time * currentRoughness) * 2f - 1f) * currentShakeMagnitude;

            transform.localPosition = originalLocalPosition + new Vector3(x, y, z);

            // Decay the shake over time
            currentShakeDuration -= Time.deltaTime * dampingSpeed;
            currentShakeMagnitude = Mathf.Lerp(currentShakeMagnitude, 0f, Time.deltaTime * dampingSpeed);
        }
        else
        {
            currentShakeDuration = 0f;
            currentShakeMagnitude = 0f;
            transform.localPosition = originalLocalPosition;
        }
    }

    /// <summary>
    /// Triggers a camera shake based on impact velocity.
    /// </summary>
    public void ShakeFromImpact(float impactVelocity)
    {
        // Ignore impacts below the minimum threshold
        if (impactVelocity < minShakeVelocity) return;

        // Map impact velocity to a 0-1 intensity factor
        float t = Mathf.Clamp01((impactVelocity - minShakeVelocity) / (maxShakeVelocity - minShakeVelocity));

        float duration = Mathf.Lerp(0.1f, maxShakeDuration, t);
        float magnitude = Mathf.Lerp(0.05f, maxShakeMagnitude, t);
        float roughness = Mathf.Lerp(4f, maxRoughness, t);

        TriggerShake(duration, magnitude, roughness);
    }

    /// <summary>
    /// Triggers a camera shake with explicit parameters.
    /// </summary>
    public void TriggerShake(float duration, float magnitude, float roughness)
    {
        currentShakeDuration = Mathf.Max(currentShakeDuration, duration);
        currentShakeMagnitude = Mathf.Max(currentShakeMagnitude, magnitude);
        currentRoughness = Mathf.Max(currentRoughness, roughness);

        // Generate a new random seed for variety
        shakeSeed = Random.Range(0f, 1000f);
    }
}