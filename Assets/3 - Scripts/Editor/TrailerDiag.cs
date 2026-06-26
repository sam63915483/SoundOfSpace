using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

public static class TrailerDiag
{
    public static string Execute()
    {
        var sb = new StringBuilder();

        // ---- Quality levels ----
        var names = QualitySettings.names;
        int current = QualitySettings.GetQualityLevel();
        sb.AppendLine("=== QUALITY ===");
        for (int i = 0; i < names.Length; i++)
            sb.AppendLine($"  [{i}] {names[i]}{(i == current ? "  <-- EDITOR CURRENT" : "")}");

        // What the BUILD (Standalone) actually launches with
        var so = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
        var perPlat = so.FindProperty("m_PerPlatformDefaultQuality");
        if (perPlat != null)
        {
            for (int i = 0; i < perPlat.arraySize; i++)
            {
                var pair = perPlat.GetArrayElementAtIndex(i);
                var key = pair.FindPropertyRelative("first");
                var val = pair.FindPropertyRelative("second");
                if (key != null && val != null)
                    sb.AppendLine($"  default[{key.stringValue}] = level {val.intValue}" +
                        (val.intValue < names.Length ? $" ({names[val.intValue]})" : ""));
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== CURRENT QUALITY DETAIL ===");
        sb.AppendLine($"  antiAliasing(MSAA)   = {QualitySettings.antiAliasing}");
        sb.AppendLine($"  pixelLightCount      = {QualitySettings.pixelLightCount}");
        sb.AppendLine($"  shadows              = {QualitySettings.shadows}");
        sb.AppendLine($"  shadowResolution     = {QualitySettings.shadowResolution}");
        sb.AppendLine($"  shadowDistance       = {QualitySettings.shadowDistance}");
        sb.AppendLine($"  globalTextureMipBias = {QualitySettings.globalTextureMipmapLimit}");
        sb.AppendLine($"  vSyncCount           = {QualitySettings.vSyncCount}");

        // ---- Game view size ----
        sb.AppendLine();
        sb.AppendLine("=== GAME VIEW ===");
        var tGV = System.Type.GetType("UnityEditor.PlayModeWindow, UnityEditor");
        var mainGameSize = UnityStats.screenRes;
        sb.AppendLine($"  UnityStats.screenRes = {mainGameSize}");

        // ---- Main camera ----
        sb.AppendLine();
        sb.AppendLine("=== CAMERAS ===");
        foreach (var cam in Object.FindObjectsOfType<Camera>())
        {
            sb.AppendLine($"  '{cam.name}'  depth={cam.depth} HDR={cam.allowHDR} MSAA={cam.allowMSAA} " +
                $"dynRes={cam.allowDynamicResolution} pixelRect={cam.pixelWidth}x{cam.pixelHeight}");
        }

        // ---- Flares / Lens flares ----
        sb.AppendLine();
        sb.AppendLine("=== FLARES (legacy LensFlare components) ===");
        var lensFlares = Object.FindObjectsOfType<LensFlare>();
        if (lensFlares.Length == 0) sb.AppendLine("  (none)");
        foreach (var lf in lensFlares)
        {
            sb.AppendLine($"  '{lf.name}' brightness={lf.brightness} fadeSpeed={lf.fadeSpeed} " +
                $"color={lf.color} flareAsset={(lf.flare ? lf.flare.name : "NULL")}");
        }

        // ---- Anything with 'flare'/'sun' in the name ----
        sb.AppendLine();
        sb.AppendLine("=== OBJECTS NAMED flare/sun/glow ===");
        foreach (var go in Object.FindObjectsOfType<GameObject>())
        {
            string n = go.name.ToLower();
            if (n.Contains("flare") || n.Contains("sun") || n.Contains("glow") || n.Contains("bloom"))
                sb.AppendLine($"  {go.name}  (active={go.activeInHierarchy}) comps=[{ComponentList(go)}]");
        }

        sb.AppendLine();
        sb.AppendLine("=== POST PROCESSING ===");
        sb.AppendLine($"  GraphicsSettings.renderPipelineAsset = " +
            $"{(GraphicsSettings.defaultRenderPipeline ? GraphicsSettings.defaultRenderPipeline.name : "NULL (Built-in RP)")}");

        return sb.ToString();
    }

    static string ComponentList(GameObject go)
    {
        var sb = new StringBuilder();
        foreach (var c in go.GetComponents<Component>())
        {
            if (c == null) continue;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(c.GetType().Name);
        }
        return sb.ToString();
    }
}
