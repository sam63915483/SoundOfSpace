using UnityEngine;

/// <summary>
/// Sprites generated at runtime for the computer UI.
///
/// There is no art for this screen and no reason to add any — a ring and a disc
/// are cheaper to rasterise than to author, import and keep in sync with a
/// palette Sam is still tuning. All colours come from tinting these white
/// shapes, so re-skinning the whole screen is a change to the palette constants
/// in ShuttleComputerUI and nothing else.
///
/// Generated once and cached statically; they survive scene loads, which is
/// what we want since the terminal rebuilds its UI on demand.
/// </summary>
public static class TraxUISprites
{
    static Sprite _white;
    static Sprite _ring;
    static Sprite _disc;

    /// Flat 4x4 white — the fill for every panel, bar and background.
    public static Sprite White
    {
        get
        {
            if (_white == null) _white = MakeSolid(4);
            return _white;
        }
    }

    /// <summary>
    /// Ring, used with Image.type = Filled / Radial360 to draw the knob arcs.
    /// A radial fill over a ring gives an arc; over a disc it would give a pie.
    /// </summary>
    public static Sprite Ring
    {
        get
        {
            if (_ring == null) _ring = MakeRing(160, 0.355f, 0.46f);
            return _ring;
        }
    }

    /// <summary>
    /// 1px hollow border, 9-sliced. Used with Image.type = Sliced so a whole
    /// frame is ONE Image — which means recolouring a border (rack modules light
    /// up when enabled) is a single color assignment rather than hunting down
    /// four separate edge objects.
    /// </summary>
    public static Sprite Border
    {
        get
        {
            if (_border == null) _border = MakeBorder(16);
            return _border;
        }
    }

    static Sprite _border;

    static Sprite MakeBorder(int size)
    {
        var tex = NewTex(size);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                px[y * size + x] = new Color32(255, 255, 255, edge ? (byte)255 : (byte)0);
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        // 2px slice margins keep the 1px line crisp at any rect size — the
        // stretched middle band never contains the line itself.
        var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                              0, SpriteMeshType.FullRect, new Vector4(2, 2, 2, 2));
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    /// Solid circle for the knob hub.
    public static Sprite Disc
    {
        get
        {
            if (_disc == null) _disc = MakeDisc(128);
            return _disc;
        }
    }

    /// <summary>
    /// 4px scanline cell: two clear rows, two dark. Tiled over the whole screen
    /// with a RawImage. This plus the vignette is most of why the browser build
    /// reads as a CRT and a plain UGUI panel doesn't.
    /// wrapMode is Repeat and filtering is Point, or the lines blur to mush.
    /// </summary>
    public static Texture2D Scanlines
    {
        get
        {
            if (_scanlines == null)
            {
                var tex = new Texture2D(1, 4, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.hideFlags = HideFlags.HideAndDontSave;
                tex.SetPixels32(new Color32[]
                {
                    new Color32(0, 0, 0, 0),
                    new Color32(0, 0, 0, 0),
                    new Color32(0, 0, 0, 72),
                    new Color32(0, 0, 0, 72)
                });
                tex.Apply();
                _scanlines = tex;
            }
            return _scanlines;
        }
    }

    static Texture2D _scanlines;

    /// Radial darkening for the screen corners. Clear through the middle so it
    /// never dims the content you are actually reading.
    public static Sprite Vignette
    {
        get
        {
            if (_vignette == null) _vignette = MakeVignette(128);
            return _vignette;
        }
    }

    static Sprite _vignette;

    static Sprite MakeVignette(int size)
    {
        var tex = NewTex(size);
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        float maxD = Mathf.Sqrt(2f) * c;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / maxD;   // 0 centre .. 1 corner
                // Flat until 55%, then ramp — mirrors the CSS radial-gradient.
                float t = Mathf.InverseLerp(0.55f, 1f, d);
                px[y * size + x] = new Color32(0, 0, 0, (byte)(t * t * 150f));
            }
        }

        tex.SetPixels32(px);
        return Finish(tex, size);
    }

    static Texture2D NewTex(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }

    static Sprite Finish(Texture2D tex, int size)
    {
        tex.Apply();
        var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    static Sprite MakeSolid(int size)
    {
        var tex = NewTex(size);
        var px = new Color32[size * size];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(px);
        return Finish(tex, size);
    }

    static Sprite MakeRing(int size, float innerFrac, float outerFrac)
    {
        var tex = NewTex(size);
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        float ro = size * outerFrac;
        float ri = size * innerFrac;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                // One-pixel linear feather on both edges — enough AA that the
                // ring doesn't look jagged at any sane knob size.
                float a = Mathf.Clamp01(ro - d) * Mathf.Clamp01(d - ri);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
        }

        tex.SetPixels32(px);
        return Finish(tex, size);
    }

    static Sprite MakeDisc(int size)
    {
        var tex = NewTex(size);
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        float r = size * 0.47f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - d);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }

        tex.SetPixels32(px);
        return Finish(tex, size);
    }
}
