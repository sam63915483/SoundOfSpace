using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Dev shortcut: hold Shift + D and press 1-8 to jump straight into that dimension
/// (via the normal PortalManager path, so inventory carry + autosave behave exactly
/// like a real entry). Auto-singleton; seeded in MainMenuController for builds (trap #1).
/// </summary>
public class DimensionDevLoader : MonoBehaviour
{
    public static DimensionDevLoader Instance;

    static readonly string[] Scenes =
    {
        "D1_ShiftingHalls", "D2_DuneSea", "D3_LongDark", "D4_WaitingField",
        "D5_Archive", "D6_FrozenSea", "D7_HallOfDoors", "D8_Procession",
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        if (Instance != null) return;
        var go = new GameObject("DimensionDevLoader");
        DontDestroyOnLoad(go);
        go.AddComponent<DimensionDevLoader>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift)) return;
        if (!Input.GetKey(KeyCode.D)) return;
        for (int i = 0; i < Scenes.Length; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                Debug.Log("[DimensionDevLoader] jumping to " + Scenes[i]);
                PortalManager.EnterInterior(Scenes[i]);
                return;
            }
    }
}
