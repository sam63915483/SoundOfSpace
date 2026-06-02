using UnityEngine;

// Shared galaxy palette + procedural sprite helpers for HUD restyling.
// Mirrors the look of TutorialUI / MainMenuController without touching them.
public static class GalaxyHudKit
{
    public static readonly Color BgTopColor    = new Color32(0x35, 0x18, 0x66, 0xFF);
    public static readonly Color BgMidColor    = new Color32(0x1B, 0x0C, 0x42, 0xFF);
    public static readonly Color BgBottomColor = new Color32(0x07, 0x05, 0x1C, 0xFF);
    public static readonly Color SlotColor     = new Color32(0x05, 0x03, 0x12, 0xF2);
    public static readonly Color BorderCool    = new Color32(0x5B, 0xD8, 0xFF, 0xFF);
    public static readonly Color BorderHot     = new Color32(0xC9, 0x4F, 0xFF, 0xFF);
    public static readonly Color GlowColor     = new Color32(0x6F, 0x80, 0xFF, 0x60);
    public static readonly Color LabelColor    = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
    public static readonly Color LabelGlow     = new Color32(0x5B, 0xD8, 0xFF, 0x80);

    // Resource colours — left → right gradient on each fill bar.
    public static readonly Color HealthA   = new Color32(0xFF, 0x6B, 0x9F, 0xFF);
    public static readonly Color HealthB   = new Color32(0xE6, 0x39, 0x52, 0xFF);
    public static readonly Color HungerA   = new Color32(0xFF, 0xC4, 0x77, 0xFF);
    public static readonly Color HungerB   = new Color32(0xFF, 0x8A, 0x4C, 0xFF);
    public static readonly Color ThirstA   = new Color32(0x7B, 0xE2, 0xFF, 0xFF);
    public static readonly Color ThirstB   = new Color32(0x4A, 0x8B, 0xFF, 0xFF);
    public static readonly Color ShipPowA  = new Color32(0xB8, 0x8C, 0xFF, 0xFF);
    public static readonly Color ShipPowB  = new Color32(0xC9, 0x4F, 0xFF, 0xFF);

    // Boost colours.
    public static readonly Color UpThrustA  = new Color32(0x5B, 0xD8, 0xFF, 0xFF);
    public static readonly Color UpThrustB  = new Color32(0x76, 0xFF, 0xFF, 0xFF);
    public static readonly Color DownA      = new Color32(0xC9, 0x4F, 0xFF, 0xFF);
    public static readonly Color DownB      = new Color32(0xFF, 0x66, 0xCC, 0xFF);
    public static readonly Color DirA       = new Color32(0xFF, 0xC4, 0x77, 0xFF);
    public static readonly Color DirB       = new Color32(0xFF, 0xE0, 0x66, 0xFF);

    // Sprite cache.
    static Sprite roundedSprite, slotSprite, nebulaSprite, glowSprite, accentSprite;

