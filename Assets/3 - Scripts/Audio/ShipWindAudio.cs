using UnityEngine;

// Atmosphere wind for a ship — a loop whose volume scales with the ship's speed
// (relative to the planet) AND how deep it is in the atmosphere; pitch rises
// with speed; silent in space. Added to the SHIP44 prefab so every ship
// (including ones bought from the vendor) gets it. Clip + params assigned in the
// Inspector on the prefab.
[RequireComponent(typeof(Rigidbody))]
public class ShipWindAudio : MonoBehaviour
{
    [SerializeField] private AudioClip windLoopClip;
    [SerializeField, Range(0f, 1f)] private float windMaxVolume = 0.6f;
    [Tooltip("Speed (units/s, relative to the planet) at which wind reaches full volume + max pitch.")]
    [SerializeField] private float windFullSpeed = 60f;
    [SerializeField] private float windMinPitch = 0.7f;
    [SerializeField] private float windMaxPitch = 1.7f;
    [Tooltip("Atmosphere band thickness as a fraction of the nearest body's radius. Larger = wind audible higher up.")]
    [SerializeField, Range(0.05f, 2f)] private float atmosphereHeightFraction = 0.5f;

    Rigidbody _rb;
    AudioSource _windSrc;
    Ship _ship;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _ship = GetComponent<Ship>();
        var go = new GameObject("ShipWindAudio");
        go.transform.SetParent(transform, false);
        _windSrc = go.AddComponent<AudioSource>();
        _windSrc.playOnAwake = false;
        _windSrc.loop = true;
        _windSrc.spatialBlend = 0f;
        _windSrc.volume = 0f;
        if (windLoopClip != null) { _windSrc.clip = windLoopClip; _windSrc.Play(); }
    }

    void Update()
    {
        if (_windSrc == null || windLoopClip == null) return;
        if (_windSrc.clip == null) _windSrc.clip = windLoopClip;

        // Only audible when the player is piloting THIS ship or standing within its
        // prompt radius. The wind is 2D (in-cockpit feel), so without this gate an
        // unmanned ship ripping through low orbit was heard everywhere — even by a
        // player standing still on the surface far away.
        bool audible = _ship == null || _ship.PlayerIsNearOrPiloting();

        float atmo = AtmosphericWind.Factor(transform.position, atmosphereHeightFraction, out Vector3 bodyVel);
        float speed = _rb != null ? (_rb.velocity - bodyVel).magnitude : 0f;
        float speed01 = windFullSpeed > 0f ? Mathf.Clamp01(speed / windFullSpeed) : 0f;
        float targetVol = audible ? (windMaxVolume * speed01 * atmo) : 0f;
        _windSrc.volume = Mathf.MoveTowards(_windSrc.volume, targetVol, Time.deltaTime * 2f);
        _windSrc.pitch  = Mathf.Lerp(windMinPitch, windMaxPitch, speed01);
        if (!_windSrc.isPlaying) _windSrc.Play();
    }
}
