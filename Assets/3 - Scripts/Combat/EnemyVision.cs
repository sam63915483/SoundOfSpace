using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-enemy perception for the stealth revamp. A wide forward view cone + LOS raycast, with a
/// suspicion meter that fills FASTER when the player is centered in the cone and SLOWER toward
/// the edges (center = centerSpotSeconds, edge = edgeSpotSeconds). Sprinting multiplies the fill
/// RATE (it does not instantly alert). While the owning enemy is Chasing/Searching the cone reaches
/// further (chaseRangeMult) so the player can't simply out-run or slip behind it. Read by
/// <see cref="EnemyController"/>. Also renders the red translucent 3D debug cone.
/// </summary>
public class EnemyVision : MonoBehaviour
{
    public static readonly List<EnemyVision> AllInstances = new List<EnemyVision>();
    public static bool ShowDebugCones = false;   // dev-only; toggled with K (EnemyDetectionHUD)

    [Header("View cone")]
    public float viewRange = 30f;
    public float viewHalfAngleDeg = 55f;       // 110° total — wide, still a rear blind spot
    public float eyeHeight = 1.4f;
    [Tooltip("BICONE vision: the volume is two cones base-to-base — widest at this fraction of viewRange, tapering to a POINT at full range straight ahead. So max sight distance is dead-center only; the outer angles see much shorter. (Real eyes: sharp far vision only in the middle.)")]
    public float bulgeFraction = 0.45f;
    [Tooltip("Range multiplier while the enemy is Chasing/Searching — sees further, harder to lose.")]
    public float chaseRangeMult = 2f;

    [Header("Detection timing")]
    [Tooltip("Seconds to get spotted when dead-CENTER in the cone (walking).")]
    public float centerSpotSeconds = 2f;
    [Tooltip("Seconds to get spotted at the very EDGE of the cone (walking).")]
    public float edgeSpotSeconds = 4f;
    [Tooltip("Sprinting multiplies the fill RATE by this — it does NOT instantly alert.")]
    public float sprintFillMult = 1.5f;
    [Tooltip("Suspicion fills this much faster while this enemy is Searching — it was just hunting you and thinks it sees you. Re-spotting is NOT instant, just quick.")]
    public float searchFillMult = 2f;
    public float suspicionDecayPerSec = 1.5f;

    // ── Perception outputs ──
    public bool CanSeePlayerNow { get; private set; }
    /// <summary>
    /// WHICH player this enemy can currently see, or null for nobody.
    ///
    /// The whole reason this exists: perception used to evaluate against the
    /// NEAREST player only. Two co-op players stand near each other, so for most
    /// enemies the nearest was the host — and the guest was not merely hard to
    /// spot, they were never a CANDIDATE. They could stand in front of an alien
    /// and be looked straight through, which is exactly what the first playtest
    /// reported. Sight now considers everybody; the target is whoever is actually
    /// seen.
    /// </summary>
    public Transform VisibleTarget { get; private set; }
    public float Suspicion01 { get; private set; }
    public bool IsAlerted => Suspicion01 >= 1f;
    public bool HasLastSeen { get; private set; }
    public float SpottingSince { get; private set; } = float.MaxValue;

    // Last-seen is stored PLANET-RELATIVE and converted back on read: the planet orbits
    // (and the floating origin shifts) between the sighting and the search that consumes
    // this, so a world-absolute point would drift by planet.velocity × elapsed time and
    // send the searcher up the planet's motion trail. Same convention as the wander leash.
    public Vector3 LastSeenPlayerPos =>
        _lastSeenPlanet != null ? _lastSeenPlanet.transform.TransformPoint(_lastSeenLocal)
                                : _lastSeenWorldFallback;
    CelestialBody _lastSeenPlanet;
    Vector3 _lastSeenLocal;
    Vector3 _lastSeenWorldFallback;
    CelestialBody _planet;   // cached lazily — SetParent(planet) happens after Instantiate/Awake

    void RecordLastSeen(Vector3 worldPos)
    {
        if (_planet == null) _planet = GetComponentInParent<CelestialBody>();
        _lastSeenPlanet = _planet;
        if (_planet != null) _lastSeenLocal = _planet.transform.InverseTransformPoint(worldPos);
        else _lastSeenWorldFallback = worldPos;
        HasLastSeen = true;
    }

