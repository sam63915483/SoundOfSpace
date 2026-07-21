using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One-time editor tool: bakes the procedural bubble-dome into a real, inspectable
/// prefab (Assets/Resources/DomeFX/BubbleDome.prefab) with saved material assets +
/// the hex force-field shield at full size. Run via Coplay execute_script. The
/// runtime registrar loads this prefab if present (procedural build is fallback).
/// </summary>
public static class DomePrefabBaker
{
    const float Radius = 12f;
    const string Dir = "Assets/Resources/DomeFX";
    static readonly Color MetalColor  = new Color(0.20f, 0.24f, 0.30f, 1f);
    static readonly Color EmitColor   = new Color(0.40f, 0.85f, 1.00f, 1f);
    static readonly Color ShieldColor = new Color(0.40f, 0.82f, 1.00f, 1f);

    public static void Execute()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Resources", "DomeFX");

        // ── Materials as assets ──
        var metal = new Material(Shader.Find("Standard"));
        metal.color = MetalColor; metal.SetFloat("_Metallic", 0.85f); metal.SetFloat("_Glossiness", 0.6f);
        SaveMat(metal, Dir + "/DomeMetal.mat");

        var emit = new Material(Shader.Find("Standard"));
        emit.color = new Color(0.05f, 0.10f, 0.14f, 1f);
        emit.EnableKeyword("_EMISSION");
        emit.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        emit.SetColor("_EmissionColor", EmitColor * 1.5f);
        SaveMat(emit, Dir + "/DomeEmit.mat");

        var ffShader = Shader.Find("Custom/ForceField");
        var shield = new Material(ffShader != null ? ffShader : Shader.Find("Standard"));
        if (ffShader != null)
        {
            shield.SetColor("_Color", ShieldColor);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/forcefield.png");
            if (tex != null) shield.SetTexture("_MainTex", tex);
        }
        SaveMat(shield, Dir + "/DomeShield.mat");

        metal  = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/DomeMetal.mat");
        emit   = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/DomeEmit.mat");
        shield = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/DomeShield.mat");

        // ── Hierarchy ──
        var root = new GameObject("BubbleDome");
        var emitter = new GameObject("Emitter");
        emitter.transform.SetParent(root.transform, false);

