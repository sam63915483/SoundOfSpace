using System.Collections.Generic;

// Per-blueprint lock for the build menu. Two states:
//   • Inactive (default) — no restrictions; every BuildableEntry shows in the
//     menu. This is the post-tutorial / sandbox state.
//   • Active — only entries whose displayName is in _unlocked are shown. Used
//     during the build tutorial (Phase 6) to restrict the player to cabin/
//     torch/bonfire while they learn.
//
// ⚠ NOT CONSULTED BY THE BUILD MENU ANY MORE. The live gate is
// BuildableUnlocks (Colonizer level → blueprints), which BuildMenuUI and
// PhoneBuildApp both ask. This class is kept because it's in the save schema
// and the retired tutorial steps still call it — but wiring it back into the
// menu would black the whole thing out on a new game, since NewGameReset calls
// LockAllExcept() with no arguments, i.e. "lock everything".
//
// Saved/restored via SaveCollector.
public static class BuildMenuLock
{
    static readonly HashSet<string> _unlocked = new HashSet<string>();

    /// When false, IsUnlocked always returns true (no restrictions).
    public static bool IsLockingActive { get; private set; }

    /// True if `name` is allowed to appear in the build menu.
    public static bool IsUnlocked(string name) =>
        !IsLockingActive || (!string.IsNullOrEmpty(name) && _unlocked.Contains(name));

    /// Adds a single entry to the unlocked set. Only meaningful while locking
    /// is active — call LockAllExcept first to enter restricted mode.
    public static void Unlock(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        _unlocked.Add(name);
    }

    /// Enter restricted mode with only the listed names allowed.
    public static void LockAllExcept(params string[] names)
    {
        IsLockingActive = true;
        _unlocked.Clear();
        if (names != null)
            foreach (var n in names)
                if (!string.IsNullOrEmpty(n)) _unlocked.Add(n);
    }

    /// Exit restricted mode — every blueprint shows in the menu again.
    public static void UnlockAll()
    {
        IsLockingActive = false;
        _unlocked.Clear();
    }

    // ── Save / restore ──

    public static IEnumerable<string> GetUnlockedNames() => _unlocked;

    public static void ApplySaveState(bool isLockingActive, IEnumerable<string> unlockedNames)
    {
        IsLockingActive = isLockingActive;
        _unlocked.Clear();
        if (unlockedNames != null)
            foreach (var n in unlockedNames)
                if (!string.IsNullOrEmpty(n)) _unlocked.Add(n);
    }
}
