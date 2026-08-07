using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns a flat hotbar icon into a chunky 3D-looking slab, so 2D resources
/// (wood, crystal, dust, saplings, the fish bag) can sit in the world or in the
/// player's hand without reading as cardboard.
///
/// Built as kSlices copies of the icon quad spaced through the slab depth, each
/// double-sided. With alpha-cutout + ZWrite the nearest slice occludes the ones
/// behind it, so face-on you see exactly the flat icon, and at an angle the
/// stack fills in a continuous solid body — that's the thickness. It never goes
/// invisible edge-on.
///
/// Why not extrude the sprite outline: measured, these icons' "tight" sprite
/// meshes are shredded (the wood log is 1624 triangles with 1250 BOUNDARY
/// edges — hundreds of disconnected islands, not one hull). Extruding that gave
/// a swarm of little slabs you could see through to an offset back face, which
/// read as every item being doubled. Dense slices have no single big parallax
/// gap, so they can't do that.
///
/// Meshes and materials are cached per sprite/texture and shared by every user.
/// </summary>
public static class SpriteSlab
{
    public const float kThicknessFraction = 0.09f;  // slab depth as a fraction of the icon's longest edge
    public const int   kSlices            = 14;     // layers making up that depth

    static readonly Dictionary<Sprite, Mesh> s_meshCache = new Dictionary<Sprite, Mesh>();
    static readonly Dictionary<Texture, Material> s_materialCache = new Dictionary<Texture, Material>();

    /// <summary>Longest edge of the sprite in its own local units — divide the
    /// desired world size by this to get a normalising transform scale.</summary>
    public static float LongestEdge(Sprite icon)
    {
        Vector3 size = icon.bounds.size;
        return Mathf.Max(0.0001f, Mathf.Max(size.x, size.y));
    }

    public static Mesh GetMesh(Sprite icon)
    {
        if (icon == null) return null;
        if (s_meshCache.TryGetValue(icon, out var cached) && cached != null) return cached;

        Bounds b = icon.bounds;
        float x0 = b.min.x, x1 = b.max.x, y0 = b.min.y, y1 = b.max.y;

        Rect tr = icon.textureRect;
        var tex = icon.texture;
        float u0 = tr.xMin / tex.width,  u1 = tr.xMax / tex.width;
        float v0 = tr.yMin / tex.height, v1 = tr.yMax / tex.height;

        float thickness = Mathf.Max(b.size.x, b.size.y) * kThicknessFraction;
        int slices = Mathf.Max(2, kSlices);

        var verts = new List<Vector3>(slices * 4);
        var uvs   = new List<Vector2>(slices * 4);
        var tris  = new List<int>(slices * 12);

        for (int s = 0; s < slices; s++)
        {
            float z = Mathf.Lerp(-thickness * 0.5f, thickness * 0.5f, s / (float)(slices - 1));
            int o = verts.Count;
            verts.Add(new Vector3(x0, y0, z)); uvs.Add(new Vector2(u0, v0));
            verts.Add(new Vector3(x1, y0, z)); uvs.Add(new Vector2(u1, v0));
            verts.Add(new Vector3(x1, y1, z)); uvs.Add(new Vector2(u1, v1));
            verts.Add(new Vector3(x0, y1, z)); uvs.Add(new Vector2(u0, v1));

            // Double-sided — the cutout shader culls back faces and these are
            // viewed (and spun) from every angle.
            tris.Add(o); tris.Add(o + 2); tris.Add(o + 1);
            tris.Add(o); tris.Add(o + 3); tris.Add(o + 2);
            tris.Add(o); tris.Add(o + 1); tris.Add(o + 2);
            tris.Add(o); tris.Add(o + 2); tris.Add(o + 3);
        }

        var mesh = new Mesh { name = $"SpriteSlab_{icon.name}", hideFlags = HideFlags.HideAndDontSave };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        s_meshCache[icon] = mesh;
        return mesh;
    }

    public static Material GetMaterial(Texture tex)
    {
        if (tex == null) return null;
        if (s_materialCache.TryGetValue(tex, out var cached) && cached != null) return cached;

        // Wanted: unlit, alpha-CUTOUT (so it writes depth and back-face culls
        // correctly on a solid mesh), no lighting so items stay readable at
        // night. Sprites/Default is the last-ditch fallback only — it's Cull Off
        // + ZWrite Off, which would let the slab's far side draw over its near
        // side. Same Shader.Find-with-fallbacks pattern the pistol tracer uses.
        Shader shader = Shader.Find("Unlit/Transparent Cutout");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_Cutoff"))  mat.SetFloat("_Cutoff", 0.35f);
        if (mat.HasProperty("_Color"))   mat.SetColor("_Color", Color.white);
        // Transparent queue so the slab draws AFTER the [ImageEffectOpaque]
        // atmosphere pass. Sitting in the AlphaTest band (2450) put it BEFORE
        // that pass, and the atmosphere painted sky straight over anything
        // silhouetted against the sky. Still depth-TESTED, so terrain occludes
        // it normally.
        mat.renderQueue = 3000;
        s_materialCache[tex] = mat;
        return mat;
    }

    /// <summary>Builds a ready-to-parent slab GameObject for an icon.</summary>
    public static GameObject Build(Sprite icon, string name)
    {
        if (icon == null) return null;
        var go = new GameObject(name);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = GetMesh(icon);
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = GetMaterial(icon.texture);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return go;
    }
}
