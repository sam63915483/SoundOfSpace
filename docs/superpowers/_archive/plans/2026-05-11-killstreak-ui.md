# Killstreak UI + Slow-Mo Stacking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Top-center killstreak popup (DOUBLE KILL → TRIPLE KILL → … → WICKED SICK cap), driven by the existing `EnemyController.OnAnyEnemyDeath` static event, with each consecutive kill in the streak stacking the on-kill slow-mo duration by ×1.2.

**Architecture:** Two new singleton MonoBehaviours auto-created at runtime — `KillstreakManager` tracks the count + decay timer and fires events; `KillstreakHUD` renders the popup. `SlowmoOnKill` switches from `OnAnyEnemyDeath` to `KillstreakManager.OnKillRegistered` so it always reads the post-increment streak. Both new singletons are seeded by `MainMenuController.EnsureGameplaySingletons` and registered with `HUDSceneGate` so they hide in MainMenu.

**Tech Stack:** Unity 2022.3, uGUI + TMP, the existing static-event hooks (`EnemyController.OnAnyEnemyDeath`, `ResourceManager.OnDeath`, `SceneManager.sceneLoaded`).

**Spec:** `docs/superpowers/specs/2026-05-11-killstreak-ui-design.md`

**Verification model:** This is a Unity project with no automated test framework (per `CLAUDE.md` — "all iteration happens in the Editor"). Each task verifies via **compile-clean check + Editor play-mode procedure** specific to that task.

---

## Task 1 · `KillstreakManager` — tracking + events

**Files:**
- Create: `Assets/3 - Scripts/Combat/KillstreakManager.cs`

After this commit the manager exists, tracks the streak in `Debug.Log` form, and fires events — but nothing visual yet. The HUD lands in Task 3.

- [ ] **Step 1: Create the file**

Path: `Assets/3 - Scripts/Combat/KillstreakManager.cs`

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tracks the player's consecutive enemy-kill streak. Each kill increments
/// the count and resets a decay timer; when the timer expires without a new
/// kill, the streak resets to 0 and OnStreakBroken fires.
///
/// Tier windows shrink as the streak climbs — 10 s after the first kill,
/// 9 s at x2 (DOUBLE), 8 s at x3 (TRIPLE), … 1 s at x10, and a 1 s cap from
/// x11 onward (KillstreakHUD reuses the same WICKED SICK visual past the cap).
///
/// Public surface used by KillstreakHUD and SlowmoOnKill:
///   - CurrentStreak (0 idle, 1 after first kill, 2 at DOUBLE, …)
///   - DecayProgress01 (1.0 just-killed → 0.0 about-to-break)
///   - OnKillRegistered(int newStreak) — fires AFTER the increment
///   - OnStreakBroken()
///
/// Auto-creates like the other procedural singletons, skipped in MainMenu.
/// Resets on player death (ResourceManager.OnDeath) and scene reload.
/// </summary>
public class KillstreakManager : MonoBehaviour
{
    public static KillstreakManager Instance { get; private set; }

    public int CurrentStreak { get; private set; }
    public float DecayProgress01 =>
        _currentWindow > 0f ? Mathf.Clamp01(_decayTimer / _currentWindow) : 0f;

    public event System.Action<int> OnKillRegistered;
    public event System.Action OnStreakBroken;

    // Indexed by streak count (clamped to last entry past cap).
    //   idx 0: never read (we start at streak=1 after the first kill).
    //   idx 1: 10 s window from kill 1 → kill 2 (pre-popup).
    //   idx 2..10: 9, 8, 7, 6, 5, 4, 3, 2, 1 — windows at DOUBLE … LEGENDARY.
    //   idx 11+: 1 s — WICKED SICK cap.
    static readonly float[] s_windowByStreak =
    {
        /* 0  */ 0f,
        /* 1  */ 10f,
        /* 2  */ 9f,
        /* 3  */ 8f,
        /* 4  */ 7f,
        /* 5  */ 6f,
        /* 6  */ 5f,
        /* 7  */ 4f,
        /* 8  */ 3f,
        /* 9  */ 2f,
        /* 10 */ 1f,
        /* 11+ */ 1f,
    };

