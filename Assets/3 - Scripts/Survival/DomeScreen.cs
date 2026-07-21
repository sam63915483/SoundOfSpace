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

    [Tooltip("Optional bottom row for planet terraforming progress. Runtime-created under the panel if left empty (keeps the hand-positioned prefab untouched).")]
    public TMP_Text planetText;

    [Tooltip("Seconds between screen refreshes. Cheap; the screen doesn't need per-frame.")]
    [SerializeField] float refreshInterval = 0.25f;

    float _timer;

    void Awake()
    {
        if (dome == null) dome = GetComponentInParent<BubbleDome>();
        EnsurePlanetText();
    }

    // The vent block now holds the tree-production equation, so the planet line
    // gets its own bottom row — created at runtime so the prefab (which Sam
    // positions by hand) never needs rebaking.
    void EnsurePlanetText()
    {
        if (planetText != null || ventText == null) return;
        var go = new GameObject("Planet", typeof(RectTransform));
        go.transform.SetParent(ventText.rectTransform.parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = 15;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.color = new Color(0.62f, 0.72f, 0.81f, 1f);
        t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(360f, 26f);
        rt.anchoredPosition = new Vector2(0f, -150f);
        planetText = t;
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
            : $"PLANET O2  {planet:0}%";
        if (planetText != null) SetText(planetText, planetLine);

        // Offline: no fuel → the emitter is dead. Make that unmistakable.
        if (!dome.HasFuel)
        {
            if (fuelText != null) { SetText(fuelText, "FUEL  0%"); fuelText.color = FuelAlarm; }
            if (fuelFill != null) fuelFill.fillAmount = 0f;
            if (timeText != null) SetText(timeText, "<color=#FF6060>OFFLINE - INSERT CRYSTALS</color>");
            if (ventText != null) SetText(ventText, "");
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

        // The production math, spelled out so the player can plan their planting:
        //   line 1: TREES n ×perTree% + BASE floor% = raw%
        //   line 2: over 100 → how much is excess and the resulting vent rate;
        //           under 100 → how far from starting to vent.
        // BASE is max(emitter minimum, planet's own air let in) — the dome floor.
        if (ventText != null)
        {
            float units = dome.TreeUnitsInside;
            float floor = Mathf.Max(dome.BaseInteriorO2, dome.OutsideO2);
            float raw = dome.RawInteriorO2;
            string eq = $"<color=#8FE8A0>TREES {units:0.#}</color> ×{dome.PerTreeInterior:0.#}%  +  BASE {floor:0}%  =  {raw:0}%";
            string status = raw >= 100f
                ? $"<color=#7FD4FF>EXCESS +{dome.ExcessO2:0}%  →  VENTING +{dome.VentPerMinute:0.#}%/MIN</color>"
                : $"<color=#8FA6BD>+{100f - raw:0}% MORE TO START VENTING</color>";
            SetText(ventText, eq + "\n" + status);
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
