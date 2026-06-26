using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEngine;

public static class TrailerRecorderSetup
{
    public static string Execute()
    {
        // ---- output folder (outside Assets so clips don't get imported) ----
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outDir = Path.Combine(projectRoot, "Recordings");
        Directory.CreateDirectory(outDir);

        // ---- controller settings asset ----
        string assetDir = "Assets/Recordings";
        if (!AssetDatabase.IsValidFolder(assetDir))
            AssetDatabase.CreateFolder("Assets", "Recordings");
        string assetPath = assetDir + "/TrailerRecorder.asset";

        var controller = ScriptableObject.CreateInstance<RecorderControllerSettings>();

        // ---- movie recorder: MP4 / H.264, 2560x1440, 60fps, high bitrate ----
        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "Trailer 1440p";
        movie.Enabled = true;
        movie.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
        movie.VideoBitRateMode = VideoBitrateMode.High;

        var gv = new GameViewInputSettings
        {
            OutputWidth = 2560,
            OutputHeight = 1440
        };
        movie.ImageInputSettings = gv;

        movie.AudioInputSettings.PreserveAudio = true;
        movie.OutputFile = outDir.Replace("\\", "/") + "/shot_<Take>";

        controller.AddRecorderSettings(movie);
        controller.SetRecordModeToManual();           // press Stop when you're done
        controller.FrameRate = 60;
        controller.CapFrameRate = true;

        AssetDatabase.CreateAsset(controller, assetPath);
        // MovieRecorderSettings must live as a sub-asset of the controller
        AssetDatabase.AddObjectToAsset(movie, controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ---- open the Recorder window with this config loaded ----
        var win = EditorWindow.GetWindow<RecorderWindow>();
        win.SetRecorderControllerSettings(controller);
        win.titleContent = new GUIContent("Recorder");
        win.Show();

        return "OK\n" +
               "Recorder settings asset: " + assetPath + "\n" +
               "Output folder: " + outDir + "\n" +
               "Resolution: 2560x1440 | MP4/H.264 | 60fps | High bitrate | mode=Manual";
    }
}
