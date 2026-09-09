using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The whole NAV solar map — orbit rings, the jump-range circle, planet discs,
/// the sun's glow, leader lines and the starfield — drawn as ONE UI mesh.
///
/// UGUI has no line or circle primitive, and doing this the obvious way (an
/// Image per orbit, an Image per planet) would be a few dozen extra objects and
/// draw calls on a screen that is also mirrored to the cockpit monitor. A
/// single <see cref="MaskableGraphic"/> costs one draw call however much is on
/// it, and gives us arcs and dashes, which a scaled ring sprite cannot do at
/// all.
///
/// Usage is immediate-mode: <see cref="Clear"/>, add shapes, <see cref="Commit"/>.
/// Nothing is retained between frames, so the caller never has to reconcile
/// what changed — it just describes the map again. The caller (see
/// ShuttleComputerNavMapUI) does that on a fixed cadence rather than every
/// frame; the planets move slowly enough that nobody can tell.
///
/// Coordinates are this RectTransform's local space: (0,0) is the middle of the
/// map, +y is up. Text cannot live in a mesh, so labels are TMP children on top.
/// </summary>
[AddComponentMenu("")]
public class NavMapGraphic : MaskableGraphic
{
    enum Kind : byte { Arc, Fan, Quad }

    struct Cmd
    {
        public Kind kind;
        public Vector2 a, b, offset;
        public float r, w, a0, a1, dash, gap;
        public Color c0, c1;
    }

    readonly List<Cmd> _cmds = new List<Cmd>(320);

