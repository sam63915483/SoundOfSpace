using UnityEngine;

// Physical green/red landing lamp on the console (handoff §6). Sam places a
// small mesh named "LandingLamp" anywhere under the shuttle (the prefab patch
// tool adds a default one on the console); this drives its emission by phase:
// green = hover + landing valid, red = hover + invalid, dim amber = any other
// flight phase, off = parked. ReactorGlow's material-instancing recipe —
// .materials[] auto-instances so nothing leaks to the shared asset.
public class LandingLamp : MonoBehaviour
{
    static readonly Color Green = new Color(0.1f, 1f, 0.25f);
    static readonly Color Red   = new Color(1f, 0.12f, 0.08f);
    static readonly Color Amber = new Color(1f, 0.55f, 0.1f);
    const float Intensity = 2.2f;

    Material _mat;
    Light _light;

    void Awake()
    {
        var mr = GetComponent<MeshRenderer>();
        if (mr == null) return;
        var mats = mr.materials;                     // instances — safe to edit
        if (mats.Length == 0) return;
        _mat = mats[0];
        _mat.EnableKeyword("_EMISSION");
        _mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

        var lightGo = new GameObject("LampLight");
        lightGo.transform.SetParent(transform, false);
        _light = lightGo.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.range = 1.5f;
        _light.intensity = 0f;
        Apply(Color.black, 0f);
    }

    public void SetPhase(ShuttleAutopilot.Phase phase, bool landingValid)
    {
        if (_mat == null) return;
        switch (phase)
        {
            case ShuttleAutopilot.Phase.Hover:
            case ShuttleAutopilot.Phase.Landing:
                Apply(landingValid ? Green : Red, 1f);
                break;
            case ShuttleAutopilot.Phase.Parked:
                Apply(Color.black, 0f);
                break;
            default:
                Apply(Amber, 0.5f);
                break;
        }
    }

    void Apply(Color c, float lightScale)
    {
        _mat.SetColor("_EmissionColor", c * Intensity);
        _mat.color = c == Color.black ? Color.gray : c;
        if (_light != null)
        {
            _light.color = c;
            _light.intensity = 0.8f * lightScale;
        }
    }
}
