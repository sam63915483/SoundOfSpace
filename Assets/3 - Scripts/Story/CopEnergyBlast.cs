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

    public static CopEnergyBlast Spawn(Vector3 origin, Ship target,
                                       float speed, float hitRadius, Action<bool> onResolved)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "CopEnergyBlast";
        Destroy(go.GetComponent<Collider>());   // never shove the ship — hit test is manual
        go.transform.position = origin;
        go.transform.localScale = Vector3.one * 1.8f;

        var mat = go.GetComponent<Renderer>().material;
        var glow = new Color(0.3f, 0.85f, 1f);
        mat.color = glow;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", glow * 3f);

        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = glow;
        light.range = 30f;
        light.intensity = 4f;

        var b = go.AddComponent<CopEnergyBlast>();
        b._target = target;
        b._hitRadius = hitRadius;
        b._onResolved = onResolved;

        Rigidbody rb = target != null ? target.Rigidbody : null;
        Vector3 shipPos = rb != null ? rb.position : (target != null ? target.transform.position : origin);
        b._rel = origin - shipPos;
        b._relVel0 = -b._rel.normalized * speed;            // aimed dead at the ship
        b._shipVelAtFire = rb != null ? rb.velocity : Vector3.zero;
        b._life = (b._rel.magnitude / Mathf.Max(1f, speed)) * 1.6f + 0.5f;
        return b;
    }

    void Update()
    {
        if (_done) return;
        if (_target == null) { Resolve(false); return; }

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
        if (dist <= _hitRadius) { Resolve(true); return; }
        if (_t >= _life) Resolve(false);
    }

    void Resolve(bool hit)
    {
        if (_done) return;
        _done = true;
        _onResolved?.Invoke(hit);
        Destroy(gameObject);
    }
}
