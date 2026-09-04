using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools ▸ Tree Gallery ▸ Build Scene — every tree-looking prefab in the
/// project, laid out on a flat field so Sam can walk the rows and pick the
/// trees Humble Abode should actually use (2026-09-04).
///
/// Layout: one row per source pack, one BAY per prefab. A bay holds the prefab
/// at three sizes side by side — regular (prefab scale), tall (stretched up),
/// and super big + tall — each standing on the ground with a small size tag in
/// front, and a sign in front of the bay with the prefab name and its pack.
/// The scene is built ADDITIVELY and then closed, so whatever scene is open in
/// the Editor is left untouched. Also writes docs/TREE_GALLERY.md (the same
/// list, with asset paths, in placement order).
///
/// Re-running overwrites the scene — it's a generated artefact, never hand-edit.
/// </summary>
public static class TreeGalleryBuilder
{
    const string SceneDir      = "Assets/4 - Scenes";
    const string ScenePath     = SceneDir + "/TreeGallery.unity";
    const string GroundMatPath = SceneDir + "/TreeGallery_Ground.mat";
    const string SignMatPath   = SceneDir + "/TreeGallery_Sign.mat";
    const string ManifestPath  = "docs/TREE_GALLERY.md";

    // Matched against the prefab's FILE NAME only (the folder "Nature & Trees"
    // would otherwise match everything). "tree" must start a word or be a
    // suffix (PalmTree_01, BananaTree) so "Streetlight" doesn't sneak in.
    static readonly Regex Include = new Regex(
        @"(^|[^a-z])tree|[a-z]tree(?=[0-9_\-\.\s]|$)|pine|oak|birch|palm|spruce|willow|cypress|maple|sapling|conifer|bamboo",
        RegexOptions.IgnoreCase);
    static readonly Regex Exclude = new Regex(
        @"stump|treehouse|tree_house|streetlight|street_light|(^|[^a-z])log|leaf|leaves|branch|root|cloak|spine|soak",
        RegexOptions.IgnoreCase);

    struct SizeDef { public string label; public Vector3 scale; }
    static readonly SizeDef[] Sizes =
    {
        new SizeDef { label = "regular",          scale = Vector3.one },
        new SizeDef { label = "tall",             scale = new Vector3(1.15f, 1.9f, 1.15f) },
        new SizeDef { label = "super big + tall", scale = new Vector3(2.6f, 3.8f, 2.6f) },
    };

    const float Gap        = 6f;    // metres between the three sizes in a bay
    const float BayGap     = 16f;   // metres between bays
    const float SignSetback = 10f;  // sign this far in FRONT (-Z) of the trees' front edge
    const float RowGap     = 40f;   // clear ground between a row's deepest tree and the next row's signs
    const float MaxRowWidth = 900f; // wrap a pack onto another row past this

    class Placed
    {
        public string name, pack, path;
        public Vector3 regularSize;
        public Vector3 signPos;
    }

    [MenuItem("Tools/Tree Gallery/Build Scene")]
    public static void Build()
    {
        var prefabs = FindTreePrefabs();
        if (prefabs.Count == 0)
        {
            Debug.LogWarning("[TreeGallery] No tree-looking prefabs found — nothing built.");
            return;
        }

        if (!AssetDatabase.IsValidFolder(SceneDir)) AssetDatabase.CreateFolder("Assets", "4 - Scenes");

        var prevActive = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var placed = new List<Placed>();
        try
        {
            Populate(scene, prefabs, placed);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                Debug.LogError("[TreeGallery] SaveScene failed for " + ScenePath);
        }
        finally
        {
            if (prevActive.IsValid()) SceneManager.SetActiveScene(prevActive);
            EditorSceneManager.CloseScene(scene, true);
        }

        WriteManifest(placed);
        AssetDatabase.Refresh();
        Debug.Log($"[TreeGallery] Built {placed.Count} prefabs × {Sizes.Length} sizes → {ScenePath}  (list: {ManifestPath}). " +
                  "Open it via Tools ▸ Tree Gallery ▸ Open Scene, press Play, fly with WASD/QE + mouse, Shift = fast, wheel = speed.");
    }

