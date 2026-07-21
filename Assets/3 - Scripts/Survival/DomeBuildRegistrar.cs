using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Injects a placeholder "Bubble Dome" entry into the build menu at runtime so
/// domes are placeable (build menu → costs wood) with no hand-authored prefab.
/// The visual is a translucent walk-through sphere carrying a BubbleDome
/// component. Swap in a real hemisphere model + an authored BuildableEntry
/// (isBubbleDome) later — this registrar then steps aside (it skips if an
/// authored dome entry already exists).
///
/// Auto-singleton with MainMenu skip — ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1 in CLAUDE.md).
/// </summary>
public class DomeBuildRegistrar : MonoBehaviour
{
    public static DomeBuildRegistrar Instance { get; private set; }

    GameObject _template;
    bool _done;
    const float DomeRadius   = 12f;   // 1.5× the original 8m
    const int   DomeWoodCost = 100;   // cost: 100 wood + 20 crystals
    const int   DomeCrystalCost = 20;

    static readonly Color MetalColor = new Color(0.20f, 0.24f, 0.30f, 1f);
    static readonly Color EmitColor  = new Color(0.40f, 0.85f, 1.00f, 1f);
    static readonly Color ShieldColor = new Color(0.40f, 0.82f, 1.00f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("DomeBuildRegistrar");
        DontDestroyOnLoad(go);
        go.AddComponent<DomeBuildRegistrar>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // _done must reset on every scene load: this singleton is DontDestroyOnLoad
    // but BuildMenuUI is a scene object — after a death reload / backrooms trip /
    // menu round-trip the fresh menu has the authored array WITHOUT the dome
    // entry, and a stale _done=true meant we never re-injected (dome missing
    // from the build menu AND saved domes failing to resolve their prefab).
    void OnEnable()  { SceneManager.sceneLoaded += OnSceneLoaded; }
    void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }
    void OnSceneLoaded(Scene s, LoadSceneMode m) { _done = false; }

    void Update()
    {
        if (_done) return;
        var menu = BuildMenuUI.Instance;
        if (menu == null) return;   // wait for the scene's build menu to exist

        // Respect an authored dome entry if one already exists.
        if (menu.buildables != null)
        {
            foreach (var be in menu.buildables)
                if (be != null && be.isBubbleDome) { _done = true; return; }
        }

        var entry = new BuildableEntry
        {
            displayName = "Bubble Dome",
            description = "A quarantined air pocket. Plant trees inside to fill it; a full dome slowly revives the whole planet's atmosphere.",
            prefab = GetTemplate(),
            isBubbleDome = true,
            addBonfireInteractionOnPlace = false,
            woodCost = DomeWoodCost,
            crystalCost = DomeCrystalCost,
            category = BuildableCategory.General,
        };

        var list = new List<BuildableEntry>();
        if (menu.buildables != null) list.AddRange(menu.buildables);
        list.Add(entry);
        menu.buildables = list.ToArray();
        _done = true;
    }

    GameObject GetTemplate()
    {
        if (_template != null) return _template;

        // Prefer the Coplay-generated-parts prefab (BubbleDome_Gen.prefab): the dark
        // machine body + spinning gyro ring + glowing core, assembled by
        // DomePrefabBaker.BakeFromParts(). Fall back to the procedural-emitter prefab
        // (BubbleDome.prefab), then to the code-built fallback below.
        var baked = Resources.Load<GameObject>("DomeFX/BubbleDome_Gen")
                 ?? Resources.Load<GameObject>("DomeFX/BubbleDome");
        if (baked != null) { _template = baked; return baked; }

        // Shared materials.
        Material metal = new Material(Shader.Find("Standard"));
        metal.color = MetalColor;
        metal.SetFloat("_Metallic", 0.85f);
        metal.SetFloat("_Glossiness", 0.6f);

        Material emit = new Material(Shader.Find("Standard"));
        emit.color = new Color(0.05f, 0.10f, 0.14f, 1f);
        emit.EnableKeyword("_EMISSION");
        emit.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        emit.SetColor("_EmissionColor", EmitColor * 1.5f);

        Shader ffShader = Shader.Find("Custom/ForceField");
        Material shield = ffShader != null ? new Material(ffShader)
                                           : GhostPlacement.MakeGhostMaterial(new Color(0.45f, 0.8f, 1f, 0.25f));
        if (ffShader != null)
        {
            shield.SetColor("_Color", ShieldColor);
            // Use the generated force-field texture if it has landed; the shader
            // falls back to a procedural pattern (white default texture) if not.
            var ffTex = Resources.Load<Texture2D>("DomeFX/forcefield");
            if (ffTex != null) { ffTex.wrapMode = TextureWrapMode.Repeat; shield.SetTexture("_MainTex", ffTex); }
        }

        // Root carries the dome logic + growth driver.
        var root = new GameObject("BubbleDome_Placeholder");
        var emitter = new GameObject("Emitter");
        emitter.transform.SetParent(root.transform, false);

        // Emitter body: use the generated device model if it has landed, else a
        // procedural base. Either way the animated spinner + core go on top.
        var genModel = Resources.Load<GameObject>("DomeFX/emitter");
        if (genModel != null)
        {
            var body = Instantiate(genModel, emitter.transform);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            FitHeight(body, 2.2f);
            StripForDisplay(body);
        }
        else
        {
            // Procedural base + stalk + three angled tube "legs".
            Prim(PrimitiveType.Cylinder, emitter.transform, new Vector3(0, 0.28f, 0),
                 new Vector3(0.9f, 0.28f, 0.9f), Quaternion.identity, metal);
            Prim(PrimitiveType.Cylinder, emitter.transform, new Vector3(0, 0.75f, 0),
                 new Vector3(0.14f, 0.45f, 0.14f), Quaternion.identity, metal);
            for (int i = 0; i < 3; i++)
            {
                float a = i * (Mathf.PI * 2f / 3f);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var leg = Prim(PrimitiveType.Cylinder, emitter.transform,
                    dir * 0.55f + Vector3.up * 0.45f,
                    new Vector3(0.09f, 0.55f, 0.09f), Quaternion.identity, metal);
                leg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (Vector3.up * 2f - dir).normalized);
            }
        }

