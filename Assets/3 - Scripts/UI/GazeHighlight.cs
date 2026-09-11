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
    public float outlineWidth = 0.031f;     // 1.7x the original 0.018 (Sam, 2026-09-11)
    [Tooltip("Renderers whose bounds diagonal exceeds this (metres) are skipped — a prompt owned by something huge (the ship) should not slather the whole hull.")]
    public float maxRendererSize = 12f;
    [Tooltip("Renderers SMALLER than this (world metres, bounds diagonal) are not outlined. A cat is three skinned renderers - the body and two separate eye meshes - and a green ring around each eye reads as noise, not as an outline. Anything under this is a detail, not a thing.")]
    public float minRendererSize = 0.25f;
    [Tooltip("At most this many renderers get outlined per target.")]
    public int maxRenderers = 12;

    Object _owner;
    Material _mat;
    // Same shader with the OUTLINE pass switched off: contributes to the
    // stencil silhouette without drawing a rim. For the parts of an object too
    // small to outline (a cat's separate eyeball meshes) that still sit INSIDE
    // its outline -- without this, the body's socket holes are unmasked and
    // the body's own inflated rim draws a green ring inside each eye.
    Material _maskMat;
    readonly List<GameObject> _outlines = new List<GameObject>();
    readonly List<Renderer> _outlineRenderers = new List<Renderer>();

    // -- the wipe (Sam, 2026-09-11) ------------------------------------------
    //
    // "Instead of just getting the green outline instantly, make it go from
    // the bottom up to the top and reveal itself, then when you look away it
    // will do the opposite" -- and if you look back mid-fade, "it will just
    // refill back to the top from where it was so that it doesn't fully
    // restart."
    //
    // So the outline has a reveal level, 0..1, driven toward 1 while gazed
    // and toward 0 when not, with MoveTowards -- which is what gives the
    // resume-from-wherever-it-is for free. The clones are NOT destroyed the
    // moment gaze is lost any more; they stay while the level falls and go
    // only once it reaches 0. A DIFFERENT target appearing snaps the old one
    // away and starts the new one from 0.
    [Tooltip("Seconds for the outline to fill bottom-to-top when you look at something.")]
    public float revealInSeconds = 0.64f;   // 2x slower than the first cut (Sam, 2026-09-11)
    [Tooltip("Seconds for it to empty top-to-bottom after you look away. Slightly slower than the fill, so a glance away and back reads as a hesitation rather than a flicker.")]
    public float revealOutSeconds = 0.55f;
    float _reveal;
    bool _fadingOut;
    static readonly int RevealId    = Shader.PropertyToID("_Reveal");
    static readonly int RevealUpId  = Shader.PropertyToID("_RevealUp");
    static readonly int RevealMinId = Shader.PropertyToID("_RevealMin");
    static readonly int RevealMaxId = Shader.PropertyToID("_RevealMax");
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
        if (_maskMat != null) Destroy(_maskMat);
    }

    void LateUpdate()
    {
        Object owner = InteractPromptUI.CurrentOwner;

        if (!ReferenceEquals(owner, _owner))
        {
            if (owner != null)
            {
                // A different target: the old rim snaps away, the new one
                // starts filling from the bottom.
                ClearOutlines();
                _owner = owner;
                _reveal = 0f;
                _fadingOut = false;
                Apply(owner);
            }
            else
            {
                // Lost the target: keep the rim and let it drain. _owner is
                // kept so that re-acquiring the SAME target resumes upward
                // from wherever the level has fallen to.
                _fadingOut = true;
            }
        }
        else if (owner != null)
        {
            _fadingOut = false;     // still on it, or back on it mid-fade
        }

        // Target (or an outlined child) may have been destroyed under us.
        if (_owner is Component c && c == null)
        {
            ClearOutlines(); _owner = null; _reveal = 0f; _fadingOut = false;
            return;
        }
        if (_owner == null) return;

        float target = _fadingOut ? 0f : 1f;
        float secs = target > _reveal ? revealInSeconds : revealOutSeconds;
        _reveal = Mathf.MoveTowards(_reveal, target, Time.unscaledDeltaTime / Mathf.Max(0.02f, secs));

        if (_fadingOut && _reveal <= 0f)
        {
            ClearOutlines(); _owner = null; _fadingOut = false;
            return;
        }

        PushReveal();
    }

    /// Feed the shader the current level and the object's extent along its
    /// OWN up -- not world Y. A cat on the underside of a planet still wipes
    /// from its paws to its ears. Recomputed every frame because the target
    /// can be an animating skinned mesh, and bounds are cheap.
    void PushReveal()
    {
        if (_mat == null || _outlineRenderers.Count == 0) return;
        var comp = _owner as Component;
        Vector3 up = comp != null ? comp.transform.up : Vector3.up;

        float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
        for (int i = 0; i < _outlineRenderers.Count; i++)
        {
            var r = _outlineRenderers[i];
            if (r == null) continue;
            var b = r.bounds;
            // Extent of an axis-aligned box along an arbitrary direction.
            float c = Vector3.Dot(b.center, up);
            float e = Mathf.Abs(up.x) * b.extents.x + Mathf.Abs(up.y) * b.extents.y + Mathf.Abs(up.z) * b.extents.z;
            if (c - e < lo) lo = c - e;
            if (c + e > hi) hi = c + e;
        }
        if (float.IsInfinity(lo)) return;

        _mat.SetFloat(RevealId, _reveal);
        _mat.SetVector(RevealUpId, up);
        _mat.SetFloat(RevealMinId, lo);
        _mat.SetFloat(RevealMaxId, hi);
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
            float size = r.bounds.size.magnitude;
            if (size > maxRendererSize) continue;
            // Too small to outline, but it still has to MASK, or the hole it
            // fills in a bigger renderer (an eye socket) is left open for that
            // renderer's rim to draw through.
            bool maskOnly = size < minRendererSize;

            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                var go = NewOutlineChild(smr.transform, smr.sharedMesh.subMeshCount);
                var clone = go.AddComponent<SkinnedMeshRenderer>();
                // Smoothed normals for SKINNED meshes too. This path used the
                // raw mesh, so on a low-poly rig with hard edges (the cats)
                // the inflation ran along split normals and the outline tore
                // apart at every edge - it looked like it was outlining the
                // cat's parts rather than the cat. Instantiate(Mesh) is a deep
                // copy, so bone weights and bind poses come along and skinning
                // still works. Needs Read/Write on the model; unreadable meshes
                // fall back to the raw one inside SmoothedOutlineMesh.
                clone.sharedMesh = SmoothedOutlineMesh(smr.sharedMesh);
                clone.bones = smr.bones;
                clone.rootBone = smr.rootBone;
                clone.localBounds = smr.localBounds;
                Configure(clone, smr.sharedMesh.subMeshCount, maskOnly);
                made++;
            }
            else if (r is MeshRenderer)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var go = NewOutlineChild(r.transform, mf.sharedMesh.subMeshCount);
                // Smoothed-normal copy: on hard-edged meshes (the console
                // screen box) the corner vertices are DUPLICATED with split
                // normals, so inflating along them pushes each face apart and
                // the outline's corners never meet (Sam's screenshot).
                // Averaging normals across position-duplicates closes them.
                go.AddComponent<MeshFilter>().sharedMesh = SmoothedOutlineMesh(mf.sharedMesh);
                Configure(go.AddComponent<MeshRenderer>(), mf.sharedMesh.subMeshCount, maskOnly);
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

    void Configure(Renderer clone, int subMeshes, bool maskOnly)
    {
        var mats = new Material[Mathf.Max(1, subMeshes)];
        var use = maskOnly ? _maskMat : _mat;
        for (int i = 0; i < mats.Length; i++) mats[i] = use;
        clone.sharedMaterials = mats;
        _outlineRenderers.Add(clone);
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
        _mat.SetFloat(RevealId, 0f);    // a new target starts hidden, then fills

        // Mask-only twin. One queue EARLIER than the outline material, so every
        // mask-only piece has stamped the stencil before any rim is drawn --
        // Unity draws opaques by queue first, and the eye must already be
        // masked when the body's OUTLINE pass reaches the socket.
        _maskMat = new Material(shader);
        _maskMat.SetShaderPassEnabled("OUTLINE", false);
        _maskMat.renderQueue = _mat.renderQueue - 1;
        return true;
    }

    void ClearOutlines()
    {
        for (int i = 0; i < _outlines.Count; i++)
            if (_outlines[i] != null) Destroy(_outlines[i]);
        _outlines.Clear();
        _outlineRenderers.Clear();
    }

    // ── smoothed-normal outline meshes, cached per source mesh ───────────
    // Skinned aliens keep their (already smooth) shared mesh; only static
    // MeshRenderers get the bake. Cost: one bake per unique mesh, ever.

    static readonly Dictionary<Mesh, Mesh> s_smoothed = new Dictionary<Mesh, Mesh>();

    static Mesh SmoothedOutlineMesh(Mesh src)
    {
        if (s_smoothed.TryGetValue(src, out var cached) && cached != null) return cached;
        if (!src.isReadable) { s_smoothed[src] = src; return src; }   // can't bake — keep the gap over an exception

        var verts = src.vertices;
        var norms = src.normals;
        if (norms == null || norms.Length != verts.Length) { s_smoothed[src] = src; return src; }

        // Average normals across vertices that share a POSITION (the split
        // corners), quantised so float noise still buckets together.
        var sum = new Dictionary<Vector3, Vector3>(verts.Length);
        Vector3 Key(Vector3 p) => new Vector3(Mathf.Round(p.x * 1000f), Mathf.Round(p.y * 1000f), Mathf.Round(p.z * 1000f));
        for (int i = 0; i < verts.Length; i++)
        {
            var k = Key(verts[i]);
            sum[k] = sum.TryGetValue(k, out var n) ? n + norms[i] : norms[i];
        }
        var outNorms = new Vector3[norms.Length];
        for (int i = 0; i < verts.Length; i++)
            outNorms[i] = sum[Key(verts[i])].normalized;

        var m = Object.Instantiate(src);
        m.name = src.name + "_outline";
        m.normals = outNorms;
        s_smoothed[src] = m;
        return m;
    }
}
