using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// The big moment. Two ceremony cards, both centre-screen, both queued through
/// one pump so they can never overlap:
///
///   • GENERAL — the phone's general level went up. Screen dims, time drops to
///     0.3× for a beat, the old numeral flips to the new one behind an
///     expanding ring, and the new rank is named. This is the only place in the
///     game that stops to congratulate you, so it's allowed to be theatrical.
///
///   • BLUEPRINTS — a Colonizer level unlocked new things to build. Lists them.
///     No slow-mo: it fires on nearly every early placement and grinding the
///     game to a crawl six times in the first ten minutes would wear through
///     fast.
///
/// Per-TRACK level-ups keep using ProgressToastUI's small under-compass toast.
/// This is deliberately a different weight of feedback: toast for "you did a
/// thing", ceremony for "you became something".
///
/// Everything animates on UNSCALED time — it has to keep moving during its own
/// slow-mo, and during a kill-cam's.
///
/// Auto-singleton with MainMenu skip — ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1).
/// </summary>
public class LevelUpCeremonyUI : MonoBehaviour
{
    public static LevelUpCeremonyUI Instance { get; private set; }

    // ── Timing (seconds, unscaled) ───────────────────────────────────────────
    const float GeneralInTime    = 0.30f;
    const float GeneralFlipAt    = 0.60f;   // when the numeral turns over
    const float GeneralTotal     = 3.10f;   // before the fade starts
    const float GeneralOutTime   = 0.45f;
    const float SlowMoScale      = 0.30f;
    const float SlowMoDuration   = 1.60f;

    const float UnlockInTime     = 0.28f;
    const float UnlockStagger    = 0.07f;   // per listed blueprint
    const float UnlockTotal      = 2.90f;
    const float UnlockOutTime    = 0.40f;

    const float QueueGap         = 0.20f;

    const float DimAlpha         = 0.55f;   // general ceremony's screen dim
    const float UnlockDimAlpha   = 0.22f;

    // Most blueprint tiers are 3–6 entries; beyond that the card would grow
    // taller than it is wide and stop reading as a card.
    const int MaxListedUnlocks   = 8;

    // ── Palette (same family as ProgressToastUI / VitalsHUD) ─────────────────
    static readonly Color LabelText = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    static readonly Color DimText   = new Color32(0x8F, 0xB4, 0xC6, 0xFF);
    static Color Accent => HelmetHudPalette.Accent;

    /// Rank names, indexed by general level 0..10. The general level is a mean
    /// of five percentages, which is an honest number and a completely
    /// unmemorable one — the rank is what the player will actually quote.
    static readonly string[] RankNames =
    {
        "CASTAWAY", "SURVIVOR", "SETTLER", "HOMESTEADER", "PIONEER", "COLONIST",
        "TRAILBLAZER", "FRONTIERSMAN", "WARDEN", "LUMINARY", "LEGEND",
    };

    public static string RankFor(int generalLevel)
        => RankNames[Mathf.Clamp(generalLevel, 0, RankNames.Length - 1)];

    enum Kind { General, Blueprints }

    struct Entry
    {
        public Kind kind;
        public int fromLevel, toLevel;
        public List<string> names;
    }

