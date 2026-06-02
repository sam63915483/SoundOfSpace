using UnityEngine;

// Shared cone-beam mesh + additive material factory used by ConcertConeLight
// and ConcertStrobeLight to draw a visible "Pink Floyd" beam in air. Without
// a visible beam mesh, a Unity Spot light is invisible until it hits a surface.
//
// Per the existing ConcertLaser pattern: materials are PER-INSTANCE (each cone
// owns its material, destroys it in OnDestroy). A single static gradient
// texture is shared across all instances since textures don't have the
// per-renderer color-override problem.
public static class ConcertBeamShared
{
    public static readonly int TintColorId = Shader.PropertyToID("_TintColor");
    public static readonly int ColorId     = Shader.PropertyToID("_Color");
    public static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    static Texture2D s_gradientTex;

    // Build a multi-layer cone mesh with apex at origin pointing in +Z. The
    // mesh contains 3 concentric cone surfaces:
    //   • Outer (full radius, ~25% alpha) — soft halo / outline
    //   • Middle (~60% radius, ~55% alpha) — body
    //   • Inner (~25% radius, 100% alpha) — bright core down the axis
    //
    // Combined with additive blending, the overlapping layers produce a
    // brightness gradient from bright core to soft rim — reads as a 3D
    // volumetric beam from any view angle (instead of a flat triangle).
    //
    // Vertex colors carry the per-layer alpha; the gradient texture handles
    // the apex→base falloff via UVs. Both stack multiplicatively in the
    // Particles/Additive shader.
    public static Mesh BuildConeMesh(float halfAngleDeg, float length, int segments)
    {
        var mesh = new Mesh { name = "ConcertConeBeam" };
        if (segments < 8) segments = 8;
        if (length <= 0.01f) length = 0.01f;

        // Layer parameters: radius multiplier + per-layer RGB scaling.
        // RGB scaling is what controls additive layer brightness — alpha is
        // ignored by Particles/Additive (BlendOne One). Keep the sum modest
        // so partial-saturation hues (amber, peach, etc.) don't clip to white
        // when additively stacked. sum * 2 (shader gain) * beamBrightness
        // should stay near 1.0 for vibrant color without whitewash.
        float[] radiiMul = { 1.0f, 0.6f, 0.25f };
        float[] rgbMul   = { 0.20f, 0.35f, 0.55f };  // sum 1.10 → ~1.5x palette color at peak
        const int layers = 3;

        int vertsPerLayer = segments + 1;
        int totalVerts = vertsPerLayer * layers;
        int totalTris  = segments * layers;

        var verts  = new Vector3[totalVerts];
        var uvs    = new Vector2[totalVerts];
        var colors = new Color[totalVerts];
        var tris   = new int[totalTris * 3];

        float baseR = length * Mathf.Tan(halfAngleDeg * Mathf.Deg2Rad);
        for (int layer = 0; layer < layers; layer++)
        {
            int baseV = layer * vertsPerLayer;
            float r = baseR * radiiMul[layer];
            float m = rgbMul[layer];

            verts[baseV] = Vector3.zero;
            uvs[baseV]   = new Vector2(0.5f, 0f);  // apex samples bottom of texture (alpha=1)
            colors[baseV] = new Color(m, m, m, 1f);

            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                verts[baseV + i + 1] = new Vector3(Mathf.Sin(a) * r, Mathf.Cos(a) * r, length);
                uvs[baseV + i + 1]   = new Vector2(i / (float)segments, 1f);  // base samples top (alpha=0)
                colors[baseV + i + 1] = new Color(m, m, m, 1f);
            }

            int baseT = layer * segments * 3;
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                tris[baseT + i * 3 + 0] = baseV;
                tris[baseT + i * 3 + 1] = baseV + i + 1;
                tris[baseT + i * 3 + 2] = baseV + next + 1;
            }
        }

        mesh.vertices  = verts;
        mesh.triangles = tris;
        mesh.uv        = uvs;
        mesh.colors    = colors;
        // Generous bounds so frustum culling doesn't clip when the camera
        // is inside the cone (player walks through the beam).
        mesh.bounds = new Bounds(new Vector3(0f, 0f, length * 0.5f),
                                 new Vector3(baseR * 2f + 1f, baseR * 2f + 1f, length + 1f));
        return mesh;
    }

    // Create a per-instance additive transparent material with the gradient
    // texture pre-assigned. Caller owns the material and must Destroy() it in
    // OnDestroy.
    //
    // SHADER SOURCING: prefer a Material asset shipped under Resources/ so
    // Unity is GUARANTEED to include the shader + every used variant in
    // standalone builds — Shader.Find alone is unreliable in builds because
    // Unity strips shaders not referenced by any Material asset or by the
    // Always Included Shaders list. When that stripping bites, the fallback
    // chain (Particles/Additive → Sprites/Default → Unlit/Color) lands on a
    // shader that doesn't read vertex colors, and the visible cone / laser
    // renders flat white. We instantiate the cached template Material so
    // every caller gets its own tintable instance.
    static Material s_templateMat;
    static Shader s_resolvedShader;
    public static Material MakeBeamMaterial()
    {
        Material mat = null;
        // Prefer Concert/ConeBeam — same additive blend as Concert/Additive
        // but adds a view-axis fade and Cull Front so the cone visual
        // doesn't 2-4× the apparent ground brightness when the camera is
        // pointed roughly along the beam axis. Falls through to the older
        // Concert/Additive Resources material if the new shader can't be
        // resolved (e.g., stripped in a build), and from there through the
        // same Shader.Find fallback chain the laser path uses.
        if (s_resolvedShader == null)
            s_resolvedShader = Shader.Find("Concert/ConeBeam");
        if (s_resolvedShader != null)
        {
            mat = new Material(s_resolvedShader) { name = "ConcertConeBeamMat" };
        }
        else
        {
            if (s_templateMat == null)
            {
                s_templateMat = Resources.Load<Material>("ConcertAdditiveMaterial");
                if (s_templateMat == null)
                    Debug.LogWarning("[ConcertBeamShared] Concert/ConeBeam and Resources/ConcertAdditiveMaterial.mat both unavailable — falling back to Shader.Find. Beams may render flat white in standalone builds.");
            }
            if (s_templateMat != null)
            {
                mat = new Material(s_templateMat) { name = "ConcertConeBeamMat" };
            }
            else
            {
                Shader fallback = Shader.Find("Concert/Additive");
                if (fallback == null) fallback = Shader.Find("Particles/Additive");
                if (fallback == null) fallback = Shader.Find("Legacy Shaders/Particles/Additive");
                if (fallback == null) fallback = Shader.Find("Sprites/Default");
                if (fallback == null) fallback = Shader.Find("Unlit/Color");
                Debug.Log($"[ConcertBeamShared] Cone-beam shader fallback resolved to: {(fallback != null ? fallback.name : "<null>")}");
                mat = new Material(fallback) { name = "ConcertConeBeamMat" };
            }
        }
        var tex = GetGradientTexture();
        if (mat.HasProperty("_MainTex"))   mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseMap"))   mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty(TintColorId))  mat.SetColor(TintColorId, Color.white);
        if (mat.HasProperty(ColorId))      mat.SetColor(ColorId,     Color.white);
        if (mat.HasProperty(BaseColorId))  mat.SetColor(BaseColorId, Color.white);
        return mat;
    }

    static Texture2D GetGradientTexture()
    {
        if (s_gradientTex != null) return s_gradientTex;
        const int size = 64;
        s_gradientTex = new Texture2D(1, size, TextureFormat.RGBA32, mipChain: false)
        {
            name = "ConcertBeamGradient",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var pixels = new Color32[size];
        for (int y = 0; y < size; y++)
        {
            // Bottom of texture (y=0) = apex (bright), top (y=size-1) = base (faded).
            float u = y / (float)(size - 1);
            float a = Mathf.Pow(1f - u, 1.5f);
            byte v = (byte)Mathf.RoundToInt(a * 255f);
            pixels[y] = new Color32(v, v, v, v);
        }
        s_gradientTex.SetPixels32(pixels);
        s_gradientTex.Apply(false, true);
        return s_gradientTex;
    }
}
