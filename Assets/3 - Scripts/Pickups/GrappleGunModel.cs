using UnityEngine;

/// <summary>
/// Builds the grapple gun's look at runtime from primitives, in the game's
/// low-poly style, so the prototype needs no imported art. Three things:
///
///   BuildGun(parent)  — the viewmodel. Barrel along +Z, grip down -Y, same
///                       convention as the pistol prefabs. Children the
///                       controller looks up by name: "Muzzle" (rope + hook
///                       start) and "HookHead" (the detachable grapnel seated
///                       in the barrel; hidden while a shot is out).
///   BuildHook(parent) — the grapnel projectile. Origin = tail (rope attach),
///                       tip at +Z, four curled prongs.
///   BuildIcon()       — a 128×128 side-view sprite for the hotbar / locker.
///
/// Materials use the Standard shader (referenced by hundreds of scene
/// materials, so never stripped from a build) and are shared across calls.
/// </summary>
public static class GrappleGunModel
{
    // Palette (low-poly, slightly desaturated so it sits with the asset packs).
    static readonly Color GunMetal   = new Color(0.22f, 0.24f, 0.27f);
    static readonly Color GunMetalLt = new Color(0.36f, 0.39f, 0.43f);
    static readonly Color Brass      = new Color(0.72f, 0.55f, 0.24f);
    static readonly Color Copper     = new Color(0.62f, 0.36f, 0.22f);
    static readonly Color Wood       = new Color(0.36f, 0.22f, 0.12f);
    static readonly Color Rope       = new Color(0.45f, 0.30f, 0.14f);
    static readonly Color Steel      = new Color(0.60f, 0.62f, 0.66f);
    static readonly Color Rubber     = new Color(0.10f, 0.10f, 0.11f);

    static Material s_gunMetal, s_gunMetalLt, s_brass, s_copper, s_wood, s_rope, s_steel, s_rubber;

    static Material Mat(ref Material slot, Color c, float metallic, float smooth)
    {
        if (slot != null) return slot;
        var sh = Shader.Find("Standard");
        slot = new Material(sh) { color = c, hideFlags = HideFlags.HideAndDontSave };
        slot.SetFloat("_Metallic", metallic);
        slot.SetFloat("_Glossiness", smooth);
        return slot;
    }

    static Material GunMetalMat   => Mat(ref s_gunMetal,   GunMetal,   0.75f, 0.45f);
    static Material GunMetalLtMat => Mat(ref s_gunMetalLt, GunMetalLt, 0.80f, 0.55f);
    static Material BrassMat      => Mat(ref s_brass,      Brass,      0.90f, 0.70f);
    static Material CopperMat     => Mat(ref s_copper,     Copper,     0.85f, 0.50f);
    static Material WoodMat       => Mat(ref s_wood,       Wood,       0.00f, 0.25f);
    static Material RopeMat       => Mat(ref s_rope,       Rope,       0.00f, 0.10f);
    static Material SteelMat      => Mat(ref s_steel,      Steel,      0.90f, 0.60f);
    static Material RubberMat     => Mat(ref s_rubber,     Rubber,     0.00f, 0.20f);

    /// <summary>The rope colour the gun's spool is wound in — the controller's line matches it.</summary>
    public static Color RopeColor => Rope;

