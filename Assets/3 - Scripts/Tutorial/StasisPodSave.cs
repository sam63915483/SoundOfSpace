using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The stasis-pod SAVE ritual (2026-07-24). Flow:
//   valve button opens the door → player steps fully into the pod → the door
//   seals behind them (StasisPodDoor's entry rule, ~2s) → once fully closed,
//   a ~3s "consciousness upload" overlay plays → clears → the door opens and
//   holds → leaving closes it 2s later. The save (SaveSystem.Save) fires at
//   the moment of full coverage. Lands as the "stasis pod 1" slot; the
//   existing autosave/manual flows are untouched.
//
// Visual: the "DATA LANES" design (picked from the localhost showcase,
// 2026-07-24) — a dark blue veil fills the screen from the TOP DOWN with a
// glowing leading edge; inside it, voxel pixels FLOW UPWARD in column-synced
// surge waves (upload channels) against binary digit columns raining DOWN,
// over scanlines, with a segmented progress bar and pulsing UPLOADING text.
// The covered region is a RectMask2D, so every element clips exactly to the
// veil's frontier.
public class StasisPodSave : MonoBehaviour
{
    [Tooltip("Save-slot name this pod writes (fixed slot; overwritten each ritual).")]
    public string saveSlotName = "stasis pod 1";
    public float fillSeconds = 1.2f;
    public float holdSeconds = 0.7f;
    public float clearSeconds = 1.2f;
    [Range(0f, 1f), Tooltip("Peak opacity of the whole upload/download overlay (1 = fully opaque).")]
    public float overlayOpacity = 0.5f;

    StasisPodDoor _door;
    bool _running;
    bool _armed = true;          // re-armed when the player leaves the pod
    bool _leftPodSinceLoad;      // false until the player has been OUTSIDE once
    string _labelBase = "UPLOADING";

    // Overlay bleed past every screen edge — kills edge gaps on any
    // display/aspect (the 4K-TV "see through at top/bottom" bug).
    const float EdgeBleed = 300f;

    // ── Overlay ──────────────────────────────────────────────────────────────
    Canvas _canvas;
    CanvasGroup _group;
    RectTransform _rootRT;   // full-screen; W/H source
    RectTransform _maskRT;   // the covered region (RectMask2D)
    Texture2D _veilTex, _sqTex, _streakTex, _glowTex, _scanTex;
    Sprite _sqSprite, _streakSprite;
    TextMeshProUGUI _uploadText;
    float _uploadTextT;
    float _cov;              // current 0..1 coverage
    float _fxT;              // ritual-local time (drives all waves/flicker)
    float W = 1920f, H = 1080f;
    float _cs;               // voxel cell size

    // lanes + rising pixels (DATA LANES core)
    struct LaneInfo { public float ph, baseSp; }
    LaneInfo[] _lanes;
    class Px
    {
        public RectTransform rt; public Image img;
        public int lane; public float y, hue, fl, age; public bool streaking;
    }
    readonly List<Px> _px = new List<Px>();
    static readonly Color32[] Pal = {
        new Color32(8, 30, 60, 255), new Color32(12, 66, 118, 255),
        new Color32(22, 126, 196, 255), new Color32(74, 224, 255, 255) };

    // falling binary rain columns
    class RainCol { public RectTransform rt; public TextMeshProUGUI txt; public float xFrac, y, spFrac; public float fs; }
    readonly List<RainCol> _rain = new List<RainCol>();
    static readonly StringBuilder _sb = new StringBuilder(160);

    Image[] _barSegs;
    const int BarSegments = 14;
    float _lastCs = -1f;

    void Awake()
    {
        _door = GetComponent<StasisPodDoor>();
        if (_door == null) _door = GetComponentInParent<StasisPodDoor>();
    }

    void Update()
    {
        if (_door == null || _running) return;

        var seq = GetComponentInParent<ShuttleArrivalSequence>();
        if (seq != null && seq.IsActive) return;   // never during the intro

        // Boot window: while the save restore is still settling, "Outside"
        // readings are untrusted (the player may simply not be teleported yet)
        // and must never arm a real save. A Deep-inside-a-sealed-pod reading
        // IS trusted — only a pod-save load puts you there — and fires the
        // DOWNLOAD wake-up the same frame, so it's the first thing on screen.
        bool boot = Time.timeSinceLevelLoad < 3f;

        if (!boot && _door.CurrentZone == StasisPodDoor.Zone.Outside)
        {
            _armed = true;
            _leftPodSinceLoad = true;
        }

        if (_armed && _door.CurrentZone == StasisPodDoor.Zone.Deep && _door.IsFullyClosed)
        {
            _armed = false;
            StartCoroutine(Ritual(download: boot || !_leftPodSinceLoad));
        }
    }

