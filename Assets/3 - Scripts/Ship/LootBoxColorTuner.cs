using UnityEngine;

// Live-tuning helper for the SHIP44 loot box (or any prop that uses the
// Standard shader). Attach to the loot box GameObject in the SHIP44
// prefab; the inspector exposes albedo tint, emission tint, and an HDR
// emission multiplier — adjust them in edit mode or play mode until the
// colours match the ship interior, then either leave the component on
// for runtime override or copy the values back into the .mat asset.
//
// Uses MaterialPropertyBlock so the shared material asset is never
// mutated — overrides are scoped to this renderer only.
[ExecuteAlways]
[DisallowMultipleComponent]
public class LootBoxColorTuner : MonoBehaviour
{
    [Tooltip("Albedo tint. Multiplies into the base texture under direct light. White = show the texture's natural colours; warmer = bias warm.")]
    [ColorUsage(false, false)]
    public Color baseTint = new Color(0.85f, 0.55f, 0.3f, 1f);

    [Tooltip("Emission tint. Multiplies into the emission map (bound to the base texture) for the self-lit glow. The hue here drives what colour the box reads as in scenes with no ambient.")]
    [ColorUsage(false, true)]
    public Color emissionTint = new Color(1.0f, 0.6f, 0.25f, 1f);

    [Tooltip("HDR multiplier on the emission tint. 0 = no glow, 1 = LDR brightness, > 1 = bloom kicks in. Tune alongside the bloom post-process.")]
    [Range(0f, 5f)]
    public float emissionBoost = 1f;

    [Tooltip("Apply to this MeshRenderer. Auto-resolved from this GameObject or its children if left empty.")]
    public MeshRenderer targetRenderer;

    MaterialPropertyBlock _mpb;
    static readonly int ColorID         = Shader.PropertyToID("_Color");
    static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    void OnEnable()
    {
        ResolveRenderer();
        Apply();
    }

    void OnValidate()
    {
        // Fires when inspector values change — gives instant feedback in
        // edit mode without needing to drop into play mode.
        ResolveRenderer();
        Apply();
    }

    void Update()
    {
        // Re-apply every frame in case something else (e.g. ReactorGlow on
        // a different prop, or a save-system pass) clears the property
        // block between frames.
        Apply();
    }

    void ResolveRenderer()
    {
        if (targetRenderer != null) return;
        targetRenderer = GetComponent<MeshRenderer>();
        if (targetRenderer == null) targetRenderer = GetComponentInChildren<MeshRenderer>(true);
    }

    void Apply()
    {
        if (targetRenderer == null) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        targetRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(ColorID, baseTint);
        _mpb.SetColor(EmissionColorID, emissionTint * emissionBoost);
        targetRenderer.SetPropertyBlock(_mpb);
    }

    [ContextMenu("Log current values")]
    void LogValues()
    {
        Color emissionFinal = emissionTint * emissionBoost;
        Debug.Log(
            $"[LootBoxColorTuner] baseTint = ({baseTint.r:F2}, {baseTint.g:F2}, {baseTint.b:F2}), " +
            $"emissionTint = ({emissionTint.r:F2}, {emissionTint.g:F2}, {emissionTint.b:F2}), " +
            $"emissionBoost = {emissionBoost:F2}  =>  _EmissionColor = ({emissionFinal.r:F2}, {emissionFinal.g:F2}, {emissionFinal.b:F2})",
            this);
    }
}
