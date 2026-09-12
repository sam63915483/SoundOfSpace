using UnityEngine;

/// <summary>
/// Shlawg, the pub bartender. Same empty as its AuthoredNPCSpawner (npcName
/// "Shlawg" → StreamingAssets/Story/npc_shlawg.json, edited in Dialogue
/// Studio). The graph does the talking and the money (MoneyAtLeast /
/// SpendMoney); this class only answers what C# knows:
///
///   Probes  : counterFull    — no free beer slot left on the bar
///             roomFor2..5    — at least N free slots (the "buy N" buttons)
///             cupOnCounter   — any full beer standing on the bar
///             holdingCup     — the player carries a beer or an empty
///             cupEmptyInHand — the cup in hand is an empty
///   Actions : pourBeer, pourBeer2..pourBeer5 — that many beers onto the bar
///
/// Add a probe/action = one case here + a vocab.json entry (with sim ops) so
/// the browser player can pretend it. See CLAUDE.md "NPC dialogue = data".
/// </summary>
public class BartenderTalk : AuthoredNPCTalk
{
    [Tooltip("The BarCounter this bartender pours onto. Empty = the nearest BarCounter to the NPC.")]
    public BarCounter counter;

    BarCounter Counter
    {
        get
        {
            if (counter != null) return counter;
            Vector3 from = Spawner != null && Spawner.Body != null ? Spawner.Body.transform.position : transform.position;
            BarCounter best = null; float bestD = float.MaxValue;
            for (int i = 0; i < BarCounter.All.Count; i++)
            {
                var c = BarCounter.All[i];
                if (c == null) continue;
                float d = (c.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = c; }
            }
            counter = best;
            return best;
        }
    }

    static BeerCupController Cup() => Object.FindObjectOfType<BeerCupController>();

    protected override bool GraphProbe(string name)
    {
        switch (name)
        {
            case "counterFull":    { var c = Counter; return c != null && c.IsFull; }
            case "cupOnCounter":   { var c = Counter; return c != null && c.HasFullCup; }
            case "holdingCup":     { var k = Cup();   return k != null && (k.HasBeers || k.HasEmpties); }
            case "cupEmptyInHand": { var k = Cup();   return k != null && k.HoldingEmpty; }
        }
        if (name.StartsWith("roomFor") && int.TryParse(name.Substring(7), out int n))
        {
            var c = Counter;
            return c != null && c.FreeSlots >= n;
        }
        return false;
    }

    protected override bool GraphAction(string name)
    {
        if (name.StartsWith("pourBeer"))
        {
            int n = 1;
            if (name.Length > 8 && !int.TryParse(name.Substring(8), out n)) return false;
            var c = Counter;
            if (c == null) { Debug.LogWarning("[BartenderTalk] no BarCounter in the scene to pour onto.", this); return true; }
            int poured = c.PourBeer(n);
            if (poured < n) Debug.LogWarning($"[BartenderTalk] asked for {n} beers, only {poured} fit on the bar.", this);
            return true;
        }
        return false;
    }
}
