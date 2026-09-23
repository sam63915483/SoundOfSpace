using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes a whole cave render as the planet's own surface. Sits on the cave
/// root. When the planet's terrain has its runtime material instance (the
/// generator sets heightMinMax / bodyScale / biome values on it in Start), this
/// builds ONE material for the cave: our Custom/CaveMoonRock shader (MoonA plus
/// darkness) with every property copied from that live terrain material, and
/// assigns it to every renderer under the cave that carries the template
/// material. Same colours, normal maps, noise and per-body uniforms as the
/// ground next to the mouth — the seam is the same material on both sides.
///
/// The cave meshes are authored in the generator's unit-sphere space under a
/// transform that reproduces the generator's, so every input the terrain
/// shader reads lines up. Read-only use of the generator (trap #2).
/// </summary>
public class MoonSkinMaterialSync : MonoBehaviour
{
    [Tooltip("The cave's material asset (Custom/CaveMoonRock). Renderers using it get the synced copy.")]
    public Material template;

    CelestialBody _body;
    float _giveUpAt;
    Material _synced;

    void Awake()
    {
        _body = GetComponentInParent<CelestialBody>();
        _giveUpAt = Time.time + 30f;
    }

    void Update()
    {
        if (_body == null) { enabled = false; return; }
        var gen = _body.GetComponentInChildren<CelestialBodyGenerator>();
        Transform terrain = gen != null ? gen.transform.Find("Terrain Mesh") : null;
        var r = terrain != null ? terrain.GetComponent<Renderer>() : null;
        if (r != null && r.sharedMaterial != null)
        {
            var moon = r.sharedMaterial;
            Shader ours = template != null ? template.shader : Shader.Find("Custom/CaveMoonRock");
            if (ours == null) { enabled = false; return; }
            _synced = new Material(ours) { name = "CaveMoonRock (synced)" };
            _synced.CopyPropertiesFromMaterial(moon);        // everything with a matching name
            _synced.shaderKeywords = moon.shaderKeywords;     // _USEEJECTA_ON etc.
            if (template != null)
            {
                // Keep our own cave knobs (the moon has none of these).
                foreach (var p in new[] { "_ExposureFloor", "_ExposurePower", "_CraterFadeStart", "_CraterFadeEnd" })
                    if (template.HasProperty(p)) _synced.SetFloat(p, template.GetFloat(p));
            }
            var rs = new List<Renderer>();
            GetComponentsInChildren(true, rs);
            int n = 0;
            foreach (var rr in rs)
            {
                if (rr == null) continue;
                if (template == null || rr.sharedMaterial == template || (rr.sharedMaterial != null && rr.sharedMaterial.shader == ours))
                {
                    rr.sharedMaterial = _synced; n++;
                }
            }
            enabled = false;
            return;
        }
        if (Time.time > _giveUpAt)
        {
            Debug.LogWarning($"[MoonSkinMaterialSync] '{name}': never found the terrain material under '{_body.bodyName}' — the cave keeps its asset material.", this);
            enabled = false;
        }
    }

    void OnDestroy()
    {
        if (_synced != null) Destroy(_synced);
    }
}
