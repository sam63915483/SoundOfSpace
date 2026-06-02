using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class CreateCutsceneScene
{
    public static void Execute()
    {
        string scenePath = "Assets/4 - Scenes/Cutscene.unity";

        // Create the new scene
        var newScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Set camera background to black
        var cam = Object.FindObjectOfType<Camera>();
        if (cam != null)
            cam.backgroundColor = Color.black;

        // Create Canvas
        var canvasGO = new GameObject("Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // Create flashback text
        var textGO = new GameObject("FlashbackText");
        textGO.transform.SetParent(canvasGO.transform, false);
        var text = textGO.AddComponent<UnityEngine.UI.Text>();
        text.text = "[Flashback sequence — coming soon]";
        text.fontSize = 32;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font != null) text.font = font;
        var rt = textGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // Create CutsceneController
        var controllerGO = new GameObject("CutsceneController");
        var controller = controllerGO.AddComponent<CutsceneController>();
        controller.flashbackText = text;
        controller.displayDuration = 5f;

        // Save the scene
        EditorSceneManager.SaveScene(newScene, scenePath);

        // Add both scenes to build settings
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        bool hasGame = false, hasCutscene = false;
        foreach (var s in scenes)
        {
            if (s.path.Contains("1.6.7.7.7")) hasGame = true;
            if (s.path.Contains("Cutscene")) hasCutscene = true;
        }
        if (!hasGame)
            scenes.Insert(0, new EditorBuildSettingsScene("Assets/1.6.7.7.7.unity", true));
        if (!hasCutscene)
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();

        Debug.Log("Cutscene scene created and added to Build Settings.");

        // Reload main scene
        EditorSceneManager.OpenScene("Assets/1.6.7.7.7.unity");
    }
}
