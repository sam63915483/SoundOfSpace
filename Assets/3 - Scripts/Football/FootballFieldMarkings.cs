using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Paints a football field onto whatever grass this sits over: yard lines
/// every 5, goal lines, sidelines, hash marks, the mow stripes, and the yard
/// numbers with the arrow that points at the nearer goal line. Built from
/// FootballField's dimensions, so the paint always matches the sim.
///
/// Sits under FieldRoot (Sam, 2026-09-18: "the football lines and then the
/// numbers with an arrow saying 10 yards to goal line, and the 50 in the
/// middle"). The stadium pack's pitch is a soccer field — the builder puts one
/// flat grass on all of it (stripes and the cut-in soccer lines alike), and
/// this draws the football on top.
///
/// Geometry, not a texture: one mesh for every white mark (one draw call, crisp
/// at any distance), one mesh of translucent dark bands for the mowing stripes
/// (so they land exactly between the yard lines, unlike the pack's 6.3 m
/// stripes), and a TextMeshPro per digit. All of it hovers a few centimetres
/// over the grass — far enough not to z-fight, too close to see from the
/// stands. Runs in the editor too (ExecuteAlways) so the scene view shows the
/// field; the generated children are DontSave and rebuilt on every enable.
///
/// Cyclops note: the stripes and digits are transparent-queue, which the
/// atmosphere post does not scatter. If they look pasted-on at distance out
/// there, switch them to opaque (a darker grass material for the stripes).
/// </summary>
[ExecuteAlways]
public class FootballFieldMarkings : MonoBehaviour
{
    [Tooltip("Height of the paint above the FieldRoot plane, metres. The pack's pitch surface is ~0.005; anything under ~0.01 z-fights.")]
    public float paintHeight = 0.03f;
    [Tooltip("Yard-line / hash-mark width, metres (NFL: 4 in).")]
    public float lineWidth = 0.1f;
    [Tooltip("Sideline and end-line width, metres (NFL border is 6 ft; narrower reads better at this scale).")]
    public float borderWidth = 0.5f;
    [Tooltip("Goal-line width, metres (NFL: 8 in).")]
    public float goalLineWidth = 0.2f;
    [Tooltip("How far past the sidelines the mow stripes run, metres (the stadium's grass ends ~19 m out).")]
    public float stripeOverhang = 18f;
    [Range(0f, 0.6f)] public float stripeDarkness = 0.16f;
    [Tooltip("Cap height of the yard numbers, metres (NFL: 6 ft).")]
    public float numberHeight = 2.2f;
    [Range(0f, 0.5f)] [Tooltip("Stroke thickening on the digits (TMP face dilate) — the font is a text face, field numbers are block letters.")]
    public float numberBoldness = 0.3f;
    [Tooltip("Distance from the sideline to the BOTTOM of the numbers, metres (NFL: 12 yd).")]
    public float numberInset = 12f;
    public Color lineColor = Color.white;
    public Color numberColor = Color.white;

    const string GeneratedName = "__Markings";
    readonly List<Object> _owned = new List<Object>();
    Material _digitMat;

    void OnEnable()  { Rebuild(); }
    void OnDisable() { Clear(); }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        Clear();
        var root = new GameObject(GeneratedName);
        root.hideFlags = HideFlags.DontSave;
        root.transform.SetParent(transform, false);
        _owned.Add(root);

