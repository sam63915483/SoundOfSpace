#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public class FixLowPolyMarketMaterials
{
    public static void Execute()
    {
        Shader standard = Shader.Find("Standard");
        if (standard == null) { Debug.LogError("Could not find Standard shader."); return; }

        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Low-Poly Medieval Market/Materials" });
        int fixed_count = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            // Pink = shader missing or Hidden/ error shader
            bool isBroken = mat.shader == null || mat.shader.name.StartsWith("Hidden/");
            if (!isBroken) continue;

            // Grab the existing main texture before swapping shader
            Texture mainTex = mat.GetTexture("_MainTex");
            if (mainTex == null) mainTex = mat.GetTexture("_BaseMap");

            mat.shader = standard;

            if (mainTex != null)
                mat.SetTexture("_MainTex", mainTex);

            // Enable alpha cutout if the material had it (for foliage/fence cutouts)
            // We'll leave it opaque unless you need it

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            Debug.Log($"Fixed: {mat.name} (path={path})");
            fixed_count++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"Done. Fixed {fixed_count} material(s).");
    }
}
#endif
