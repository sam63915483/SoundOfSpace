using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CutsceneController : MonoBehaviour
{
    public Text flashbackText;
    public float displayDuration = 5f;

    void Start()
    {
        if (flashbackText)
            flashbackText.text = "[Flashback sequence — coming soon]";
        Invoke(nameof(LoadGameScene), displayDuration);
    }

    void LoadGameScene()
    {
        SceneManager.LoadScene("1.6.7.7.7");
    }
}
