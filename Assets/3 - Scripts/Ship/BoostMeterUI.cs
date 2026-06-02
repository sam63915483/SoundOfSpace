using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BoostMeterUI : MonoBehaviour
{
    [Header("Up Thrust")]
    public Image upThrustFill;
    public TMP_Text upThrustLabel;

    [Header("Down Thrust")]
    public Image downThrustFill;
    public TMP_Text downThrustLabel;

    [Header("Directional Thrust")]
    public Image dirThrustFill;
    public TMP_Text dirThrustLabel;

    PlayerController player;
    CanvasGroup _cg;

    void Awake()
    {
        ConfigureCanvasScaling();
        _cg = GetComponent<CanvasGroup>();
        if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
    }

    void ConfigureCanvasScaling()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var scaler = canvas.rootCanvas.GetComponent<CanvasScaler>();
        if (scaler == null) return;
        if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) return;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    void Update()
    {
        // If the new GForceHUD owns the boost-meter rendering, suppress this legacy canvas.
        if (GForceHUD.Instance != null && GForceHUD.Instance.OwnsBoostMeter)
        {
            if (_cg != null)
            {
                _cg.alpha = 0f;
                _cg.blocksRaycasts = false;
                _cg.interactable = false;
            }
            return;
        }

        if (player == null)
            player = FindObjectOfType<PlayerController>(true);
        if (player == null) return;

        // Hide the entire HUD until the jetpack has been purchased from Alien7.
        bool show = player.JetpackUnlocked;
        if (_cg != null)
        {
            _cg.alpha = show ? 1f : 0f;
            _cg.blocksRaycasts = show;
            _cg.interactable = show;
        }
        if (!show) return;

        upThrustFill.fillAmount = player.JetpackFuelPercent;
        downThrustFill.fillAmount = player.DownThrustFuelPercent;
        if (dirThrustFill) dirThrustFill.fillAmount = player.DirectionalThrustFuelPercent;
    }
}