    bool _wasSeeing;
    float _lastEdgeFrac;              // 0 = dead-center of the vision volume, 1 = at its boundary (last sighting)
    Transform _player;
    EnemyController _owner;
    float _nextLosCheck;
    const float LosInterval = 0.15f;
    static readonly int LosMask = ~((1 << 9) | (1 << 11) | (1 << 12));

    Transform _coneTf;
    MeshRenderer _coneRend;
    static Mesh _coneMesh;
    static Material _coneMat;

    void OnEnable() { if (!AllInstances.Contains(this)) AllInstances.Add(this); }
    void OnDisable() { AllInstances.Remove(this); }
    void Awake() { _owner = GetComponent<EnemyController>(); }

    float EffectiveRange()
    {
        if (_owner != null && (_owner.State == EnemyController.AIState.Chasing
                            || _owner.State == EnemyController.AIState.Searching))
            return viewRange * chaseRangeMult;
        return viewRange;
    }

    void Update()
    {
        if (Time.time >= _nextLosCheck)
        {
            _nextLosCheck = Time.time + LosInterval;
            ScanForPlayers();
        }

        if (CanSeePlayerNow)
        {
            // Center → fast (centerSpotSeconds), boundary → slow (edgeSpotSeconds).
            float spotSeconds = Mathf.Lerp(centerSpotSeconds, edgeSpotSeconds, _lastEdgeFrac);
            float rate = 1f / Mathf.Max(0.05f, spotSeconds);
            if (_targetSprinting) rate *= sprintFillMult;
            if (_owner != null && _owner.State == EnemyController.AIState.Searching) rate *= searchFillMult;
            Suspicion01 = Mathf.Min(1f, Suspicion01 + rate * Time.deltaTime);
        }
        else
        {
            Suspicion01 = Mathf.Max(0f, Suspicion01 - suspicionDecayPerSec * Time.deltaTime);
        }

        UpdateDebugCone();
    }

    /// <summary>
    /// Look at EVERYBODY, and see whoever is actually visible.
    ///
    /// ⚠️ This used to test the NEAREST player and nobody else, which in co-op
    /// meant an enemy perceived exactly one person. Two players explore together,
    /// so the nearest was usually the host — and the guest was not just hard to
    /// notice, they were never considered at all. They could walk up to an alien,
    /// stand in its face, sprint past it, and be looked straight through. When
    /// they briefly did become the nearest, the enemy would glance over and then
    /// switch back to the host, which is the "sees me then looks away" symptom.
    ///
    /// Among everyone visible, the NEAREST one wins — that keeps Sam's rule that
    /// stepping in closer pulls a mob off your friend, while making the choice
    /// out of players the enemy can genuinely see rather than out of all of them.
    ///
    /// Suspicion is deliberately NOT reset when the seen player changes. The
    /// meter means "somebody is over there".
    ///
    /// Cost: the bicone test is cheap and rejects almost everyone before the
    /// raycast, so this is one extra ray per VISIBLE player per 0.15 s, on the
    /// host only. Guests run none of it at all.
    /// </summary>
    void ScanForPlayers()
    {
        Transform seen = null, nearestAny = null;
        float seenSqr = float.MaxValue, nearestSqr = float.MaxValue;
        float seenEdge = 0f;
        bool seenSprinting = false;

        var all = PlayerRoster.All();
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i].Transform;
            if (t == null) continue;

            float d2 = (t.position - transform.position).sqrMagnitude;
            if (d2 < nearestSqr) { nearestSqr = d2; nearestAny = t; }

