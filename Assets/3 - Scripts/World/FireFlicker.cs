using UnityEngine;

// Cheap, deterministic flicker for a single fire light. Perlin-noise driven so it
// looks like flame rather than a strobe. One light only — the spawn area is
// draw/shadow bound, so the fire glow is faked with additive particles plus this
// single shadowless point light, never a swarm.
[RequireComponent(typeof(Light))]
public class FireFlicker : MonoBehaviour {

    public float baseIntensity = 2f;
    public float amplitude = 0.7f;
    public float speed = 7f;

    Light _light;
    float _seed;

    void Awake () {
        _light = GetComponent<Light> ();
        // Stable per-instance offset so multiple fires don't flicker in lockstep.
        _seed = (transform.position.x * 7.31f) + (transform.position.z * 3.17f) + 13.7f;
    }

    void Update () {
        if (_light == null) return;
        float n = Mathf.PerlinNoise (_seed, Time.time * speed);
        _light.intensity = Mathf.Max (0f, baseIntensity + (n - 0.5f) * 2f * amplitude);
    }
}
