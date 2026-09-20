using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// The big board (Sam, 2026-09-18): "a bunch of small lights in an 8 position"
/// — every number is a dot-matrix seven-segment digit, 21 lamps on a 5×9
/// grid (three per segment), lit amber or left dark. Shows both scores, the quarter, the clock,
/// down, distance, where the ball is, who has it (a lamp by the score) and a
/// status line (KICKOFF / HALFTIME / FINAL). Reads FootballMatch.Instance;
/// with no match it shows a demo so it can be placed in the editor.
///
/// Free-standing: drop it anywhere, rotate it to face the stands. Builds its
/// own lamps and labels (ExecuteAlways, DontSave children) so the editor
/// shows the real thing while Sam moves it. One mesh per digit; a digit's
/// lamps are vertex colours, rewritten only when its value changes.
/// </summary>
[ExecuteAlways]
public class FootballScoreboard : MonoBehaviour
{
    [Header("Size (metres)")]
    public float width = 16f;
    public float height = 8f;
    public float depth = 0.5f;
    // Lamp colours are code, not scene values: the scene keeps whatever the
    // component had when it was placed, and Sam has placed it.
    static readonly Color lampOn = new Color(1f, 0.78f, 0.22f);
    static readonly Color lampOff = new Color(0.07f, 0.055f, 0.04f);   // near-black: from the stands an off lamp must vanish
    static readonly Color possessionOn = new Color(0.3f, 1f, 0.35f);
    static readonly Color boardColor = new Color(0.06f, 0.06f, 0.07f);
    static readonly Color labelColor = new Color(0.85f, 0.85f, 0.85f);
    public string homeLabel = "HOME";
    public string awayLabel = "GUEST";

    const string GeneratedName = "__Board";
    readonly List<Object> _owned = new List<Object>();

    // Live parts.
    Digit[] _homeScore, _awayScore, _quarter, _clockMin, _clockSec, _down, _toGo, _ballOn;
    Lamp _homePoss, _awayPoss, _colonTop, _colonBot;
    TextMeshPro _homeName, _awayName, _status;
    Material _lampMat;
    float _blink;

    void OnEnable()  { Build(); }
    void OnDisable() { Clear(); }

    void Awake()
    {
        // Play mode: any generated board left over from the editor (DontSave
        // children can outlive a scene reload) would draw on top of the live
        // one. Sweep the whole scene for them before building.
        if (!Application.isPlaying) return;
        foreach (var t in FindObjectsOfType<Transform>(true))
            if (t != null && t.name == GeneratedName && t.parent != transform) Destroy(t.gameObject);
    }

