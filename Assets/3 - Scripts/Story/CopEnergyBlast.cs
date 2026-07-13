using System;
using UnityEngine;

/// Slow, dodgeable electromagnetic blast fired by the patrol corvette (mission B-1).
/// Simulated entirely in the target ship's reference frame: the blast's position
/// is stored RELATIVE to the ship and recomputed to world space each frame, which
/// makes it immune to floating-origin shifts and orbital drift. Physics of the
/// dodge: the blast keeps the constant velocity it was fired with (in the frame
/// the ship had at fire time), so if the player holds course it flies straight
/// into them — and any acceleration (turn, boost, brake) bends its apparent path
/// away. Fly hard, dodge blasts.
public class CopEnergyBlast : MonoBehaviour
{
    Ship _target;
    Vector3 _rel;            // blast position relative to the ship, world axes
    Vector3 _relVel0;        // fire-time velocity toward the ship (frame-relative)
    Vector3 _shipVelAtFire;  // ship rb velocity when fired
    float _life;
    float _t;
    float _hitRadius;
    Action<bool> _onResolved;
    bool _done;

    LineRenderer[] _arcs;    // crackling lightning around the core — "big taser shot"
    float _nextArcAt;

    // Cockpit radar: a 2D ping that gets louder and faster as the shot closes.
    AudioClip _pingClip, _zapClip;
    AudioSource _pingSrc;
    float _startDist, _nextPingAt;

    public static CopEnergyBlast Spawn(Vector3 origin, Ship target,
                                       float speed, float hitRadius,
                                       AudioClip pingClip, AudioClip zapClip,
                                       Action<bool> onResolved)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "CopEnergyBlast";
        Destroy(go.GetComponent<Collider>());   // never shove the ship — hit test is manual
        go.transform.position = origin;
        go.transform.localScale = Vector3.one * 5.4f;   // 3× the original — unmissable at chase distance

        var mat = go.GetComponent<Renderer>().material;
        var glow = new Color(0.3f, 0.85f, 1f);
        mat.color = glow;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", glow * 4f);

        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = glow;
        light.range = 70f;
        light.intensity = 8f;

        var b = go.AddComponent<CopEnergyBlast>();
        b._target = target;
        b._hitRadius = hitRadius;
        b._onResolved = onResolved;
        b._pingClip = pingClip;
        b._zapClip = zapClip;
        if (pingClip != null)
        {
            b._pingSrc = go.AddComponent<AudioSource>();
            b._pingSrc.spatialBlend = 0f;   // cockpit radar, not positional
        }
        b.BuildArcs(go.transform);

        Rigidbody rb = target != null ? target.Rigidbody : null;
        Vector3 shipPos = rb != null ? rb.position : (target != null ? target.transform.position : origin);
        b._rel = origin - shipPos;
        b._relVel0 = -b._rel.normalized * speed;            // aimed dead at the ship
        b._shipVelAtFire = rb != null ? rb.velocity : Vector3.zero;
        b._life = (b._rel.magnitude / Mathf.Max(1f, speed)) * 1.6f + 0.5f;
        b._startDist = Mathf.Max(1f, b._rel.magnitude);
        return b;
    }

    // Jittery polylines wrapped around the core, re-randomized ~30×/s —
    // cheap convincing electricity. Local space, so they ride the blast free.
    void BuildArcs(Transform parent)
    {
        _arcs = new LineRenderer[7];
        var arcMat = new Material(Shader.Find("Particles/Standard Unlit"));
        arcMat.color = new Color(0.9f, 0.98f, 1f);
        for (int i = 0; i < _arcs.Length; i++)
        {
            var go = new GameObject("Arc" + i);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.material = arcMat;
            lr.positionCount = 7;
            lr.startWidth = 0.16f;   // parent scale ×5.4 → ~0.85 world units at the root
            lr.endWidth = 0.03f;
            lr.startColor = new Color(0.95f, 0.99f, 1f, 1f);
            lr.endColor = new Color(0.35f, 0.9f, 1f, 0.75f);
            _arcs[i] = lr;
        }
        RandomizeArcs();
    }

    void RandomizeArcs()
    {
        for (int i = 0; i < _arcs.Length; i++)
        {
            var lr = _arcs[i];
            if (lr == null) continue;
            Vector3 a = UnityEngine.Random.onUnitSphere * 0.5f;
            Vector3 b = UnityEngine.Random.onUnitSphere * 0.5f;
            for (int p = 0; p < lr.positionCount; p++)
            {
                float t = p / (float)(lr.positionCount - 1);
                Vector3 point = Vector3.Slerp(a, b, t);
                point *= UnityEngine.Random.Range(1.0f, 2.3f);           // long tendrils off the surface
                point += UnityEngine.Random.insideUnitSphere * 0.22f;    // hard kinks
                lr.SetPosition(p, point);
            }
        }
    }

    void Update()
    {
        if (_done) return;
        if (_target == null) { Resolve(false); return; }

        if (Time.time >= _nextArcAt)
        {
            _nextArcAt = Time.time + 0.033f;
            RandomizeArcs();
        }

        float dt = Time.deltaTime;
        Rigidbody rb = _target.Rigidbody;
        Vector3 shipPos = rb != null ? rb.position : _target.transform.position;
        Vector3 shipVel = rb != null ? rb.velocity : Vector3.zero;

        // Frame-correct kinematics: in the ship frame the blast's velocity is its
        // fire-time velocity minus however much the ship has accelerated since.
        _rel += (_relVel0 + (_shipVelAtFire - shipVel)) * dt;
        _t += dt;

        transform.position = shipPos + _rel;

        float dist = _rel.magnitude;

        // Radar ping: louder and faster the closer the shot is. Dies with the
        // blast (miss = silence until the next shot).
        if (_pingSrc != null && _pingClip != null && Time.time >= _nextPingAt)
        {
            float closeness = 1f - Mathf.Clamp01(dist / _startDist);
            _pingSrc.PlayOneShot(_pingClip, Mathf.Lerp(0.25f, 1f, closeness));
            _nextPingAt = Time.time + Mathf.Lerp(0.85f, 0.12f, closeness);
        }

        if (dist <= _hitRadius) { Resolve(true); return; }
        if (_t >= _life) Resolve(false);
    }

    void Resolve(bool hit)
    {
        if (_done) return;
        _done = true;
        if (hit && _zapClip != null)
        {
            // The blast object dies right now — play the fry on a detached 2D
            // one-shot that outlives it.
            var go = new GameObject("TaserZap");
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.PlayOneShot(_zapClip, 1f);
            Destroy(go, _zapClip.length + 0.2f);
        }
        _onResolved?.Invoke(hit);
        Destroy(gameObject);
    }
}
