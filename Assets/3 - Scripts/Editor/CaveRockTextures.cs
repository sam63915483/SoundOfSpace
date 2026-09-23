using UnityEditor;
using UnityEngine;

/// <summary>
/// Procedural rock textures + materials for the cave solids, one set per
/// recipe so the three moon caves read as different stone. Generated once and
/// saved as assets next to the cave prefabs; delete the assets to regenerate.
///
/// The material references `Custom/CaveRock` (Assets/Shaders/CaveRock.shader)
/// as a real asset reference, so the shader survives build stripping — a
/// `Shader.Find` at runtime would not (see editor-truth-vs-build-truth).
///
/// The normal map is stored as a plain RGBA texture (RG = xy, not DXT5nm), and
/// the shader decodes it itself. It is NOT imported as a normal map because it
/// never goes through the importer.
/// </summary>
public static class CaveRockTextures
{
    public const string Folder = "Assets/1 - samsPrefabs/Cave/Moon";
    const int Size = 512;

    public static Material GetMaterial(CaveSolid.Recipe recipe)
    {
        if (!AssetDatabase.IsValidFolder("Assets/1 - samsPrefabs/Cave")) AssetDatabase.CreateFolder("Assets/1 - samsPrefabs", "Cave");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/1 - samsPrefabs/Cave", "Moon");

        string matPath = $"{Folder}/Cave_Rock_{recipe}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

        var shader = Shader.Find("Custom/CaveRock");
        if (shader == null)
        {
            Debug.LogError("[CaveRockTextures] Shader 'Custom/CaveRock' not found — is Assets/Shaders/CaveRock.shader compiling?");
            shader = Shader.Find("Standard");
        }

        string albedoPath = $"{Folder}/Cave_Rock_{recipe}_Albedo.asset";
        string normalPath = $"{Folder}/Cave_Rock_{recipe}_Normal.asset";
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        if (albedo == null || normal == null)
        {
            Generate(recipe, out albedo, out normal);
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath) != null) AssetDatabase.DeleteAsset(albedoPath);
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) != null) AssetDatabase.DeleteAsset(normalPath);
            AssetDatabase.CreateAsset(albedo, albedoPath);
            AssetDatabase.CreateAsset(normal, normalPath);
        }

        if (mat == null)
        {
            mat = new Material(shader) { name = $"Cave_Rock_{recipe}" };
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.shader = shader;
        // The cave is MOON ROCK: same two flat colours, same steep colour and
        // the same two normal maps the moon's own material uses (read from
        // Constant Companion.mat + Shading.asset, 2026-09-22). _MainTex is only
        // a noise source now, like the moon's own noise texture.
        mat.SetTexture("_MainTex", albedo);
        mat.SetTexture("_NormalFlat", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/5 - External Imports/Celestial Body/Textures/Normals/Craters.tif"));
        mat.SetTexture("_NormalSteep", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/5 - External Imports/Celestial Body/Textures/Normals/Rock1.jpg"));
        mat.SetFloat("_Tiling", 2.5f);                 // the moon tiles its normals every ~2.5 m of surface
        mat.SetFloat("_NormalStrength", 0.589f);       // the moon's _NormalMapStrength
        mat.SetColor("_FlatColA", new Color(1f, 1f, 1f));
        mat.SetColor("_FlatColB", new Color(0.735849f, 0.735849f, 0.735849f));
        mat.SetColor("_SteepCol", new Color(0.057654828f, 0.046858326f, 0.084905684f));
        mat.SetColor("_SteepColInside", new Color(0.24f, 0.22f, 0.27f));
        mat.SetFloat("_ExposureFloor", 0.03f);
        mat.SetFloat("_ExposurePower", 1.6f);
        mat.SetColor("_Color", Color.white);
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ── generation ───────────────────────────────────────────────────────────

    static void Generate(CaveSolid.Recipe recipe, out Texture2D albedo, out Texture2D normal)
    {
        var height = new float[Size * Size];
        var cellId = new int[Size * Size];
        var crack = new float[Size * Size];

        int cells; float crackWidth; float fbmWeight; float streak; Color baseCol; float cellVar; float normalStrength;
        switch (recipe)
        {
            case CaveSolid.Recipe.Strata:
                cells = 7; crackWidth = 0.05f; fbmWeight = 0.55f; streak = 1f; cellVar = 0.10f; normalStrength = 2.2f;
                baseCol = new Color(0.56f, 0.57f, 0.60f); break;
            case CaveSolid.Recipe.Dripstone:
                cells = 4; crackWidth = 0.0f; fbmWeight = 0.7f; streak = 3.2f; cellVar = 0.06f; normalStrength = 1.4f;
                baseCol = new Color(0.62f, 0.54f, 0.44f); break;
            default:
                cells = 11; crackWidth = 0.09f; fbmWeight = 0.8f; streak = 1f; cellVar = 0.14f; normalStrength = 2.8f;
                baseCol = new Color(0.40f, 0.40f, 0.42f); break;
        }

        // Tileable Worley feature points on a wrapped grid.
        var rng = new System.Random((int)recipe * 977 + 13);
        var pts = new Vector2[cells * cells];
        for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());

        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = x / (float)Size, v = y / (float)Size;
                // fBm, tileable by blending four offset samples.
                // Streaks: a lower frequency along v (flowstone runs down the wall).
                float f = TileNoise(u, v, 3f, 3f / streak) * 0.5f + TileNoise(u, v, 7f, 7f / streak) * 0.3f + TileNoise(u, v, 17f, 17f / streak) * 0.14f + TileNoise(u, v, 41f, 41f / streak) * 0.06f;
                // Worley: nearest two feature points (wrapped).
                float f1 = 9f, f2 = 9f; int id = 0;
                int cx = (int)(u * cells), cy = (int)(v * cells);
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int gx = ((cx + ox) % cells + cells) % cells, gy = ((cy + oy) % cells + cells) % cells;
                        Vector2 fp = (pts[gy * cells + gx] + new Vector2(cx + ox, cy + oy)) / cells;
                        float d = (new Vector2(u, v) - fp).magnitude;
                        if (d < f1) { f2 = f1; f1 = d; id = gy * cells + gx; }
                        else if (d < f2) f2 = d;
                    }
                float edge = f2 - f1;                                   // 0 on cell borders
                float cr = crackWidth > 0f ? 1f - Mathf.SmoothStep(0f, crackWidth, edge) : 0f;
                int i = y * Size + x;
                cellId[i] = id;
                crack[i] = cr;
                float cellH = Hash(id) * 0.35f;                          // each block sits at its own height
                height[i] = f * fbmWeight + cellH * (crackWidth > 0f ? 1f : 0f) - cr * 0.45f;
            }

        albedo = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = $"Cave_Rock_{recipe}_Albedo", wrapMode = TextureWrapMode.Repeat };
        normal = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = $"Cave_Rock_{recipe}_Normal", wrapMode = TextureWrapMode.Repeat };
        var apx = new Color[Size * Size];
        var npx = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int i = y * Size + x;
                float h = height[i];
                float shade = Mathf.Lerp(0.72f, 1.18f, Mathf.Clamp01(h + 0.25f));
                float cv = 1f + (Hash(cellId[i] + 91) - 0.5f) * 2f * cellVar;
                Color c = baseCol * shade * cv;
                c = Color.Lerp(c, c * 0.45f, crack[i] * 0.9f);
                c.a = 1f;
                apx[i] = c;

                // Sobel normal, tileable.
                float hl = height[y * Size + ((x - 1 + Size) % Size)], hr = height[y * Size + ((x + 1) % Size)];
                float hd = height[((y - 1 + Size) % Size) * Size + x], hu = height[((y + 1) % Size) * Size + x];
                Vector3 n = new Vector3((hl - hr) * normalStrength, (hd - hu) * normalStrength, 1f).normalized;
                npx[i] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        albedo.SetPixels(apx); albedo.Apply(true);
        normal.SetPixels(npx); normal.Apply(true);
    }

    static float Hash(int i)
    {
        uint x = (uint)i * 2654435761u;
        x ^= x >> 13; x *= 0x5bd1e995u; x ^= x >> 15;
        return (x & 0xFFFFFF) / (float)0x1000000;
    }

    // Perlin blended around a torus so the tile wraps without a seam.
    static float TileNoise(float u, float v, float fu, float fv)
    {
        float a = Mathf.PerlinNoise(u * fu, v * fv);
        float b = Mathf.PerlinNoise((u - 1f) * fu, v * fv);
        float c = Mathf.PerlinNoise(u * fu, (v - 1f) * fv);
        float d = Mathf.PerlinNoise((u - 1f) * fu, (v - 1f) * fv);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }
}
