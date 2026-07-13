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
    public float tailDistance = 260f;        // shadowing offset behind the target during the siren phase
    public float tailHeight = 50f;
    public float chaseSpeed = 75f;
    public float escapeDistance = 900f;
    public float minChaseDistance = 260f;    // the cop never closes past this — shots stay visible + dodgeable
    public float chaseStartDistance = 380f;  // gap at the moment the chase kicks off
    public float blastInterval = 4f;
    public float calloutLeadSeconds = 1.5f;  // radio bark plays this long before each shot
    public int maxBlasts = 5;
    public float blastRange = 550f;
    public float blastSpeed = 90f;
    public float blastHitRadius = 8f;
    public int hitsToKill = 3;

    enum Mode { Inactive, Tail, FlyIn, Hold, Chase, Depart }
    Mode _mode = Mode.Inactive;

    Ship _target;
    CelestialBody _anchor;
    Action _onArrived, _onEscaped, _onCaught;

    Vector3 _holdLocal;      // rest pose, target-ship local space
    Vector3 _tailLocal;      // shadowing pose behind the target
    Vector3 _startLocal;
    float _flyT;
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

    /// The corvette materialises BEHIND the target and shadows it (sirens +
    /// lights) while the mission script talks the player down. Call
    /// PullInFront() once the target has (nearly) stopped to run the approach.
    public void Init(Ship target, CelestialBody anchor, AudioClip sirenClip, AudioClip[] calloutClips)
    {
        _target = target;
        _anchor = anchor;
        _callouts = calloutClips;
        _endless = FindObjectOfType<EndlessManager>();

        // Rest pose: dead ahead of the target's front window, slightly above,
        // nose pointed back at it.
        _holdLocal = new Vector3(0f, standoffHeight, standoffDistance);
        _tailLocal = new Vector3(0f, tailHeight, -tailDistance);

        transform.position = LocalToWorld(_tailLocal);
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

        _mode = Mode.Tail;
    }

    /// Sweep from wherever we are (tail pose) to the hold pose dead ahead of
    /// the target's windshield. onArrived fires when parked.
    public void PullInFront(Action onArrived)
    {
        _onArrived = onArrived;
        _startLocal = WorldToLocal(transform.position);
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
        _departLocalDir = (Vector3.up * 0.35f + Vector3.forward).normalized;
        _departSpeed = 60f;
        _mode = Mode.Depart;
        if (_siren != null) _siren.Stop();
        Destroy(gameObject, 10f);
    }

    public void StartChase(Action onEscaped, Action onCaught)
    {
        _onEscaped = onEscaped;
        _onCaught = onCaught;
        _mode = Mode.Chase;
        _nextBlastAt = Time.time + 1.5f;

        // The chase is simulated entirely in the target ship's reference frame:
        // cop position = shipPos + _chaseRel, recomputed fresh every frame. That
        // makes it immune to floating-origin shifts and to the N-body sim's
        // velocity/transform unit quirks. The gap opens or closes purely from
        // how hard the player flees relative to their velocity at the stop.
        Rigidbody rb = _target.Rigidbody;
        _fleeBaseVel = rb != null ? rb.velocity : Vector3.zero;

        // Drop in BEHIND the target at chase-start distance. It was holding
        // dead ahead — starting the chase from there meant the fleeing player
        // flew straight through it ("right inside of you"). The swap happens
        // while the ticket dialogue is closing, so it reads as the corvette
        // swinging around rather than teleporting.
        Transform shipT = _target.transform;
        _chaseRel = (-shipT.forward + shipT.up * 0.25f).normalized * chaseStartDistance;

        if (_siren != null) { _siren.loop = true; _siren.Play(); }
    }

    void LateUpdate()
    {
        if (_target == null || _mode == Mode.Inactive) return;
        float dt = Time.deltaTime;

        FlashLights(dt);

        switch (_mode)
        {
            case Mode.Tail:
                // Glued to a ship-local offset behind the target — matches its
                // speed exactly however hard the player is boosting.
                transform.position = LocalToWorld(_tailLocal);
                FaceTarget();
                break;

            case Mode.FlyIn:
            {
                _flyT += dt / Mathf.Max(0.1f, flyInSeconds);
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(_flyT), 3f);   // ease-out cubic
                transform.position = LocalToWorld(Vector3.LerpUnclamped(_startLocal, _holdLocal, e));
                FaceTarget();
                if (_flyT >= 1f)
                {
                    _mode = Mode.Hold;
                    _onArrived?.Invoke();
                    _onArrived = null;
                }
                break;
            }

            case Mode.Hold:
                transform.position = LocalToWorld(_holdLocal);
                FaceTarget();
                break;

            case Mode.Depart:
                _departSpeed = Mathf.Min(2500f, _departSpeed + _departSpeed * 1.6f * dt);   // accelerate away "super fast"
                _holdLocal += _departLocalDir * _departSpeed * dt;
                transform.position = LocalToWorld(_holdLocal);
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
        float dist = _chaseRel.magnitude;
        dist = Mathf.Max(minChaseDistance, dist + (fleeSpeed - chaseSpeed) * dt);

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
            _holdLocal = WorldToLocal(transform.position);
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
