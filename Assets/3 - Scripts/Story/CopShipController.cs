using System;
using UnityEngine;

/// Runtime brain added to the spawned patrol corvette (mission B-1).
/// Fly-in / hold are driven in the target ship's local frame each LateUpdate,
/// so floating-origin shifts are free (the target ship is registered).
/// The chase runs in world space with the corvette registered on EndlessManager.
public class CopShipController : MonoBehaviour
{
    public float flyInSeconds = 2.6f;
    public float standoffDistance = 130f;
    public float standoffHeight = 25f;
    public float shadowDistance = 300f;      // shadowing offset AHEAD of the target during the siren phase
    public float shadowHeight = 35f;
    public float chaseSpeed = 75f;
    public float escapeDistance = 900f;
    public float minChaseDistance = 260f;    // the cop never closes past this — shots stay visible + dodgeable
    public float fleeThreshold = 20f;        // rel speed that counts as "trying to run"
    public float blastInterval = 4f;
    public float calloutLeadSeconds = 1.5f;  // radio bark plays this long before each shot
    public int maxBlasts = 5;
    public float blastRange = 550f;
    public float blastSpeed = 90f;
    public float blastHitRadius = 8f;
    public int hitsToKill = 3;

    enum Mode { Inactive, FlyIn, Hold, AwaitFlee, Chase, Depart }
    Mode _mode = Mode.Inactive;

    Ship _target;
    CelestialBody _anchor;
    Action _onArrived, _onEscaped, _onCaught, _onFleeDetected;
    AudioClip _pursuitClip;

    Vector3 _holdLocal;      // interrogation pose, target-ship local space
    Vector3 _shadowLocal;    // siren-phase shadowing pose, ahead of the target
    Vector3 _restLocal;      // wherever Hold/AwaitFlee should park right now
    Vector3 _flyFrom, _flyTo;
    float _flyT;
    float _floorRamp;        // chase floor eases from the takeover gap up to minChaseDistance
    Vector3 _departLocalDir;
    float _departSpeed;
    Vector3 _chaseRel;       // cop position relative to the ship, world axes
    Vector3 _fleeBaseVel;    // ship rb velocity at chase start (the "stopped" frame)

    Light _spot, _red, _blue;
    Transform _spotPivot;
    AudioSource _siren, _radio;
    float _flashTimer;
    bool _redOn;

    AudioClip[] _callouts;   // escalating warnings, one per shot, in array order
    int _calloutIdx;
    bool _shotArmed;         // callout played, shot queued
    float _fireAt;

    int _fired, _hits, _pending;
    float _nextBlastAt;
    bool _resolved;          // chase finished (escaped or caught)
    EndlessManager _endless;

    /// The corvette streaks in from far AHEAD of the target and settles into a
    /// shadowing pose in front of the windshield (sirens + lights) — the player
    /// sees it arrive through the glass. Call PullInFront() once the target has
    /// (nearly) stopped to close to the interrogation pose.
    public void Init(Ship target, CelestialBody anchor, AudioClip sirenClip, AudioClip[] calloutClips)
    {
        _target = target;
        _anchor = anchor;
        _callouts = calloutClips;
        _endless = FindObjectOfType<EndlessManager>();

        _holdLocal = new Vector3(0f, standoffHeight, standoffDistance);
        _shadowLocal = new Vector3(0f, shadowHeight, shadowDistance);

        _flyFrom = _shadowLocal + new Vector3(0f, 180f, 1400f);
        _flyTo = _shadowLocal;
        transform.position = LocalToWorld(_flyFrom);
        FaceTarget();

        BuildLights();
        if (sirenClip != null)
        {
            _siren = gameObject.AddComponent<AudioSource>();
            _siren.clip = sirenClip;
            _siren.spatialBlend = 0.4f;
            _siren.volume = 0.9f;
            _siren.Play();
        }

        _mode = Mode.FlyIn;
        _flyT = 0f;
    }

