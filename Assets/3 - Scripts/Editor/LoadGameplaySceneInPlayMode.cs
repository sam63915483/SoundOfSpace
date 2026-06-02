using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LoadGameplaySceneInPlayMode
{
    public static void Execute()
    {
        // Force-load the gameplay scene to simulate user clicking New Game.
        SceneManager.LoadScene("1.6.7.7.7");
        Debug.Log("[LoadGameplaySceneInPlayMode] Triggered LoadScene('1.6.7.7.7')");
    }
}
