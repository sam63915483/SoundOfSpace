using UnityEngine.SceneManagement;

/// <summary>
/// The tutorial box (docs/superpowers/specs/2026-09-14-tutorial-box-design.md).
///
/// One question, answered in one place: "are we in the tutorial scene right
/// now?" The codebase treats "any scene that isn't MainMenu" as the real game
/// (SaveCollector.IsGameplayScene, HUDSceneGate, every auto-singleton), which
/// is exactly what we want for HUDs and systems — the tutorial is meant to
/// look and play like the game — but NOT for anything that writes a save file
/// or reloads the solar-system scene. Those few places check IsActive.
///
/// Fenced by this flag (grep for TutorialSession.IsActive):
///   StasisPodSave        — the pod ritual plays as DOWNLOADING, never writes
///   DeathCutsceneController — in-place respawn, never loads a save
///   TabbedPauseMenu      — MULTIPLAYER row hidden (single-player only)
/// </summary>
public static class TutorialSession
{
    public const string SceneName = "Tutorial";
    public const string ScenePath = "Assets/4 - Scenes/Tutorial.unity";

    /// True while the ACTIVE scene is the tutorial box. Cheap (no allocation).
    public static bool IsActive => SceneManager.GetActiveScene().name == SceneName;
}
