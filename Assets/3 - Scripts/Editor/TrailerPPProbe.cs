using System.Text;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEditor;

public static class TrailerPPProbe
{
    public static string Execute()
    {
        var sb = new StringBuilder();

        // Color space matters hugely for "washed vs rich"
        sb.AppendLine("activeColorSpace = " + QualitySettings.activeColorSpace);
        sb.AppendLine("PlayerSettings.colorSpace = " + PlayerSettings.colorSpace);
        sb.AppendLine();

        // PostProcessLayers on cameras
        sb.AppendLine("=== PostProcessLayer (on cameras) ===");
        var layers = Object.FindObjectsOfType<PostProcessLayer>(true);
        if (layers.Length == 0) sb.AppendLine("  NONE FOUND");
        foreach (var l in layers)
            sb.AppendLine($"  on '{l.gameObject.name}' enabled={l.enabled} aa={l.antialiasingMode} volumeLayer={l.volumeLayer.value}");

        // PostProcessVolumes
        sb.AppendLine();
        sb.AppendLine("=== PostProcessVolumes ===");
        var vols = Object.FindObjectsOfType<PostProcessVolume>(true);
        if (vols.Length == 0) sb.AppendLine("  NONE FOUND");
        foreach (var v in vols)
        {
            sb.AppendLine($"  '{v.gameObject.name}' active={v.gameObject.activeInHierarchy} enabled={v.enabled} isGlobal={v.isGlobal} priority={v.priority} weight={v.weight} layer={LayerMask.LayerToName(v.gameObject.layer)} profile={(v.sharedProfile ? v.sharedProfile.name : "NULL")}");
            if (v.sharedProfile != null)
            {
                foreach (var s in v.sharedProfile.settings)
                    sb.AppendLine($"        - {s.GetType().Name} active={s.active} enabled={s.enabled.value}");
            }
        }

        return sb.ToString();
    }
}
