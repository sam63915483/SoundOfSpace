using UnityEngine;

// Holds a torch's Light at a constant intensity, regardless of whatever else
// is animating it (Animator, particle Lights Module, manually-keyed clips).
// Runs in LateUpdate so it overrides anything that wrote to the intensity
// earlier in the frame.
//
// Optional subtle flicker: leave flickerAmplitude at 0 for a perfectly steady
// light. Bump to ~0.05–0.15 for a small "torch breathing" effect — each torch
// instance picks its own random phase on Awake, so multiple torches naturally
// desync instead of pulsing in unison.
[RequireComponent(typeof(Light))]
public class TorchLightSteady : MonoBehaviour
{
    [Tooltip("Target intensity to hold the light at. If <= 0, captures the light's current intensity on Awake.")]
    public float targetIntensity = 0f;

    [Tooltip("0 = perfectly steady. ~0.08 = subtle breathe. ~0.2 = visible flicker. The intensity is multiplied by (1 + flickerAmplitude * sin-noise).")]
    [Range(0f, 0.5f)] public float flickerAmplitude = 0f;

    [Tooltip("How fast the flicker oscillates. Higher = twitchier.")]
    public float flickerSpeed = 4f;

    [Tooltip("Torch light on the GPU-instanced grass. Instanced grass never receives Unity's additive point lights, so a torch lights the ground but NOT the grass unless it carries a GrassPointLight marker (lanterns already do). This auto-adds that marker so torches light grass too. 0.5 matches the lanterns; lower for subtler, 0 to leave grass unlit by this torch.")]
    [Range(0f, 2f)] public float grassStrength = 0.5f;

    Light _light;
    float _baseIntensity;
    float _phaseOffset;

    void Awake()
    {
        _light = GetComponent<Light>();
        _baseIntensity = targetIntensity > 0f ? targetIntensity : _light.intensity;
        // Per-instance phase so multiple torches don't pulse in lockstep.
        _phaseOffset = Random.Range(0f, Mathf.PI * 2f);

        // Make this torch illuminate the instanced grass like the lanterns do.
        // Instanced grass never gets Unity's additive point lights, so it needs the
        // GrassPointLight marker (InstancedGrassRenderer reads those). Lanterns were
        // marked; torches weren't — so torches lit the ground but not the grass.
        if (grassStrength > 0f)
        {
            var gpl = GetComponent<GrassPointLight>();
            if (gpl == null) gpl = gameObject.AddComponent<GrassPointLight>();
            gpl.grassStrength = grassStrength;
        }
    }

    void LateUpdate()
    {
        if (_light == null) return;

        // Make sure the light stays on — anything that disabled it gets undone.
        if (!_light.enabled) _light.enabled = true;

        if (flickerAmplitude <= 0f)
        {
            _light.intensity = _baseIntensity;
            return;
        }

        // Two overlapping sine waves (different frequencies + phases) gives a
        // more organic, less "obvious sine" feel than a single oscillator.
        float t = Time.time * flickerSpeed + _phaseOffset;
        float wobble = (Mathf.Sin(t) + Mathf.Sin(t * 1.7f + 0.6f)) * 0.5f;
        _light.intensity = _baseIntensity * (1f + flickerAmplitude * wobble);
    }
}
