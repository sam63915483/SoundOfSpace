using UnityEngine;

// Procedural beveled-panel + outline sprites used by the tutorial/prompt-pill
// family (TutorialUI, InteractPromptUI, Hotbar name plate, VitalsHUD).
// Cached statics — first call generates, subsequent calls reuse.
public static class UIPanelSprites
{
    static Sprite _panel, _outline;

    /// <summary>Filled beveled panel — clipped top-left + bottom-right corners. 64x64 source, 18 px slice borders.</summary>
    public static Sprite GetBeveledPanel()
    {
        if (_panel != null) return _panel;
        var tex = MakePanel(64, 14);
        _panel = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f),
                               100f, 0u, SpriteMeshType.FullRect, new Vector4(18, 18, 18, 18));
        _panel.name = "UIBeveledPanel";
        return _panel;
    }

    /// <summary>Hollow beveled outline — 2 px ring matching the panel shape.</summary>
    public static Sprite GetBeveledOutline()
    {
        if (_outline != null) return _outline;
        var tex = MakeOutline(64, 14, 2);
        _outline = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f),
                                 100f, 0u, SpriteMeshType.FullRect, new Vector4(18, 18, 18, 18));
        _outline.name = "UIBeveledOutline";
        return _outline;
    }

    static Texture2D MakePanel(int size, int bevel)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        int s = size - 1;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int distTL = x + (s - y);
                int distBR = (s - x) + y;
                float a = 1f;
                if (distTL < bevel) a = Mathf.Clamp01(distTL - (bevel - 1) + 0.5f);
                else if (distBR < bevel) a = Mathf.Clamp01(distBR - (bevel - 1) + 0.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static Texture2D MakeOutline(int size, int bevel, int thickness)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        int s = size - 1;
        int innerBevel = Mathf.Max(0, bevel - thickness);
        int innerSize = size - 2 * thickness;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int distTL = x + (s - y);
                int distBR = (s - x) + y;
                float outerA = 1f;
                if (distTL < bevel) outerA = Mathf.Clamp01(distTL - (bevel - 1) + 0.5f);
                else if (distBR < bevel) outerA = Mathf.Clamp01(distBR - (bevel - 1) + 0.5f);

                int ix = x - thickness;
                int iy = y - thickness;
                float innerA = 0f;
                if (ix >= 0 && iy >= 0 && ix < innerSize && iy < innerSize)
                {
                    int innerS = innerSize - 1;
                    int iDistTL = ix + (innerS - iy);
                    int iDistBR = (innerS - ix) + iy;
                    innerA = 1f;
                    if (iDistTL < innerBevel) innerA = Mathf.Clamp01(iDistTL - (innerBevel - 1) + 0.5f);
                    else if (iDistBR < innerBevel) innerA = Mathf.Clamp01(iDistBR - (innerBevel - 1) + 0.5f);
                }
                float ringA = Mathf.Clamp01(outerA - innerA);
                pixels[y * size + x] = new Color(1f, 1f, 1f, ringA);
            }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
