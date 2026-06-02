using UnityEngine;

// Drop on an empty GameObject pointed where you want a stage blinder light to
// flash (e.g., facing the audience, or up at the rig). Auto-creates a Unity
// Light component, flashes its intensity on beats, holds full-bright during
// drops, and idles dim otherwise.
//
// Subscribes to ConcertAudioDirector for beats + drops. Falls back to dim
// idle when no music is playing (so it doesn't go dark).
public class ConcertBlinder : MonoBehaviour
{
    [Header("Light")]
    public LightType lightType = LightType.Spot;
    public Color color = new Color(1f, 0.95f, 0.85f, 1f);
    public float spotAngle = 60f;
    public float range = 50f;

    [Header("Intensities")]
    public float idleIntensity = 0.5f;
    [Tooltip("Peak brightness at full Kick envelope (i.e., on a strong bass hit). Built-in pipeline lights are in arbitrary units; 10–20 is concert-bright.")]
    public float kickIntensity = 18f;
    [Tooltip("Brightness when a drop is detected, held for dropFlashSeconds.")]
    public float dropIntensity = 35f;
    public float dropFlashSeconds = 0.5f;
    [Tooltip("Bonus brightness on each detected discrete beat (added on top of Kick response).")]
    public float beatBonus = 6f;
    public float beatBonusSeconds = 0.12f;
    [Tooltip("Bonus brightness on every snare hit. Real concert blinders pop on the backbeat.")]
    public float snareBonus = 4f;
    public float snareBonusSeconds = 0.1f;
    [Tooltip("Brightness when a crash cymbal is detected, held for crashFlashSeconds. Bigger than drops.")]
    public float crashIntensity = 50f;
    public float crashFlashSeconds = 0.5f;

    Light _light;
    ConcertAudioDirector _director;
    float _beatFlashUntil;
    float _snareFlashUntil;
    float _dropFlashUntil;
    float _crashFlashUntil;
    bool _subscribed;

    void Awake()
    {
        _light = GetComponent<Light>();
        if (_light == null) _light = gameObject.AddComponent<Light>();
        _light.type = lightType;
        _light.color = color;
        _light.intensity = idleIntensity;
        _light.spotAngle = spotAngle;
        _light.range = range;
        _light.shadows = LightShadows.None; // perf — many of these in a scene
    }

    // Live-poll the InputSettings.fxConcertShadows toggle. Default None matches
    // the hard-coded value in Awake; flipping the toggle in the pause menu
    // promotes to Soft shadows on the next Update tick.
    void ApplyShadowsSetting()
    {
        if (_light == null) return;
        var cem = CameraEffectsManager.Instance;
        bool want = cem != null && cem.Input != null && cem.Input.fxConcertShadows;
        var wantedMode = want ? LightShadows.Soft : LightShadows.None;
        if (_light.shadows != wantedMode) _light.shadows = wantedMode;
    }

    void Start()
    {
        _director = ConcertAudioDirector.Instance;
        if (_director != null)
        {
            _director.OnBeat  += HandleBeat;
            _director.OnSnare += HandleSnare;
            _director.OnDrop  += HandleDrop;
            _director.OnCrash += HandleCrash;
            _subscribed = true;
        }
    }

    void OnDestroy()
    {
        if (_subscribed && _director != null)
        {
            _director.OnBeat  -= HandleBeat;
            _director.OnSnare -= HandleSnare;
            _director.OnDrop  -= HandleDrop;
            _director.OnCrash -= HandleCrash;
            _subscribed = false;
        }
    }

    void HandleBeat()  { _beatFlashUntil  = Time.time + beatBonusSeconds; }
    void HandleSnare() { _snareFlashUntil = Time.time + snareBonusSeconds; }
    void HandleDrop()  { _dropFlashUntil  = Time.time + dropFlashSeconds; }
    void HandleCrash() { _crashFlashUntil = Time.time + crashFlashSeconds; }

    void Update()
    {
        if (_light == null) return;

        ApplyShadowsSetting();

        // Continuous Kick-driven response: blinder pumps with every bass transient.
        float kick = _director != null ? _director.Kick : 0f;
        float target = idleIntensity + kick * (kickIntensity - idleIntensity);

        // Bonus punches: snare on backbeat, beat fallback, drop, crash (biggest).
        if (Time.time < _beatFlashUntil)  target += beatBonus;
        if (Time.time < _snareFlashUntil) target += snareBonus;
        if (Time.time < _dropFlashUntil)  target = Mathf.Max(target, dropIntensity);
        if (Time.time < _crashFlashUntil) target = Mathf.Max(target, crashIntensity);

        float rate = (target > _light.intensity) ? 80f : 25f;
        _light.intensity = Mathf.MoveTowards(_light.intensity, target, Time.deltaTime * rate);
        _light.color = color;
    }

    void OnValidate()
    {
        if (_light == null) _light = GetComponent<Light>();
        if (_light != null)
        {
            _light.type = lightType;
            _light.color = color;
            _light.spotAngle = spotAngle;
            _light.range = range;
        }
    }
}
