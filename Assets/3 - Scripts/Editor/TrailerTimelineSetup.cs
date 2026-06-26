using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEditor.Recorder.Timeline;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public static class TrailerTimelineSetup
{
    public static string Execute()
    {
        // ---- output folder (outside Assets) ----
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outDir = Path.Combine(projectRoot, "Recordings").Replace("\\", "/");
        Directory.CreateDirectory(outDir);

        string assetDir = "Assets/Recordings";
        if (!AssetDatabase.IsValidFolder(assetDir))
            AssetDatabase.CreateFolder("Assets", "Recordings");
        string timelinePath = assetDir + "/TrailerCapture.playable";

        // wipe any previous one so re-runs are clean
        AssetDatabase.DeleteAsset(timelinePath);

        // ---- timeline + recorder track + clip ----
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(timeline, timelinePath);

        var track = timeline.CreateTrack<RecorderTrack>(null, "Recorder");
        var tClip = track.CreateClip<RecorderClip>();
        tClip.displayName = "Trailer 1440p";
        tClip.start = 0;
        tClip.duration = 10;   // seconds — auto-stops here

        var rClip = (RecorderClip)tClip.asset;

        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "Trailer 1440p";
        movie.Enabled = true;
        movie.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
        movie.VideoBitRateMode = VideoBitrateMode.High;
        movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 2560, OutputHeight = 1440 };
        movie.AudioInputSettings.PreserveAudio = true;
        movie.FrameRate = 60;
        movie.CapFrameRate = true;
        movie.OutputFile = outDir + "/flaretest_<Take>";

        rClip.settings = movie;
        AssetDatabase.AddObjectToAsset(movie, timeline);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ---- director in the scene to drive it on Play ----
        var go = GameObject.Find("TrailerDirector");
        if (go == null) go = new GameObject("TrailerDirector");
        var dir = go.GetComponent<PlayableDirector>();
        if (dir == null) dir = go.AddComponent<PlayableDirector>();
        dir.playableAsset = timeline;
        dir.playOnAwake = true;
        dir.extrapolationMode = DirectorWrapMode.None;

        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;

        return "OK\n" +
               "Timeline: " + timelinePath + " (10s recorder clip)\n" +
               "Director GameObject: 'TrailerDirector' (Play On Awake)\n" +
               "Output: " + outDir + "/flaretest_001.mp4\n" +
               "Press PLAY in the Editor -> it records 10s at 2560x1440 then stops automatically.";
    }
}