    [MenuItem("Tools/Tree Gallery/Open Scene")]
    public static void Open()
    {
        if (!File.Exists(ScenePath)) { Debug.LogWarning("[TreeGallery] Scene not built yet — run Build Scene first."); return; }
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    // ── discovery ───────────────────────────────────────────────────────────

    static List<(GameObject prefab, string path, string pack)> FindTreePrefabs()
    {
        var list = new List<(GameObject, string, string)>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string file = Path.GetFileNameWithoutExtension(path);
            if (!Include.IsMatch(file) || Exclude.IsMatch(file)) continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            if (!HasGeometry(prefab)) continue;
            list.Add((prefab, path, PackOf(path)));
        }
        list.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(a.Item3, b.Item3);
            return c != 0 ? c : EditorUtility.NaturalCompare(a.Item1.name, b.Item1.name);
        });
        return list;
    }

    static bool HasGeometry(GameObject prefab)
    {
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            if (r is MeshRenderer || r is SkinnedMeshRenderer) return true;
        return false;
    }

    /// "Assets/5 - External Imports/Nature & Trees/FantasyForest/Prefabs/..." → "Nature & Trees / FantasyForest";
    /// anything else → its first two folders.
    static string PackOf(string path)
    {
        string[] seg = path.Split('/');
        if (seg.Length > 4 && seg[1] == "5 - External Imports") return seg[2] + " / " + seg[3];
        if (seg.Length > 2) return seg[1] + " / " + seg[2];
        return seg.Length > 1 ? seg[1] : "Assets";
    }

    // ── layout ──────────────────────────────────────────────────────────────

    static void Populate(Scene scene, List<(GameObject prefab, string path, string pack)> prefabs, List<Placed> placed)
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var signMat = LoadOrCreateMaterial(SignMatPath, new Color(0.06f, 0.06f, 0.07f), 0f);
        var root = new GameObject("--- Tree Gallery ---").transform;

        float z = 0f;               // front edge (sign line) of the current row
        float x = 0f;
        float rowDepth = 0f;        // deepest tree in the current row
        float maxX = 0f;
        string currentPack = null;
        Transform rowT = null;
        int rowIndex = 0;

        for (int i = 0; i < prefabs.Count; i++)
        {
            var (prefab, path, pack) = prefabs[i];
            bool newRow = rowT == null || pack != currentPack || x > MaxRowWidth;
            if (newRow)
            {
                if (rowT != null) z += SignSetback + rowDepth + RowGap;
                x = 0f; rowDepth = 0f; rowIndex++;
                currentPack = pack;
                rowT = new GameObject($"Row {rowIndex:00} — {pack}").transform;
                rowT.SetParent(root, false);
                // Pack sign at the head of the row, off to the left.
                MakeSign(rowT, font, signMat, new Vector3(-34f, 0f, z), pack, "", 0.6f, 12f);
            }

            var bayT = new GameObject(prefab.name).transform;
            bayT.SetParent(rowT, false);
            float bayStart = x;
            float treeFrontZ = z + SignSetback;
            Vector3 regularSize = Vector3.zero;
            float bayDepth = 0f;

            for (int s = 0; s < Sizes.Length; s++)
            {
                var inst = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (inst == null) continue;
                inst.name = $"{prefab.name} ({Sizes[s].label})";
                inst.transform.SetParent(bayT, false);
                inst.transform.localScale = Vector3.Scale(prefab.transform.localScale, Sizes[s].scale);
                inst.transform.position = new Vector3(x, 0f, treeFrontZ);
                inst.transform.rotation = prefab.transform.rotation;

                if (!TryBounds(inst, out Bounds b)) { Object.DestroyImmediate(inst); continue; }
                // Stand it on the ground with its left edge at x and its front
                // face on the row's tree line.
                inst.transform.position += new Vector3(x - b.min.x, -b.min.y, treeFrontZ - b.min.z);
                TryBounds(inst, out b);
                if (s == 0) regularSize = b.size;
                bayDepth = Mathf.Max(bayDepth, b.size.z);

                MakeLabel(bayT, font, new Vector3(b.center.x, 0.02f, treeFrontZ - 2.5f),
                          $"{Sizes[s].label}  ×({Sizes[s].scale.x:0.##}, {Sizes[s].scale.y:0.##}, {Sizes[s].scale.z:0.##})",
                          0.14f, Color.white, TextAnchor.MiddleCenter, lieFlat: true);

                x = b.max.x + Gap;
            }

            float bayEnd = x - Gap;
            var signPos = new Vector3((bayStart + bayEnd) * 0.5f, 0f, z);
            MakeSign(bayT, font, signMat, signPos, prefab.name, pack, 0.35f, 8f);
            placed.Add(new Placed { name = prefab.name, pack = pack, path = path, regularSize = regularSize, signPos = signPos });

            rowDepth = Mathf.Max(rowDepth, bayDepth);
            x = bayEnd + BayGap;
            maxX = Mathf.Max(maxX, bayEnd);
        }
        float totalDepth = z + SignSetback + rowDepth;

        // Ground: one big flat plane, centred under everything.
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(root, false);
        float span = Mathf.Max(maxX, totalDepth) + 400f;
        ground.transform.position = new Vector3(maxX * 0.5f, 0f, totalDepth * 0.5f);
        ground.transform.localScale = new Vector3(span / 10f, 1f, span / 10f);   // Plane is 10 m
        ground.GetComponent<MeshRenderer>().sharedMaterial = LoadOrCreateMaterial(GroundMatPath, new Color(0.34f, 0.5f, 0.27f), 0f);

        // Light + camera come from DefaultGameObjects; just aim them.
        foreach (var l in Object.FindObjectsOfType<Light>())
            if (l.gameObject.scene == scene && l.type == LightType.Directional)
            {
                l.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
                l.shadows = LightShadows.Soft;
                l.shadowStrength = 0.75f;
                l.intensity = 1.1f;
            }
        foreach (var cam in Object.FindObjectsOfType<Camera>())
            if (cam.gameObject.scene == scene)
            {
                cam.transform.position = new Vector3(-10f, 9f, -45f);
                cam.transform.rotation = Quaternion.Euler(6f, 12f, 0f);
                cam.farClipPlane = 6000f;
                if (cam.GetComponent<TreeGalleryFlyCam>() == null) cam.gameObject.AddComponent<TreeGalleryFlyCam>();
            }

        var help = new GameObject("README (select me)");
        help.transform.SetParent(root, false);
        MakeLabel(help.transform, font, new Vector3(-34f, 6f, -20f),
                  "TREE GALLERY\nPlay → WASD / Q E fly, Shift fast, wheel = speed, Esc frees the mouse\nRows = packs, bays = prefabs: regular · tall · super big\nRebuild: Tools ▸ Tree Gallery ▸ Build Scene",
                  0.25f, Color.white, TextAnchor.LowerLeft, lieFlat: false);
    }

    static bool TryBounds(GameObject go, out Bounds b)
    {
        bool any = false; b = default;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any;
    }

    // ── signs & labels ──────────────────────────────────────────────────────

    /// Upright sign at `pos` facing -Z (the direction the camera starts looking
    /// from): big name line, smaller pack line, dark board behind for contrast.
    static void MakeSign(Transform parent, Font font, Material boardMat, Vector3 pos, string title, string subtitle, float titleSize, float minWidth)
    {
        var sign = new GameObject("Sign: " + title).transform;
        sign.SetParent(parent, false);
        sign.position = pos;

        float titleH = titleSize * 10f * 0.9f;               // ≈ world height of a fontSize-64 line at this characterSize... see MakeLabel
        float subSize = titleSize * 0.45f;
        float subH = string.IsNullOrEmpty(subtitle) ? 0f : subSize * 10f * 0.9f;
        float boardH = titleH + subH + 1.2f;
        float boardW = Mathf.Max(minWidth, Mathf.Max(title.Length * titleH * 0.6f, subtitle.Length * subH * 0.6f) + 1.5f);
        float postH = 1.6f;

        var board = GameObject.CreatePrimitive(PrimitiveType.Quad);
        board.name = "Board";
        board.transform.SetParent(sign, false);
        board.transform.localPosition = new Vector3(0f, postH + boardH * 0.5f, 0.06f);
        board.transform.localScale = new Vector3(boardW, boardH, 1f);
        board.GetComponent<MeshRenderer>().sharedMaterial = boardMat;
        Object.DestroyImmediate(board.GetComponent<Collider>());

        var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
        post.name = "Post";
        post.transform.SetParent(sign, false);
        post.transform.localPosition = new Vector3(0f, postH * 0.5f, 0.1f);
        post.transform.localScale = new Vector3(0.18f, postH, 0.18f);
        post.GetComponent<MeshRenderer>().sharedMaterial = boardMat;
        Object.DestroyImmediate(post.GetComponent<Collider>());

        float y = postH + boardH - 0.5f;
        MakeLabel(sign, font, pos + new Vector3(0f, y, 0f), title, titleSize, new Color(1f, 0.95f, 0.7f), TextAnchor.UpperCenter, lieFlat: false);
        if (!string.IsNullOrEmpty(subtitle))
            MakeLabel(sign, font, pos + new Vector3(0f, y - titleH - 0.15f, 0f), subtitle, subSize, new Color(0.75f, 0.85f, 1f), TextAnchor.UpperCenter, lieFlat: false);
    }

    /// TextMesh readable from the -Z side (or lying flat on the ground, readable
    /// from above/behind it — for the size tags at each tree's feet).
    static TextMesh MakeLabel(Transform parent, Font font, Vector3 pos, string text, float characterSize, Color color, TextAnchor anchor, bool lieFlat)
    {
        var go = new GameObject("Label: " + text.Split('\n')[0]);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        if (lieFlat) go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.font = font;
        tm.fontSize = 64;
        tm.characterSize = characterSize;
        tm.anchor = anchor;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        tm.fontStyle = FontStyle.Bold;
        if (font != null) go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        return tm;
    }

    static Material LoadOrCreateMaterial(string path, Color color, float smoothness)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.color = color;
        mat.SetFloat("_Glossiness", smoothness);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ── manifest ────────────────────────────────────────────────────────────

    static void WriteManifest(List<Placed> placed)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Tree Gallery");
        sb.AppendLine();
        sb.AppendLine($"Generated by `Tools ▸ Tree Gallery ▸ Build Scene` on {System.DateTime.Now:yyyy-MM-dd HH:mm}. Scene: `{ScenePath}`.");
        sb.AppendLine("Every tree-looking prefab in the project, in the order it stands in the scene (rows = packs, west → east). Sizes per bay: regular · tall (×1.15, ×1.9, ×1.15) · super big + tall (×2.6, ×3.8, ×2.6).");
        sb.AppendLine();
        sb.AppendLine("| # | Prefab | Pack | Regular size w×h×d (m) | Sign at (x, z) | Asset path |");
        sb.AppendLine("|---|--------|------|------------------------|----------------|------------|");
        for (int i = 0; i < placed.Count; i++)
        {
            var p = placed[i];
            sb.AppendLine($"| {i + 1} | {p.name} | {p.pack} | {p.regularSize.x:0.0}×{p.regularSize.y:0.0}×{p.regularSize.z:0.0} | ({p.signPos.x:0}, {p.signPos.z:0}) | `{p.path}` |");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath));
        File.WriteAllText(ManifestPath, sb.ToString());
    }
}
