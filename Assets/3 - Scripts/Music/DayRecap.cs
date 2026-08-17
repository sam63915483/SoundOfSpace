/// <summary>
/// The end-of-day wrap message (loop-feel pass B) — one phone text per
/// GalaxyTime day summarising what the day did: sales, money, rent state,
/// who warmed to you, who's getting hungry.
///
/// Informational ONLY. Nothing is deducted, nothing is gated — rent still
/// moves exclusively through Tev's payment screen. This is Schedule 1's
/// "one more day" ledger moment, delivered as a message from the shuttle AI.
///
/// Composed ONCE at the day tick and stored as text on the event: a wrap is
/// a frozen snapshot of that day, so unlike negotiation bubbles it must NOT
/// re-render against live state (yesterday's "owes $20" staying "$20" in the
/// thread history is correct even after the debt is paid).
///
/// PURE: no Unity types, testable in the headless suite.
/// </summary>
public static class DayRecap
{
    /// <param name="day">GalaxyTime day just finished.</param>
    /// <param name="tapesSold">tapes sold that day (walk-up + delivery).</param>
    /// <param name="earned">credits earned from tapes that day.</param>
    /// <param name="rentOwed">arrears in credits; 0 = paid up; negative =
    /// rent not set up yet (pre-haggle) — the line is omitted.</param>
    /// <param name="daysToLockout">whole days of non-payment left before the
    /// plugin lockout bites (only meaningful when rentOwed &gt; 0).</param>
    /// <param name="pluginsLocked">the lockout is already in force.</param>
    /// <param name="bondUps">display names whose bond rose today, comma-joined;
    /// "" = nobody.</param>
    /// <param name="hungry">display names of buyers craving more (loop-feel C);
    /// "" = nobody.</param>
    public static string Compose(int day, int tapesSold, int earned,
                                 int rentOwed, int daysToLockout, bool pluginsLocked,
                                 string bondUps, string hungry)
    {
        var sb = new System.Text.StringBuilder(160);
        sb.Append("DAY ").Append(day).Append(" WRAP");

        if (tapesSold > 0)
            sb.Append('\n').Append("sold ").Append(tapesSold)
              .Append(tapesSold == 1 ? " tape" : " tapes")
              .Append(" — earned $").Append(earned);
        else
            sb.Append('\n').Append("no tapes sold today");

        if (rentOwed == 0)
            sb.Append('\n').Append("rent: paid up");
        else if (rentOwed > 0 && pluginsLocked)
            sb.Append('\n').Append("rent: $").Append(rentOwed)
              .Append(" owed — Tev's plugin rack is CLOSED to you");
        else if (rentOwed > 0)
        {
            sb.Append('\n').Append("rent: owes $").Append(rentOwed);
            if (daysToLockout > 0)
                sb.Append(" — ").Append(daysToLockout)
                  .Append(daysToLockout == 1 ? " day" : " days")
                  .Append(" to plugin lockout");
        }
        // rentOwed < 0: no rent arrangement yet — say nothing about it.

        if (!string.IsNullOrEmpty(bondUps))
            sb.Append('\n').Append("warmer to you: ").Append(bondUps);

        if (!string.IsNullOrEmpty(hungry))
            sb.Append('\n').Append(hungry).Append(" — asking around for more");

        return sb.ToString();
    }
}
