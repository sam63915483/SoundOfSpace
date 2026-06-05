using UnityEngine;

// Lives on the same GameObject as ShipReactor. Drives the emission on the
// reactor's "tube" submesh (material index 1, the Opacity variant of the
// retro-lab prop material) based on the ship's fuel level: bright when full,
// dark when empty. A point Light child is spawned to throw bloom-able glow
// onto nearby surfaces.
//
// Behaviours layered onto the base fuel-driven intensity:
//   * Breathing — sine-wave amplitude modulation while BLUE. Faster when fuel
//     is high, slower when fuel is low. Disappears entirely at 0% fuel.
//   * Long red event — occasional "out-of-control crystal" moments. Colour
//     fades to red and back; during the red phase the smooth breathing is
//     replaced by rapid Perlin-noise flicker. Frequency scales with fuel.
//   * Short red flicker — much shorter, more frequent random sparks of red
//     that briefly tint the glow without committing to a full event.
//   * PingFlash — additive intensity spike triggered by ShipReactor.Refuel().
//
// Per-renderer material instancing keeps the glow local to this reactor's
// renderer — other props in the scene using the same source material are
// unaffected.
public class ReactorGlow : MonoBehaviour
{
    [Tooltip("Auto-resolved via GetComponentInParent<Ship>() if null.")]
    public Ship ship;

    [Tooltip("Index into the renderer's materials array that drives the glowing tube. The retro-lab reactor has 2 materials; index 1 is the Opacity variant covering the tube.")]
    public int targetMaterialIndex = 1;

    [Header("Glow colour + intensity")]
    [Tooltip("Electric-blue base emission colour, multiplied by intensity each frame.")]
    public Color glowColor = new Color(0.15f, 0.45f, 1.0f);
    [Tooltip("Emission HDR intensity multiplier when fuel = 100%.")]
    public float maxIntensity = 6f;
    [Tooltip("Emission HDR intensity multiplier when fuel = 0%.")]
    public float minIntensity = 0f;
    [Tooltip("Power curve applied to fuel before lerping to baseIntensity. <1 keeps the glow bright as fuel drops, drops off only near empty. 0.5 = sqrt curve; 1.0 = linear; 2.0 = quadratic falloff (drops fast).")]
    [Range(0.1f, 3f)] public float glowFalloffPower = 0.5f;

    [Header("Breathing (only while BLUE)")]
    [Tooltip("Seconds per breath cycle when fuel = 100% (faster breathing).")]
    public float breathPeriodAtFull = 1.5f;
    [Tooltip("Seconds per breath cycle when fuel = 0% (slower breathing).")]
    public float breathPeriodAtEmpty = 5f;
    [Tooltip("How deep the breath dips below the baseline (0 = no breathing, 0.5 = baseline halves at the trough).")]
    [Range(0f, 0.7f)] public float breathAmplitude = 0.5f;

    [Header("Red event (out-of-control moments)")]
    public Color redColor = new Color(1.0f, 0.15f, 0.1f);
    [Tooltip("Average seconds between red events when fuel = 100% (events happen more often at full).")]
    public float redIntervalAtFull = 4f;
    [Tooltip("Average seconds between red events when fuel = 0%.")]
    public float redIntervalAtEmpty = 30f;
    [Tooltip("Seconds spent fading in to red AND fading back out (each leg).")]
    public float redTransitionDuration = 0.8f;
    [Tooltip("Seconds held at peak red between fade-in and fade-out.")]
    public float redHoldDuration = 2.0f;
    [Tooltip("Hard cap on total event duration (transitions + hold).")]
    public float redMaxTotalDuration = 4.0f;

    [Header("Red flicker (Perlin-driven while RED)")]
    [Tooltip("Higher = faster strobing during a red event. ~20-30 gives a buzzing electrical feel.")]
    public float flickerNoiseFrequency = 25f;
    [Tooltip("Minimum intensity multiplier during flicker. Lower = darker dips.")]
    [Range(0f, 1f)] public float flickerMin = 0.35f;
    [Tooltip("Maximum intensity multiplier during flicker. Higher = brighter spikes (can blow out bloom).")]
    [Range(1f, 3f)] public float flickerMax = 1.6f;

