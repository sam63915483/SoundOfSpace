using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws a texture as a perspective-warped quad: a square→quad homography
/// (Heckbert) maps the unit UV square onto four corner points given as
/// fractions of this Graphic's rect. The mesh is a subdivided grid so the
/// per-triangle affine texture interpolation approximates the projective
/// mapping (content genuinely foreshortens toward the quad's far edge —
/// something no RectTransform rotation can do on an overlay canvas, which
/// renders orthographically). Used to seat the HUD cluster RenderTextures
/// onto the helmet art's painted screens, whose edges converge toward a
/// vanishing point. The Graphic stretches over the whole canvas so corner
/// fractions track the stretched art at any aspect ratio automatically.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]   // Graphic's own RequireComponent doesn't inherit — without this the warp silently never renders
public class HousingScreenWarp : MaskableGraphic
{
    Texture _tex;
    Vector2 _bl, _br, _tr, _tl;   // canvas fractions (0..1)
    bool _hasQuad;
    const int Grid = 12;          // 12×12 cells ≈ invisible projective error

    public override Texture mainTexture => _tex != null ? _tex : s_WhiteTexture;

    public void SetTexture(Texture tex)
    {
        _tex = tex;
        SetMaterialDirty();
        SetVerticesDirty();
    }

    public void SetQuad(Vector2 blFrac, Vector2 brFrac, Vector2 trFrac, Vector2 tlFrac)
    {
        _bl = blFrac; _br = brFrac; _tr = trFrac; _tl = tlFrac;
        _hasQuad = true;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (!_hasQuad || _tex == null) return;

        Rect r = rectTransform.rect;
        Vector2 F(Vector2 f) => new Vector2(r.xMin + f.x * r.width, r.yMin + f.y * r.height);
        Vector2 p00 = F(_bl), p10 = F(_br), p01 = F(_tl), p11 = F(_tr);

        // Square→quad homography: x(u,v) = (a·u + b·v + c) / (g·u + h·v + 1),
        // y likewise with d,e,f. Solved from the four corner correspondences.
        Vector2 d1 = p10 - p11;
        Vector2 d2 = p01 - p11;
        Vector2 sum = p00 - p10 - p01 + p11;
        float den = d1.x * d2.y - d1.y * d2.x;
        float g = 0f, h = 0f;
        if (Mathf.Abs(den) > 1e-6f)
        {
            g = (sum.x * d2.y - sum.y * d2.x) / den;
            h = (d1.x * sum.y - d1.y * sum.x) / den;
        }
        float a = p10.x - p00.x + g * p10.x;
        float b = p01.x - p00.x + h * p01.x;
        float c = p00.x;
        float d = p10.y - p00.y + g * p10.y;
        float e = p01.y - p00.y + h * p01.y;
        float f = p00.y;

        Color32 col = color;
        for (int j = 0; j <= Grid; j++)
        {
            float v = j / (float)Grid;
            for (int i = 0; i <= Grid; i++)
            {
                float u = i / (float)Grid;
                float w = g * u + h * v + 1f;
                Vector3 pos = new Vector3((a * u + b * v + c) / w, (d * u + e * v + f) / w, 0f);
                vh.AddVert(pos, col, new Vector4(u, v, 0f, 0f));
            }
        }
        int stride = Grid + 1;
        for (int j = 0; j < Grid; j++)
            for (int i = 0; i < Grid; i++)
            {
                int i0 = j * stride + i;
                vh.AddTriangle(i0, i0 + stride, i0 + stride + 1);
                vh.AddTriangle(i0, i0 + stride + 1, i0 + 1);
            }
    }
}
