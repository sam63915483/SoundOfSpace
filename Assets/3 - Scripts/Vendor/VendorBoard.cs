using TMPro;
using UnityEngine;

/// <summary>
/// The sign hanging on a vendor's stand. This is the whole knowledge half of the
/// planet economy: a player learns a trade route by READING boards and
/// remembering them, so the board shows words — "Local", "Imported", "Delicacy" —
/// never multipliers or dollar figures. Same rule as the tape word-ladder.
///
/// A fish market's board lists that planet's real buy list from
/// <see cref="PlanetEconomy"/>; standing close enough to read it is what puts
/// the planet on the phone's MARKETS page (<see cref="MarketKnowledge"/>). A
/// goods vendor's board just names the shop. With no table on disk yet the
/// board shows placeholder copy, so stands can be placed before the economy
/// exists.
///
/// World-space <see cref="TextMeshPro"/> (not the UGUI flavour) so no canvas is
/// involved: ten of these across the system cost ten draw calls, not ten
/// canvases. It renders only while the player is close enough to read it.
/// </summary>
[DisallowMultipleComponent]
public class VendorBoard : MonoBehaviour
{
    [Tooltip("Metres at which the board switches its text renderer off. The stand " +
             "is visible far further than the words are readable.")]
    public float readableRange = 22f;

    [Tooltip("Heading above the list. The planet name is filled in automatically.")]
    public string heading = "FISH MARKET";

    [Tooltip("Board text while the economy tables are not built yet (Phase 1).")]
    [TextArea(2, 6)]
    public string placeholderBody = "Buying today:\n<color=#8CAFFF>Local catch</color>\n<color=#3CDCBE>Imported — pays well</color>\n<color=#FFD232>Delicacy — pays top</color>";

    TextMeshPro _tmp;
    VendorSite  _site;
    Transform   _player;
    string      _shown = null;
    float       _nextPlayerScan;
    float       _nextRefresh;

    // Board palette — matches the sell panel so the words read as one system.
    static readonly Color32 C_Head  = new Color32(60,  220, 190, 255);
    static readonly Color32 C_Body  = new Color32(220, 230, 255, 255);
    const string HexLocal = "#8CAFFF", HexImport = "#3CDCBE", HexDelic = "#FFD232";

    void Awake()
    {
        _site = GetComponentInParent<VendorSite>();

        _tmp = GetComponent<TextMeshPro>();
        if (_tmp == null) _tmp = gameObject.AddComponent<TextMeshPro>();
        _tmp.fontSize     = 1.4f;
        _tmp.color        = C_Body;
        _tmp.alignment    = TextAlignmentOptions.Top;
        _tmp.outlineWidth = 0.18f;
        _tmp.outlineColor = Color.black;
        _tmp.enableWordWrapping = true;

        var rt = _tmp.rectTransform;
        rt.sizeDelta = new Vector2(2.6f, 2.2f);

        Refresh();
    }

    void LateUpdate()
    {
        if (_tmp == null) return;

        // Cheap distance gate. The player is re-found on a throttle rather than
        // per frame (repo rule: never FindObjectOfType in Update).
        if (_player == null && Time.time >= _nextPlayerScan)
        {
            _nextPlayerScan = Time.time + 2f;
            var pc = FindObjectOfType<PlayerController>();
            if (pc != null) _player = pc.transform;
        }
        if (_player == null) { _tmp.enabled = false; return; }

        float sqr = (_player.position - transform.position).sqrMagnitude;
        bool near = sqr <= readableRange * readableRange;
        if (_tmp.enabled != near) _tmp.enabled = near;
        if (!near) return;

        // Reading the board is what teaches the route. Fish markets only — a
        // goods vendor's sign has nothing to remember.
        if (_site != null && _site.kind == VendorSite.VendorKind.FishMarket && _site.IsResolved)
            MarketKnowledge.NoteSeen(_site.BodyName);

        // The table can change under us (Sam editing the JSON in Play mode, or
        // the site resolving its planet a frame late), so re-derive on a slow
        // tick; SetText is change-gated so this costs nothing when unchanged.
        if (Time.time >= _nextRefresh) { _nextRefresh = Time.time + 1f; Refresh(); }
    }

    /// <summary>Rebuild the board from the vendor's buy list, or the placeholder
    /// when this planet has no table yet.</summary>
    public void Refresh()
    {
        string planet = _site != null ? _site.BodyName : string.Empty;
        string head   = string.IsNullOrEmpty(planet) ? heading : $"{heading}\n<size=70%>{planet}</size>";
        string headTag = $"<color=#{ColorUtility.ToHtmlStringRGB(C_Head)}><b>{head}</b></color>";

        bool isMarket = _site != null && _site.kind == VendorSite.VendorKind.FishMarket;
        var entry = isMarket ? PlanetEconomy.EntryFor(planet) : null;
        if (entry == null)
        {
            SetText($"{headTag}\n\n{placeholderBody}");
            return;
        }

        var sb = new System.Text.StringBuilder(256);
        sb.Append(headTag).Append("\n\n");
        Group(sb, HexLocal,  "Local",              entry.catchable);
        Group(sb, HexImport, "Imported — pays well", entry.imports);
        Group(sb, HexDelic,  "Delicacy — pays top",  entry.delicacies);
        SetText(sb.ToString());
    }

    static void Group(System.Text.StringBuilder sb, string hex, string title, System.Collections.Generic.List<string> ids)
    {
        if (ids == null || ids.Count == 0) return;
        sb.Append("<color=").Append(hex).Append("><b>").Append(title).Append("</b></color>\n<size=85%>");
        for (int i = 0; i < ids.Count; i++)
        {
            if (i > 0) sb.Append(" · ");
            sb.Append(PlanetEconomy.DisplayName(ids[i]));
        }
        sb.Append("</size>\n");
    }

    /// <summary>Kept for callers that hand the board pre-built copy.</summary>
    public void SetLines(string title, string body)
    {
        if (!string.IsNullOrEmpty(title)) heading = title;
        SetText($"<color=#{ColorUtility.ToHtmlStringRGB(C_Head)}><b>{heading}</b></color>\n\n{body}");
    }

    void SetText(string s)
    {
        if (_tmp == null || s == _shown) return;   // per-frame string churn guard
        _shown    = s;
        _tmp.text = s;
    }
}
