using UnityEngine;

// Screen-space drug-trip effect appended to CustomPostProcessing.effects[]
// at runtime by RawFishTripController. When intensity == 0 it short-circuits
// and just blits source → destination, so leaving it permanently in the
// effects array costs ~one branch per frame.
public class KaleidoscopeTripEffect : PostProcessingEffect
{
    [System.NonSerialized] public float intensity;        // colour-shift gate (rarity-independent)
    [System.NonSerialized] public float kaleidoStrength;  // geometry gate (rarity-scaled)
    [System.NonSerialized] public float waveStrength;     // chill wavy shimmer gate
    [System.NonSerialized] public float tripTime;
    [System.NonSerialized] public Shader shader;

    Material _mat;

    public override void Render(RenderTexture source, RenderTexture destination)
    {
        // Skip the shader entirely if all effect components are silent.
        if ((intensity <= 0.0001f && kaleidoStrength <= 0.0001f && waveStrength <= 0.0001f) || shader == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        if (_mat == null)
        {
            _mat = new Material(shader);
            _mat.hideFlags = HideFlags.HideAndDontSave;
        }

        float aspect = source.width > 0 && source.height > 0
            ? (float)source.width / source.height
            : 16f / 9f;

        _mat.SetFloat("_Intensity", intensity);
        _mat.SetFloat("_KaleidoStrength", kaleidoStrength);
        _mat.SetFloat("_WaveStrength", waveStrength);
        _mat.SetFloat("_TripTime", tripTime);
        _mat.SetFloat("_Aspect", aspect);
        Graphics.Blit(source, destination, _mat);
    }
}
