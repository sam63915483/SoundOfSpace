using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Blade-edge contact for the physics-axe spike (M2). The blade IS the hitbox:
/// 3 sample points along the edge are swept with between-frame SphereCasts —
/// no collider on the axe itself.
///
/// A hit only registers above minEdgeSpeed: resting the blade on a tree does
/// nothing (a quiet scrape below the gate, per Sam's call). Trees/crystals
/// keep INTEGER chops routed into the exact receivers the classic swing used
/// (SpawnedTree.TakeDamage / SpawnedCrystal.TakeDamage) so drops, planet-O2
/// recount, and persistence are untouched. Enemies are M3 — not handled here.
///
/// Floating-origin safety: previous-frame edge positions are cached in
/// CAMERA-local space and re-projected through the camera's CURRENT world
/// pose, so an origin shift between frames cannot fake a supersonic swing.
///
/// Ticked explicitly by AxeSwing.LateUpdate after the swing pose is applied.
/// </summary>
public class BladeSweep : MonoBehaviour
{
    [Header("Edge geometry (local to the axe model instance)")]
    [Tooltip("Measure the axe model's renderer bounds at equip and lay the samples from grip to head automatically (recommended — the model's authoring axes are nonstandard, so hand-guessed points miss the visual blade). Off = use edgeLocalPoints/bladeRadius as authored.")]
    public bool autoComputeFromBounds = true;
    [Tooltip("Manual sample points along the blade edge, local to the spawned axe model. Only used when autoComputeFromBounds is off. Nudge with drawDebug on until the gizmo line hugs the edge.")]
    public Vector3[] edgeLocalPoints = new Vector3[]
    {
        new Vector3(0f, 0.45f, 0.10f),
        new Vector3(0f, 0.62f, 0.16f),
        new Vector3(0f, 0.78f, 0.22f),
    };
    [Tooltip("Manual SphereCast radius (m). Only used when autoComputeFromBounds is off.")]
    public float bladeRadius = 0.06f;
    [Tooltip("Draw the edge samples + sweep paths in the Scene view (spike build).")]
    public bool drawDebug = true;

    [Header("Hit rules (wind-up arming — speed does NOT gate damage)")]
    [Tooltip("Edge speed (m/s) that triggers the whoosh sound and scales hit feedback. Damage is gated by the wind-up arm (AxeSwing), not by speed.")]
    public float minEdgeSpeed = 2.5f;
    [Tooltip("Edge speed (m/s) above which UNARMED contact makes a scrape sound.")]
    public float scrapeMinSpeed = 0.4f;
    [Tooltip("Fraction of a charged hit an UNCHARGED swing deals. Trees keep integer chops, so this accumulates per target and lands a real chop when the pool fills (1/3 → three uncharged swings = one chop). Must still be a real swing — edge speed above minEdgeSpeed — so parking the blade in a tree stays harmless.")]
    public float unchargedHitFraction = 1f / 3f;
    [Tooltip("Seconds between uncharged damage ticks on the same target — one per swing-through, not one per frame.")]
    public float unchargedHitCooldown = 0.6f;
    [Tooltip("Chops a FULLY charged swing deals (the crosshair bar at green). A just-armed swing deals 1; in between scales linearly, fractions pooling per target.")]
    public float fullChargeChops = 2f;
    [Tooltip("Seconds between scrape sounds on the same target.")]
    public float scrapeCooldown = 0.4f;
    [Tooltip("Edge speed at (and past) which hit feedback maxes out — pitch, shake, hit-stop scaling.")]
    public float maxFeedbackSpeed = 8f;

    [Header("Feel hooks (M2 scope — no polish pass)")]
    [Tooltip("Hit-stop duration (s, realtime) on a connecting chop. Doc: 30–60 ms.")]
    public float hitStopDuration = 0.045f;
    [Tooltip("Time.timeScale during the hit-stop.")]
    public float hitStopScale = 0.05f;
    [Tooltip("Camera micro-shake magnitude at maxFeedbackSpeed (uses CameraShake, scaled by edge speed).")]
    public float hitShakeMagnitude = 0.12f;

