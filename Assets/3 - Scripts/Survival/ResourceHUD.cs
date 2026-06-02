using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ResourceHUD : MonoBehaviour
{
    [Header("Bar Fill RectTransforms (scaled by X)")]
    public RectTransform hungerBarFill;
    public RectTransform thirstBarFill;
    public RectTransform healthBarFill;
    public RectTransform shipPowerBarFill;
    public RectTransform shipFuelBarFill;

    [Header("Bar Fill Images (for pulsing)")]
    public Image hungerBarImage;
    public Image thirstBarImage;
    public Image healthBarImage;
    public Image shipPowerBarImage;
    public Image shipFuelBarImage;

    [Header("Labels")]
    public TMP_Text hungerLabel;
    public TMP_Text thirstLabel;
    public TMP_Text healthLabel;
    public TMP_Text shipPowerLabel;
    public TMP_Text shipFuelLabel;
    public TMP_Text chargingLabel;

    [Header("Solar Panel Charging")]
    public SolarPanelCharger solarCharger;

    [Header("Ship Power Visibility")]
    [Tooltip("The GameObject containing the ship-power row (bar + label). Hidden when the player is not piloting a ship. If left null, auto-resolves to shipPowerLabel.transform.parent.gameObject on Start.")]
    public GameObject shipPowerRow;
    [Tooltip("The GameObject containing the ship-fuel row (bar + label). Auto-cloned from shipPowerRow at startup if null.")]
    public GameObject shipFuelRow;
    [Tooltip("Optional. RectTransform of the vitals panel to shrink when the ship-power row is hidden, so the remaining rows visually fill the gap. Leave null to skip resizing.")]
    public RectTransform panelToShrink;
    [Tooltip("Pixels to add to panelToShrink.sizeDelta.y while the ship-power row is hidden. Negative = panel gets shorter. Tune to match the row's height in the scene.")]
    public float collapsedHeightDelta = -60f;

    [Header("Warning Settings")]
    public float pulseThreshold = 0.25f;
    public float urgentThreshold = 0.10f;
    public float pulseFrequency = 1f;
    public AudioClip warningClip;

    AudioSource audioSource;

    bool hungerWarned;
    bool thirstWarned;
    bool healthWarned;
    bool shipPowerWarned;

    bool _shipRowResolved;
    bool _shipRowVisible = true;
    Vector2 _panelOriginalSize;
    bool _panelSizeCaptured;

    void Awake()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        if (chargingLabel != null) chargingLabel.gameObject.SetActive(false);
        ConfigureCanvasScaling();
    }

    void ConfigureCanvasScaling()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        // Register with HUDSceneGate so this HUD is auto-hidden whenever
        // the active scene is MainMenu. Without this, a saved game returning
        // to the main menu would leave the vitals bars visible over the
        // menu — same pattern Hotbar / VitalsHUD / CompassHUD use.
        HUDSceneGate.Register(canvas.rootCanvas);
        var scaler = canvas.rootCanvas.GetComponent<CanvasScaler>();
        if (scaler == null) return;
        if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) return;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    void Start()
    {
        if (solarCharger == null) solarCharger = FindObjectOfType<SolarPanelCharger>();
        ResolveShipPowerRow();
        if (shipFuelRow == null) AutoCloneFuelRow();
        if (panelToShrink != null && !_panelSizeCaptured)
        {
            _panelOriginalSize = panelToShrink.sizeDelta;
            _panelSizeCaptured = true;
        }
    }

    // Clones the ship-power row to make a ship-fuel row beneath it, then wires
    // the new serialized refs by finding the analogous children inside the clone
    // (matched by relative path from row root). Falls back silently if the
    // source row is null or the clone can't be parented — the fuel UI then just
    // stays invisible and other code paths are null-guarded.
    void AutoCloneFuelRow()
    {
        if (shipPowerRow == null) return;
        var srcRT = shipPowerRow.transform as RectTransform;
        if (srcRT == null) return;
        var parent = srcRT.parent;
        var clone = Instantiate(shipPowerRow, parent);
        clone.name = "ShipFuelRow";
        shipFuelRow = clone;

        // Position immediately below the source row in the layout.
        var dstRT = clone.transform as RectTransform;
        if (dstRT != null)
            dstRT.anchoredPosition = srcRT.anchoredPosition + new Vector2(0f, -60f);

        // Map serialized refs onto matching children inside the clone using
        // the relative-path lookup from shipPowerRow.
        shipFuelBarFill  = FindByRelativePath(shipPowerRow.transform, shipPowerBarFill != null ? shipPowerBarFill.transform : null, clone.transform) as RectTransform;
        var fuelImageT   = FindByRelativePath(shipPowerRow.transform, shipPowerBarImage != null ? shipPowerBarImage.transform : null, clone.transform);
        shipFuelBarImage = fuelImageT != null ? fuelImageT.GetComponent<Image>() : null;
        var fuelLabelT   = FindByRelativePath(shipPowerRow.transform, shipPowerLabel != null ? shipPowerLabel.transform : null, clone.transform);
        shipFuelLabel    = fuelLabelT != null ? fuelLabelT.GetComponent<TMP_Text>() : null;

        // Recolor the fuel bar + label to crystal cyan so the player tells them apart.
        var fuelColor = new Color32(0x8C, 0xE6, 0xFF, 0xFF);
        if (shipFuelBarImage != null) shipFuelBarImage.color = fuelColor;
        if (shipFuelLabel    != null) shipFuelLabel.color    = fuelColor;
    }

    // Walks the parent chain from `descendant` up to `ancestor`, recording the
    // path; then walks the same path under `newRoot` and returns the matching
    // transform. Returns null if descendant isn't actually under ancestor.
    static Transform FindByRelativePath(Transform ancestor, Transform descendant, Transform newRoot)
    {
        if (ancestor == null || descendant == null || newRoot == null) return null;
        var stack = new System.Collections.Generic.List<int>();
        var t = descendant;
        while (t != null && t != ancestor)
        {
            stack.Add(t.GetSiblingIndex());
            t = t.parent;
        }
        if (t != ancestor) return null;
        var cursor = newRoot;
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            int idx = stack[i];
            if (idx < 0 || idx >= cursor.childCount) return null;
            cursor = cursor.GetChild(idx);
        }
        return cursor;
    }

    void ResolveShipPowerRow()
    {
        if (_shipRowResolved) return;
        if (shipPowerRow == null && shipPowerLabel != null)
        {
            // Walk up from the label until we hit the direct child of this HUD root.
            // The scene layout is HUD/Row/BarBG/Label, so the label's parent is the
            // bar background — not the row. Stopping at "direct child of HUD" gives
            // us the row container regardless of how deep the label is nested.
            Transform t = shipPowerLabel.transform;
            Transform hudRoot = transform;
            while (t != null && t.parent != null && t.parent != hudRoot) t = t.parent;
            if (t != null && t.parent == hudRoot) shipPowerRow = t.gameObject;
        }
        _shipRowResolved = true;
    }

    void Update()
    {
        if (ResourceManager.Instance == null) return;

        ApplyShipPowerVisibility(Ship.AnyShipPiloted);

        var piloted    = Ship.PilotedInstance;
        float hunger   = ResourceManager.Instance.HungerPercent;
        float thirst   = ResourceManager.Instance.ThirstPercent;
        float health   = ResourceManager.Instance.HealthPercent;
        float shipPwr  = piloted != null ? piloted.PowerPercent : 0f;
        float shipFuel = piloted != null ? piloted.FuelPercent  : 0f;

        SetBar(hungerBarFill, hunger);
        SetBar(thirstBarFill, thirst);
        SetBar(healthBarFill, health);
        if (_shipRowVisible)
        {
            SetBar(shipPowerBarFill, shipPwr);
            SetBar(shipFuelBarFill,  shipFuel);
        }

        PulseBar(hungerBarImage, hunger);
        PulseBar(thirstBarImage, thirst);
        PulseBar(healthBarImage, health);
        if (_shipRowVisible)
        {
            PulseBar(shipPowerBarImage, shipPwr);
            PulseBar(shipFuelBarImage,  shipFuel);
        }

        CheckWarning(hunger, ref hungerWarned);
        CheckWarning(thirst, ref thirstWarned);
        CheckWarning(health, ref healthWarned);
        if (_shipRowVisible) CheckWarning(shipPwr, ref shipPowerWarned);
        else shipPowerWarned = false;

        if (chargingLabel != null)
        {
            bool charging = _shipRowVisible && solarCharger != null && solarCharger.IsCharging;
            if (chargingLabel.gameObject.activeSelf != charging)
                chargingLabel.gameObject.SetActive(charging);
        }
    }

    void ApplyShipPowerVisibility(bool visible)
    {
        if (visible == _shipRowVisible
            && shipPowerRow != null && shipPowerRow.activeSelf == visible
            && (shipFuelRow == null || shipFuelRow.activeSelf == visible)) return;
        _shipRowVisible = visible;
        if (shipPowerRow != null && shipPowerRow.activeSelf != visible)
            shipPowerRow.SetActive(visible);
        if (shipFuelRow != null && shipFuelRow.activeSelf != visible)
            shipFuelRow.SetActive(visible);
        if (panelToShrink != null && _panelSizeCaptured)
        {
            Vector2 s = _panelOriginalSize;
            if (!visible) s.y += collapsedHeightDelta;
            panelToShrink.sizeDelta = s;
        }
    }

    void SetBar(RectTransform fill, float percent)
    {
        if (fill == null) return;
        fill.localScale = new Vector3(percent, 1f, 1f);
    }

    void PulseBar(Image img, float percent)
    {
        if (img == null) return;
        if (percent < pulseThreshold)
        {
            float t = (Mathf.Sin(Time.time * pulseFrequency * Mathf.PI * 2f) + 1f) * 0.5f;
            Color c = img.color;
            c.a = Mathf.Lerp(0.3f, 1f, t);
            img.color = c;
        }
        else
        {
            Color c = img.color;
            c.a = 1f;
            img.color = c;
        }
    }

    void CheckWarning(float percent, ref bool warned)
    {
        if (percent < urgentThreshold && !warned)
        {
            warned = true;
            if (warningClip != null)
                audioSource.PlayOneShot(warningClip);
        }
        else if (percent >= urgentThreshold && warned)
        {
            warned = false;
        }
    }
}
