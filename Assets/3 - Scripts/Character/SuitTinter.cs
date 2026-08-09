using UnityEngine;

/// <summary>
/// Recolours an astronaut's suit — the local player, a remote puppet, or the
/// menu preview — without ever touching a material asset.
///
/// ── Why MaterialPropertyBlock ────────────────────────────────────────────
/// Astronaut.fbx resolves its materials by legacy name-search
/// (`materialLocation: 1`, `externalObjects: {}`), so every astronaut in the
/// game shares the SAME two material assets on disk: `Suit.mat` and
/// `Suit Dark.mat`. Two consequences:
///
///   1. Writing `renderer.sharedMaterial.color` would edit the asset itself —
///      permanently, on disk, for every astronaut, and in the Editor it survives
///      exiting play mode. That is how you lose an afternoon.
///   2. Writing `renderer.material.color` would silently instantiate a material
///      per player, breaking batching and leaking on scene reload.
///
/// A MaterialPropertyBlock overrides the colour per-renderer, per-slot, with no
/// instantiation and nothing written back to the asset.
///
/// ── Why the visor is safe ────────────────────────────────────────────────
/// The model has two material slots. The suit shell is `Suit` (Standard shader,
/// _Color 0.8 grey, NO texture — which is why a flat tint reads cleanly). The
/// visor is `Suit Dark` (0.1376 grey). We only ever write to slots whose
/// material is the suit, so "the visor is never tintable" costs no special case
/// — it simply never gets touched.
/// </summary>
public static class SuitTinter
{
    /// Standard shader's main colour property.
    static readonly int ColorId = Shader.PropertyToID("_Color");

    /// The material name that takes the tint. Matched case-insensitively and by
    /// prefix, because `Suit Dark` must NOT match — hence the explicit exclusion
    /// below rather than a bare StartsWith.
    const string SuitMaterialName = "Suit";
    const string VisorMaterialName = "Suit Dark";

    /// Reused so tinting never allocates. MaterialPropertyBlock is a managed
    /// wrapper over native memory; one shared instance is the documented pattern.
    static MaterialPropertyBlock _block;

    /// Tints every suit slot under `root`. Safe to call on a hierarchy with no
    /// astronaut in it (does nothing), on disabled renderers, and repeatedly.
    public static void Apply(Transform root, int swatchIndex)
    {
        if (root == null) return;
        Apply(root, SuitPalette.ColorAt(swatchIndex));
    }

    public static void Apply(Transform root, Color color)
    {
        if (root == null) return;
        if (_block == null) _block = new MaterialPropertyBlock();

        // `true` includes inactive children — puppets spend their first frames
        // with renderers disabled (PlanetRelativeSync hides them until a valid
        // pose arrives), and they must already be the right colour when shown.
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;

            var mats = r.sharedMaterials;
            for (int slot = 0; slot < mats.Length; slot++)
            {
                if (!IsSuitMaterial(mats[slot])) continue;

                // Per-slot overload: slot 1 (the visor) keeps its own block,
                // which we never write, so it stays exactly as authored.
                r.GetPropertyBlock(_block, slot);
                _block.SetColor(ColorId, color);
                r.SetPropertyBlock(_block, slot);
            }
        }
    }

    /// True for the suit shell, false for the visor and for anything else
    /// (the nametag's font material, for one — it lives under the same root).
    static bool IsSuitMaterial(Material m)
    {
        if (m == null) return false;
        string n = m.name;
        if (string.IsNullOrEmpty(n)) return false;

        // Unity appends " (Instance)" to runtime-instanced materials, so compare
        // by prefix rather than equality.
        if (n.StartsWith(VisorMaterialName, System.StringComparison.OrdinalIgnoreCase))
            return false;
        return n.StartsWith(SuitMaterialName, System.StringComparison.OrdinalIgnoreCase);
    }
}
