using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The species table for the mushroom economy.
///
/// A mushroom's SPECIES KEY is its prefab name (e.g. "mushroom_red_big"). A name
/// — not an index — because the key is stored in hotbar slots, locker slots and
/// save files: reordering MushroomSpawner.mushroomPrefabs in the inspector must
/// not silently turn every saved red cap into a blue one.
///
/// Everything species-shaped funnels through here so the ground drop, the hotbar
/// icon and the model in your hand are guaranteed to be the same mushroom you
/// chopped (the same "it looks like the thing you caught" rule the fish use):
///   • <see cref="PrefabFor"/>  — the source prefab (drops, held model, planting)
///   • <see cref="BuildModel"/> — a render-only instance normalised to a size
///   • <see cref="Preview"/>    — a cached RenderTexture for the hotbar slot
///
/// Prefabs are read from the scene's MushroomSpawner, so Sam adds a species by
/// dragging a prefab into that one array — nothing else to wire.
/// </summary>
public static class MushroomRegistry
{
    static GameObject[] _prefabs;
    static string[] _keys;
    static readonly Dictionary<string, int> _byKey = new Dictionary<string, int>();
    static readonly Dictionary<string, RenderTexture> _previews = new Dictionary<string, RenderTexture>();
    static MushroomSpawner _spawner;
    static float _nextResolve;

    // Preview rig (built once, on demand). Layer 31 is empty in this project and
    // is only ever occupied for the single Render() call below, so it can't
    // collide with the Fishingdex's own preview rig.
    const int PreviewLayer = 31;
    static Camera _previewCam;
    static Transform _previewStage;

    // ── Species table ──────────────────────────────────────────────────────

    public static int Count { get { Resolve(); return _keys != null ? _keys.Length : 0; } }

    /// True once a MushroomSpawner with prefabs has been found in the scene.
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

    /// A species key that definitely exists — used when something has to hand
    /// out a mushroom before the player has chopped one (Tev's gift). Returns
    /// null if no spawner/prefabs exist yet.
    public static string AnyKey()
    {
        Resolve();
        return (_keys != null && _keys.Length > 0) ? _keys[0] : null;
    }

    /// A deterministic species pick, so "3 mushrooms of one species" is the same
    /// species every time for the same seed rather than a fresh roll per item.
    public static string KeyForSeed(int seed)
    {
        Resolve();
        if (_keys == null || _keys.Length == 0) return null;
        int i = Mathf.Abs(seed) % _keys.Length;
        return _keys[i];
    }

    /// The species' STREET NAME ("Amanita_big" → "Fly Agaric"), from
    /// <see cref="MushroomSpecies"/>. Species with no authored row fall back to
    /// the old prettifier, so an unlisted prefab still reads sensibly.
    public static string DisplayName(string key)
    {
        if (string.IsNullOrEmpty(key)) return "mushroom";
        return MushroomSpecies.DisplayName(key);
    }

    /// Rarity tier — drives slot corner colour and the sell panel's label.
    public static MushroomTier Tier(string key) => MushroomSpecies.Tier(key);

    /// Market value per cap, before any buyer's multiplier. A property of the
    /// STRAIN, identical for every buyer, so it's safe to show the player.
    public static int BaseValue(string key) => MushroomSpecies.BaseValue(key);

    static void Resolve()
    {
        if (_keys != null && _keys.Length > 0 && _spawner != null) return;
        // Throttled re-search — the spawner may not exist yet (or at all, off the
        // solar-system scene). Never per-frame FindObjectOfType (CLAUDE.md).
        if (Time.time < _nextResolve && _keys != null) return;
        _nextResolve = Time.time + 2f;

        if (_spawner == null) _spawner = Object.FindObjectOfType<MushroomSpawner>();
        var prefabs = _spawner != null ? _spawner.mushroomPrefabs : null;
        if (prefabs == null || prefabs.Length == 0) return;

        var keys = new List<string>(prefabs.Length);
        var kept = new List<GameObject>(prefabs.Length);
        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i] == null) continue;
            string k = prefabs[i].name;
            if (_byKeyContains(keys, k)) continue;   // duplicate prefab name — first wins
            keys.Add(k);
            kept.Add(prefabs[i]);
        }
        _keys = keys.ToArray();
        _prefabs = kept.ToArray();
        _byKey.Clear();
        for (int i = 0; i < _keys.Length; i++) _byKey[_keys[i]] = i;
    }

    static bool _byKeyContains(List<string> list, string k)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == k) return true;
        return false;
    }

    // ── Models ─────────────────────────────────────────────────────────────

    /// A render-only clone of the species prefab, stripped of colliders /
    /// rigidbodies / behaviours and scaled so its longest edge is
    /// <paramref name="worldSize"/> metres. Used for the ground drop and the
    /// model in the player's hand. Returns null for an unknown species.
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

        float longest = LongestLocalEdge(go);
        if (longest > 0.0001f && worldSize > 0f)
            go.transform.localScale = Vector3.one * (worldSize / longest);
        return go;
    }

    /// Longest edge of the combined renderer bounds, measured in the root's own
    /// local frame so the result is independent of the instance's scale.
    public static float LongestLocalEdge(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return 0f;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        Vector3 s = b.size;
        float scale = Mathf.Max(0.0001f, go.transform.lossyScale.x);
        return Mathf.Max(s.x, Mathf.Max(s.y, s.z)) / scale;
    }

    // ── Hotbar preview ─────────────────────────────────────────────────────

    /// Cached 3D render of the species, for the hotbar slot / any UI that wants
    /// to show "which mushroom is this". One render per species per session.
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
        SetLayerRecursive(model, PreviewLayer);

        // Frame it: normalise to 1 unit tall, camera sits 2.2 units back.
        float longest = LongestLocalEdge(model);
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

        var root = new GameObject("MushroomPreviewRig");
        Object.DontDestroyOnLoad(root);
        root.transform.position = new Vector3(0f, 60000f, 0f);   // far from any world geometry

        var stage = new GameObject("Stage");
        stage.transform.SetParent(root.transform, false);
        _previewStage = stage.transform;

        var camGO = new GameObject("PreviewCam");
        camGO.transform.SetParent(root.transform, false);
        camGO.transform.localPosition = new Vector3(0f, 0.35f, -2.2f);
        camGO.transform.LookAt(root.transform.position + new Vector3(0f, 0.2f, 0f));
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
