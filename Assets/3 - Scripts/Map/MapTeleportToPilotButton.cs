using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Top-middle "TELEPORT TO PILOT" button for the map view. Visible only when
// the player has focused a ship in the legend (two clicks on a ship row).
// Click → SolarSystemMapController.TeleportToFollowedShipPilot() closes the
// map and drops the player into that ship's pilot seat.
public class MapTeleportToPilotButton : MonoBehaviour
{
    Canvas _canvas;
    GameObject _root;
    Button _button;
    TextMeshProUGUI _label;
    SolarSystemMapController _controller;
    bool _mapOpen;

    void Awake()
    {
        BuildUI();
        SetVisible(false);
        if (_canvas != null) _canvas.enabled = false;
    }

    public void Init(SolarSystemMapController controller)
    {
        _controller = controller;
    }

    public void SetMapOpen(bool mapOpen)
    {
        _mapOpen = mapOpen;
        // The map's OpenMap routine disables every non-exempt canvas; we
        // explicitly re-enable our own canvas here so the button can show
        // when a ship is followed. On close, force it off.
        if (_canvas != null) _canvas.enabled = mapOpen;
        if (!mapOpen) SetVisible(false);
    }

    void Update()
    {
        if (_controller == null) return;
        bool shouldShow = _mapOpen && _controller.FollowedShip != null;
        SetVisible(shouldShow);
        if (shouldShow && _label != null)
        {
            var s = _controller.FollowedShip;
            _label.text = $"TELEPORT TO PILOT — {s.name}";
        }
    }

    void SetVisible(bool visible)
    {
        if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
    }

    void OnClickTeleport()
    {
        if (_controller != null) _controller.TeleportToFollowedShipPilot();
    }

    void BuildUI()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = UILayer.Modal; // overlays map + pause when map is open
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        // Mark non-modal — without this, ControllerUINavigator picks this
        // high-sortingOrder canvas as the "top modal" and disables raycasters
        // on every canvas below (including the legend), making the legend
        // unclickable the moment a ship gets focused.
        gameObject.AddComponent<SkipControllerNav>();

        _root = new GameObject("TeleportButtonRoot", typeof(RectTransform));
        _root.transform.SetParent(transform, false);
        var rt = (RectTransform)_root.transform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(540f, 64f);
        rt.anchoredPosition = new Vector2(0f, -32f);

        var bgImg = _root.AddComponent<Image>();
        bgImg.color = new Color32(0x4F, 0x95, 0x4B, 240); // muted forest green — distinct from
                                                          // the orbit-lines green so it reads
                                                          // as "action, not annotation".
        _button = _root.AddComponent<Button>();
        _button.targetGraphic = bgImg;
        var colors = _button.colors;
        colors.normalColor      = new Color32(0x4F, 0x95, 0x4B, 240);
        colors.highlightedColor = new Color32(0x6F, 0xB8, 0x6A, 250);
        colors.pressedColor     = new Color32(0x35, 0x70, 0x33, 250);
        colors.selectedColor    = colors.highlightedColor;
        colors.fadeDuration = 0.1f;
        _button.colors = colors;
        _button.onClick.AddListener(OnClickTeleport);

        // Cyan border to match the map UI's palette.
        var borderGO = new GameObject("Border", typeof(RectTransform), typeof(Image));
        borderGO.transform.SetParent(rt, false);
        var brt = (RectTransform)borderGO.transform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
        var bImg = borderGO.GetComponent<Image>();
        bImg.color = new Color(GalaxyHudKit.BorderCool.r, GalaxyHudKit.BorderCool.g, GalaxyHudKit.BorderCool.b, 0.75f);
        bImg.raycastTarget = false;
        bImg.sprite = GalaxyHudKit.RoundedSprite();
        bImg.type = Image.Type.Sliced;
        bImg.material = null;
        // Force a hollow look — bake transparency in the center using a child
        // image overlay that knocks out the middle. Simpler approach: just
        // tint, no fill. The default Image with a sprite already feels framed.
        bImg.color = new Color(GalaxyHudKit.BorderCool.r, GalaxyHudKit.BorderCool.g, GalaxyHudKit.BorderCool.b, 0f);

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(rt, false);
        var lrt = (RectTransform)labelGO.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(16f, 0f); lrt.offsetMax = new Vector2(-16f, 0f);
        _label = labelGO.AddComponent<TextMeshProUGUI>();
        _label.text = "TELEPORT TO PILOT";
        _label.fontSize = 24;
        _label.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        _label.color = Color.white;
        _label.alignment = TextAlignmentOptions.Center;
        _label.characterSpacing = 4f;
        _label.raycastTarget = false;
    }
}
