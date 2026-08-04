using UnityEngine;

/// <summary>
/// What ONE alien pays per mushroom. Schedule 1's rule, and Sam's: every NPC has
/// their own price, and it's the same price every time you come back to them —
/// so the player can learn a route and work out who's worth the walk.
///
/// That's the difference from <see cref="NPCSellDustOption"/>, which rerolls
/// price + an accept-chance gamble on every single conversation. Mushrooms have
/// no refusal roll at all: a buyer buys. The interesting decision is WHO you
/// walk to, not whether the dice land.
///
/// The price is DERIVED, never stored: a hash of the NPC's stable identity
/// (spawn cell for streamed aliens, scene name otherwise) mapped into
/// [minPrice, maxPrice]. That means it survives streaming, save/load and scene
/// reloads for free — a wandering alien despawned at 300 m and restreamed later
/// still quotes the same number, with nothing to persist.
///
/// Auto-attached on demand via GetOrAdd(npc), like NPCSellDustOption.
/// </summary>
public class NPCMushroomPrice : MonoBehaviour
{
    [Tooltip("Cheapest an alien will pay per mushroom. The band is centred on ~20 credits (Sam's target).")]
    public int minPrice = 12;
    [Tooltip("Most an alien will pay per mushroom.")]
    public int maxPrice = 29;

    int _cached = -1;
    string _cachedFrom;

    public static NPCMushroomPrice GetOrAdd(MonoBehaviour npc)
    {
        if (npc == null) return null;
        var existing = npc.GetComponent<NPCMushroomPrice>();
        return existing != null ? existing : npc.gameObject.AddComponent<NPCMushroomPrice>();
    }

    /// Credits this alien pays per mushroom. Stable for the life of the alien.
    public int PricePerMushroom
    {
        get
        {
            string id = StableIdentity();
            if (_cached > 0 && _cachedFrom == id) return _cached;
            _cachedFrom = id;
            int lo = Mathf.Min(minPrice, maxPrice);
            int hi = Mathf.Max(minPrice, maxPrice);
            uint h = Hash(id);
            _cached = lo + (int)(h % (uint)(hi - lo + 1));
            return _cached;
        }
    }

    /// Something that identifies THIS alien and nothing else, and that comes back
    /// the same after a despawn/restream. Streamed aliens are keyed by their spawn
    /// cell (the same key the spawner uses to decide they exist at all); fixed
    /// scene NPCs by their hierarchy name, which never changes.
    string StableIdentity()
    {
        var spawned = GetComponent<SpawnedAlienNPC>();
        if (spawned != null) return $"cell:{spawned.BodySlot}:{spawned.CellId}";
        return $"scene:{gameObject.name}";
    }

    // FNV-1a + avalanche.
    static uint Hash(string s)
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