    // ── primitives ───────────────────────────────────────────────────────

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Quaternion localRot, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return go;
    }

    static GameObject Box(string name, Transform p, Vector3 pos, Vector3 size, Material m, Quaternion? rot = null)
        => Prim(PrimitiveType.Cube, name, p, pos, rot ?? Quaternion.identity, size, m);

    /// Cylinder whose axis runs along local +Z (Unity's primitive is Y-up, so rotate it).
    static GameObject TubeZ(string name, Transform p, Vector3 pos, float radius, float length, Material m)
        => Prim(PrimitiveType.Cylinder, name, p, pos, Quaternion.Euler(90f, 0f, 0f), new Vector3(radius * 2f, length * 0.5f, radius * 2f), m);

    /// Cylinder whose axis runs along local +X.
    static GameObject TubeX(string name, Transform p, Vector3 pos, float radius, float length, Material m)
        => Prim(PrimitiveType.Cylinder, name, p, pos, Quaternion.Euler(0f, 0f, 90f), new Vector3(radius * 2f, length * 0.5f, radius * 2f), m);

    static GameObject Ball(string name, Transform p, Vector3 pos, float radius, Material m)
        => Prim(PrimitiveType.Sphere, name, p, pos, Quaternion.identity, Vector3.one * radius * 2f, m);

    // ── the gun ──────────────────────────────────────────────────────────

    public static GameObject BuildGun(Transform parent)
    {
        var root = new GameObject("GrappleGun");
        root.transform.SetParent(parent, false);
        var t = root.transform;

        // Receiver — the boxy heart of it, with a lighter top cover.
        Box("Receiver", t, new Vector3(0f, 0f, 0.04f), new Vector3(0.07f, 0.075f, 0.20f), GunMetalMat);
        Box("TopCover", t, new Vector3(0f, 0.042f, 0.03f), new Vector3(0.05f, 0.012f, 0.16f), GunMetalLtMat);
        Box("SightPost", t, new Vector3(0f, 0.058f, 0.10f), new Vector3(0.008f, 0.02f, 0.012f), BrassMat);
        Box("SightRear", t, new Vector3(0f, 0.056f, -0.03f), new Vector3(0.03f, 0.016f, 0.008f), BrassMat);

        // Barrel — a fat launch tube, brass muzzle ring, black rubber collar.
        TubeZ("Barrel", t, new Vector3(0f, 0.005f, 0.24f), 0.03f, 0.22f, GunMetalLtMat);
        TubeZ("MuzzleRing", t, new Vector3(0f, 0.005f, 0.345f), 0.036f, 0.025f, BrassMat);
        TubeZ("BarrelCollar", t, new Vector3(0f, 0.005f, 0.15f), 0.034f, 0.02f, RubberMat);

        // Pressure tank slung under the barrel, copper, two brass straps.
        TubeZ("Tank", t, new Vector3(0f, -0.04f, 0.19f), 0.021f, 0.16f, CopperMat);
        Ball("TankCapF", t, new Vector3(0f, -0.04f, 0.27f), 0.021f, CopperMat);
        Ball("TankCapB", t, new Vector3(0f, -0.04f, 0.11f), 0.021f, CopperMat);
        TubeZ("StrapF", t, new Vector3(0f, -0.04f, 0.24f), 0.024f, 0.012f, BrassMat);
        TubeZ("StrapB", t, new Vector3(0f, -0.04f, 0.14f), 0.024f, 0.012f, BrassMat);
        Box("TankGauge", t, new Vector3(0.022f, -0.03f, 0.19f), new Vector3(0.012f, 0.014f, 0.014f), BrassMat);

        // Spool at the back — drum wound with rope, brass cheek plates, a crank.
        var spool = new GameObject("Spool"); spool.transform.SetParent(t, false); spool.transform.localPosition = new Vector3(0f, 0.012f, -0.085f);
        TubeX("Drum", spool.transform, Vector3.zero, 0.030f, 0.056f, GunMetalMat);
        TubeX("RopeWind", spool.transform, Vector3.zero, 0.041f, 0.044f, RopeMat);
        TubeX("CheekL", spool.transform, new Vector3(-0.032f, 0f, 0f), 0.048f, 0.006f, BrassMat);
        TubeX("CheekR", spool.transform, new Vector3(0.032f, 0f, 0f), 0.048f, 0.006f, BrassMat);
        TubeX("Axle", spool.transform, Vector3.zero, 0.008f, 0.09f, SteelMat);
        // crank on the right cheek
        Box("CrankArm", spool.transform, new Vector3(0.05f, 0.018f, 0f), new Vector3(0.006f, 0.04f, 0.008f), SteelMat);
        TubeX("CrankKnob", spool.transform, new Vector3(0.062f, 0.036f, 0f), 0.007f, 0.02f, WoodMat);

        // Rope guide: a small brass eye on top of the receiver the line runs through.
        TubeZ("RopeEye", t, new Vector3(0f, 0.052f, 0.115f), 0.01f, 0.014f, BrassMat);

        // Grip — raked back 18°, wooden, with a steel butt cap. Trigger + guard.
        var gripRot = Quaternion.Euler(-18f, 0f, 0f);
        Box("Grip", t, new Vector3(0f, -0.085f, -0.03f), new Vector3(0.036f, 0.11f, 0.05f), WoodMat, gripRot);
        Box("GripCap", t, new Vector3(0f, -0.14f, -0.048f), new Vector3(0.04f, 0.012f, 0.055f), SteelMat, gripRot);
        Box("Trigger", t, new Vector3(0f, -0.055f, 0.03f), new Vector3(0.008f, 0.03f, 0.008f), SteelMat, Quaternion.Euler(20f, 0f, 0f));
        Box("GuardFront", t, new Vector3(0f, -0.06f, 0.055f), new Vector3(0.008f, 0.05f, 0.006f), GunMetalMat);
        Box("GuardBottom", t, new Vector3(0f, -0.083f, 0.025f), new Vector3(0.008f, 0.006f, 0.066f), GunMetalMat);

        // Muzzle — where the rope leaves and the hook seats.
        var muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(t, false);
        muzzle.transform.localPosition = new Vector3(0f, 0.005f, 0.355f);

        // The detachable grapnel, seated in the barrel with its prongs just proud of the muzzle.
        var hook = BuildHook(t);
        hook.name = "HookHead";
        hook.transform.localPosition = new Vector3(0f, 0.005f, 0.25f);
        hook.transform.localRotation = Quaternion.identity;

        return root;
    }

    // ── the grapnel ──────────────────────────────────────────────────────

    /// <summary>Length from tail (origin) to the tip, so the controller can bury the tip in the hit point.</summary>
    public const float HookLength = 0.13f;

    public static GameObject BuildHook(Transform parent)
    {
        var root = new GameObject("GrappleHook");
        if (parent != null) root.transform.SetParent(parent, false);
        var t = root.transform;

        // Shaft with a rope eye at the tail and a collar near the head.
        TubeZ("Shaft", t, new Vector3(0f, 0f, 0.05f), 0.009f, 0.10f, SteelMat);
        Ball("TailEye", t, Vector3.zero, 0.012f, BrassMat);
        TubeZ("Collar", t, new Vector3(0f, 0f, 0.085f), 0.014f, 0.016f, BrassMat);
        Ball("Head", t, new Vector3(0f, 0f, 0.10f), 0.016f, SteelMat);

        // Four prongs: out and forward from the head, then a tip that curls back.
        for (int i = 0; i < 4; i++)
        {
            float ang = i * 90f + 45f;
            Quaternion around = Quaternion.AngleAxis(ang, Vector3.forward);
            Vector3 outDir = around * Vector3.up;

            var prong = new GameObject("Prong" + i);
            prong.transform.SetParent(t, false);
            prong.transform.localPosition = new Vector3(0f, 0f, 0.10f);
            prong.transform.localRotation = Quaternion.LookRotation(Vector3.forward, outDir);

            // arm: leans 50° outward from the shaft axis
            var armRot = Quaternion.AngleAxis(-50f, Vector3.right);
            Box("Arm", prong.transform, armRot * new Vector3(0f, 0f, 0.022f), new Vector3(0.008f, 0.008f, 0.045f), SteelMat, armRot);
            // tip: bends back toward the tail (a grapnel's curl), sharpened by a slim box
            Vector3 elbow = armRot * new Vector3(0f, 0f, 0.045f);
            var tipRot = Quaternion.AngleAxis(-125f, Vector3.right);
            Box("Tip", prong.transform, elbow + tipRot * new Vector3(0f, 0f, 0.016f), new Vector3(0.007f, 0.007f, 0.034f), SteelMat, tipRot);
            Ball("Elbow", prong.transform, elbow, 0.006f, SteelMat);
        }
        return root;
    }

    // ── the icon ─────────────────────────────────────────────────────────

    static Sprite s_icon;

    /// <summary>Side-view hotbar icon (barrel to the right), built once per session.</summary>
    public static Sprite BuildIcon()
    {
        if (s_icon != null) return s_icon;
        const int S = 128;
        var px = new Color32[S * S];
        var clear = new Color32(0, 0, 0, 0);
        for (int i = 0; i < px.Length; i++) px[i] = clear;

        void Rect(int x0, int y0, int x1, int y1, Color c)
        {
            var c32 = (Color32)c;
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(S - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(S - 1, x1); x++) px[y * S + x] = c32;
        }
        void Circle(int cx, int cy, int r, Color c)
        {
            var c32 = (Color32)c;
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || y < 0 || x >= S || y >= S) continue;
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy <= r * r) px[y * S + x] = c32;
                }
        }
        void Line(int x0, int y0, int x1, int y1, int w, Color c)
        {
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)) + 1;
            for (int i = 0; i <= steps; i++)
            {
                float u = i / (float)steps;
                int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, u));
                int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, u));
                Circle(x, y, w, c);
            }
        }

        // Layout (y up): receiver mid-frame, barrel to the right, grip down-left, spool at the back.
        Rect(30, 56, 78, 82, GunMetal);                // receiver
        Rect(34, 82, 70, 86, GunMetalLt);              // top cover
        Rect(56, 86, 59, 92, Brass);                   // sight
        Rect(78, 60, 112, 76, GunMetalLt);             // barrel
        Rect(108, 57, 114, 79, Brass);                 // muzzle ring
        Rect(66, 44, 104, 54, Copper);                 // tank
        Rect(74, 42, 78, 56, Brass); Rect(94, 42, 98, 56, Brass);   // straps
        Circle(30, 66, 15, Brass);                     // spool cheek
        Circle(30, 66, 11, Rope);                      // rope wind
        Circle(30, 66, 4, Steel);                      // axle
        Line(30, 66, 22, 82, 2, Steel); Circle(22, 82, 4, Wood);    // crank
        // grip, raked back
        for (int y = 12; y <= 56; y++)
        {
            int shift = (56 - y) / 4;
            Rect(40 - shift, y, 56 - shift, y, Wood);
        }
        Rect(28, 10, 46, 14, Steel);                   // butt cap
        Line(58, 54, 62, 44, 1, Steel);                // trigger
        Line(56, 42, 70, 42, 1, GunMetal); Line(70, 42, 70, 54, 1, GunMetal);  // guard
        // the hook proud of the muzzle
        Rect(112, 66, 120, 70, Steel);                 // shaft
        Line(120, 68, 126, 78, 2, Steel); Line(126, 78, 122, 84, 2, Steel);   // upper prong + curl
        Line(120, 68, 126, 58, 2, Steel); Line(126, 58, 122, 52, 2, Steel);   // lower prong + curl

        // 1-px dark outline so it reads on any slot background.
        var outline = (Color32)new Color(0.05f, 0.05f, 0.06f, 1f);
        var src = (Color32[])px.Clone();
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                if (src[y * S + x].a != 0) continue;
                bool edge = false;
                for (int oy = -1; oy <= 1 && !edge; oy++)
                    for (int ox = -1; ox <= 1 && !edge; ox++)
                    {
                        int nx = x + ox, ny = y + oy;
                        if (nx < 0 || ny < 0 || nx >= S || ny >= S) continue;
                        if (src[ny * S + nx].a != 0) edge = true;
                    }
                if (edge) px[y * S + x] = outline;
            }

        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        tex.SetPixels32(px);
        tex.Apply(false, true);
        s_icon = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        s_icon.name = "GrappleGunIcon";
        s_icon.hideFlags = HideFlags.HideAndDontSave;
        return s_icon;
    }
}