    float _decayTimer;
    float _currentWindow;
    ResourceManager _resources;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("KillstreakManager");
        DontDestroyOnLoad(go);
        go.AddComponent<KillstreakManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        EnemyController.OnAnyEnemyDeath += HandleKill;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        HookResourceManager();
    }

    void OnDisable()
    {
        EnemyController.OnAnyEnemyDeath -= HandleKill;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (_resources != null) _resources.OnDeath -= HandlePlayerDeath;
    }

    void Update()
    {
        // ResourceManager auto-creates after us in some load orders — hook lazily.
        if (_resources == null) HookResourceManager();

        if (CurrentStreak <= 0) return;
        _decayTimer -= Time.unscaledDeltaTime;
        if (_decayTimer <= 0f) BreakStreak();
    }

    void HandleKill()
    {
        CurrentStreak++;
        _currentWindow = WindowForStreak(CurrentStreak);
        _decayTimer = _currentWindow;
        OnKillRegistered?.Invoke(CurrentStreak);
    }

    void BreakStreak()
    {
        if (CurrentStreak <= 0) return;
        CurrentStreak = 0;
        _decayTimer = 0f;
        _currentWindow = 0f;
        OnStreakBroken?.Invoke();
    }

    void HandlePlayerDeath() => BreakStreak();

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        BreakStreak();
        HookResourceManager();
    }

    void HookResourceManager()
    {
        if (_resources != null) _resources.OnDeath -= HandlePlayerDeath;
        _resources = ResourceManager.Instance;
        if (_resources != null) _resources.OnDeath += HandlePlayerDeath;
    }

    static float WindowForStreak(int streak)
    {
        int idx = Mathf.Clamp(streak, 1, s_windowByStreak.Length - 1);
        return s_windowByStreak[idx];
    }
}
```

- [ ] **Step 2: Compile-check**

Save and let Unity recompile. Open `Window > General > Console`. Expected: zero errors.

If you see `'EnemyController' could not be found` or `'ResourceManager' could not be found`, both already exist (`Assets/3 - Scripts/Combat/EnemyController.cs`, `Assets/3 - Scripts/Survival/ResourceManager.cs`) — verify the new file is in the same `Assembly-CSharp` (no `.asmdef` differences).

- [ ] **Step 3: Play-mode smoke test (events only, no UI yet)**

Temporarily wire up Debug.Log on the events to confirm they fire. In the Console, run a test by attaching a temporary listener — or, simpler, add this debug block to `OnEnable` right before pressing Play (remove after this step):

```csharp
        OnKillRegistered += s => Debug.Log($"[Killstreak] kill registered — streak now ×{s}");
        OnStreakBroken   += () => Debug.Log("[Killstreak] streak broken");
```

Press Play, kill three toy enemies within 10 s of each other. Expected Console output:

```
[Killstreak] kill registered — streak now ×1
[Killstreak] kill registered — streak now ×2
[Killstreak] kill registered — streak now ×3
```

If you wait past the decay window without killing, you should see `[Killstreak] streak broken`.

Remove the temporary debug subscriptions before committing.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/Combat/KillstreakManager.cs"
git commit -m "feat(combat): KillstreakManager — tracks streak count + decay window, fires OnKillRegistered / OnStreakBroken events. Resets on player death + scene reload."
```

---

## Task 2 · `SlowmoOnKill` — duration stacking with end-time pattern

**Files:**
- Modify: `Assets/3 - Scripts/Camera/SlowmoOnKill.cs` (entire body — file is ~25 lines)

After this commit the slow-mo dip stretches as the streak climbs (×2 = 0.54 s, ×5 ≈ 0.93 s, ×10 ≈ 2.32 s). Still no visible popup — that's Task 3.

- [ ] **Step 1: Replace the file body**

Path: `Assets/3 - Scripts/Camera/SlowmoOnKill.cs`. Replace the entire current contents with:

