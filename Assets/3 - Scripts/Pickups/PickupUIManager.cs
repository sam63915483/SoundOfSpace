using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// DefaultExecutionOrder bumped past CameraTransformFX (100) so this manager's
// LateUpdate runs AFTER strafe tilt / headbob / landing dip have written the
// frame's camera rotation. Without this, marker screen positions are computed
// against the pre-tilt camera transform while the 3D scene is rendered with
// the post-tilt camera — markers visibly mis-track the rotating world on
// every strafe-tilt frame, which reads as flicker / disappear-reappear.
[DefaultExecutionOrder(200)]
public class PickupUIManager : MonoBehaviour
{
    public static PickupUIManager Instance { get; private set; }

    // Inspector wiring is retained as an OPTIONAL override path. New scenes
    // get the auto-created singleton; older scenes that wire these in the
    // inspector continue to work without re-saving them.
    [Header("Marker Prefab (auto-built if null)")]
    public GameObject markerPrefab;
    public Transform markerContainer;

    [Header("Player Camera (auto-resolved if null)")]
    public Transform playerCamera;

    Camera _cameraComponent;
    float _nextCameraFindAttempt;
    const float CameraFindRetryInterval = 0.5f;

    private List<PickupMarkerData> activeMarkers = new List<PickupMarkerData>();

    private class PickupMarkerData
    {
        public PickupMarker pickup;
        public GameObject uiInstance;
        public RectTransform uiRect;
        public Image iconImage;
        public TextMeshProUGUI distanceText;
        public CanvasGroup canvasGroup;
        public string namePrefix;
        public int lastDistanceTenths;
        // Per-frame scratch state used by the de-overlap + sibling-sort passes.
        public float distance;
        public bool hidden;
        public float stackPxOffset;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        // Skip in MainMenu scene; the gameplay scene transition seeds it via
        // MainMenuController.EnsureGameplaySingletons. (Pattern matches the
        // other HUDs.)
        var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (active.name == "MainMenu") return;
        if (Instance != null) return;
        var go = new GameObject("PickupUIManager");
        DontDestroyOnLoad(go);
        go.AddComponent<PickupUIManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        EnsureCanvasAndPrefab();
    }

