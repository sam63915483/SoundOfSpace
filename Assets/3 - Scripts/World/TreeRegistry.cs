using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The species table for TREES — the sapling half of the ecosystem loop.
///
/// A tree's SPECIES KEY is its prefab name (e.g. "PineTree_03"). A name, not an
/// index, for exactly the reason <see cref="MushroomRegistry"/> gives: the key
/// is stored in hotbar slots, locker slots and save files, so reordering
/// TreeSpawner.treePrefabs in the inspector must not silently turn every saved
/// birch sapling into a pine.
///
/// Added 2026-09-16 (Sam): "no matter what kind of tree i chop down, when i
/// replant its sapling the same tree comes out every time … make it a mini
/// version of the tree that you cut down." Two things caused that, and both
/// needed a species key to fix: SaplingPlanter always resolved to
/// treePrefabs[0], and GhostPlacement called SaplingGrowth.Init(body, 0) with a
/// hardcoded index.
///
/// Everything species-shaped funnels through here so the thing on the ground,
/// the thing in the hotbar, the thing in your hand and the thing that grows back
/// are all the same tree:
///   • <see cref="PrefabFor"/>  — the source prefab (drops, held model, planting)
///   • <see cref="BuildModel"/> — a render-only instance normalised to a size
///   • <see cref="Preview"/>    — a cached RenderTexture for the hotbar slot
///
/// Prefabs are read from the scene's TreeSpawner, so Sam adds a species by
/// dragging a prefab into that one array — nothing else to wire.
///
/// ⚠️ This is a deliberate near-duplicate of MushroomRegistry, including its
/// preview rig. The two were left separate rather than merged behind a shared
/// base, because merging means editing the working mushroom path to gain
/// nothing the player can see. If a THIRD species type ever appears, merge all
/// three then — that is the point where the duplication starts costing.
/// </summary>
public static class TreeRegistry
{
    static GameObject[] _prefabs;
    static string[] _keys;
    static readonly Dictionary<string, int> _byKey = new Dictionary<string, int>();
    static readonly Dictionary<string, RenderTexture> _previews = new Dictionary<string, RenderTexture>();
    static TreeSpawner _spawner;
    static float _nextResolve;

    // Preview rig. Shares layer 31 with MushroomRegistry's rig, which is safe
    // because a stage only ever holds a model for the duration of one
    // synchronous Render() call — but it sits somewhere else in the world so the
    // two cameras can never frame each other's stage.
    const int PreviewLayer = 31;
    static Camera _previewCam;
    static Transform _previewStage;

    // ── Species table ──────────────────────────────────────────────────────

    public static int Count { get { Resolve(); return _keys != null ? _keys.Length : 0; } }

    /// True once a TreeSpawner with prefabs has been found in the scene.
    public static bool Ready => Count > 0;

    public static string KeyAt(int index)
    {
        Resolve();
        if (_keys == null || index < 0 || index >= _keys.Length) return null;
        return _keys[index];
    }

    public static int IndexOf(string key)
    {
        Resolve();
        if (string.IsNullOrEmpty(key)) return -1;
        return _byKey.TryGetValue(key, out int i) ? i : -1;
    }

    public static GameObject PrefabFor(string key)
    {
        int i = IndexOf(key);
        return (i >= 0 && _prefabs != null && i < _prefabs.Length) ? _prefabs[i] : null;
    }

    /// <summary>
    /// The key for a TreeSpawner prefab INDEX — how a felled tree names the
    /// species it drops, since SpawnedTree tracks its prefab by index.
    ///
    /// The index is into TreeSpawner.treePrefabs, which may contain nulls and
    /// duplicate names that Resolve() drops, so this reads the spawner's array
    /// directly and converts by NAME rather than assuming the two arrays line
    /// up. Getting that wrong would hand out the wrong species silently.
    /// </summary>
    public static string KeyForPrefabIndex(int index)
    {
        Resolve();
        var arr = _spawner != null ? _spawner.treePrefabs : null;
        if (arr == null || index < 0 || index >= arr.Length || arr[index] == null) return AnyKey();
        string name = arr[index].name;
        return _byKey.ContainsKey(name) ? name : AnyKey();
    }

