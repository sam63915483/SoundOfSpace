using UnityEngine;

/// <summary>
/// The pub bartender. Same empty as its AuthoredNPCSpawner (npcName
/// "Bartender" → StreamingAssets/Story/npc_bartender.json, edited in
/// Dialogue Studio). The graph does the talking and the money
/// (MoneyAtLeast / SpendMoney); this class only answers what C# knows:
///
///   Probes  : cupOnCounter   — a full beer is already standing on the bar
///             holdingCup     — the player owns a cup right now (any fill)
///             cupEmptyInHand — ...and it is empty
///   Actions : pourBeer       — a full cup appears on the counter
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
            case "cupOnCounter":   { var c = Counter; return c != null && c.HasFullCup; }
            case "holdingCup":     { var k = Cup();   return k != null && k.IsUnlocked; }
            case "cupEmptyInHand": { var k = Cup();   return k != null && k.IsUnlocked && k.IsEmpty; }
        }
        return false;
    }

    protected override bool GraphAction(string name)
    {
        switch (name)
        {
            case "pourBeer":
            {
                var c = Counter;
                if (c == null) { Debug.LogWarning("[BartenderTalk] no BarCounter in the scene to pour onto.", this); return true; }
                c.PourBeer();
                return true;
            }
        }
        return false;
    }
}
