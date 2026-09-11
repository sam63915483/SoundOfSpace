using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// "Meow! Spare a fish?" → yes/no → pick any fish → the case opens.
///
/// ── Why the spoken half is not built here ────────────────────────────────
/// It used to be, and it never matched Tev or the aliens. Twice I rebuilt the
/// plate by copying PhosphorDialogueBox's numbers and twice it came out wrong,
/// because the numbers are not the whole story: the real box ADOPTS the one
/// shared HUD label (NPCDialogue.dialogueText), on whatever canvas that happens
/// to live on, and PhosphorDialogueBox restyles it in place.
///
/// So the cat now speaks through that same shared label and offers its choices
/// through the same <see cref="PostGreetingChoicePanel"/> every other NPC uses.
/// The cat's box is not "made to look like" the others — it IS the other box.
/// It cannot drift, because there is nothing here left to drift.
///
/// Only the shop-like half is bespoke, exactly as the vendor menus are: the
/// fish picker, the case reel and the result card.
///
/// Rules (Sam, 2026-09-10): ONE perk slot; a new roll REPLACES the running one
/// and resets the timer to the new tier's full length; no confirmation box; no
/// cat cooldown.
/// </summary>
public class CatPerkTradeUI : MonoBehaviour
{
    public static CatPerkTradeUI Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance._open;

    /// TabbedPauseMenu checks this so the Esc that closes the cat panel does not
    /// also open the pause menu in the same frame.
    static int s_escFrame = -1;
    public static bool ConsumedEscapeThisFrame => s_escFrame == Time.frameCount;
    public static SpaceCat Target => Instance != null ? Instance._cat : null;

    [Header("Audio (optional — wire in Audio Studio)")]
    public AudioClip meowClip;
    public AudioClip tickClip;
    public AudioClip winClip;
    [Range(0f, 1f)] public float volume = 0.7f;

    // Reel geometry, on a 1920x1080 canvas — this half is ours, not the dialogue
    // system's, so it uses the project's usual UI reference size.
    const int   CardCount   = 56;
    const int   WinnerIndex = 46;
    const float CardHeight  = 78f;
    const float CardGap     = 6f;
    const float Pitch       = CardHeight + CardGap;
    const float ViewHeight  = 300f;
    const float ViewWidth   = 264f;
    const float ReelSeconds = 5.4f;
    const float TypeDelay   = 0.018f;

    bool _open;
    SpaceCat _cat;
    Canvas _canvas;
    AudioSource _audio;
    Coroutine _flow;

    TextMeshProUGUI _shared;          // the borrowed NPCDialogue label

    RectTransform _case, _strip, _result;
    readonly List<RectTransform> _cards = new List<RectTransform>();
    readonly List<FishEntry> _fishBuf = new List<FishEntry>();
    Coroutine _reel;
    int _choice = -1;
    static Texture2D _fadeTex;