    /// <summary>
    /// The TreeSpawner prefab INDEX for a species key — the inverse, used when
    /// planting, because SaplingGrowth and SaplingSave both store the index.
    /// Returns 0 (not -1) for an unknown key, so a save written before this
    /// feature still plants something rather than nothing.
    /// </summary>
    public static int PrefabIndexFor(string key)
    {
        Resolve();
        var arr = _spawner != null ? _spawner.treePrefabs : null;
        if (arr == null || string.IsNullOrEmpty(key)) return 0;
        for (int i = 0; i < arr.Length; i++)
            if (arr[i] != null && arr[i].name == key) return i;
        return 0;
    }

    /// A species key that definitely exists, for anything that has to hand out a
    /// sapling before the player has chopped a tree. Null if no spawner yet.
    public static string AnyKey()
    {
        Resolve();
        return (_keys != null && _keys.Length > 0) ? _keys[0] : null;
    }

    /// <summary>
    /// Player-facing name. There is no authored species table for trees the way
    /// MushroomSpecies gives mushrooms one, so the prefab name is tidied into
    /// something readable: separators become spaces and camelCase is split.
    ///
    /// Two rules earn their keep on THIS project's names, which are all shaped
    /// like "HA_FF_Tree_05":
    ///   • Short ALL-CAPS tokens are dropped. "HA" is Humble Abode and "FF" is
    ///     the art pack - pack bookkeeping, meaningless to the player.
    ///   • The trailing NUMBER IS KEPT, unlike the mushroom names. Here it is
    ///     the only thing telling one species from another: all eighteen tree
    ///     prefabs are "HA_FF_Tree_NN", so dropping it would label every sapling
    ///     in the game "Tree" and make the stacks unreadable.
    /// So "HA_FF_Tree_05" → "Tree 05".
    /// </summary>
    public static string DisplayName(string key)
    {
        if (string.IsNullOrEmpty(key)) return "Sapling";

        var sb = new System.Text.StringBuilder(key.Length + 4);
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            if (c == '_' || c == '-' || c == '.') { sb.Append(' '); continue; }
            // Split camelCase, but not runs of capitals or digit groups.
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(key[i - 1]) && key[i - 1] != ' ')
                sb.Append(' ');
            sb.Append(c);
        }

        var words = sb.ToString().Split(' ');
        var kept = new List<string>(words.Length);
        foreach (var w in words)
        {
            if (string.IsNullOrWhiteSpace(w)) continue;
            if (IsPackAbbreviation(w)) continue;
            kept.Add(w);
        }
        return kept.Count > 0 ? string.Join(" ", kept) : "Sapling";
    }

    /// A one- or two-letter ALL-CAPS token: a pack or planet abbreviation the
    /// player has no use for ("HA", "FF"). Never a whole name on its own, so a
    /// prefab called just "HA" still falls back to "Sapling" rather than "".
    static bool IsPackAbbreviation(string w)
    {
        if (w.Length > 2) return false;
        foreach (char c in w) if (!char.IsUpper(c)) return false;
        return true;
    }

    static void Resolve()
    {
        if (_keys != null && _keys.Length > 0 && _spawner != null) return;
        // Throttled re-search — the spawner may not exist yet (or at all, off the
        // solar-system scene). Never per-frame FindObjectOfType (CLAUDE.md).
        if (Time.time < _nextResolve && _keys != null) return;
        _nextResolve = Time.time + 2f;

        if (_spawner == null) _spawner = TreeSpawner.Instance != null
            ? TreeSpawner.Instance
            : Object.FindObjectOfType<TreeSpawner>();
        var prefabs = _spawner != null ? _spawner.treePrefabs : null;
        if (prefabs == null || prefabs.Length == 0) return;

        var keys = new List<string>(prefabs.Length);
        var kept = new List<GameObject>(prefabs.Length);
        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i] == null) continue;
            string k = prefabs[i].name;
            if (keys.Contains(k)) continue;   // duplicate prefab name — first wins
            keys.Add(k);
            kept.Add(prefabs[i]);
        }
        _keys = keys.ToArray();
        _prefabs = kept.ToArray();
        _byKey.Clear();
        for (int i = 0; i < _keys.Length; i++) _byKey[_keys[i]] = i;
    }

    // ── Models ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A render-only clone of the species prefab, stripped of colliders /
    /// rigidbodies / behaviours and scaled so its longest edge is
    /// <paramref name="worldSize"/> metres — a MINI VERSION of the tree, which
    /// is what makes a sapling on the ground readable as the tree it came off.
    /// Returns null for an unknown species.
    ///
    /// Stripping MonoBehaviours matters more here than it does for mushrooms: a
    /// tree prefab can carry SpawnedTree and an LOD handler, and a live
    /// SpawnedTree on a dropped sapling would register itself as a choppable
    /// tree lying on the floor.
    /// </summary>
    public static GameObject BuildModel(string key, string name, float worldSize)
    {
        var prefab = PrefabFor(key);
        if (prefab == null) return null;

        var go = Object.Instantiate(prefab);
        go.name = name;
        if (!go.activeSelf) go.SetActive(true);

        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(rb);
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.Destroy(mb);
        foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) Object.Destroy(lg);

        float longest = MushroomRegistry.LongestLocalEdge(go);
        if (longest > 0.0001f && worldSize > 0f)
            go.transform.localScale = Vector3.one * (worldSize / longest);
        return go;
    }

    // ── Hotbar preview ─────────────────────────────────────────────────────

    /// Cached 3D render of the species, for the hotbar slot — "which sapling is
    /// this". One render per species per session.
    public static RenderTexture Preview(string key, int size = 96)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (_previews.TryGetValue(key, out var cached) && cached != null) return cached;

        var prefab = PrefabFor(key);
        if (prefab == null) return null;
        EnsurePreviewRig();
        if (_previewCam == null || _previewStage == null) return null;

        var model = Object.Instantiate(prefab, _previewStage.position, Quaternion.Euler(0f, 25f, 0f));
        if (!model.activeSelf) model.SetActive(true);
        foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        foreach (var mb in model.GetComponentsInChildren<MonoBehaviour>(true)) Object.Destroy(mb);
        foreach (var lg in model.GetComponentsInChildren<LODGroup>(true)) Object.Destroy(lg);
        SetLayerRecursive(model, PreviewLayer);

        // Frame it: normalise to 1 unit tall, camera sits 2.2 units back.
        float longest = MushroomRegistry.LongestLocalEdge(model);
        if (longest > 0.0001f) model.transform.localScale = Vector3.one / longest;

        var rt = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32);
        rt.Create();
        _previewCam.targetTexture = rt;
        _previewCam.Render();
        _previewCam.targetTexture = null;
        Object.DestroyImmediate(model);

        _previews[key] = rt;
        return rt;
    }

    static void EnsurePreviewRig()
    {
        if (_previewCam != null) return;

        var root = new GameObject("TreePreviewRig");
        Object.DontDestroyOnLoad(root);
        // 1 km below the mushroom rig — see the PreviewLayer note above.
        root.transform.position = new Vector3(0f, 59000f, 0f);

        var stage = new GameObject("Stage");
        stage.transform.SetParent(root.transform, false);
        _previewStage = stage.transform;

        var camGO = new GameObject("PreviewCam");
        camGO.transform.SetParent(root.transform, false);
        // A tree is tall and thin where a mushroom is squat, so the camera looks
        // at the middle of a 1-unit-tall model rather than near its base.
        camGO.transform.localPosition = new Vector3(0f, 0.5f, -2.2f);
        camGO.transform.LookAt(root.transform.position + new Vector3(0f, 0.45f, 0f));
        _previewCam = camGO.AddComponent<Camera>();
        _previewCam.clearFlags = CameraClearFlags.SolidColor;
        _previewCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _previewCam.cullingMask = 1 << PreviewLayer;
        _previewCam.orthographic = false;
        _previewCam.fieldOfView = 32f;
        _previewCam.nearClipPlane = 0.05f;
        _previewCam.farClipPlane = 12f;
        _previewCam.allowHDR = false;
        _previewCam.allowMSAA = false;
        _previewCam.enabled = false;   // rendered on demand only

        AddPreviewLight(root.transform, new Vector3(-1.4f, 1.6f, -1.6f), 1.5f);
        AddPreviewLight(root.transform, new Vector3(1.6f, 0.6f, -1.2f), 0.8f);
    }

    static void AddPreviewLight(Transform parent, Vector3 localPos, float intensity)
    {
        var go = new GameObject("PreviewLight");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.layer = PreviewLayer;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.intensity = intensity;
        l.range = 12f;
        l.cullingMask = 1 << PreviewLayer;
        l.shadows = LightShadows.None;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
    }
}
