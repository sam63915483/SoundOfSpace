using UnityEngine;

/// <summary>Perlin-noise intensity flicker for the D1 fluorescent hum-light.</summary>
public class FlickerLight : MonoBehaviour
{
    Light _light;
    float _baseIntensity;
    float _seed;

    void Awake()
    {
        _light = GetComponent<Light>();
        _baseIntensity = _light != null ? _light.intensity : 1f;
        _seed = Random.value * 100f;
    }

    void Update()
    {
        if (_light == null) return;
        float n = Mathf.PerlinNoise(_seed, Time.time * flickerSpeed);       // 0..1 wander
        float drop = n < dropThreshold ? dropAmount : 0f;                   // occasional hard dip
        _light.intensity = _baseIntensity * (1f - flickerDepth * n - drop);
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Tooltip("How fast the flicker noise scrolls.")]
    public float flickerSpeed = 8f;
    [Tooltip("Fraction of base intensity the smooth noise can remove (0-1).")]
    [Range(0f, 1f)] public float flickerDepth = 0.25f;
    [Tooltip("Noise below this triggers a hard fluorescent dip.")]
    [Range(0f, 1f)] public float dropThreshold = 0.12f;
    [Tooltip("Extra intensity fraction removed during a hard dip.")]
    [Range(0f, 1f)] public float dropAmount = 0.5f;
}
