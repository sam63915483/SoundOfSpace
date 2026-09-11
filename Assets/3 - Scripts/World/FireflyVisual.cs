using UnityEngine;

/// <summary>
/// The one place a firefly's LOOK is defined — the wild bug in a swarm, the one
/// in your hand, and the one a co-op partner sees you holding all come from
/// <see cref="Build"/>, so they can never drift apart.
///
/// Procedural on purpose (no art pack): a bobber-sized ellipsoid, head at +Z,
/// two flat wing quads over the back, and a billboard halo quad. Two materials,
/// one shader (<c>SoundOfSpace/Firefly</c>) — see that file for what the blink
/// and fade properties mean. Materials are REAL assets in Resources/ so the
/// instanced shader variant survives a build (SpaceDustField / AmbientFish
/// precedent: a runtime <c>new Material(Shader.Find(...))</c> draws nothing in
/// the player).
/// </summary>
public static class FireflyVisual
{
    /// Yellow-orange. The shader and the point lights both use this.
    public static readonly Color GlowColor = new Color(1f, 0.68f, 0.22f, 1f);

    /// Head-to-tail length in metres. "A decent bobber sized bug" — the bobber
    /// is a 0.2 m sphere.
    public const float BodyLength = 0.20f;
    const float BodyRadius = 0.055f;

    public static readonly int BlinkId = Shader.PropertyToID("_Blink");
    public static readonly int FadeId  = Shader.PropertyToID("_Fade");

    static Mesh _bodyMesh, _haloMesh;
    static Material _bodyMat, _haloMat;
    static bool _matsTried;

    // ── the blink, mirrored from the shader ─────────────────────────────

    /// <summary>
    /// 0..1 glow level at <paramref name="time"/> (pass
    /// <c>Time.timeSinceLevelLoad</c> — that is what the shader's <c>_Time.y</c>
    /// is). MUST stay identical to <c>BlinkLevel</c> in Firefly.shader: the real
    /// point light on the nearest bugs is driven by this while their glow is
    /// drawn by that, and the two have to pulse together.
    /// </summary>
    public static float Blink(float time, float phase, float period, float floor, float gaze)
    {
        float t = time / Mathf.Max(0.2f, period) + phase;
        float s = 0.5f + 0.5f * Mathf.Sin(t * 6.2831853f);
        float pulse = s * s;
        return Mathf.Max(Mathf.Lerp(floor, 1f, pulse), gaze);
    }

    // ── building ────────────────────────────────────────────────────────

    /// <summary>
    /// A bug: root object (unit scale, +Z forward) with a "Body" and a "Halo"
    /// child renderer. <paramref name="haloSize"/> is the halo quad's edge in
    /// metres — 0.5 m for a wild bug so it twinkles from afar, much smaller in
    /// the hand where it would otherwise fill the screen. Returns null only if
    /// the materials could not be resolved at all.
    /// </summary>
    public static GameObject Build(string name, float haloSize,
                                   out MeshRenderer body, out MeshRenderer halo)
    {
        body = null; halo = null;
        if (!EnsureMaterials()) return null;

        var root = new GameObject(name);

        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(root.transform, false);
        bodyGo.AddComponent<MeshFilter>().sharedMesh = BodyMesh();
        body = bodyGo.AddComponent<MeshRenderer>();
        Configure(body, _bodyMat);

        var haloGo = new GameObject("Halo");
        haloGo.transform.SetParent(root.transform, false);
        haloGo.transform.localScale = Vector3.one * Mathf.Max(0.01f, haloSize);
        haloGo.AddComponent<MeshFilter>().sharedMesh = HaloMesh();
        halo = haloGo.AddComponent<MeshRenderer>();
        Configure(halo, _haloMat);

        return root;
    }

