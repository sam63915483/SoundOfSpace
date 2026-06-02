using UnityEngine;
using TMPro;

/// <summary>
/// Shared TMP font resolver for procedurally-built HUDs. Tries the project's
/// preferred fonts in priority order and falls back through the standard TMP
/// shipped SDF fonts. Mirrors the pattern in TutorialUI / GForceHUD so any
/// new HUD has one-call font setup that works in builds (where individual
/// font assets in Resources may or may not be present depending on which
/// TMP essentials were imported).
/// </summary>
public static class HudFontResolver
{
    static TMP_FontAsset _font;
    static bool _resolved;

    public static TMP_FontAsset Default
    {
        get
        {
            if (_resolved) return _font;
            _resolved = true;
            _font = Resources.Load<TMP_FontAsset>("Techno SDF");
            if (_font == null)
            {
                var rawFont = Resources.Load<Font>("Techno");
                if (rawFont != null) _font = TMP_FontAsset.CreateFontAsset(rawFont);
            }
            if (_font == null) _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationMono SDF");
            if (_font == null) _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/CourierNewBold SDF");
            if (_font == null) _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            return _font;
        }
    }

    public static void Apply(TextMeshProUGUI t)
    {
        var f = Default;
        if (f != null) t.font = f;
    }
}
