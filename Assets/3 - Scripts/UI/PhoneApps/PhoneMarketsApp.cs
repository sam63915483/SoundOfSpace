using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The phone's MARKETS page: one row per planet with a fish market, showing
/// exactly what its board said — and "??" for any board this player has not
/// stood in front of. (docs/Handoff_PlanetEconomy_Fuel_Fishing_v2.md §7.3.)
///
/// The game remembers what you have SEEN; it never tells you what you have not.
/// That is the whole design: the trade routes are something the player learns
/// by flying out and reading signs, and this page is their notebook. Each
/// species shows its bucket word and the current price per pound — same as the
/// boards (Sam's call: prices visible before you sell).
///
/// Built on <see cref="PhoneAppBase"/> like the Fishingdex: rows on the left,
/// the chosen planet's board on the right.
/// </summary>
public class PhoneMarketsApp : PhoneAppBase
{
    protected override string Title => "MARKETS";

    TMP_Text _planetName, _body, _hint;
    readonly List<(string body, Button row, TMP_Text right)> _rows = new List<(string, Button, TMP_Text)>();
    string _selected;

    const string HexLocal = "#8CAFFF", HexImport = "#3CDCBE", HexDelic = "#FFD232";

    protected override void BuildBody()
    {
        _planetName = MakeText(DetailPane, "—", 12f, LabelWhite, TextAlignmentOptions.Center);
        _planetName.fontStyle = FontStyles.Bold;
        var nRT = _planetName.rectTransform;
        nRT.anchorMin = new Vector2(0f, 1f); nRT.anchorMax = new Vector2(1f, 1f);
        nRT.pivot = new Vector2(0.5f, 1f);
        nRT.sizeDelta = new Vector2(0f, 18f);
        nRT.anchoredPosition = new Vector2(0f, -4f);

        _body = MakeText(DetailPane, "", 9f, LabelWhite, TextAlignmentOptions.TopLeft);
        var bRT = _body.rectTransform;
        bRT.anchorMin = new Vector2(0f, 0f); bRT.anchorMax = new Vector2(1f, 1f);
        bRT.offsetMin = new Vector2(4f, 4f);
        bRT.offsetMax = new Vector2(-4f, -26f);
        _body.enableWordWrapping = true;
        _body.richText = true;

        _hint = MakeText(DetailPane, "", 8f, LabelDim, TextAlignmentOptions.BottomLeft);
        var hRT = _hint.rectTransform;
        hRT.anchorMin = new Vector2(0f, 0f); hRT.anchorMax = new Vector2(1f, 0f);
        hRT.pivot = new Vector2(0.5f, 0f);
        hRT.sizeDelta = new Vector2(0f, 14f);
        hRT.anchoredPosition = new Vector2(0f, 2f);
    }

    protected override void OnOpened()
    {
        MarketKnowledge.Changed += Rebuild;
        Rebuild();
    }

    protected override void OnClosed()
    {
        MarketKnowledge.Changed -= Rebuild;
    }

    void Rebuild()
    {
        ClearRows();
        _rows.Clear();

        // Every planet that has a fish market, in a stable order. The stands
        // themselves are the source of truth for "where is there a market" —
        // it is whatever Sam placed.
        var bodies = new List<string>();
        foreach (var site in VendorSite.AllInstances)
        {
            if (site == null || site.kind != VendorSite.VendorKind.FishMarket) continue;
            string b = site.BodyName;
            if (!string.IsNullOrEmpty(b) && !bodies.Contains(b)) bodies.Add(b);
        }
        bodies.Sort(System.StringComparer.Ordinal);

        if (bodies.Count == 0)
        {
            _planetName.text = "—";
            _body.text = "No markets known.";
            _hint.text = "";
            return;
        }

        if (string.IsNullOrEmpty(_selected) || !bodies.Contains(_selected)) _selected = bodies[0];

        foreach (var b in bodies)
        {
            bool seen = MarketKnowledge.HasSeen(b);
            string captured = b;
            Button row = null;
            row = AddRow(b.ToUpperInvariant(), seen ? "" : "??", LabelDim, () => Select(captured, row),
                         out _, out TMP_Text right);
            _rows.Add((b, row, right));
        }

        int seenCount = 0;
        foreach (var b in bodies) if (MarketKnowledge.HasSeen(b)) seenCount++;
        if (TopRightText != null) TopRightText.text = $"{seenCount}/{bodies.Count} VISITED";

        Select(_selected, null);
    }

    void Select(string body, Button row)
    {
        _selected = body;
        foreach (var r in _rows) SetRowSelected(r.row, r.body == body);

        _planetName.text = body.ToUpperInvariant();

        if (!MarketKnowledge.HasSeen(body))
        {
            _body.text = "<color=" + HexImport + ">??</color>\n\nYou haven't read this market's board yet.";
            _hint.text = "Stand at the stand to read its sign.";
            return;
        }

        var e = PlanetEconomy.EntryFor(body);
        if (e == null)
        {
            _body.text = "Buys everything at the normal price.";
            _hint.text = "";
            return;
        }

        var sb = new System.Text.StringBuilder(256);
        Group(sb, body, HexLocal,  "LOCAL",    e.catchable);
        Group(sb, body, HexImport, "IMPORTED", e.imports);
        Group(sb, body, HexDelic,  "DELICACY", e.delicacies);

        bool crystals = VendorSite.Find(body, VendorSite.VendorKind.GoodsVendor) != null;
        sb.Append("\n<color=").Append(LabelDimHex).Append(">Sells crystals: ")
          .Append(crystals ? "yes" : "no").Append("</color>");

        _body.text = sb.ToString();
        _hint.text = "Anything not listed sells at half its base price.";
    }

    static string LabelDimHex => "#A8D2EB";

    // Prices are CURRENT, not a snapshot from the visit: the page is a notebook
    // of which planets you have read, and the live number is more useful than a
    // stale one once you know a market exists.
    static void Group(System.Text.StringBuilder sb, string body, string hex, string title, List<string> ids)
    {
        if (ids == null || ids.Count == 0) return;
        sb.Append("<color=").Append(hex).Append("><b>").Append(title).Append("</b></color>\n");
        for (int i = 0; i < ids.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(PlanetEconomy.PriceLine(body, ids[i]));
        }
        sb.Append("\n\n");
    }
}
