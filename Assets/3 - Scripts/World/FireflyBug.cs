using UnityEngine;

/// <summary>
/// One wild firefly. Flies inside its <see cref="FireflySwarm"/>'s volume, blinks
/// on its own rhythm, and is an <see cref="Interactable"/> so the ordinary
/// look-at + F prompt catches it ("Press F to catch firefly").
///
/// ── No colliders, on purpose ──────────────────────────────────────────────
/// A hundred moving trigger volumes would cost physics for nothing: the gaze
/// system already resolves collider-less props through their rendered
/// silhouette (the cats work the same way), and "in range" is one distance
/// check per frame here instead of OnTriggerEnter/Exit. The gaze target is the
/// BODY mesh only — the halo quad is bigger than GazeHighlight's outline
/// threshold and would otherwise get a green square drawn round it; the body
/// is below it, so a bug gets no rim. Its highlight is instead being pinned at
/// full glow while it is the focused one (see below).
///
/// ── One focused bug ───────────────────────────────────────────────────────
/// Several bugs of a swarm can sit inside the crosshair at once, and each
/// Interactable handles F independently — so without a tie-break one press
/// could catch two, and the prompt would flip owner every frame. The spawner
/// elects ONE <see cref="Focused"/> bug per frame (nearest to the crosshair
/// among those in catch range) and only that one can own the prompt or be
/// caught: the bug whose prompt is on screen is the bug you get.
///
/// Movement runs in the swarm's planet-local frame (the swarm root is parented
/// to the CelestialBody, +Y = radial up), so orbit and floating-origin shifts
/// are free. The swarm ticks its bugs; this class's own Update only does the
/// interaction half and returns immediately when the player is out of range.
/// </summary>
public class FireflyBug : Interactable
{
    /// The one bug allowed to prompt / be caught this frame. Set by FireflySpawner.
    public static FireflyBug Focused;

    public FireflySwarm Swarm { get; private set; }
    public MeshRenderer Body { get; private set; }
    public MeshRenderer Halo { get; private set; }
    public bool Caught { get; private set; }

    // blink identity (set once per spawn; the shader reads the same numbers)
    float _phase, _period, _floor;
    float _appliedGaze = -1f, _appliedFade = -1f;
    MaterialPropertyBlock _block;

    // flight
    Vector3 _target;      // swarm-local
    Vector3 _vel;         // swarm-local, m/s
    float _speed;
    float _retargetAt;
    float _wobbleSeed;
    int _rayMask;

    // the real light, only on the few nearest bugs
    Light _lamp;
    bool _lit;
    float _lampIntensity;

    /// <summary>Wire the renderers built by FireflyVisual. Called once, right
    /// after the object is created; pooled bugs keep them.</summary>
    public void Bind(MeshRenderer body, MeshRenderer halo)
    {
        Body = body;
        Halo = halo;
        if (_block == null) _block = new MaterialPropertyBlock();
        // Gaze on the body only — see the class comment.
        gazeTarget = body != null ? body.transform : transform;
        // A 20 cm target that never stops moving: hold a yes a little longer
        // than the default so the prompt does not strobe as it drifts across
        // the crosshair. Only ever extends a yes.
        gazeLatchSeconds = 0.25f;
    }

    /// <summary>(Re)start this bug inside <paramref name="swarm"/>. Everything
    /// per-spawn is reset here so a pooled object cannot carry its last life.</summary>
    public void Spawn(FireflySwarm swarm, Vector3 localPos, float phase, float period,
                      float floor, float speed, int rayMask)
    {
        Swarm = swarm;
        Caught = false;
        playerInInteractionZone = false;
        _phase = phase; _period = period; _floor = floor;
        _speed = speed;
        _rayMask = rayMask;
        _wobbleSeed = phase * 37.1f;
        transform.localPosition = localPos;
        transform.localRotation = Quaternion.Euler(0f, phase * 360f, 0f);
        _vel = transform.localRotation * Vector3.forward * (_speed * 0.5f);
        _retargetAt = -1f;            // pick a target on the first tick
        _appliedGaze = -1f; _appliedFade = -1f;
        SetLit(false, 0f, 0f, 0f);
        ApplyVisual(0f, swarm != null ? swarm.Fade : 1f);
    }

    // ── flight ──────────────────────────────────────────────────────────

    /// Called by the swarm every frame. <paramref name="time"/> is
    /// Time.timeSinceLevelLoad, the shader's clock.
    public void Tick(float dt, float time, float fade)
    {
        var swarm = Swarm;
        if (swarm == null) return;

        Vector3 pos = transform.localPosition;
        Vector3 toT = _target - pos;
        float dist = toT.magnitude;
        if (_retargetAt < 0f || time >= _retargetAt || dist < 0.35f)
        {
            PickTarget(swarm, time);
            toT = _target - pos;
            dist = toT.magnitude;
        }

        // Smooth steering toward the target, plus a small figure-of-eight
        // wobble so the path never reads as a straight line.
        Vector3 desired = dist > 0.001f ? toT / dist * _speed : Vector3.zero;
        float k = 1f - Mathf.Exp(-3f * dt);
        _vel = Vector3.Lerp(_vel, desired, k);

        Vector3 side = Vector3.Cross(Vector3.up, _vel);
        if (side.sqrMagnitude > 0.0001f) side.Normalize();
        float w1 = Mathf.Sin(time * 6.1f + _wobbleSeed) * 0.22f;
        float w2 = Mathf.Sin(time * 4.3f + _wobbleSeed * 1.7f) * 0.14f;
        Vector3 step = (_vel + side * w1 + Vector3.up * w2) * dt;
        pos += step;
        transform.localPosition = pos;

        // Face the way it flies (tail glows behind), gently.
        if (_vel.sqrMagnitude > 0.0004f)
        {
            Quaternion face = Quaternion.LookRotation(_vel.normalized, Vector3.up);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, face, 1f - Mathf.Exp(-4f * dt));
        }

