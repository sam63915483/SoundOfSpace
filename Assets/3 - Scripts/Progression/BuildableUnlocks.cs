using System.Collections.Generic;
using System.Text;

/// <summary>
/// Which blueprints the build menu will let you place, as a function of the
/// COLONIZER track level. This is the live gate — BuildMenuUI asks this class
/// and nothing else.
///
/// (BuildMenuLock still exists and is still saved, but the UI does NOT consult
/// it: it's the retired forced-tutorial's gate, and NewGameReset leaves it in
/// "lock everything" state, so wiring it back in would black out the whole menu
/// on a new game. Left alone deliberately.)
///
/// Locked entries are NOT hidden — BuildMenuUI draws them dimmed with a padlock
/// and the level they need. Seeing what's coming is the point.
///
/// ── Editing the table ────────────────────────────────────────────────────
/// ByLevel[i] lists the blueprints that become available AT Colonizer level i,
/// so index 0 is what you start the game with. Names are matched against
/// BuildableEntry.displayName loosely (case-insensitive, whitespace collapsed)
/// because several scene-authored names carry stray spaces — "Stool ",
/// "Barrel 1 ", "Stairs Case  1". Write them naturally here.
///
/// Anything NOT in the table is always available. That's the safe default: the
/// build menu gains entries at RUNTIME (SaplingPlanter registers "Sapling",
/// DomeBuildRegistrar registers "Bubble Dome"), so an unlisted name must never
/// fall into "locked forever". Runtime-registered names CAN still be gated here
/// — matching is by displayName, so the registrar's name just has to agree.
/// "Bubble Dome" is gated at L6 that way; "Sapling" stays ungated on purpose,
/// since planting is the one thing the player must always be able to do.
///
/// Nothing here is saved. The unlock set is derived from the Colonizer score,
/// which PlayerProgress already saves — so a load restores unlocks for free.
///
/// Colonizer levels land at 1, 2, 5, 8, 13, 19, 28, 40, 56 and 80 placements
/// (PlayerProgress.BaseCurve × 0.8).
/// </summary>
public static class BuildableUnlocks
{
    /// Returned by RequiredLevel for entries the table doesn't gate.
    public const int AlwaysAvailable = -1;

    static readonly string[][] ByLevel =
    {
        // L0 — what you land with. Warmth and light, nothing structural.
        new[] { "Torch", "Bonfire" },

        // L1 (1 placed) — the first wall goes up the moment you place anything.
        new[] { "Wall 1", "Wall 3", "Wooden Floor 1" },

        // L2 (2) — enough to close a corner and step up into it, plus the first
        // step off foraging: the Grow Pot is the whole Industry path's entry fee.
        new[] { "Wall 2", "Ground Step", "Roof Tile 1", "Grow Pot" },

        // L3 (5) — a real roof over a real building.
        new[] { "Cabin", "Chimney", "Wooden Floor 2", "Roof Tile 2" },

        // L4 (8) — a second storey becomes possible.
        new[] { "Wall 4", "Wall 5", "Stairs Case 1", "Ladder", "Roof Top Cover 1" },

        // L5 (13) — furnishing tier opens.
        new[] { "Bench", "Chair", "Table 1", "Crate 1", "Barrel 1" },

        // L6 (19) — wall variety + the first shelf, and the Bubble Dome: the
        // Industry path's tier 2 and the thing that makes barren rock farmable.
        new[] { "Wall 6", "Wall 7", "Wall 8", "Roof Tile 3", "Checker Floor", "Book Shelf 1",
                "Bubble Dome" },

        // L7 (28)
        new[] { "Wall 9", "Wall 10", "Wall 11", "Stairs Case 2", "Roof Tile 4",
                "Crate 2", "Crate 3", "Table 2", "Chair 1" },

        // L8 (40) — the small dressing props.
        new[] { "Wall 12", "Wall 13", "Roof Top Cover 2", "Book Shelf 2",
                "Bucket", "Cup", "Plate 1", "Plate 2", "Stool" },

        // L9 (56)
        new[] { "Wall 14", "Wall 15", "Roof Tile 5", "Roof Top Cover 3",
                "Barrel 2", "Crate 4", "Chair 2", "Book Shelf 3" },

        // L10 (80) — the decorative flex tier. Nothing here is needed to build
        // a house; it's what you put on the walls once you have them.
        new[] { "Building Stand", "Wall Attachment", "Wall Attachment 2",
                "Wall Prop", "Wall Props 1", "Wall Props 2" },
    };