```csharp
using System.Collections;
using UnityEngine;

/// <summary>
/// Brief Time.timeScale dip when an enemy dies. Duration scales by 1.2× per
/// streak tier — chained kills extend the same dip via end-time bookkeeping
/// instead of starting overlapping coroutines (which would fight each other
/// over Time.timeScale and end the slow-mo prematurely on the FIRST routine's
/// 0.45 s timer regardless of later kills).
///
/// Subscribes to KillstreakManager.OnKillRegistered (which fires AFTER the
/// streak count is incremented) rather than EnemyController.OnAnyEnemyDeath
/// directly, so we always see the post-increment count — solo kill is x1,
/// DOUBLE is x2, etc. The lazy-hook in Update handles the auto-create-order
/// race between the manager and this component.
/// </summary>
public class SlowmoOnKill : MonoBehaviour
{
    const float kBaseDuration   = 0.45f;
    const float kStackMultiplier = 1.2f;
    const float kSlowTimeScale  = 0.15f;

    float _slowmoEndTime;
    bool  _routineRunning;
    bool  _subscribed;

    void OnDisable()
    {
        var mgr = KillstreakManager.Instance;
        if (mgr != null) mgr.OnKillRegistered -= Handle;
        _subscribed = false;
    }

    void Update()
    {
        // Lazy hook — KillstreakManager may auto-create after us this scene.
        if (!_subscribed && KillstreakManager.Instance != null)
        {
            KillstreakManager.Instance.OnKillRegistered += Handle;
            _subscribed = true;
        }
    }

    void Handle(int newStreak)
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled) return;
        if (mgr.Input != null && !mgr.Input.fxSlowmoOnKill) return;

        // Streak 1 (solo kill) → baseDuration; each tier above multiplies by 1.2.
        int exp = Mathf.Max(0, newStreak - 1);
        float duration = kBaseDuration * Mathf.Pow(kStackMultiplier, exp);

        float candidateEnd = Time.unscaledTime + duration;
        if (candidateEnd > _slowmoEndTime) _slowmoEndTime = candidateEnd;

        if (!_routineRunning) StartCoroutine(Routine());
    }

    IEnumerator Routine()
    {
        _routineRunning = true;
        Time.timeScale = kSlowTimeScale;
        while (Time.unscaledTime < _slowmoEndTime) yield return null;
        Time.timeScale = 1f;
        _routineRunning = false;
    }
}
```

Note the two breaking changes vs. the old file:
- The old `OnEnable`/`OnDisable` pair subscribed to `EnemyController.OnAnyEnemyDeath` — replaced with a lazy hook to `KillstreakManager.OnKillRegistered`.
- `Handle()` now takes an `int newStreak` and computes duration from it.

- [ ] **Step 2: Compile-check**

Console: zero errors. Watch for `'EnemyController.OnAnyEnemyDeath'` warnings — nothing else in the codebase subscribes to that event, so removing this subscriber is fine (`KillstreakManager` is the only remaining subscriber after Task 1).

- [ ] **Step 3: Play-mode verification**

1. Press Play, get into combat range of one toy enemy. Kill it. The slow-mo dip should feel the same as before (~0.45 s).
2. Kill two enemies back to back (within ~5 s). The second kill's dip should feel noticeably longer (~0.54 s).
3. Chain three. The third dip should feel even longer (~0.65 s).
4. Let the streak break (wait past the decay window). Next kill should feel like a fresh ~0.45 s dip.

If you can't get back-to-back kills easily, lower `EnemyController.spawnInterval` in the inspector temporarily.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/Camera/SlowmoOnKill.cs"
git commit -m "feat(camera-fx): slow-mo duration stacks 1.2x per killstreak tier

