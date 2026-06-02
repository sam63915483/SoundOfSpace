using UnityEngine;

/// <summary>
/// Single source of truth for HAL's visual identity (red eye colour + the
/// procedural disc sprite). Previously duplicated between AIChatScreen and
/// HALLineHUD — if HAL's red ever shifts, we now update it in exactly one
/// place.
/// </summary>
public static class HALVisuals
{
    /// HAL 9000's iconic warm red, slightly orange-shifted.
    public static readonly Color EyeRed = new Color(1f, 0.13f, 0.05f, 1f);

    /// Procedural soft-edged disc sprite, generated once and cached. The
    /// 1-pixel anti-aliased edge keeps the eye reading as round at any size
    /// without an asset import.
    static Sprite _cachedDisc;
    public static Sprite Disc()
    {
        if (_cachedDisc != null) return _cachedDisc;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[S * S];
        float cx = S * 0.5f, cy = S * 0.5f;
        float r  = S * 0.5f - 1f;
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = x - cx, dy = y - cy;
            float d  = Mathf.Sqrt(dx * dx + dy * dy);
            float a  = Mathf.Clamp01(r - d);
            pixels[y * S + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        _cachedDisc = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        _cachedDisc.name = "HAL_Disc";
        return _cachedDisc;
    }
}
