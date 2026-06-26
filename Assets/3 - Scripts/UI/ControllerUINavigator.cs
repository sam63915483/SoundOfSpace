using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Scripting;

// Run before StandaloneInputModule (default order 0) so auto-select fires
// FIRST in the frame the player pushes the stick. Otherwise the input module
// processes the stick push, sees no selection, discards the Move event, and
// the player has to push twice — which made it look completely broken in
// built games (the editor was lucky with execution order most of the time).
[UnityEngine.DefaultExecutionOrder(-1000)]
[Preserve]
// Singleton that makes UI panels controller-navigable in a Minecraft-style way:
//
//   1. When a panel becomes active and nothing is selected, it auto-picks the
//      first interactable Selectable in the topmost canvas (sortingOrder).
//   2. Draws a bright yellow rounded border around whatever Selectable is
//      currently focused (Button, Slider, etc.) so the player can see exactly
//      which control they'll activate with A.
//   3. Hides the border when the player is clearly using mouse/keyboard, so
//      mouse-only sessions still look clean.
//
// Unity's StandaloneInputModule already handles directional nav, Submit, and
// Cancel — left stick / D-pad both feed the InputManager's "Horizontal" /
// "Vertical" axes (we wired both above), and JoystickButton 0/1 are mapped
// to "Submit" / "Cancel" in InputManager.asset. The piece that was missing
// was setting an INITIAL selection when a panel opens (Unity normally relies
// on the player mouse-clicking first), and giving that selection a clearly
// visible focus indicator. That's all this script does.
public class ControllerUINavigator : MonoBehaviour
{
    public static ControllerUINavigator Instance { get; private set; }

    Canvas borderCanvas;
    GameObject borderRoot;
    RectTransform borderRT;
    Image borderImage;
    Coroutine pulseRoutine;

