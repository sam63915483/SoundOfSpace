using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Makes the astronaut cast a shadow. (Sam, 2026-09-07: "I want my astronaut's
/// body and the things they hold to cast shadows.")
///
/// <b>Why the body never did.</b> The astronaut's renderers sit on the
/// <c>PlayerReflect</c> layer, lit by their own directional
/// <c>AstronautReflectLight</c> (no shadows, that layer only) so the suit reads
/// clearly whatever the sun is doing. The sun — <c>Sun Shadow Caster</c> —
/// deliberately EXCLUDES that layer from its culling mask so the body is not lit
/// twice. A light that cannot see a renderer cannot draw its shadow either, so
/// the body cast nothing. Simply adding the layer to the sun would double-light
/// the suit and change its whole look.
///
/// <b>What this does instead.</b> For every renderer on the reflect layer under
/// the player it spawns a <i>shadow proxy</i>: a copy of the same mesh — the
/// same bones for the skinned body — on the Default layer with
/// <c>ShadowCastingMode.ShadowsOnly</c>. The sun sees it and draws its shadow;
/// ShadowsOnly means the proxy itself is never drawn, so nothing is lit twice.
/// The reflect light does not see Default, and the viewmodel fill light casts no
/// shadows, so neither is touched.
///
/// Attached automatically on every gameplay scene load (a <c>sceneLoaded</c>
/// hook, which — unlike a one-shot RuntimeInitializeOnLoadMethod — fires for
/// the gameplay scene in a build that boots through the main menu).
/// </summary>
[DisallowMultipleComponent]
public class PlayerShadowProxy : MonoBehaviour
{
    [Tooltip("Layer the astronaut's renderers live on. Proxies are made for renderers on this layer only.")]
    public string reflectLayerName = "PlayerReflect";

    [Tooltip("Layer the shadow proxies are put on — one the sun's culling mask includes.")]
    public string proxyLayerName = "Default";

    readonly List<Renderer> _proxies = new List<Renderer>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MainMenu") return;
        var pc = FindObjectOfType<PlayerController>(true);
        if (pc == null) return;
        if (pc.GetComponent<PlayerShadowProxy>() == null) pc.gameObject.AddComponent<PlayerShadowProxy>();
    }

    void Start()  { Rebuild(); }
    void OnDestroy() { Clear(); }

    /// <summary>Build (or rebuild) the proxies. Safe to call again after the
    /// astronaut's look changes; old proxies are dropped first.</summary>
    public void Rebuild()
    {
        Clear();
        int reflect = LayerMask.NameToLayer(reflectLayerName);
        int proxy   = LayerMask.NameToLayer(proxyLayerName);
        if (reflect < 0 || proxy < 0)
        {
            Debug.LogWarning($"[PlayerShadowProxy] layer missing (reflect={reflect}, proxy={proxy}) — no body shadow.");
            return;
        }

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r.gameObject.layer != reflect) continue;
            if (r.name.StartsWith("~shadow ")) continue;          // one of ours
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;

            Renderer made = null;
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                // Same mesh, same bones: the proxy deforms exactly with the body.
                var go = new GameObject("~shadow " + r.name);
                go.transform.SetParent(r.transform, false);
                go.layer = proxy;
                var p = go.AddComponent<SkinnedMeshRenderer>();
                p.sharedMesh      = smr.sharedMesh;
                p.sharedMaterials = smr.sharedMaterials;
                p.bones           = smr.bones;
                p.rootBone        = smr.rootBone;
                p.quality         = smr.quality;
                p.updateWhenOffscreen = true;    // its bounds must follow the pose or the shadow pops
                made = p;
            }
            else if (r is MeshRenderer mr)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var go = new GameObject("~shadow " + r.name);
                go.transform.SetParent(r.transform, false);   // rides the source exactly
                go.layer = proxy;
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var p = go.AddComponent<MeshRenderer>();
                p.sharedMaterials = mr.sharedMaterials;
                made = p;
            }
            if (made == null) continue;

            made.shadowCastingMode = ShadowCastingMode.ShadowsOnly;   // never drawn, only its shadow
            made.receiveShadows    = false;
            made.lightProbeUsage   = LightProbeUsage.Off;
            made.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _proxies.Add(made);
        }

        if (_proxies.Count == 0)
            Debug.LogWarning("[PlayerShadowProxy] no renderers on layer '" + reflectLayerName + "' under the player — nothing to shadow.");
    }

    void Clear()
    {
        foreach (var p in _proxies) if (p != null) Destroy(p.gameObject);
        _proxies.Clear();
    }
}
