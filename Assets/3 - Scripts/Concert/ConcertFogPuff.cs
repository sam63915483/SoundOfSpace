using UnityEngine;

// Drop on an empty GameObject placed where a fog machine should sit (typically
// at floor level upstage of the band). Auto-creates a ParticleSystem that
// emits a single burst of dense fog every N beats, simulating a real fog
// machine doing periodic puffs.
//
// Subscribes to ConcertAudioDirector for beat ticks. Falls back to a slow
// timed cycle if no music is playing.
[RequireComponent(typeof(ParticleSystem))]
public class ConcertFogPuff : MonoBehaviour
{
    [Header("Trigger")]
    [Tooltip("Minimum seconds between puffs. Real fog machines fire periodically, not on every kick — this prevents constant fogging.")]
    public float minSecondsBetweenPuffs = 4f;
    [Tooltip("Kick envelope must exceed this threshold to trigger a puff (after the cooldown).")]
    [Range(0f, 1f)] public float kickThreshold = 0.45f;
    [Tooltip("Also puff on every detected drop.")]
    public bool puffOnDrop = true;
    [Tooltip("Fallback puff interval when no audio is playing.")]
    public float fallbackPuffSeconds = 6f;

    [Header("Burst Look")]
    [Range(10, 400)] public int particlesPerPuff = 60;
    [Tooltip("Initial upward speed in local +Y (which is 'away from planet center' if you orient the GameObject vertically).")]
    public float upwardSpeed = 3f;
    public float spreadSpeed = 1.5f;
    public float lifetime = 5f;
    public float startSizeMin = 2f;
    public float startSizeMax = 4.5f;
    [Range(0f, 1f)] public float opacity = 0.45f;
    public Color tint = new Color(0.95f, 0.95f, 1f);

    ParticleSystem _ps;
    ConcertAudioDirector _director;
    float _lastPuffTime = -999f;
    bool _subscribed;

    void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
        Configure();
    }

    void Start()
    {
        _director = ConcertAudioDirector.Instance;
        if (_director != null)
        {
            _director.OnDrop += HandleDrop;
            _subscribed = true;
        }
    }

    void OnDestroy()
    {
        if (_subscribed && _director != null)
        {
            _director.OnDrop -= HandleDrop;
            _subscribed = false;
        }
    }

    void HandleDrop() { if (puffOnDrop) Puff(); }

    void Update()
    {
        // Cooldown — don't puff more than once per minSecondsBetweenPuffs.
        if (Time.time - _lastPuffTime < minSecondsBetweenPuffs) return;

        if (_director != null && _director.IsPlaying)
        {
            // Music-driven trigger: puff when the Kick envelope crosses the threshold.
            // This gives reliable puffs on bass-heavy moments without relying on
            // discrete beat detection (which misses on ambient tracks).
            if (_director.Kick > kickThreshold) Puff();
        }
        else
        {
            // No audio — fallback timed puff so the fog machine still works.
            if (Time.time - _lastPuffTime >= fallbackPuffSeconds) Puff();
        }
    }

    void Puff()
    {
        if (_ps == null) return;
        _ps.Emit(particlesPerPuff);
        _lastPuffTime = Time.time;
    }

    void OnValidate()
    {
        if (_ps == null) _ps = GetComponent<ParticleSystem>();
        if (_ps != null) Configure();
    }

    void Configure()
    {
        if (Application.isPlaying) _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        var main = _ps.main;
        main.duration = 5f;
        main.loop = false;            // we trigger explicitly via Emit()
        main.playOnAwake = false;
        main.startLifetime = lifetime;
        main.startSpeed = upwardSpeed;
        main.startSize = new ParticleSystem.MinMaxCurve(startSizeMin, startSizeMax);
        main.startColor = new Color(tint.r, tint.g, tint.b, opacity);
        main.maxParticles = 1000;
        // Local — particles ride with the planet so they don't get left behind
        // in space as Humble Abode orbits.
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.gravityModifier = 0f; // local-space "gravity" doesn't make sense on a moving planet

        var emission = _ps.emission;
        emission.rateOverTime = 0f;   // bursts only

        var shape = _ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 25f;
        shape.radius = 0.8f;

        var vel = _ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(-spreadSpeed, spreadSpeed);
        vel.z = new ParticleSystem.MinMaxCurve(-spreadSpeed, spreadSpeed);
        vel.y = new ParticleSystem.MinMaxCurve(0.3f, 0.9f); // gentle continuous rise

        var col = _ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(0.7f, 0.6f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var size = _ps.sizeOverLifetime;
        size.enabled = true;
        var sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.5f);
        sizeCurve.AddKey(0.4f, 1.4f);
        sizeCurve.AddKey(1f, 2.2f);
        size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var renderer = _ps.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = ConcertParticleAssets.GetAlphaBlendCloudMaterial();
            renderer.sharedMaterial = ConcertParticleAssets.GetAlphaBlendCloudMaterial();
        }
    }
}
