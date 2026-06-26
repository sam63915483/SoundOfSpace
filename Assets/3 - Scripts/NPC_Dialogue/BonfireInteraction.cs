using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class BonfireInteraction : MonoBehaviour
{
    [Header("Panel & Prompt")]
    public GameObject cookPanel;
    public TextMeshProUGUI promptText;

    [Header("Cooking Settings")]
    public float cookDuration          = 10f;
    public float hungerRestoreCommon   = 20f;
    public float hungerRestoreUncommon = 35f;
    public float hungerRestoreRare     = 60f;

    [Header("Sound Effects")]
    [SerializeField] private AudioClip eatClip;
    [SerializeField, Range(0, 1)] private float eatVolume = 0.7f;
    [SerializeField] private AudioClip fireLoopClip;
    [SerializeField, Range(0, 1)] private float fireVolume = 0.5f;
    private AudioSource sfxSource;
    private AudioSource fireSource;

    // Legacy serialized refs kept for scene compatibility (unused in new flow)
    [HideInInspector] public TextMeshProUGUI commonCountText, uncommonCountText, rareCountText;
    [HideInInspector] public TextMeshProUGUI commonStagedText, uncommonStagedText, rareStagedText;
    [HideInInspector] public Button commonAddButton, uncommonAddButton, rareAddButton;
    [HideInInspector] public Button browseFishButton, cookButton, eatButton;
    [HideInInspector] public TextMeshProUGUI timerText, cookStatusText;

    // ── State ──────────────────────────────────────────────────────────────────
    bool playerInRange, panelOpen, isCooking, foodReady;
    float pendingHungerRestore;
    // Phase 4: tuple grows by one element — FishSource for per-fish return-to-
    // exact-source on cancel paths. RenderTexture stays nullable; the picker
    // doesn't produce one, so cook scroll rows render with no thumbnail.
    readonly List<(FishEntry fish, RenderTexture rt, FishSource source)> stagedFish = new List<(FishEntry, RenderTexture, FishSource)>();

    // ── Built UI refs ──────────────────────────────────────────────────────────
    Button   uiAddBtn, uiCookBtn, uiEatBtn;
    TextMeshProUGUI uiListHeader, uiTimer, uiStatus, uiHunger;
    Transform uiListContent;

    // ── Dependencies ───────────────────────────────────────────────────────────
    Coroutine             cookCoroutine;
    FishingRodController  rodCtrl;
    GuitarController      guitarCtrl;
    WaterBottleController bottleCtrl;

    // ── Palette ────────────────────────────────────────────────────────────────
    static Color32 Hex(string h) { ColorUtility.TryParseHtmlString(h, out Color c); return c; }

    static readonly Color32 C_PanelBg  = new Color32(10,  15,  28,  250);
    static readonly Color32 C_CardBg   = new Color32(22,  30,  52,  255);
    static readonly Color32 C_Divider  = new Color32(50,  65,  100, 255);
    static readonly Color32 C_ScrollBg = new Color32(7,   10,  20,  255);
    static readonly Color32 C_Title    = new Color32(255, 165, 40,  255);
    static readonly Color32 C_Label    = new Color32(220, 230, 255, 255);
    static readonly Color32 C_Sub      = new Color32(120, 140, 185, 255);
    static readonly Color32 C_Hint     = new Color32(70,  85,  120, 255);
    static readonly Color32 C_Green    = new Color32(60,  220, 140, 255);
    static readonly Color32 C_Common   = new Color32(140, 175, 255, 255);
    static readonly Color32 C_Uncommon = new Color32(60,  220, 190, 255);
    static readonly Color32 C_Rare     = new Color32(255, 210, 50,  255);
    static readonly Color32 C_BtnAdd   = new Color32(45,  100, 210, 255);
    static readonly Color32 C_BtnCook  = new Color32(195, 95,  20,  255);
    static readonly Color32 C_BtnEat   = new Color32(30,  165, 70,  255);
    static readonly Color32 C_BtnRem   = new Color32(175, 35,  35,  255);

    // ── Unity lifecycle ────────────────────────────────────────────────────────
    void Start()
    {
        rodCtrl    = FindObjectOfType<FishingRodController>();
        guitarCtrl = FindObjectOfType<GuitarController>();
        bottleCtrl = FindObjectOfType<WaterBottleController>();

        // First scene bonfire with a valid cookPanel publishes its refs so
        // placed bonfires (build menu + save load) can read them without
        // re-scanning the scene for a template. First-write-wins so a placed
        // bonfire (which gets cookPanel populated by GhostPlacement AFTER
        // Start) can't overwrite the source.
        if (cookPanel != null && BonfireUIRegistry.CookPanel == null)
        {
            BonfireUIRegistry.CookPanel  = cookPanel;
            BonfireUIRegistry.PromptText = promptText;
        }

        if (cookPanel != null) { HideOldChildren(); BuildUI(); }
        if (cookPanel != null) cookPanel.SetActive(false);
        if (promptText != null) promptText.gameObject.SetActive(false);

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;

        fireSource = gameObject.AddComponent<AudioSource>();
        fireSource.playOnAwake = false;
        fireSource.loop = true;
        fireSource.volume = fireVolume;
    }

    void HideOldChildren()
    {
        foreach (Transform child in cookPanel.transform)
            child.gameObject.SetActive(false);
    }

    // ── Trigger ────────────────────────────────────────────────────────────────
    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = true;
        if (!panelOpen)
            InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to cook");
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = false;
        InteractPromptUI.Clear(this);
        FishingdexManager.Instance?.CloseIfContextOpen();
        if (panelOpen) CloseCookPanel();
    }

    // Cached prompt text — rebuilt only when the InputSource flips (KBM ↔
    // controller), not every frame. The previous version's `$"..."` string
    // interpolation allocated ~1.2 KB per frame even when nothing changed.
    string _promptCached;
    TutorialGate.InputSource _promptCachedSource = (TutorialGate.InputSource)(-1);

    void Update()
    {
        if (!playerInRange) return;
        // Re-assert the "cook" prompt every frame so input-source glyph swaps
        // (F → X on controller) refresh live. Skip while the cook panel is open
        // so the "close" hint set by OpenCookPanel isn't overwritten.
        if (!panelOpen)
        {
            var src = TutorialGate.LastSource;
            if (_promptCached == null || src != _promptCachedSource)
            {
                _promptCachedSource = src;
                _promptCached = $"Press {PromptGlyphs.Interact} to cook";
            }
            InteractPromptUI.Show(this, _promptCached);
        }
        if (TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
        {
            // Close works regardless of where you're looking (don't trap the
            // player in the menu); opening requires looking at the bonfire.
            if (panelOpen) CloseCookPanel();
            else if (InteractGaze.IsLookingAt(this)) OpenCookPanel();
        }
    }

    // Fired when any bonfire's cook panel opens. Subscribed to by tutorial
    // steps (OpenCookPanelStep) to detect first-time interaction. Static
    // because there can be many bonfires in the world but only one tutorial.
    public static event System.Action OnPanelOpened;

    // Fired when the player clicks the Eat button after a successful cook.
    // CookAndEatStep subscribes — checking hunger delta isn't reliable
    // because hunger caps at 100%, so eating with full hunger looks like a
    // no-op.
    public static event System.Action OnEat;

    // ── Open / Close ───────────────────────────────────────────────────────────
    void OpenCookPanel()
    {
        if (cookPanel == null)
        {
            Debug.LogWarning($"[BonfireInteraction] OpenCookPanel skipped on '{name}' — cookPanel is unassigned.");
            return;
        }
        panelOpen = true;
        PlayerController.isInDialogue = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        OnPanelOpened?.Invoke();

        if (rodCtrl    == null) rodCtrl    = FindObjectOfType<FishingRodController>();
        if (guitarCtrl == null) guitarCtrl = FindObjectOfType<GuitarController>();
        if (bottleCtrl == null) bottleCtrl = FindObjectOfType<WaterBottleController>();
        rodCtrl?.ForceUnequipRod();
        guitarCtrl?.ForceUnequipGuitar();
        bottleCtrl?.ForceUnequipBottle();

        // Only reset cooking state on a *fresh* open. If we closed mid-cook (or
        // with food ready), the coroutine has been running in the background
        // and the staged fish are mid-cook — preserve all of that so the
        // player just sees the live timer / Eat button on re-open.
        if (!isCooking && !foodReady)
        {
            // Return any previously staged fish before resetting
            foreach (var (f, rt, src) in stagedFish) { if (!FishStagingUI.TryReturnTo(f, src)) InventoryFullPopup.Show(); ReleaseRT(rt); }
            stagedFish.Clear();
            pendingHungerRestore = 0f;
        }

        cookPanel.SetActive(true);
        // Hide the floating "Press F" prompt while the cook panel is open —
        // the panel itself is the active UI, so the redundant world-space
        // prompt was just visual noise. Update() re-asserts the prompt
        // automatically once the panel closes.
        InteractPromptUI.Clear(this);
        RefreshUI();
    }

    static void ReleaseRT(RenderTexture rt) { if (rt != null) { rt.Release(); Destroy(rt); } }

    void CloseCookPanel()
    {
        if (cookPanel == null)
        {
            panelOpen = false;
            return;
        }
        // If a cook is in progress (or food is ready and waiting to be eaten),
        // closing the panel is just a *hide* — let the coroutine keep running
        // in the background. The player can walk away, come back, and re-open
        // to either watch the remaining timer or hit Eat.
        bool cookInFlight = isCooking || foodReady;

        if (!cookInFlight)
        {
            // Return any staged fish on close and release their thumbnails.
            // Only when nothing is cooking — otherwise stagedFish IS the
            // currently-cooking batch and must be left alone.
            foreach (var (f, rt, src) in stagedFish) { if (!FishStagingUI.TryReturnTo(f, src)) InventoryFullPopup.Show(); ReleaseRT(rt); }
            stagedFish.Clear();
        }

        panelOpen = false;
        PlayerController.isInDialogue = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        cookPanel.SetActive(false);
        // If the player is still standing in the trigger zone, swap back to
        // the "cook" prompt; otherwise clear (OnTriggerExit will have done
        // this already, but be defensive).
        if (playerInRange)
            InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to cook");
        else
            InteractPromptUI.Clear(this);

        if (!cookInFlight)
        {
            // Only tear down the coroutine + fire SFX when no cook is mid-flight.
            // Mid-flight close keeps the coroutine alive (the fire keeps crackling
            // — that's the audible "cooking is still happening" cue).
            if (cookCoroutine != null) { StopCoroutine(cookCoroutine); cookCoroutine = null; }
            if (fireSource != null && fireSource.isPlaying) fireSource.Stop();
            isCooking = false;
            foodReady = false;
        }
    }

    // ── Callbacks ──────────────────────────────────────────────────────────────
    void OnAddFishClicked()
    {
        if (isCooking || foodReady) return;
        if (FishStagingUI.Instance == null) return;
        FishStagingUI.Instance.Open("COOK FISH", entries =>
        {
            foreach (var (fish, source) in entries)
                stagedFish.Add((fish, null, source));
            RefreshUI();
        });
    }

    void OnRemoveFish(FishEntry entry)
    {
        int idx = stagedFish.FindIndex(x => x.fish == entry);
        if (idx < 0) return;
        var (f, rt, src) = stagedFish[idx];
        stagedFish.RemoveAt(idx);
        // Phase 4: return to exact original source via shared chain.
        if (!FishStagingUI.TryReturnTo(f, src)) InventoryFullPopup.Show();
        if (rt != null) ReleaseRT(rt);
        RefreshUI();
    }

    void OnCookClicked()
    {
        if (isCooking || foodReady || stagedFish.Count == 0) return;

        pendingHungerRestore = 0f;
        foreach (var (f, _, _) in stagedFish)
            pendingHungerRestore += f.fishType == "Rare"     ? hungerRestoreRare
                                  : f.fishType == "Uncommon" ? hungerRestoreUncommon
                                  :                            hungerRestoreCommon;

        // Fish already removed from inventory when staged
        cookCoroutine = StartCoroutine(CookRoutine());
    }

    void OnEatClicked()
    {
        if (!foodReady) return;
        if (eatClip != null && sfxSource != null)
            sfxSource.PlayOneShot(eatClip, eatVolume);
        ResourceManager.Instance?.ConsumeFood(pendingHungerRestore);
        OnEat?.Invoke();
        foodReady            = false;
        pendingHungerRestore = 0f;
        stagedFish.Clear();
        RefreshUI();
    }

    IEnumerator CookRoutine()
    {
        isCooking = true;
        RefreshUI();

        if (fireLoopClip != null && fireSource != null)
        {
            fireSource.clip = fireLoopClip;
            fireSource.volume = fireVolume;
            fireSource.Play();
        }

        float elapsed = 0f;
        while (elapsed < cookDuration)
        {
            elapsed += Time.deltaTime;
            float rem = cookDuration - elapsed;
            if (uiTimer != null) uiTimer.text = $"Cooking...  {rem:F1}s";
            yield return null;
        }

        if (fireSource != null && fireSource.isPlaying)
            fireSource.Stop();

        isCooking     = false;
        foodReady     = true;
        // Fish consumed; release their thumbnails
        foreach (var (_, rt, _) in stagedFish) ReleaseRT(rt);
        stagedFish.Clear();
        cookCoroutine = null;
        RefreshUI();
    }

    // ── UI Refresh ─────────────────────────────────────────────────────────────
    void RefreshUI()
    {
        if (uiListHeader != null)
            uiListHeader.text = $"Added Fish  ({stagedFish.Count})";

        RebuildFishCards();
        if (uiListContent != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(uiListContent.GetComponent<RectTransform>());
        Canvas.ForceUpdateCanvases();

        // Hunger total preview
        if (uiHunger != null)
        {
            if (stagedFish.Count > 0 && !isCooking && !foodReady)
            {
                float total = 0f;
                foreach (var (f, _, _) in stagedFish)
                    total += f.fishType == "Rare" ? hungerRestoreRare
                           : f.fishType == "Uncommon" ? hungerRestoreUncommon
                           : hungerRestoreCommon;
                uiHunger.text = $"Total hunger restore:  +{total:F0}";
            }
            else uiHunger.text = "";
        }

        if (uiTimer != null) uiTimer.gameObject.SetActive(isCooking);

        if (uiStatus != null)
        {
            if (foodReady)      uiStatus.text = "Your food is ready!  Press Eat to consume.";
            else if (isCooking) uiStatus.text = "";
            else if (stagedFish.Count == 0) uiStatus.text = "Add fish, then press Cook.";
            else uiStatus.text = $"{stagedFish.Count} fish added.  Ready to cook!";
        }

        bool canAdd  = !isCooking && !foodReady;
        bool canCook = canAdd && stagedFish.Count > 0;

        if (uiAddBtn  != null) { uiAddBtn.gameObject.SetActive(canAdd);  uiAddBtn.interactable  = canAdd; }
        if (uiCookBtn != null) { uiCookBtn.gameObject.SetActive(!isCooking && !foodReady); uiCookBtn.interactable = canCook; }
        if (uiEatBtn  != null) { uiEatBtn.gameObject.SetActive(foodReady); uiEatBtn.interactable = foodReady; }
    }

    void RebuildFishCards()
    {
        if (uiListContent == null) return;

        for (int i = uiListContent.childCount - 1; i >= 0; i--)
        {
            var child = uiListContent.GetChild(i);
            if (child.name != "EmptyHint") DestroyImmediate(child.gameObject);
        }

        var emptyHint = uiListContent.Find("EmptyHint");
        if (emptyHint != null) emptyHint.gameObject.SetActive(stagedFish.Count == 0);

        bool canRemove = !isCooking && !foodReady;
        foreach (var (f, rt, _) in stagedFish)
        {
            var captured = f;
            float hunger = f.fishType == "Rare"     ? hungerRestoreRare
                         : f.fishType == "Uncommon" ? hungerRestoreUncommon
                         : hungerRestoreCommon;
            Color32 dot  = f.fishType == "Rare"     ? C_Rare
                         : f.fishType == "Uncommon" ? C_Uncommon
                         : C_Common;
            string detail = $"+{hunger:F0} hunger  |  {f.weightLbs} lbs";

            MkFishCard(uiListContent, f.fishType, detail, dot, rt,
                       canRemove ? () => OnRemoveFish(captured) : (System.Action)null);
        }
    }

    // ── UI Build ───────────────────────────────────────────────────────────────
    void BuildUI()
    {
        var panelImg = cookPanel.GetComponent<Image>() ?? cookPanel.AddComponent<Image>();
        panelImg.color = C_PanelBg;
        panelImg.type  = Image.Type.Simple;

        var panelRT = cookPanel.GetComponent<RectTransform>();
        panelRT.sizeDelta = new Vector2(440, 550);
        var panelLE = cookPanel.GetComponent<LayoutElement>() ?? cookPanel.AddComponent<LayoutElement>();
        panelLE.ignoreLayout = true;

        // Remove any existing layout group and add a fresh one
        var existingVLG = cookPanel.GetComponent<VerticalLayoutGroup>();
        if (existingVLG != null) DestroyImmediate(existingVLG);
        var vlg = cookPanel.AddComponent<VerticalLayoutGroup>();
        vlg.padding              = new RectOffset(18, 18, 18, 18);
        vlg.spacing              = 9;
        vlg.childControlWidth    = true;
        vlg.childControlHeight   = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        // Title
        MkText(cookPanel.transform, "BONFIRE COOKING", 21, C_Title, 42, FontStyles.Bold, TextAlignmentOptions.Center);
        MkDivider(cookPanel.transform);

        // List header
        uiListHeader = MkText(cookPanel.transform, "Added Fish  (0)", 13, C_Sub, 22);

        // Scroll list
        uiListContent = MkScrollArea(cookPanel.transform, 215);

        // Hunger preview row
        uiHunger = MkText(cookPanel.transform, "", 13, C_Green, 20, FontStyles.Normal, TextAlignmentOptions.Right);

        MkDivider(cookPanel.transform);

        // Cooking timer (hidden until cooking starts)
        uiTimer = MkText(cookPanel.transform, "", 16, C_Title, 28, FontStyles.Bold, TextAlignmentOptions.Center);
        uiTimer.gameObject.SetActive(false);

        // Status message
        uiStatus = MkText(cookPanel.transform, "Add fish, then press Cook.", 13, C_Sub, 22, FontStyles.Normal, TextAlignmentOptions.Center);

        // Button row
        var row = MkHRow(cookPanel.transform, 48);
        uiAddBtn  = MkButton(row, "Add Fish", C_BtnAdd);
        uiCookBtn = MkButton(row, "Cook",     C_BtnCook);
        uiEatBtn  = MkButton(row, "Eat",      C_BtnEat);

        uiAddBtn.onClick.AddListener(OnAddFishClicked);
        uiCookBtn.onClick.AddListener(OnCookClicked);
        uiEatBtn.onClick.AddListener(OnEatClicked);

    }

    // ── UI Helpers ─────────────────────────────────────────────────────────────
    static TextMeshProUGUI MkText(Transform parent, string text, int size, Color32 color, float height,
        FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        var go  = new GameObject("Txt", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le  = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color     = color;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        return tmp;
    }

    static void MkDivider(Transform parent)
    {
        var go  = new GameObject("Divider", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le  = go.AddComponent<LayoutElement>();
        le.preferredHeight = 1;
        var img = go.AddComponent<Image>();
        img.color = C_Divider;
    }

    static Transform MkScrollArea(Transform parent, float height)
    {
        var scrollGO  = new GameObject("ScrollArea", typeof(RectTransform));
        scrollGO.transform.SetParent(parent, false);
        var scrollLE  = scrollGO.AddComponent<LayoutElement>();
        scrollLE.preferredHeight = height;
        var scrollImg = scrollGO.AddComponent<Image>();
        scrollImg.color = C_ScrollBg;
        var sr = scrollGO.AddComponent<ScrollRect>();

        var vpGO = new GameObject("Viewport", typeof(RectTransform));
        vpGO.transform.SetParent(scrollGO.transform, false);
        var vpRT = vpGO.GetComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
        vpRT.sizeDelta = Vector2.zero; vpRT.offsetMin = Vector2.zero; vpRT.offsetMax = Vector2.zero;
        vpGO.AddComponent<RectMask2D>();

        var cGO = new GameObject("Content", typeof(RectTransform));
        cGO.transform.SetParent(vpGO.transform, false);
        var cRT = cGO.GetComponent<RectTransform>();
        cRT.anchorMin = new Vector2(0, 1); cRT.anchorMax = new Vector2(1, 1);
        cRT.pivot     = new Vector2(0.5f, 1);
        cRT.offsetMin = Vector2.zero;      cRT.offsetMax = Vector2.zero;
        var cVLG = cGO.AddComponent<VerticalLayoutGroup>();
        cVLG.padding = new RectOffset(7, 7, 7, 7);
        cVLG.spacing = 5;
        cVLG.childControlWidth = true; cVLG.childControlHeight = true;
        cVLG.childForceExpandWidth = true; cVLG.childForceExpandHeight = false;
        var csf = cGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        sr.viewport          = vpRT;
        sr.content           = cRT;
        sr.horizontal        = false;
        sr.vertical          = true;
        sr.scrollSensitivity = 30;
        sr.movementType      = ScrollRect.MovementType.Clamped;

        // Empty hint
        var hint = new GameObject("EmptyHint", typeof(RectTransform));
        hint.transform.SetParent(cGO.transform, false);
        hint.AddComponent<LayoutElement>().preferredHeight = 80;
        var hTMP = hint.AddComponent<TextMeshProUGUI>();
        hTMP.text      = "No fish added yet.\nClick  Add Fish  below.";
        hTMP.fontSize  = 13;
        hTMP.alignment = TextAlignmentOptions.Center;
        hTMP.color     = C_Hint;

        return cGO.transform;
    }

    static Transform MkHRow(Transform parent, float height)
    {
        var go  = new GameObject("HRow", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().preferredHeight = height;
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;
        return go.transform;
    }

    static Button MkButton(Transform parent, string label, Color32 bg, int fontSize = 15)
    {
        var go  = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = bg;
        var btn = go.AddComponent<Button>();
        var col = btn.colors;
        col.normalColor      = bg;
        col.highlightedColor = new Color32(
            (byte)Mathf.Clamp(bg.r + 40, 0, 255),
            (byte)Mathf.Clamp(bg.g + 40, 0, 255),
            (byte)Mathf.Clamp(bg.b + 40, 0, 255), 255);
        col.pressedColor = new Color32(
            (byte)Mathf.Clamp(bg.r - 40, 0, 255),
            (byte)Mathf.Clamp(bg.g - 40, 0, 255),
            (byte)Mathf.Clamp(bg.b - 40, 0, 255), 255);
        btn.colors = col;

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(go.transform, false);
        var lblRT = lblGO.GetComponent<RectTransform>();
        lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
        lblRT.sizeDelta = Vector2.zero; lblRT.offsetMin = Vector2.zero; lblRT.offsetMax = Vector2.zero;
        var tmp = lblGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        return btn;
    }

    static void MkFishCard(Transform parent, string fishType, string detail, Color32 dotColor, RenderTexture preview, System.Action onRemove)
    {
        var card = new GameObject("FishCard", typeof(RectTransform));
        card.transform.SetParent(parent, false);
        card.AddComponent<LayoutElement>().preferredHeight = 70;
        var cardImg = card.AddComponent<Image>();
        cardImg.color = C_CardBg;
        var hlg = card.AddComponent<HorizontalLayoutGroup>();
        hlg.padding  = new RectOffset(8, 8, 6, 6);
        hlg.spacing  = 8;
        hlg.childControlHeight = true; hlg.childControlWidth = true;
        hlg.childForceExpandHeight = true; hlg.childForceExpandWidth = false;

        // Fish preview image or rarity strip
        if (preview != null)
        {
            var imgGO = new GameObject("Preview", typeof(RectTransform));
            imgGO.transform.SetParent(card.transform, false);
            var imgLE = imgGO.AddComponent<LayoutElement>();
            imgLE.preferredWidth = 58; imgLE.flexibleHeight = 1;
            var raw = imgGO.AddComponent<RawImage>();
            raw.texture = preview;
        }
        else
        {
            var dot = new GameObject("Dot", typeof(RectTransform));
            dot.transform.SetParent(card.transform, false);
            dot.AddComponent<LayoutElement>().preferredWidth = 5;
            dot.AddComponent<Image>().color = dotColor;
        }

        // Info column
        var info = new GameObject("Info", typeof(RectTransform));
        info.transform.SetParent(card.transform, false);
        var infoLE = info.AddComponent<LayoutElement>();
        infoLE.flexibleWidth = 1;
        var infoVLG = info.AddComponent<VerticalLayoutGroup>();
        infoVLG.childControlWidth = true; infoVLG.childControlHeight = true;
        infoVLG.childForceExpandWidth = true; infoVLG.spacing = 2;

        var nameGO = new GameObject("Name", typeof(RectTransform));
        nameGO.transform.SetParent(info.transform, false);
        nameGO.AddComponent<LayoutElement>().preferredHeight = 20;
        var nameTMP = nameGO.AddComponent<TextMeshProUGUI>();
        nameTMP.text = fishType;
        nameTMP.fontSize = 14;
        nameTMP.fontStyle = FontStyles.Bold;
        nameTMP.color = C_Label;

        var detGO = new GameObject("Detail", typeof(RectTransform));
        detGO.transform.SetParent(info.transform, false);
        detGO.AddComponent<LayoutElement>().preferredHeight = 17;
        var detTMP = detGO.AddComponent<TextMeshProUGUI>();
        detTMP.text = detail;
        detTMP.fontSize = 11;
        detTMP.color = C_Sub;

        // Remove button
        if (onRemove != null)
        {
            var remGO = new GameObject("RemoveBtn", typeof(RectTransform));
            remGO.transform.SetParent(card.transform, false);
            var remLE = remGO.AddComponent<LayoutElement>();
            remLE.preferredWidth = 34; remLE.preferredHeight = 34;
            var remImg = remGO.AddComponent<Image>();
            remImg.color = C_BtnRem;
            var remBtn = remGO.AddComponent<Button>();
            var rCol = remBtn.colors;
            rCol.normalColor = C_BtnRem;
            rCol.highlightedColor = new Color32(220, 60, 60, 255);
            rCol.pressedColor     = new Color32(120, 20, 20, 255);
            remBtn.colors = rCol;
            remBtn.onClick.AddListener(() => onRemove());

            var rLbl = new GameObject("X", typeof(RectTransform));
            rLbl.transform.SetParent(remGO.transform, false);
            var rRT = rLbl.GetComponent<RectTransform>();
            rRT.anchorMin = Vector2.zero; rRT.anchorMax = Vector2.one;
            rRT.sizeDelta = Vector2.zero; rRT.offsetMin = Vector2.zero; rRT.offsetMax = Vector2.zero;
            var rTMP = rLbl.AddComponent<TextMeshProUGUI>();
            rTMP.text      = "X";
            rTMP.fontSize  = 14;
            rTMP.fontStyle = FontStyles.Bold;
            rTMP.color     = Color.white;
            rTMP.alignment = TextAlignmentOptions.Center;
        }
    }
}
