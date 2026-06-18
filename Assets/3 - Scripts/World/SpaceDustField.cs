using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Purely-visual glowing amber "space dust" field. Auto-singleton (mirrors SpaceDustInventory).
/// Renders specks that drift toward the black hole, form spiral arms, thicken near the BH and
/// away from planets, vanish inside atmospheres, and are immune to floating-origin rebases.
/// </summary>
public class SpaceDustField : MonoBehaviour
{
    public static SpaceDustField Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("SpaceDustField");
        DontDestroyOnLoad(go);
        go.AddComponent<SpaceDustField>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Debug.Log("[SpaceDustField] created");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
