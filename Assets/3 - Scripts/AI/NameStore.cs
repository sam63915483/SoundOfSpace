// Player's chosen name, AI's chosen name, and the first-contact-complete flag.
//
// Mirrors the EarlyGameProgress pattern: static fields, no MonoBehaviour, no
// scene dependency. SaveCollector reads and writes these via NameStoreSave.
// The resolved accessors apply sensible defaults ("Player" / "Assistant") so
// downstream code never has to null-check.
public static class NameStore
{
    // Raw fields — empty string means "never set" (different from default).
    public static string PlayerName = "";
    public static string AIName     = "";

    // Has the AI's first-contact / naming UX completed for this save? If
    // false, AIChatScreen runs the scripted state machine on next open.
    public static bool FirstContactComplete = false;

    // ── Resolved accessors ──────────────────────────────────────────
    // Use these in code paths that need a non-empty string (display, token
    // resolver, system prompt). The fields above keep empty-string semantics
    // for save migration: an old save missing these fields loads as empty
    // strings → resolved as defaults → first-contact reruns to fix.

    /// The character system owns the player's name as of Aug 2026, so it is
    /// checked FIRST.
    ///
    /// Why it wins over the PlayerName field rather than being copied into it:
    ///   • A character is CROSS-SAVE; PlayerName is per-save. Loading an old
    ///     world would otherwise restore a stale name over the current one, and
    ///     getting that right would mean threading a new step into
    ///     SaveCollector's already fragile 17-step apply order.
    ///   • PlayerName is dead at runtime anyway. It was written by HAL's
    ///     first-contact naming exchange, which was retired when typed input was
    ///     removed (AIChatScreen.Init now force-completes first contact), so it
    ///     has resolved to the literal "Player" ever since.
    ///
    /// The field and its save round-trip are left intact so existing saves keep
    /// loading unchanged.
    public static string ResolvedPlayerName
    {
        get
        {
            string character = CharacterStore.ActiveName;
            if (!string.IsNullOrWhiteSpace(character)) return character;
            return string.IsNullOrWhiteSpace(PlayerName) ? "Player" : PlayerName;
        }
    }

    public static string ResolvedAIName
        => string.IsNullOrWhiteSpace(AIName) ? "Assistant" : AIName;

    // Hard cap on either name. Keeps the "{AI_NAME}: " prefix readable in
    // the chat UI and in HUD pop-ups. Applied at capture time, not display
    // time, so the cap survives save/load.
    public const int MaxNameLength = 24;

    /// Trim, validate, length-cap. Returns the cleaned value (which may be
    /// empty if the input was all whitespace).
    public static string Sanitize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var s = raw.Trim();
        if (s.Length > MaxNameLength) s = s.Substring(0, MaxNameLength);
        return s;
    }
}