    /// <summary>Fired when an armed contact lands — AxeSwing disarms until the next wind-up.</summary>
    public System.Action OnHitLanded;

    AxeController _axe;
    Transform _blade;               // spawned axe model instance
    Transform _cam;
    AudioSource _audio;

    Vector3[] _samples;             // blade-local sample points actually used
    float _radius;                  // sweep radius actually used
    Vector3[] _prevCamLocal;        // last frame's edge samples, camera-local
    bool _hasPrev;
    bool _whooshArmed = true;
    float _lastWhooshTime;
    float _lastEdgeSpeed;
    float _currentCharge;           // 0..1 charge of the in-flight swing, from AxeSwing
    readonly Dictionary<int, float> _lastScrapeTime = new Dictionary<int, float>();
    readonly Dictionary<int, float> _lastUnchargedHitTime = new Dictionary<int, float>();
    readonly Dictionary<int, float> _unchargedDamagePool = new Dictionary<int, float>();   // fractional chops per target
    Coroutine _hitStop;

    public float LastEdgeSpeed => _lastEdgeSpeed;

    // Exposed for AxeSwing's ground-clearance pass — the calibrated edge
    // samples double as the axe's collision probe points.
    public Transform Blade => _blade;
    public Vector3[] SampleLocalPoints => _samples;
    public float SampleRadius => _radius;

    /// World-space displacement applied to the whole axe this frame by
    /// something other than swinging (the ground-clearance lift). Excluded
    /// from edge-speed measurement so a clearance bounce can't read as a
    /// swing — no phantom whooshes/scrapes. Set by AxeSwing each frame.
    [System.NonSerialized] public Vector3 ExternalMotion;