    void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i);
            if (c.name == GeneratedName) { if (Application.isPlaying) Destroy(c.gameObject); else DestroyImmediate(c.gameObject); }
        }
        foreach (var o in _owned) if (o != null) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }
        _owned.Clear();
    }

    [ContextMenu("Rebuild")]
    public void Build()
    {
        Clear();
        var root = new GameObject(GeneratedName) { hideFlags = HideFlags.DontSave };
        root.transform.SetParent(transform, false);
        _owned.Add(root);

        // The slab. Front face is −Z: rotate the board so −Z looks at the crowd.
        var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slab.name = "Slab";
        slab.transform.SetParent(root.transform, false);
        slab.transform.localScale = new Vector3(width, height, depth);
        var slabMat = new Material(FootballShader.Standard) { name = "Board", color = boardColor };
        slabMat.SetFloat("_Glossiness", 0.2f);
        slab.GetComponent<Renderer>().sharedMaterial = slabMat;
        _owned.Add(slabMat);

        var lampShader = FootballShader.Sprite;
        if (lampShader == null) lampShader = FootballShader.Unlit;
        _lampMat = new Material(lampShader) { name = "Lamps", color = Color.white };
        _owned.Add(_lampMat);

        float z = -depth * 0.5f - 0.03f;          // just proud of the face
        float u = width / 16f;                    // layout unit: the board's 16ths
        float sBig = u * 0.32f, sMid = u * 0.19f;   // lamp pitches

        // ── top: names and scores ──
        float yTop = height * 0.27f;
        _homeName = Label(root.transform, homeLabel, new Vector3(-u * 5.6f, yTop + u * 1.55f, z), u * 0.9f, TextAlignmentOptions.Center);
        _awayName = Label(root.transform, awayLabel, new Vector3( u * 5.6f, yTop + u * 1.55f, z), u * 0.9f, TextAlignmentOptions.Center);
        _homeScore = Digits(root.transform, 2, new Vector3(-u * 5.6f, yTop - u * 0.4f, z), sBig);
        _awayScore = Digits(root.transform, 2, new Vector3( u * 5.6f, yTop - u * 0.4f, z), sBig);
        _homePoss = new Lamp(root.transform, new Vector3(-u * 7.4f, yTop - u * 0.4f, z), sBig * 1.2f, _lampMat, lampOff);
        _awayPoss = new Lamp(root.transform, new Vector3( u * 7.4f, yTop - u * 0.4f, z), sBig * 1.2f, _lampMat, lampOff);
        _owned.Add(_homePoss.go); _owned.Add(_awayPoss.go); _owned.Add(_homePoss.mesh); _owned.Add(_awayPoss.mesh);

        // Centre: the clock, quarter under it.
        // A big digit is 5 columns = 4 pitches wide and the group advances
        // 6.2 pitches; the minute digit, colon and seconds must not share
        // columns (they did: the minute and the tens-of-seconds overlapped
        // by a column with the colon inside it, so a 0 in the seconds read
        // as part of the minute — Sam's "the 0 overtakes the first number").
        float dwBig = sBig * 6.2f;
        _clockMin = Digits(root.transform, 1, new Vector3(-dwBig * 0.95f, yTop, z), sBig);
        _clockSec = Digits(root.transform, 2, new Vector3( dwBig * 0.68f, yTop, z), sBig);
        float colonX = -dwBig * 0.28f;
        _colonTop = new Lamp(root.transform, new Vector3(colonX, yTop + sBig * 1.5f, z), sBig * 0.8f, _lampMat, lampOn);
        _colonBot = new Lamp(root.transform, new Vector3(colonX, yTop - sBig * 1.5f, z), sBig * 0.8f, _lampMat, lampOn);
        _owned.Add(_colonTop.go); _owned.Add(_colonBot.go); _owned.Add(_colonTop.mesh); _owned.Add(_colonBot.mesh);

        // ── middle row: QTR · DOWN · TO GO · BALL ON ──
        float yMid = -height * 0.12f;
        Label(root.transform, "QTR", new Vector3(-u * 5.6f, yMid + u * 1.2f, z), u * 0.55f, TextAlignmentOptions.Center);
        _quarter = Digits(root.transform, 1, new Vector3(-u * 5.6f, yMid - u * 0.3f, z), sMid);
        Label(root.transform, "DOWN", new Vector3(-u * 2.0f, yMid + u * 1.2f, z), u * 0.55f, TextAlignmentOptions.Center);
        _down = Digits(root.transform, 1, new Vector3(-u * 2.0f, yMid - u * 0.3f, z), sMid);
        Label(root.transform, "TO GO", new Vector3(u * 1.8f, yMid + u * 1.2f, z), u * 0.55f, TextAlignmentOptions.Center);
        _toGo = Digits(root.transform, 2, new Vector3(u * 1.8f, yMid - u * 0.3f, z), sMid);
        Label(root.transform, "BALL ON", new Vector3(u * 5.6f, yMid + u * 1.2f, z), u * 0.55f, TextAlignmentOptions.Center);
        _ballOn = Digits(root.transform, 2, new Vector3(u * 5.6f, yMid - u * 0.3f, z), sMid);

        // ── bottom: status ──
        _status = Label(root.transform, "ALIEN FOOTBALL", new Vector3(0f, -height * 0.38f, z), u * 0.8f, TextAlignmentOptions.Center);
        _status.color = lampOn;

        Refresh(true);
    }

    void Update()
    {
        _blink += Application.isPlaying ? Time.deltaTime : 0f;
        Refresh(false);
    }

    void Refresh(bool force)
    {
        var m = Application.isPlaying ? FootballMatch.Instance : null;
        if (m == null || m.Current == FootballMatch.State.Idle)
        {
            // Demo face for placing it.
            Set(_homeScore, 21); Set(_awayScore, 17); Set(_quarter, 4);
            Set(_clockMin, 2); Set(_clockSec, 30, true); Set(_down, 3); Set(_toGo, 7); Set(_ballOn, 42);
            _homePoss.Set(possessionOn); _awayPoss.Set(lampOff);
            if (_homeName != null) _homeName.text = homeLabel;
            if (_awayName != null) _awayName.text = awayLabel;
            if (_status != null) _status.text = "ALIEN FOOTBALL";
            _colonTop.Set(lampOn); _colonBot.Set(lampOn);
            return;
        }

        Set(_homeScore, m.home.score); Set(_awayScore, m.away.score);
        Set(_quarter, m.Quarter);
        int secs = Mathf.CeilToInt(m.ClockSeconds);
        Set(_clockMin, secs / 60); Set(_clockSec, secs % 60, true);
        bool live = m.Current == FootballMatch.State.Play || m.Current == FootballMatch.State.DeadBall;
        bool ballInPlay = live || m.Current == FootballMatch.State.Kickoff;
        if (live) { Set(_down, m.Down); Set(_toGo, Mathf.CeilToInt(m.ToGo)); }
        else { Set(_down, -1); Set(_toGo, -1); }
        if (ballInPlay && m.Possession != null) Set(_ballOn, Mathf.RoundToInt(FootballField.YardLineLabel(m.LineOfScrimmageZ)));
        else Set(_ballOn, -1);
        bool homeHas = m.Possession == m.home && ballInPlay, awayHas = m.Possession == m.away && ballInPlay;
        _homePoss.Set(homeHas ? possessionOn : lampOff);
        _awayPoss.Set(awayHas ? possessionOn : lampOff);
        // Colon blinks while the clock runs.
        bool colon = !live || (_blink % 1f) < 0.5f;
        _colonTop.Set(colon ? lampOn : lampOff); _colonBot.Set(colon ? lampOn : lampOff);

        if (_homeName != null) _homeName.text = m.home.shortName;
        if (_awayName != null) _awayName.text = m.away.shortName;
        if (_status != null)
        {
            string st = m.Current switch
            {
                FootballMatch.State.CoinToss => "COIN TOSS",
                FootballMatch.State.Kickoff => "KICKOFF",
                FootballMatch.State.Score => "TOUCHDOWN",
                FootballMatch.State.QuarterBreak => m.Quarter == 2 ? "HALFTIME" : "END OF QUARTER",
                FootballMatch.State.GameOver => "FINAL",
                _ => m.Possession != null ? m.Possession.name.ToUpperInvariant() + " BALL" : "",
            };
            if (_status.text != st) _status.text = st;
        }
    }

    // ── parts ──────────────────────────────────────────────────────────────

    TextMeshPro Label(Transform parent, string text, Vector3 pos, float size, TextAlignmentOptions align)
    {
        var go = new GameObject("Label " + text);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;         // TMP at identity reads from −Z, the board's front
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = size * 10f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = align;
        tmp.color = labelColor;
        tmp.enableWordWrapping = false;
        tmp.GetComponent<RectTransform>().sizeDelta = new Vector2(size * 12f, size * 1.6f);
        return tmp;
    }

    Digit[] Digits(Transform parent, int count, Vector3 centre, float pitch)
    {
        var arr = new Digit[count];
        float dw = pitch * 6.2f;                      // digit advance (5 columns + a gap)
        float x0 = centre.x - dw * (count - 1) * 0.5f;
        for (int i = 0; i < count; i++)
        {
            arr[i] = new Digit(parent, new Vector3(x0 + dw * i, centre.y, centre.z), pitch, _lampMat, lampOn, lampOff);
            _owned.Add(arr[i].go); _owned.Add(arr[i].mesh);
        }
        return arr;
    }

    /// Writes a number across a digit group. −1 = blank. Leading zeros are
    /// dark unless `zeroPad` (the clock's seconds).
    static void Set(Digit[] d, int value, bool zeroPad = false)
    {
        if (d == null) return;
        for (int i = d.Length - 1; i >= 0; i--)
        {
            int v;
            if (value < 0) v = -1;
            else { v = value % 10; value /= 10; if (v == 0 && value == 0 && i < d.Length - 1 && !zeroPad) v = -1; }
            d[i].Set(v);
        }
    }

    /// One lamp: a quad.
    class Lamp
    {
        public readonly GameObject go;
        public readonly Mesh mesh;
        readonly MeshFilter _mf;
        Color _cur;
        public Lamp(Transform parent, Vector3 pos, float size, Material mat, Color c)
        {
            go = new GameObject("Lamp");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            _mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mesh = Digit.Quad(size, c);
            _mf.sharedMesh = mesh;
            _cur = c;
        }
        public void Set(Color c)
        {
            if (c == _cur || _mf == null || _mf.sharedMesh == null) return;
            _cur = c;
            var cols = _mf.sharedMesh.colors;
            for (int i = 0; i < cols.Length; i++) cols[i] = c;
            _mf.sharedMesh.colors = cols;
        }
    }

    /// One seven-segment digit made of lamps on a 5×9 grid (7 segments × 3).
    class Digit
    {
        public readonly GameObject go;
        public readonly Mesh mesh;
        readonly Color _on, _off;
        const int LampCount = 21;
        readonly int[] _lampSeg = new int[LampCount];   // segment index of each lamp
        int _value = int.MinValue;

        // Segment bits: a b c d e f g = 1 2 4 8 16 32 64
        static readonly int[] Glyph = { 63, 6, 91, 79, 102, 109, 125, 7, 127, 111 };

        public Digit(Transform parent, Vector3 pos, float pitch, Material mat, Color on, Color off)
        {
            _on = on; _off = off;
            go = new GameObject("Digit");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Lamp grid: columns 0..4, rows 0..8 (row 0 at the top).
            var verts = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();
            int n = 0;
            float r = pitch * 0.42f;
            void Add(int col, int row, int seg)
            {
                float x = (col - 2) * pitch, y = (4 - row) * pitch;   // row 0 at the top, col 4 on the viewer's right
                int b = verts.Count;
                verts.Add(new Vector3(x - r, y - r, 0f)); verts.Add(new Vector3(x - r, y + r, 0f));
                verts.Add(new Vector3(x + r, y + r, 0f)); verts.Add(new Vector3(x + r, y - r, 0f));
                uvs.Add(Vector2.zero); uvs.Add(Vector2.up); uvs.Add(Vector2.one); uvs.Add(Vector2.right);
                // Face −Z: counter-clockwise seen from +Z is clockwise from −Z.
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                _lampSeg[n++] = seg;
            }
            for (int c = 1; c <= 3; c++) Add(c, 0, 0);            // a
            for (int rr = 1; rr <= 3; rr++) Add(4, rr, 1);        // b
            for (int rr = 5; rr <= 7; rr++) Add(4, rr, 2);        // c
            for (int c = 1; c <= 3; c++) Add(c, 8, 3);            // d
            for (int rr = 5; rr <= 7; rr++) Add(0, rr, 4);        // e
            for (int rr = 1; rr <= 3; rr++) Add(0, rr, 5);        // f
            for (int c = 1; c <= 3; c++) Add(c, 4, 6);            // g

            mesh = new Mesh { name = "Digit" };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            var cols = new Color[verts.Count];
            for (int i = 0; i < cols.Length; i++) cols[i] = off;
            mesh.colors = cols;
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
        }

        public void Set(int v)
        {
            if (v == _value) return;
            _value = v;
            int bits = v >= 0 && v <= 9 ? Glyph[v] : 0;
            var cols = mesh.colors;
            for (int i = 0; i < LampCount; i++)
            {
                Color c = (bits & (1 << _lampSeg[i])) != 0 ? _on : _off;
                cols[i * 4] = cols[i * 4 + 1] = cols[i * 4 + 2] = cols[i * 4 + 3] = c;
            }
            mesh.colors = cols;
        }

        public static Mesh Quad(float size, Color c)
        {
            float r = size * 0.38f;
            var m = new Mesh { name = "Lamp" };
            m.vertices = new[] { new Vector3(-r, -r, 0f), new Vector3(-r, r, 0f), new Vector3(r, r, 0f), new Vector3(r, -r, 0f) };
            m.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.colors = new[] { c, c, c, c };
            m.RecalculateBounds();
            return m;
        }
    }
}
