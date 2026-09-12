using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The bar counter in the village pub. Put this on the counter object (under
/// the planet, inside the house). No child markers needed: the "middle of the
/// counter" and the placeable top face are read off the counter's own mesh.
///
/// Three jobs:
///   • <see cref="PourBeer"/> — called by the bartender's dialogue (Custom
///     effects "pourBeer" … "pourBeer5") after the money is taken: full cups
///     with a <see cref="BeerCupPickup"/> each appear on a grid of SLOTS across
///     the top face, middle first, working outward. The bartender only refuses
///     when every slot is taken (<see cref="IsFull"/>); reply buttons for N
///     beers show only while <see cref="FreeSlots"/> ≥ N.
///   • While the player holds an EMPTY cup and looks at the counter, the
///     counter tints green and a translucent ghost cup follows the crosshair
///     across the top face. F sets the empty cup down right there.
///   • A set-down empty cup fades out after <see cref="emptyCupLifetime"/> s
///     (<see cref="BeerCupFadeAway"/>).
///
/// ── Why the ghost is NOT a Physics.Raycast ──────────────────────────────
/// Colliders live at the planet's PHYSICS pose; the counter's transform (and
/// the camera) are at the interpolated RENDER pose. On an orbiting planet the
/// two differ by up to a physics step of travel every frame, so a raycast hit
/// point converted through the render transform jittered — and the cup got
/// placed inside / above the counter. The crosshair ray is intersected with
/// the counter's own local mesh box analytically instead: camera and counter
/// are on the same clock, so the ghost sits still and F puts the cup exactly
/// where the ghost is. The same reason the Interactable gaze gate is bypassed
/// (<c>requireGazeToInteract = false</c>) and <see cref="CanInteract"/> uses
/// the analytic hit.
///
/// Cups are parented to the counter's PARENT (the counter may be a
/// non-uniformly scaled cube) and sized in world metres. Not saved: on reload
/// the counter is bare; the player's cups live in EquipmentSave.
/// </summary>
public class BarCounter : Interactable
{
    public static readonly List<BarCounter> All = new List<BarCounter>();

    [Tooltip("Cup mesh to spawn (FantasyVillage CupGOOD.prefab). Empty = borrow the Player's BeerCupController.cupPrefab.")]
    public GameObject cupPrefab;
    [Tooltip("World size multiplier for cups on the counter (1 = the prefab's own metres).")]
    public float cupWorldScale = 1f;
    [Tooltip("A Standard-shader material in FADE mode. Used for the green ghost cup and for fading empties out. Must be a real asset so the transparent shader variant is in the build.")]
    public Material fadeMaterial;
    [Tooltip("Colour of the placement ghost.")]
    public Color ghostColor = new Color(0.35f, 1f, 0.45f, 0.5f);
    [Tooltip("Tint multiplied onto the counter while a cup can be set down on it.")]
    public Color highlightTint = new Color(0.45f, 1f, 0.5f, 1f);
    [Tooltip("Seconds a set-down empty cup stays before fading.")]
    public float emptyCupLifetime = 5f;
    [Tooltip("Seconds the fade-out takes.")]
    public float fadeSeconds = 1f;
    [Tooltip("Keep the ghost this far (metres) inside the counter's edges.")]
    public float edgeMargin = 0.12f;
    [Tooltip("How far from the counter the set-down prompt works (a trigger this size is added if the counter has no trigger collider).")]
    public float triggerRadius = 3.5f;
    [Tooltip("Furthest the crosshair reaches the counter top from (metres).")]
    public float reach = 5f;
    [Tooltip("Distance between beer slots on the counter top (metres). The cup is ~0.27 m across.")]
    public float slotSpacing = 0.45f;
    [Tooltip("Hard cap on how many beers the counter can hold at once.")]
    public int maxSlots = 30;

    Vector3[] _slotLocal;            // counter-local slot centres on the top face, middle first
    GameObject[] _slotCup;           // the full beer standing in each slot (null = free)
    GameObject _ghost;
    Material _ghostMat;
    Renderer[] _ownRenderers;        // the counter's own renderers, captured before any cup exists
    MaterialPropertyBlock _tintBlock;
    bool _tinted;
    bool _ghostValid;
    Vector3 _ghostWorld;
    Camera _cam;
    float _nextCamRetry;
    Bounds _meshBounds;              // counter-local

    /// Any full beer standing on the counter.
    public bool HasFullCup { get { for (int i = 0; i < SlotCount; i++) if (_slotCup[i] != null) return true; return false; } }
    public int SlotCount => _slotLocal != null ? _slotLocal.Length : 0;
    public int FreeSlots { get { int n = 0; for (int i = 0; i < SlotCount; i++) if (_slotCup[i] == null) n++; return n; } }
    public bool IsFull => SlotCount > 0 && FreeSlots == 0;

    void Awake()
    {
        _ownRenderers = GetComponentsInChildren<Renderer>(true);
        _tintBlock = new MaterialPropertyBlock();

        var mf = GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null) _meshBounds = mf.sharedMesh.bounds;
        else
        {
            var box = GetComponent<BoxCollider>();
            _meshBounds = box != null ? new Bounds(box.center, box.size) : new Bounds(Vector3.zero, Vector3.one);
        }

        BuildSlots();

        bool hasTrigger = false;
        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (c.isTrigger) { hasTrigger = true; break; }
        if (!hasTrigger)
        {
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = triggerRadius;
        }

        // The analytic crosshair test below is the gaze gate (see the class doc).
        requireGazeToInteract = false;
    }

    /// A grid of cup positions across the top face, spaced slotSpacing metres
    /// apart in WORLD terms and sorted middle-first, so the first beer lands
    /// dead centre and the bar fills outward.
    void BuildSlots()
    {
        Vector3 s = transform.lossyScale;
        float mx = edgeMargin / Mathf.Max(0.0001f, Mathf.Abs(s.x));
        float mz = edgeMargin / Mathf.Max(0.0001f, Mathf.Abs(s.z));
        float sx = slotSpacing / Mathf.Max(0.0001f, Mathf.Abs(s.x));
        float sz = slotSpacing / Mathf.Max(0.0001f, Mathf.Abs(s.z));
        float minX = _meshBounds.min.x + mx, maxX = _meshBounds.max.x - mx;
        float minZ = _meshBounds.min.z + mz, maxZ = _meshBounds.max.z - mz;
        int nx = Mathf.Max(1, Mathf.FloorToInt((maxX - minX) / sx) + 1);
        int nz = Mathf.Max(1, Mathf.FloorToInt((maxZ - minZ) / sz) + 1);
        var c = _meshBounds.center;
        var list = new List<Vector3>(nx * nz);
        for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
            {
                float x = nx == 1 ? c.x : c.x + (ix - (nx - 1) * 0.5f) * sx;
                float z = nz == 1 ? c.z : c.z + (iz - (nz - 1) * 0.5f) * sz;
                list.Add(new Vector3(x, _meshBounds.max.y, z));
            }
        // Middle first (world distances so a long thin bar still fills sensibly).
        list.Sort((a, b) =>
        {
            float da = (transform.TransformPoint(a) - TopCenterWorld()).sqrMagnitude;
            float db = (transform.TransformPoint(b) - TopCenterWorld()).sqrMagnitude;
            return da.CompareTo(db);
        });
        if (list.Count > Mathf.Max(1, maxSlots)) list.RemoveRange(maxSlots, list.Count - maxSlots);
        _slotLocal = list.ToArray();
        _slotCup = new GameObject[_slotLocal.Length];
    }

    void OnEnable()  { if (!All.Contains(this)) All.Add(this); }
    void OnDisable()
    {
        All.Remove(this);
        SetTint(false);
        if (_ghost != null) _ghost.SetActive(false);
    }

    void OnDestroy()
    {
        if (_ghostMat != null) Destroy(_ghostMat);
    }

    static BeerCupController Controller() => Object.FindObjectOfType<BeerCupController>();

    // ── geometry (all in the counter's RENDER-clock frame) ────────

    /// World point in the middle of the top face.
    Vector3 TopCenterWorld()
    {
        var c = _meshBounds.center;
        return transform.TransformPoint(new Vector3(c.x, _meshBounds.max.y, c.z));
    }

    /// Snap a counter-LOCAL point onto the top face, kept inside the edges. Returns world.
    Vector3 SnapLocalToTop(Vector3 l)
    {
        Vector3 s = transform.lossyScale;
        float mx = edgeMargin / Mathf.Max(0.0001f, Mathf.Abs(s.x));
        float mz = edgeMargin / Mathf.Max(0.0001f, Mathf.Abs(s.z));
        float minX = _meshBounds.min.x + mx, maxX = _meshBounds.max.x - mx;
        float minZ = _meshBounds.min.z + mz, maxZ = _meshBounds.max.z - mz;
        if (minX > maxX) minX = maxX = _meshBounds.center.x;
        if (minZ > maxZ) minZ = maxZ = _meshBounds.center.z;
        l.x = Mathf.Clamp(l.x, minX, maxX);
        l.z = Mathf.Clamp(l.z, minZ, maxZ);
        l.y = _meshBounds.max.y;
        return transform.TransformPoint(l);
    }

    Quaternion CupRotation() => Quaternion.LookRotation(transform.forward, transform.up);

    Camera Cam()
    {
        if (_cam != null && _cam.isActiveAndEnabled) return _cam;
        if (Time.unscaledTime < _nextCamRetry) return null;
        _nextCamRetry = Time.unscaledTime + 1f;
        _cam = Camera.main;
        return _cam;
    }

    /// The crosshair ray against the counter's local mesh box — no physics, no
    /// clock mismatch. Gives the entry point in counter-local space.
    bool TryCrosshairHitLocal(out Vector3 local)
    {
        local = default;
        var cam = Cam();
        if (cam == null) return false;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 o = transform.InverseTransformPoint(ray.origin);
        Vector3 d = transform.InverseTransformVector(ray.direction);   // scale-aware, NOT normalised on purpose:
                                                                       // t stays in world metres along the ray
        Vector3 mn = _meshBounds.min, mx = _meshBounds.max;
        float tmin = 0f, tmax = reach;
        for (int a = 0; a < 3; a++)
        {
            float da = d[a], oa = o[a];
            if (Mathf.Abs(da) < 1e-6f)
            {
                if (oa < mn[a] || oa > mx[a]) return false;
                continue;
            }
            float t1 = (mn[a] - oa) / da, t2 = (mx[a] - oa) / da;
            if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
            if (t1 > tmin) tmin = t1;
            if (t2 < tmax) tmax = t2;
            if (tmin > tmax) return false;
        }
        local = o + d * tmin;
        return true;
    }

    // ── pouring ───────────────────────────────────────────────────

    /// <summary>Pour <paramref name="count"/> beers into free slots, middle first. Returns how many were poured.</summary>
    public int PourBeer(int count = 1)
    {
        var ctrl = Controller();
        var prefab = cupPrefab != null ? cupPrefab : (ctrl != null ? ctrl.cupPrefab : null);
        if (prefab == null)
        {
            Debug.LogWarning("[BarCounter] no cup prefab (assign cupPrefab here or on BeerCupController).", this);
            return 0;
        }
        if (_slotLocal == null) BuildSlots();

        int poured = 0;
        for (int i = 0; i < SlotCount && poured < count; i++)
        {
            if (_slotCup[i] != null) continue;
            var cup = SpawnCup(prefab, transform.TransformPoint(_slotLocal[i]), true);
            cup.name = "BeerCup(Full)";
            if (ctrl != null)
                BeerCupArt.AttachLiquid(cup, ctrl.liquidAxis, ctrl.liquidRadius,
                                        ctrl.liquidFloorY, ctrl.liquidFullY, ctrl.foamThickness, 1f);
            else
                BeerCupArt.AttachLiquid(cup, new Vector3(0.005f, 0f, -0.001f), 0.07f, 0.035f, 0.19f, 0.02f, 1f);
            var pickup = cup.AddComponent<BeerCupPickup>();
            pickup.counter = this;
            _slotCup[i] = cup;
            poured++;
        }
        return poured;
    }

    /// <summary>The pickup took a beer off the counter; its slot is free again.</summary>
    public void NotifyCupTaken(BeerCupPickup p)
    {
        if (p == null) return;
        for (int i = 0; i < SlotCount; i++)
            if (_slotCup[i] == p.gameObject) { _slotCup[i] = null; return; }
    }

    GameObject SpawnCup(GameObject prefab, Vector3 worldPos, bool keepColliders)
    {
        Transform parent = transform.parent != null ? transform.parent : transform;
        var go = Instantiate(prefab, worldPos, CupRotation(), parent);
        Vector3 ps = parent.lossyScale;
        float inv = cupWorldScale / Mathf.Max(0.0001f, ps.x);
        go.transform.localScale = Vector3.one * inv;
        SetLayerRecursively(go, gameObject.layer);
        // A prop on a counter, not a physics object. The full cup keeps its solid
        // collider so the crosshair can pick it; empties are decorative.
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
        if (!keepColliders)
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        return go;
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursively(go.transform.GetChild(i).gameObject, layer);
    }

    // ── placement ghost + green tint ──────────────────────────────

    bool HoldingEmptyCup()
    {
        var ctrl = Controller();
        return ctrl != null && ctrl.HoldingEmpty;
    }

    protected override void Update()
    {
        // Resolve the ghost FIRST so CanInteract/BuildInteractMessage (called by
        // the base) see this frame's answer.
        bool want = playerInInteractionZone && !PlayerController.isInDialogue && HoldingEmptyCup();
        _ghostValid = false;
        if (want && TryCrosshairHitLocal(out Vector3 local))
        {
            _ghostValid = true;
            _ghostWorld = SnapLocalToTop(local);
        }
        SetTint(_ghostValid);
        ShowGhost(_ghostValid);

        base.Update();
    }

    void SetTint(bool on)
    {
        if (on == _tinted || _ownRenderers == null) return;
        _tinted = on;
        for (int i = 0; i < _ownRenderers.Length; i++)
        {
            var r = _ownRenderers[i];
            if (r == null) continue;
            if (on)
            {
                r.GetPropertyBlock(_tintBlock);
                _tintBlock.SetColor("_Color", highlightTint);
                r.SetPropertyBlock(_tintBlock);
            }
            else r.SetPropertyBlock(null);
        }
    }

    void ShowGhost(bool on)
    {
        if (!on) { if (_ghost != null && _ghost.activeSelf) _ghost.SetActive(false); return; }

        if (_ghost == null)
        {
            var ctrl = Controller();
            var prefab = cupPrefab != null ? cupPrefab : (ctrl != null ? ctrl.cupPrefab : null);
            if (prefab == null) return;
            _ghost = SpawnCup(prefab, _ghostWorld, false);
            _ghost.name = "BeerCup(Ghost)";
            foreach (var p in _ghost.GetComponentsInChildren<BeerCupPickup>(true)) Destroy(p);
            _ghostMat = MakeGhostMaterial();
            foreach (var r in _ghost.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterial = _ghostMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }
        if (!_ghost.activeSelf) _ghost.SetActive(true);
        _ghost.transform.SetPositionAndRotation(_ghostWorld, CupRotation());
    }

    Material MakeGhostMaterial()
    {
        Material m;
        if (fadeMaterial != null) m = new Material(fadeMaterial);
        else
        {
            // Editor-only fallback: a build strips Standard's Fade variant unless
            // a material asset carries it. Assign fadeMaterial.
            m = new Material(Shader.Find("Standard"));
            m.SetFloat("_Mode", 2f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = 3000;
        }
        m.mainTexture = null;
        m.color = ghostColor;
        return m;
    }

    // ── setting the empty cup down (Interactable) ─────────────────

    protected override bool CanInteract()
    {
        if (!TutorialGate.IsUnlocked(TutorialAbility.Pickup)) return false;
        return _ghostValid;
    }

    protected override string BuildInteractMessage() =>
        $"Press {PromptGlyphs.Interact} to set the empty cup down";

    protected override void Interact()
    {
        base.Interact();
        var ctrl = Controller();
        if (ctrl == null || !ctrl.HoldingEmpty) return;

        var prefab = cupPrefab != null ? cupPrefab : ctrl.cupPrefab;
        Vector3 where = _ghostValid ? _ghostWorld : TopCenterWorld();

        ctrl.RemoveEmptyCup();              // keeps holding an empty if more remain

        if (prefab != null)
        {
            var empty = SpawnCup(prefab, where, false);
            empty.name = "BeerCup(Empty)";
            foreach (var p in empty.GetComponentsInChildren<BeerCupPickup>(true)) Destroy(p);
            empty.AddComponent<BeerCupFadeAway>().Begin(emptyCupLifetime, fadeSeconds, fadeMaterial);
        }
        _ghostValid = false;
        SetTint(false);
        ShowGhost(false);
        GameUI.ClearInteractionPrompt(this);
    }
}
