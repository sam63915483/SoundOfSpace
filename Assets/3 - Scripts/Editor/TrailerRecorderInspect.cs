using System.Text;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEngine;

public static class TrailerRecorderInspect
{
    public static string Execute()
    {
        var sb = new StringBuilder();
        string assetPath = "Assets/Recordings/TrailerRecorder.asset";

        var controller = AssetDatabase.LoadAssetAtPath<RecorderControllerSettings>(assetPath);
        if (controller == null)
        {
            sb.AppendLine("CONTROLLER ASSET NOT FOUND at " + assetPath);
        }
        else
        {
            var recorders = controller.RecorderSettings;
            int count = 0;
            foreach (var r in recorders) count++;
            sb.AppendLine($"Controller loaded. FrameRate={controller.FrameRate}");
            sb.AppendLine($"Recorder count = {count}");
            foreach (var r in recorders)
            {
                if (r == null) { sb.AppendLine("  <null recorder>"); continue; }
                sb.AppendLine($"  '{r.name}' enabled={r.Enabled} type={r.GetType().Name} output={r.OutputFile}");
                var movie = r as MovieRecorderSettings;
                if (movie != null)
                {
                    sb.AppendLine($"     format={movie.OutputFormat}");
                    var gv = movie.ImageInputSettings as UnityEditor.Recorder.Input.GameViewInputSettings;
                    if (gv != null) sb.AppendLine($"     gameView={gv.OutputWidth}x{gv.OutputHeight}");
                    else sb.AppendLine($"     inputSettings={movie.ImageInputSettings?.GetType().Name ?? "NULL"}");
                }
            }
        }

        // sub-assets actually saved in the file
        sb.AppendLine("Sub-assets in file:");
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            sb.AppendLine($"  {(o == null ? "null" : o.GetType().Name + " '" + o.name + "'")}");

        return sb.ToString();
    }
}
