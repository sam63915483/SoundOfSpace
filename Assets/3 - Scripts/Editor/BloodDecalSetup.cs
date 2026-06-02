using UnityEditor;
using UnityEngine;

/// <summary>
/// One-off editor utility: builds the blood-decal Projector material + prefab
/// (Custom/BloodDecalProjector shader + Blood_Decal_circle texture) and wires it
/// into the scene BloodFX as poolPrefab. Run via Unity MCP execute_script.
/// </summary>
public static class BloodDecalSetup
{
    const string Dir = "Assets/BloodFX_Generated";
    const string DecalTexGuid = "7fc4c140de5e23d458ebbce28f3b8f65"; // Blood_Decal_circle.png

    public static void Execute()
    {
        if (!AssetDatabase.IsValidFolder(Dir))
            AssetDatabase.CreateFolder("Assets", "BloodFX_Generated");

        var shader = Shader.Find("Custom/BloodDecalProjector");
        if (shader == null) { Debug.LogError("[BloodDecalSetup] shader 'Custom/BloodDecalProjector' not found."); return; }

        string texPath = AssetDatabase.GUIDToAssetPath(DecalTexGuid);
        var tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);

        // Material
        string matPath = Dir + "/BloodDecal.mat";
        var mat = new Material(shader);
        mat.SetColor("_Color", new Color(0.45f, 0.02f, 0.02f, 1f));
        if (tex != null) mat.SetTexture("_ShadowTex", tex);
        AssetDatabase.CreateAsset(mat, matPath);

        // Projector prefab
        var go = new GameObject("BloodDecal_Projector");
        var proj = go.AddComponent<Projector>();
        proj.orthographic = true;
        proj.orthographicSize = 1f;
        proj.nearClipPlane = 0.05f;
        proj.farClipPlane = 3f;
        proj.aspectRatio = 1f;
        proj.material = mat;
        go.AddComponent<BloodDecalFader>();

        string prefabPath = Dir + "/BloodDecal_Projector.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);

        // Wire poolPrefab on the scene BloodFX
        var bloodFX = Object.FindObjectOfType<BloodFX>(true);
        bool wired = false;
        if (bloodFX != null)
        {
            var so = new SerializedObject(bloodFX);
            so.FindProperty("poolPrefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bloodFX);
            wired = true;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[BloodDecalSetup] tex='{texPath}' (null={tex == null}); created {matPath} + {prefabPath}; poolPrefab wired={wired}. Save the scene to persist the wiring.");
    }
}
