using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class FlashbackManager : MonoBehaviour
{
    public static FlashbackManager Instance { get; private set; }

    [Header("References")]
    public Image fadeOverlay;

    [Header("Settings")]
    public string nextSceneName = "Cutscene";
    public float fadeDuration = 3f;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (fadeOverlay) fadeOverlay.color = new Color(0, 0, 0, 0);
    }

    public void TriggerCutscene()
    {
        StartCoroutine(FadeThenLoad());
    }

    IEnumerator FadeThenLoad()
    {
        if (fadeOverlay) fadeOverlay.gameObject.SetActive(true);
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float a = Mathf.Clamp01(elapsed / fadeDuration);
            if (fadeOverlay) fadeOverlay.color = new Color(0, 0, 0, a);
            yield return null;
        }
        SceneManager.LoadScene(nextSceneName);
    }
}