            if (d2 >= seenSqr) continue;             // already have someone closer in view
            if (!CanSee(t, out float edge)) continue;
            seenSqr = d2; seen = t; seenEdge = edge; seenSprinting = all[i].IsSprinting;
        }

        // Kept even with nobody in view: ForceAlert and the investigate facing
        // need someone to point at when an enemy is alerted without seeing.
        _player = nearestAny;

        VisibleTarget   = seen;
        CanSeePlayerNow = seen != null;

        if (seen != null)
        {
            _lastEdgeFrac    = seenEdge;
            _targetSprinting = seenSprinting;
            if (!_wasSeeing) SpottingSince = Time.time;
            RecordLastSeen(seen.position);
        }
        else
        {
            SpottingSince = float.MaxValue;
            _targetSprinting = false;
        }
        _wasSeeing = CanSeePlayerNow;
    }

    bool _targetSprinting;

    /// Can this enemy see `target` right now? `edgeFrac` reports 0 for dead
    /// centre of the vision volume and 1 at its boundary, which is what decides
    /// how fast suspicion fills for them.
    bool CanSee(Transform target, out float edgeFrac)
    {
        edgeFrac = 0f;

        // BICONE containment: express the player in axial coords (z along forward, r off-axis)
        // and test against the diamond profile — radius grows linearly to the bulge (z ≤
        // baseDist: near cone) then shrinks linearly to a point at maxR (far cone). Farthest
        // sight is dead-ahead only; the outer angles reach far shorter.
        Vector3 eye = transform.position + transform.up * eyeHeight;
        Vector3 to = target.position - eye;
        float dist = to.magnitude;
        if (dist < 0.001f) return true;

        float maxR = EffectiveRange();
        float z = Vector3.Dot(to, transform.forward);
        if (z < 0.05f || z > maxR) return false;

        float r = (to - transform.forward * z).magnitude;
        float baseDist = maxR * Mathf.Clamp(bulgeFraction, 0.05f, 0.95f);
        float tanHalf = Mathf.Tan(viewHalfAngleDeg * Mathf.Deg2Rad);
        float allowed = z <= baseDist
            ? z * tanHalf                                              // near cone (opening)
            : baseDist * tanHalf * (maxR - z) / (maxR - baseDist);     // far cone (closing to a point)
        if (allowed < 0.01f || r > allowed) return false;

        Vector3 dir = to / dist;
        const float selfSkip = 1.1f;
        if (dist > selfSkip)
        {
            Vector3 origin = eye + dir * selfSkip;
            // A remote player is collider-less, so this ray simply reaches its
            // full length and reports nothing — which already counts as clear.
            // (Measured: the player root sits at the BODY CENTRE, so the ray runs
            // at chest height and never grazes the ground at their feet. Local
            // and remote come out identical at every range.)
            if (Physics.Raycast(origin, dir, out RaycastHit hit, dist - selfSkip, LosMask, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<PlayerController>() == null) return false;
        }
        edgeFrac = Mathf.Clamp01(r / allowed);
        return true;
    }

    /// <summary>Force full alert (e.g. the enemy was shot by the player).</summary>
    public void ForceAlert()
    {
        Suspicion01 = 1f;
        CanSeePlayerNow = true;
        // An enemy can be alerted (shot, or recruited by a screaming packmate)
        // in the same frame it spawned, before its first Update has resolved a
        // target — without this it would alert with no last-seen point and
        // immediately give up.
        if (_player == null) ScanForPlayers();
        if (_player != null) RecordLastSeen(_player.position);
    }

    /// <summary>
    /// Force full alert toward a SPECIFIC place — a gunshot, whose origin is
    /// known exactly and is not necessarily near whoever this enemy had resolved
    /// as its nearest player. In co-op that distinction is the whole point: a
    /// guest's shot must send them at the guest.
    /// </summary>
    public void ForceAlert(Vector3 heardAt)
    {
        Suspicion01 = 1f;
        CanSeePlayerNow = true;
        RecordLastSeen(heardAt);
    }

    /// <summary>
    /// Co-op: push the HOST's perception of THIS machine's player onto a puppet.
    ///
    /// This component is disabled on a puppet — the whole point is that a guest
    /// stops paying for bicone tests and LOS raycasts. But EnemyDetectionHUD
    /// reads Suspicion01 and CanSeePlayerNow to draw the meter that fills while
    /// you are being noticed, and with nothing driving them a guest would creep
    /// around with no feedback at all and reasonably conclude stealth was broken.
    /// A disabled component's fields are still perfectly readable, so the HUD
    /// works unchanged once these are fed.
    ///
    /// Only ever called with the host's suspicion about the LOCAL player. An
    /// enemy busy noticing the other player must not light up our HUD.
    /// </summary>
    public void ApplyNetworkPerception(float suspicion, bool canSee)
    {
        Suspicion01 = Mathf.Clamp01(suspicion);
        CanSeePlayerNow = canSee;
    }

    /// <summary>Show/hide the debug cone renderer (e.g. hidden permanently on death).</summary>
    public void SetConeVisible(bool visible)
    {
        if (_coneRend != null) _coneRend.enabled = visible;
    }

    // ── Debug cone (a real 3D cone; scales with the current effective range) ──
    void UpdateDebugCone()
    {
        if (!ShowDebugCones)
        {
            if (_coneRend != null && _coneRend.enabled) _coneRend.enabled = false;
            return;
        }
        if (_coneTf == null) BuildDebugCone();
        if (_coneRend != null && !_coneRend.enabled) _coneRend.enabled = true;
        if (_coneTf != null)
        {
            // Uniform scale keeps the apex angle while stretching the cone to the current range,
            // so it visibly lengthens when the enemy goes into chase/search.
            _coneTf.localScale = Vector3.one * (EffectiveRange() / Mathf.Max(0.01f, viewRange));
        }
    }

    void BuildDebugCone()
    {
        if (_coneMesh == null) _coneMesh = BuildBiconeMesh(viewHalfAngleDeg, viewRange, bulgeFraction, 22);
        if (_coneMat == null)
        {
            _coneMat = new Material(Shader.Find("Sprites/Default"));
            _coneMat.color = new Color(1f, 0.15f, 0.15f, 1f);
            // Transparent-queue gotcha (CLAUDE.md): Sprites/Default sits at queue 3000, which
            // renders AFTER the [ImageEffectOpaque] atmosphere/ocean pass — cones would glow
            // through the atmosphere from space. ≤2500 keeps them hidden behind it.
            _coneMat.renderQueue = 2450;
        }
        var go = new GameObject("DebugViewCone");
        _coneTf = go.transform;
        _coneTf.SetParent(transform, false);
        _coneTf.localPosition = new Vector3(0f, eyeHeight, 0f);   // apex at the eye
        _coneTf.localRotation = Quaternion.identity;             // opens along local +Z (forward)
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = _coneMesh;
        _coneRend = go.AddComponent<MeshRenderer>();
        _coneRend.sharedMaterial = _coneMat;
        _coneRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _coneRend.receiveShadows = false;
    }

    // BICONE (diamond/lens): near cone apex at origin (eye) opening along +Z to the widest
    // rim at z = range × bulge, then a far cone closing back to a POINT at z = range. Matches
    // the ComputeCanSee profile exactly — what you see is what they see.
    static Mesh BuildBiconeMesh(float halfAngleDeg, float range, float bulge, int segments)
    {
        segments = Mathf.Max(6, segments);
        float baseDist = range * Mathf.Clamp(bulge, 0.05f, 0.95f);
        float baseR = baseDist * Mathf.Tan(halfAngleDeg * Mathf.Deg2Rad);
        var verts = new Vector3[segments + 2];
        var cols = new Color[segments + 2];
        var red = new Color(1f, 0.15f, 0.15f, 0.20f);
        verts[0] = Vector3.zero;                      // near apex (the eye)
        for (int i = 0; i < segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            verts[1 + i] = new Vector3(Mathf.Cos(a) * baseR, Mathf.Sin(a) * baseR, baseDist);
        }
        int farTip = segments + 1;
        verts[farTip] = new Vector3(0f, 0f, range);   // far apex (max range, dead ahead)
        for (int i = 0; i < verts.Length; i++) cols[i] = red;

        var tris = new int[segments * 6];
        int t = 0;
        for (int i = 0; i < segments; i++)
        {
            int a = 1 + i, b = 1 + (i + 1) % segments;
            tris[t++] = 0; tris[t++] = a; tris[t++] = b;        // near cone side
            tris[t++] = farTip; tris[t++] = b; tris[t++] = a;   // far cone side
        }
        var m = new Mesh { name = "EnemyViewBicone" };
        m.vertices = verts;
        m.colors = cols;
        m.triangles = tris;
        m.RecalculateBounds();
        return m;
    }
}
