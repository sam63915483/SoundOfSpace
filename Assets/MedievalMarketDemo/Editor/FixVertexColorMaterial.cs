using UnityEngine;
using UnityEditor;

public class FixVertexColorMaterial
{
    public static void Execute()
    {
        string matPath = "Assets/MedievalMarketDemo/Materials/M_VertexPaint_All.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { Debug.LogError("Material not found at: " + matPath); return; }

        Shader shader = Shader.Find("Custom/VertexColor_BuiltIn");
        if (shader == null) { Debug.LogError("Shader 'Custom/VertexColor_BuiltIn' not found. Make sure it compiled successfully."); return; }

        mat.shader = shader;
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        Debug.Log("Successfully reassigned M_VertexPaint_All to Custom/VertexColor_BuiltIn");
    }
}