        BuildLines(root.transform);
        BuildStripes(root.transform);
        BuildNumbers(root.transform);
    }

    void Clear()
    {
        // Anything from a previous enable (or a stale one that survived a
        // domain reload) — DontSave children never clean themselves up.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i);
            if (c.name == GeneratedName) DestroyNow(c.gameObject);
        }
        foreach (var o in _owned) if (o != null) DestroyNow(o);
        _owned.Clear();
    }

    static void DestroyNow(Object o)
    {
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    // ── white marks ────────────────────────────────────────────────────────

    void BuildLines(Transform parent)
    {
        float hw = FootballField.HalfWidth;
        float gz = FootballField.GoalLineZ;
        float ez = FootballField.EndLineZ;
        float yd = FootballField.MetresPerYard;
        var mb = new MeshBuilder();

        // Yard lines every 5 yards, goal line to goal line (exclusive).
        for (int y = 5; y < FootballField.PlayingLengthYd; y += 5)
        {
            float z = -gz + y * yd;
            mb.Rect(-hw, hw, z - lineWidth * 0.5f, z + lineWidth * 0.5f);
        }
        // Goal lines.
        mb.Rect(-hw, hw, -gz - goalLineWidth * 0.5f, -gz + goalLineWidth * 0.5f);
        mb.Rect(-hw, hw,  gz - goalLineWidth * 0.5f,  gz + goalLineWidth * 0.5f);
        // Sidelines and end lines — the border sits OUTSIDE the field of play.
        mb.Rect(-hw - borderWidth, -hw, -ez - borderWidth, ez + borderWidth);
        mb.Rect( hw, hw + borderWidth, -ez - borderWidth, ez + borderWidth);
        mb.Rect(-hw, hw, -ez - borderWidth, -ez);
        mb.Rect(-hw, hw,  ez, ez + borderWidth);

        // Hash marks every yard that isn't a 5: two rows inboard (NFL hashes
        // are 18'6" apart) and one just inside each sideline.
        float hashLen = 0.67f * yd;                 // 2 ft
        float hashX   = 2.82f * yd;                 // half of 18'6"
        for (int y = 1; y < FootballField.PlayingLengthYd; y++)
        {
            if (y % 5 == 0) continue;
            float z = -gz + y * yd;
            float z0 = z - lineWidth * 0.5f, z1 = z + lineWidth * 0.5f;
            mb.Rect(-hashX - hashLen * 0.5f, -hashX + hashLen * 0.5f, z0, z1);
            mb.Rect( hashX - hashLen * 0.5f,  hashX + hashLen * 0.5f, z0, z1);
            mb.Rect(-hw, -hw + hashLen, z0, z1);
            mb.Rect( hw - hashLen, hw, z0, z1);
        }

        // Arrows beside every number except the 50, pointing at the nearer
        // goal line (NFL: 36" long, 18" base, at the top of the number on the
        // goal-line side). "Top" of a number is toward midfield — the numbers
        // read from their own sideline.
        float arrowLen = 1.3f * yd, arrowBase = 0.65f * yd;   // NFL is 36" × 18"; a touch bigger reads from the upper tier
        for (int y = 10; y < FootballField.PlayingLengthYd; y += 10)
        {
            float z = -gz + y * yd;
            if (Mathf.Abs(z) < 0.01f) continue;         // the 50
            float g = Mathf.Sign(z);                     // toward the nearer goal line
            for (int side = -1; side <= 1; side += 2)
            {
                float xNum = NumberCentreX(side);
                float xArrow = xNum - side * numberHeight * 0.45f;   // toward midfield = the number's top
                float zBase = z + g * (numberHeight * 1.1f);
                float zTip  = zBase + g * arrowLen;
                mb.Tri(new Vector2(xArrow - arrowBase * 0.5f, zBase),
                       new Vector2(xArrow + arrowBase * 0.5f, zBase),
                       new Vector2(xArrow, zTip));
            }
        }

        var mat = new Material(FootballShader.Standard) { name = "FieldLines", color = lineColor };
        mat.SetFloat("_Glossiness", 0.15f);
        _owned.Add(mat);
        MakeRenderer(parent, "Lines", mb.Build("FieldLines"), mat, paintHeight);
    }

    // ── mow stripes ────────────────────────────────────────────────────────

    void BuildStripes(Transform parent)
    {
        float gz = FootballField.GoalLineZ;
        float yd = FootballField.MetresPerYard;
        float x  = FootballField.HalfWidth + stripeOverhang;
        var mb = new MeshBuilder();
        int bands = Mathf.RoundToInt(FootballField.PlayingLengthYd / 5f);
        for (int i = 0; i < bands; i++)
        {
            if ((i & 1) == 0) continue;                  // every other 5-yard band is the dark one
            float z0 = -gz + i * 5f * yd, z1 = z0 + 5f * yd;
            mb.Rect(-x, x, z0, z1);
        }
        var mat = new Material(FootballShader.Standard) { name = "FieldStripes" };
        SetFade(mat, new Color(0f, 0f, 0f, stripeDarkness));
        _owned.Add(mat);
        // Just under the lines: ZWrite is off in Fade mode, so line pixels
        // already in the depth buffer win and the white stays white.
        MakeRenderer(parent, "Stripes", mb.Build("FieldStripes"), mat, paintHeight - 0.01f);
    }

    // ── numbers ────────────────────────────────────────────────────────────

    float NumberCentreX(int side) => side * (FootballField.HalfWidth - numberInset - numberHeight * 0.5f);

    void BuildNumbers(Transform parent)
    {
        _digitMat = null;
        float gz = FootballField.GoalLineZ;
        float yd = FootballField.MetresPerYard;
        // TMP: font size 10 ≈ a 1 m line; cap height is ~0.7 of that.
        float fontSize = numberHeight / 0.07f;
        for (int y = 10; y < FootballField.PlayingLengthYd; y += 10)
        {
            float z = -gz + y * yd;
            string label = Mathf.RoundToInt(FootballField.YardLineLabel(z)).ToString("00");
            for (int side = -1; side <= 1; side += 2)
            {
                // A pivot on the yard line, lying flat, "up" toward midfield so
                // the number reads from its own sideline; the two digits sit
                // either side of the line with the line running between them.
                var pivot = new GameObject("Number " + label + (side < 0 ? " L" : " R"));
                pivot.transform.SetParent(parent, false);
                pivot.transform.localPosition = new Vector3(NumberCentreX(side), paintHeight + 0.005f, z);
                pivot.transform.localRotation = Quaternion.Euler(90f, side < 0 ? 90f : -90f, 0f);
                float gap = numberHeight * 0.42f;
                Digit(pivot.transform, label[0].ToString(), -gap, fontSize);
                Digit(pivot.transform, label[1].ToString(),  gap, fontSize);
            }
        }
    }

    void Digit(Transform pivot, string text, float localX, float fontSize)
    {
        var go = new GameObject("Digit " + text);
        go.transform.SetParent(pivot, false);
        go.transform.localPosition = new Vector3(localX, 0f, 0f);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = numberColor;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.GetComponent<RectTransform>().sizeDelta = new Vector2(numberHeight, numberHeight);
        // One thickened copy of the font's material, shared by every digit
        // (a per-text instance would be 36 materials). Same atlas, so TMP
        // accepts it as this font's material.
        if (_digitMat == null && tmp.fontSharedMaterial != null)
        {
            _digitMat = new Material(tmp.fontSharedMaterial) { name = "FieldDigits" };
            _digitMat.SetFloat(ShaderUtilities.ID_FaceDilate, numberBoldness);
            _owned.Add(_digitMat);
        }
        if (_digitMat != null) tmp.fontSharedMaterial = _digitMat;
    }

    // ── helpers ────────────────────────────────────────────────────────────

    void MakeRenderer(Transform parent, string name, Mesh mesh, Material mat, float height)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, height, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
        _owned.Add(mesh);
    }

    static void SetFade(Material m, Color c)
    {
        m.color = c;
        m.SetFloat("_Mode", 2f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON"); m.EnableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.SetFloat("_Glossiness", 0f);
    }

    /// Flat quads and triangles in the field's XZ plane, normals up.
    class MeshBuilder
    {
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<int> _t = new List<int>();

        public void Rect(float x0, float x1, float z0, float z1)
        {
            int b = _v.Count;
            _v.Add(new Vector3(x0, 0f, z0)); _v.Add(new Vector3(x0, 0f, z1));
            _v.Add(new Vector3(x1, 0f, z1)); _v.Add(new Vector3(x1, 0f, z0));
            _t.Add(b); _t.Add(b + 1); _t.Add(b + 2);
            _t.Add(b); _t.Add(b + 2); _t.Add(b + 3);
        }

        /// Winding is fixed up so the face points +Y whatever order the corners come in.
        public void Tri(Vector2 a, Vector2 b, Vector2 c)
        {
            int i = _v.Count;
            _v.Add(new Vector3(a.x, 0f, a.y)); _v.Add(new Vector3(b.x, 0f, b.y)); _v.Add(new Vector3(c.x, 0f, c.y));
            float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (cross > 0f) { _t.Add(i); _t.Add(i + 2); _t.Add(i + 1); }
            else            { _t.Add(i); _t.Add(i + 1); _t.Add(i + 2); }
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(_v);
            m.SetTriangles(_t, 0);
            var n = new Vector3[_v.Count];
            for (int i = 0; i < n.Length; i++) n[i] = Vector3.up;
            m.normals = n;
            m.RecalculateBounds();
            return m;
        }
    }
}
