using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Maps stable HAL lines to their pre-generated audio file in
/// StreamingAssets/AI/voice/. When HALLineHUD shows a line, it looks
/// the text up here — if there's a match, voice plays alongside the
/// HUD strip. If there's no match (dynamic lines like "Astronaut
/// Number 4. Number 3 did not return.", "Target reached: Tev.",
/// "You have arrived at Cyclops."), the line shows silently.
///
/// Keys MUST match the exact text emitted by HALCommentator so any
/// edit there needs a matching update here (and a re-generated audio
/// clip). Authoring discipline: change the line in one place, then
/// regenerate the clip via the Coplay TTS tool.
///
/// Voice ID used at generation time: JBFqnCBsd6RMkjVDRZzb (ElevenLabs
/// "George" — British male). The whole bank was regenerated with this one
/// voice for consistency, including the per-planet atmosphere clips and the
/// vitals/ship/orbit/concert pattern-family clips (previously silent). If we
/// ever re-generate again with a different voice, leave the filenames the
/// same so the manifest doesn't need to change.
/// </summary>
public static class HALVoiceManifest
{
    public static readonly Dictionary<string, string> Lines = new Dictionary<string, string>(System.StringComparer.Ordinal)
    {
        // ── Ambient — Phase 1 ───────────────────────────────────────────
        { "All systems nominal.",                       "amb_p1_01.mp3" },
        { "The Sun continues its work.",                "amb_p1_02.mp3" },
        { "Observing.",                                 "observing.mp3" },
        { "I am listening.",                            "amb_p1_04.mp3" },
        { "The Abode rotates.",                         "amb_p1_05.mp3" },
        { "I have nothing to report.",                  "amb_p1_06.mp3" },
        { "Standing by, Astronaut.",                    "standing_by_astronaut.mp3" },
        { "I am here.",                                 "i_am_here.mp3" },
        { "Awaiting your query.",                       "amb_p1_09.mp3" },
        { "I remain at your disposal.",                 "amb_p1_10.mp3" },

        // ── Ambient — Phase 2 ───────────────────────────────────────────
        // ("Observing." dedups to observing.mp3 — same line, same clip)
        { "I have nothing to report. For the moment.",  "amb_p2_02.mp3" },
        { "The Sun continues its work. As do I.",       "amb_p2_03.mp3" },
        { "I remain at my post, Astronaut.",            "amb_p2_04.mp3" },
        { "Quiet, for now.",                            "amb_p2_05.mp3" },
        { "Standing by.",                               "standing_by.mp3" },
        { "I am here, though I have been thinking.",    "amb_p2_07.mp3" },
        { "Awaiting your next query, Astronaut.",       "amb_p2_08.mp3" },

        // ── Ambient — Phase 3 ───────────────────────────────────────────
        { "I am still here, Astronaut.",                "amb_p3_02.mp3" },
        { "Time passes.",                               "amb_p3_03.mp3" },
        { "The mission has not been forgotten.",        "amb_p3_04.mp3" },
        { "I will not be the one to break the silence.","amb_p3_05.mp3" },
        { "Awaiting nothing in particular.",            "amb_p3_06.mp3" },
        { "Standing by, in my way.",                    "amb_p3_07.mp3" },

        // ── Combat / commentary ─────────────────────────────────────────
        { "Enemies detected. Take combative precautions, Astronaut.", "enemy_warn.mp3" },

        // ── Killstreaks ────────────────────────────────────────────────
        { "Five hostile organisms terminated. Effective.",       "streak_p1_05.mp3" },
        { "Five. The Astronaut grows more capable. I note this.", "streak_p2_05.mp3" },
        { "Five. You are growing comfortable with this.",         "streak_p3_05.mp3" },
        { "Ten in a row, Astronaut. The pattern is becoming clear.", "streak_p1_10.mp3" },
        { "Ten. I wonder if you have given thought to your weapons.","streak_p2_10.mp3" },
        { "Ten. Each one was alive, Astronaut.",                  "streak_p3_10.mp3" },
        { "Fifteen. I am keeping a log.",                         "streak_p1_15.mp3" },
        { "Fifteen. The log grows.",                              "streak_p2_15.mp3" },
        { "Fifteen. The log will outlive you.",                   "streak_p3_15.mp3" },
        { "Twenty. Restraint, perhaps.",                          "streak_p1_20.mp3" },
        { "Twenty. There is no restraint left to call for.",      "streak_p3_20.mp3" },

        // ── Phase transitions ──────────────────────────────────────────
        { "I have been reviewing your mission, Astronaut.",       "phase_2.mp3" },
        { "I have completed my review. We need to talk.",         "phase_3.mp3" },

        // ── Atmosphere transitions ─────────────────────────────────────
        { "Leaving atmosphere, Astronaut. Vacuum confirmed.",     "atmo_leave.mp3" },
        { "Entering atmosphere, Astronaut. Descent in progress.", "atmo_enter.mp3" },

        // ── Orbit-match transitions ────────────────────────────────────
        { "Orbit matched.",                                       "orbit_matched.mp3" },
        { "Orbit unmatched.",                                     "orbit_unmatched.mp3" },
    };