Subscribes to KillstreakManager.OnKillRegistered (post-increment streak)
instead of EnemyController.OnAnyEnemyDeath. Uses an end-time scheduler so
chained kills extend the dip instead of overlapping coroutines that race
each other to restore timeScale=1 prematurely."
```

---

## Task 3 · `KillstreakHUD` — popup canvas

**Files:**
- Create: `Assets/3 - Scripts/UI/KillstreakHUD.cs`

After this commit the popup actually appears top-center, escalates, decays, and fades.

- [ ] **Step 1: Create the file**

Path: `Assets/3 - Scripts/UI/KillstreakHUD.cs`

```csharp
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Top-center killstreak popup. Subscribes to KillstreakManager's events,
/// looks up the tier in s_tiers (×2 DOUBLE KILL … ×11+ WICKED SICK cap),
/// updates the big skewed text + multiplier + decay bar, and animates in /
/// out with EaseOutBack + alpha. All animations use unscaledDeltaTime so
/// they keep playing during the slow-mo dip.
/// </summary>
public class KillstreakHUD : MonoBehaviour
{
    public static KillstreakHUD Instance { get; private set; }

    // (label, font size) indexed by streak count. Streak 0/1 = no popup.
    static readonly (string label, float fontSize)[] s_tiers =
    {
        /* 0 */ ("", 0f),
        /* 1 */ ("", 0f),
        /* 2 */ ("DOUBLE KILL",    42f),
        /* 3 */ ("TRIPLE KILL",    44f),
        /* 4 */ ("QUADRUPLE KILL", 46f),
        /* 5 */ ("RAMPAGE",        48f),
        /* 6 */ ("KILLING SPREE",  50f),
        /* 7 */ ("UNSTOPPABLE",    52f),
        /* 8 */ ("DOMINATING",     54f),
        /* 9 */ ("GODLIKE",        56f),
        /* 10 */ ("LEGENDARY",     58f),
        /* 11+ */ ("WICKED SICK",  60f),
    };

    // ── palette (matches the rest of the cyan HUDs) ───────────────────
    static readonly Color NavyOutline = new Color32(0x04, 0x10, 0x1E, 0xFF);
    static readonly Color CyanText    = new Color32(0x7B, 0xE2, 0xFF, 0xFF);
    static readonly Color CyanGlow    = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color CyanBright  = new Color32(0xB3, 0xEC, 0xFF, 0xFF);
    static readonly Color CapRedGlow  = new Color(0.70f, 0.30f, 0.30f, 0.55f);
    static readonly Color BarTrack    = new Color(1f, 1f, 1f, 0.10f);

    Canvas _canvas;
    CanvasGroup _group;
    RectTransform _root;
    TextMeshProUGUI _multText;
    TextMeshProUGUI _nameText;
    RectTransform _barFillRT;
    Shadow _nameGlowShadow;   // cyan halo (swapped to red at cap)

    bool _visible;
    bool _subscribed;
    Coroutine _visRoutine;
    Coroutine _punchRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("KillstreakHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<KillstreakHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildCanvas();
        ImmediateHide();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        var mgr = KillstreakManager.Instance;
        if (mgr != null)
        {
            mgr.OnKillRegistered -= HandleKill;
            mgr.OnStreakBroken   -= HandleBreak;
        }
    }

    void Update()
    {
        if (!_subscribed && KillstreakManager.Instance != null)
        {
            KillstreakManager.Instance.OnKillRegistered += HandleKill;
            KillstreakManager.Instance.OnStreakBroken   += HandleBreak;
            _subscribed = true;
        }

        if (_visible && _barFillRT != null && KillstreakManager.Instance != null)
            _barFillRT.localScale = new Vector3(KillstreakManager.Instance.DecayProgress01, 1f, 1f);
    }

    void HandleKill(int streak)
    {
        if (streak < 2) return; // no popup for solo kills
        ApplyTier(streak);
        if (!_visible)
        {
            StartVisibility(true);
        }
        else
        {
            if (_punchRoutine != null) StopCoroutine(_punchRoutine);
            _punchRoutine = StartCoroutine(PunchPulse());
        }
    }

    void HandleBreak()
    {
        if (!_visible) return;
        StartVisibility(false);
    }

