using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Rim-lights whatever the player is gaze-locked onto (Sam's request,
/// 2026-08-16): look at the shuttle computer and its edges glow, look away
/// and it stops.
///
/// ── One source of truth ──────────────────────────────────────────────────
/// The outline keys off <see cref="InteractPromptUI.CurrentOwner"/> — the
/// exact object whose [F] prompt is on screen — so the glow and the prompt
/// can never disagree about what you're looking at (the promise/grade rule,
/// applied to visuals).
///
/// ── How ──────────────────────────────────────────────────────────────────
/// Classic inverted hull: each of the target's renderers gets a child clone
/// drawn with SoundOfSpace/GazeOutline (front-culled, normal-inflated).
/// Cheap (one extra draw per renderer, no post-processing, no cameras
/// touched — deliberately nowhere near the fragile atmosphere stack), works
/// on skinned aliens, and occluded parts of the rim stay hidden.
/// </summary>
public class GazeHighlight : MonoBehaviour
{
    public static GazeHighlight Instance { get; private set; }

    [Tooltip("Rim color. Defaults to the helmet-HUD amber so the outline and the [F] prompt read as one system.")]
    public Color outlineColor = new Color32(0xFF, 0xC4, 0x6B, 0xFF);
    [Tooltip("Outline thickness in world metres.")]
    public float outlineWidth = 0.018f;
    [Tooltip("Renderers whose bounds diagonal exceeds this (metres) are skipped — a prompt owned by something huge (the ship) should not slather the whole hull.")]
    public float maxRendererSize = 12f;
    [Tooltip("At most this many renderers get outlined per target.")]
    public int maxRenderers = 12;

    Object _owner;
    Material _mat;
    readonly List<GameObject> _outlines = new List<GameObject>();
    static readonly List<Renderer> _rendBuf = new List<Renderer>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("GazeHighlight");
        DontDestroyOnLoad(go);
        go.AddComponent<GazeHighlight>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ClearOutlines();
        if (_mat != null) Destroy(_mat);
    }

    void LateUpdate()
    {
        Object owner = InteractPromptUI.CurrentOwner;
        if (ReferenceEquals(owner, _owner))
        {
            // Target (or an outlined child) may have been destroyed under us.
            if (_owner is Component c && c == null) { ClearOutlines(); _owner = null; }
            return;
        }
        ClearOutlines();
        _owner = owner;
        if (owner != null) Apply(owner);
    }

    void Apply(Object owner)
    {
        var comp = owner as Component;
        if (comp == null) return;
        if (!EnsureMaterial()) return;

        // Same aim resolution the gaze test uses: an Interactable may point a
        // small control at the visible mesh it represents.
        Transform aim = comp.transform;
        if (owner is Interactable it && it.gazeTarget != null) aim = it.gazeTarget;

        int made = OutlineUnder(aim);
        // Mirror InteractGaze's one-level parent walk: scripts that live on a
        // mesh-less trigger child (the bonfire) draw their parent's geometry.
        if (made == 0 && aim.parent != null) OutlineUnder(aim.parent);
    }

    int OutlineUnder(Transform root)
    {
        int made = 0;
        root.GetComponentsInChildren(_rendBuf);
        for (int i = 0; i < _rendBuf.Count && made < maxRenderers; i++)
        {
            var r = _rendBuf[i];
            if (r == null || !r.enabled) continue;
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            if (r.gameObject.name == "GazeOutline") continue;   // never outline an outline
            if (r.bounds.size.magnitude > maxRendererSize) continue;

            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                var go = NewOutlineChild(smr.transform, smr.sharedMesh.subMeshCount);
                var clone = go.AddComponent<SkinnedMeshRenderer>();
                clone.sharedMesh = smr.sharedMesh;
                clone.bones = smr.bones;
                clone.rootBone = smr.rootBone;
                clone.localBounds = smr.localBounds;
                Configure(clone, smr.sharedMesh.subMeshCount);
                made++;
            }
            else if (r is MeshRenderer)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var go = NewOutlineChild(r.transform, mf.sharedMesh.subMeshCount);
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                Configure(go.AddComponent<MeshRenderer>(), mf.sharedMesh.subMeshCount);
                made++;
            }
        }
        return made;
    }

    GameObject NewOutlineChild(Transform parent, int subMeshes)
    {
        var go = new GameObject("GazeOutline");
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        _outlines.Add(go);
        return go;
    }

    void Configure(Renderer clone, int subMeshes)
    {
        var mats = new Material[Mathf.Max(1, subMeshes)];
        for (int i = 0; i < mats.Length; i++) mats[i] = _mat;
        clone.sharedMaterials = mats;
        clone.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        clone.receiveShadows = false;
        clone.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        clone.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    bool EnsureMaterial()
    {
        if (_mat != null)
        {
            _mat.SetColor("_OutlineColor", outlineColor);
            _mat.SetFloat("_OutlineWidth", outlineWidth);
            return true;
        }
        // Registered in GraphicsSettings' Always Included Shaders so this
        // survives build stripping; the guard keeps a missing shader from
        // breaking interaction itself.
        var shader = Shader.Find("SoundOfSpace/GazeOutline");
        if (shader == null)
        {
            Debug.LogWarning("[GazeHighlight] SoundOfSpace/GazeOutline shader not found — highlight disabled");
            enabled = false;
            return false;
        }
        _mat = new Material(shader);
        _mat.SetColor("_OutlineColor", outlineColor);
        _mat.SetFloat("_OutlineWidth", outlineWidth);
        return true;
    }

    void ClearOutlines()
    {
        for (int i = 0; i < _outlines.Count; i++)
            if (_outlines[i] != null) Destroy(_outlines[i]);
        _outlines.Clear();
    }
}
