using UnityEngine;

/// <summary>
/// Makes a cave's mouth skin render with the SAME material instance as the
/// planet's own terrain, so the sinkhole is literally moon surface: same
/// shader, same colours, same normal maps, same per-body uniforms
/// (heightMinMax, bodyScale) that the generator sets on its runtime material
/// instance. The skin mesh is authored in the generator's unit-sphere space
/// and its transform reproduces the generator's, so every input the terrain
/// shader reads (vertPos, terrainData in UV0) lines up with the terrain next
/// to it.
///
/// The terrain's material instance only exists once the generator has run its
/// Start, so this polls until the "Terrain Mesh" renderer under the body has
/// a material, copies it, and switches itself off. Read-only use of the
/// generator (trap #2): nothing in it is modified.
/// </summary>
public class MoonSkinMaterialSync : MonoBehaviour
{
    Renderer _mine;
    CelestialBody _body;
    float _giveUpAt;

    void Awake()
    {
        _mine = GetComponent<Renderer>();
        _body = GetComponentInParent<CelestialBody>();
        _giveUpAt = Time.time + 30f;
    }

    void Update()
    {
        if (_mine == null || _body == null) { enabled = false; return; }
        var gen = _body.GetComponentInChildren<CelestialBodyGenerator>();
        Transform terrain = gen != null ? gen.transform.Find("Terrain Mesh") : null;
        var r = terrain != null ? terrain.GetComponent<Renderer>() : null;
        if (r != null && r.sharedMaterial != null)
        {
            _mine.sharedMaterial = r.sharedMaterial;
            enabled = false;
            return;
        }
        if (Time.time > _giveUpAt)
        {
            Debug.LogWarning($"[MoonSkinMaterialSync] '{name}': never found the terrain material under '{_body.bodyName}' — the mouth keeps the asset material.", this);
            enabled = false;
        }
    }
}
