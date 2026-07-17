using UnityEngine;

// Full-screen underwater wash — murky tint, edge vignette, gentle refraction wobble +
// faint caustics. Driven by PoolFlood (intensity 0 above the surface -> 1 when the
// camera is submerged).
//
// Mirrors BlackHoleVignetteEffect: runtime-added so it appends AFTER the post-process
// stack (only ever post-processing the already-final image), is a pass-through blit
// when intensity is 0 or the material is missing, and self-fades + destroys itself
// once PoolFlood stops driving it (so it never lingers if the camera persists).
[DisallowMultipleComponent]
public class UnderwaterImageEffect : MonoBehaviour
{
    public Material material;
    [Range(0f, 1f)] public float intensity;

    float _lastDriven = -999f;
    // Called each frame PoolFlood drives this effect. When driving stops (you surface,
    // or the scene changes), Update eases the wash out and self-destructs.
    public void MarkDriven() { _lastDriven = Time.unscaledTime; }

    void Update()
    {
        if (Time.unscaledTime - _lastDriven <= 0.15f) return;   // still being driven → leave it
        intensity = Mathf.MoveTowards(intensity, 0f, Time.unscaledDeltaTime / 0.4f);
        if (intensity <= 0.0005f) Destroy(this);
    }

    static readonly int _IntensityID = Shader.PropertyToID("_Intensity");

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (material == null || intensity <= 0.0001f)
        {
            Graphics.Blit(src, dst);
            return;
        }
        material.SetFloat(_IntensityID, intensity);
        Graphics.Blit(src, dst, material);
    }
}
