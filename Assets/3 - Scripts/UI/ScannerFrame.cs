using UnityEngine;
using UnityEngine.UI;

// Shared visual helpers for the scanner panels (FishingdexManager + BuildMenuUI).
//
// AddBrackets: 4 corner L-shapes built from 8 thin Images (2 per corner). They
// never overlap content because they're absolutely-positioned in the corners.
//
// AddBlueprintGrid: two tiled Image strips (one horizontal, one vertical)
// drawn at a low alpha to suggest engineering paper behind a build preview.
// The fishingdex skips the grid (creatures, not structures); the build menu
// uses it on its preview region.
public static class ScannerFrame
{
    public static void AddBrackets(RectTransform parent, float length = 14f, float thickness = 2f)
    {
        AddBrackets(parent, length, thickness, CyanScannerPalette.BracketColor);
    }

    public static void AddBrackets(RectTransform parent, float length, float thickness, Color32 color)
    {
        AddCornerBracket(parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), length, thickness, color);   // TL
        AddCornerBracket(parent, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), length, thickness, color);   // TR
        AddCornerBracket(parent, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), length, thickness, color);   // BL
        AddCornerBracket(parent, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), length, thickness, color);   // BR
    }

    static void AddCornerBracket(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                                 float length, float thickness, Color32 color)
    {
        // One horizontal stub + one vertical stub at each corner. The pivot
        // determines which direction they extend inward.
        // For anchorMin == anchorMax == (0,1) (top-left): horizontal extends right (+X), vertical extends down (-Y).
        bool extendRight = pivot.x < 0.5f;
        bool extendUp    = pivot.y < 0.5f;

        // Horizontal stub
        var hGo = new GameObject("BracketH", typeof(RectTransform), typeof(Image));
        hGo.transform.SetParent(parent, false);
        var hRt = (RectTransform)hGo.transform;
        hRt.anchorMin = anchorMin;
        hRt.anchorMax = anchorMax;
        hRt.pivot     = pivot;
        hRt.sizeDelta = new Vector2(length, thickness);
        hRt.anchoredPosition = Vector2.zero;
        hGo.GetComponent<Image>().color = color;
        hGo.GetComponent<Image>().raycastTarget = false;

        // Vertical stub
        var vGo = new GameObject("BracketV", typeof(RectTransform), typeof(Image));
        vGo.transform.SetParent(parent, false);
        var vRt = (RectTransform)vGo.transform;
        vRt.anchorMin = anchorMin;
        vRt.anchorMax = anchorMax;
        vRt.pivot     = pivot;
        vRt.sizeDelta = new Vector2(thickness, length);
        vRt.anchoredPosition = Vector2.zero;
        vGo.GetComponent<Image>().color = color;
        vGo.GetComponent<Image>().raycastTarget = false;
    }

    // Adds a child container with two faint grid strips (horizontal + vertical).
    // Spacing defaults to 24 px; alpha is built into CyanScannerPalette.GridLine.
    public static void AddBlueprintGrid(RectTransform parent, float gridSpacing = 24f)
    {
        var go = new GameObject("BlueprintGrid", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var bg = go.GetComponent<Image>();
        bg.color = new Color32(0, 0, 0, 0);
        bg.raycastTarget = false;

        // Build a single 1×N stripe texture for each axis and tile via UV.
        AddGridStripes(rt, true, gridSpacing);
        AddGridStripes(rt, false, gridSpacing);
    }

    static void AddGridStripes(RectTransform parent, bool horizontal, float spacing)
    {
        // Tile manually via repeated thin Images so we don't need a custom
        // shader. With ~24-px spacing on a ~360-px preview, that's ~15 strips
        // per axis — well under the per-frame draw-call budget for one menu.
        // Build "enough strips to cover the parent rect" then layout-anchor
        // them along the relevant axis.
        const int MaxStripes = 32;
        for (int i = 1; i < MaxStripes; i++)
        {
            var go = new GameObject(horizontal ? "GridH" : "GridV", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            if (horizontal)
            {
                rt.anchorMin = new Vector2(0, 0);
                rt.anchorMax = new Vector2(1, 0);
                rt.pivot     = new Vector2(0.5f, 0);
                rt.sizeDelta = new Vector2(0, 1f);
                rt.anchoredPosition = new Vector2(0, i * spacing);
            }
            else
            {
                rt.anchorMin = new Vector2(0, 0);
                rt.anchorMax = new Vector2(0, 1);
                rt.pivot     = new Vector2(0, 0.5f);
                rt.sizeDelta = new Vector2(1f, 0);
                rt.anchoredPosition = new Vector2(i * spacing, 0);
            }
            var img = go.GetComponent<Image>();
            img.color = CyanScannerPalette.GridLine;
            img.raycastTarget = false;
        }
    }
}
