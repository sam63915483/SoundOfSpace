using UnityEngine;
using UnityEngine.UI;

// Procedural center reticle that replaces the old static white "Dot".
//
// Idle  : a triangular frame of 3 corner chevrons (points up).
// Active: a square frame of 4 corner brackets — the "lock-on" state shown
//         whenever an interactable is in range (InteractPromptUI.IsPromptVisible).
//
// The morph between the two is a quick rotate-scale-fade tween: the triangle
// rotates / shrinks out while the square spins / settles in (mirrors the
// brainstorm mockup). A small center pip stays put the whole time.
//
// Attach this to the existing scene "Dot" GameObject (a UI Image under the HUD
// canvas). On Awake it disables that Image and builds the bracket arms as
// children, so it inherits the Dot's centering + whatever HUD-visibility gating
// the Dot already had. Nothing is auto-created, so the MainMenu build-seeding
// trap (CLAUDE.md trap #1) does not apply.
//
// All geometry is authored in reference-canvas pixels and multiplied by `scale`.
//
// [ExecuteAlways] so the reticle also draws (and re-tunes live) in the Editor
// without entering Play mode. The built children are flagged DontSave so they
// never get serialized into the scene — they're rebuilt on enable / on edit.
[ExecuteAlways]
public class CrosshairReticle : MonoBehaviour
{
    // ── Live state ───────────────────────────────────────────────────
    CanvasGroup _triGroup;   // 3 chevrons (idle)
    CanvasGroup _sqGroup;    // 4 corners  (active / lock-on)
    RectTransform _triRT;
    RectTransform _sqRT;
    float _morph;            // 0 = triangle, 1 = square

    // Base geometry, in reference-canvas px (before `scale`). Tuned to match
    // the approved mockup proportions.
    const float SqRadius  = 11f;   // half-extent of the square frame
    const float TriRadius = 13f;   // circumradius of the triangle
    const float ArmLen    = 7f;    // length of each bracket arm
    const float PipRadius = 1.6f;

    bool _needsRebuild;

    void OnEnable()  { Rebuild(); }
    void OnDisable() { Teardown(); }

    void OnValidate()
    {
        // Field changed in the Inspector — rebuild on the next editor tick
        // (can't safely destroy/create GameObjects from inside OnValidate).
        _needsRebuild = true;
    }

    void Rebuild()
    {
        Teardown();

        // Normalise the host RectTransform — the legacy Dot carried a 0.1
        // localScale + 90° rotation we don't want to inherit.
        var rt = transform as RectTransform;
        if (rt != null)
        {
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }

        // Hide the old white dot Image on this object (if present).
        var ownImage = GetComponent<Image>();
        if (ownImage != null) ownImage.enabled = false;

        Build();
        ApplyMorph(_morph);

        // Snapshot the geometry-affecting fields so Update can detect external
        // changes (inspector drag, MCP/scripted set_property that doesn't fire
        // OnValidate) and rebuild.
        _builtScale = scale;
        _builtThickness = thickness;
        _builtShowPip = showPip;
        _builtColor = color;
    }

    float _builtScale = float.NaN, _builtThickness = float.NaN;
    bool _builtShowPip;
    Color _builtColor;