    readonly Queue<Entry> _queue = new Queue<Entry>();
    bool _pumping;
    Canvas _canvas;
    RectTransform _screen;
    AudioSource _audio;
    bool _slowMoApplied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("LevelUpCeremonyUI");
        DontDestroyOnLoad(go);
        go.AddComponent<LevelUpCeremonyUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildCanvas();

        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f;          // 2D — it's a UI sound, not a world one
        _audio.volume = 0.55f;
    }

    void OnEnable()
    {
        PlayerProgress.OnGeneralLevelUp += HandleGeneralLevelUp;
        PlayerProgress.OnTrackLevelUp   += HandleTrackLevelUp;
    }

    void OnDisable()
    {
        PlayerProgress.OnGeneralLevelUp -= HandleGeneralLevelUp;
        PlayerProgress.OnTrackLevelUp   -= HandleTrackLevelUp;
        // Never leave the world in slow motion because a scene unloaded mid-card.
        if (_slowMoApplied) { SlowMoTime.Restore(); _slowMoApplied = false; }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void BuildCanvas()
    {
        var canvasGo = new GameObject("LevelUpCeremonyCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 70;         // above the progress toast (60)
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        // No GraphicRaycaster — pure output, must never eat a click.
        HUDSceneGate.Register(_canvas.rootCanvas);
        HudVisibility.RegisterHideable(_canvas.rootCanvas);

        _screen = new GameObject("Screen", typeof(RectTransform)).GetComponent<RectTransform>();
        _screen.SetParent(canvasGo.transform, false);
        Stretch(_screen);
    }

    // ── Intake ───────────────────────────────────────────────────────────────

    void HandleGeneralLevelUp(int from, int to)
        => Enqueue(new Entry { kind = Kind.General, fromLevel = from, toLevel = to });

    void HandleTrackLevelUp(ProgressTrack track, int from, int to)
    {
        if (track != ProgressTrack.Colonizer) return;
        var unlocked = BuildableUnlocks.UnlockedBetween(from, to);
        if (unlocked.Count == 0) return;    // a Colonizer level that grants nothing stays a toast
        Enqueue(new Entry { kind = Kind.Blueprints, fromLevel = from, toLevel = to, names = unlocked });
    }

    void Enqueue(Entry e)
    {
        _queue.Enqueue(e);
        if (!_pumping) StartCoroutine(Pump());
    }

    IEnumerator Pump()
    {
        _pumping = true;
        while (_queue.Count > 0)
        {
            var e = _queue.Dequeue();
            yield return e.kind == Kind.General ? PlayGeneral(e) : PlayBlueprints(e);
            if (_queue.Count > 0) yield return new WaitForSecondsRealtime(QueueGap);
        }
        _pumping = false;
    }

    // ── The general-level ceremony ───────────────────────────────────────────

    IEnumerator PlayGeneral(Entry e)
    {
        var dim  = NewImage(_screen, "Dim", new Color(0f, 0f, 0f, 0f));
        Stretch(dim.rectTransform);

        var card = NewCard("GeneralCard", 780f, 320f, out CanvasGroup group);

        var eyebrow = NewText(card, "Eyebrow", 16f, Accent, TextAlignmentOptions.Center);
        eyebrow.text = "SUIT ASSESSMENT";
        eyebrow.characterSpacing = 16f;
        Anchor(eyebrow.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
               new Vector2(24f, -46f), new Vector2(-24f, -18f));

        // Rules above and below the numeral. The HUD readouts have no boxes
        // (VitalsHUD.ApplyIntegratedStyle strips them), so a framed panel would
        // read as foreign — two accent rules give the card presence without one.
        var ruleTop = NewImage(card, "RuleTop", new Color(Accent.r, Accent.g, Accent.b, 0.55f));
        Anchor(ruleTop.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
               new Vector2(-150f, -56f), new Vector2(150f, -55f));

        // Ring burst — sits behind the numeral, punches outward on the flip.
        var ring = NewImage(card, "Ring", new Color(Accent.r, Accent.g, Accent.b, 0f));
        ring.sprite = RingSprite();
        var ringRt = ring.rectTransform;
        ringRt.anchorMin = ringRt.anchorMax = new Vector2(0.5f, 0.5f);
        ringRt.pivot = new Vector2(0.5f, 0.5f);
        ringRt.anchoredPosition = new Vector2(0f, 14f);
        ringRt.sizeDelta = new Vector2(200f, 200f);

        var numeral = NewText(card, "Numeral", 128f, LabelText, TextAlignmentOptions.Center);
        numeral.text = e.fromLevel.ToString();
        numeral.fontStyle = FontStyles.Bold;
        var numRt = numeral.rectTransform;
        numRt.anchorMin = numRt.anchorMax = new Vector2(0.5f, 0.5f);
        numRt.pivot = new Vector2(0.5f, 0.5f);
        numRt.anchoredPosition = new Vector2(0f, 18f);
        numRt.sizeDelta = new Vector2(400f, 160f);

        var title = NewText(card, "Title", 20f, Accent, TextAlignmentOptions.Center);
        title.text = "GENERAL LEVEL";
        title.characterSpacing = 12f;
        Anchor(title.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
               new Vector2(24f, 62f), new Vector2(-24f, 90f));

        var ruleBottom = NewImage(card, "RuleBottom", new Color(Accent.r, Accent.g, Accent.b, 0.55f));
        Anchor(ruleBottom.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
               new Vector2(-150f, 54f), new Vector2(150f, 55f));

        var rank = NewText(card, "Rank", 24f, LabelText, TextAlignmentOptions.Center);
        rank.text = "RANK · " + RankFor(e.fromLevel);
        rank.characterSpacing = 6f;
        Anchor(rank.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
               new Vector2(24f, 20f), new Vector2(-24f, 50f));

        PlayChime(e.toLevel);
        ApplySlowMo();

        float t = 0f;
        bool flipped = false;
        var cardRt = (RectTransform)card;
        while (t < GeneralTotal)
        {
            t += Time.unscaledDeltaTime;

            // Entrance: dim washes in, card eases up to full size.
            float inU = Mathf.Clamp01(t / GeneralInTime);
            inU = 1f - (1f - inU) * (1f - inU);
            dim.color = new Color(0f, 0f, 0f, DimAlpha * inU);
            group.alpha = inU;
            cardRt.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, inU);

            if (!flipped && t >= GeneralFlipAt)
            {
                flipped = true;
                numeral.text = e.toLevel.ToString();
                rank.text = "RANK · " + RankFor(e.toLevel);
                PlayChime(e.toLevel, second: true);
            }

            // Flip punch: the numeral overshoots and settles, the ring blows out.
            float since = t - GeneralFlipAt;
            if (since >= 0f)
            {
                float p = Mathf.Clamp01(since / 0.55f);
                float ease = 1f - (1f - p) * (1f - p) * (1f - p);
                numRt.localScale = Vector3.one * Mathf.Lerp(1.55f, 1f, ease);
                ringRt.sizeDelta = Vector2.one * Mathf.Lerp(150f, 460f, ease);
                ring.color = new Color(Accent.r, Accent.g, Accent.b, 0.75f * (1f - p));
                numeral.color = Color.Lerp(Color.white, LabelText, p);
            }

            if (_slowMoApplied && t >= SlowMoDuration) ReleaseSlowMo();
            yield return null;
        }

        ReleaseSlowMo();

        // Exit: the card lifts as it goes, so it reads as leaving rather than
        // simply being switched off.
        float o = 0f;
        Vector2 restPos = cardRt.anchoredPosition;
        while (o < GeneralOutTime)
        {
            o += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(o / GeneralOutTime);
            group.alpha = 1f - u;
            dim.color = new Color(0f, 0f, 0f, DimAlpha * (1f - u));
            cardRt.anchoredPosition = restPos + new Vector2(0f, 26f * u);
            yield return null;
        }

        Destroy(card.gameObject);
        Destroy(dim.gameObject);
    }

    // ── The blueprint-unlock card ────────────────────────────────────────────

    IEnumerator PlayBlueprints(Entry e)
    {
        int shown = Mathf.Min(e.names.Count, MaxListedUnlocks);
        int extra = e.names.Count - shown;
        float height = 116f + shown * 30f + (extra > 0 ? 26f : 0f);

        var dim = NewImage(_screen, "Dim", new Color(0f, 0f, 0f, 0f));
        Stretch(dim.rectTransform);

        var card = NewCard("BlueprintCard", 560f, height, out CanvasGroup group);
        var cardRt = (RectTransform)card;

        var eyebrow = NewText(card, "Eyebrow", 15f, Accent, TextAlignmentOptions.Center);
        eyebrow.text = $"COLONIZER · LEVEL {e.toLevel}";
        eyebrow.characterSpacing = 12f;
        Anchor(eyebrow.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
               new Vector2(20f, -40f), new Vector2(-20f, -16f));

        var title = NewText(card, "Title", 26f, LabelText, TextAlignmentOptions.Center);
        title.text = shown == 1 ? "NEW BLUEPRINT" : "NEW BLUEPRINTS";
        title.characterSpacing = 8f;
        title.fontStyle = FontStyles.Bold;
        Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
               new Vector2(20f, -78f), new Vector2(-20f, -44f));

        var rule = NewImage(card, "Rule", new Color(Accent.r, Accent.g, Accent.b, 0.5f));
        Anchor(rule.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
               new Vector2(-110f, -86f), new Vector2(110f, -85f));

        // One row per unlock, revealed on a stagger so the list reads as
        // arriving rather than as a block of text appearing.
        var rows = new List<TextMeshProUGUI>(shown);
        for (int i = 0; i < shown; i++)
        {
            var row = NewText(card, "Row" + i, 19f, LabelText, TextAlignmentOptions.Center);
            row.text = e.names[i].Trim().ToUpper();
            row.characterSpacing = 4f;
            row.alpha = 0f;
            float top = -96f - i * 30f;
            Anchor(row.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(20f, top - 26f), new Vector2(-20f, top));
            rows.Add(row);
        }
        if (extra > 0)
        {
            var more = NewText(card, "More", 15f, DimText, TextAlignmentOptions.Center);
            more.text = $"+{extra} MORE";
            float top = -96f - shown * 30f;
            Anchor(more.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(20f, top - 22f), new Vector2(-20f, top));
        }

        PlayChime(e.toLevel, second: true);

        float t = 0f;
        while (t < UnlockTotal)
        {
            t += Time.unscaledDeltaTime;
            float inU = Mathf.Clamp01(t / UnlockInTime);
            inU = 1f - (1f - inU) * (1f - inU);
            group.alpha = inU;
            dim.color = new Color(0f, 0f, 0f, UnlockDimAlpha * inU);
            cardRt.localScale = Vector3.one * Mathf.Lerp(0.95f, 1f, inU);

            for (int i = 0; i < rows.Count; i++)
            {
                float start = UnlockInTime + i * UnlockStagger;
                rows[i].alpha = Mathf.Clamp01((t - start) / 0.18f);
            }
            yield return null;
        }

        float o = 0f;
        Vector2 restPos = cardRt.anchoredPosition;
        while (o < UnlockOutTime)
        {
            o += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(o / UnlockOutTime);
            group.alpha = 1f - u;
            dim.color = new Color(0f, 0f, 0f, UnlockDimAlpha * (1f - u));
            cardRt.anchoredPosition = restPos + new Vector2(0f, 22f * u);
            yield return null;
        }

        Destroy(card.gameObject);
        Destroy(dim.gameObject);
    }

    // ── Slow-mo ──────────────────────────────────────────────────────────────
    // Only ever taken when time is running normally: a kill-cam slow-mo or an
    // open pause menu already owns the timescale, and stomping it would end
    // with the ceremony restoring 1.0 underneath them.

    void ApplySlowMo()
    {
        if (_slowMoApplied) return;
        if (!Mathf.Approximately(Time.timeScale, 1f)) return;
        SlowMoTime.Apply(SlowMoScale);
        _slowMoApplied = true;
    }

    void ReleaseSlowMo()
    {
        if (!_slowMoApplied) return;
        SlowMoTime.Restore();
        _slowMoApplied = false;
    }

    // ── Procedural chime ─────────────────────────────────────────────────────
    // Synthesised rather than an imported clip: it's two notes, it needs to
    // pitch up with the level, and an asset would mean a GUID reference to keep
    // alive for something a dozen lines of maths does better.

    static AudioClip _chimeLow, _chimeHigh;

    void PlayChime(int level, bool second = false)
    {
        if (_audio == null) return;
        // Explicit == null, not ?? — UnityEngine.Object overrides equality, and
        // the null-coalescing operator bypasses that overload.
        AudioClip clip;
        if (second)
        {
            if (_chimeHigh == null) _chimeHigh = BuildChime(784f);      // G5
            clip = _chimeHigh;
        }
        else
        {
            if (_chimeLow == null) _chimeLow = BuildChime(523.25f);     // C5
            clip = _chimeLow;
        }
        // Higher levels ring slightly brighter — a free sense of escalation.
        _audio.pitch = 1f + Mathf.Clamp(level, 0, 10) * 0.02f;
        _audio.PlayOneShot(clip, second ? 0.7f : 0.45f);
    }

    static AudioClip BuildChime(float freq)
    {
        const int rate = 44100;
        const float dur = 0.9f;
        int samples = (int)(rate * dur);
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            float env = Mathf.Exp(-4.5f * t);                 // struck-bell decay
            float body = Mathf.Sin(2f * Mathf.PI * freq * t)
                       + 0.45f * Mathf.Sin(2f * Mathf.PI * freq * 2f * t)
                       + 0.20f * Mathf.Sin(2f * Mathf.PI * freq * 3f * t);
            // Short attack ramp so it doesn't click on the first sample.
            float attack = Mathf.Clamp01(t / 0.004f);
            data[i] = body * env * attack * 0.32f;
        }
        var clip = AudioClip.Create("LevelUpChime" + (int)freq, samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ── Ring sprite ──────────────────────────────────────────────────────────

    static Sprite _ringSprite;

    static Sprite RingSprite()
    {
        if (_ringSprite != null) return _ringSprite;
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "CeremonyRing", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
        };
        var px = new Color[size * size];
        const float c = size * 0.5f, outer = 60f, inner = 52f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                // Soft on both edges so the ring stays smooth when scaled to 4×.
                float a = Mathf.Clamp01((outer - d) / 3f) * Mathf.Clamp01((d - inner) / 3f);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        _ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        _ringSprite.name = "CeremonyRing";
        return _ringSprite;
    }

    // ── Builders ─────────────────────────────────────────────────────────────

    /// A centred card carrying the shared HUD treatment: 10% accent wash plus
    /// the repeating scanlines, and nothing else. Same recipe as the progress
    /// toast, so the two read as the same instrument.
    Transform NewCard(string name, float width, float height, out CanvasGroup group)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(_screen, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, height);
        rt.anchoredPosition = new Vector2(0f, 40f);   // slightly above centre — clear of the hotbar

        group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        var wash = NewImage(rt, "Wash", HelmetHudPalette.AccentFaint);
        Stretch(wash.rectTransform);

        var scanGo = new GameObject("Scanlines", typeof(RectTransform));
        scanGo.transform.SetParent(rt, false);
        Stretch((RectTransform)scanGo.transform);
        var scan = scanGo.AddComponent<RawImage>();
        scan.texture = ScanlineTexture();
        scan.color = new Color(Accent.r, Accent.g, Accent.b, 0.07f);
        scan.uvRect = new Rect(0f, 0f, 1f, height / 4f);
        scan.raycastTarget = false;

        return rt;
    }

    static Texture2D _scanTex;

    static Texture2D ScanlineTexture()
    {
        if (_scanTex != null) return _scanTex;
        _scanTex = new Texture2D(1, 4, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat,   // required — uvRect.height > 1 tiles it
            filterMode = FilterMode.Point,
            name = "CeremonyScanlines",
        };
        _scanTex.SetPixels(new[]
        {
            new Color(1f, 1f, 1f, 1f),
            new Color(1f, 1f, 1f, 0f),
            new Color(1f, 1f, 1f, 0f),
            new Color(1f, 1f, 1f, 0f),
        });
        _scanTex.Apply();
        return _scanTex;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static void Anchor(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
    }

    static Image NewImage(Transform parent, string name, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    static TextMeshProUGUI NewText(Transform parent, string name, float size,
                                   Color c, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var txt = go.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(txt);
        txt.fontSize = size;
        txt.color = c;
        txt.alignment = align;
        txt.raycastTarget = false;
        txt.enableWordWrapping = false;
        txt.overflowMode = TextOverflowModes.Overflow;
        return txt;
    }
}
