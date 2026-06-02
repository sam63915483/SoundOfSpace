using UnityEngine;
using UnityEditor;

public class FixSceneMaterials
{
    public static void Execute()
    {
        string matPath = "Assets/MedievalMarketDemo/Materials/M_VertexPaint_All.mat";
        Material fixedMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (fixedMat == null) { Debug.LogError("Fixed material not found."); return; }

        string brokenGuid = "c3650a1a696f42f40b1359a83447f2c8";
        int fixedCount = 0;

        // Include inactive objects too
        Renderer[] all = Resources.FindObjectsOfTypeAll<Renderer>();
        foreach (Renderer r in all)
        {
            // Skip assets, only touch scene objects
            if (EditorUtility.IsPersistent(r)) continue;

            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                bool broken = mats[i] == null;
                if (!broken && mats[i] != null)
                {
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mats[i]));
                    broken = guid == brokenGuid || mats[i].shader.name.StartsWith("Hidden/");
                }
                if (broken)
                {
                    Debug.Log($"Fixing: {r.gameObject.name} slot {i}");
                    mats[i] = fixedMat;
                    changed = true;
                    fixedCount++;
                }
            }
            if (changed)
            {
                r.sharedMaterials = mats;
                EditorUtility.SetDirty(r);
            }
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"Fixed {fixedCount} material slot(s) in scene.");
    }
}
