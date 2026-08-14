using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The people who buy your music: who they are, what they like, and what they
/// are asking for right now.
///
/// ── This is the screen the whole taste system was waiting for ────────────
/// Until it existed, the only way to learn an alien's taste was to be refused
/// by them — knowledge arriving attached to a failure, and unrecorded, so it
/// lived in the player's head or nowhere. With it, a sale turns a stranger into
/// a line you can read: "Vess — GLORP — bond 24 — wants something GLORPY".
/// That is what makes carrying the right tape to the right person possible, and
/// targeting is where the money is (a well-matched buyer pays about 2.5x a
/// poorly-matched one).
///
/// Built on PhoneAppBase like the build menu and fishingdex, so it inherits the
/// top bar, the scroll list and the detail pane rather than inventing a screen.
/// </summary>
public class PhoneContactsApp : PhoneAppBase
{
    protected override string Title { get { return "CONTACTS"; } }

    readonly List<string> _ids = new List<string>();
    string _selected;
    int _versionShown = -1;

    TMPro.TMP_Text _detailName, _detailGenre, _detailBond, _detailRequest, _detailHistory, _detailHint;

    protected override void BuildBody()
    {
        _detailName    = MakeText(DetailPane, "", 15, LabelWhite, TMPro.TextAlignmentOptions.TopLeft);
        Place(_detailName, -6, 22);

        _detailGenre   = AddSpecLine(DetailPane, "LIKES", -34f);
        _detailBond    = AddSpecLine(DetailPane, "BOND",  -56f);
        _detailHistory = AddSpecLine(DetailPane, "HEARD", -78f);

        _detailRequest = MakeText(DetailPane, "", 12, AccentCyan, TMPro.TextAlignmentOptions.TopLeft);
        Place(_detailRequest, -104, 46);

        _detailHint = MakeText(DetailPane, "", 11, LabelDim, TMPro.TextAlignmentOptions.TopLeft);
        Place(_detailHint, -152, 60);
    }

    static void Place(TMPro.TMP_Text t, float y, float h)
    {
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.offsetMin = new Vector2(6, 0);
        rt.offsetMax = new Vector2(-6, 0);
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
        rt.anchoredPosition = new Vector2(0, y);
        t.enableWordWrapping = true;
    }

    protected override void OnOpened()
    {
        // Opening the app is what "reads" the messages — the unseen count on
        // the home tile clears here rather than on a timer.
        TapeRequests.MarkAllSeen();
        Rebuild();
    }

    void Update()
    {
        if (Root == null || !Root.gameObject.activeInHierarchy) return;
        int v = TapeMemory.Version + TapeRequests.Version;
        if (v != _versionShown) Rebuild();
    }

    void Rebuild()
    {
        _versionShown = TapeMemory.Version + TapeRequests.Version;
        ClearRows();
        _ids.Clear();

        foreach (string id in TapeMemory.Contacts) _ids.Add(id);

        // Anyone waiting on an order first, then by bond — the list is a work
        // queue, and the person who asked for something outranks the person you
        // merely know.
        _ids.Sort(delegate (string a, string b)
        {
            bool ra = TapeRequests.For(a) != null, rb = TapeRequests.For(b) != null;
            if (ra != rb) return ra ? -1 : 1;
            int bond = TapeMemory.Bond(b).CompareTo(TapeMemory.Bond(a));
            if (bond != 0) return bond;
            return string.Compare(AlienNames.For(a), AlienNames.For(b), System.StringComparison.OrdinalIgnoreCase);
        });

        if (_ids.Count == 0)
        {
            TMPro.TMP_Text el, er;
            AddRow("No contacts yet", "", LabelDim, delegate { }, out el, out er);
            ShowDetail(null);
            TopRightText.text = "";
            return;
        }

        for (int i = 0; i < _ids.Count; i++)
        {
            string id = _ids[i];
            var req = TapeRequests.For(id);
            string right = req != null ? "WANTS " + req.genre : AlienTaste.FavouriteGenre(id);
            string captured = id;
            TMPro.TMP_Text rl, rr;
            AddRow(AlienNames.For(id), right, req != null ? AccentCyan : LabelDim,
                   delegate { ShowDetail(captured); }, out rl, out rr);
        }

        int waiting = TapeRequests.OpenCount;
        TopRightText.text = _ids.Count + (waiting > 0 ? "  ·  " + waiting + " WAITING" : "");

        ShowDetail(_selected != null && _ids.Contains(_selected) ? _selected : _ids[0]);
    }

    void ShowDetail(string id)
    {
        _selected = id;
        if (string.IsNullOrEmpty(id))
        {
            _detailName.text = "";
            _detailGenre.text = "";
            _detailBond.text = "";
            _detailHistory.text = "";
            _detailRequest.text = "Sell a tape to someone and they'll give you their number.";
            _detailHint.text = "";
            return;
        }

        _detailName.text = AlienNames.For(id);
        _detailGenre.text = AlienTaste.FavouriteGenre(id);

        int bond = TapeMemory.Bond(id);
        _detailBond.text = bond + "  " + BondWord(bond);
        int heard = TapeMemory.HeardCount(id);
        _detailHistory.text = heard + (heard == 1 ? " song" : " songs");

        var req = TapeRequests.For(id);
        if (req != null)
        {
            _detailRequest.text = "\"Got anything " + req.genre + "? I'll pay well for it.\"";
            _detailHint.text = "Make something that classifies as " + req.genre +
                               ", press it, and bring it to them in person. Matching the "
                               + "request pays a bonus on top of the usual price.";
        }
        else
        {
            _detailRequest.text = "";
            // The useful thing to say when there is no order: what to carry.
            _detailHint.text = "Nothing on order. They'll still buy " +
                               AlienTaste.FavouriteGenre(id) +
                               " — and they won't take a song they've already heard.";
        }
    }

    static string BondWord(int bond)
    {
        if (bond >= 80) return "(regular)";
        if (bond >= 45) return "(warm)";
        if (bond >= 20) return "(knows you)";
        return "(stranger)";
    }
}
