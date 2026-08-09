using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Whether the pause menu is up, and whether that should stop the world.
///
/// ── Why this exists ──────────────────────────────────────────────────────
/// `Time.timeScale = 0` used to do two unrelated jobs at once: freeze the
/// simulation AND block player input (PlayerController's Update and FixedUpdate
/// both open with `if (Time.timeScale == 0) return;`). That conflation is why
/// the menu could not simply stop freezing things — nothing else was holding
/// the player still.
///
/// So the two jobs are now separate:
///   • MenuOpen      — "a menu owns the input right now". Always true while the
///                     pause menu is up, in every mode.
///   • WorldFreezes  — "…and the simulation should stop too". Single player only.
///
/// ── Why multiplayer does not freeze (Sam's call, 2026-08-09) ─────────────
/// Because there, pausing is a LIE. The other player keeps playing, the host
/// keeps orbiting, and only your machine stops — which is not a pause, it is a
/// desync. Three separate bugs came out of pretending otherwise: a paused player
/// drifting across their friend's screen, the planet sliding out from under a
/// paused guest, and a paused host rubber-banding live guests. Not freezing
/// removes the cause rather than defending against it.
///
/// Single player still freezes, deliberately. There is a 30-second drowning
/// timer, oxygen depletion, enemies that hunt, and a fall-damage window — being
/// able to die inside the settings menu is a real cost with nothing to buy it.
/// (Stardew Valley draws the line in exactly this place.)
///
/// ── If you add a new input reader ────────────────────────────────────────
/// Gate it on `MenuOpen`, not on `Time.timeScale`. In multiplayer the clock is
/// running while the menu is up, so a timeScale check will not save you.
/// TutorialGate.MovementInputSuppressed already folds this in and is the
/// broadest existing hook.
/// </summary>
public static class PauseState
{
    /// True while the pause menu is up, in ANY mode. The thing to gate input on.
    public static bool MenuOpen { get; private set; }

    /// True when opening the menu should also stop time — i.e. not in a live
    /// multiplayer session.
    public static bool WorldFreezes => !MultiplayerLive;

    static bool MultiplayerLive
    {
        get
        {
            var nm = NetworkManager.Singleton;
            return nm != null && nm.IsListening;
        }
    }

    /// Called by TabbedPauseMenu when the menu opens.
    public static void Enter()
    {
        MenuOpen = true;
        Time.timeScale = WorldFreezes ? 0f : 1f;
    }

    /// Called by TabbedPauseMenu when the menu closes, and by every other path
    /// that force-restores timeScale (scene changes, quit-to-menu, death).
    /// Safe to call when already closed.
    public static void Exit()
    {
        MenuOpen = false;
        Time.timeScale = 1f;
    }
}