    // Normalised name → required level. Built once.
    static Dictionary<string, int> _required;

    static Dictionary<string, int> Required
    {
        get
        {
            if (_required != null) return _required;
            _required = new Dictionary<string, int>();
            for (int lv = 0; lv < ByLevel.Length; lv++)
            {
                var names = ByLevel[lv];
                if (names == null) continue;
                foreach (var n in names)
                {
                    string key = Normalize(n);
                    if (key.Length == 0) continue;
                    _required[key] = lv;   // last write wins if a name is listed twice
                }
            }
            return _required;
        }
    }

    /// Case-insensitive, whitespace-collapsed. "Stairs Case  1" and
    /// "stairs case 1" and "Stool " all land on the same key.
    static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        bool lastWasSpace = true;              // leading run is skipped
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) { sb.Append(' '); lastWasSpace = true; }
                continue;
            }
            sb.Append(char.ToUpperInvariant(c));
            lastWasSpace = false;
        }
        if (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;   // trailing run
        return sb.ToString();
    }

    /// Colonizer level the named blueprint needs, or AlwaysAvailable when the
    /// table doesn't gate it.
    public static int RequiredLevel(string displayName)
    {
        string key = Normalize(displayName);
        if (key.Length == 0) return AlwaysAvailable;
        return Required.TryGetValue(key, out int lv) ? lv : AlwaysAvailable;
    }

    /// The player's Colonizer level, 0 when progression hasn't spawned yet
    /// (e.g. a scene without PlayerProgress). Callers refresh on menu open, so
    /// a transient 0 here only ever means "locks look right a frame later".
    public static int ColonizerLevel
    {
        get
        {
            var p = PlayerProgress.Instance;
            return p != null ? p.LevelOf(ProgressTrack.Colonizer) : 0;
        }
    }

    public static bool IsUnlocked(string displayName)
        => IsUnlockedAt(displayName, ColonizerLevel);

    public static bool IsUnlockedAt(string displayName, int colonizerLevel)
    {
        int req = RequiredLevel(displayName);
        return req == AlwaysAvailable || colonizerLevel >= req;
    }

    /// Blueprints that become available exactly AT `level`. Empty for a level
    /// the table doesn't reach.
    public static string[] UnlockedAt(int level)
        => (level >= 0 && level < ByLevel.Length && ByLevel[level] != null)
           ? ByLevel[level]
           : System.Array.Empty<string>();

    /// Everything gained crossing fromLevel → toLevel (exclusive → inclusive).
    /// This is what the level-up ceremony lists.
    public static List<string> UnlockedBetween(int fromLevel, int toLevel)
    {
        var list = new List<string>();
        for (int lv = fromLevel + 1; lv <= toLevel; lv++)
            list.AddRange(UnlockedAt(lv));
        return list;
    }

    /// Lowest level above `colonizerLevel` that unlocks anything, or -1 when
    /// everything is already unlocked.
    public static int NextUnlockLevel(int colonizerLevel)
    {
        for (int lv = colonizerLevel + 1; lv < ByLevel.Length; lv++)
            if (UnlockedAt(lv).Length > 0) return lv;
        return -1;
    }

    /// Header carrot for the build menu — "NEXT: 4 BLUEPRINTS AT COLONIZER LV 3
    /// (3 MORE PLACED)". Returns "ALL BLUEPRINTS UNLOCKED" when maxed.
    public static string NextUnlockSummary()
    {
        int lv = ColonizerLevel;
        int next = NextUnlockLevel(lv);
        if (next < 0) return "ALL BLUEPRINTS UNLOCKED";

        int count = UnlockedAt(next).Length;
        string what = count == 1 ? "1 BLUEPRINT" : count + " BLUEPRINTS";

        var p = PlayerProgress.Instance;
        if (p == null) return $"NEXT: {what} AT COLONIZER LV {next}";

        // Only meaningful for the very next level — beyond that the remaining
        // count would need summing across curve steps and reads as noise.
        if (next == lv + 1)
        {
            int remaining = p.NextThresholdOf(ProgressTrack.Colonizer)
                          - System.Math.Max(0, p.ScoreOf(ProgressTrack.Colonizer));
            if (remaining > 0)
                return $"NEXT: {what} — {remaining} MORE PLACED";
        }
        return $"NEXT: {what} AT COLONIZER LV {next}";
    }
}