    void EnsureCanvasAndPrefab()
    {
        if (markerContainer == null)
        {
            var canvasGo = new GameObject("PickupMarkerCanvas");
            DontDestroyOnLoad(canvasGo);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UILayer.Hud;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            HUDSceneGate.Register(canvas);
            markerContainer = canvasGo.transform;
        }

        if (markerPrefab == null)
        {
            // Procedural marker: a transparent container with an Image (icon
            // slot — left untouched if no customIcon) plus a TMP text below
            // it. Same shape the legacy inspector-wired prefab had.
            markerPrefab = new GameObject("PickupMarker");
            markerPrefab.hideFlags = HideFlags.HideAndDontSave;
            var rt = markerPrefab.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(80, 100);
            markerPrefab.AddComponent<CanvasGroup>();

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(markerPrefab.transform, false);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.sizeDelta = new Vector2(40, 40);
            iconRt.anchoredPosition = new Vector2(0, 20);
            iconGo.AddComponent<Image>();

            var textGo = new GameObject("Distance", typeof(RectTransform));
            textGo.transform.SetParent(markerPrefab.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.sizeDelta = new Vector2(120, 40);
            textRt.anchoredPosition = new Vector2(0, -20);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 18;
            HudFontResolver.Apply(tmp);
        }
    }

    Camera ResolveCamera()
    {
        if (_cameraComponent != null) return _cameraComponent;
        if (playerCamera != null)
        {
            _cameraComponent = playerCamera.GetComponent<Camera>();
            if (_cameraComponent != null) return _cameraComponent;
        }
        if (Time.unscaledTime < _nextCameraFindAttempt) return null;
        _nextCameraFindAttempt = Time.unscaledTime + CameraFindRetryInterval;
        var cam = Camera.main;
        if (cam != null)
        {
            playerCamera = cam.transform;
            _cameraComponent = cam;
        }
        return _cameraComponent;
    }

    public void RegisterPickup(PickupMarker pickup)
    {
        if (pickup == null) return;
        EnsureCanvasAndPrefab();
        foreach (var data in activeMarkers)
            if (data.pickup == pickup) return;

        GameObject ui = Instantiate(markerPrefab, markerContainer);
        ui.hideFlags = HideFlags.None;
        ui.SetActive(true);
        PickupMarkerData newData = new PickupMarkerData
        {
            pickup = pickup,
            uiInstance = ui,
            uiRect = ui.GetComponent<RectTransform>(),
            iconImage = ui.GetComponentInChildren<Image>(),
            distanceText = ui.GetComponentInChildren<TextMeshProUGUI>(),
            canvasGroup = ui.GetComponent<CanvasGroup>(),
            namePrefix = string.IsNullOrEmpty(pickup.displayName) ? "" : pickup.displayName + "\n",
            lastDistanceTenths = int.MinValue,
        };

        if (pickup.customIcon != null && newData.iconImage != null)
            newData.iconImage.sprite = pickup.customIcon;

        activeMarkers.Add(newData);
    }

    public void UnregisterPickup(PickupMarker pickup)
    {
        for (int i = activeMarkers.Count - 1; i >= 0; i--)
        {
            if (activeMarkers[i].pickup == pickup)
            {
                Destroy(activeMarkers[i].uiInstance);
                activeMarkers.RemoveAt(i);
                break;
            }
        }
    }

    void LateUpdate()
    {
        var cam = ResolveCamera();
        if (cam == null || playerCamera == null) return;

        // Sweep destroyed pickups first so the visible-list iterations below
        // don't trip over a dangling uiInstance.
        for (int i = activeMarkers.Count - 1; i >= 0; i--)
        {
            var data = activeMarkers[i];
            if (data.pickup == null || data.pickup.gameObject == null)
            {
                Destroy(data.uiInstance);
                activeMarkers.RemoveAt(i);
            }
        }

        // First pass: compute fresh screen position + distance for every
        // marker. Visible flag drives sibling sorting in pass two and the
        // de-overlap pass in pass three.
        for (int i = 0; i < activeMarkers.Count; i++)
            UpdateMarker(activeMarkers[i], cam);

        // Second pass: stack markers that project to the same screen pixel.
        // Two ships crashing in the same crater can leave 10+ pickups within
        // a couple of metres of each other, and without this the icons /
        // distance texts pile on top of each other and read as flickery
        // garbage. We bias y by a small step per cluster index so they
        // form a vertical column instead.
        DeOverlapMarkers();

        // Third pass: sibling-sort so closer markers render on top. Without
        // this the render order is "first registered = lowest = behind",
        // which means the second crash's parts can end up rendering behind
        // a sometimes-hidden first crash's parts and look like they're
        // flickering. Closer-to-camera = highest sibling index = front.
        SortByDistanceSibling();
    }

    void UpdateMarker(PickupMarkerData data, Camera cam)
    {
        PickupMarker pickup = data.pickup;
        Transform target = pickup.transform;

        Vector3 worldPos = target.position + target.TransformDirection(pickup.worldOffset);
        Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
        bool isBehind = screenPos.z < 0;

        float distance = Vector3.Distance(playerCamera.position, target.position);
        bool shouldHide = distance <= pickup.hideDistance || isBehind;

        if (data.canvasGroup != null)
            data.canvasGroup.alpha = shouldHide ? 0f : 1f;

        data.distance = distance;
        data.hidden = shouldHide;

        // Always update the rect position, even when hidden — without this,
        // a marker that becomes visible after being hidden snaps in from
        // last-known-stale on the next frame. Cheap (one Vector2 write).
        // No pixel snap: while strafe tilt is animating, the sub-pixel
        // ideal position oscillates around an integer, and a Round() snap
        // flips back and forth between adjacent pixels every frame.
        // Floats let Unity's UI subpixel sampling smooth the motion.
        if (data.uiRect != null)
            data.uiRect.position = new Vector3(screenPos.x, screenPos.y, 0f);

        if (!shouldHide && data.distanceText != null)
        {
            int tenths = Mathf.RoundToInt(distance * 10f);
            if (tenths != data.lastDistanceTenths)
            {
                data.lastDistanceTenths = tenths;
                data.distanceText.text = data.namePrefix + (tenths * 0.1f).ToString("F1") + "m";
            }
        }
    }

    // Vertical-stack markers that share a screen pixel within `clusterPx`.
    // O(N^2) but N is at most a couple dozen pickups — well under any frame
    // budget concern, and the comparisons are pure Vector2 work.
    const float kClusterPxThreshold = 50f;
    const float kClusterPxStep = 60f; // vertical spacing per stacked marker
    void DeOverlapMarkers()
    {
        int n = activeMarkers.Count;
        for (int i = 0; i < n; i++)
        {
            var a = activeMarkers[i];
            if (a.hidden || a.uiRect == null) continue;
            int stackIndex = 0;
            // Count visible markers earlier in the list whose base screen
            // position is within the cluster radius; this becomes our
            // upward offset count.
            for (int j = 0; j < i; j++)
            {
                var b = activeMarkers[j];
                if (b.hidden || b.uiRect == null) continue;
                Vector2 da = a.uiRect.position;
                Vector2 db = b.uiRect.position;
                // Compare against the same baseline (b might have been
                // already-offset; subtract its stack offset to get its base).
                db.y -= b.stackPxOffset;
                if (Vector2.Distance(da, db) <= kClusterPxThreshold) stackIndex++;
            }
            float offset = stackIndex * kClusterPxStep;
            a.stackPxOffset = offset;
            if (offset > 0f)
            {
                Vector3 p = a.uiRect.position;
                p.y += offset;
                a.uiRect.position = p;
            }
        }
    }

    // Sibling-sort visible markers so closer ones render on top. Cheap O(N^2)
    // insertion-style: iterate in distance-descending order, bumping the
    // current marker to the end. Final sibling order: farthest...nearest,
    // and the nearest ends up rendered last (= visually on top).
    void SortByDistanceSibling()
    {
        int n = activeMarkers.Count;
        if (n <= 1) return;
        // Build a small temp list of (distance, uiInstance) sorted desc.
        // Avoid LINQ allocations.
        for (int pass = 0; pass < n - 1; pass++)
        {
            // Find farthest among unsorted prefix
            int farthestIdx = 0;
            for (int i = 1; i < n - pass; i++)
            {
                if (activeMarkers[i].distance > activeMarkers[farthestIdx].distance)
                    farthestIdx = i;
            }
            // Swap to end of unsorted prefix
            var tmp = activeMarkers[farthestIdx];
            activeMarkers[farthestIdx] = activeMarkers[n - 1 - pass];
            activeMarkers[n - 1 - pass] = tmp;
        }
        // Now activeMarkers is ascending by distance (nearest first).
        // Set sibling index so nearest goes LAST (top) — markers render in
        // sibling order, last = on top.
        for (int i = 0; i < n; i++)
        {
            var data = activeMarkers[i];
            if (data.uiInstance == null) continue;
            // Nearest (index 0) gets highest sibling.
            data.uiInstance.transform.SetSiblingIndex(n - 1 - i);
        }
    }
}
