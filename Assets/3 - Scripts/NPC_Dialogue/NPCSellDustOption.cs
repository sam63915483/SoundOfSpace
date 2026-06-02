using UnityEngine;

/// <summary>
/// Per-NPC component that holds the rolled accept-chance + price-per-dust +
/// preferred-max-quantity for a single conversation. RollFresh() is called at
/// conversation start by each NPC script; the rolled values persist until the
/// NPC's StopConversation flow runs (then the next encounter rolls again).
///
/// Auto-attached on demand via NPCSellDustOption.GetOrAdd(npc) so individual
/// NPC scripts don't need an Inspector field per type.
/// </summary>
public class NPCSellDustOption : MonoBehaviour
{
    [Tooltip("Min/max accept chance at or below the NPC's preferred quantity, rolled per conversation. Past the preferred qty this chance scales down by (preferredMaxQty / qty).")]
    [Range(0f, 1f)] public float minChance = 0.55f;
    [Range(0f, 1f)] public float maxChance = 0.85f;
    [Tooltip("Min/max credits per dust, rolled per conversation.")]
    public int minPricePerDust = 3;
    public int maxPricePerDust = 7;
    [Tooltip("Min/max preferred-MAX quantity, rolled per conversation. Selling at-or-below this quantity gets full base AcceptChance; selling more drops the effective chance linearly by (preferredMaxQty / qty). Make it modest (single digits to a few dozen) so the slider actually matters to the player.")]
    public int minPreferredMaxQty = 5;
    public int maxPreferredMaxQty = 30;

    public float AcceptChance     { get; private set; }
    public int   PricePerDust     { get; private set; }
    public int   PreferredMaxQty  { get; private set; }

    public static NPCSellDustOption GetOrAdd(MonoBehaviour npc)
    {
        if (npc == null) return null;
        var existing = npc.GetComponent<NPCSellDustOption>();
        return existing != null ? existing : npc.gameObject.AddComponent<NPCSellDustOption>();
    }

    public void RollFresh()
    {
        AcceptChance = Random.Range(minChance, maxChance);
        PricePerDust = Random.Range(minPricePerDust, maxPricePerDust + 1);
        PreferredMaxQty = Random.Range(Mathf.Max(1, minPreferredMaxQty), Mathf.Max(1, maxPreferredMaxQty) + 1);
    }

    /// <summary>
    /// Effective accept chance when offering <paramref name="qty"/> dust. At or
    /// below the NPC's PreferredMaxQty this returns AcceptChance unchanged; past
    /// it the chance scales down by (PreferredMaxQty / qty) — so 2× the preferred
    /// qty halves the chance, 10× knocks it to a tenth.
    /// </summary>
    public float EffectiveAcceptChance(int qty)
    {
        if (qty <= 0) return 0f;
        if (PreferredMaxQty <= 0) return AcceptChance;
        float scale = Mathf.Min(1f, (float)PreferredMaxQty / qty);
        return Mathf.Clamp01(AcceptChance * scale);
    }
}