    // Destroy any reticle children we built previously (named below). Handles
    // both our cached refs and stragglers from a prior enable/recompile.
    void Teardown()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i);
            if (c == null) continue;
            if (c.name == "SquareFrame" || c.name == "TriangleFrame" || c.name == "Pip")
                DestroyGO(c.gameObject);
        }
        _triGroup = _sqGroup = null;
        _triRT = _sqRT = null;
    }

    static void DestroyGO(GameObject go)
    {
        if (Application.isPlaying) Destroy(go);
        else DestroyImmediate(go);
    }

    void Update()
    {
        if (_needsRebuild
            || !Mathf.Approximately(_builtScale, scale)
            || !Mathf.Approximately(_builtThickness, thickness)
            || _builtShowPip != showPip
            || _builtColor != color)
        {
            _needsRebuild = false;
            Rebuild();
        }

        // Keep the gaze gate in sync with the inspector fields (live-tunable).
        InteractGaze.RequireGaze = requireLookToInteract;
        InteractGaze.AimRadius = aimRadius;

        bool active = Application.isPlaying && InteractPromptUI.IsPromptVisible;
        float target = active ? 1f : 0f;
        if (!Mathf.Approximately(_morph, target))
        {
            float step = (morphSeconds <= 0f) ? 1f : Time.unscaledDeltaTime / morphSeconds;
            _morph = Mathf.MoveTowards(_morph, target, step);
            ApplyMorph(_morph);
        }
    }

    // ── Morph ────────────────────────────────────────────────────────
    void ApplyMorph(float m)
    {
        // Eased so the snap feels crisp without being instant.
        float e = m * m * (3f - 2f * m);   // smoothstep

        if (_triGroup != null) _triGroup.alpha = 1f - e;
        if (_sqGroup  != null) _sqGroup.alpha  = e;

        if (_triRT != null)
        {
            _triRT.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 40f, e));
            float s = Mathf.Lerp(1f, 0.78f, e);
            _triRT.localScale = new Vector3(s, s, 1f);
        }
        if (_sqRT != null)
        {
            _sqRT.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-25f, 0f, e));
            float s = Mathf.Lerp(1.18f, 1f, e);
            _sqRT.localScale = new Vector3(s, s, 1f);
        }
    }

    // ── Build ────────────────────────────────────────────────────────
    void Build()
    {
        float r = SqRadius * scale;
        float tr = TriRadius * scale;
        float L = ArmLen * scale;
        float t = Mathf.Max(1f, thickness * scale);

        // Square frame (active) — 4 L-shaped corners.
        _sqRT = NewGroup("SquareFrame", out _sqGroup);
        AddCorner(_sqRT, new Vector2(-r,  r),  1f, -1f, L, t); // top-left
        AddCorner(_sqRT, new Vector2( r,  r), -1f, -1f, L, t); // top-right
        AddCorner(_sqRT, new Vector2(-r, -r),  1f,  1f, L, t); // bottom-left
        AddCorner(_sqRT, new Vector2( r, -r), -1f,  1f, L, t); // bottom-right

        // Triangle frame (idle) — 3 chevrons, points up.
        _triRT = NewGroup("TriangleFrame", out _triGroup);
        Vector2 vTop = new Vector2(0f,  tr);
        Vector2 vBL  = new Vector2(-tr * 0.866f, -tr * 0.5f);
        Vector2 vBR  = new Vector2( tr * 0.866f, -tr * 0.5f);
        AddChevron(_triRT, vTop, vBL, vBR, L, t);
        AddChevron(_triRT, vBL, vTop, vBR, L, t);
        AddChevron(_triRT, vBR, vTop, vBL, L, t);

        // Center pip — always visible.
        if (showPip)
        {
            var pip = NewArm(transform as RectTransform, "Pip");
            float d = PipRadius * 2f * scale;
            pip.sizeDelta = new Vector2(d, d);
            pip.anchoredPosition = Vector2.zero;
        }
    }

    // A corner bracket: a horizontal arm + a vertical arm meeting at `corner`.
    // dirH / dirV (±1) point the arms back toward the frame's open center.
    void AddCorner(RectTransform parent, Vector2 corner, float dirH, float dirV, float L, float t)
    {
        var h = NewArm(parent, "ArmH");
        h.sizeDelta = new Vector2(L, t);
        h.anchoredPosition = corner + new Vector2(dirH * (L - t) * 0.5f, 0f);

        var v = NewArm(parent, "ArmV");
        v.sizeDelta = new Vector2(t, L);
        v.anchoredPosition = corner + new Vector2(0f, dirV * (L - t) * 0.5f);
    }

    // A chevron at triangle vertex `v`: two arms angled along the edges toward
    // the two neighbouring vertices.
    void AddChevron(RectTransform parent, Vector2 v, Vector2 n1, Vector2 n2, float L, float t)
    {
        AddChevronArm(parent, v, n1, L, t);
        AddChevronArm(parent, v, n2, L, t);
    }

    void AddChevronArm(RectTransform parent, Vector2 v, Vector2 neighbour, float L, float t)
    {
        Vector2 dir = (neighbour - v).normalized;
        var arm = NewArm(parent, "Arm");
        arm.sizeDelta = new Vector2(L, t);
        arm.anchoredPosition = v + dir * (L * 0.5f);
        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        arm.localRotation = Quaternion.Euler(0f, 0f, ang);
    }

    // ── UI plumbing ──────────────────────────────────────────────────
    RectTransform NewGroup(string name, out CanvasGroup group)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.hideFlags = HideFlags.DontSave;
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        Center(rt);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        group = go.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        return rt;
    }

    RectTransform NewArm(RectTransform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.hideFlags = HideFlags.DontSave;
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        Center(rt);
        var img = go.AddComponent<Image>();   // null sprite => crisp white quad
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    static void Center(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    // ── Tunables (appended at END — CLAUDE.md serialization convention) ──
    [Header("Reticle")]
    [Tooltip("Bracket / pip color.")]
    public Color color = new Color(0.749f, 0.914f, 1f, 1f);   // #BFE9FF
    [Tooltip("Overall size multiplier on all geometry.")]
    public float scale = 12f;
    [Tooltip("Arm thickness in reference-canvas px (before scale).")]
    public float thickness = 2f;
    [Tooltip("Seconds for the idle<->lock-on morph.")]
    public float morphSeconds = 0.2f;
    [Tooltip("Draw the small center dot.")]
    public bool showPip = true;

    [Header("Look-to-Interact")]
    [Tooltip("Require the player to look at an object (crosshair raycast) before its prompt shows, the reticle morphs, and F works. Off = old radius-only behaviour.")]
    public bool requireLookToInteract = true;
    [Tooltip("Fatness of the crosshair aim-ray in world units. 0 = razor-thin (must be dead-on); ~0.1 gives a little forgiveness without feeling loose.")]
    [Range(0f, 0.5f)]
    public float aimRadius = 0.10f;
}
