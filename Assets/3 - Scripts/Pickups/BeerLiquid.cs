using UnityEngine;

/// <summary>
/// The beer inside a cup: a short amber cylinder plus a cream foam disc, built
/// from primitives and parented inside the cup mesh. <see cref="SetFill"/> (0-1)
/// shrinks the column from the top so the cup visibly empties as you drink.
///
/// Attached by <see cref="BeerCupArt.AttachLiquid"/> — the same visual is used
/// on the counter and in the hand, so a half-drunk cup looks half-drunk in both.
///
/// The cup's interior was measured from the FantasyVillage Cup mesh (axis at
/// x≈0.005, floor at y≈0.035, liquid surface when full at y≈0.19, inner wall
/// r≈0.07). The knobs on <see cref="BeerCupController"/> feed these numbers in;
/// nothing here is hard-wired to that one mesh.
/// </summary>
public class BeerLiquid : MonoBehaviour
{
    Transform _beer, _foam;
    Vector3 _axisLocal;      // cup-local centre of the column at the floor
    float _radius, _floorY, _fullY, _foamThickness;
    float _fill = 1f;

    public float Fill => _fill;

    internal void Build(Vector3 axisLocal, float radius, float floorY, float fullY, float foamThickness)
    {
        _axisLocal = axisLocal;
        _radius = radius;
        _floorY = floorY;
        _fullY = fullY;
        _foamThickness = foamThickness;

        _beer = MakeCylinder("Beer", BeerCupArt.BeerMat);
        _foam = MakeCylinder("Foam", BeerCupArt.FoamMat);
        SetFill(_fill);
    }

    Transform MakeCylinder(string n, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = n;
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.transform.SetParent(transform, false);
        go.layer = gameObject.layer;
        return go.transform;
    }

    /// <summary>0 = empty (nothing drawn), 1 = full to the measured surface.</summary>
    public void SetFill(float fill01)
    {
        _fill = Mathf.Clamp01(fill01);
        if (_beer == null) return;

        bool any = _fill > 0.005f;
        _beer.gameObject.SetActive(any);
        _foam.gameObject.SetActive(any);
        if (!any) return;

        float height = (_fullY - _floorY) * _fill;
        // Unity's cylinder primitive is 2 units tall (y = -1..1) and 1 wide.
        _beer.localPosition = _axisLocal + new Vector3(0f, _floorY + height * 0.5f, 0f);
        _beer.localScale = new Vector3(_radius * 2f, height * 0.5f, _radius * 2f);

        float foamH = Mathf.Min(_foamThickness, height);
        _foam.localPosition = _axisLocal + new Vector3(0f, _floorY + height - foamH * 0.5f, 0f);
        _foam.localScale = new Vector3(_radius * 2.02f, foamH * 0.5f, _radius * 2.02f);
    }
}
