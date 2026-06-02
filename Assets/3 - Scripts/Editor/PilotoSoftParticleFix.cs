using UnityEngine;
using UnityEditor;

/// <summary>
/// One-off editor utility. The Piloto Blood VFX Essentials materials enable a
/// "Use Soft Particles?" shader keyword (_USESOFTALPHA) that fades particles
/// against scene depth. In Built-in RP without a camera depth texture, that
/// fade collapses to zero alpha and whole particle layers render invisible
/// (per the pack README). This disables the soft-particle keyword on every
/// Piloto material that exposes it, so all layers render fully regardless of
/// camera setup. Fully reversible (re-enable the keyword to restore).
///
/// Run via Unity MCP execute_script, or the menu item below.
/// </summary>
public static class PilotoSoftParticleFix
{
    [MenuItem("Tools/Piloto/Disable Soft Particles On Blood VFX")]
    public static void Execute()
    {
        int scanned = 0, changed = 0;
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Piloto Studio" });
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;
            // Only touch materials that actually expose the soft-particle toggle.
            bool hasUpper = mat.HasProperty("_USESOFTALPHA");
            bool hasMixed = mat.HasProperty("_UseSoftAlpha");
            if (!hasUpper && !hasMixed) continue;
            scanned++;

            bool touched = false;
            if (mat.IsKeywordEnabled("_USESOFTALPHA")) { mat.DisableKeyword("_USESOFTALPHA"); touched = true; }
            if (hasUpper && mat.GetFloat("_USESOFTALPHA") != 0f) { mat.SetFloat("_USESOFTALPHA", 0f); touched = true; }
            if (hasMixed && mat.GetFloat("_UseSoftAlpha") != 0f) { mat.SetFloat("_UseSoftAlpha", 0f); touched = true; }
            if (touched) { EditorUtility.SetDirty(mat); changed++; }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[PilotoSoftParticleFix] Soft particles disabled on {changed} material(s) " +
                  $"({scanned} had the toggle, {guids.Length} materials scanned under Assets/Piloto Studio).");
    }
}
