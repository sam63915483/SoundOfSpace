using UnityEngine;

// Shared utilities for concert particle systems (haze, fog puffs).
// Generates a soft radial-gradient texture and a properly configured
// alpha-blend material so particles render as soft cloud puffs instead of
// hard rectangular billboards. Built-in render pipeline compatible.
public static class ConcertParticleAssets
{
    static Texture2D s_softCloud;
    static Material s_alphaBlendMat;

    // Irregular cloud puff: radial gradient warped by Perlin noise so each
    // particle has a slightly amorphous, asymmetric shape. Combined with random
    // particle rotation, overlapping particles blend into smoke-like density
    // instead of stacking as visible identical circles.
    public static Texture2D GetSoftCloudTexture()
    {
        if (s_softCloud != null) return s_softCloud;
        const int size = 128;
        s_softCloud = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true);
        s_softCloud.wrapMode = TextureWrapMode.Clamp;
        s_softCloud.filterMode = FilterMode.Bilinear;
        s_softCloud.hideFlags = HideFlags.HideAndDontSave;
        var pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        // Random offset into Perlin field so the noise pattern is unique each session.
        float ox = UnityEngine.Random.value * 1000f;
        float oy = UnityEngine.Random.value * 1000f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - center) / center;
                float dy = (y - center) / center;
                // Two octaves of Perlin offset to break the perfect circle.
                float n1 = Mathf.PerlinNoise(ox + x * 0.04f,  oy + y * 0.04f) - 0.5f;
                float n2 = Mathf.PerlinNoise(ox + x * 0.12f,  oy + y * 0.12f) - 0.5f;
                float radial = Mathf.Sqrt(dx * dx + dy * dy);
                // Subtract noise from the effective radius — pulls/pushes the edge in/out.
                float r = radial + (n1 * 0.35f + n2 * 0.15f);
                float a = Mathf.Clamp01(1f - r);
                a = a * a * (3f - 2f * a);
                // Multiply by another noise sample so density itself is patchy.
                float densityNoise = Mathf.PerlinNoise(ox + x * 0.08f, oy + y * 0.08f);
                a *= 0.55f + 0.55f * densityNoise;
                a = Mathf.Clamp01(a);
                byte v = (byte)Mathf.RoundToInt(a * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, v);
            }
        }
        s_softCloud.SetPixels32(pixels);
        s_softCloud.Apply(true, true); // generate mipmaps + lock for upload
        return s_softCloud;
    }

    // Standard alpha-blended particle material with the soft cloud texture.
    // Cached and reused so adding many particle systems doesn't allocate
    // duplicate materials. Uses Built-in pipeline shaders only.
    public static Material GetAlphaBlendCloudMaterial()
    {
        if (s_alphaBlendMat != null) return s_alphaBlendMat;

        Shader shader = Shader.Find("Mobile/Particles/Alpha Blended");
        if (shader == null) shader = Shader.Find("Particles/Alpha Blended");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        s_alphaBlendMat = new Material(shader);
        s_alphaBlendMat.hideFlags = HideFlags.HideAndDontSave;
        var tex = GetSoftCloudTexture();
        if (s_alphaBlendMat.HasProperty("_MainTex"))   s_alphaBlendMat.SetTexture("_MainTex", tex);
        if (s_alphaBlendMat.HasProperty("_BaseMap"))   s_alphaBlendMat.SetTexture("_BaseMap", tex);
        if (s_alphaBlendMat.HasProperty("_TintColor")) s_alphaBlendMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        return s_alphaBlendMat;
    }
}
