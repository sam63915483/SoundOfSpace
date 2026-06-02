using UnityEngine;
using UnityEditor;
using System.IO;

public class FixPrefabMaterials
{
    public static void Execute()
    {
        string matPath = "Assets/MedievalMarketDemo/Materials/M_VertexPaint_All.mat";
        Material fixedMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (fixedMat == null) { Debug.LogError("Could not find fixed material at: " + matPath); return; }

        string brokenShaderGuid = "c3650a1a696f42f40b1359a83447f2c8";

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/MedievalMarketDemo/Prefabs" });
        int fixedCount = 0;
        int prefabCount = 0;

        // Log first prefab's material info for diagnosis
        bool logged = false;

        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            bool changed = false;
            using (var editScope = new PrefabUtility.EditPrefabContentsScope(path))
            {
                GameObject root = editScope.prefabContentsRoot;
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (!logged)
                        {
                            if (mats[i] == null)
                                Debug.Log($"[Diag] Prefab={path} mat=NULL");
                            else
                            {
                                string ap = AssetDatabase.GetAssetPath(mats[i]);
                                string mg = AssetDatabase.AssetPathToGUID(ap);
                                Debug.Log($"[Diag] Prefab={path} mat={mats[i].name} shader={mats[i].shader.name} assetPath={ap} guid={mg}");
                            }
                            logged = true;
                        }

                        // null means Unity couldn't load the sub-asset (shadergraph in Built-in RP)
                        bool isBroken = mats[i] == null
                            || AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mats[i])) == brokenShaderGuid
                            || mats[i].shader.name.StartsWith("Hidden/");
                        if (isBroken)
                        {
                            mats[i] = fixedMat;
                            changed = true;
                            fixedCount++;
                        }
                    }
                    if (changed) r.sharedMaterials = mats;
                }
            }

            if (changed) prefabCount++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Fixed {fixedCount} material slot(s) across {prefabCount} prefab(s).");
    }
}
