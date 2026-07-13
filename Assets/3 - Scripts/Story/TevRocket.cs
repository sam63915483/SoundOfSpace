using System;
using UnityEngine;

/// Tev's emergency rocket (mission B-1 chase finale). Simulated in the player
/// ship's reference frame, like CopEnergyBlast, so floating-origin shifts and
/// N-body unit quirks can't bend its path. Homes on the corvette every frame —
/// this is a scripted kill shot, it always connects.
public class TevRocket : MonoBehaviour
{
    Ship _frameShip;
    Transform _target;
    float _speed;
    Action _onHit;
    Vector3 _rel;            // rocket position relative to the player ship
    float _life;
    bool _done;
    Light _glow;
    ParticleSystem _trail;

    public static TevRocket Spawn(Vector3 origin, Ship frameShip, Transform target, float speed, Action onHit)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "TevRocket";
        UnityEngine.Object.Destroy(go.GetComponent<Collider>());
        go.transform.position = origin;
        go.transform.localScale = new Vector3(0.9f, 2.2f, 0.9f);

        var mat = go.GetComponent<Renderer>().material;
        mat.color = new Color(1f, 0.45f, 0.1f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(1f, 0.5f, 0.15f) * 3f);

        var r = go.AddComponent<TevRocket>();
        r._frameShip = frameShip;
        r._target = target;
        r._speed = speed;
        r._onHit = onHit;

        Rigidbody rb = frameShip != null ? frameShip.Rigidbody : null;
        Vector3 shipPos = rb != null ? rb.position : (frameShip != null ? frameShip.transform.position : origin);
        r._rel = origin - shipPos;

        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(go.transform, false);
        r._glow = lightGo.AddComponent<Light>();
        r._glow.type = LightType.Point;
        r._glow.color = new Color(1f, 0.55f, 0.2f);
        r._glow.range = 60f;
        r._glow.intensity = 6f;

        // Simple exhaust trail so the shot reads even in the mirror-less cockpit.
        var trailGo = new GameObject("Trail");
        trailGo.transform.SetParent(go.transform, false);
        r._trail = trailGo.AddComponent<ParticleSystem>();
        var main = r._trail.main;
        main.startLifetime = 0.6f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startColor = new Color(1f, 0.7f, 0.3f, 0.8f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = r._trail.emission;
        emission.rateOverTime = 60f;
        trailGo.GetComponent<ParticleSystemRenderer>().material =
            new Material(Shader.Find("Particles/Standard Unlit"));

        return r;
    }

    void Update()
    {
        if (_done) return;
        if (_frameShip == null || _target == null) { Resolve(); return; }

        Rigidbody rb = _frameShip.Rigidbody;
        Vector3 shipPos = rb != null ? rb.position : _frameShip.transform.position;
        Vector3 targetRel = _target.position - shipPos;

        _rel = Vector3.MoveTowards(_rel, targetRel, _speed * Time.deltaTime);
        transform.position = shipPos + _rel;
        Vector3 dir = targetRel - _rel;
        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(dir.normalized) * Quaternion.Euler(90f, 0f, 0f);

        _life += Time.deltaTime;
        if ((targetRel - _rel).magnitude < 12f || _life > 8f) Resolve();
    }

    void Resolve()
    {
        if (_done) return;
        _done = true;
        _onHit?.Invoke();
        _onHit = null;
        Destroy(gameObject);
    }
}