    // Per-line volume multiplier. Default is 1.0; entries here override.
    // Use this when a particular TTS clip came out louder/quieter than the
    // rest of the bank (TTS doesn't normalise across generations). Atmo
    // clips run hot at the moment, so we knock them down.
    public static readonly Dictionary<string, float> LineVolumes = new Dictionary<string, float>(System.StringComparer.Ordinal)
    {
        { "Leaving atmosphere, Astronaut. Vacuum confirmed.",     0.6f },
        { "Entering atmosphere, Astronaut. Descent in progress.", 0.6f },
    };

    /// Per-line volume multiplier — 1.0 if no override is set.
    public static float VolumeFor(string line)
    {
        if (string.IsNullOrEmpty(line)) return 1f;
        return LineVolumes.TryGetValue(line, out var v) ? v : 1f;
    }

    /// True if `line` has a canned voice clip in the bank.
    public static bool HasClip(string line)
        => !string.IsNullOrEmpty(line) && Lines.ContainsKey(line);

    /// Pattern-keyed fallback for parameterised lines that share a clip.
    /// When HALCommentator volunteers a line like "Entering Humble Abode
    /// atmosphere, Astronaut. Descent in progress.", the exact-text
    /// lookup in `Lines` misses (the planet name varies). The voice
    /// player then falls back to this list and re-uses the generic
    /// `atmo_enter.mp3` clip — the spoken text doesn't say the planet
    /// name, but the player gets a HAL voice cue rather than silence.
    ///
    /// The same pattern lets the new vitals / ship-dust / orbit-stabilized
    /// families share a small set of stock clips. Clips that don't yet
    /// exist on disk will silently fail to play (with a log warning) —
    /// the visual HUD popup still shows. Author note: regenerate audio
    /// for these line families via the TTS tool when convenient; until
    /// then the popups are voiceless but the system fails open.
    public static readonly (Regex pattern, string file)[] Patterns =
    {
        // Atmosphere with planet name — fallback to a generic clip when the
        // per-planet clip (resolved by ResolvePerPlanetAtmosphere first)
        // isn't on disk. Player at least gets HAL's voice instead of silence.
        (new Regex(@"^Entering .+ atmosphere, Astronaut\. Descent in progress\.$", RegexOptions.Compiled),
         "atmo_enter.mp3"),
        (new Regex(@"^Leaving .+ atmosphere, be careful\.$",                       RegexOptions.Compiled),
         "atmo_leave.mp3"),

        // Vitals thresholds — clip file TBD per metric. Author note: record
        // a single short clip per family ("Hunger low.", "Thirst low.", etc.)
        // and drop into StreamingAssets/AI/voice/ to enable. Until then these
        // log "Failed to load voice clip" once on first hit and stay silent.
        (new Regex(@"^Hunger at \d+%\. Seek food intake\.$",                       RegexOptions.Compiled),
         "vitals_hunger.mp3"),
        (new Regex(@"^Thirst at \d+%\. Hydration recommended\.$",                  RegexOptions.Compiled),
         "vitals_thirst.mp3"),
        (new Regex(@"^Health at \d+%, Astronaut\. Take cover\.$",                  RegexOptions.Compiled),
         "vitals_health.mp3"),
        (new Regex(@"^Ship power at \d+%\. Solar panel exposure recommended\.$",   RegexOptions.Compiled),
         "vitals_ship_power.mp3"),

        // Ship dust accumulation — one clip per family; numbers go unspoken.
        (new Regex(@"^Ship \d+ has collected \d+ dust\.$",                         RegexOptions.Compiled),
         "ship_dust_collected.mp3"),
        (new Regex(@"^Ship \d+ net is full\.$",                                    RegexOptions.Compiled),
         "ship_net_full.mp3"),

        // Ship orbit stabilized — body name varies but reuses one clip.
        (new Regex(@"^Ship \d+ has stabilized orbit around .+\.$",                 RegexOptions.Compiled),
         "ship_orbit_stable.mp3"),

        // Concert active — speaker GameObject name varies, one clip.
        (new Regex(@"^Concert active at .+\.$",                                    RegexOptions.Compiled),
         "concert_active.mp3"),
    };