        Prim(PrimitiveType.Cylinder, emitter.transform, new Vector3(0, 0.28f, 0), new Vector3(0.9f, 0.28f, 0.9f), Quaternion.identity, metal);
        Prim(PrimitiveType.Cylinder, emitter.transform, new Vector3(0, 0.75f, 0), new Vector3(0.14f, 0.45f, 0.14f), Quaternion.identity, metal);
        for (int i = 0; i < 3; i++)
        {
            float a = i * (Mathf.PI * 2f / 3f);
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var leg = Prim(PrimitiveType.Cylinder, emitter.transform, dir * 0.55f + Vector3.up * 0.45f, new Vector3(0.09f, 0.55f, 0.09f), Quaternion.identity, metal);
            leg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (Vector3.up * 2f - dir).normalized);
        }

        var core = Prim(PrimitiveType.Sphere, emitter.transform, new Vector3(0, 1.25f, 0), Vector3.one * 0.5f, Quaternion.identity, emit);

        var spinner = new GameObject("Spinner");
        spinner.transform.SetParent(emitter.transform, false);
        spinner.transform.localPosition = new Vector3(0, 0.9f, 0);
        var pulse = new List<Renderer> { core.GetComponent<Renderer>() };
        const int nodes = 6;
        for (int i = 0; i < nodes; i++)
        {
            float a = i * (Mathf.PI * 2f / nodes);
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.0f;
            var node = Prim(PrimitiveType.Cube, spinner.transform, p, Vector3.one * 0.18f, Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), emit);
            pulse.Add(node.GetComponent<Renderer>());
            var tube = Prim(PrimitiveType.Cylinder, spinner.transform, p * 0.5f, new Vector3(0.05f, 0.5f, 0.05f), Quaternion.identity, metal);
            tube.transform.localRotation = Quaternion.FromToRotation(Vector3.up, p.normalized);
        }

        var fx = root.AddComponent<DomeEmitterFX>();
        fx.spinner = spinner.transform; fx.pulseRenderers = pulse.ToArray(); fx.emitColor = EmitColor;

        // Shield bubble — FULL size in the prefab so it's visible when inspecting.
        // DomeShieldGrow shrinks it to 0 and re-grows it only at runtime (OnEnable).
        var bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bubble.name = "Shield";
        var bcol = bubble.GetComponent<Collider>(); if (bcol != null) Object.DestroyImmediate(bcol);
        bubble.transform.SetParent(root.transform, false);
        bubble.transform.localScale = Vector3.one * (Radius * 2f);
        var bmr = bubble.GetComponent<MeshRenderer>();
        bmr.sharedMaterial = shield; bmr.shadowCastingMode = ShadowCastingMode.Off; bmr.receiveShadows = false;

        var grow = root.AddComponent<DomeShieldGrow>();
        grow.bubble = bubble.transform; grow.fullDiameter = Radius * 2f;

        var dome = root.AddComponent<BubbleDome>();
        dome.SetRadius(Radius);

        var path = Dir + "/BubbleDome.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[DomePrefabBaker] Saved prefab + materials to " + Dir);
    }

    /// Assembles the Coplay-generated parts (part_body / part_ring / part_core)
    /// into an ANIMATED generator (body static, ring + core spin, glow pulses) and
    /// saves it as BubbleDome_Gen.prefab — separate from the working code prefab so
    /// nothing is clobbered until it's approved. Run once the .glb parts have landed.
    public static void BakeFromParts()
    {
        AssetDatabase.Refresh();   // import any freshly-landed .glb parts
        var bodyP = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/part_body.glb");
        var ringP = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/part_ring.glb");
        var coreP = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/part_core.glb");
        if (bodyP == null && ringP == null && coreP == null)
        { Debug.LogError("[DomePrefabBaker] No generated parts found yet (part_body/ring/core.glb)."); return; }

        var shield = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/DomeShield.mat");
        var emit   = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/DomeEmit.mat");
        if (shield == null || emit == null) { Debug.LogError("[DomePrefabBaker] Run Execute() first (needs DomeShield/DomeEmit .mat)."); return; }

        var root = new GameObject("BubbleDome");
        var emitter = new GameObject("Emitter");
        emitter.transform.SetParent(root.transform, false);
        var pulse = new List<Renderer>();

        float hubY = 1.6f;
        if (bodyP != null)
        {
            var body = InstantiatePart(bodyP, emitter.transform);
            FitToSize(body, 2.6f, alignBase: true);
            hubY = WorldBounds(body).max.y + 0.35f;   // seat the emitter hub on TOP of the body
        }

        // Core hub: the generated core + a big pulsing glow sphere, slow spin — so
        // it always reads as a powered energy core even if the metal is dark.
        var coreSpin = new GameObject("CoreSpin");
        coreSpin.transform.SetParent(emitter.transform, false);
        coreSpin.transform.localPosition = new Vector3(0, hubY, 0);
        var cs = coreSpin.AddComponent<SpinPart>(); cs.axis = Vector3.up; cs.speed = 25f;
        if (coreP != null) { var core = InstantiatePart(coreP, coreSpin.transform); FitToSize(core, 1.0f, alignBase: false); }
        var glow = Prim(PrimitiveType.Sphere, coreSpin.transform, Vector3.zero, Vector3.one * 0.7f, Quaternion.identity, emit);
        pulse.Add(glow.GetComponent<Renderer>());

        // Gyro ring AROUND the core (up at the hub, tilted), faster spin, ringed with
        // glowing nodes so the ring energizes as it turns.
        var gyro = new GameObject("GyroSpin");
        gyro.transform.SetParent(emitter.transform, false);
        gyro.transform.localPosition = new Vector3(0, hubY, 0);
        gyro.transform.localRotation = Quaternion.Euler(25f, 0f, 12f);
        var gs = gyro.AddComponent<SpinPart>(); gs.axis = Vector3.up; gs.speed = 55f;
        const float ringR = 1.15f;
        if (ringP != null) { var ring = InstantiatePart(ringP, gyro.transform); FitToSize(ring, ringR * 2f, alignBase: false); }
        for (int i = 0; i < 6; i++)
        {
            float a = i * (Mathf.PI * 2f / 6f);
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ringR;
            var node = Prim(PrimitiveType.Sphere, gyro.transform, p, Vector3.one * 0.16f, Quaternion.identity, emit);
            pulse.Add(node.GetComponent<Renderer>());
        }

        var fx = root.AddComponent<DomeEmitterFX>();
        fx.spinner = null;                 // spins handled by SpinPart now
        fx.pulseRenderers = pulse.ToArray();
        fx.emitColor = EmitColor;

        var bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bubble.name = "Shield";
        var bcol = bubble.GetComponent<Collider>(); if (bcol != null) Object.DestroyImmediate(bcol);
        bubble.transform.SetParent(root.transform, false);
        bubble.transform.localScale = Vector3.one * (Radius * 2f);
        var bmr = bubble.GetComponent<MeshRenderer>();
        bmr.sharedMaterial = shield; bmr.shadowCastingMode = ShadowCastingMode.Off; bmr.receiveShadows = false;

        var grow = root.AddComponent<DomeShieldGrow>(); grow.bubble = bubble.transform; grow.fullDiameter = Radius * 2f;
        var dome = root.AddComponent<BubbleDome>(); dome.SetRadius(Radius);

        foreach (var c in emitter.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

        var path = Dir + "/BubbleDome_Gen.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Debug.Log("[DomePrefabBaker] Assembled generated parts → " + path);
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }

    static GameObject InstantiatePart(GameObject prefab, Transform parent)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
        return go;
    }

    // Scale so the largest bounds dimension = target; optionally seat the base at y=0.
    static void FitToSize(GameObject go, float target, bool alignBase)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        Bounds b = rends[0].bounds; for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (maxDim > 0.0001f) go.transform.localScale *= target / maxDim;
        b = rends[0].bounds; for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        // Center in the PARENT's local frame (bounds are world-space; subtract the
        // parent world position or a part parented to a raised hub gets shoved down).
        Vector3 parentPos = go.transform.parent != null ? go.transform.parent.position : Vector3.zero;
        Vector3 lp = go.transform.localPosition;
        if (alignBase) lp.y -= (b.min.y - parentPos.y);
        else lp -= (b.center - parentPos);
        go.transform.localPosition = lp;
    }

    // Drops an instance of the baked prefab into the open scene for a capture.
    public static void SpawnPreview()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/BubbleDome.prefab");
        if (prefab == null) { Debug.LogError("[DomePrefabBaker] no baked prefab to preview"); return; }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.name = "DomePreview";
        inst.transform.position = Vector3.zero;
    }

    public static void SpawnGenPreview()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/BubbleDome_Gen.prefab");
        if (prefab == null) { Debug.LogError("[DomePrefabBaker] no BubbleDome_Gen prefab"); return; }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.name = "DomeGenPreview";
        inst.transform.position = Vector3.zero;

        // Temp light so the capture isn't a black void (represents the in-game sun).
        var lightGO = new GameObject("DomePreviewLight");
        var l = lightGO.AddComponent<Light>();
        l.type = LightType.Directional; l.intensity = 1.4f; l.color = new Color(1f, 0.97f, 0.9f);
        lightGO.transform.rotation = Quaternion.Euler(38f, 150f, 0f);
    }

    static void SaveMat(Material m, string path)
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
    }

    static GameObject Prim(PrimitiveType t, Transform parent, Vector3 lp, Vector3 ls, Quaternion lr, Material mat)
    {
        var go = GameObject.CreatePrimitive(t);
        var col = go.GetComponent<Collider>(); if (col != null) Object.DestroyImmediate(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp; go.transform.localRotation = lr; go.transform.localScale = ls;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        return go;
    }
}
