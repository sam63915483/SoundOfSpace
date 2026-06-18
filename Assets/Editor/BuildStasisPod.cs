using UnityEngine;
using UnityEditor;

// Builds the StasisPod prefab from Unity primitives (no Blender, no asset-gen).
// Re-runnable: Tools ▸ Intro ▸ Build Stasis Pod (or Coplay execute_script -> Execute).
// Octagonal body = 4 gunmetal panels alternating with 4 glass windows; tapered
// collars top & bottom each capped by a glass window (ceiling = space, floor =
// planet). Dark-industrial: gunmetal hull, amber emissive trim, smoky glass.
public static class BuildStasisPod
{
    const string PrefabPath   = "Assets/1 - samsPrefabs/StasisPod.prefab";
    const string MatDir       = "Assets/2 - Materials/Intro";
    const string HullMatPath  = MatDir + "/PodHull.mat";
    const string GlassMatPath = MatDir + "/PodGlass.mat";
    const string TrimMatPath  = MatDir + "/PodTrim.mat";

    // Geometry (metres). Octagon circumradius R; the player camera sits near centre.
    const float R             = 1.35f;  // interior radius
    const float bodyHeight    = 1.6f;   // height of the side-window band
    const float wallThickness = 0.06f;
    const float capRise       = 0.7f;   // how far the cap window sits beyond the body band
    const float capRadius     = 0.55f;  // radius of the floor/ceiling glass cap
    const float trimThickness = 0.035f;

    [MenuItem("Tools/Intro/Build Stasis Pod")]
    public static void Execute()
    {
        EnsureFolder(MatDir);
        Material hull  = MakeHullMat();
        Material glass = MakeGlassMat();
        Material trim  = MakeTrimMat();

        var root = new GameObject("StasisPod");
        var hullRoot = new GameObject("Hull");
        hullRoot.transform.SetParent(root.transform, false);

        float edge = 2f * R * Mathf.Tan(Mathf.Deg2Rad * 22.5f); // octagon edge length

        // Body: 8 faces. Even index = glass window (0/90/180/270), odd = gunmetal strut.
        for (int i = 0; i < 8; i++)
        {
            float ang = i * 45f;
            Vector3 outDir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(outDir, Vector3.up);
            if (i % 2 == 0)
            {
                var q = MakeQuad("Body_Window_" + i, hullRoot.transform, glass);
                q.transform.localPosition = outDir * R;
                q.transform.localRotation = rot;
                q.transform.localScale = new Vector3(edge * 0.92f, bodyHeight, 1f);
            }
            else
            {
                var b = MakeBox("Body_Panel_" + i, hullRoot.transform, hull);
                b.transform.localPosition = outDir * R;
                b.transform.localRotation = rot;
                b.transform.localScale = new Vector3(edge, bodyHeight, wallThickness);
            }
        }

        // Emissive trim ribs at the 8 octagon corners (frame each window).
        for (int c = 0; c < 8; c++)
        {
            float ang = c * 45f + 22.5f;
            Vector3 cornerDir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            var rib = MakeBox("Trim_Rib_" + c, hullRoot.transform, trim);
            rib.transform.localPosition = cornerDir * (R + trimThickness * 0.5f);
            rib.transform.localRotation = Quaternion.LookRotation(cornerDir, Vector3.up);
            rib.transform.localScale = new Vector3(trimThickness, bodyHeight * 1.02f, trimThickness);
        }

        BuildCap(hullRoot.transform, hull, glass, +1);  // ceiling (space)
        BuildCap(hullRoot.transform, hull, glass, -1);  // floor (planet)

        var lightGO = new GameObject("InteriorLight");
        lightGO.transform.SetParent(root.transform, false);
        var lt = lightGO.AddComponent<Light>();
        lt.type = LightType.Point;
        lt.range = 3f;
        lt.intensity = 1.2f;
        lt.color = new Color(1f, 0.55f, 0.25f); // dim amber

        foreach (var col in root.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(col); // cosmetic only

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        if (prefab != null) { Selection.activeObject = prefab; EditorGUIUtility.PingObject(prefab); }
        Debug.Log("[StasisPod] Built prefab at " + PrefabPath);
    }

    // sign +1 = top (ceiling/space), -1 = bottom (floor/planet).
    static void BuildCap(Transform parent, Material hull, Material glass, int sign)
    {
        float yBodyEdge = sign * bodyHeight * 0.5f;
        float yCap      = sign * (bodyHeight * 0.5f + capRise);

        // Glass cap (horizontal pane).
        var cap = MakeQuad(sign > 0 ? "TopWindow" : "BottomWindow", parent, glass);
        cap.transform.localPosition = new Vector3(0f, yCap, 0f);
        cap.transform.localRotation = Quaternion.LookRotation(Vector3.up * sign, Vector3.forward);
        cap.transform.localScale = new Vector3(capRadius * 2f, capRadius * 2f, 1f);

        // 8 opaque collar panels bridging the body octagon edge up/down to the cap.
        float edge = 2f * R * Mathf.Tan(Mathf.Deg2Rad * 22.5f);
        for (int i = 0; i < 8; i++)
        {
            float ang = i * 45f;
            Vector3 outDir  = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            Vector3 tangent = Quaternion.Euler(0f, ang, 0f) * Vector3.right;
            Vector3 bodyPt = outDir * R + Vector3.up * yBodyEdge;
            Vector3 capPt  = outDir * capRadius + Vector3.up * yCap;
            Vector3 slant  = capPt - bodyPt;
            float L = slant.magnitude;
            Vector3 slantDir = slant / L;
            Vector3 normal = Vector3.Cross(slantDir, tangent).normalized;

            var b = MakeBox("Collar_" + (sign > 0 ? "T_" : "B_") + i, parent, hull);
            b.transform.localPosition = (bodyPt + capPt) * 0.5f;
            b.transform.localRotation = Quaternion.LookRotation(normal, slantDir);
            b.transform.localScale = new Vector3(edge, L, wallThickness);
        }
    }

    // ── Primitive + material helpers ─────────────────────────────────────────
    static GameObject MakeBox(string name, Transform parent, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static GameObject MakeQuad(string name, Transform parent, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Material MakeHullMat()
    {
        var m = LoadOrCreate(HullMatPath, () => new Material(Shader.Find("Standard")));
        m.color = new Color(0.18f, 0.19f, 0.21f);
        m.SetFloat("_Metallic", 0.7f);
        m.SetFloat("_Glossiness", 0.5f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material MakeGlassMat()
    {
        var sh = Shader.Find("Custom/StasisPodGlassDoubleSided");
        var m = LoadOrCreate(GlassMatPath, () => new Material(sh));
        if (sh != null && m.shader != sh) m.shader = sh;
        m.SetColor("_Color", new Color(0.10f, 0.12f, 0.15f, 0.35f));
        m.SetFloat("_Glossiness", 0.6f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material MakeTrimMat()
    {
        var m = LoadOrCreate(TrimMatPath, () => new Material(Shader.Find("Standard")));
        m.color = new Color(0.05f, 0.04f, 0.03f);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.SetColor("_EmissionColor", new Color(1.0f, 0.4f, 0.1f) * 2.0f); // amber glow
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material LoadOrCreate(string path, System.Func<Material> create)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var m = create();
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        var leaf = System.IO.Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