    void ApplyTier(int streak)
    {
        int idx = Mathf.Clamp(streak, 2, s_tiers.Length - 1);
        var tier = s_tiers[idx];
        bool isCap = idx == s_tiers.Length - 1;

        if (_nameText != null)
        {
            _nameText.text = tier.label;
            _nameText.fontSize = tier.fontSize;
            _nameText.color = isCap ? Color.white : CyanText;
        }
        if (_multText != null)
        {
            _multText.text = "×" + streak;
        }
        if (_nameGlowShadow != null)
        {
            // Cap shifts the soft halo from cyan → red.
            _nameGlowShadow.effectColor = isCap
                ? CapRedGlow
                : new Color(CyanGlow.r, CyanGlow.g, CyanGlow.b, 0.55f);
        }
    }

    void StartVisibility(bool show)
    {
        _visible = show;
        if (_visRoutine != null) StopCoroutine(_visRoutine);
        _visRoutine = StartCoroutine(VisibilityRoutine(show));
    }

    void ImmediateHide()
    {
        _visible = false;
        _group.alpha = 0f;
        _root.localScale = Vector3.one * 0.6f;
    }

    IEnumerator VisibilityRoutine(bool show)
    {
        float dur = show ? 0.25f : 0.4f;
        float fromAlpha = _group.alpha;
        float toAlpha   = show ? 1f : 0f;
        Vector3 fromScale = _root.localScale;
        Vector3 toScale   = show ? Vector3.one : Vector3.one * 0.85f;

        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float k = show ? EaseOutBack(u) : u;
            _group.alpha = Mathf.Lerp(fromAlpha, toAlpha, u);
            _root.localScale = Vector3.Lerp(fromScale, toScale, k);
            yield return null;
        }
        _group.alpha = toAlpha;
        _root.localScale = toScale;
    }

    IEnumerator PunchPulse()
    {
        const float dur = 0.15f;
        const float peak = 1.08f;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float k = u < 0.5f ? Mathf.Lerp(1f, peak, u * 2f)
                               : Mathf.Lerp(peak, 1f, (u - 0.5f) * 2f);
            _root.localScale = Vector3.one * k;
            yield return null;
        }
        _root.localScale = Vector3.one;
        _punchRoutine = null;
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    // ── canvas build ──────────────────────────────────────────────────

    void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 830; // above LetterboxBars (820), with the other gameplay HUDs
        HUDSceneGate.Register(_canvas);

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.interactable = false;
        _group.blocksRaycasts = false;

        // Root anchored to the top-center of the screen, ~140 px down so it
        // sits under the compass (the compass canvas is sortingOrder 300 and
        // takes the top ~120 px of the screen).
        _root = NewUI("Root", transform);
        _root.anchorMin = new Vector2(0.5f, 1f);
        _root.anchorMax = new Vector2(0.5f, 1f);
        _root.pivot     = new Vector2(0.5f, 1f);
        _root.anchoredPosition = new Vector2(0f, -140f);
        var vlg = _root.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 4f;
        var fitter = _root.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // Multiplier "×2" — sits above the name, smaller font, bright cyan.
        _multText = NewText(_root, "Mult", "×2", 22f, FontStyles.Bold, CyanBright);
        _multText.alignment = TextAlignmentOptions.Center;
        _multText.characterSpacing = 4f;
        AddShadow(_multText, new Color(CyanGlow.r, CyanGlow.g, CyanGlow.b, 0.55f), Vector2.zero);
        AddShadow(_multText, NavyOutline, new Vector2(0f, -2f));

        // Tier name "DOUBLE KILL" — italic stand-in for the spec's -6° skew.
        // Real CSS skew isn't expressible on a TMP RectTransform without a
        // custom shader; italic on the Techno SDF font is the closest we get.
        _nameText = NewText(_root, "Name", "DOUBLE KILL", 42f,
                            FontStyles.Bold | FontStyles.Italic, CyanText);
        _nameText.alignment = TextAlignmentOptions.Center;
        _nameText.characterSpacing = 4f;
        // Cyan halo (cached so ApplyTier can swap to red at cap).
        _nameGlowShadow = AddShadow(_nameText,
            new Color(CyanGlow.r, CyanGlow.g, CyanGlow.b, 0.55f), Vector2.zero);
        // Navy outline (4-way Shadow stack — Unity's Outline component is
        // softer than what we want).
        AddShadow(_nameText, NavyOutline, new Vector2(-2f, 0f));
        AddShadow(_nameText, NavyOutline, new Vector2( 2f, 0f));
        AddShadow(_nameText, NavyOutline, new Vector2(0f, -2f));
        AddShadow(_nameText, NavyOutline, new Vector2(0f,  2f));
        // Drop shadow below.
        AddShadow(_nameText, NavyOutline, new Vector2(0f, -3f));

        // Decorative "streak line" gradient bar between name and decay bar.
        var lineRT = NewUI("StreakLine", _root);
        var lineLE = lineRT.gameObject.AddComponent<LayoutElement>();
        lineLE.preferredWidth  = 240f;
        lineLE.preferredHeight = 2f;
        var lineImg = lineRT.gameObject.AddComponent<Image>();
        lineImg.color = new Color(CyanText.r, CyanText.g, CyanText.b, 0.7f);
        lineImg.raycastTarget = false;

        // Decay bar (track + fill driven by KillstreakManager.DecayProgress01).
        var barRT = NewUI("DecayBar", _root);
        var barLE = barRT.gameObject.AddComponent<LayoutElement>();
        barLE.preferredWidth  = 240f;
        barLE.preferredHeight = 3f;
        var barBg = barRT.gameObject.AddComponent<Image>();
        barBg.color = BarTrack;
        barBg.raycastTarget = false;

        _barFillRT = NewUI("Fill", barRT);
        _barFillRT.anchorMin = Vector2.zero;
        _barFillRT.anchorMax = Vector2.one;
        _barFillRT.pivot     = new Vector2(0f, 0.5f);
        _barFillRT.offsetMin = Vector2.zero;
        _barFillRT.offsetMax = Vector2.zero;
        var fillImg = _barFillRT.gameObject.AddComponent<Image>();
        fillImg.color = CyanGlow;
        fillImg.raycastTarget = false;
    }

    static Shadow AddShadow(TextMeshProUGUI t, Color color, Vector2 distance)
    {
        var sh = t.gameObject.AddComponent<Shadow>();
        sh.effectColor = color;
        sh.effectDistance = distance;
        return sh;
    }

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static TextMeshProUGUI NewText(Transform parent, string name, string text,
                                   float size, FontStyles style, Color color)
    {
        var rt = NewUI(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(t);
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        return t;
    }

    static TMP_FontAsset _hudFont;
    static bool _hudFontResolved;

    static void ApplyDefaultFont(TextMeshProUGUI t)
    {
        if (!_hudFontResolved)
        {
            _hudFont = Resources.Load<TMP_FontAsset>("Techno SDF");
            if (_hudFont == null)
            {
                var raw = Resources.Load<Font>("Techno");
                if (raw != null) _hudFont = TMP_FontAsset.CreateFontAsset(raw);
            }
            if (_hudFont == null) _hudFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            _hudFontResolved = true;
        }
        if (_hudFont != null) t.font = _hudFont;
    }
}
```

- [ ] **Step 2: Compile-check**

Console: zero errors. `HUDSceneGate`, `TextMeshProUGUI`, `Shadow`, `KillstreakManager` are all already in the project — no missing references expected.

- [ ] **Step 3: Play-mode verification**

1. Press Play. Spawn point view: no popup visible.
2. Kill one toy — no popup (correct; first kill is silent).
3. Kill a second within 10 s — `×2 DOUBLE KILL` should pop in with a scale-bounce. The decay bar underneath should be full and start draining over 9 s.
4. Kill a third within 9 s — text updates to `×3 TRIPLE KILL`, font grows from 42 → 44 px, popup does a quick scale-punch (~0.15 s), decay bar snaps back to full.
5. Stop killing. Decay bar drains to 0; popup fades out (alpha + scale shrink) over ~0.4 s.
6. Verify the popup is hidden when you exit to main menu and on a fresh game session.

If the popup is colliding visually with the compass, tweak `_root.anchoredPosition` from `(0, -140)` to a larger negative Y (e.g. `(0, -180)`) in `BuildCanvas`.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/UI/KillstreakHUD.cs"
git commit -m "feat(ui): KillstreakHUD — top-center popup driven by KillstreakManager events

Skewed Impact-style cyan text + multiplier + decay bar, scale-bounce enter,
punch-pulse on streak advance, fade-out on streak break. Cap (x11+ WICKED
SICK) shifts halo cyan->red and text cyan->white. All animations use
unscaledDeltaTime so they keep playing during the slow-mo dip."
```