    [Header("Refuel flash")]
    [Tooltip("Extra emission added on top of the baseline immediately after refuelling. Decays to 0 over flashDuration.")]
    public float flashBoost = 4f;
    [Tooltip("Seconds the flash takes to decay from flashBoost back to 0.")]
    public float flashDuration = 0.8f;

    [Header("Point light")]
    [Tooltip("Spawn a child Point Light that throws coloured light onto nearby surfaces. Disable if the bloom-only emission is enough.")]
    public bool spawnPointLight = true;
    public float lightMaxIntensity = 4f;
    public float lightRange = 3f;

    [Header("Reactor audio")]
    [Tooltip("Looping reactor-core hum. Volume scales with fuel/glow.")]
    [Range(0f, 1f)] public float reactorBuzzVolume = 0.75f;
    [Tooltip("Harsher electric surge played on each red 'unstable' event.")]
    [Range(0f, 1f)] public float reactorSurgeVolume = 0.6f;

    Material _instMat;
    Light _light;
    float _flashRemaining;
    AudioSource _buzzSource, _surgeSource;
    AudioClip _buzzClip, _surgeClip;

    // Red-event scheduling state
    float _nextRedTime = -1f;
    bool _inRedEvent;
    float _redElapsed;
    float _redEventTotalDuration;

    // Persistent perlin offset so each instance flickers independently.
    float _flickerSeed;

