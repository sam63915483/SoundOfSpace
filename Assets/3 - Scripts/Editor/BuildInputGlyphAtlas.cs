using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using TMPro;

// One-shot (rerunnable) builder for the controller-prompt glyph atlas.
// Packs the canonical PNGs in Assets/InputPrompts/src into a single atlas,
// creates/updates a TMP_SpriteAsset whose sprite names match the file names
// (xbox_a, ps_cross, ...), and registers it as a global TMP fallback so
// <sprite name="xbox_a"> resolves in every TMP text in the game.
public static class BuildInputGlyphAtlas
{
    const string SrcDir = "Assets/InputPrompts/src";
    const string AtlasPath = "Assets/InputPrompts/InputGlyphAtlas.png";
    const string SpriteAssetPath = "Assets/InputPrompts/InputGlyphs.asset";

    // TMP scales sprite glyphs by (text size / font sampling point size), so
    // 64-px source icons render tiny next to a high-sampling SDF font. 2.5×
    // puts a button icon at roughly capital-letter-and-a-bit height.
    const float GlyphScale = 2.5f;

    [MenuItem("Tools/Input/Build Glyph Atlas + TMP Sprite Asset")]
    public static void Build()
    {
        var pngs = Directory.GetFiles(SrcDir, "*.png").OrderBy(p => p).ToArray();
        if (pngs.Length == 0) { Debug.LogError($"[InputGlyphs] no PNGs in {SrcDir}"); return; }

        foreach (var p in pngs)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(p.Replace('\\', '/'));
            if (ti != null && !ti.isReadable) { ti.isReadable = true; ti.SaveAndReimport(); }
        }
        var texs = pngs.Select(p => AssetDatabase.LoadAssetAtPath<Texture2D>(p.Replace('\\', '/'))).ToArray();
        var names = pngs.Select(Path.GetFileNameWithoutExtension).ToArray();

        var atlas = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
        var rects = atlas.PackTextures(texs, 4, 1024);
        File.WriteAllBytes(AtlasPath, atlas.EncodeToPNG());
        AssetDatabase.ImportAsset(AtlasPath);
        var ati = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
        ati.textureType = TextureImporterType.Sprite;
        ati.mipmapEnabled = false;
        ati.SaveAndReimport();
        var atlasTex = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);

        var sa = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(SpriteAssetPath);
        if (sa == null)
        {
            sa = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            AssetDatabase.CreateAsset(sa, SpriteAssetPath);
        }
        // Version stamp skips TMP's legacy spriteInfoList upgrade path, which
        // NPEs on a freshly created (never-serialized) sprite asset. The
        // property is read-only in TMP 3.0.7, so set the backing field.
        typeof(TMP_SpriteAsset)
            .GetField("m_Version", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(sa, "1.1.0");
        sa.spriteSheet = atlasTex;
        sa.spriteGlyphTable.Clear();
        sa.spriteCharacterTable.Clear();

        for (int i = 0; i < texs.Length; i++)
        {
            var r = rects[i];
            var gr = new GlyphRect(
                Mathf.RoundToInt(r.x * atlasTex.width), Mathf.RoundToInt(r.y * atlasTex.height),
                Mathf.RoundToInt(r.width * atlasTex.width), Mathf.RoundToInt(r.height * atlasTex.height));
            // Metrics: sit the icon on the baseline, ~80% above like a capital.
            var glyph = new TMP_SpriteGlyph {
                index = (uint)i, glyphRect = gr, scale = 1f,
                metrics = new GlyphMetrics(gr.width, gr.height, 0f, gr.height * 0.8f, gr.width)
            };
            sa.spriteGlyphTable.Add(glyph);
            var ch = new TMP_SpriteCharacter(0xFFFE, glyph) { name = names[i], scale = GlyphScale };
            sa.spriteCharacterTable.Add(ch);
        }

        if (sa.material == null)
        {
            var mat = new Material(Shader.Find("TextMeshPro/Sprite"));
            mat.name = "InputGlyphs Material";
            AssetDatabase.AddObjectToAsset(mat, sa);
            sa.material = mat;
        }
        sa.material.mainTexture = atlasTex;
        sa.UpdateLookupTables();
        EditorUtility.SetDirty(sa);

        // Register as a global fallback so every TMP text resolves the names.
        var def = TMP_Settings.defaultSpriteAsset;
        if (def != null && def != sa)
        {
            if (def.fallbackSpriteAssets == null)
                def.fallbackSpriteAssets = new System.Collections.Generic.List<TMP_SpriteAsset>();
            if (!def.fallbackSpriteAssets.Contains(sa)) def.fallbackSpriteAssets.Add(sa);
            EditorUtility.SetDirty(def);
        }
        else if (def == null)
        {
            var so = new SerializedObject(TMP_Settings.instance);
            var prop = so.FindProperty("m_defaultSpriteAsset");
            if (prop != null)
            {
                prop.objectReferenceValue = sa;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(TMP_Settings.instance);
            }
            else Debug.LogError("[InputGlyphs] couldn't find m_defaultSpriteAsset on TMP Settings");
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[InputGlyphs] Built atlas with {texs.Length} sprites into {SpriteAssetPath}.");
    }
}
