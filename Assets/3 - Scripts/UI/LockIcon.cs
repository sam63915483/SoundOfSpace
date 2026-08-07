using UnityEngine;

/// <summary>
/// A padlock sprite drawn in code. White with an alpha-shaped body, so callers
/// tint it with Image.color like any other UI glyph.
///
/// Generated rather than authored because the HUD's TMP font atlas has no
/// padlock character — 🔒 renders as a tofu box, and shipping a 64×64 PNG for
/// one glyph means another asset + .meta + a GUID reference to keep alive. This
/// costs ~4 KB of texture, once, shared by every locked build-menu row.
/// </summary>
public static class LockIcon
{
    const int Size = 64;

    static Sprite _sprite;

    public static Sprite Sprite
    {
        get
        {
            if (_sprite != null) return _sprite;
            var tex = Build();
            _sprite = UnityEngine.Sprite.Create(
                tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            _sprite.name = "LockIcon";
            return _sprite;
        }
    }

    static Texture2D Build()
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = "LockIconTex",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        var px = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                // 3×3 supersample — cheap analytic AA on the shackle's curve,
                // which is the only place hard edges read as jagged at 24 px.
                int hits = 0;
                for (int sy = 0; sy < 3; sy++)
                    for (int sx = 0; sx < 3; sx++)
                        if (Solid(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f)) hits++;
                px[y * Size + x] = new Color(1f, 1f, 1f, hits / 9f);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // Texture space: origin bottom-left. Body sits low, shackle arches above it.
    static bool Solid(float x, float y)
    {
        // Keyhole is cut from everything.
        if (InCircle(x, y, 32f, 26f, 4.2f)) return false;
        if (InRect(x, y, 29.8f, 34.2f, 16f, 26f)) return false;

        // Lock body — rounded rect.
        if (InRoundedRect(x, y, 13f, 51f, 6f, 36f, 5f)) return true;

        // Shackle — upper half of an annulus, plus the two legs dropping into
        // the body so the arch reads as attached rather than floating.
        const float cx = 32f, cy = 36.5f, outer = 15f, inner = 9.5f;
        if (y >= cy)
        {
            float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            if (d <= outer && d >= inner) return true;
        }
        else if (y >= 30f)
        {
            if (InRect(x, y, cx - outer, cx - inner, 30f, cy)) return true;
            if (InRect(x, y, cx + inner, cx + outer, 30f, cy)) return true;
        }
        return false;
    }

    static bool InRect(float x, float y, float x0, float x1, float y0, float y1)
        => x >= x0 && x <= x1 && y >= y0 && y <= y1;

    static bool InCircle(float x, float y, float cx, float cy, float r)
        => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;

    static bool InRoundedRect(float x, float y, float x0, float x1, float y0, float y1, float r)
    {
        if (!InRect(x, y, x0, x1, y0, y1)) return false;
        // Distance past the corner box. Nested two-arg Max on purpose — the
        // three-arg overload is params float[], i.e. an allocation per pixel
        // sample, and this runs 64×64×9 times.
        float qx = Mathf.Max(Mathf.Max(x0 + r - x, 0f), x - (x1 - r));
        float qy = Mathf.Max(Mathf.Max(y0 + r - y, 0f), y - (y1 - r));
        return qx * qx + qy * qy <= r * r;
    }
}
