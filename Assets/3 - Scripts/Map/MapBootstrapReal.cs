using UnityEngine;

[RequireComponent(typeof(SolarSystemMapController))]
public class MapBootstrapReal : MonoBehaviour
{
    bool built;

    void Start() { Build(); }

    void Build()
    {
        if (built) return;
        built = true;

        SolarSystemMapController controller = GetComponent<SolarSystemMapController>();

        // Locate the player's camera; fall back to Camera.main.
        var player = FindObjectOfType<PlayerController>();
        Camera playerCam = player != null ? player.GetComponentInChildren<Camera>(true) : null;
        if (playerCam == null) playerCam = Camera.main;
        if (playerCam == null)
        {
            Debug.LogError("MapBootstrapReal: no player camera found in scene. Map disabled.");
            return;
        }

        // Build the map camera as a sibling of this GameObject.
        var camGO = new GameObject("MapCamera");
        camGO.transform.SetParent(transform, worldPositionStays: false);

        Camera mapCam = camGO.AddComponent<Camera>();
        mapCam.cullingMask     = playerCam.cullingMask;
        mapCam.clearFlags      = playerCam.clearFlags;
        mapCam.backgroundColor = playerCam.backgroundColor;
        mapCam.nearClipPlane   = playerCam.nearClipPlane;
        mapCam.farClipPlane    = playerCam.farClipPlane;
        mapCam.fieldOfView     = playerCam.fieldOfView;
        mapCam.depth           = playerCam.depth + 1f;
        mapCam.allowMSAA       = playerCam.allowMSAA;
        mapCam.allowHDR        = playerCam.allowHDR;
        mapCam.enabled         = false;

        // Audio listener disabled — the player's listener stays authoritative.
        camGO.AddComponent<AudioListener>().enabled = false;

        // Mirror CustomPostProcessing — share the same effects[] reference so atmosphere/ocean look identical.
        var srcCpp = playerCam.GetComponent<CustomPostProcessing>();
        if (srcCpp != null)
        {
            var dstCpp = camGO.AddComponent<CustomPostProcessing>();
            dstCpp.effects = srcCpp.effects;
        }

        camGO.AddComponent<MapCameraRig>();

        // Initial camera position — over the player's shoulder so first frame isn't disorienting.
        camGO.transform.position = playerCam.transform.position;
        camGO.transform.rotation = playerCam.transform.rotation;

        // Build the legend canvas under --- UI ---
        Transform uiSection = FindUiSection();
        var legendGO = new GameObject("MapLegendCanvas");
        if (uiSection != null) legendGO.transform.SetParent(uiSection, worldPositionStays: false);
        var canvas = legendGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UILayer.Map; // above HUD, below pause
        var scaler = legendGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f; // height-matched so panel never exceeds screen vertically
        legendGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        // Stop ControllerUINavigator from auto-focusing legend buttons —
        // left stick is reserved for flying the map camera, so it must
        // not also navigate the legend. Legend stays mouse-clickable.
        legendGO.AddComponent<SkipControllerNav>();
        var legend = legendGO.AddComponent<MapLegendUI>();
        legend.Build(NBodySimulation.Bodies, controller);
        canvas.enabled = false; // hidden until map opens

        // Procedural orbit-line container. Parented under the controller so it
        // doesn't move with any planet — line positions are rebuilt in world
        // space each frame from each body's primary.
        var orbitGO = new GameObject("MapOrbitLines");
        orbitGO.transform.SetParent(transform, worldPositionStays: false);
        var orbitLines = orbitGO.AddComponent<MapOrbitLines>();
        orbitLines.viewCamera = mapCam;
        orbitLines.Init(NBodySimulation.Bodies);

        // Velocity-matched / unmatched toast. Procedural overlay; lives next
        // to the legend canvas under --- UI ---. Visibility is driven by the
        // controller's OpenMap / CloseMap so it never bleeds into gameplay.
        var hudGO = new GameObject("MapVelocityHud");
        if (uiSection != null) hudGO.transform.SetParent(uiSection, worldPositionStays: false);
        var velocityHud = hudGO.AddComponent<MapVelocityHud>();

        // "TELEPORT TO PILOT" button — top-middle of the screen, visible only
        // when a ship in the legend has been clicked-to-focus.
        var teleportGO = new GameObject("MapTeleportToPilotButton");
        if (uiSection != null) teleportGO.transform.SetParent(uiSection, worldPositionStays: false);
        var teleportButton = teleportGO.AddComponent<MapTeleportToPilotButton>();
        teleportButton.Init(controller);

        controller.mapCamera   = mapCam;
        controller.cameraRig   = camGO.GetComponent<MapCameraRig>();
        controller.mainCamera  = playerCam;
        controller.legendCanvas = canvas;
        controller.legendUI    = legend;
        controller.orbitLines  = orbitLines;
        controller.teleportButton = teleportButton;
        controller.velocityHud = velocityHud;
        controller.Init(NBodySimulation.Bodies);
    }

    Transform FindUiSection()
    {
        // Find a root GameObject named exactly "--- UI ---". If none, parent under self.
        var roots = gameObject.scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            if (roots[i].name == "--- UI ---") return roots[i].transform;
        return null;
    }
}
