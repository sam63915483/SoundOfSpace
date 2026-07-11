using System;
using UnityEngine;

/// <summary>
/// Single source of truth for the helmet HUD accent color. The accent isn't
/// locked yet, so everything routes through here: change DefaultAccent (or
/// tweak HelmetHudConfig.accentColor in the Inspector at runtime) and every
/// bezel/glow/glass layer re-tints via OnAccentChanged.
/// Defaults to the existing LED cyan (VitalsHUD/GForceHUD 0x5CC8FF) so the
/// helmet matches the current HUD family out of the box.
/// </summary>
public static class HelmetHudPalette
{
    public static readonly Color32 DefaultAccent = new Color32(0x5C, 0xC8, 0xFF, 0xFF);

    static Color32 _accent = DefaultAccent;
    public static Color32 Accent => _accent;

    /// Fired after the accent changes — subscribers re-tint their cached images.
    public static event Action OnAccentChanged;

    public static void SetAccent(Color32 c)
    {
        if (_accent.r == c.r && _accent.g == c.g && _accent.b == c.b && _accent.a == c.a) return;
        _accent = c;
        OnAccentChanged?.Invoke();
    }

    // Derived tints (recomputed on read so they track the live accent).
    public static Color AccentGlow     => WithAlpha(_accent, 0.55f);
    public static Color AccentFaint    => WithAlpha(_accent, 0.10f);
    public static Color BezelRing      => Color.Lerp((Color)_accent, Color.black, 0.55f);
    public static Color GlassBackplate => new Color(0.03f, 0.07f, 0.12f, 0.92f);
    public static Color GlassSheen     => WithAlpha(_accent, 0.35f);
    public static Color FrameTint      => Color.white;   // helmet art rendered as-authored

    static Color WithAlpha(Color32 c, float a)
    {
        Color col = c; col.a = a; return col;
    }
}