    /// Sweep from wherever we are to the interrogation pose dead ahead of the
    /// target's windshield. onArrived fires when parked.
    public void PullInFront(Action onArrived)
    {
        _onArrived = onArrived;
        _flyFrom = WorldToLocal(transform.position);
        _flyTo = _holdLocal;
        _flyT = 0f;
        _mode = Mode.FlyIn;
    }

    /// Cockpit-radio bark: 2D so it reads as coming over the player's own
    /// comms rather than from the corvette's position.
    public void PlayRadio(AudioClip clip)
    {
        if (clip == null) return;
        if (_radio == null)
        {
            _radio = gameObject.AddComponent<AudioSource>();
            _radio.spatialBlend = 0f;
            _radio.volume = 1f;
        }
        _radio.PlayOneShot(clip);
    }

    public void FlyAway()
    {
        if (_mode == Mode.Depart) return;
        _restLocal = WorldToLocal(transform.position);
        _departLocalDir = (Vector3.up * 0.35f + Vector3.forward).normalized;
        _departSpeed = 60f;
        _mode = Mode.Depart;
        if (_siren != null) _siren.Stop();
        Destroy(gameObject, 10f);
    }

    /// Tev's rocket connected: detonate. Fireball + flash + boom + shake, and
    /// the corvette is gone. Ends the chase without invoking escaped/caught —
    /// the mission drives what happens next.
    public void BlowUp(AudioClip boomClip)
    {
        if (_resolved && _mode == Mode.Depart) return;
        _resolved = true;
        if (_siren != null) _siren.Stop();
        SpawnExplosion(transform.position, boomClip);
        if (CameraShake.Instance != null) CameraShake.Instance.TriggerShake(0.9f, 0.8f, 5f);
        Destroy(gameObject);
    }

    static void SpawnExplosion(Vector3 pos, AudioClip boomClip)
    {
        var go = new GameObject("CopShipExplosion");
        go.transform.position = pos;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(15f, 55f);
        main.startSize = new ParticleSystem.MinMaxCurve(4f, 14f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.1f), new Color(1f, 0.85f, 0.3f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 6f;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = new Material(Shader.Find("Particles/Standard Unlit"));
        renderer.material.color = new Color(1f, 0.6f, 0.15f);
        ps.Emit(300);

        var lightGo = new GameObject("Flash");
        lightGo.transform.SetParent(go.transform, false);
        var flash = lightGo.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = new Color(1f, 0.6f, 0.2f);
        flash.range = 900f;
        flash.intensity = 35f;
        go.AddComponent<ExplosionFlashFade>();

        if (boomClip != null)
        {
            // Mostly-2D so the boom lands hard even from 300+ units behind the cockpit.
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0.2f;
            src.PlayOneShot(boomClip, 1f);
        }

        Destroy(go, 5f);
    }

    /// Arms the chase but does NOT move: the corvette keeps holding in front,
    /// watching. The moment the player actually accelerates (rel speed passes
    /// fleeThreshold), it barks pursuitClip over the radio, swings around
    /// behind them, and the real chase begins (onFleeDetected fires — the
    /// mission uses it to start Tev's hidden rocket timer).
    public void StartChase(Action onEscaped, Action onCaught, Action onFleeDetected, AudioClip pursuitClip)
    {
        _onEscaped = onEscaped;
        _onCaught = onCaught;
        _onFleeDetected = onFleeDetected;
        _pursuitClip = pursuitClip;

        Rigidbody rb = _target.Rigidbody;
        _fleeBaseVel = rb != null ? rb.velocity : Vector3.zero;
        _restLocal = WorldToLocal(transform.position);
        _mode = Mode.AwaitFlee;

        if (_siren != null) { _siren.loop = true; _siren.Play(); }
    }

    void LateUpdate()
    {
        if (_target == null || _mode == Mode.Inactive) return;
        float dt = Time.deltaTime;

        FlashLights(dt);

        switch (_mode)
        {
            case Mode.FlyIn:
            {
                _flyT += dt / Mathf.Max(0.1f, flyInSeconds);
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(_flyT), 3f);   // ease-out cubic
                transform.position = LocalToWorld(Vector3.LerpUnclamped(_flyFrom, _flyTo, e));
                FaceTarget();
                if (_flyT >= 1f)
                {
                    _restLocal = _flyTo;
                    _mode = Mode.Hold;
                    _onArrived?.Invoke();
                    _onArrived = null;
                }
                break;
            }

            case Mode.Hold:
                transform.position = LocalToWorld(_restLocal);
                FaceTarget();
                break;

            case Mode.AwaitFlee:
            {
                // Parked in front, watching. Glued ship-local, so even if the
                // player creeps it stays put relative to them.
                transform.position = LocalToWorld(_restLocal);
                FaceTarget();

                Rigidbody rb = _target.Rigidbody;
                Vector3 fleeVel = (rb != null ? rb.velocity : Vector3.zero) - _fleeBaseVel;
                if (fleeVel.magnitude > fleeThreshold)
                {
                    PlayRadio(_pursuitClip);
                    Vector3 shipPos = rb != null ? rb.position : _target.transform.position;
                    _chaseRel = transform.position - shipPos;
                    // Floor starts at the current (small, in-front) gap and eases
                    // up to minChaseDistance, so the takeover has no snap; the
                    // trail slerp in TickChase swings it around behind.
                    _floorRamp = Mathf.Min(_chaseRel.magnitude, minChaseDistance);
                    _nextBlastAt = Time.time + 3f;
                    _mode = Mode.Chase;
                    _onFleeDetected?.Invoke();
                    _onFleeDetected = null;
                }
                break;
            }

            case Mode.Depart:
                _departSpeed = Mathf.Min(2500f, _departSpeed + _departSpeed * 1.6f * dt);   // accelerate away "super fast"
                _restLocal += _departLocalDir * _departSpeed * dt;
                transform.position = LocalToWorld(_restLocal);
                break;

            case Mode.Chase:
                TickChase(dt);
                break;
        }

        AimSpot();
    }

