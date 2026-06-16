using UnityEngine;

// Full-screen "groggy waking" post effect (blur + double vision) for the Mission
// 1 cold open. IntroSequenceController adds this to the gameplay camera at the
// start of the wake-up and removes it at handoff, driving `intensity` from 1
// (fully groggy) down to 0 (sharp) as the player comes to.
//
// Deliberately isolated from the planet/atmosphere post-processing (CLAUDE.md
// trap #2): it owns nothing but its own OnRenderImage, runs LAST in the camera's
// effect chain (it is added at runtime, so it appends after CustomPostProcessing),
// and only ever post-processes the already-final image. When intensity is 0 or
// the material is missing it is a pass-through blit.
[DisallowMultipleComponent]
public class GrogginessImageEffect : MonoBehaviour
{
    public Material material;
    [Range(0f, 1f)] public float intensity;

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (material == null || intensity <= 0.0001f)
        {
            Graphics.Blit(src, dst);
            return;
        }
        material.SetFloat("_Intensity", intensity);
        Graphics.Blit(src, dst, material);
    }
}