    [Preserve]
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[ControllerUINavigator]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ControllerUINavigator>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        EnsureEventSystem();
        BuildBorderUI();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        // Each newly-loaded scene needs its own EventSystem (the EventSystem
        // GameObject doesn't carry across scenes unless it's DontDestroyOnLoad,
        // and not every project scene has one baked in).
        EnsureEventSystem();
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        // Find any existing in the loaded scenes (active or inactive).
        var existing = FindObjectOfType<EventSystem>(true);
        if (existing != null)
        {
            if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true);
            if (!existing.enabled) existing.enabled = true;
            return;
        }
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    void BuildBorderUI()
    {
        // Dedicated overlay canvas at very high sorting order so the border is
        // never occluded by any other UI.
        borderCanvas = gameObject.AddComponent<Canvas>();
        borderCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        borderCanvas.sortingOrder = 32000;

        // ConstantPixelSize so our border math (in screen pixels) renders at
        // the same size regardless of canvas scaler.
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        // Don't add a GraphicRaycaster — the border doesn't accept input.

        var go = new GameObject("Border", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        borderRoot = go;
        borderRT = go.GetComponent<RectTransform>();
        borderRT.anchorMin = new Vector2(0.5f, 0.5f);
        borderRT.anchorMax = new Vector2(0.5f, 0.5f);
        borderRT.pivot = new Vector2(0.5f, 0.5f);

        borderImage = go.AddComponent<Image>();
        borderImage.sprite = MakeBorderSprite();
        borderImage.type = Image.Type.Sliced;
        borderImage.color = new Color(1f, 0.95f, 0.2f, 0.95f); // bright Minecraft-ish yellow
        borderImage.raycastTarget = false;

        borderRoot.SetActive(false);

        pulseRoutine = StartCoroutine(BorderPulse());
    }

    System.Collections.IEnumerator BorderPulse()
    {
        // Subtle alpha pulse so the border feels alive (and so you can tell at
        // a glance the panel is awaiting your input rather than frozen).
        while (this != null)
        {
            if (borderImage != null && borderImage.gameObject.activeInHierarchy)
            {
                float t = (Mathf.Sin(Time.unscaledTime * 4f) + 1f) * 0.5f;
                var c = borderImage.color;
                c.a = Mathf.Lerp(0.65f, 0.95f, t);
                borderImage.color = c;
            }
            yield return null;
        }
    }

    static Sprite _cachedBorder;
    static Sprite MakeBorderSprite()
    {
        if (_cachedBorder != null) return _cachedBorder;

        // Hollow rounded square — sliced edges produce a clean border at any size.
        const int S = 48, B = 5;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            bool inBorderArea = x < B || y < B || x >= S - B || y >= S - B;
            pixels[y * S + x] = inBorderArea ? Color.white : new Color(0, 0, 0, 0);
        }
        tex.SetPixels(pixels);
        tex.Apply();

        _cachedBorder = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f),
                                      100f, 0u, SpriteMeshType.FullRect, new Vector4(B, B, B, B));
        _cachedBorder.name = "ControllerNavBorder";
        return _cachedBorder;
    }

    // Tracks GraphicRaycasters we disabled because a modal canvas opened on
    // top of them. Re-enabled when the modal closes / no longer outranks them.
    readonly System.Collections.Generic.HashSet<GraphicRaycaster> _disabledRaycasters
        = new System.Collections.Generic.HashSet<GraphicRaycaster>();

    // Tracks Selectables whose component we DISABLED (not just `.interactable`)
    // because a modal opened. Re-enabled when the modal closes. We disable
    // the component fully because:
    //   • Selectable.allSelectablesArray only contains components whose
    //     enabled flag is true and whose GameObject is active.
    //   • Unity's StandaloneInputModule.OnMove → FindSelectable iterates
    //     that array, so a disabled component is invisible to nav.
    //   • Setting only .interactable=false leaves the entry in the array;
    //     FindSelectable does check IsInteractable(), but there's a frame-
    //     ordering window where nav can still resolve to the lower button
    //     before our suppression runs. Disabling the component closes
    //     that window and blocks both nav and mouse clicks.
    readonly System.Collections.Generic.HashSet<Selectable> _disabledSelectables
        = new System.Collections.Generic.HashSet<Selectable>();

    // Throttle the FindObjectsOfType-heavy work (canvas/selectable scans) to
    // ~4 Hz. UI panel open/close happens on input events, not every frame.
    // At 0.1s (10 Hz) each scan was still allocating ~4.5 KB (three
    // FindObjectsOfType arrays), totalling ~45 KB/sec of GC pressure.
    // 0.25 s drops that to ~18 KB/sec — focus-snap to a freshly-opened modal
    // is delayed at worst a quarter-second, which is below perceptible.
    // The border-drawing path (UpdateBorder) still runs every frame so the
    // focus ring tracks button motion without visible lag.
    const float NavScanInterval = 0.25f;
    float _navScanTimer;
    Selectable _cachedTopmost;

    // Reusable buffers so the per-frame border/validation path allocates
    // nothing (GetWorldCorners + GetComponentsInParent would otherwise churn
    // the heap every frame the controller UI is active).
    readonly Vector3[] _corners = new Vector3[4];
    static readonly System.Collections.Generic.List<CanvasGroup> _cgScratch = new System.Collections.Generic.List<CanvasGroup>();
    static readonly System.Collections.Generic.List<Canvas> _canvasScratch = new System.Collections.Generic.List<Canvas>();

    void Update()
    {
        var es = EventSystem.current;
        if (es == null) { HideBorder(); return; }

        _navScanTimer -= Time.unscaledDeltaTime;
        if (_navScanTimer <= 0f)
        {
            _navScanTimer = NavScanInterval;
            // Suppress lower canvases' raycasters whenever a strictly-higher
            // sorted canvas exists with selectables. Stops Unity's nav module
            // from navigating into buttons "behind" a modal — the underlying
            // main menu dim/glass etc. doesn't need to be interactive while a
            // modal is open.
            UpdateRaycasterSuppression();
            // _cachedTopmost is ONLY consumed below inside the
            // `if (TutorialGate.ControllerEnabled)` path (focus migration).
            // For keyboard/mouse players we return before ever using it, so
            // skip its extra FindObjectsOfType<Canvas> + recursive selectable
            // walk entirely — this was a pure-waste per-tick cost on KBM.
            _cachedTopmost = TutorialGate.ControllerEnabled
                ? FindFirstSelectableInTopmostPanel()
                : null;
        }

        // KBM mode → no controller, no auto-selection. Clear any selection
        // each frame so Unity's StandaloneInputModule (which can still auto-
        // select on stray controller input) doesn't show the yellow focus
        // ring around a pause-menu button when the player just opens it
        // with the mouse. Player can still mouse-click everything normally,
        // since PointerClick events fire independently of selection state.
        //
        // EXCEPTION: an InputField needs to hold focus to receive typed keys.
        // Without this check, clicking the phone's AI-chat input field gives
        // it focus for one frame, then the next ControllerUINavigator tick
        // clears the selection → onDeselect → caret disappears mid-type.
        if (!TutorialGate.ControllerEnabled)
        {
            if (es.currentSelectedGameObject != null && !IsTextInputSelected(es))
                es.SetSelectedGameObject(null);
            HideBorder();
            return;
        }

        // Always force selection into the topmost panel that has selectables.
        // Two checks:
        //   1. If the current selection isn't valid (destroyed, disabled,
        //      hidden by CanvasGroup/disabled Canvas), pick the topmost.
        //   2. If the current selection IS valid but is on a canvas with a
        //      lower sortingOrder than the topmost, force-migrate to the
        //      topmost. This catches the case where the player just clicked
        //      a button that opened a modal — the Selectable they clicked
        //      is still valid, but selection should snap to the modal.
        // Within the same canvas, leave the player's selection alone so
        // they can navigate freely between buttons in the active panel.
        var topmost = _cachedTopmost;
        var current = es.currentSelectedGameObject;
        bool needsMigration = false;

        if (!IsValidSelection(current))
        {
            needsMigration = true;
        }
        else if (topmost != null)
        {
            var currentCanvas = current.GetComponentInParent<Canvas>();
            var topmostCanvas = topmost.GetComponentInParent<Canvas>();
            if (currentCanvas != null && topmostCanvas != null &&
                currentCanvas != topmostCanvas &&
                topmostCanvas.sortingOrder > currentCanvas.sortingOrder)
            {
                needsMigration = true;
            }
        }

        if (needsMigration)
        {
            if (topmost != null) es.SetSelectedGameObject(topmost.gameObject);
            else es.SetSelectedGameObject(null);
        }

        // Show the bright yellow border ONLY when the controller is actually
        // being used — that's the most-recent input source. Mouse users still
        // see Unity's default selectedColor tint via Selectable.ColorBlock.
        bool useControllerVisual =
            TutorialGate.LastSource == TutorialGate.InputSource.Controller;
        if (!useControllerVisual) { HideBorder(); return; }

        UpdateBorder(es.currentSelectedGameObject);
    }

    static bool IsTextInputSelected(EventSystem es)
    {
        var go = es != null ? es.currentSelectedGameObject : null;
        if (go == null) return false;
        if (go.GetComponent<TMPro.TMP_InputField>() != null) return true;
        if (go.GetComponent<UnityEngine.UI.InputField>() != null) return true;
        return false;
    }

    static bool IsValidSelection(GameObject go)
    {
        if (go == null) return false;
        if (!go.activeInHierarchy) return false;
        // Walk up CanvasGroup chain — if any ancestor's alpha is ~0, the panel
        // is hidden via fade-out (not SetActive), so its buttons are visually
        // absent even though the GameObject is still active. Non-alloc overload
        // (reusable list) — this runs every frame the border is drawn.
        go.transform.GetComponentsInParent(false, _cgScratch);
        for (int i = 0; i < _cgScratch.Count; i++)
            if (_cgScratch[i].alpha < 0.05f) return false;
        // Walk up Canvas chain — a parent canvas with enabled=false hides
        // the entire subtree without disabling activeInHierarchy.
        go.transform.GetComponentsInParent(false, _canvasScratch);
        for (int i = 0; i < _canvasScratch.Count; i++)
            if (!_canvasScratch[i].enabled) return false;
        var sel = go.GetComponent<Selectable>();
        if (sel == null) return false;
        // CRITICAL: also check the Selectable component is itself enabled.
        // Modal suppression disables the component to remove it from
        // FindSelectable, but IsInteractable() doesn't reflect that — it
        // only checks the .interactable field. A disabled Selectable should
        // never be considered a valid current selection.
        if (!sel.enabled) return false;
        return sel.IsInteractable();
    }

    // Walks all active canvases, finds the highest sortingOrder one that has
    // an interactable Selectable, and suppresses everything strictly below it
    // — both GraphicRaycasters (mouse clicks) AND Selectable.interactable
    // (directional nav). Re-enables on close.
    void UpdateRaycasterSuppression()
    {
        // Find the highest-sorted canvas with a selectable.
        Canvas top = null;
        int topOrder = int.MinValue;
        var allCanvases = FindObjectsOfType<Canvas>();
        for (int i = 0; i < allCanvases.Length; i++)
        {
            var c = allCanvases[i];
            if (c == null || !c.enabled) continue;
            if (!c.gameObject.activeInHierarchy) continue;
            if (c == borderCanvas) continue;
            // Tagged canvases (map legend, map teleport banner) aren't modal —
            // they must never become "top" or they'd disable every canvas below.
            if (c.GetComponentInParent<SkipControllerNav>() != null) continue;
            var rc = c.GetComponent<GraphicRaycaster>();
            if (rc == null) continue;
            if (FindFirstSelectableUnder(c.transform) == null) continue;
            if (c.sortingOrder > topOrder) { topOrder = c.sortingOrder; top = c; }
        }

        // Fast path (the common case during gameplay): no modal canvas with
        // selectables is open AND nothing was previously suppressed. The two
        // loops below would be a pure no-op, so skip the expensive
        // FindObjectsOfType<Selectable>(includeInactive:true) scan entirely —
        // that scan walks every (even inactive) object in all loaded scenes
        // and was the single largest per-tick CPU + GC cost in the profiler.
        if (top == null && _disabledRaycasters.Count == 0 && _disabledSelectables.Count == 0)
            return;

        // Disable raycasters on canvases strictly below the top.
        for (int i = 0; i < allCanvases.Length; i++)
        {
            var c = allCanvases[i];
            if (c == null || !c.enabled) continue;
            if (c == borderCanvas) continue;
            var rc = c.GetComponent<GraphicRaycaster>();
            if (rc == null) continue;
            if (top != null && c != top && c.sortingOrder < topOrder)
            {
                if (rc.enabled)
                {
                    rc.enabled = false;
                    _disabledRaycasters.Add(rc);
                }
            }
            else if (_disabledRaycasters.Contains(rc))
            {
                rc.enabled = true;
                _disabledRaycasters.Remove(rc);
            }
        }
        _disabledRaycasters.RemoveWhere(rc => rc == null);

        // Suppress directional nav into selectables under non-top canvases.
        // We disable the Selectable component entirely (not just .interactable)
        // so the entry leaves Selectable.allSelectablesArray and is invisible
        // to FindSelectable.
        //
        // Iterate via FindObjectsOfType<Selectable>(true) so disabled selectables
        // are still in the loop and we can re-enable them when the modal closes.
        // (allSelectablesArray would not include them once we've disabled them.)
        var allSelectables = FindObjectsOfType<Selectable>(includeInactive: true);
        for (int i = 0; i < allSelectables.Length; i++)
        {
            var s = allSelectables[i];
            if (s == null) continue;
            if (!s.gameObject.activeInHierarchy) continue;
            // SkipControllerNav-tagged selectables are NEVER touched here —
            // they're mouse-only by design and keep their original state.
            if (s.GetComponentInParent<SkipControllerNav>() != null)
            {
                if (_disabledSelectables.Contains(s) && !s.enabled)
                {
                    s.enabled = true;
                    _disabledSelectables.Remove(s);
                }
                continue;
            }
            var sCanvas = s.GetComponentInParent<Canvas>();
            if (sCanvas == null) continue;
            bool shouldBeActive = (top == null || sCanvas == top || sCanvas.sortingOrder >= topOrder);
            if (shouldBeActive)
            {
                if (_disabledSelectables.Contains(s))
                {
                    s.enabled = true;
                    _disabledSelectables.Remove(s);
                }
            }
            else
            {
                if (s.enabled)
                {
                    s.enabled = false;
                    _disabledSelectables.Add(s);
                }
            }
        }
        _disabledSelectables.RemoveWhere(s => s == null);
    }

    // Returns the canvas with the strictly-higher sortingOrder than `current`
    // that contains an interactable Selectable, or null if none. Used to
    // migrate focus into modal panels opened on top of other UI.
    Canvas FindHighestCanvasAbove(Canvas current)
    {
        Canvas best = null;
        int bestOrder = current.sortingOrder; // strictly higher
        var allCanvases = FindObjectsOfType<Canvas>();
        for (int i = 0; i < allCanvases.Length; i++)
        {
            var c = allCanvases[i];
            if (c == null || !c.enabled) continue;
            if (!c.gameObject.activeInHierarchy) continue;
            if (c == borderCanvas || c == current) continue;
            var rc = c.GetComponent<GraphicRaycaster>();
            if (rc == null || !rc.enabled) continue;
            if (FindFirstSelectableUnder(c.transform) == null) continue;
            if (c.sortingOrder > bestOrder) { bestOrder = c.sortingOrder; best = c; }
        }
        return best;
    }

    Selectable FindFirstSelectableInTopmostPanel()
    {
        // Find the active canvas with the highest sortingOrder that contains
        // an interactable Selectable. The "topmost" rule matches what the
        // player sees: when a modal opens above the gameplay HUD, focus goes
        // to the modal, not to a stray button under it.
        Canvas best = null;
        int bestOrder = int.MinValue;

        var allCanvases = FindObjectsOfType<Canvas>();
        for (int i = 0; i < allCanvases.Length; i++)
        {
            var c = allCanvases[i];
            if (c == null || !c.enabled) continue;
            if (!c.gameObject.activeInHierarchy) continue;
            if (c == borderCanvas) continue;
            // Skip canvases tagged with SkipControllerNav (e.g. the map legend
            // — controller can't auto-focus those because left-stick controls
            // something else in the same panel).
            if (c.GetComponentInParent<SkipControllerNav>() != null) continue;
            // Skip canvases that don't accept input.
            var rc = c.GetComponent<GraphicRaycaster>();
            if (rc == null || !rc.enabled) continue;
            // Skip canvases without any interactable selectable.
            if (FindFirstSelectableUnder(c.transform) == null) continue;
            if (c.sortingOrder > bestOrder) { bestOrder = c.sortingOrder; best = c; }
        }
        if (best == null) return null;
        return FindFirstSelectableUnder(best.transform);
    }

    static Selectable FindFirstSelectableUnder(Transform parent)
    {
        if (parent == null) return null;
        if (!parent.gameObject.activeInHierarchy) return null;
        // Skip subtrees marked with SkipControllerNav.
        if (parent.GetComponent<SkipControllerNav>() != null) return null;
        // Skip subtrees hidden via CanvasGroup.alpha=0 (fade-out without SetActive).
        var localGroup = parent.GetComponent<CanvasGroup>();
        if (localGroup != null && localGroup.alpha < 0.05f) return null;
        // Skip subtrees rooted at a disabled Canvas (component-disabled but
        // GameObject still active — used by SolarSystemMapController.CloseMap
        // to hide UI without deactivating GameObjects).
        var localCanvas = parent.GetComponent<Canvas>();
        if (localCanvas != null && !localCanvas.enabled) return null;

        var sel = parent.GetComponent<Selectable>();
        if (sel != null && sel.IsInteractable() && sel.gameObject.activeInHierarchy) return sel;

        for (int i = 0; i < parent.childCount; i++)
        {
            var found = FindFirstSelectableUnder(parent.GetChild(i));
            if (found != null) return found;
        }
        return null;
    }

    void UpdateBorder(GameObject selected)
    {
        // Use the same validity rules as IsValidSelection — covers active state,
        // CanvasGroup alpha, and interactability — so the border never draws on
        // a stale or invisible button.
        if (!IsValidSelection(selected)) { HideBorder(); return; }
        var srcRT = selected.transform as RectTransform;
        if (srcRT == null) { HideBorder(); return; }

        borderRoot.SetActive(true);

        // Project the selected element's world corners into screen pixels.
        // RectTransformUtility.WorldToScreenPoint handles all canvas modes
        // uniformly: pass null camera for ScreenSpaceOverlay, the canvas's
        // worldCamera otherwise.
        var srcCanvas = selected.GetComponentInParent<Canvas>();
        Camera cam = (srcCanvas != null && srcCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                     ? srcCanvas.worldCamera
                     : null;

        srcRT.GetWorldCorners(_corners);
        Vector3 minScreen = RectTransformUtility.WorldToScreenPoint(cam, _corners[0]); // bottom-left
        Vector3 maxScreen = RectTransformUtility.WorldToScreenPoint(cam, _corners[2]); // top-right

        float w = maxScreen.x - minScreen.x;
        float h = maxScreen.y - minScreen.y;
        Vector2 center = new Vector2((minScreen.x + maxScreen.x) * 0.5f,
                                     (minScreen.y + maxScreen.y) * 0.5f);

        // Pad slightly so the border visibly surrounds the element rather than
        // sitting flush against it.
        const float pad = 6f;
        borderRT.position = new Vector3(center.x, center.y, 0f);
        borderRT.sizeDelta = new Vector2(w + pad * 2f, h + pad * 2f);
    }

    void HideBorder()
    {
        if (borderRoot != null && borderRoot.activeSelf) borderRoot.SetActive(false);
    }
}