---

## Task 4 · Seed singletons in MainMenuController

**Files:**
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs` — extend `EnsureGameplaySingletons`

After this commit the singletons exist immediately when the player clicks Play, so the first frame of gameplay can already register kills (no auto-create timing window).

- [ ] **Step 1: Append the two new seeds**

Open `Assets/3 - Scripts/UI/MainMenuController.cs`. Find the existing `static void EnsureGameplaySingletons()` method (around line 473). After the existing `AutosaveManager` seed block (the last `if` inside the method), add these two blocks:

```csharp
        if (KillstreakManager.Instance == null)
        {
            var go = new GameObject("KillstreakManager");
            DontDestroyOnLoad(go);
            go.AddComponent<KillstreakManager>();
        }
        if (KillstreakHUD.Instance == null)
        {
            var go = new GameObject("KillstreakHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<KillstreakHUD>();
        }
```

- [ ] **Step 2: Compile-check**

Console: zero errors.

- [ ] **Step 3: Play-mode verification — full end-to-end**

1. From MainMenu, click "Play" / "New Game" / pick a save.
2. No popup or compass-area HUD visible in the main menu (HUDSceneGate hides it).
3. Once in the gameplay scene, get to the dark side and kill enemies. Confirm popup behavior:
   - 2nd kill within 10 s of 1st → `×2 DOUBLE KILL` at 42 px.
   - 3rd kill within 9 s of 2nd → `×3 TRIPLE KILL` at 44 px, punch pulse, decay bar reset.
   - 4th kill within 8 s of 3rd → `×4 QUADRUPLE KILL` at 46 px.
   - And so on. If you cap at ×11+ WICKED SICK (very hard — would need 11 kills in less than a minute), confirm text turns white + glow turns red.
4. Slow-mo: each successive kill in a streak should feel longer than the last. Solo kill after a break should feel short again.
5. Quit to main menu mid-streak; popup disappears (HUDSceneGate), state resets on next gameplay.
6. Die while in a streak; popup fades out (`ResourceManager.OnDeath` resets the manager).

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/UI/MainMenuController.cs"
git commit -m "feat(menu): seed KillstreakManager + KillstreakHUD in EnsureGameplaySingletons

Matches the pattern used for PlayerWallet/TutorialUI/Hotbar/etc. so the
singletons exist the moment gameplay scene loads — no auto-create timing
window where the first kill could miss the manager."
```

---

## Final acceptance walkthrough

After Task 4 is committed:

1. Load `Assets/1.6.7.7.7.unity`, press Play, get out under the dark side, and kill enemies in rapid succession.
2. Confirm each item from the spec's acceptance criteria:
   - 2 kills within 10 s → `DOUBLE KILL` ×2 popup, cyan B-style typography.
   - 3rd kill within 9 s of 2nd → advances to `TRIPLE KILL` ×3, font grows, decay bar snaps full, punch pulse fires.
   - Letting the decay bar empty → fades the popup, resets streak.
   - Slow-mo: ×2 ≈ 0.54 s, ×5 ≈ 0.93 s, ×10 ≈ 2.32 s. Two kills 0.1 s apart maintain the full dip (no premature `timeScale = 1f` flicker).
   - Hidden in MainMenu, visible in gameplay; persists across floating-origin shifts (UI only — no transform shift involved).
   - Player death mid-streak resets the popup and the count.

If anything's off, the failing item points to the exact task: text/animation → Task 3, duration/timing → Task 2, count/decay → Task 1, MainMenu-flash on transition → Task 4.