    void TickChase(float dt)
    {
        if (_resolved) return;

        Rigidbody rb = _target.Rigidbody;
        Vector3 shipPos = rb != null ? rb.position : _target.transform.position;
        Vector3 fleeVel = (rb != null ? rb.velocity : Vector3.zero) - _fleeBaseVel;
        float fleeSpeed = fleeVel.magnitude;

        // Scalar gap model: the player's flee speed opens the gap, chaseSpeed
        // closes it. Dawdle → the cop closes to point-blank. Commit to the
        // throttle → the gap opens toward escapeDistance.
        _floorRamp = Mathf.MoveTowards(_floorRamp, minChaseDistance, 70f * dt);
        float dist = _chaseRel.magnitude;
        dist = Mathf.Max(_floorRamp, dist + (fleeSpeed - chaseSpeed) * dt);

        // The cop settles in behind the player's direction of travel.
        Vector3 trailDir = fleeSpeed > 2f ? -fleeVel.normalized : _chaseRel.normalized;
        Vector3 dir = Vector3.Slerp(_chaseRel.normalized, trailDir, 1.2f * dt).normalized;
        _chaseRel = dir * dist;

        transform.position = shipPos + _chaseRel;
        Vector3 toShip = -_chaseRel.normalized;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(toShip, _target.transform.up), 4f * dt);

        if (dist > escapeDistance) { ResolveChase(escaped: true); return; }

        // Every shot is telegraphed: a radio bark (escalating through the
        // callout list) plays calloutLeadSeconds before the blast leaves.
        if (_fired < maxBlasts && dist < blastRange)
        {
            if (!_shotArmed && Time.time >= _nextBlastAt)
            {
                _shotArmed = true;
                _fireAt = Time.time + calloutLeadSeconds;
                if (_callouts != null && _callouts.Length > 0)
                    PlayRadio(_callouts[_calloutIdx++ % _callouts.Length]);
            }
            if (_shotArmed && Time.time >= _fireAt)
            {
                _shotArmed = false;
                CopEnergyBlast.Spawn(transform.position + transform.forward * 25f,
                                     _target, blastSpeed, blastHitRadius, OnBlastResolved);
                _fired++;
                _pending++;
                _nextBlastAt = Time.time + blastInterval;
            }
        }