    /// A stroked segment is this many pixels long before we bend again. Small
    /// enough that a 250 px orbit reads as a circle, large enough that a
    /// zoomed-in rail does not blow the vertex budget.
    const float PixelsPerSegment = 12f;
    const int MaxSegments = 160;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;      // the map handles its own hit testing
    }

    // ── immediate-mode API ───────────────────────────────────────────────────

    public void Clear() { _cmds.Clear(); }
    public void Commit() { SetVerticesDirty(); }

    public void Ring(Vector2 centre, float radius, Color c, float width)
    {
        Arc(centre, radius, 0f, Mathf.PI * 2f, c, width);
    }

    public void DashedRing(Vector2 centre, float radius, Color c, float width, float dash, float gap)
    {
        _cmds.Add(new Cmd { kind = Kind.Arc, a = centre, r = radius, c0 = c, c1 = c,
                            w = width, a0 = 0f, a1 = Mathf.PI * 2f, dash = dash, gap = gap });
    }

    /// <summary>Angles in radians, counter-clockwise, screen-up = +y.</summary>
    public void Arc(Vector2 centre, float radius, float from, float to, Color c, float width)
    {
        _cmds.Add(new Cmd { kind = Kind.Arc, a = centre, r = radius, c0 = c, c1 = c,
                            w = width, a0 = from, a1 = to });
    }

    /// <summary>A filled circle. <paramref name="lightOffset"/> shifts the centre
    /// vertex so the gradient runs from the lit side, which is what stops the
    /// planets reading as flat dots on a chart.</summary>
    public void Disc(Vector2 centre, float radius, Color inner, Color outer, Vector2 lightOffset)
    {
        _cmds.Add(new Cmd { kind = Kind.Fan, a = centre, r = radius,
                            c0 = inner, c1 = outer, offset = lightOffset });
    }

    public void Disc(Vector2 centre, float radius, Color c) => Disc(centre, radius, c, c, Vector2.zero);

    public void Line(Vector2 from, Vector2 to, Color c, float width)
    {
        _cmds.Add(new Cmd { kind = Kind.Quad, a = from, b = to, c0 = c, c1 = c, w = width });
    }

    public void Dot(Vector2 p, float size, Color c)
    {
        _cmds.Add(new Cmd { kind = Kind.Quad, a = new Vector2(p.x - size * 0.5f, p.y),
                            b = new Vector2(p.x + size * 0.5f, p.y), c0 = c, c1 = c, w = size });
    }

    // ── mesh ─────────────────────────────────────────────────────────────────

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        for (int i = 0; i < _cmds.Count; i++)
        {
            var cmd = _cmds[i];
            switch (cmd.kind)
            {
                case Kind.Arc:  EmitArc(vh, cmd);  break;
                case Kind.Fan:  EmitFan(vh, cmd);  break;
                case Kind.Quad: EmitQuad(vh, cmd); break;
            }
        }
    }

    static readonly Vector2 UV = new Vector2(0.5f, 0.5f);

    void EmitArc(VertexHelper vh, Cmd c)
    {
        if (c.r <= 0.01f || c.w <= 0f) return;
        float span = c.a1 - c.a0;
        if (Mathf.Abs(span) < 1e-5f) return;

        int steps = Mathf.Clamp(
            Mathf.CeilToInt(Mathf.Abs(span) * c.r / PixelsPerSegment), 3, MaxSegments);
        float step = span / steps;
        float half = c.w * 0.5f;
        float rIn = Mathf.Max(0f, c.r - half), rOut = c.r + half;
        bool dashed = c.dash > 0.01f;
        float cycle = c.dash + c.gap;

        for (int i = 0; i < steps; i++)
        {
            float t0 = c.a0 + step * i, t1 = t0 + step;
            if (dashed)
            {
                // Where along the arc this segment starts, in pixels — so the
                // dash length stays constant however big the circle is.
                float along = Mathf.Abs(t0 - c.a0) * c.r;
                if (Mathf.Repeat(along, cycle) >= c.dash) continue;
            }

            float s0 = Mathf.Sin(t0), k0 = Mathf.Cos(t0);
            float s1 = Mathf.Sin(t1), k1 = Mathf.Cos(t1);
            int v = vh.currentVertCount;
            vh.AddVert(new Vector3(c.a.x + k0 * rIn,  c.a.y + s0 * rIn),  c.c0, UV);
            vh.AddVert(new Vector3(c.a.x + k0 * rOut, c.a.y + s0 * rOut), c.c0, UV);
            vh.AddVert(new Vector3(c.a.x + k1 * rOut, c.a.y + s1 * rOut), c.c0, UV);
            vh.AddVert(new Vector3(c.a.x + k1 * rIn,  c.a.y + s1 * rIn),  c.c0, UV);
            vh.AddTriangle(v, v + 1, v + 2);
            vh.AddTriangle(v, v + 2, v + 3);
        }
    }

    void EmitFan(VertexHelper vh, Cmd c)
    {
        if (c.r <= 0.01f) return;
        int steps = Mathf.Clamp(Mathf.CeilToInt(c.r * 0.9f), 10, 64);
        int centre = vh.currentVertCount;
        vh.AddVert(new Vector3(c.a.x + c.offset.x, c.a.y + c.offset.y), c.c0, UV);
        for (int i = 0; i <= steps; i++)
        {
            float t = Mathf.PI * 2f * i / steps;
            vh.AddVert(new Vector3(c.a.x + Mathf.Cos(t) * c.r, c.a.y + Mathf.Sin(t) * c.r), c.c1, UV);
        }
        for (int i = 0; i < steps; i++) vh.AddTriangle(centre, centre + 1 + i, centre + 2 + i);
    }

    void EmitQuad(VertexHelper vh, Cmd c)
    {
        Vector2 d = c.b - c.a;
        float len = d.magnitude;
        if (len < 1e-4f || c.w <= 0f) return;
        Vector2 n = new Vector2(-d.y, d.x) / len * (c.w * 0.5f);
        int v = vh.currentVertCount;
        vh.AddVert(new Vector3(c.a.x + n.x, c.a.y + n.y), c.c0, UV);
        vh.AddVert(new Vector3(c.b.x + n.x, c.b.y + n.y), c.c0, UV);
        vh.AddVert(new Vector3(c.b.x - n.x, c.b.y - n.y), c.c0, UV);
        vh.AddVert(new Vector3(c.a.x - n.x, c.a.y - n.y), c.c0, UV);
        vh.AddTriangle(v, v + 1, v + 2);
        vh.AddTriangle(v, v + 2, v + 3);
    }
}
