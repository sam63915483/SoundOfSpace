using System;
using System.Collections.Generic;

/// <summary>
/// One character: the identity that travels BETWEEN worlds.
///
/// ── The split ────────────────────────────────────────────────────────────
/// A save (`saves/*.json`) owns a WORLD — planets, customers, buildings, story
/// progress. A character owns YOU. Terraria's model: you pick a character, then
/// pick a world, and the character is the same person in every one of them.
///
/// ── A character is a NAME and a SUIT COLOUR, and nothing else ────────────
/// Sam's call, 2026-08-18: everything else is a world save. Belongings,
/// equipment, vitals, levels and the orientation board all live in the world,
/// in a PlayerBlockSave keyed by the `id` below — so two people can share one
/// save file without trampling each other's pockets, and the same character
/// starting a new world starts it properly empty-handed. It is the cleaner
/// arrangement to develop against; a Terraria-style grind that travels between
/// worlds can be layered back on later if the finished game wants one.
///
///   • `id` is the stable key. Every per-character row in the world save
///     references it — the personal blocks, and Tev's fronting debt — never the
///     name, which is editable and duplicable.
///   • `schemaVersion` gates migration when fields are added or removed.
///   • Loading tolerates missing/unknown fields (JsonUtility skips what it does
///     not recognise, and CharacterStore.Normalise fills blanks).
///
/// JsonUtility rules apply — plain public fields only, no dictionaries, no
/// polymorphism, no properties.
/// </summary>
[Serializable]
public class CharacterProfile
{
    /// Stable GUID string. Never reused, never shown to the player.
    public string id;

    /// Display name. Trimmed, capped at MaxNameLength, never empty once saved.
    public string name;

    /// Index into SuitPalette.Swatches. The INDEX is what persists and what
    /// syncs over the network — never a raw colour, so the palette can be
    /// retuned without touching saved characters or breaking version parity
    /// between two players on slightly different builds.
    public int swatchIndex;

    /// Bumped when fields are added. Read by CharacterStore.Migrate.
    public int schemaVersion;

    /// ISO-8601 (round-trip "o" format). Display/sort only.
    public string createdAt;

    /// Current schema. Bump when you add a field, and add a matching step to
    /// CharacterStore.Migrate.
    ///
    /// v3 (2026-08-18) REMOVED orientationMask. It moved into the world save's
    /// per-character block along with everything else earned in a run — see the
    /// class summary. Old files still parse: JsonUtility ignores the field it no
    /// longer knows about.
    public const int CurrentSchemaVersion = 3;

    /// Hard cap on a character name. Chosen so the overhead nameplate stays
    /// readable at distance without scaling, and so it fits a FixedString32Bytes
    /// on the wire with room to spare (32 BYTES, and a name may be non-ASCII).
    public const int MaxNameLength = 16;

    /// Drops a trailing lone high surrogate.
    ///
    /// A char is 16 bits, but an emoji is a SURROGATE PAIR of two chars. Cutting
    /// a string to a char count can land between the halves and leave an
    /// orphaned high surrogate, which renders as a replacement box. Trimming one
    /// more char removes the whole character instead of half of it.
    public static string TrimDanglingSurrogate(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.IsHighSurrogate(s[s.Length - 1]) ? s.Substring(0, s.Length - 1) : s;
    }

    /// Trim + length-cap. Returns "" if the input was empty or all whitespace,
    /// which callers treat as invalid rather than saving.
    ///
    /// Mirrors NameStore.Sanitize deliberately — same contract, different cap.
    public static string Sanitize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var s = raw.Trim();
        if (s.Length > MaxNameLength) s = TrimDanglingSurrogate(s.Substring(0, MaxNameLength));
        return s;
    }

    public static CharacterProfile Create(string cleanName, int swatch)
    {
        return new CharacterProfile
        {
            id            = Guid.NewGuid().ToString("N"),
            name          = cleanName,
            swatchIndex   = SuitPalette.Clamp(swatch),
            schemaVersion = CurrentSchemaVersion,
            createdAt     = DateTime.UtcNow.ToString("o"),
        };
    }
}

/// <summary>
/// The on-disk file: every character plus which one was last used.
///
/// Lives at `Application.persistentDataPath/characters.json` — BESIDE `saves/`,
/// not inside it. Characters outlive any individual world, so deleting a save
/// must never take a character with it.
/// </summary>
[Serializable]
public class CharacterBook
{
    public List<CharacterProfile> characters = new List<CharacterProfile>();

    /// Id of the character to auto-select on boot. The whole point of the
    /// "remembered identity" flow: you pick once and never think about it again
    /// until you deliberately change it.
    public string lastSelectedId = "";
}