        // Visual state: gaze pin + swarm fade. Only re-pushes the property
        // block when either actually changed (fades and focus changes are the
        // rare cases; a hundred bugs cruising cost nothing here).
        float gaze = Focused == this ? 1f : 0f;
        if (gaze != _appliedGaze || fade != _appliedFade) ApplyVisual(gaze, fade);

        if (_lit && _lamp != null)
        {
            float level = FireflyVisual.Blink(time, _phase, _period, _floor, gaze) * fade;
            _lamp.intensity = _lampIntensity * level;
        }
    }

    void PickTarget(FireflySwarm swarm, float time)
    {
        // Uniform in AREA over the swarm disc (sqrt), like the ambient fish —
        // a plain radius roll piles bugs into the middle.
        float r = swarm.Radius * Mathf.Sqrt(Random.value);
        float a = Random.value * Mathf.PI * 2f;
        Vector3 t = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        float h = Random.Range(swarm.HeightMin, swarm.HeightMax);

        // Ground under the target: one raycast per re-pick, so the swarm
        // follows the slope instead of a bug flying into a hillside. The
        // anchor sits ON the ground, so a miss just means "anchor height".
        float groundY = 0f;
        Transform root = swarm.transform;
        Vector3 origin = root.TransformPoint(t + Vector3.up * 8f);
        if (Physics.Raycast(origin, -root.up, out RaycastHit hit, 16f, _rayMask, QueryTriggerInteraction.Ignore))
            groundY = root.InverseTransformPoint(hit.point).y;
        t.y = groundY + h;

        _target = t;
        _retargetAt = time + Random.Range(1.5f, 3.5f);
    }

    void ApplyVisual(float gaze, float fade)
    {
        _appliedGaze = gaze;
        _appliedFade = fade;
        FireflyVisual.Apply(Body, Halo, _block, _phase, _period, _floor, gaze, fade);
    }

    // ── the real light ──────────────────────────────────────────────────

    /// <summary>Turn the real point light on or off. Only the spawner's few
    /// nearest bugs are ever lit; the Light is created the first time a bug
    /// earns one and simply disabled afterwards.</summary>
    public void SetLit(bool lit, float intensity, float range, float grassStrength)
    {
        _lit = lit;
        _lampIntensity = intensity;
        if (!lit)
        {
            if (_lamp != null && _lamp.enabled) _lamp.enabled = false;
            return;
        }
        if (_lamp == null)
        {
            _lamp = gameObject.AddComponent<Light>();
            _lamp.type = LightType.Point;
            _lamp.shadows = LightShadows.None;
            _lamp.color = FireflyVisual.GlowColor;
            // Grass never receives real additive lights; the marker feeds
            // this one into the grass shader's faked-light pool. 0.5 is the
            // lantern/torch value = exactly the ground's brightness.
            var marker = gameObject.GetComponent<GrassPointLight>();
            if (marker == null) marker = gameObject.AddComponent<GrassPointLight>();
            marker.grassStrength = grassStrength;
        }
        else
        {
            var marker = gameObject.GetComponent<GrassPointLight>();
            if (marker != null) marker.grassStrength = grassStrength;
        }
        _lamp.range = range;
        _lamp.intensity = intensity;
        _lamp.enabled = true;
    }

    // ── interaction ─────────────────────────────────────────────────────

    protected override void Update()
    {
        var sp = FireflySpawner.Instance;
        bool inZone = !Caught && sp != null && Swarm != null && Swarm.Catchable
                   && (transform.position - sp.ViewerPos).sqrMagnitude < sp.catchRange * sp.catchRange;
        if (inZone != playerInInteractionZone)
        {
            playerInInteractionZone = inZone;
            if (!inZone) GameUI.ClearInteractionPrompt(this);
        }
        // Out of range: nothing in the base Update can apply (no prompt, no F),
        // so skip its per-frame input polling entirely for the other 99 bugs.
        if (!inZone) return;
        base.Update();
    }

    protected override bool CanInteract() => !Caught && Focused == this && Swarm != null && Swarm.Catchable;

    protected override string BuildInteractMessage() => $"Press {PromptGlyphs.Interact} to catch firefly";

    protected override void Interact()
    {
        if (Swarm == null || Caught) return;
        if (Swarm.TryCatch(this)) Caught = true;
    }

    /// The swarm calls this as it hands the object back to the pool.
    public void Release()
    {
        GameUI.ClearInteractionPrompt(this);
        if (Focused == this) Focused = null;
        playerInInteractionZone = false;
        Swarm = null;
        SetLit(false, 0f, 0f, 0f);
    }

    void OnDisable()
    {
        if (Focused == this) Focused = null;
    }
}
