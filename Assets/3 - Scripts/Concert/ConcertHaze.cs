using UnityEngine;

// Drop this on an empty GameObject placed somewhere in the venue (e.g., near
// the stage or above the dance floor). It auto-creates and configures a
// ParticleSystem that emits slow, drifting, semi-transparent fog particles
// inside a configurable box volume — the kind of haze that makes laser beams
// visible volumetrically at a real concert.
//
// No prefab needed. Just place an empty GameObject, add this script, adjust
// the Volume Size in inspector, and you have stage haze.
[RequireComponent(typeof(ParticleSystem))]
public class ConcertHaze : MonoBehaviour
{
    [Header("Volume")]
    [Tooltip("Box (in local space) where particles spawn.")]
    public Vector3 volumeSize = new Vector3(40f, 16f, 40f);
    [Tooltip("Offset of the spawn box from the GameObject's position (local space). " +
             "Useful when the Haze GameObject is mounted above the stage but you want " +
             "haze to extend downward — set a negative Y here (e.g. (0, -8, 0)).")]
    public Vector3 volumeCenter = Vector3.zero;

    [Header("Density / Look")]
    [Range(0, 1500)] public int maxParticles = 400;
    [Tooltip("Particles per second. More small particles = even haze. Fewer big ones = smoke.")]
    public float emissionRate = 35f;
    [Tooltip("Lifetime in seconds — long lifetime + slow drift = persistent haze.")]
    public float particleLifetime = 12f;
    public float particleSizeMin = 2.5f;
    public float particleSizeMax = 5.5f;
    [Range(0f, 1f)] public float opacity = 0.07f;
    public Color tint = new Color(0.85f, 0.88f, 1f, 1f);

    [Header("Drift (in local space — moves with the planet)")]
    [Tooltip("Slow drift so haze isn't perfectly static.")]
    public Vector3 driftVelocity = new Vector3(0.15f, 0.05f, 0.1f);
    [Tooltip("Random velocity added on top of drift, so particles wander.")]
    public float randomVelocity = 0.15f;

    ParticleSystem _ps;

    void Reset() { /* triggers RequireComponent if added via inspector */ }

    void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
        Configure();
    }

    void OnValidate()
    {
        if (_ps == null) _ps = GetComponent<ParticleSystem>();
        if (_ps != null) Configure();
    }

    void Configure()
    {
        // Stop the system before mutating modules in OnValidate to avoid Unity warnings.
        if (Application.isPlaying) _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        var main = _ps.main;
        main.duration = 60f;
        main.loop = true;
        main.startLifetime = particleLifetime;
        main.startSpeed = 0f; // velocity comes from Velocity Over Lifetime + drift below
        main.startSize = new ParticleSystem.MinMaxCurve(particleSizeMin, particleSizeMax);
        main.startColor = new Color(tint.r, tint.g, tint.b, opacity);
        main.maxParticles = maxParticles;
        // Random start rotation so overlapping particles don't look identical.
        // Without this, every billboard shows the same image and they stack as
        // visible distinct circles instead of blending into amorphous fog.
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        // Local simulation space — particles ride along with the GameObject as
        // Humble Abode moves through its orbit. Without this, particles get
        // left behind in world space as the planet drifts under them.
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.gravityModifier = 0f;
        main.playOnAwake = true;

        var emission = _ps.emission;
        emission.rateOverTime = emissionRate;

        var shape = _ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = volumeSize;
        shape.position = volumeCenter;

        var vel = _ps.velocityOverLifetime;
        vel.enabled = true;
        // Local — drift is in the GameObject's local frame, so it stays consistent
        // even as the planet rotates/orbits.
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(driftVelocity.x - randomVelocity, driftVelocity.x + randomVelocity);
        vel.y = new ParticleSystem.MinMaxCurve(driftVelocity.y - randomVelocity * 0.3f, driftVelocity.y + randomVelocity * 0.3f);
        vel.z = new ParticleSystem.MinMaxCurve(driftVelocity.z - randomVelocity, driftVelocity.z + randomVelocity);

        // Slow rotation-over-lifetime so each particle drifts in orientation —
        // makes overlapping particles look like swirling fog rather than static blobs.
        var rot = _ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f); // radians/sec, signed range

        // Fade in over the first ~5% of life, plateau, fade out at end.
        var col = _ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.1f),
                new GradientAlphaKey(1f, 0.85f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // Renderer: soft alpha-blended cloud puffs via the shared particle material.
        // The material has a built-in soft radial gradient texture so each particle
        // is a feathered circle, not a hard square.
        var renderer = _ps.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = ConcertParticleAssets.GetAlphaBlendCloudMaterial();
            renderer.sharedMaterial = ConcertParticleAssets.GetAlphaBlendCloudMaterial();
            renderer.sortingOrder = -1;
        }

        if (Application.isPlaying) _ps.Play();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.85f, 1f, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(volumeCenter, volumeSize);
    }
}
