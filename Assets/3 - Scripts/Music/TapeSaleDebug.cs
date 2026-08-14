using System.Text;
using UnityEngine;

/// <summary>
/// Runs Tev's three demos against every alien ACTUALLY ALIVE IN THE WORLD and
/// dumps the numbers.
///
/// ── Why this exists ──────────────────────────────────────────────────────
/// The headless diagnostic (test/verify-diagnostic.py) says Tev's demos should
/// be refused roughly half the time, and Sam got nine sales out of nine, twice.
/// One of those two is wrong, and no amount of reading the code settles which:
/// the headless run invents alien ids, and the difference between an invented
/// id and a real one is exactly the sort of thing that hides a bug like this.
///
/// So this measures the REAL path with REAL spawned aliens — same identity
/// function, same taste model, same gate the sell flow uses. Sam's idea, and
/// the right one.
///
/// Press L in a build. Editor-and-build both, because playtesting happens in
/// builds. Delete once the discrepancy is understood.
/// </summary>
public static class TapeSaleDebug
{
    /// How many times to resolve the coin-flip band per alien per tape, so the
    /// reported acceptance is a rate rather than one roll.
    const int Trials = 200;

    public static void RunReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== TAPE SALE REPORT ===");

        var aliens = SpawnedAlienNPC.AllAliens;
        if (aliens == null || aliens.Count == 0)
        {
            sb.AppendLine("No aliens are currently spawned. Walk near some and try again.");
            Debug.Log(sb.ToString());
            return;
        }

        // The three demos, built exactly as TevDemoTapes builds them.
        var demos = TevDemoTapes.All;
        var tracks = new TraxTrack[demos.Length];
        var dials = new double[demos.Length][];
        for (int i = 0; i < demos.Length; i++)
        {
            tracks[i] = TevDemoTapes.TrackFor(demos[i]);
            dials[i] = TapeSellFlow.DialsOf(tracks[i]);
        }

        sb.AppendLine("Aliens alive: " + aliens.Count);
        sb.AppendLine();

        // IDENTITY FIRST. If these collide, every alien is the same alien and
        // nothing downstream means anything.
        var seen = new System.Collections.Generic.HashSet<string>();
        int duplicates = 0;
        for (int i = 0; i < aliens.Count; i++)
        {
            string id = AlienIdentity.Of(aliens[i]);
            if (!seen.Add(id)) duplicates++;
        }
        sb.AppendLine("DISTINCT IDENTITIES: " + seen.Count + " of " + aliens.Count +
                      (duplicates > 0 ? "   *** " + duplicates + " COLLISIONS ***" : "   (all unique)"));
        sb.AppendLine();

        int totalOffers = 0, totalAccepted = 0;

        for (int a = 0; a < aliens.Count && a < 12; a++)
        {
            var npc = aliens[a];
            if (npc == null) continue;
            string id = AlienIdentity.Of(npc);

            sb.AppendLine(AlienNames.For(id) + "   [" + id + "]");
            sb.AppendLine("   likes " + AlienTaste.FavouriteGenre(id) +
                          "   falloff " + AlienTaste.Falloff(id).ToString("0.00") +
                          "   pay " + AlienTaste.PayFactor(id).ToString("0.00") +
                          "   patience " + AlienTaste.Patience(id).ToString("0.00"));

            double[] ear = AlienTaste.TastePoint(id);
            sb.Append("   ear [");
            for (int i = 0; i < ear.Length; i++) sb.Append(ear[i].ToString("0.0") + " ");
            sb.AppendLine("]");

            for (int d = 0; d < demos.Length; d++)
            {
                double sat = AlienTaste.Satisfaction(id, dials[d]);
                AlienTaste.Verdict verdict = AlienTaste.Gate(sat);
                double dist = AlienTaste.Distance(dials[d], ear);

                int accepted = 0;
                for (int t = 0; t < Trials; t++)
                {
                    double s2;
                    if (TapeOffer.Listen(id, dials[d], Random.value < 0.5f, out s2)
                        == TapeOffer.Reaction.Liked) accepted++;
                }
                totalOffers += Trials;
                totalAccepted += accepted;

                int value = TapeOffer.Value(id, tracks[d].ActiveCount(), 1, sat, false);

                sb.AppendLine("      " + demos[d].name.PadRight(22) +
                              " dist " + dist.ToString("00.0") +
                              "  sat " + sat.ToString("000.0") +
                              "  " + verdict.ToString().PadRight(9) +
                              "  buys " + (100 * accepted / Trials).ToString("000") + "%" +
                              "  pays $" + value);
            }
            sb.AppendLine();
        }

        sb.AppendLine("OVERALL: " + (totalOffers == 0 ? 0 : 100 * totalAccepted / totalOffers) +
                      "% of offers accepted across " + totalOffers + " simulated offers.");
        sb.AppendLine("(The headless diagnostic predicts roughly 35-45% for these three demos.");
        sb.AppendLine(" A number far above that means the world disagrees with the model.)");

        Debug.Log(sb.ToString());
    }
}
