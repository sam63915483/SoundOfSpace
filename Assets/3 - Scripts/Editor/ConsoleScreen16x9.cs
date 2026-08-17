using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools ▸ TRAX ▸ Console Screen 16:9 — one click sets the shuttle's
/// ConsoleScreen to a 16:9 face (y = x * 9/16; at the current x 0.97 that's
/// y ≈ 0.5456, up from 0.5) in BOTH the prefab asset and the open scene's
/// instance, so the change survives and nothing is left as a stray override.
///
/// Patches via LoadPrefabContents like every other shuttle tool — the
/// Shuttle_Lander prefab is hand-maintained and must never be regenerated.
/// </summary>
public static class ConsoleScreen16x9
{
    const string PrefabPath = "Assets/1 - samsPrefabs/Shuttle_Lander.prefab";
    const string ScreenName = "ConsoleScreen";
    const float Aspect = 9f / 16f;

    [MenuItem("Tools/TRAX/Console Screen 16x9")]
    static void Apply()
    {
        // ── the prefab ───────────────────────────────────────────────────
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform screen = FindDeep(root.transform, ScreenName);
            if (screen == null)
            {
                Debug.LogError($"[ConsoleScreen16x9] '{ScreenName}' not found in {PrefabPath} — nothing changed.");
                return;
            }
            Vector3 s = screen.localScale;
            float newY = s.x * Aspect;
            screen.localScale = new Vector3(s.x, newY, s.z);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[ConsoleScreen16x9] prefab: scale y {s.y:F4} -> {newY:F4} (x {s.x:F4}).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // ── the open scene's instance (may carry its own override) ───────
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t.name != ScreenName) continue;
            Vector3 s = t.localScale;
            Undo.RecordObject(t, "Console Screen 16:9");
            t.localScale = new Vector3(s.x, s.x * Aspect, s.z);
            EditorUtility.SetDirty(t);
            Debug.Log($"[ConsoleScreen16x9] scene instance '{GetPath(t)}': scale y {s.y:F4} -> {s.x * Aspect:F4}. Save the scene to keep it.");
        }
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    static string GetPath(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