        // All shots spent, everything resolved, target still alive → give up.
        if (_fired >= maxBlasts && _pending == 0 && _hits < hitsToKill)
            ResolveChase(escaped: true);
    }

    void OnBlastResolved(bool hit)
    {
        _pending = Mathf.Max(0, _pending - 1);
        if (!hit || _resolved) return;

        _hits++;
        if (CameraShake.Instance != null) CameraShake.Instance.TriggerShake(0.5f, 0.5f, 6f);

        if (_hits >= hitsToKill)
        {
            ResolveChase(escaped: false);
        }
        else if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.TakeDamage(15f);   // sting, not lethal
        }
    }

    void ResolveChase(bool escaped)
    {
        if (_resolved) return;
        _resolved = true;
        if (escaped)
        {
            _onEscaped?.Invoke();
            // Depart drives in target-local space — refresh the local pose first.
            _restLocal = WorldToLocal(transform.position);
            FlyAway();
        }
        else
        {
            _onCaught?.Invoke();
            FlyAway();
        }
    }

    // ── Presentation ──

    void BuildLights()
    {
        // Alternating red/blue light bar.
        _red = MakeLight("CopLight_Red", Color.red);
        _blue = MakeLight("CopLight_Blue", new Color(0.2f, 0.4f, 1f));

        // Spotlight: reuse the prefab's brightest light if it has one, else make one.
        foreach (var l in GetComponentsInChildren<Light>())
        {
            if (l == _red || l == _blue) continue;
            if (l.type == LightType.Spot) { _spot = l; break; }
        }
        if (_spot == null)
        {
            var go = new GameObject("CopSpotlight");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, -2f, 5f);
            _spot = go.AddComponent<Light>();
            _spot.type = LightType.Spot;
            _spot.spotAngle = 28f;
            _spot.range = 600f;
            _spot.intensity = 10f;
            _spot.color = Color.white;
        }
        _spotPivot = _spot.transform;
    }

    Light MakeLight(string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 8f, 0f);
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = 320f;   // must reach the player's hull from the tail pose so the red/blue wash is visible
        l.intensity = 7f;
        l.enabled = false;
        return l;
    }

    void FlashLights(float dt)
    {
        _flashTimer += dt;
        if (_flashTimer < 0.35f) return;
        _flashTimer = 0f;
        _redOn = !_redOn;
        if (_red != null) _red.enabled = _redOn;
        if (_blue != null) _blue.enabled = !_redOn;
    }

    void AimSpot()
    {
        if (_spotPivot == null || _target == null) return;
        Vector3 dir = _target.transform.position - _spotPivot.position;
        if (dir.sqrMagnitude < 0.01f) return;
        _spotPivot.rotation = Quaternion.Slerp(_spotPivot.rotation,
            Quaternion.LookRotation(dir.normalized, transform.up), 6f * Time.deltaTime);
    }

    // ── Frames ──

    Vector3 LocalToWorld(Vector3 local) => _target.transform.TransformPoint(local);
    Vector3 WorldToLocal(Vector3 world) => _target.transform.InverseTransformPoint(world);

    void FaceTarget()
    {
        Vector3 dir = _target.transform.position - transform.position;
        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(dir.normalized, _target.transform.up);
    }

    void OnDestroy()
    {
        if (_endless != null) _endless.UnregisterPhysicsObject(transform);
    }
}

/// Fades the explosion flash light out over its lifetime.
public class ExplosionFlashFade : MonoBehaviour
{
    Light _light;
    float _t, _startIntensity;

    void Awake()
    {
        _light = GetComponentInChildren<Light>();
        _startIntensity = _light != null ? _light.intensity : 0f;
    }

    void Update()
    {
        if (_light == null) return;
        _t += Time.deltaTime;
        _light.intensity = Mathf.Lerp(_startIntensity, 0f, _t / 1.6f);
        if (_t >= 1.6f) _light.enabled = false;
    }
}