    public void Attach(Transform bladeInstance, AxeController axe)
    {
        _blade = bladeInstance;
        _axe = axe;
        var cam = bladeInstance != null ? bladeInstance.GetComponentInParent<Camera>() : null;
        _cam = cam != null ? cam.transform : null;

        _samples = edgeLocalPoints;
        _radius = bladeRadius;
        if (autoComputeFromBounds && _blade != null) ComputeSamplesFromBounds();
        // Dedicated source: we pitch-shift per hit, and doing that on the shared
        // AxeController source would warp any equip/swing clip already playing.
        if (_audio == null)
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
        }
        _hasPrev = false;
        _lastScrapeTime.Clear();
        _lastUnchargedHitTime.Clear();
        _unchargedDamagePool.Clear();
    }

    public void Detach(Transform bladeInstance)
    {
        if (_blade == bladeInstance) _blade = null;
        _hasPrev = false;
    }

    /// <summary>Called by AxeSwing after the swing pose is applied this frame.
    /// charge = 0..1 wind-up charge of the in-flight swing (scales damage).</summary>
    public void Tick(float dt, bool armed, float charge = 0f)
    {
        _currentCharge = charge;
        if (_blade == null || _cam == null || dt <= 0f) { _hasPrev = false; return; }
        if (_samples == null || _samples.Length == 0) { _samples = edgeLocalPoints; _radius = bladeRadius; }
        int n = _samples.Length;
        if (n == 0) return;
        if (_prevCamLocal == null || _prevCamLocal.Length != n) { _prevCamLocal = new Vector3[n]; _hasPrev = false; }

        float fastest = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 cur = _blade.TransformPoint(_samples[i]);
            if (_hasPrev)
            {
                Vector3 prev = _cam.TransformPoint(_prevCamLocal[i]);   // origin-shift safe
                Vector3 move = cur - prev;
                float dist = move.magnitude;
                // Speed from swing motion only — the clearance lift moves the
                // whole axe and must not register as a swing.
                float speed = (move - ExternalMotion).magnitude / dt;
                if (speed > fastest) fastest = speed;

                // Armed: any contact counts, however slow. Unarmed: cast only
                // fast enough motion, for the scrape sound.
                if (dist > 0.0008f && (armed || speed >= scrapeMinSpeed))
                {
                    if (CastSegment(prev, move / dist, dist, speed, armed))
                        armed = false;   // hit landed — remaining samples this frame scrape at most
                }

                if (drawDebug) Debug.DrawLine(prev, cur, armed ? Color.red : Color.yellow, 0.25f);
            }
            _prevCamLocal[i] = _cam.InverseTransformPoint(cur);
        }
        _hasPrev = true;
        _lastEdgeSpeed = fastest;

        // Whoosh when the edge moves fast; re-arms once it slows down, with a
        // hard floor between whooshes so borderline speeds can't machine-gun it.
        if (_whooshArmed && fastest >= minEdgeSpeed && Time.time - _lastWhooshTime >= 0.3f)
        {
            _whooshArmed = false;
            _lastWhooshTime = Time.time;
            if (_audio != null && _axe != null && _axe.SwingClip != null)
            {
                _audio.pitch = Mathf.Lerp(0.95f, 1.25f, Mathf.Clamp01(fastest / Mathf.Max(0.01f, maxFeedbackSpeed)));
                _audio.PlayOneShot(_axe.SwingClip, _axe.SwingVolume);
            }
        }
        if (fastest < minEdgeSpeed * 0.6f) _whooshArmed = true;
    }

    // Measure the spawned axe model and lay the sweep samples along its real
    // grip→head axis, with a radius sized to the real head. Immune to the
    // model's nonstandard authoring axes — works purely from rendered bounds.
    void ComputeSamplesFromBounds()
    {
        var renderers = _blade.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

        Vector3 grip = _blade.parent != null ? _blade.parent.position : _blade.position;
        Vector3 toCenter = b.center - grip;
        if (toCenter.sqrMagnitude < 1e-6f) return;
        Vector3 dir = toCenter.normalized;

        // Head = far end of the bounds along the grip→center axis.
        float extentAlong = Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.y) * b.extents.y + Mathf.Abs(dir.z) * b.extents.z;
        Vector3 headCenter = b.center + dir * (extentAlong * 0.5f);
        float length = Vector3.Distance(grip, headCenter);
        if (length < 0.1f) return;

        _samples = new Vector3[]
        {
            _blade.InverseTransformPoint(grip + dir * (length * 0.55f)),
            _blade.InverseTransformPoint(grip + dir * (length * 0.80f)),
            _blade.InverseTransformPoint(headCenter),
        };

        // Radius from the average cross-section (the two smaller half-extents).
        float maxExtent = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
        float crossSection = (b.extents.x + b.extents.y + b.extents.z - maxExtent) * 0.5f;
        _radius = Mathf.Clamp(crossSection * 0.5f, 0.08f, 0.22f);
    }

    // Returns true when an armed hit landed (one hit per wind-up — the
    // OnHitLanded callback disarms AxeSwing).
    bool CastSegment(Vector3 origin, Vector3 dir, float dist, float speed, bool armed)
    {
        var hits = Physics.SphereCastAll(origin, _radius, dir, dist, ~0, QueryTriggerInteraction.Ignore);
        for (int h = 0; h < hits.Length; h++)
        {
            var col = hits[h].collider;
            if (col == null) continue;
            if (col.GetComponentInParent<PlayerController>() != null) continue;   // never self-hit

            var tree = col.GetComponentInParent<SpawnedTree>();
            var crystal = tree == null ? col.GetComponentInParent<SpawnedCrystal>() : null;
            if (tree == null && crystal == null) continue;
            if (tree != null && tree.IsDead) continue;
            if (crystal != null && crystal.IsDead) continue;

            int id = tree != null ? tree.GetInstanceID() : crystal.GetInstanceID();
            if (armed)
            {
                // Damage scales with wind-up charge: just-armed = 1 chop,
                // full bar = fullChargeChops. Fractions pool per target so the
                // integer tree pipeline (drops/O2/saves) never sees a partial.
                float chops = Mathf.Lerp(1f, Mathf.Max(1f, fullChargeChops), Mathf.Clamp01(_currentCharge));
                _unchargedDamagePool.TryGetValue(id, out float pool);
                pool += chops * _axe.damagePerSwing;
                int apply = (int)pool;
                pool -= apply;
                _unchargedDamagePool[id] = pool;
                if (apply > 0)
                {
                    if (tree != null) tree.TakeDamage(apply);
                    else crystal.TakeDamage(apply);
                }
                HitFeedback(speed);
                OnHitLanded?.Invoke();
                return true;
            }

            // Uncharged but genuinely swinging: partial damage. Fractions pool
            // per target and convert to a real integer chop when full, so the
            // tree pipeline (drops/O2/saves) never sees a fraction.
            if (speed >= minEdgeSpeed && unchargedHitFraction > 0f)
            {
                UnchargedHit(tree, crystal, id, speed);
                continue;
            }

            Scrape(id);
        }
        return false;
    }

    void UnchargedHit(SpawnedTree tree, SpawnedCrystal crystal, int id, float speed)
    {
        float now = Time.time;
        if (_lastUnchargedHitTime.TryGetValue(id, out float last) && now - last < unchargedHitCooldown) return;
        _lastUnchargedHitTime[id] = now;

        _unchargedDamagePool.TryGetValue(id, out float pool);
        pool += unchargedHitFraction;
        if (pool >= 1f)
        {
            pool -= 1f;
            if (tree != null) tree.TakeDamage(_axe.damagePerSwing);
            else if (crystal != null) crystal.TakeDamage(_axe.damagePerSwing);
        }
        _unchargedDamagePool[id] = pool;

        // Lighter feedback than a charged hit: a dull knock, small rumble,
        // no hit-stop, no camera shake.
        if (_audio != null && _axe != null && _axe.HitClip != null)
        {
            _audio.pitch = 0.8f;
            _audio.PlayOneShot(_axe.HitClip, _axe.HitVolume * 0.45f);
        }
        GamepadRumble.Pulse(0.25f, 0.12f, 0.1f);
    }

    void HitFeedback(float speed)
    {
        float t = Mathf.Clamp01(speed / Mathf.Max(0.01f, maxFeedbackSpeed));

        if (_audio != null && _axe != null && _axe.HitClip != null)
        {
            _audio.pitch = Mathf.Lerp(0.9f, 1.3f, t);
            _audio.PlayOneShot(_axe.HitClip, _axe.HitVolume);
        }

        GamepadRumble.Pulse(0.6f * t + 0.2f, 0.35f * t + 0.1f, 0.15f);
        if (CameraShake.Instance != null)
            CameraShake.Instance.TriggerShake(0.06f, hitShakeMagnitude * t, 5f);

        // One hit-stop at a time — restarting mid-dip would see timeScale != 1
        // and bail, leaving the game stuck slow.
        if (hitStopDuration > 0f && _hitStop == null) _hitStop = StartCoroutine(HitStop());
    }

    IEnumerator HitStop()
    {
        // Only dip from normal speed — never fight pause menus or cutscene scaling.
        if (!Mathf.Approximately(Time.timeScale, 1f)) yield break;
        Time.timeScale = hitStopScale;
        yield return new WaitForSecondsRealtime(hitStopDuration);
        if (Mathf.Approximately(Time.timeScale, hitStopScale)) Time.timeScale = 1f;
        _hitStop = null;
    }

    void Scrape(int targetId)
    {
        if (_audio == null || _axe == null || _axe.HitClip == null) return;
        float now = Time.time;
        if (_lastScrapeTime.TryGetValue(targetId, out float last) && now - last < scrapeCooldown) return;
        _lastScrapeTime[targetId] = now;
        // Placeholder scrape: the hit clip, slow and quiet. Swap in a real
        // scrape clip on AxeController later if the spike graduates.
        _audio.pitch = 0.55f;
        _audio.PlayOneShot(_axe.HitClip, _axe.HitVolume * 0.25f);
    }
}