    static void Configure(MeshRenderer r, Material m)
    {
        r.sharedMaterial = m;
        // No shadows either way: the shader has no ShadowCaster pass, but the
        // flags keep the renderer out of the shadow pass entirely.
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    /// Write the per-instance blink + fade into both renderers through one
    /// shared property block. Nothing is allocated per call.
    public static void Apply(MeshRenderer body, MeshRenderer halo, MaterialPropertyBlock block,
                             float phase, float period, float floor, float gaze, float fade)
    {
        if (block == null) return;
        block.SetVector(BlinkId, new Vector4(phase, period, floor, gaze));
        block.SetFloat(FadeId, fade);
        if (body != null) body.SetPropertyBlock(block);
        if (halo != null) halo.SetPropertyBlock(block);
    }

    // ── materials ───────────────────────────────────────────────────────

    static bool EnsureMaterials()
    {
        if (_bodyMat != null && _haloMat != null) return true;
        if (_matsTried && _bodyMat == null && _haloMat == null) return false;
        _matsTried = true;

        _bodyMat = Resources.Load<Material>("FireflyBody");
        _haloMat = Resources.Load<Material>("FireflyHalo");
        if (_bodyMat != null && _haloMat != null) return true;

        // Editor-only safety net. A runtime material loses its INSTANCING_ON
        // variant in a build and draws nothing there, so this exists to keep
        // the Editor working while someone restores the assets, not to ship.
        var sh = Shader.Find("SoundOfSpace/Firefly");
        if (sh == null)
        {
            Debug.LogWarning("[Firefly] Neither Resources/FireflyBody.mat + FireflyHalo.mat nor the "
                           + "SoundOfSpace/Firefly shader could be found — no fireflies will draw.");
            return false;
        }
        Debug.LogWarning("[Firefly] Resources/FireflyBody.mat or FireflyHalo.mat missing — using a "
                       + "runtime material, which will draw NOTHING in a build. Restore the assets.");
        if (_bodyMat == null)
        {
            _bodyMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            _bodyMat.SetFloat("_Halo", 0f);
            _bodyMat.SetFloat("_SrcBlend", 1f); _bodyMat.SetFloat("_DstBlend", 0f); _bodyMat.SetFloat("_ZWrite", 1f);
            _bodyMat.renderQueue = 2000;
        }
        if (_haloMat == null)
        {
            _haloMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            _haloMat.SetFloat("_Halo", 1f);
            _haloMat.SetFloat("_SrcBlend", 1f); _haloMat.SetFloat("_DstBlend", 1f); _haloMat.SetFloat("_ZWrite", 0f);
            _haloMat.renderQueue = 3000;
        }
        return true;
    }

    // ── meshes ──────────────────────────────────────────────────────────

    /// Ellipsoid body, head at +Z, tail at -Z, plus two wing quads over the
    /// back. uv.x runs 0 at the head to 1 at the tail (the shader's glow
    /// gradient); wing verts carry uv.y = 2 as a flag.
    static Mesh BodyMesh()
    {
        if (_bodyMesh != null) return _bodyMesh;

        const int rings = 8, segs = 12;
        int sphereVerts = (rings + 1) * (segs + 1);
        var verts = new Vector3[sphereVerts + 8];
        var uvs   = new Vector2[sphereVerts + 8];
        var tris  = new System.Collections.Generic.List<int>(rings * segs * 6 + 12);

        float halfLen = BodyLength * 0.5f;
        for (int r = 0; r <= rings; r++)
        {
            // theta 0 = head (+Z) .. pi = tail (-Z)
            float theta = Mathf.PI * r / rings;
            float z = Mathf.Cos(theta);
            float ring = Mathf.Sin(theta);
            // Slight taper toward the tail so it reads as a bug, not an egg.
            float radius = BodyRadius * Mathf.Lerp(1f, 0.82f, r / (float)rings);
            for (int s = 0; s <= segs; s++)
            {
                float phi = 2f * Mathf.PI * s / segs;
                int i = r * (segs + 1) + s;
                verts[i] = new Vector3(Mathf.Cos(phi) * ring * radius,
                                       Mathf.Sin(phi) * ring * radius,
                                       z * halfLen);
                uvs[i] = new Vector2(r / (float)rings, 0f);
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segs; s++)
            {
                int a = r * (segs + 1) + s;
                int b = a + segs + 1;
                tris.Add(a); tris.Add(a + 1); tris.Add(b);
                tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
            }

        // Wings: two flat quads from the mid-back, swept out and back like a
        // beetle's opened cases. Cull is off in the shader, so one side each.
        int w = sphereVerts;
        Vector3[] right =
        {
            new Vector3(0.010f, 0.045f,  0.030f),
            new Vector3(0.080f, 0.078f, -0.015f),
            new Vector3(0.062f, 0.070f, -0.105f),
            new Vector3(0.006f, 0.050f, -0.070f),
        };
        for (int k = 0; k < 4; k++)
        {
            verts[w + k]     = right[k];
            verts[w + 4 + k] = new Vector3(-right[k].x, right[k].y, right[k].z);
            uvs[w + k] = uvs[w + 4 + k] = new Vector2(0.5f, 2f);
        }
        tris.Add(w + 0); tris.Add(w + 1); tris.Add(w + 2);
        tris.Add(w + 0); tris.Add(w + 2); tris.Add(w + 3);
        tris.Add(w + 4); tris.Add(w + 6); tris.Add(w + 5);
        tris.Add(w + 4); tris.Add(w + 7); tris.Add(w + 6);

        _bodyMesh = new Mesh { name = "FireflyBody" };
        _bodyMesh.vertices = verts;
        _bodyMesh.uv = uvs;
        _bodyMesh.SetTriangles(tris, 0);
        _bodyMesh.RecalculateNormals();
        _bodyMesh.RecalculateBounds();
        _bodyMesh.hideFlags = HideFlags.HideAndDontSave;
        return _bodyMesh;
    }

    /// Unit quad in XY, uv 0..1. The shader billboards it and sizes it from
    /// the halo object's scale.
    static Mesh HaloMesh()
    {
        if (_haloMesh != null) return _haloMesh;
        _haloMesh = new Mesh { name = "FireflyHalo" };
        _haloMesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),   new Vector3(-0.5f, 0.5f, 0f),
        };
        _haloMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        _haloMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        _haloMesh.RecalculateNormals();
        // The quad is billboarded in the vertex shader, so the mesh bounds must
        // cover every orientation or the renderer gets culled edge-on.
        _haloMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1.2f);
        _haloMesh.hideFlags = HideFlags.HideAndDontSave;
        return _haloMesh;
    }
}
