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
    [Tooltip("Cheapest an alien will pay per mushroom. LEGACY — only used by the old flat-price path; the live model is minMultiplier/maxMultiplier below.")]
    public int minPrice = 12;
    [Tooltip("Most an alien will pay per mushroom. LEGACY — see minMultiplier/maxMultiplier.")]
    public int maxPrice = 29;

    int _cached = -1;
    string _cachedFrom;

    public static NPCMushroomPrice GetOrAdd(MonoBehaviour npc)
    {
        if (npc == null) return null;
        var existing = npc.GetComponent<NPCMushroomPrice>();
        return existing != null ? existing : npc.gameObject.AddComponent<NPCMushroomPrice>();
    }

    /// Credits this alien pays per mushroom, ignoring species. LEGACY — kept so
    /// nothing that still calls it breaks. Real pricing is <see cref="PriceFor"/>.
    public int PricePerMushroom
    {
        get
        {
            string id = Identity;
            if (_cached > 0 && _cachedFrom == id) return _cached;
            _cachedFrom = id;
            int lo = Mathf.Min(minPrice, maxPrice);
            int hi = Mathf.Max(minPrice, maxPrice);
            uint h = Hash(id);
            _cached = lo + (int)(h % (uint)(hi - lo + 1));
            return _cached;
        }
    }

    // ── The live model (2026-08-06) ────────────────────────────────────────
    // Schedule 1's rule: price = the PRODUCT's market value × THIS BUYER's
    // multiplier. Flat credits made a Fly Agaric worth the same as a Buttoncap,
    // which threw away the whole rarity table. Both numbers below are derived
    // from the same stable identity hash the flat price used, so they survive
    // streaming, save/load and scene reloads with nothing persisted.
    //
    // NEITHER IS EVER SHOWN TO THE PLAYER. Sam's call: printing "pays 130% of
    // base · patience 34% over" hands over the two things the player is meant to
    // learn by dealing. The buyer's counter-offer is the only channel that leaks
    // their rate, and it costs a failed offer to get.

    [Tooltip("Stingiest buyer pays this fraction of a strain's market value.")]
    public float minMultiplier = 0.75f;
    [Tooltip("Most generous buyer pays this fraction of a strain's market value.")]
    public float maxMultiplier = 1.35f;
    [Tooltip("Tightest buyer walks this far above their own price (1.12 = 12% over).")]
    public float minPatience = 1.12f;
    [Tooltip("Most tolerant buyer walks this far above their own price.")]
    public float maxPatience = 1.40f;

    /// What fraction of market value this buyer pays. HIDDEN from the player.
    public float Multiplier => Mathf.Lerp(
        Mathf.Min(minMultiplier, maxMultiplier),
        Mathf.Max(minMultiplier, maxMultiplier),
        Unit(Hash(Identity + ":mult")));

    /// How far over their own price you can push before they walk. HIDDEN.
    public float Patience => Mathf.Lerp(
        Mathf.Min(minPatience, maxPatience),
        Mathf.Max(minPatience, maxPatience),
        Unit(Hash(Identity + ":patience")));

    // ── Taste ──────────────────────────────────────────────────────────────
    // A buyer who pays the same for a Buttoncap as for an Amanita makes the
    // rarity tiers a number and nothing else. Giving each buyer a tier they're
    // keen on and one they turn their nose up at means rarity decides WHO you
    // walk to, which is the decision the whole panel is built around.

    [Tooltip("What a buyer pays for the tier they're keen on.")]
    public float favouriteTasteBonus = 1.35f;
    [Tooltip("What a buyer pays for the tier they don't rate.")]
    public float dislikedTastePenalty = 0.72f;

    /// The tier this buyer pays a premium for. HIDDEN until the player sells
    /// them one and notices the price.
    public MushroomTier FavouriteTier => (MushroomTier)(AlienIdentity.Hash(Identity + ":taste") % 3u);

    /// The tier they don't rate. Always one of the other two.
    public MushroomTier DislikedTier
    {
        get
        {
            int fav = (int)FavouriteTier;
            int step = 1 + (int)(AlienIdentity.Hash(Identity + ":distaste") % 2u);
            return (MushroomTier)((fav + step) % 3);
        }
    }

    public float TasteFor(MushroomTier tier)
    {
        if (tier == FavouriteTier) return favouriteTasteBonus;
        if (tier == DislikedTier)  return dislikedTastePenalty;
        return 1f;
    }

    // ── Appetite ───────────────────────────────────────────────────────────

    [Tooltip("Fewest caps a buyer will take before they're full.")]
    public int minAppetite = 6;
    [Tooltip("Most caps a buyer will take before they're full.")]
    public int maxAppetite = 24;

    /// How many caps this buyer takes before they're full. Refills over real
    /// time — see MushroomDealState.AppetiteRefillSeconds.
    public int AppetiteMax
    {
        get
        {
            int lo = Mathf.Min(minAppetite, maxAppetite);
            int hi = Mathf.Max(minAppetite, maxAppetite);
            return lo + (int)(AlienIdentity.Hash(Identity + ":appetite") % (uint)(hi - lo + 1));
        }
    }

    /// What this buyer thinks ONE cap of this species is worth right now.
    /// Hidden — the player only ever sees it via a counter-offer or an accepted
    /// deal. Three things move it, and only the first is fixed:
    ///   • their multiplier   (who they are)
    ///   • their taste        (what you're selling)
    ///   • how full they are  (how much you've already dumped on them)
    /// Saturation reuses the appetite number rather than adding a second hidden
    /// quantity, so there's only one thing for the player to learn per buyer.
    public int PriceFor(string speciesKey)
    {
        float full = MushroomDealState.Fullness(Identity, AppetiteMax);
        float saturation = Mathf.Lerp(1f, 0.8f, full);
        float v = MushroomRegistry.BaseValue(speciesKey)
                  * Multiplier
                  * TasteFor(MushroomRegistry.Tier(speciesKey))
                  * saturation;
        return Mathf.Max(1, Mathf.RoundToInt(v));
    }

    static float Unit(uint h) => (h & 0xFFFFu) / 65535f;

    /// Something that identifies THIS alien and nothing else, and that comes back
    /// the same after a despawn/restream. Streamed aliens are keyed by their spawn
    /// cell (the same key the spawner uses to decide they exist at all); fixed
    /// scene NPCs by their hierarchy name, which never changes.
    /// Public because the deal state (cooldown + remembered counter) is keyed by it.
    /// Shared with AlienNames, so a buyer's name and their price can never
    /// disagree about who they are.
    public string Identity => AlienIdentity.Of(this);

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