        // Glowing central core (pulses) — always present, on top of whichever body.
        var core = Prim(PrimitiveType.Sphere, emitter.transform, new Vector3(0, 1.25f, 0),
             Vector3.one * 0.5f, Quaternion.identity, emit);

        // Spinning ring of glowing nodes (the moving parts + lights).
        var spinner = new GameObject("Spinner");
        spinner.transform.SetParent(emitter.transform, false);
        spinner.transform.localPosition = new Vector3(0, 0.9f, 0);
        var pulseList = new List<Renderer> { core.GetComponent<Renderer>() };
        const int nodes = 6;
        for (int i = 0; i < nodes; i++)
        {
            float a = i * (Mathf.PI * 2f / nodes);
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.0f;
            var node = Prim(PrimitiveType.Cube, spinner.transform, p,
                Vector3.one * 0.18f, Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), emit);
            pulseList.Add(node.GetComponent<Renderer>());
            // Thin tube from the hub out to each node.
            var tube = Prim(PrimitiveType.Cylinder, spinner.transform, p * 0.5f,
                new Vector3(0.05f, 0.5f, 0.05f), Quaternion.identity, metal);
            tube.transform.localRotation = Quaternion.FromToRotation(Vector3.up, p.normalized);
        }

        var fx = root.AddComponent<DomeEmitterFX>();
        fx.spinner = spinner.transform;
        fx.pulseRenderers = pulseList.ToArray();
        fx.emitColor = EmitColor;

        // Shield bubble — small in the template (so the placement ghost is a small
        // hint); DomeShieldGrow expands it to full on placement.
        var bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bubble.name = "Shield";
        var bcol = bubble.GetComponent<Collider>();
        if (bcol != null) Destroy(bcol);
        bubble.transform.SetParent(root.transform, false);
        bubble.transform.localScale = Vector3.one * 4f;   // ghost-preview size
        var bmr = bubble.GetComponent<MeshRenderer>();
        bmr.sharedMaterial = shield;
        bmr.shadowCastingMode = ShadowCastingMode.Off;
        bmr.receiveShadows = false;

        var grow = root.AddComponent<DomeShieldGrow>();
        grow.bubble = bubble.transform;
        grow.fullDiameter = DomeRadius * 2f;   // 16 → radius 8, matches BubbleDome

        // Deactivate BEFORE adding BubbleDome so its OnEnable doesn't register the
        // inert template as a live dome — the placement flow clones + activates it.
        root.SetActive(false);
        var dome = root.AddComponent<BubbleDome>();
        dome.SetRadius(DomeRadius);            // interior matches the visible bubble
        DontDestroyOnLoad(root);

        _template = root;
        return root;
    }

    // Scale a generated model to a target height and seat its base at the emitter
    // origin. Bounds are world-space, but the template is built at the world
    // origin during construction, so world ≈ local here.
    static void FitHeight(GameObject go, float targetHeight)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        if (b.size.y > 0.0001f) go.transform.localScale *= targetHeight / b.size.y;

        b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        Vector3 lp = go.transform.localPosition;
        lp.y -= b.min.y;                 // base to y = 0
        go.transform.localPosition = lp;
    }

    static void StripForDisplay(GameObject go)
    {
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    static GameObject Prim(PrimitiveType type, Transform parent, Vector3 localPos,
                           Vector3 localScale, Quaternion localRot, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = localScale;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go;
    }
}
