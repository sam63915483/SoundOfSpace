using System.Collections;
using UnityEngine;

/// <summary>
/// Moon-base airlock for "Cold Company". Two doors — an outer SEALDOOR (starts open) and the
/// inner MoonBaseEntrance (starts closed) — plus the power-switch levers and 4 corner
/// decompressor vents. Cycle:
///
///   All levers ON  → SEALDOOR seals (down) + vents blow smoke + door sound → pause → the inner
///                    door opens → the base pressurizes (oxygen tops up, no drain). Fires the
///                    "left in a hurry" reveal once.
///   All levers OFF → inner door closes → pause → vents + SEALDOOR opens → depressurized, the
///                    suit drains again outside.
///
/// The decompressor smoke replicates the ship's pressurizer puff (built in code, emitted in
/// bursts) — see Ship.BuildPressurizerPuff. Oxygen uses OxygenManager.InPressurizedBase.
/// </summary>
public class AirlockController : MonoBehaviour
{
    public static AirlockController Instance { get; private set; }

    [Header("Doors")]
    public MoonBaseDoor innerDoor;   // MoonBaseEntrance — opens into the base
    public MoonBaseDoor outerDoor;   // SEALDOOR — seals to the outside

    [Header("Decompressor vents")]
    [Tooltip("4 empty markers at the switch-wall corners. Smoke shoots along each marker's local -Y.")]
    public Transform[] vents;
    [Tooltip("Seconds the vents blow smoke while the airlock cycles.")]
    public float ventSeconds = 3f;

    [Header("Timing")]
    public float pauseSeconds = 1f;

    [Header("Sound")]
    [Tooltip("Optional door/seal clip. The vent hiss loads from StreamingAssets/Audio/Pressurizer.wav.")]
    public AudioClip doorSound;

    bool _sealed;   // true once cycled to "inside" (inner open, outer sealed)
    bool _busy;
    ParticleSystem[] _puffs;
    AudioSource[] _ventAudio;
    AudioClip _hissClip;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        BuildVents();
        StartCoroutine(StreamingAudio.Load("Audio/Pressurizer.wav", AudioType.WAV, c => _hissClip = c));
        if (OxygenManager.Instance != null) OxygenManager.Instance.InPressurizedBase = false; // start outside
    }

    void Update()
    {
        if (_busy) return;
        bool allOn = MoonBasePowerSwitch.AllOn();
        if (allOn && !_sealed) StartCoroutine(EnterCycle());
        else if (!allOn && _sealed) StartCoroutine(LeaveCycle());
    }

    IEnumerator EnterCycle()
    {
        _busy = true;
        // Seal the outside first, venting the chamber.
        if (outerDoor != null) outerDoor.Close();
        if (doorSound != null) AudioSource.PlayClipAtPoint(doorSound, transform.position);
        FireVents();
        yield return new WaitForSeconds(pauseSeconds);

        // Pressurize, then open the inner door.
        var om = OxygenManager.Instance;
        if (om != null) { om.InPressurizedBase = true; om.ApplyState(float.MaxValue, float.MaxValue, om.CyclopsCheckpointReached); }
        if (innerDoor != null) innerDoor.Open();
        ColdCompany.NotifyEnteredBase();   // "left in a hurry" reveal (once)

        _sealed = true;
        _busy = false;
    }

    IEnumerator LeaveCycle()
    {
        _busy = true;
        // Depressurize (drain resumes) and close the inner door first.
        var om = OxygenManager.Instance;
        if (om != null) om.InPressurizedBase = false;
        if (innerDoor != null) innerDoor.Close();
        yield return new WaitForSeconds(pauseSeconds);

        // Vent, then open back to the outside.
        FireVents();
        if (doorSound != null) AudioSource.PlayClipAtPoint(doorSound, transform.position);
        if (outerDoor != null) outerDoor.Open();

        _sealed = false;
        _busy = false;
    }

    void FireVents()
    {
        if (_puffs == null) return;
        for (int i = 0; i < _puffs.Length; i++)
        {
            if (_puffs[i] != null) StartCoroutine(EmitPuff(_puffs[i], ventSeconds));
            if (_ventAudio != null && _ventAudio[i] != null && _hissClip != null)
                _ventAudio[i].PlayOneShot(_hissClip, 0.7f);
        }
    }

    static IEnumerator EmitPuff(ParticleSystem ps, float dur)
    {
        const float step = 0.1f;
        float t = 0f;
        while (t < dur)
        {
            if (ps == null) yield break;
            ps.Emit(8);
            t += step;
            yield return new WaitForSeconds(step);
        }
    }

    void BuildVents()
    {
        if (vents == null) return;
        _puffs = new ParticleSystem[vents.Length];
        _ventAudio = new AudioSource[vents.Length];
        for (int i = 0; i < vents.Length; i++)
        {
            if (vents[i] == null) continue;
            _puffs[i] = BuildPuff(vents[i]);
            var a = vents[i].gameObject.AddComponent<AudioSource>();
            a.playOnAwake = false; a.spatialBlend = 1f; a.minDistance = 2f; a.maxDistance = 20f;
            _ventAudio[i] = a;
        }
    }

    // Mirrors Ship.BuildPressurizerPuff — a burst-emitted cloud that plumes along local -Y.
    static ParticleSystem BuildPuff(Transform parent)
    {
        var go = new GameObject("VentPuff");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();
        var main = ps.main;
        main.duration = 1f; main.loop = false; main.playOnAwake = false;
        main.startLifetime = 1.2f; main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.95f, 0.95f, 1f, 0.55f));
        main.maxParticles = 300;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.gravityModifier = 0f;

        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.05f;

        var vel = ps.velocityOverLifetime; vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
        vel.y = new ParticleSystem.MinMaxCurve(-3.6f, -1.8f);

        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var siz = ps.sizeOverLifetime; siz.enabled = true;
        siz.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.8f)));

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.material = ConcertParticleAssets.GetAlphaBlendCloudMaterial();
        return ps;
    }
}
