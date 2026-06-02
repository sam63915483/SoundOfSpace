using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// In-memory log of every line HAL has volunteered via HALCommentator —
/// commentator reactions, ambient idle observations, enemy-proximity
/// warnings, anything that gets pushed through Volunteer(). The AI chat
/// screen reads this on open so the player sees a continuous transcript
/// of what HAL has been saying, instead of the lines vanishing with the
/// HUD strip.
///
/// AIChatScreen also subscribes to OnLineAdded so live volunteered lines
/// appear as new bubbles in the chat while it is open.
///
/// Auto-singleton with MainMenu skip — must also be seeded in
/// MainMenuController.EnsureGameplaySingletons per the trap in CLAUDE.md.
/// In-memory only; lines do not persist across full game restarts (saves
/// don't capture them yet).
/// </summary>
public class HALVolunteeredLog : MonoBehaviour
{
    public static HALVolunteeredLog Instance { get; private set; }

    // Hard cap so an extremely long play session doesn't blow up the chat
    // history when opened. Old lines drop off the front.
    const int MaxLines = 500;

    readonly List<string> _lines = new List<string>();

    public IReadOnlyList<string> Lines => _lines;

    /// Fires AFTER the line has been appended. AIChatScreen subscribes so it
    /// can add a live bubble while the player is reading the chat.
    public event Action<string> OnLineAdded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("HALVolunteeredLog");
        DontDestroyOnLoad(go);
        go.AddComponent<HALVolunteeredLog>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    public void Append(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        _lines.Add(line);
        if (_lines.Count > MaxLines) _lines.RemoveAt(0);
        OnLineAdded?.Invoke(line);
    }

    /// Wipes the transcript. Called by NewGameReset so a previous run's volunteered
    /// lines don't bleed into a fresh game. Does not raise OnLineAdded.
    public void Clear() => _lines.Clear();
}
