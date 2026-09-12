using UnityEngine;

/// <summary>
/// Art helpers for the bar's beer cup: the shared beer/foam materials, the
/// liquid attach, and a drawn hotbar icon (a pixel tankard, like the grapple
/// gun's icon — no sprite asset to forget in a build).
///
/// Materials use the Standard shader, which hundreds of scene materials
/// reference, so it is never stripped from a build.
/// </summary>
public static class BeerCupArt
{
    static readonly Color Amber = new Color(0.86f, 0.55f, 0.12f);
    static readonly Color Cream = new Color(0.97f, 0.93f, 0.80f);

    static Material s_beer, s_foam;
    static Sprite s_icon, s_iconEmpty;

    public static Material BeerMat => Mat(ref s_beer, Amber, 0.0f, 0.75f);
    public static Material FoamMat => Mat(ref s_foam, Cream, 0.0f, 0.25f);

    static Material Mat(ref Material slot, Color c, float metallic, float smooth)
    {
        if (slot != null) return slot;
        var sh = Shader.Find("Standard");
        slot = new Material(sh) { color = c, hideFlags = HideFlags.HideAndDontSave };
        slot.SetFloat("_Metallic", metallic);
        slot.SetFloat("_Glossiness", smooth);
        return slot;
    }

    /// <summary>
    /// Put a drainable beer inside <paramref name="cup"/> (a Cup.prefab instance,
    /// or anything with the same interior numbers). Returns the liquid so the
    /// caller can drive <see cref="BeerLiquid.SetFill"/>.
    /// </summary>
    public static BeerLiquid AttachLiquid(GameObject cup, Vector3 axisLocal, float radius,
                                         float floorY, float fullY, float foamThickness, float fill01)
    {
        if (cup == null) return null;
        var existing = cup.GetComponentInChildren<BeerLiquid>(true);
        if (existing != null) { existing.SetFill(fill01); return existing; }

        var go = new GameObject("BeerLiquid");
        go.transform.SetParent(cup.transform, false);
        go.layer = cup.layer;
        var liquid = go.AddComponent<BeerLiquid>();
        liquid.Build(axisLocal, radius, floorY, fullY, foamThickness);
        liquid.SetFill(fill01);
        return liquid;
    }

    /// <summary>128×128 side-view tankard for the hotbar slot — with beer (BEER) or without (CUP).</summary>
    public static Sprite BuildIcon(bool withBeer)
    {
        if (withBeer && s_icon != null) return s_icon;
        if (!withBeer && s_iconEmpty != null) return s_iconEmpty;
        const int S = 128;
        var px = new Color32[S * S];
        var clear = new Color32(0, 0, 0, 0);
        for (int i = 0; i < px.Length; i++) px[i] = clear;

        void Rect(int x0, int y0, int x1, int y1, Color c)
        {
            var c32 = (Color32)c;
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(S - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(S - 1, x1); x++) px[y * S + x] = c32;
        }
        void Circle(int cx, int cy, int r, Color c)
        {
            var c32 = (Color32)c;
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || y < 0 || x >= S || y >= S) continue;
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy <= r * r) px[y * S + x] = c32;
                }
        }

        var wood     = new Color(0.45f, 0.29f, 0.14f);
        var woodDark = new Color(0.32f, 0.20f, 0.09f);
        var woodLite = new Color(0.58f, 0.40f, 0.21f);
        var band     = new Color(0.30f, 0.30f, 0.32f);

        // Handle (a ring on the right, drawn first so the body covers its inner half).
        Circle(86, 62, 24, woodDark);
        Circle(86, 62, 15, clear);

        // Body: a slightly tapered wooden tankard, x 30..78, y 20..104.
        Rect(30, 20, 78, 104, wood);
        Rect(30, 20, 36, 104, woodDark);      // left shading
        Rect(72, 20, 78, 104, woodLite);      // right highlight
        // Stave lines.
        for (int x = 42; x <= 66; x += 12) Rect(x, 22, x, 102, woodDark);
        // Iron bands.
        Rect(28, 30, 80, 35, band);
        Rect(28, 86, 80, 91, band);

        if (withBeer)
        {
            // Beer showing above the rim + foam head spilling over.
            Rect(32, 100, 76, 106, Amber);
            Rect(30, 104, 78, 114, Cream);
            Circle(38, 114, 7, Cream);
            Circle(52, 117, 8, Cream);
            Circle(66, 115, 7, Cream);
            Circle(76, 111, 6, Cream);
            // A drip down the left side.
            Rect(31, 92, 34, 104, Cream);
            Circle(32, 90, 3, Cream);
        }
        else
        {
            // Open rim: a dark ellipse so it reads as hollow.
            var inside = new Color(0.20f, 0.12f, 0.05f);
            Rect(34, 100, 74, 106, inside);
            Rect(38, 106, 70, 108, inside);
            Rect(38, 98, 70, 100, inside);
        }

        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        tex.SetPixels32(px);
        tex.Apply(false, true);
        var sprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 128f);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        if (withBeer) s_icon = sprite; else s_iconEmpty = sprite;
        return sprite;
    }
}
