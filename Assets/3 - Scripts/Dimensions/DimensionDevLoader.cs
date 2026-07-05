using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Dev shortcut, TV-remote style: hold Shift + D and type digits (1..28) — they
/// accumulate into a buffer shown on screen, and 3 seconds after the last digit the
/// player jumps into that dimension (via the normal PortalManager path, so inventory
/// carry + autosave behave exactly like a real entry). Typing a new digit restarts
/// the countdown; releasing Shift+D doesn't cancel a pending jump.
/// Auto-singleton; seeded in MainMenuController for builds (trap #1).
/// </summary>
public class DimensionDevLoader : MonoBehaviour
{
    public static DimensionDevLoader Instance;

    static readonly string[] Scenes =
    {
        "D1_ShiftingHalls", "D2_DuneSea", "D3_LongDark", "D4_WaitingField",
        "D5_Archive", "D6_FrozenSea", "D7_HallOfDoors", "D8_Procession",
        "D9_RedForest", "D10_SaltFlats", "D11_Shelves", "D12_MirrorLake",
        "D13_Orchard", "D14_GlacierThroat", "D15_Congregation", "D16_NeonGrid",
        "D17_TidePools", "D18_StaticField", "D19_BoneGarden", "D20_CloudShelf",
        "D21_ArchiveStacks", "D22_RustSea", "D23_WheatAtDusk", "D24_WaitingRoom",
        "D25_CandleSea", "D26_Ferry", "D27_InvertedRain", "D28_LongTable",
    };

    const float FireDelay = 3f;

    string _buffer = "";
    float _fireAt = -1f;
    GUIStyle _style;

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
        bool chordHeld = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                      && Input.GetKey(KeyCode.D);
        if (chordHeld)
            for (int d = 0; d <= 9; d++)
                if (Input.GetKeyDown(KeyCode.Alpha0 + d) || Input.GetKeyDown(KeyCode.Keypad0 + d))
                {
                    if (_buffer.Length < 2) _buffer += (char)('0' + d);
                    else _buffer = d.ToString();            // overflow: start a fresh number
                    _fireAt = Time.unscaledTime + FireDelay;
                }

        if (_fireAt > 0f && Time.unscaledTime >= _fireAt) Fire();
    }

    void Fire()
    {
        string typed = _buffer;
        _buffer = "";
        _fireAt = -1f;
        if (!int.TryParse(typed, out int n) || n < 1 || n > Scenes.Length)
        {
            Debug.LogWarning("[DimensionDevLoader] invalid dimension '" + typed + "' (1-" + Scenes.Length + ")");
            return;
        }
        string scene = Scenes[n - 1];
        if (!Application.CanStreamedLevelBeLoaded(scene))
        {
            Debug.LogWarning("[DimensionDevLoader] scene '" + scene + "' is not in Build Settings yet — skipping");
            return;
        }
        Debug.Log("[DimensionDevLoader] jumping to " + scene);
        PortalManager.EnterInterior(scene);
    }

    void OnGUI()
    {
        if (_fireAt < 0f || _buffer.Length == 0) return;
        if (_style == null)
            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        float secondsLeft = Mathf.Max(0f, _fireAt - Time.unscaledTime);
        string text = "Dimension " + _buffer + " in " + secondsLeft.ToString("0.0") + "s";
        var rect = new Rect(0f, Screen.height * 0.12f, Screen.width, 40f);
        _style.normal.textColor = Color.black;
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, _style);
        _style.normal.textColor = new Color(0.8f, 1f, 0.9f);
        GUI.Label(rect, text, _style);
    }
}
