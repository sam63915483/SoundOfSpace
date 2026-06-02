using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-off editor utility to wire the scene's BloodFX component:
/// fills damageSplashPrefabs with every prefab in the Blood Splashes folder
/// (useful after a pack reimport). Run via Unity MCP execute_script.
/// </summary>
public static class BloodFXWire
{
    public static void Execute()
    {
        var bloodFX = Object.FindObjectOfType<BloodFX>(true);
        if (bloodFX == null) { Debug.LogError("[BloodFXWire] BloodFX not found in the open scene."); return; }

        var so = new SerializedObject(bloodFX);

        string[] guids = AssetDatabase.FindAssets("t:Prefab",
            new[] { "Assets/Piloto Studio/Blood VFX Essentials/Blood Splashes" });
        var arr = so.FindProperty("damageSplashPrefabs");
        arr.arraySize = guids.Length;
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            arr.GetArrayElementAtIndex(i).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(bloodFX);
        EditorSceneManager.MarkSceneDirty(bloodFX.gameObject.scene);

        Debug.Log($"[BloodFXWire] damageSplashPrefabs filled with {guids.Length} prefab(s). " +
                  $"Scene marked dirty (save separately).");
    }
}