    // ── lifecycle ────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        // Trap #1: never auto-creates in a build (first scene is MainMenu), so
        // it is also seeded in MainMenuController.EnsureGameplaySingletons.
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("[CatPerkTradeUI]");
        DontDestroyOnLoad(go);
        go.AddComponent<CatPerkTradeUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    /// A scene change kills the conversation. Closing hard matters less for the
    /// visuals than for isInModalSlotUI: that is a static, so leaving it true
    /// survives into the next New Game and locks the player out of their own
    /// controls (the MushroomSellUI bug). The borrowed label dies with the old
    /// scene, so drop it too.
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _shared = null;
        if (_open) Close();
    }

    // ── public entry ─────────────────────────────────────────────────────

    public static void Open(SpaceCat cat)
    {
        if (Instance == null || cat == null) return;
        Instance.OpenInternal(cat);
    }

    public static void Close()
    {
        if (Instance != null) Instance.CloseInternal();
    }

    void OpenInternal(SpaceCat cat)
    {
        if (_open) return;
        _open = true;
        _cat = cat;
        _canvas.gameObject.SetActive(true);
        _case.gameObject.SetActive(false);
        _result.gameObject.SetActive(false);

        PlayerController.isInModalSlotUI = true;
        // Own this flag the way every other dialogue owner does. It drives the
        // letterbox bars AND the player's input gate, and the cat was borrowing
        // NPCDialogue's LABEL without ever going through NPCDialogue's own
        // start/stop path -- so nothing was ever responsible for clearing it.
        PlayerController.isInDialogue = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // The same hook every talking NPC fires — it is what puts the speaker
        // name in the phosphor header.
        NPCConversationTracker.NotifyStart(cat);

        _lastVisibleAt = Time.unscaledTime;
        Play(meowClip);
        _flow = StartCoroutine(Flow());
    }

    void CloseInternal()
    {
        if (!_open) return;
        _open = false;
        _cat = null;
        if (_flow != null) { StopCoroutine(_flow); _flow = null; }
        if (_reel != null) { StopCoroutine(_reel); _reel = null; }

        HideChoices();
        if (_shared != null) _shared.gameObject.SetActive(false);
        _canvas.gameObject.SetActive(false);

        PlayerController.isInModalSlotUI = false;
        PlayerController.isInDialogue = false;     // drops the letterbox bars
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    float _lastVisibleAt;

    static bool PostGreetingChoicePanelVisible =>
        PostGreetingChoicePanel.Instance != null && PostGreetingChoicePanel.Instance.IsVisible;

    void Update()
    {
        if (!_open) return;
        // Esc backs out of anything except a reel in flight — bailing mid-spin
        // would eat the fish and hand back nothing.
        // Esc OR pad B backs out of anything except a reel in flight (bailing
        // mid-spin would eat the fish and hand back nothing). The frame stamp is
        // what stops the same Esc ALSO popping the pause menu on top — the same
        // guard SolarMap and the vendor panels use.
        bool back = Input.GetKeyDown(KeyCode.Escape) || TutorialGate.PadPressed(TutorialGate.PadButton.B);
        if (back && _reel == null)
        {
            s_escFrame = Time.frameCount;
            CloseInternal();
            return;
        }

        // DEAD-MAN'S SWITCH - the important safety net in this file.
        //
        // If NOTHING is on screen while this panel considers itself open then
        // nobody is ever going to close it, and a modal that cannot close locks
        // the player out of their own controls behind letterbox bars. The
        // previous version only fired once the flow coroutine had ENDED, which
        // missed the case that actually trapped Sam: a coroutine still running,
        // stuck waiting on something that never arrives.
        //
        // Checking what is VISIBLE rather than what is RUNNING covers every
        // cause, including ones I have not thought of.
        bool somethingVisible =
               _case.gameObject.activeSelf
            || _result.gameObject.activeSelf
            || PostGreetingChoicePanelVisible
            || (_shared != null && _shared.gameObject.activeSelf);

        if (somethingVisible) _lastVisibleAt = Time.unscaledTime;
        else if (Time.unscaledTime - _lastVisibleAt > 1.5f) CloseInternal();
    }

    // ── the conversation, through the SHARED dialogue systems ────────────

    bool BorrowLabel()
    {
        if (_shared != null) return true;
        var host = FindObjectOfType<NPCDialogue>(true);
        if (host == null || host.dialogueText == null) return false;
        _shared = host.dialogueText;
        return true;
    }

    IEnumerator Say(string line)
    {
        if (!BorrowLabel()) yield break;
        _shared.gameObject.SetActive(true);
        // The exact reveal every other NPC uses — zero-alloc via
        // maxVisibleCharacters, and the phosphor skin re-asserts over the top.
        _skipTyping = false;
        yield return DialogueTextStyling.RevealCharsTMP(_shared, line, TypeDelay, () => _skipTyping);

        // THEN WAIT FOR THE PLAYER. PostGreetingChoicePanel.Show() deliberately
        // hides the spoken line when it opens, so going straight from typing to
        // choices gave Sam about a third of a second to read it. Every other NPC
        // waits for a click here (AuthoredNPCTalk.Speak does exactly this); the
        // timeout is belt and braces so a missed input can never strand anyone.
        float until = Time.unscaledTime + ReadTimeout;
        while (Time.unscaledTime < until && _open)
        {
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)
                || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.F)
                || Input.GetKeyDown(KeyCode.Return)
                || TutorialGate.PadPressed(TutorialGate.PadButton.A)) break;
            yield return null;
        }
    }

    bool _skipTyping;
    const float ReadTimeout = 8f;

    IEnumerator Choose(params string[] labels)
    {
        _choice = -1;
        var panel = PostGreetingChoicePanel.Instance;
        if (panel == null) { _choice = PostGreetingChoicePanel.Cancelled; yield break; }

        var rows = new List<PostGreetingChoicePanel.Row>(labels.Length);
        for (int i = 0; i < labels.Length; i++)
            rows.Add(new PostGreetingChoicePanel.Row(labels[i], true));

        panel.Show(rows, i => _choice = i);
        // ⚠️ `!= -1`, NOT `< 0`. The panel reports a pad-B / right-click back-out
        // as Cancelled (-2), and `< 0` treated that as "still waiting" — so a
        // cancel span forever, the panel never closed, isInModalSlotUI stayed
        // true and the player could not move. That was the freeze.
        // The timeout is the belt to that braces: if Show ever fails to take
        // (already visible, panel mid-teardown) the callback never fires and
        // this would wait forever with the player frozen behind the bars.
        float until = Time.unscaledTime + 120f;
        while (_choice == -1 && _open && Time.unscaledTime < until) yield return null;
        if (_choice == -1) _choice = PostGreetingChoicePanel.Cancelled;
        HideChoices();
    }

    void HideChoices()
    {
        var panel = PostGreetingChoicePanel.Instance;
        if (panel != null && panel.IsVisible) panel.Hide();
    }

    IEnumerator Flow()
    {
        yield return Say("Meow! Spare a fish?");
        yield return Choose("Yes.", "No.");

        // Any negative is a back-out (walked away, pad B, right-click).
        if (_choice != 0)
        {
            yield return Say("...");
            yield return new WaitForSecondsRealtime(0.6f);
            CloseInternal();
            yield break;
        }

        yield return Say("Purrrr. Which one?");

        // ⚠️ The fish list IS a choice list now. It used to be a bespoke grid
        // floating in the middle of the screen — which left-aligned into empty
        // space when you only had one or two fish, sat nowhere near the
        // conversation, and could not be driven with a controller at all.
        // Routing it through the shared panel fixes the position, the layout and
        // the pad navigation in one move, and it looks like every other choice
        // in the game because it IS one.
        _fishBuf.Clear();
        if (Hotbar.Instance != null) Hotbar.Instance.CollectFish(_fishBuf);

        if (_fishBuf.Count == 0)
        {
            yield return Say("Your pockets are empty.");
            CloseInternal();
            yield break;
        }

        var mgr = CatPerkManager.Instance;
        if (mgr != null && mgr.HasPerk)
        {
            // No confirm box — state what is on the table, then let them do it.
            yield return Say($"{CatPerkManager.ShortName(mgr.ActiveKind)} " +
                             $"({CatPerkManager.TierLabel(mgr.ActiveTier)}) is running, " +
                             $"{Mmss(mgr.SecondsLeft)} left. Feeding me replaces it.");
        }

        var labels = new string[_fishBuf.Count + 1];
        for (int i = 0; i < _fishBuf.Count; i++)
        {
            var f = _fishBuf[i];
            string hex = ColorUtility.ToHtmlStringRGB(TierColorFor(f));
            labels[i] = $"<color=#{hex}>■</color> {f.DisplayName}  " +
                        $"<color=#4A6E59>{f.weightLbs} lb</color>";
        }
        labels[_fishBuf.Count] = "Never mind.";

        yield return Choose(labels);
        _flow = null;

        if (_choice < 0 || _choice >= _fishBuf.Count) { CloseInternal(); yield break; }
        GiveFish(_fishBuf[_choice]);
    }

    // ── stage 2: pick a fish (bespoke, like the vendor menus) ────────────

    IEnumerator CloseAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        CloseInternal();
    }

    static Color TierColorFor(FishEntry e)
    {
        int idx = e.ResolveSpecies();
        var tier = FishingRules.Species[idx].tier;
        switch (tier)
        {
            case FishTier.Rare:     return new Color32(0xFF, 0xD2, 0x32, 0xFF);
            case FishTier.Uncommon: return new Color32(0x3C, 0xDC, 0xBE, 0xFF);
            default:                return new Color32(0x8C, 0xAF, 0xFF, 0xFF);
        }
    }

    void GiveFish(FishEntry entry)
    {
        // Take the fish from where it ACTUALLY lives. RemoveFishEntry is the same
        // call the fish market's bounty turn-in uses, and it covers fish bags too.
        bool took = Hotbar.Instance != null && Hotbar.Instance.RemoveFishEntry(entry);
        if (!took) { CloseInternal(); return; }
        // Keep the parallel list in step so anything still reading it agrees.
        if (FishInventory.Instance != null) FishInventory.Instance.RemoveSpecificFish(entry);

        if (_shared != null) _shared.gameObject.SetActive(false);

        CatPerkManager.Roll(Random.value, Random.value, out var kind, out int tier);
        StartReel(kind, tier);
    }

    // ── stage 3: the reel (option B — vertical tag drop) ─────────────────

    void StartReel(CatPerkKind winKind, int winTier)
    {
        _result.gameObject.SetActive(false);
        _case.gameObject.SetActive(true);

        for (int i = _strip.childCount - 1; i >= 0; i--) Destroy(_strip.GetChild(i).gameObject);
        _cards.Clear();

        for (int i = 0; i < CardCount; i++)
        {
            CatPerkKind k; int t;
            if (i == WinnerIndex) { k = winKind; t = winTier; }
            else CatPerkManager.Roll(Random.value, Random.value, out k, out t);
            _cards.Add(BuildCard(k, t, i));
        }

        _reel = StartCoroutine(SpinRoutine(winKind, winTier));
    }

    RectTransform BuildCard(CatPerkKind kind, int tier, int index)
    {
        var go = new GameObject("Card", typeof(RectTransform));
        go.transform.SetParent(_strip, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -index * Pitch);
        rt.sizeDelta = new Vector2(0f, CardHeight);

        var img = go.AddComponent<Image>();
        img.color = new Color32(0x07, 0x1B, 0x13, 0xFF);
        img.raycastTarget = false;

        Color c = CatPerkManager.TierColor(tier);
        // Bottom edge in the tier colour — the reel is read at a glance while it
        // is moving, and a coloured bar survives motion better than text does.
        var bar = new GameObject("Bar", typeof(RectTransform));
        bar.transform.SetParent(rt, false);
        var brt = (RectTransform)bar.transform;
        brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(1f, 0f);
        brt.offsetMin = Vector2.zero; brt.offsetMax = new Vector2(0f, 3f);
        var bimg = bar.AddComponent<Image>();
        bimg.color = c; bimg.raycastTarget = false;

        string hex = ColorUtility.ToHtmlStringRGB(c);
        var label = PhosphorUI.MakeLabel(rt, "Text",
            $"<color=#{hex}>{CatPerkManager.ShortName(kind)}</color>\n" +
            $"<size=26><b><color=#{hex}>{CatPerkManager.TierLabel(tier)}</color></b></size>",
            15f, PhosphorUI.RowText);
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(16f, 4f); lrt.offsetMax = new Vector2(-12f, -4f);
        label.alignment = TextAlignmentOptions.Left;

        return rt;
    }

    IEnumerator SpinRoutine(CatPerkKind winKind, int winTier)
    {
        float mid = ViewHeight * 0.5f;
        float from = mid - (3f * Pitch + CardHeight * 0.5f);
        // A little off-centre so it never lands dead on the line — the near-miss
        // read is most of what makes a case feel like a case.
        float jitter = Random.Range(-1f, 1f) * (CardHeight * 0.20f);
        float to = mid - (WinnerIndex * Pitch + CardHeight * 0.5f) + jitter;

        int last = int.MinValue;
        float t0 = Time.unscaledTime;
        while (true)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - t0) / ReelSeconds);
            float e = 1f - Mathf.Pow(1f - t, 3.7f);
            float y = Mathf.Lerp(from, to, e);
            _strip.anchoredPosition = new Vector2(0f, -y);

            int idx = Mathf.FloorToInt((mid - y) / Pitch);
            if (idx != last) { last = idx; if (t < 0.995f) Play(tickClip, 0.35f); }
            if (t >= 1f) break;
            yield return null;
        }

        for (int i = 0; i < _cards.Count; i++)
        {
            if (i == WinnerIndex || _cards[i] == null) continue;
            var g = _cards[i].GetComponent<Image>();
            if (g != null) g.color = new Color32(0x07, 0x1B, 0x13, 0x66);
            var txt = _cards[i].GetComponentInChildren<TextMeshProUGUI>();
            if (txt != null) txt.alpha = 0.4f;
        }
        Play(winClip);
        yield return new WaitForSecondsRealtime(0.8f);

        _reel = null;
        ShowResult(winKind, winTier);
    }

    // ── stage 4: the result ──────────────────────────────────────────────

    void ShowResult(CatPerkKind kind, int tier)
    {
        _case.gameObject.SetActive(false);
        _result.gameObject.SetActive(true);
        // Only clear what a previous result built — the plate's own background,
        // border slivers and scanlines are children too and must survive.
        for (int i = _result.childCount - 1; i >= 0; i--)
        {
            var child = _result.GetChild(i);
            if (child.name == "Body" || child.name == "Close") Destroy(child.gameObject);
        }

        var mgr = CatPerkManager.Instance;
        CatPerkKind prevKind = CatPerkKind.None;
        int prevTier = 0; float prevLeft = 0f;
        if (mgr != null) mgr.Grant(kind, tier, out prevKind, out prevTier, out prevLeft);

        Color c = CatPerkManager.TierColor(tier);
        string hex = ColorUtility.ToHtmlStringRGB(c);
        float chance = CatPerkManager.ChanceOf(tier);

        string swap = "";
        if (prevKind != CatPerkKind.None && prevLeft > 0f)
        {
            bool better = tier > prevTier;
            bool same = tier == prevTier;
            string verb = better ? "TRADED UP FROM" : same ? "SWAPPED" : "THREW AWAY";
            string col = better ? "39E08B" : same ? "4A6E59" : "C8555A";
            swap = $"\n\n<size=17><color=#{col}>{verb}  {CatPerkManager.ShortName(prevKind)} · " +
                   $"{CatPerkManager.TierLabel(prevTier)}  ({Mmss(prevLeft)} was left)</color></size>";
        }

        string body =
            $"<size=16><color=#4A6E59>THE CAT COUGHS SOMETHING UP</color></size>\n\n" +
            $"<size=40><b><color=#{hex}>{CatPerkManager.DisplayName(kind)} · {CatPerkManager.TierLabel(tier)}</color></b></size>\n\n" +
            $"<size=21><color=#7DDBA9>{CatPerkManager.Blurb(kind)}</color></size>" +
            swap +
            $"\n\n<size=16><color=#4A6E59>{chance * 100f:0.0}% CHANCE  ·  1 IN {Mathf.RoundToInt(1f / chance)}</color></size>";

        var label = PhosphorUI.MakeLabel(_result, "Body", body, 20f, PhosphorUI.Body);
        var lrt = label.rectTransform;
        lrt.anchorMin = new Vector2(0.5f, 0.5f); lrt.anchorMax = new Vector2(0.5f, 0.5f);
        lrt.pivot = new Vector2(0.5f, 0.5f);
        lrt.anchoredPosition = new Vector2(0f, 36f);
        lrt.sizeDelta = new Vector2(880f, 360f);
        label.alignment = TextAlignmentOptions.Center;

        var go = new GameObject("Close", typeof(RectTransform));
        go.transform.SetParent(_result, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, -188f);
        rt.sizeDelta = new Vector2(220f, 44f);
        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        PhosphorUI.AddBorder(rt);
        var btnLabel = PhosphorUI.MakeLabel(rt, "Label", "CLOSE", 19f, PhosphorUI.Phosphor);
        var brt = btnLabel.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
        btnLabel.alignment = TextAlignmentOptions.Center;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(CloseInternal);
        go.AddComponent<CatPerkRowHover>().Bind(img, btnLabel);
    }

    static string Mmss(float seconds)
    {
        int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return (s / 60) + ":" + (s % 60).ToString("00");
    }

    void Play(AudioClip clip, float scale = 1f)
    {
        if (clip == null || _audio == null) return;
        _audio.PlayOneShot(clip, volume * scale);
    }

    // ── building (our half only) ─────────────────────────────────────────

    void BuildUI()
    {
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f;

        var canvasGO = new GameObject("Canvas", typeof(RectTransform));
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Below PostGreetingChoicePanel (905) so the shared rows stay on top.
        _canvas.sortingOrder = UILayer.Toast;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();
        var root = (RectTransform)canvasGO.transform;

        // Case (vertical reel)
        _case = MakeChild(root, "Case");
        _case.anchorMin = new Vector2(0.5f, 0.5f); _case.anchorMax = new Vector2(0.5f, 0.5f);
        _case.pivot = new Vector2(0.5f, 0.5f);
        _case.anchoredPosition = new Vector2(0f, 40f);
        _case.sizeDelta = new Vector2(ViewWidth, ViewHeight);

        var caseBg = _case.gameObject.AddComponent<Image>();
        caseBg.color = new Color32(0x04, 0x10, 0x0B, 0xFF);
        _case.gameObject.AddComponent<RectMask2D>();
        PhosphorUI.AddBorder(_case);

        _strip = MakeChild(_case, "Strip");
        _strip.anchorMin = new Vector2(0f, 1f); _strip.anchorMax = new Vector2(1f, 1f);
        _strip.pivot = new Vector2(0.5f, 1f);
        _strip.offsetMin = new Vector2(12f, 0f); _strip.offsetMax = new Vector2(-12f, 0f);
        _strip.anchoredPosition = Vector2.zero;

        // Top/bottom fades, like the mockup — they are what makes the strip read
        // as a window onto something longer rather than a list that stops.
        MakeFade(_case, true);
        MakeFade(_case, false);

        MakeWindowEdge(_case, +CardHeight * 0.5f + 3f);
        MakeWindowEdge(_case, -CardHeight * 0.5f - 3f);

        // The result card needs a PLATE behind it, in the dialogue box's own
        // colours. Sam: "its hard to read tex" — light phosphor text straight
        // over a sunlit planet is unreadable, and every other panel in the game
        // sits on this same green-black ground.
        _result = MakeChild(root, "Result");
        _result.anchorMin = new Vector2(0.5f, 0.5f); _result.anchorMax = new Vector2(0.5f, 0.5f);
        _result.pivot = new Vector2(0.5f, 0.5f);
        _result.anchoredPosition = new Vector2(0f, 30f);
        _result.sizeDelta = new Vector2(940f, 470f);
        var resultBg = _result.gameObject.AddComponent<Image>();
        resultBg.color = PhosphorUI.Plate;
        PhosphorUI.AddBorder(_result);
        PhosphorUI.AddScanlines(_result);

        _canvas.gameObject.SetActive(false);
    }

    /// A vertical alpha ramp, generated once and shared. A RawImage over a 1x64
    /// texture is cheaper and smoother than stacking translucent quads.
    void MakeFade(RectTransform parent, bool top)
    {
        if (_fadeTex == null)
        {
            _fadeTex = new Texture2D(1, 64, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[64];
            for (int i = 0; i < 64; i++)
            {
                float a = 1f - (i / 63f);            // opaque at index 0
                px[i] = new Color32(0x04, 0x10, 0x0B, (byte)(255f * a * a));
            }
            _fadeTex.SetPixels32(px);
            _fadeTex.Apply();
        }

        var go = new GameObject(top ? "FadeTop" : "FadeBottom", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
        rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
        rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, 88f);
        var raw = go.AddComponent<RawImage>();
        raw.texture = _fadeTex;
        raw.raycastTarget = false;
        // Flip the ramp for the bottom edge.
        raw.uvRect = top ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
    }

    void MakeWindowEdge(RectTransform parent, float y)
    {
        var go = new GameObject("WindowEdge", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 0.5f); rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(0f, 2f);
        var img = go.AddComponent<Image>();
        img.color = PhosphorUI.Phosphor;
        img.raycastTarget = false;
    }

    static RectTransform MakeChild(RectTransform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
}

/// Row hover for the bespoke rows — the PHOSPHOR look is text-first, so a row
/// lights up rather than growing a button face.
public class CatPerkRowHover : MonoBehaviour,
    UnityEngine.EventSystems.IPointerEnterHandler,
    UnityEngine.EventSystems.IPointerExitHandler
{
    Image _bg;
    TextMeshProUGUI _label;
    Color _restBg;

    public void Bind(Image bg, TextMeshProUGUI label)
    {
        _bg = bg; _label = label;
        if (bg != null) _restBg = bg.color;
    }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e)
    {
        if (_bg != null) _bg.color = PhosphorUI.RowHoverBg;
        if (_label != null) _label.color = PhosphorUI.RowHot;
    }

    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e)
    {
        if (_bg != null) _bg.color = _restBg;
        if (_label != null) _label.color = PhosphorUI.RowText;
    }
}
