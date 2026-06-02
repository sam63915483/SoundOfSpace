using UnityEngine;
using UnityEngine.SceneManagement;

public static class PendingLoad
{
    public static SaveData Data;
    static bool _subscribed;

    public static void ScheduleLoad(SaveData data)
    {
        Data = data;
        if (!_subscribed)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            _subscribed = true;
        }
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Only apply in gameplay scenes — main menu doesn't need state.
        if (scene.name == "MainMenu") return;
        if (Data == null) { Unsubscribe(); return; }

        var data = Data;
        Data = null;
        Unsubscribe();

        var runnerGO = new GameObject("[SaveLoadRunner]");
        runnerGO.AddComponent<SaveLoadRunner>().Run(data);
    }

    static void Unsubscribe()
    {
        if (_subscribed)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _subscribed = false;
        }
    }
}
