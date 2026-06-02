using UnityEngine;
using UnityEditor;

public class DiagSceneMaterials
{
    public static void Execute()
    {
        // Find all renderers in the scene whose material is missing/null or uses a hidden shader
        Renderer[] all = Object.FindObjectsOfType<Renderer>();
        foreach (Renderer r in all)
        {
            foreach (Material m in r.sharedMaterials)
            {
                string matInfo = m == null ? "NULL" : $"{m.name} | shader={m.shader.name}";
                string path = AssetDatabase.GetAssetPath(m);
                Debug.Log($"GO={r.gameObject.name} mat={matInfo} path={path}");
            }
        }
    }
}
