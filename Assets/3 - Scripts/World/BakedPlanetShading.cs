using UnityEngine;

/// <summary>
/// Re-applies the three Earth-terrain shader uniforms that are NOT declared shader
/// Properties — <c>heightMinMax</c>, <c>oceanLevel</c>, <c>bodyScale</c>. The runtime
/// generator sets these via Material.SetVector/SetFloat each play; on a BAKED terrain
/// prefab they can't live in the material asset (Unity strips undeclared properties on
/// serialize), so without this the height remap collapses and the whole planet renders
/// as steep rock with no flat grass zones.
///
/// Values are captured at bake time (heightMinMax = min/max vertex length of the baked
/// mesh; oceanLevel/bodyScale from the body settings) and stored as plain serialized
/// fields here, which DO persist. Applied in edit mode too so the prefab looks right in
/// the Scene view.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class BakedPlanetShading : MonoBehaviour
{
    [Tooltip("Min/max terrain height in object space (= min/max vertex distance from centre). Drives the flat-grass vs steep-rock split.")]
    public Vector2 heightMinMax = new Vector2(1f, 1f);
    [Range(0f, 1f)]
    [Tooltip("Sea level (0..1). Below this height is shaded as shore/underwater.")]
    public float oceanLevel = 0f;
    [Tooltip("World radius of the body — used by the shader's distance fresnel only.")]
    public float bodyScale = 1f;

    static readonly int _heightMinMax = Shader.PropertyToID("heightMinMax");
    static readonly int _oceanLevel   = Shader.PropertyToID("oceanLevel");
    static readonly int _bodyScale    = Shader.PropertyToID("bodyScale");

    void OnEnable()   { Apply(); }
    void OnValidate() { Apply(); }

    void Apply()
    {
        var r = GetComponent<MeshRenderer>();
        if (r == null || r.sharedMaterial == null) return;
        var m = r.sharedMaterial;   // dedicated to the baked planet — safe to set on
        m.SetVector(_heightMinMax, new Vector4(heightMinMax.x, heightMinMax.y, 0f, 0f));
        m.SetFloat(_oceanLevel, oceanLevel);
        m.SetFloat(_bodyScale, bodyScale);
    }
}
