using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DEBUG: the / (question-mark) key toggles every Light under the player on
/// and off — Sam's test rig for chasing the one-frame black flash on origin
/// shifts (suspect: the dim fill light riding the player). Lists what it
/// toggled so there's no guessing which lights exist.
/// </summary>
public class PlayerLightToggle : MonoBehaviour
{
    public static PlayerLightToggle Instance { get; private set; }

    bool _off;
    readonly List<Light> _held = new List<Light>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("[PlayerLightToggle]");
        DontDestroyOnLoad(go);
        go.AddComponent<PlayerLightToggle>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Slash)) return;

        var player = GameObject.FindWithTag("Player");
        if (player == null) return;

        if (!_off)
        {
            _held.Clear();
            var names = new System.Text.StringBuilder();
            foreach (var l in player.GetComponentsInChildren<Light>(false))
            {
                if (l == null || !l.enabled) continue;
                l.enabled = false;
                _held.Add(l);
                names.Append(names.Length == 0 ? "" : ", ").Append(l.name);
            }
            _off = true;
            Debug.Log($"[PlayerLightToggle] OFF: {names}");
            InteractPromptUI.ShowOneShot($"player lights OFF ({_held.Count}: {names})", 3f);
        }
        else
        {
            int n = 0;
            foreach (var l in _held) if (l != null) { l.enabled = true; n++; }
            _held.Clear();
            _off = false;
            Debug.Log("[PlayerLightToggle] back ON");
            InteractPromptUI.ShowOneShot($"player lights ON ({n})", 3f);
        }
    }
}