    void Awake()
    {
        if (ship == null) ship = GetComponentInParent<Ship>();
        _flickerSeed = Random.value * 1000f;

        var mr = GetComponent<MeshRenderer>();
        if (mr != null)
        {
            // .materials[] auto-instantiates all materials on this renderer so
            // edits don't leak to the shared asset.
            var mats = mr.materials;
            if (mats != null && targetMaterialIndex >= 0 && targetMaterialIndex < mats.Length)
            {
                _instMat = mats[targetMaterialIndex];
                if (_instMat != null)
                {
                    _instMat.EnableKeyword("_EMISSION");
                    _instMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            }
        }

        if (spawnPointLight)
        {
            var lightGO = new GameObject("ReactorGlowLight");
            lightGO.transform.SetParent(transform, false);
            lightGO.transform.localPosition = Vector3.zero;
            _light = lightGO.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = glowColor;
            _light.range = lightRange;
            _light.intensity = 0f;
            _light.shadows = LightShadows.None;
        }

        // Reactor audio: a looping electric buzz (volume tracks fuel/glow) plus a
        // harsher surge on each red unstable event. 3D so it comes from the
        // reactor. Clips load lazily from StreamingAssets.
        _buzzSource = gameObject.AddComponent<AudioSource>();
        _buzzSource.playOnAwake = false;
        _buzzSource.loop = true;
        _buzzSource.spatialBlend = 1f;
        _buzzSource.minDistance = 3f;    // full volume throughout the cabin
        _buzzSource.maxDistance = 22f;   // audible across the ship + just outside
        _buzzSource.volume = 0f;
        _surgeSource = gameObject.AddComponent<AudioSource>();
        _surgeSource.playOnAwake = false;
        _surgeSource.loop = false;
        _surgeSource.spatialBlend = 1f;
        _surgeSource.minDistance = 1.5f;
        _surgeSource.maxDistance = 16f;
        StartCoroutine(StreamingAudio.Load("Audio/ReactorBuzz.wav", AudioType.WAV, c =>
        {
            _buzzClip = c;
            if (_buzzSource != null) { _buzzSource.clip = c; _buzzSource.Play(); }
        }));
        StartCoroutine(StreamingAudio.Load("Audio/ReactorSurge.wav", AudioType.WAV, c => _surgeClip = c));
    }

    void Update()
    {
        if (ship == null) return;

        float fuel = Mathf.Clamp01(ship.FuelPercent);

        // Reactor buzz tracks the glow: louder with fuel, silent when dead.
        if (_buzzSource != null) _buzzSource.volume = reactorBuzzVolume * fuel;

        // ── Red event scheduling ────────────────────────────────────────
        if (_nextRedTime < 0f) ScheduleNextRedEvent(fuel);
        if (_inRedEvent)
        {
            _redElapsed += Time.deltaTime;
            if (_redElapsed >= _redEventTotalDuration)
            {
                _inRedEvent = false;
                ScheduleNextRedEvent(fuel);
            }
        }
        else if (Time.time >= _nextRedTime && fuel > 0.05f)
        {
            _inRedEvent = true;
            _redElapsed = 0f;
            float planned = redTransitionDuration * 2f + Mathf.Max(redHoldDuration, 0f);
            _redEventTotalDuration = Mathf.Min(planned, redMaxTotalDuration);
            // Harsher electric surge for the unstable moment.
            if (_surgeSource != null && _surgeClip != null)
                _surgeSource.PlayOneShot(_surgeClip, reactorSurgeVolume);
        }

        // Red event redPhase: 0 = pure blue, 1 = peak red.
        float redPhase = 0f;
        if (_inRedEvent)
        {
            float tIn = redTransitionDuration;
            float tOut = redTransitionDuration;
            float planned = redTransitionDuration * 2f + Mathf.Max(redHoldDuration, 0f);
            if (_redEventTotalDuration < planned)
            {
                float shrink = _redEventTotalDuration / planned;
                tIn *= shrink;
                tOut *= shrink;
            }
            float holdEnd = _redEventTotalDuration - tOut;
            if (_redElapsed < tIn)
                redPhase = _redElapsed / Mathf.Max(tIn, 0.0001f);
            else if (_redElapsed < holdEnd)
                redPhase = 1f;
            else
                redPhase = Mathf.Clamp01((_redEventTotalDuration - _redElapsed) / Mathf.Max(tOut, 0.0001f));
        }

        // ── Intensity modulation ────────────────────────────────────────
        // During a red event swap the smooth breath for rapid Perlin flicker
        // — feels like an unstable arc. Else breathe normally.
        float modulation;
        if (_inRedEvent)
        {
            float n = Mathf.PerlinNoise(Time.time * flickerNoiseFrequency, _flickerSeed);
            modulation = Mathf.Lerp(flickerMin, flickerMax, n);
        }
        else
        {
            float period = Mathf.Lerp(breathPeriodAtEmpty, breathPeriodAtFull, fuel);
            float omega = 2f * Mathf.PI / Mathf.Max(period, 0.1f);
            float sin01 = (Mathf.Sin(Time.time * omega) + 1f) * 0.5f;
            modulation = 1f - breathAmplitude + breathAmplitude * sin01;
        }

        // ── Refuel flash decay ──────────────────────────────────────────
        float boost = 0f;
        if (_flashRemaining > 0f)
        {
            _flashRemaining -= Time.deltaTime;
            float t = Mathf.Clamp01(_flashRemaining / flashDuration);
            boost = flashBoost * t * t;
        }

        // ── Composite + apply ───────────────────────────────────────────
        // Apply a power curve to fuel so the glow stays bright as fuel drops,
        // dropping off only near empty. With glowFalloffPower=0.5, fuel=50%
        // gives ~71% brightness, fuel=30% gives ~55% — much more visible than
        // a linear lerp's 50% and 30%.
        float fuelCurve = Mathf.Pow(fuel, glowFalloffPower);
        float baseIntensity = Mathf.Lerp(minIntensity, maxIntensity, fuelCurve);
        float intensity = baseIntensity * modulation + boost;
        Color currentColor = Color.Lerp(glowColor, redColor, redPhase);

        if (_instMat != null)
        {
            _instMat.SetColor("_EmissionColor", currentColor * intensity);
        }
        if (_light != null)
        {
            float headroom = Mathf.Max(maxIntensity + flashBoost, 0.001f);
            _light.intensity = (intensity / headroom) * lightMaxIntensity;
            _light.color = currentColor;
        }
    }

    void ScheduleNextRedEvent(float fuel)
    {
        if (fuel <= 0.05f) { _nextRedTime = Time.time + 1e6f; return; }
        float meanInterval = Mathf.Lerp(redIntervalAtEmpty, redIntervalAtFull, fuel);
        _nextRedTime = Time.time + Random.Range(meanInterval * 0.5f, meanInterval * 1.5f);
    }

    public void PingFlash()
    {
        _flashRemaining = flashDuration;
    }
}
