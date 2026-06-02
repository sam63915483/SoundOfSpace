using UnityEngine;

/// <summary>
/// Real chromatic aberration. Samples R / G / B channels at different
/// radial offsets per pixel, producing colored fringing at screen
/// corners. Attached to the player camera at runtime by
/// <see cref="CameraEffectsManager"/>; runs as a non-opaque
/// <c>OnRenderImage</c>, after the atmosphere/planet/ocean post-process
/// chain (which is <c>[ImageEffectOpaque]</c>).
///
/// This replaces the previous UI-overlay-based fake (three offset colored
/// ring images), which only produced a faint darkening at the edges
/// rather than real channel-split fringing.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ChromaticAberrationEffect : MonoBehaviour
{
    // Peak shader _Strength value at full intensity (1.0). The shader's
    // offset scales with dist², so the actual UV offset at the screen
    // corner is ~MaxStrength × 0.5 (corner UV is ≈0.707 from center).
    const float MaxStrength = 0.05f;

    Material _material;

    void Awake()
    {
        var shader = Shader.Find("Hidden/ChromaticAberration");
        if (shader == null)
        {
            Debug.LogWarning("[ChromaticAberrationEffect] Shader 'Hidden/ChromaticAberration' not found. Effect will pass-through.");
            return;
        }
        _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    void OnDestroy()
    {
        if (_material != null) DestroyImmediate(_material);
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (_material == null) { Graphics.Blit(source, destination); return; }

        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled || mgr.Input == null || !mgr.Input.fxChromaticAberration)
        { Graphics.Blit(source, destination); return; }

        float intensity = mgr.Input.fxChromaticAberrationIntensity;
        if (intensity <= 0.001f) { Graphics.Blit(source, destination); return; }

        _material.SetFloat("_Strength", intensity * MaxStrength);
        Graphics.Blit(source, destination, _material);
    }
}
