using UnityEngine;

/// <summary>
/// A stable identity for an NPC, and the name that hangs off it.
///
/// This is the same trick <see cref="NPCMushroomPrice"/> uses for prices, pulled
/// out so names and prices can't drift apart: a streamed alien is keyed by its
/// SPAWN CELL (the key the spawner already uses to decide it exists at all), a
/// hand-placed scene NPC by its hierarchy name. Both survive a despawn at 300 m
/// and a restream later, plus save/load and scene reloads, with nothing stored.
/// </summary>
public static class AlienIdentity
{
    public static string Of(Component npc)
    {
        if (npc == null) return "";
        var spawned = npc.GetComponent<SpawnedAlienNPC>();
        if (spawned != null) return $"cell:{spawned.BodySlot}:{spawned.CellId}";
        return $"scene:{npc.gameObject.name}";
    }

    // FNV-1a + avalanche.
    public static uint Hash(string s)
    {
        uint h = 2166136261u;
        if (!string.IsNullOrEmpty(s))
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; }
        h ^= h >> 16; h *= 2246822507u;
        h ^= h >> 13; h *= 3266489909u;
        h ^= h >> 16;
        return h;
    }
}

/// <summary>
/// Names for the wandering aliens.
///
/// "Wandering Alien" was the worst possible label for a mushroom economy whose
/// whole learning loop is <i>remember which buyer pays well</i>. You can't build
/// a mental map of a route when every stop on it has the same name — the sell
/// panel's "you remember: last paid 87 a cap" line is meaningless attached to a
/// generic noun. A name makes the buyer a thing you can hold in your head, which
/// is the entire point of hiding their rate in the first place.
///
/// Derived from <see cref="AlienIdentity"/>, never stored: the alien 300 m
/// behind you is still called the same thing when you walk back.
///
/// Register matches the NPCs already in the game (Tev, Kolb) — short, hard
/// consonants, no gendered reading. Both of those are deliberately absent from
/// the list so a wanderer can never collide with a story character.
///
/// 64 names, so two aliens on screen at once rarely share one. If collisions do
/// show up in play, add more rows — the hash spreads over whatever length the
/// array happens to be, and no saved data depends on the order.
/// </summary>
public static class AlienNames
{
    static readonly string[] Names =
    {
        "Vorn",  "Skell", "Draa",  "Muun",  "Ryx",   "Talvo", "Grek",  "Nyra",
        "Osk",   "Pell",  "Quen",  "Rusk",  "Sev",   "Thal",  "Ulm",   "Vess",
        "Wex",   "Xanth", "Yol",   "Zarn",  "Brek",  "Cael",  "Dorn",  "Emmi",
        "Fask",  "Gorr",  "Hask",  "Ivo",   "Jool",  "Krev",  "Lome",  "Marn",
        "Nell",  "Ombo",  "Prax",  "Quill", "Rhen",  "Sunn",  "Torv",  "Unn",
        "Vada",  "Wold",  "Xero",  "Yenn",  "Zub",   "Alk",   "Bex",   "Corvo",
        "Dask",  "Eno",   "Frell", "Gyre",  "Holt",  "Isk",   "Jarn",  "Kesh",
        "Lurr",  "Mox",   "Noor",  "Orla",  "Pim",   "Ruv",   "Sable", "Tarn",
    };

    public static int Count => Names.Length;

    /// This alien's name. Stable for as long as the alien exists, and the same
    /// again after it streams out and back.
    public static string For(Component npc) => For(AlienIdentity.Of(npc));

    public static string For(string identity)
    {
        if (string.IsNullOrEmpty(identity)) return "Alien";
        // Salted so a buyer's NAME and their PRICE aren't drawn from the same
        // bits — otherwise every "Vorn" in the galaxy would pay identically and
        // the player would learn the name instead of the individual.
        uint h = AlienIdentity.Hash(identity + ":name");
        return Names[h % (uint)Names.Length];
    }
}