    public static Sprite RoundedSprite()
    {
        if (roundedSprite != null) return roundedSprite;
        var tex = MakeRoundedRectTexture(64, 18, Color.white);
        roundedSprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f),
                                      100f, 0u, SpriteMeshType.FullRect, new Vector4(22, 22, 22, 22));
        roundedSprite.name = "GalaxyHudRounded";
        return roundedSprite;
    }

    public static Sprite SlotSprite()
    {
        if (slotSprite != null) return slotSprite;
        var tex = MakeSlotTexture(64, 14);
        slotSprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f),
                                    100f, 0u, SpriteMeshType.FullRect, new Vector4(20, 20, 20, 20));
        slotSprite.name = "GalaxyHudSlot";
        return slotSprite;
    }

    public static Sprite NebulaSprite()
    {
        if (nebulaSprite != null) return nebulaSprite;
        var tex = MakeNebulaTexture(96, 14);
        nebulaSprite = Sprite.Create(tex, new Rect(0, 0, 96, 96), new Vector2(0.5f, 0.5f),
                                      100f, 0u, SpriteMeshType.FullRect, new Vector4(20, 20, 20, 20));
        nebulaSprite.name = "GalaxyHudNebula";
        return nebulaSprite;
    }

    public static Sprite GlowSprite()
    {
        if (glowSprite != null) return glowSprite;
        var tex = MakeRadialGlowTexture(96);
        glowSprite = Sprite.Create(tex, new Rect(0, 0, 96, 96), new Vector2(0.5f, 0.5f),
                                    100f, 0u, SpriteMeshType.FullRect, new Vector4(40, 40, 40, 40));
        glowSprite.name = "GalaxyHudGlow";
        return glowSprite;
    }

    public static Sprite AccentSprite()
    {
        if (accentSprite != null) return accentSprite;
        var tex = MakeHorizontalGradient(128, 4, BorderCool, BorderHot);
        accentSprite = Sprite.Create(tex, new Rect(0, 0, 128, 4), new Vector2(0.5f, 0.5f), 100f);
        accentSprite.name = "GalaxyHudAccent";
        return accentSprite;
    }

    // Creates a horizontal gradient fill sprite cached per colour pair.
    public static Sprite GradientFillSprite(Color a, Color b)
    {
        var tex = MakeHorizontalGradient(128, 8, a, b);
        var s = Sprite.Create(tex, new Rect(0, 0, 128, 8), new Vector2(0f, 0.5f),
                              100f, 0u, SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4));
        s.name = $"GalaxyHudGradient_{ColorUtility.ToHtmlStringRGB(a)}_{ColorUtility.ToHtmlStringRGB(b)}";
        return s;
    }

    // Pill-shaped fill: rounded ends + horizontal gradient, with a soft top→bottom
    // shading for a glassy look. Used as Image.Type.Simple (preserves gradient).
    public static Sprite RoundedGradientFillSprite(Color a, Color b)
    {
        const int W = 256, H = 24, R = 12;
        var tex = MakeRoundedGradientTexture(W, H, R, a, b);
        var s = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0f, 0.5f),
                              100f, 0u, SpriteMeshType.FullRect);
        s.name = $"GalaxyHudFill_{ColorUtility.ToHtmlStringRGB(a)}_{ColorUtility.ToHtmlStringRGB(b)}";
        return s;
    }

    static Texture2D MakeRoundedGradientTexture(int width, int height, int radius, Color a, Color b)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            // Subtle vertical glassy shading — slightly brighter on top, fading toward bottom.
            float v = (float)y / (height - 1);
            float shade = Mathf.Lerp(1.18f, 0.78f, 1f - v);
            for (int x = 0; x < width; x++)
            {
                float t = (float)x / (width - 1);
                Color c = Color.Lerp(a, b, t);
                c = new Color(c.r * shade, c.g * shade, c.b * shade, c.a);
                float alpha = c.a * RoundedRectAlpha2D(x, y, width, height, radius);
                pixels[y * width + x] = new Color(c.r, c.g, c.b, alpha);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static float RoundedRectAlpha2D(int x, int y, int width, int height, int radius)
    {
        int dx = 0, dy = 0;
        if (x < radius) dx = radius - x;
        else if (x >= width - radius) dx = x - (width - radius - 1);
        if (y < radius) dy = radius - y;
        else if (y >= height - radius) dy = y - (height - radius - 1);
        if (dx <= 0 || dy <= 0) return 1f;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Clamp01(radius - d + 0.5f);
    }

    static Texture2D MakeRoundedRectTexture(int size, int radius, Color color)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = new Color(color.r, color.g, color.b,
                    color.a * RoundedRectAlpha(x, y, size, radius));
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static Texture2D MakeSlotTexture(int size, int radius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = (float)y / (size - 1);
            // Subtle vertical inner shadow (darker top, slightly lighter bottom).
            Color baseColor = Color.Lerp(SlotColor, new Color(0.07f, 0.04f, 0.16f, SlotColor.a), v);
            for (int x = 0; x < size; x++)
            {
                float a = baseColor.a * RoundedRectAlpha(x, y, size, radius);
                pixels[y * size + x] = new Color(baseColor.r, baseColor.g, baseColor.b, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static Texture2D MakeNebulaTexture(int size, int radius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = (float)y / (size - 1);
            Color baseColor = v < 0.5f
                ? Color.Lerp(BgBottomColor, BgMidColor, v * 2f)
                : Color.Lerp(BgMidColor, BgTopColor, (v - 0.5f) * 2f);
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / (size - 1);
                float n = Mathf.PerlinNoise(u * 2.4f + 4.7f, v * 2.4f + 9.3f);
                Color tinted = Color.Lerp(baseColor,
                                           new Color(0.45f, 0.20f, 0.65f, baseColor.a),
                                           Mathf.SmoothStep(0f, 1f, n) * 0.30f);
                float a = tinted.a * RoundedRectAlpha(x, y, size, radius);
                pixels[y * size + x] = new Color(tinted.r, tinted.g, tinted.b, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static Texture2D MakeRadialGlowTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        float maxR = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.6f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static Texture2D MakeHorizontalGradient(int width, int height, Color left, Color right)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[width * height];
        for (int x = 0; x < width; x++)
        {
            float t = (float)x / (width - 1);
            Color c = Color.Lerp(left, right, t);
            for (int y = 0; y < height; y++)
                pixels[y * width + x] = c;
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static float RoundedRectAlpha(int x, int y, int size, int radius)
    {
        int dx = 0, dy = 0;
        if (x < radius) dx = radius - x;
        else if (x >= size - radius) dx = x - (size - radius - 1);
        if (y < radius) dy = radius - y;
        else if (y >= size - radius) dy = y - (size - radius - 1);
        if (dx <= 0 || dy <= 0) return 1f;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Clamp01(radius - d + 0.5f);
    }
}