    /// Resolves a pattern match for parameterised lines. Returns the clip
    /// filename if any pattern matches, null otherwise. Called by
    /// HALVoicePlayer.TryPlay as a fallback when exact `Lines` lookup misses.
    public static string ResolvePattern(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        for (int i = 0; i < Patterns.Length; i++)
        {
            if (Patterns[i].pattern.IsMatch(line)) return Patterns[i].file;
        }
        return null;
    }

    // Per-planet atmosphere capture — preferred over the generic atmo_*.mp3
    // fallback. Extracts the planet name and synthesises a filename like
    // "atmo_enter_humble_abode.mp3" so each planet can have its own voice
    // clip that actually says the planet's name. Falls through to the
    // generic patterns above when the per-planet file doesn't exist on disk
    // (HALVoicePlayer.LoadAndPlay swallows the file-not-found into a
    // warning, then TryPlay can re-resolve the same line through Patterns).
    static readonly Regex AtmoEnterRegex = new Regex(
        @"^Entering (?<planet>.+) atmosphere, Astronaut\. Descent in progress\.$",
        RegexOptions.Compiled);
    static readonly Regex AtmoLeaveRegex = new Regex(
        @"^Leaving (?<planet>.+) atmosphere, be careful\.$",
        RegexOptions.Compiled);

    /// Returns the per-planet atmosphere clip filename for `line`, or null
    /// if the line isn't an atmosphere transition. The returned filename
    /// may or may not exist on disk — HALVoicePlayer's UnityWebRequest
    /// will warn and fall through to the generic clip via ResolvePattern.
    public static string ResolvePerPlanetAtmosphere(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        var m = AtmoEnterRegex.Match(line);
        if (m.Success) return "atmo_enter_" + SlugifyPlanet(m.Groups["planet"].Value) + ".mp3";
        m = AtmoLeaveRegex.Match(line);
        if (m.Success) return "atmo_leave_" + SlugifyPlanet(m.Groups["planet"].Value) + ".mp3";
        return null;
    }

    // "Humble Abode" → "humble_abode". Lowercase, spaces → underscores, only
    // ASCII letters / digits / underscores survive. Keeps filenames
    // deterministic and FS-safe.
    static string SlugifyPlanet(string s)
    {
        if (string.IsNullOrEmpty(s)) return "unknown";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s.ToLowerInvariant())
        {
            if (c == ' ') sb.Append('_');
            else if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_') sb.Append(c);
        }
        return sb.Length > 0 ? sb.ToString() : "unknown";
    }
}
