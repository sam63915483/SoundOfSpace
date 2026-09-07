using TMPro;
using UnityEngine;

/// <summary>
/// The sign hanging on a vendor's stand. This is the whole knowledge half of the
/// planet economy: a player learns a trade route by READING boards and
/// remembering them, so the board shows words — "Local", "Imported", "Delicacy" —
/// never multipliers or dollar figures. Same rule as the tape word-ladder.
///
/// Phase 1 ships the board with placeholder copy so the stands can be placed.
/// Phase 4 fills <see cref="SetLines"/> from the planet's real buy list, and the
/// phone MARKETS page mirrors exactly what a board the player has stood in front
/// of said — never anything they have not seen.
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

    // Board palette — matches the sell panel so the words read as one system.
    static readonly Color32 C_Head = new Color32(60,  220, 190, 255);
    static readonly Color32 C_Body = new Color32(220, 230, 255, 255);

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
        rt.sizeDelta = new Vector2(2.4f, 1.8f);

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
    }

    /// <summary>Rebuild the board from the vendor's current buy list. Phase 1 shows
    /// the placeholder; Phase 4 calls <see cref="SetLines"/> instead.</summary>
    public void Refresh()
    {
        string planet = _site != null ? _site.BodyName : string.Empty;
        string head   = string.IsNullOrEmpty(planet) ? heading : $"{heading}\n<size=70%>{planet}</size>";
        SetText($"<color=#{ColorUtility.ToHtmlStringRGB(C_Head)}><b>{head}</b></color>\n\n{placeholderBody}");
    }

    /// <summary>Phase 4 entry point: the vendor hands over its buy list already
    /// grouped into words, and the board prints it.</summary>
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
