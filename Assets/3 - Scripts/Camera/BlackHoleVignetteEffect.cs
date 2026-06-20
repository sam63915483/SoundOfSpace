using UnityEngine;

// Full-screen "entering a black hole" vignette — a swirling, chromatic, spectral
// band that grows in from the screen edges and darkens toward the rim. Driven by
// BlackHoleCapture (intensity 0 at the vignette radius → 1 at the core).
//
// Mirrors GrogginessImageEffect: owns nothing but its own OnRenderImage, is added
// at runtime so it appends AFTER CustomPostProcessing (only ever post-processing
// the already-final image), and is a pass-through blit when intensity is 0 or the
// material is missing. Kept a distinct type from GrogginessImageEffect so both can
// coexist on the camera (each is DisallowMultipleComponent).
[DisallowMultipleComponent]
public class BlackHoleVignetteEffect : MonoBehaviour
{
    public Material material;
    [Range(0f, 1f)] public float intensity;
    // Black-hole core position in screen UV (0..1). Set each frame by BlackHoleCapture
    // so the tesseract anchors to the actual hole on screen, not the screen centre.
    public Vector2 coreUV = new Vector2(0.5f, 0.5f);
    // Animation clock (accumulated by BlackHoleCapture; runs faster the closer you are).
    public float animTime;
    // Kaleidoscope warp gates (the mushroom-trip fold), driven by proximity. Warps the
    // whole effect — scene, swirl AND tesseract — together.
    public float kaleidoStrength;
    public float kaleidoWave;
    public float sway;   // woozy whole-frame camera-sway amount
    public float collapse;   // 0..1 rush-to-white singularity flash at the Backrooms crossing
    public float cockpitMask;   // 1 while piloting, 0 on foot (edge-fades kaleido off the hull, occludes tesseract behind it, +50% opacity)

    float _lastDriven = -999f;
    // Called each frame BlackHoleCapture drives this effect. When driving stops (after the
    // teleport, or backing out of the zone), Update fades everything out and self-destructs —
    // so on the far side of the teleport the new scene reveals smoothly FROM the white flash
    // instead of the overlay vanishing at once (or lingering if the camera persisted).
    public void MarkDriven() { _lastDriven = Time.unscaledTime; }

    void Update()
    {
        if (Time.unscaledTime - _lastDriven <= 0.12f) return;   // still being driven → leave it
        float k = Time.unscaledDeltaTime / 0.6f;
        // Warps + swirl + tesseract vanish quickly (hidden under the flash); the white flash
        // itself fades a touch slower, revealing the new scene cleanly.
        intensity       = Mathf.MoveTowards(intensity, 0f, k * 2f);
        kaleidoStrength = Mathf.MoveTowards(kaleidoStrength, 0f, k * 2f);
        kaleidoWave     = Mathf.MoveTowards(kaleidoWave, 0f, k * 2f);
        sway            = Mathf.MoveTowards(sway, 0f, k * 2f);
        collapse        = Mathf.MoveTowards(collapse, 0f, k);
        if (intensity <= 0.0005f && collapse <= 0.0005f) Destroy(this);
    }

    static readonly int _IntensityID = Shader.PropertyToID("_Intensity");
    static readonly int _CoreUVID = Shader.PropertyToID("_CoreUV");
    static readonly int _AnimTimeID = Shader.PropertyToID("_AnimTime");
    static readonly int _KaleidoStrengthID = Shader.PropertyToID("_KaleidoStrength");
    static readonly int _KaleidoWaveID = Shader.PropertyToID("_KaleidoWave");
    static readonly int _SwayID = Shader.PropertyToID("_Sway");
    static readonly int _CollapseID = Shader.PropertyToID("_Collapse");
    static readonly int _CockpitMaskID = Shader.PropertyToID("_CockpitMask");

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (material == null || (intensity <= 0.0001f && collapse <= 0.0001f))
        {
            Graphics.Blit(src, dst);
            return;
        }
        material.SetFloat(_IntensityID, intensity);
        material.SetVector(_CoreUVID, new Vector4(coreUV.x, coreUV.y, 0f, 0f));
        material.SetFloat(_AnimTimeID, animTime);
        material.SetFloat(_KaleidoStrengthID, kaleidoStrength);
        material.SetFloat(_KaleidoWaveID, kaleidoWave);
        material.SetFloat(_SwayID, sway);
        material.SetFloat(_CollapseID, collapse);
        material.SetFloat(_CockpitMaskID, cockpitMask);
        Graphics.Blit(src, dst, material);
    }
}
