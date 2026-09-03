using UnityEngine;

/// <summary>
/// The Unity side of the species table: tint conversion and model lookup.
///
/// <see cref="FishingRules"/> stores tints as raw RGB bytes so it can stay free
/// of UnityEngine and be executed headlessly by verify-fishing.py. This is the
/// one place that turns those bytes into a Color.
///
/// <b>Model reality check.</b> The handoff assumed 3 models per tier (9 total).
/// The project has THREE MODELS TOTAL, one per tier
/// (FishingdexManager.commonFishPrefab / uncommonFishPrefab / rareFishPrefab,
/// Floreswa fish01-03). Sam's call on 2026-09-01 was tint-only: the four species
/// in a tier share their tier's model and are told apart by colour, name and
/// size. modelIndex is still authored per species and still routed through this
/// lookup, so dropping in real extra shapes later is an asset assignment with
/// no code change.
/// </summary>
public static class FishSpeciesVisuals
{
    public static Color TintOf(int speciesIndex)
    {
        if (speciesIndex < 0 || speciesIndex >= FishingRules.Species.Length) return Color.white;
        var s = FishingRules.Species[speciesIndex];
        return new Color32(s.tintR, s.tintG, s.tintB, 0xFF);
    }

    public static Color TintOf(FishEntry entry) =>
        entry == null ? Color.white : TintOf(entry.ResolveSpecies());

    static Shader _standard;

    /// <summary>
    /// Tint every renderer of a fish instance. The Floreswa fish material
    /// points at the URP "Lit" shader and this project is Built-in RP, so the
    /// shader is MISSING: every fish rendered with the error shader, which
    /// ignores material.color -- the reason all species (and the red
    /// GRULABU) looked identical (Sam, 2026-09-03). Any unsupported/missing
    /// shader is swapped for Standard here before the colour is applied, on
    /// every submesh material.
    /// </summary>
    public static void Tint(GameObject fish, Color tint) => Tint(fish, tint, 1f);

    /// <summary>
    /// Shift every part's OWN colour toward the species tint by <paramref name="blend"/>
    /// (0 = untouched, 1 = flat tint). The fish prefabs are several coloured
    /// parts and some carry patterns; the tint rides on top of those instead
    /// of painting the whole model one flat colour (Sam, 2026-09-03).
    /// </summary>
    public static void Tint(GameObject fish, Color tint, float blend)
    {
        if (fish == null) return;
        foreach (var r in fish.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.materials;   // instanced copies, all submeshes
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                var sh = m.shader;
                if (sh == null || !sh.isSupported || sh.name.Contains("InternalError"))
                {
                    if (_standard == null) _standard = Shader.Find("Standard");
                    if (_standard != null) m.shader = _standard;
                }
                Color original = m.HasProperty("_Color") ? m.color : Color.white;
                // Keep the part's brightness and pattern, take the tint's hue:
                // blend toward the tint, then restore the original luminance so
                // a dark belly stays dark and a pale fin stays pale.
                Color mixed = Color.Lerp(original, tint, blend);
                float lumO = 0.299f * original.r + 0.587f * original.g + 0.114f * original.b;
                float lumM = 0.299f * mixed.r + 0.587f * mixed.g + 0.114f * mixed.b;
                if (lumM > 1e-4f) mixed *= Mathf.Clamp(lumO / lumM, 0.55f, 1.8f);
                mixed.a = original.a;
                m.color = mixed;
            }
        }
    }

    // ── Rarity glow ───────────────────────────────────────────────────────
    // Uncommon: a subtle self-glow. Rare: a strong one (the post stack's bloom
    // turns emission above 1 into a visible halo). Bounty: brighter still.
    // Applied on the line, in the hand and in the hotbar/dex preview through
    // the same call, so the three views agree. A small point light adds the
    // "radiating" read in the world (not in the preview stage).

    public static float EmissionFor(int speciesIndex)
    {
        if (speciesIndex < 0 || speciesIndex >= FishingRules.Species.Length) return 0f;
        var sp = FishingRules.Species[speciesIndex];
        if (sp.bounty) return 2.6f;
        return sp.tier == FishTier.Rare ? 1.8f : sp.tier == FishTier.Uncommon ? 0.55f : 0f;
    }

    /// <summary>
    /// The whole species look: tint blended onto the model's own parts, plus
    /// the rarity glow (emission, and optionally a point light for the world).
    /// </summary>
    public static void ApplySpeciesLook(GameObject fish, int speciesIndex, bool worldLight)
    {
        if (fish == null) return;
        Color tint = TintOf(speciesIndex);
        bool bounty = speciesIndex >= 0 && speciesIndex < FishingRules.Species.Length
                      && FishingRules.Species[speciesIndex].bounty;
        Tint(fish, tint, bounty ? 0.8f : 0.55f);

        float glow = EmissionFor(speciesIndex);
        foreach (var r in fish.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || !m.HasProperty("_EmissionColor")) continue;
                if (glow > 0f)
                {
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    m.SetColor("_EmissionColor", tint * glow);
                }
                else
                {
                    m.DisableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", Color.black);
                }
            }
        }

        var existing = fish.transform.Find("RarityGlow");
        if (existing != null) Object.Destroy(existing.gameObject);
        if (worldLight && glow > 0f)
        {
            var lightGO = new GameObject("RarityGlow");
            lightGO.transform.SetParent(fish.transform, false);
            lightGO.transform.localPosition = Vector3.zero;
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = tint;
            light.intensity = bounty ? 2.6f : glow >= 1f ? 1.6f : 0.6f;
            light.range = bounty ? 6f : glow >= 1f ? 4f : 2.5f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
        }
    }

    /// <summary>
    /// The prefab for a species. Falls back to the tier model whenever the
    /// requested modelIndex has no asset behind it — which today is every index
    /// above 0, and is exactly why this returns a sensible fish instead of null.
    /// </summary>
    public static GameObject PrefabFor(int speciesIndex, FishingdexManager dex)
    {
        if (dex == null) return null;
        if (speciesIndex < 0 || speciesIndex >= FishingRules.Species.Length) return null;
        return dex.PrefabForTier(FishingRules.Species[speciesIndex].tier);
    }
}
