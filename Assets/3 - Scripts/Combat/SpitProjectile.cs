using UnityEngine;

/// <summary>
/// A small green spit globule that arcs from an enemy's mouth to the player.
/// Spawned by EnemyController when the player climbs on top of a tree (out of
/// melee reach). The flight is parametric — homes onto the player Transform
/// each frame, so a player who tries to camp on a trunk can't dodge by
/// standing still. Damage is applied directly to ResourceManager on arrival;
/// no trigger collider is involved.
///
/// Spawned via the static Spawn() helper. Self-destroys on impact, on lifetime
/// timeout, or if the target is destroyed mid-flight.
/// </summary>
public class SpitProjectile : MonoBehaviour
{
    // Start position is stored *in planet-local space* and the projectile is
    // parented to that planet. Floating-origin shifts move the planet (and
    // therefore the projectile + the cached local start position) as a unit,
    // so the lerp endpoints stay coherent. The previous version stored a
    // world-space _startPos that didn't get shifted, producing visible
    // teleports / disappearances whenever EndlessManager fired mid-flight.
    Transform _planet;
    Vector3 _localStartPos;
    Transform _target;
    Vector3 _lastTargetPos;
    float _damage;
    float _flightDuration;
    float _arcHeight;
    Vector3 _planetUp; // captured at spawn — fine for sub-second flight times
    float _spawnTime;
    bool _hit;

    // One shared material for every spit (all the same sickly green). The old
    // version did `new Material(Shader.Find("Standard"))` per shot and assigned
    // it via `.material`, which leaks one material per projectile (an explicitly
    // assigned material is not auto-destroyed with the GameObject). Reusing one
    // sharedMaterial removes both the per-shot Shader.Find and the leak.
    static Material _sharedMat;
    static Material SharedSpitMaterial()
    {
        if (_sharedMat == null)
        {
            _sharedMat = new Material(Shader.Find("Standard"));
            _sharedMat.color = new Color(0.55f, 0.7f, 0.18f); // sickly green
        }
        return _sharedMat;
    }

    /// <summary>
    /// Build a spit and launch it. Pass the firing enemy's parent CelestialBody
    /// so we can parent under it; without that, floating-origin shifts mid-
    /// flight produce a visible teleport between the stale _startPos and the
    /// freshly-read target position.
    /// </summary>
    public static SpitProjectile Spawn(Vector3 origin, Transform target, float damage,
                                       float flightDuration, float arcHeight, Vector3 planetUp,
                                       Transform planet)
    {
        if (target == null) return null;
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "SpitProjectile";
        go.transform.position = origin;
        go.transform.localScale = Vector3.one * 0.18f;

        // Parent to the firing enemy's planet so floating-origin shifts move
        // the projectile + its cached start position as a single unit.
        if (planet != null)
            go.transform.SetParent(planet, worldPositionStays: true);

        // Drive position via script — collider would otherwise shove the player
        // around when it brushes the capsule.
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
            mr.sharedMaterial = SharedSpitMaterial();

        var p = go.AddComponent<SpitProjectile>();
        p._planet         = planet;
        p._localStartPos  = planet != null
            ? planet.InverseTransformPoint(origin)
            : origin;
        p._target         = target;
        p._lastTargetPos  = target.position;
        p._damage         = damage;
        p._flightDuration = Mathf.Max(0.05f, flightDuration);
        p._arcHeight      = arcHeight;
        p._planetUp       = planetUp;
        p._spawnTime      = Time.time;
        return p;
    }

    void Update()
    {
        if (_hit) return;

        // Track a moving player. If they get destroyed mid-flight, fall back to
        // the last known position so we don't NaN.
        Vector3 currentTarget = _target != null ? _target.position : _lastTargetPos;
        if (_target != null) _lastTargetPos = currentTarget;

        float t = (Time.time - _spawnTime) / _flightDuration;

        if (t >= 1f)
        {
            transform.position = currentTarget;
            ApplyDamage();
            return;
        }

        // Resolve start position from planet-local each frame so floating-
        // origin shifts can't make _startPos go stale relative to the live
        // target read.
        Vector3 startWorld = _planet != null
            ? _planet.TransformPoint(_localStartPos)
            : _localStartPos;

        // Linear interp + sine arc bowed away from the planet. Recomputed from
        // the live target each frame so the curve stays smooth even while
        // homing.
        Vector3 linear = Vector3.Lerp(startWorld, currentTarget, t);
        float arc = Mathf.Sin(t * Mathf.PI) * _arcHeight;
        transform.position = linear + _planetUp * arc;
    }

    void ApplyDamage()
    {
        _hit = true;
        if (ResourceManager.Instance != null) ResourceManager.Instance.TakeDamage(_damage);
        Destroy(gameObject);
    }
}
