using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives the bubble dome's status screen — the blue panel on the emitter. Reads
/// the parent BubbleDome and updates its text/bars: interior O2 %, fuel % + time
/// remaining, and (once the dome is full) the excess O2 being vented to the
/// planet's atmosphere. Pure display; refuelling lives in DomeRefuel.
///
/// The panel's GameObjects (canvas, frame, texts, bars) are authored into the
/// prefab so they're positionable in the Editor; this component only fills in the
/// live values. References are wired by DomeScreenBaker, with a parent-lookup
/// fallback so a hand-placed screen still finds its dome.
/// </summary>
public class DomeScreen : MonoBehaviour
{
    public BubbleDome dome;

    [Header("Text fields")]
    public TMP_Text o2Text;
    public TMP_Text fuelText;
    public TMP_Text timeText;
    public TMP_Text ventText;

    [Header("Bars (Image type = Filled, Horizontal)")]
    public Image o2Fill;
    public Image fuelFill;

    [Tooltip("Seconds between screen refreshes. Cheap; the screen doesn't need per-frame.")]
    [SerializeField] float refreshInterval = 0.25f;

    float _timer;

    void Awake()
    {
        if (dome == null) dome = GetComponentInParent<BubbleDome>();
    }

    void Update()
    {
        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = refreshInterval;
        Refresh();
    }

    static readonly Color FuelNormal = new Color(1f, 0.85f, 0.4f, 1f);
    static readonly Color FuelAlarm  = new Color(1f, 0.25f, 0.2f, 1f);
    const float LowFuelPercent = 10f;   // below this the FUEL readout flashes red

    void Refresh()
    {
        if (dome == null) return;

        float o2 = dome.InteriorO2;
        if (o2Text != null) SetText(o2Text, $"O2  {o2:0}%");
        if (o2Fill != null) o2Fill.fillAmount = Mathf.Clamp01(o2 / 100f);

        // Terraforming progress: the whole point of the dome is the planet line.
        float planet = (dome.Body != null && PlanetOxygen.Instance != null)
            ? PlanetOxygen.Instance.SurfaceO2(dome.Body) : -1f;
        string planetLine = planet < 0f ? ""
            : planet >= 99.5f ? "<color=#9FE8AF>PLANET ATMOSPHERE COMPLETE</color>"
            : $"<color=#9FB8CF>PLANET O2  {planet:0}%</color>";

        // Offline: no fuel → the emitter is dead. Make that unmistakable.
        if (!dome.HasFuel)
        {
            if (fuelText != null) { SetText(fuelText, "FUEL  0%"); fuelText.color = FuelAlarm; }
            if (fuelFill != null) fuelFill.fillAmount = 0f;
            if (timeText != null) SetText(timeText, "<color=#FF6060>OFFLINE - INSERT CRYSTALS</color>");
            if (ventText != null) SetText(ventText, planetLine);
            return;
        }

        float fuel = dome.FuelPercent;
        if (fuelText != null)
        {
            SetText(fuelText, $"FUEL  {fuel:0}%");
            // Low-fuel alarm: flash the readout red so a dying dome is visible
            // from across the interior, not just up close on the time line.
            fuelText.color = (fuel < LowFuelPercent && (int)(Time.unscaledTime * 3f) % 2 == 0)
                ? FuelAlarm : FuelNormal;
        }
        if (fuelFill != null) fuelFill.fillAmount = Mathf.Clamp01(fuel / 100f);
        if (timeText != null) SetText(timeText, FormatTime(dome.SecondsOfFuelLeft) + " left");

        // Excess / venting line while topped out; planet progress always shown.
        if (ventText != null)
        {
            if (dome.IsFull && dome.VentPerMinute > 0f)
                SetText(ventText, $"<color=#7FD4FF>EXCESS +{dome.ExcessO2:0}%   VENTING +{dome.VentPerMinute:0.#}%/min</color>\n{planetLine}");
            else
                SetText(ventText, planetLine);
        }
    }

    static string FormatTime(float s)
    {
        if (s < 0f) s = 0f;
        int total = Mathf.FloorToInt(s);
        int h = total / 3600;
        int m = (total % 3600) / 60;
        int sec = total % 60;
        if (h > 0) return $"{h}h {m:00}m";
        if (m > 0) return $"{m}m {sec:00}s";
        return $"{sec}s";
    }

    static void SetText(TMP_Text t, string s) { if (t.text != s) t.text = s; }
}
