using UnityEngine;

/// <summary>
/// The shaders the football code builds its materials from, looked up once
/// and never null. In the Editor every built-in shader answers
/// <c>Shader.Find</c>; in a BUILD only shaders some material uses (or the
/// Always Included list carries) exist, and <c>new Material(null)</c> throws —
/// which is how the jumbotrons vanished from the first build (2026-09-20).
/// Each property tries the wanted shader, then sensible stand-ins, then any
/// shader at all.
/// </summary>
public static class FootballShader
{
    static Shader _unlit, _unlitTex, _standard, _sprite;

    /// Unlit/Color — flat field lines, slabs, markers.
    public static Shader Unlit => _unlit != null ? _unlit : (_unlit = First("Unlit/Color", "Sprites/Default", "Unlit/Texture", "Standard"));
    /// Unlit/Texture — the jumbotron picture.
    public static Shader UnlitTexture => _unlitTex != null ? _unlitTex : (_unlitTex = First("Unlit/Texture", "Standard", "Sprites/Default"));
    /// Standard — lit props (poles, signs, the button, the ball).
    public static Shader Standard => _standard != null ? _standard : (_standard = First("Standard", "Legacy Shaders/Diffuse", "Unlit/Color"));
    /// Sprites/Default — line renderers (the throw arc).
    public static Shader Sprite => _sprite != null ? _sprite : (_sprite = First("Sprites/Default", "Unlit/Color", "Standard"));

    static Shader First(params string[] names)
    {
        foreach (var n in names) { var s = Shader.Find(n); if (s != null) return s; }
        // Last resort: whatever some renderer in the scene is already using.
        var any = Object.FindObjectOfType<Renderer>();
        if (any != null && any.sharedMaterial != null && any.sharedMaterial.shader != null) return any.sharedMaterial.shader;
        Debug.LogError("[FootballShader] no shader found for " + string.Join(" / ", names) + " — add it to Project Settings > Graphics > Always Included Shaders");
        return null;
    }
}
