using UnityEngine;

/// <summary>
/// A subtle pulsing blue glow on a cave crystal: the renderer's emission
/// breathes via a MaterialPropertyBlock (no material instances), and an
/// optional small point light breathes with it. CaveCrystalSeeder adds one to
/// every cave crystal and gives only every Nth one a light — a light per
/// crystal would mean a forward-add pass over the whole cave mesh per light.
///
/// The material must have the _EMISSION keyword enabled or the emission colour
/// is ignored; the seeder assigns CaveCrystal_Glow.mat, which does.
/// </summary>
public class CrystalGlow : MonoBehaviour
{
    [Tooltip("Emission colour at full pulse.")]
    public Color glow = new Color(0.35f, 0.62f, 1f);
    [Tooltip("Emission multiplier at the bottom / top of the pulse.")]
    public float minGlow = 0.9f, maxGlow = 1.8f;
    [Tooltip("Pulses per second.")]
    public float speed = 0.45f;
    [Tooltip("Optional point light that breathes with the emission.")]
    public Light pulseLight;
    public float lightMin = 1.0f, lightMax = 1.9f;

    Renderer[] _renderers;
    MaterialPropertyBlock _block;
    float _phase;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        _block = new MaterialPropertyBlock();
        _phase = Random.value * Mathf.PI * 2f;
    }

    void Update()
    {
        float k = 0.5f + 0.5f * Mathf.Sin(Time.time * speed * Mathf.PI * 2f + _phase);
        Color e = glow * Mathf.Lerp(minGlow, maxGlow, k);
        for (int i = 0; i < _renderers.Length; i++)
        {
            var r = _renderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(_block);
            _block.SetColor(EmissionId, e);
            r.SetPropertyBlock(_block);
        }
        if (pulseLight != null) pulseLight.intensity = Mathf.Lerp(lightMin, lightMax, k);
    }
}