    IEnumerator Ritual(bool download)
    {
        _running = true;
        _labelBase = download ? "DOWNLOADING" : "UPLOADING";
        TutorialGate.LockAll();
        TutorialGate.Unlock(TutorialAbility.MouseLook);

        BuildOverlay();
        _fxT = 0f;

        float t;
        if (!download)
        {
            // ── Fill: the frontier descends (smoothstepped, like the showcase) ──
            t = 0f;
            while (t < fillSeconds)
            {
                t += Time.deltaTime;
                _cov = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / fillSeconds));
                TickFx(Time.deltaTime);
                yield return null;
            }
        }
        _cov = 1f;

        // ── Full coverage. UPLOAD: write the save. DOWNLOAD: never save —
        //    this is the wake-up playback of an existing consciousness.
        if (!download) SaveSystem.Save(saveSlotName);
        float holdFor = download ? holdSeconds + 0.9f : holdSeconds;   // a beat longer to read DOWNLOADING
        t = 0f;
        while (t < holdFor)
        {
            t += Time.deltaTime;
            TickFx(Time.deltaTime);
            yield return null;
        }

        // ── Clear: whole overlay fades back to the world ──
        t = 0f;
        while (t < clearSeconds)
        {
            t += Time.deltaTime;
            if (_group != null) _group.alpha = overlayOpacity * (1f - Mathf.Clamp01(t / clearSeconds));
            TickFx(Time.deltaTime);
            yield return null;
        }
        DestroyOverlay();

        TutorialGate.UnlockAll();
        if (_door != null) _door.OpenHold();
        _running = false;
    }

    // ── Per-frame drive ──────────────────────────────────────────────────────
    void TickFx(float dt)
    {
        _fxT += dt;
        if (_rootRT != null && _rootRT.rect.width > 100f) { W = _rootRT.rect.width; H = _rootRT.rect.height; }
        float edge = _cov * H;

        // Cell size tracks the REAL (bleed-extended) width, whatever the
        // display; resize the pixel squares once when it settles.
        _cs = Mathf.Max(10f, W / 64f);
        if (Mathf.Abs(_cs - _lastCs) > 0.25f)
        {
            _lastCs = _cs;
            foreach (var p in _px)
                p.rt.sizeDelta = new Vector2(_cs - 1f, p.streaking ? _cs * 2.6f : _cs - 1f);
        }

        // Covered region + veil follow the frontier.
        if (_maskRT != null) _maskRT.anchorMin = new Vector2(0f, 1f - _cov);

        TickLanes(dt, edge);
        TickRain(dt, edge);
        TickBar();
        TickUploadText();
    }

    // Column-synced surge waves: surge = 1 + 2.4 * max(0, sin(t*1.6 + lanePhase)).
    void TickLanes(float dt, float edge)
    {
        for (int i = 0; i < _px.Count; i++)
        {
            var p = _px[i];
            var L = _lanes[p.lane];
            float surge = 1f + 2.4f * Mathf.Max(0f, Mathf.Sin(_fxT * 1.6f + L.ph));
            p.age += dt;
            p.y -= L.baseSp * surge * H * dt;

            if (p.y < -_cs * 2f)   // rose off the top → reborn at the frontier
            {
                p.y = edge + Random.value * 40f;
                p.age = 0f;
                p.lane = Random.Range(0, _lanes.Length);
            }

            float x = (p.lane + 0.5f) * _cs - _cs * 0.5f;
            bool surging = surge > 2.2f;
            if (surging != p.streaking)
            {
                p.streaking = surging;
                p.img.sprite = surging ? _streakSprite : _sqSprite;
                p.rt.sizeDelta = new Vector2(_cs - 1f, surging ? _cs * 2.6f : _cs - 1f);
            }

            float fadeIn = Mathf.Min(1f, p.age / 0.35f);
            float fl = 0.75f + 0.25f * Mathf.Sin(_fxT * 9f + p.fl);
            float bright = surging ? 0.25f : 0f;
            Color c = Pal[(int)(p.hue * 3.999f)];
            c.a = Mathf.Clamp01((0.60f + 0.30f * fl + bright) * fadeIn);
            p.img.color = c;
            p.rt.anchoredPosition = new Vector2(x, -p.y);
        }
    }

    void TickRain(float dt, float edge)
    {
        for (int i = 0; i < _rain.Count; i++)
        {
            var c = _rain[i];
            c.y += c.spFrac * H * dt;
            if (c.y > edge + 40f)
            {
                c.y = -20f - Random.value * H * 0.4f;
                RebuildRainText(c);
            }
            c.rt.anchoredPosition = new Vector2(c.xFrac * W, -c.y);
        }
    }

    void TickBar()
    {
        if (_barSegs == null) return;
        // Bar lives at 62% of the VISIBLE screen (the root is bleed-extended).
        float visW = W - 2f * EdgeBleed, visH = H - 2f * EdgeBleed;
        float bw = visW * 0.3f, segW = bw / BarSegments;
        float y = -(EdgeBleed + 0.62f * visH);
        int filled = Mathf.RoundToInt(_cov * BarSegments);
        for (int i = 0; i < _barSegs.Length; i++)
        {
            var rt = _barSegs[i].rectTransform;
            rt.anchoredPosition = new Vector2(-bw * 0.5f + i * segW, y);
            rt.sizeDelta = new Vector2(segW - 3f, visH * 0.014f);
            _barSegs[i].color = i < filled
                ? new Color(120f / 255f, 235f / 255f, 1f, 0.95f)
                : new Color(60f / 255f, 110f / 255f, 150f / 255f, 0.30f);
        }
    }

    void TickUploadText()
    {
        if (_uploadText == null) return;
        _uploadTextT += Time.deltaTime;
        int dots = Mathf.FloorToInt(_uploadTextT * 2.5f) % 4;
        _uploadText.text = _labelBase + new string('.', dots);
        var c = _uploadText.color;
        // Appears once the frontier passes ~55% of the screen (showcase rule).
        c.a = _cov > 0.55f ? 0.72f + 0.28f * Mathf.Sin(_uploadTextT * 6f) : 0f;
        _uploadText.color = c;
    }

    // ── Overlay construction ─────────────────────────────────────────────────
    void BuildOverlay()
    {
        var go = new GameObject("StasisSaveOverlay");
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 32766;   // above EVERYTHING, helmet frame included
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _group = go.AddComponent<CanvasGroup>();
        _group.alpha = overlayOpacity;

        // Root bleeds past every screen edge so no display/aspect/overscan can
        // ever show a gap at the borders.
        var rootGO = new GameObject("Root");
        rootGO.transform.SetParent(go.transform, false);
        _rootRT = rootGO.AddComponent<RectTransform>();
        _rootRT.anchorMin = Vector2.zero; _rootRT.anchorMax = Vector2.one;
        _rootRT.offsetMin = new Vector2(-EdgeBleed, -EdgeBleed);
        _rootRT.offsetMax = new Vector2(EdgeBleed, EdgeBleed);

        W = 1920f + 2f * EdgeBleed; H = 1080f + 2f * EdgeBleed;   // until the first live rect read
        _cs = Mathf.Max(10f, W / 64f);
        _lastCs = -1f;

        // The covered region: everything inside clips to the veil's frontier.
        var maskGO = new GameObject("CoveredRegion");
        maskGO.transform.SetParent(rootGO.transform, false);
        _maskRT = maskGO.AddComponent<RectTransform>();
        _maskRT.anchorMin = new Vector2(0f, 1f);   // zero height at start
        _maskRT.anchorMax = Vector2.one;
        _maskRT.offsetMin = Vector2.zero; _maskRT.offsetMax = Vector2.zero;
        maskGO.AddComponent<RectMask2D>();

        // Veil gradient (dark top → deep blue → teal at the frontier).
        var veil = NewImage(maskGO.transform, "Veil");
        veil.sprite = MakeVeilSprite();
        Stretch(veil.rectTransform);

        BuildLanePixels(maskGO.transform);
        BuildRain(maskGO.transform);

        // Scanlines: tiled 1×4 sprite over the covered region.
        var scan = NewImage(maskGO.transform, "Scanlines");
        scan.sprite = MakeScanSprite();
        scan.type = Image.Type.Tiled;
        scan.color = Color.white;
        Stretch(scan.rectTransform);

        // Leading edge: glow strip + hard bright line pinned to the frontier.
        var glow = NewImage(maskGO.transform, "EdgeGlow");
        glow.sprite = MakeEdgeGlowSprite();
        var grt = glow.rectTransform;
        grt.anchorMin = new Vector2(0f, 0f); grt.anchorMax = new Vector2(1f, 0f);
        grt.pivot = new Vector2(0.5f, 0f);
        grt.offsetMin = new Vector2(0f, 0f); grt.offsetMax = new Vector2(0f, 40f);
        var line = NewImage(maskGO.transform, "EdgeLine");
        line.color = new Color(140f / 255f, 240f / 255f, 1f, 0.9f);
        var lrt = line.rectTransform;
        lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(1f, 0f);
        lrt.pivot = new Vector2(0.5f, 0f);
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = new Vector2(0f, 4f);

        BuildBar(maskGO.transform);

        // UPLOADING — outside the mask, dead center, gated on coverage.
        var uGO = new GameObject("UploadText");
        uGO.transform.SetParent(rootGO.transform, false);
        _uploadText = uGO.AddComponent<TextMeshProUGUI>();
        _uploadText.text = "UPLOADING";
        _uploadText.fontSize = 64;
        _uploadText.fontStyle = FontStyles.Bold;
        _uploadText.characterSpacing = 12f;
        _uploadText.alignment = TextAlignmentOptions.Center;
        _uploadText.color = new Color(0.79f, 0.95f, 1f, 0f);
        _uploadText.raycastTarget = false;
        var urt = _uploadText.rectTransform;
        urt.anchorMin = urt.anchorMax = new Vector2(0.5f, 0.5f);
        urt.pivot = new Vector2(0.5f, 0.5f);
        urt.anchoredPosition = Vector2.zero;
        urt.sizeDelta = new Vector2(900f, 120f);
        _uploadTextT = 0f;
        _cov = 0f;
    }

    void BuildLanePixels(Transform parent)
    {
        const int laneCount = 64;   // lane x = lane * cs, cs tracks live width
        _lanes = new LaneInfo[laneCount];
        for (int i = 0; i < laneCount; i++)
            _lanes[i] = new LaneInfo { ph = Random.value * 6.28f, baseSp = 0.10f + Random.value * 0.10f };

        _sqSprite = MakeSolidSprite(ref _sqTex);
        _streakSprite = MakeStreakSprite();

        _px.Clear();
        for (int i = 0; i < 300; i++)
        {
            var img = NewImage(parent, "px" + i);
            img.sprite = _sqSprite;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);   // top-left space
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(_cs - 1f, _cs - 1f);
            _px.Add(new Px
            {
                rt = rt, img = img,
                lane = Random.Range(0, laneCount),
                y = Random.value * H,
                hue = Random.value,
                fl = Random.value * 6.28f,
                age = Random.value * 2f,
            });
        }
    }

    void BuildRain(Transform parent)
    {
        _rain.Clear();
        const int cols = 36;
        for (int i = 0; i < cols; i++)
        {
            var go = new GameObject("rain" + i);
            go.transform.SetParent(parent, false);
            var txt = go.AddComponent<TextMeshProUGUI>();
            txt.fontStyle = FontStyles.Bold;
            txt.alignment = TextAlignmentOptions.Bottom;
            txt.richText = true;
            txt.raycastTarget = false;
            txt.enableWordWrapping = false;
            var rt = txt.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0f);                   // pivot = the HEAD glyph
            var c = new RainCol
            {
                rt = rt, txt = txt,
                xFrac = (i + 0.5f) / cols,
                y = -Random.value * H,
                spFrac = 0.35f + Random.value * 0.9f,
                fs = Random.Range(16f, 30f),
            };
            txt.fontSize = c.fs;
            rt.sizeDelta = new Vector2(60f, c.fs * 1.25f * 6f + 20f);
            RebuildRainText(c);
            _rain.Add(c);
        }
    }

    // Tail (dim, top) → head (bright, bottom); rebuilt only on recycle.
    void RebuildRainText(RainCol c)
    {
        _sb.Length = 0;
        _sb.Append("<color=#5AD2FF>");
        string[] alphas = { "26", "44", "66", "88", "AA" };
        for (int k = 0; k < alphas.Length; k++)
            _sb.Append("<alpha=#").Append(alphas[k]).Append('>')
               .Append(Random.value < 0.5f ? '0' : '1').Append('\n');
        _sb.Append("</color><color=#D2FFFF><alpha=#F2>").Append(Random.value < 0.5f ? '0' : '1');
        c.txt.text = _sb.ToString();
    }

    void BuildBar(Transform parent)
    {
        // Segments anchored to the root's top-center; TickBar positions them
        // against the live VISIBLE screen size each frame.
        _barSegs = new Image[BarSegments];
        for (int i = 0; i < BarSegments; i++)
        {
            var img = NewImage(parent, "bar" + i);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            _barSegs[i] = img;
        }
    }

    // ── Sprites ──────────────────────────────────────────────────────────────
    Image NewImage(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    // #03060d (top) → #07203c (70% down) → #0f5a86 (frontier).
    Sprite MakeVeilSprite()
    {
        const int H0 = 256;
        _veilTex = new Texture2D(4, H0, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Color top = new Color(0.012f, 0.024f, 0.051f);
        Color mid = new Color(0.027f, 0.125f, 0.235f);
        Color bot = new Color(0.059f, 0.353f, 0.525f);
        for (int y = 0; y < H0; y++)
        {
            float v = 1f - y / (float)(H0 - 1);   // 0 top .. 1 bottom(frontier)
            Color c = v < 0.7f ? Color.Lerp(top, mid, v / 0.7f) : Color.Lerp(mid, bot, (v - 0.7f) / 0.3f);
            c.a = 1f;
            for (int x = 0; x < 4; x++) _veilTex.SetPixel(x, y, c);
        }
        _veilTex.Apply();
        return Sprite.Create(_veilTex, new Rect(0, 0, 4, H0), new Vector2(0.5f, 0.5f), 100f);
    }

    Sprite MakeSolidSprite(ref Texture2D tex)
    {
        tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color[16];
        for (int i = 0; i < 16; i++) px[i] = Color.white;
        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
    }

    // Solid block on top ~40%, fading tail below (the surge streak).
    Sprite MakeStreakSprite()
    {
        const int H0 = 64;
        _streakTex = new Texture2D(4, H0, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < H0; y++)
        {
            float v = y / (float)(H0 - 1);        // 0 bottom .. 1 top
            float a = v > 0.6f ? 1f : Mathf.Pow(v / 0.6f, 1.4f);
            var c = new Color(1f, 1f, 1f, a);
            for (int x = 0; x < 4; x++) _streakTex.SetPixel(x, y, c);
        }
        _streakTex.Apply();
        return Sprite.Create(_streakTex, new Rect(0, 0, 4, H0), new Vector2(0.5f, 0.5f), 100f);
    }

    Sprite MakeEdgeGlowSprite()
    {
        const int H0 = 32;
        _glowTex = new Texture2D(4, H0, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < H0; y++)
        {
            float v = 1f - y / (float)(H0 - 1);   // bright at the bottom
            var c = new Color(90f / 255f, 220f / 255f, 1f, 0.45f * v * v);
            for (int x = 0; x < 4; x++) _glowTex.SetPixel(x, y, c);
        }
        _glowTex.Apply();
        return Sprite.Create(_glowTex, new Rect(0, 0, 4, H0), new Vector2(0.5f, 0.5f), 100f);
    }

    // 1 dark row + 3 clear rows, tiled → a scanline every 4 units.
    Sprite MakeScanSprite()
    {
        _scanTex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                _scanTex.SetPixel(x, y, y == 3 ? new Color(0f, 0f, 0f, 0.15f) : Color.clear);
        _scanTex.Apply();
        return Sprite.Create(_scanTex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 1f);
    }

    void DestroyOverlay()
    {
        if (_canvas != null) Destroy(_canvas.gameObject);
        foreach (var t in new[] { _veilTex, _sqTex, _streakTex, _glowTex, _scanTex })
            if (t != null) Destroy(t);
        _veilTex = _sqTex = _streakTex = _glowTex = _scanTex = null;
        _px.Clear();
        _rain.Clear();
        _barSegs = null;
        _canvas = null; _group = null; _rootRT = null; _maskRT = null; _uploadText = null;
    }

    void OnDestroy()
    {
        if (_running) TutorialGate.UnlockAll();
        DestroyOverlay();
    }
}
