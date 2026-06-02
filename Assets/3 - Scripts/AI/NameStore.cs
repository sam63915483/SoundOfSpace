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

    public static string ResolvedPlayerName
        => string.IsNullOrWhiteSpace(PlayerName) ? "Player" : PlayerName;

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
