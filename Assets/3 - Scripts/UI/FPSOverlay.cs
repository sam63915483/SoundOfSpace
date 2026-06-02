using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-left perf overlay used to diagnose CPU/GPU bottlenecks.
///
/// Readouts (uses ProfilerRecorder; works in editor AND builds):
///   FPS  - smoothed FPS (~0.25s exponential)
///   ms   - total frame time (Time.unscaledDeltaTime * 1000)
///   cpu  - main thread ms (ProfilerCategory.Internal "Main Thread")
///   gpu  - GPU ms (ProfilerCategory.Render "GPU Frame Time" / FrameTimingManager fallback)
///   draw - draw calls this frame
///   gc   - bytes allocated by managed scripts this frame, rounded to KB
///   obj  - count of spawned enemies + NPCs + audience members in scene
///   min  - 1% low FPS over the last 1s
///
/// Diagnosis cheat-sheet:
///   cpu ≈ ms, gpu &lt; ms       → CPU-bound. Look at per-frame Update cost.
///   gc &gt; ~50 KB / frame       → GC pressure; collection pauses are the spikes.
///   obj high (audience etc)   → many MonoBehaviour.Update calls.
///   draw spiky                → batching collapse or runtime Instantiate.
///
/// Hotkeys:
///   F3 - toggle overlay visibility
///   F4 - A/B atmospheres+oceans (PlanetEffects.displayAtmospheres/Oceans).
///   F5 - A/B physics tick rate (halves Time.fixedDeltaTime).
///   F6 - freeze Concert system (ConcertLightProgram, ConcertLaser, etc).
///        If FPS jumps with [concert FROZEN] shown, those 11 per-frame scripts
///        were the cost.
///   F7 - freeze spawned NPCs/enemies/audience (SpawnedAlienNPC, EnemyController,
///        AudienceMember, NPCWaveAnimation, AlienNPCSpawner, TreeSpawner,
///        MushroomSpawner, EnemySpawner). If FPS jumps with [world FROZEN]
///        shown, streamed-entity Update/LateUpdate is the cost.
///
/// Freeze toggles disable .enabled on matched MonoBehaviours and remember
/// which ones were touched so restore only re-enables those (not anything
/// the game disabled itself in between).
/// </summary>
public class FPSOverlay : MonoBehaviour
{
    public static FPSOverlay Instance { get; private set; }

    const KeyCode ToggleKey = KeyCode.F3;
    const KeyCode AtmoToggleKey = KeyCode.F4;
    const KeyCode PhysicsToggleKey = KeyCode.F5;
    const KeyCode FreezeConcertKey = KeyCode.F6;
    const KeyCode FreezeWorldKey = KeyCode.F7;
    const KeyCode FreezeCameraFxKey = KeyCode.F8;
    const KeyCode FreezeAIKey = KeyCode.F9;
    const float SampleWindowSec = 1f;
    const float SmoothingSec = 0.25f;
    const float ObjCountIntervalSec = 1f;

    static readonly HashSet<string> ConcertTypeNames = new HashSet<string>
    {
        "ConcertLightProgram", "ConcertLaser", "ConcertBlinder", "ConcertStrobeLight",
        "ConcertConeLight", "ConcertFogPuff", "ConcertHaze", "ConcertAudioDirector",
        "ConcertStageHub", "SpeakerSource", "AudienceSpawner",
    };
    static readonly HashSet<string> WorldTypeNames = new HashSet<string>
    {
        "SpawnedAlienNPC", "EnemyController", "AudienceMember", "NPCWaveAnimation",
        "AlienNPCSpawner", "TreeSpawner", "MushroomSpawner", "EnemySpawner",
        "SpawnedTree",
    };
    static readonly HashSet<string> CameraFxTypeNames = new HashSet<string>
    {
        "CameraFOVFX", "CameraTransformFX", "CombatFX", "LensFlareRegistry",
        "SpeedLinesOverlay", "SlowmoOnKill", "MoodColorGrade", "FilmGrainOverlay",
        "LetterboxBars", "DamageFlashOverlay", "VignetteOverlay", "CameraShake",
        "ChromaticAberrationOverlay", "LensDirtOverlay", "AnamorphicStreaks",
        "RadialMotionBlurEffect",
    };
    static readonly HashSet<string> AITypeNames = new HashSet<string>
    {
        "HALCommentator", "AIStoryController", "AIChatScreen",
    };

    Canvas _canvas;
    TextMeshProUGUI _text;

    readonly Queue<float> _frameTimes = new Queue<float>(256);
    float _windowAccum;
    float _smoothedDt = 1f / 60f;
    float _smoothedCpuMs;
    float _smoothedGpuMs;
    float _smoothedGcKb;

    ProfilerRecorder _mainThreadRecorder;
    ProfilerRecorder _gpuRecorder;
    ProfilerRecorder _drawCallsRecorder;
    // GC: we use GC.GetTotalAllocatedBytes(false) and diff frame-to-frame.
    // The ProfilerRecorder "GC Allocated In Frame" counter doesn't populate
    // unless the Editor profiler is recording — GC.GetTotalAllocatedBytes
    // works in editor AND builds without any hookup.
    long _prevGcBytes;

    readonly FrameTiming[] _frameTimings = new FrameTiming[1];

    int _lastFps = -1, _lastMin = -1, _lastMs10 = -1, _lastCpuMs10 = -1, _lastGpuMs10 = -1;
    int _lastDraws = -1, _lastGcKb = -1, _lastObj = -1;
    bool _lastAtmoState, _lastPhysState, _lastConcertState, _lastWorldState;

    bool _visible = true;
    readonly StringBuilder _sb = new StringBuilder(384);

    PlanetEffects _planetFx;
    bool _atmoDisabled;
    bool _origDisplayAtmospheres, _origDisplayOceans;

    bool _physicsHalved;
    float _origFixedDeltaTime;

    readonly List<Behaviour> _frozenConcert  = new List<Behaviour>();
    readonly List<Behaviour> _frozenWorld    = new List<Behaviour>();
    readonly List<Behaviour> _frozenCameraFx = new List<Behaviour>();
    readonly List<Behaviour> _frozenAI       = new List<Behaviour>();

    int _objCount;
    float _objCountTimer = ObjCountIntervalSec;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("FPSOverlay");
        DontDestroyOnLoad(go);
        go.AddComponent<FPSOverlay>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();

        _mainThreadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
        _gpuRecorder        = ProfilerRecorder.StartNew(ProfilerCategory.Render,   "GPU Frame Time", 15);
        _drawCallsRecorder  = ProfilerRecorder.StartNew(ProfilerCategory.Render,   "Draw Calls Count", 1);
        _prevGcBytes = System.GC.GetTotalMemory(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_mainThreadRecorder.Valid) _mainThreadRecorder.Dispose();
        if (_gpuRecorder.Valid) _gpuRecorder.Dispose();
        if (_drawCallsRecorder.Valid) _drawCallsRecorder.Dispose();
    }

    void OnDisable()
    {
        if (_atmoDisabled) RestorePlanetFx();
        if (_physicsHalved) RestorePhysics();
        if (_frozenConcert.Count > 0) Unfreeze(_frozenConcert);
        if (_frozenWorld.Count > 0) Unfreeze(_frozenWorld);
        if (_frozenCameraFx.Count > 0) Unfreeze(_frozenCameraFx);
        if (_frozenAI.Count > 0) Unfreeze(_frozenAI);
    }

    void OnApplicationQuit() => OnDisable();

    void Update()
    {
        if (Input.GetKeyDown(ToggleKey))
        {
            _visible = !_visible;
            if (_canvas != null) _canvas.enabled = _visible;
        }
        if (Input.GetKeyDown(AtmoToggleKey))
        {
            if (_atmoDisabled) RestorePlanetFx(); else DisablePlanetFx();
        }
        if (Input.GetKeyDown(PhysicsToggleKey))
        {
            if (_physicsHalved) RestorePhysics(); else HalvePhysics();
        }
        if (Input.GetKeyDown(FreezeConcertKey))
        {
            if (_frozenConcert.Count > 0) Unfreeze(_frozenConcert);
            else Freeze(_frozenConcert, ConcertTypeNames);
        }
        if (Input.GetKeyDown(FreezeWorldKey))
        {
            if (_frozenWorld.Count > 0) Unfreeze(_frozenWorld);
            else Freeze(_frozenWorld, WorldTypeNames);
        }
        if (Input.GetKeyDown(FreezeCameraFxKey))
        {
            if (_frozenCameraFx.Count > 0) Unfreeze(_frozenCameraFx);
            else Freeze(_frozenCameraFx, CameraFxTypeNames);
        }
        if (Input.GetKeyDown(FreezeAIKey))
        {
            if (_frozenAI.Count > 0) Unfreeze(_frozenAI);
            else Freeze(_frozenAI, AITypeNames);
        }

        if (!_visible || _text == null) return;

        FrameTimingManager.CaptureFrameTimings();
        uint ftCount = FrameTimingManager.GetLatestTimings(1, _frameTimings);
        float gpuMsFromFT = ftCount > 0 ? (float)_frameTimings[0].gpuFrameTime : 0f;

        float mainThreadMs = _mainThreadRecorder.Valid ? (float)(_mainThreadRecorder.LastValue * 1e-6) : 0f;
        float gpuMsFromPR  = _gpuRecorder.Valid       ? (float)(_gpuRecorder.LastValue       * 1e-6) : 0f;
        float gpuMs        = gpuMsFromPR > 0f ? gpuMsFromPR : gpuMsFromFT;
        long  draws        = _drawCallsRecorder.Valid ? _drawCallsRecorder.LastValue : -1;
        // GC.GetTotalMemory is universally available (every .NET version).
        // Diffing gives a rough per-frame allocation estimate; a collection
        // between samples can make the diff negative, hence Math.Max(0, ...).
        long curGc = System.GC.GetTotalMemory(false);
        long gcBytes = System.Math.Max(0, curGc - _prevGcBytes);
        _prevGcBytes = curGc;

        float dt = Time.unscaledDeltaTime;
        float alpha = dt / Mathf.Max(SmoothingSec, dt);
        _smoothedDt = Mathf.Lerp(_smoothedDt, dt, alpha);
        if (mainThreadMs > 0f) _smoothedCpuMs = Mathf.Lerp(_smoothedCpuMs, mainThreadMs, alpha);
        if (gpuMs > 0f)        _smoothedGpuMs = Mathf.Lerp(_smoothedGpuMs, gpuMs, alpha);
        if (gcBytes >= 0)      _smoothedGcKb  = Mathf.Lerp(_smoothedGcKb, gcBytes / 1024f, alpha);

        _frameTimes.Enqueue(dt);
        _windowAccum += dt;
        while (_windowAccum > SampleWindowSec && _frameTimes.Count > 1)
            _windowAccum -= _frameTimes.Dequeue();

        float worstDt = 0f;
        foreach (var t in _frameTimes) if (t > worstDt) worstDt = t;

        // Refresh entity count once a second — FindObjectsOfType is too heavy
        // to call every frame and the number doesn't change fast.
        _objCountTimer += dt;
        if (_objCountTimer >= ObjCountIntervalSec)
        {
            _objCountTimer = 0f;
            _objCount = CountEntities();
        }

        int fps = Mathf.RoundToInt(1f / Mathf.Max(_smoothedDt, 1e-4f));
        int minFps = Mathf.RoundToInt(1f / Mathf.Max(worstDt, 1e-4f));
        int ms10 = Mathf.RoundToInt(_smoothedDt * 10000f);
        int cpuMs10 = _smoothedCpuMs > 0f ? Mathf.RoundToInt(_smoothedCpuMs * 10f) : 0;
        int gpuMs10 = _smoothedGpuMs > 0f ? Mathf.RoundToInt(_smoothedGpuMs * 10f) : 0;
        int gcKb = _smoothedGcKb > 0f ? Mathf.RoundToInt(_smoothedGcKb) : 0;
        int drawsInt = (int)System.Math.Min(draws, int.MaxValue);

        bool concertFrozen = _frozenConcert.Count > 0;
        bool worldFrozen = _frozenWorld.Count > 0;
        bool cameraFxFrozen = _frozenCameraFx.Count > 0;
        bool aiFrozen = _frozenAI.Count > 0;

        if (fps == _lastFps && minFps == _lastMin && ms10 == _lastMs10
            && cpuMs10 == _lastCpuMs10 && gpuMs10 == _lastGpuMs10 && drawsInt == _lastDraws
            && gcKb == _lastGcKb && _objCount == _lastObj
            && _atmoDisabled == _lastAtmoState && _physicsHalved == _lastPhysState
            && concertFrozen == _lastConcertState && worldFrozen == _lastWorldState)
            return;
        _lastFps = fps; _lastMin = minFps; _lastMs10 = ms10;
        _lastCpuMs10 = cpuMs10; _lastGpuMs10 = gpuMs10; _lastDraws = drawsInt;
        _lastGcKb = gcKb; _lastObj = _objCount;
        _lastAtmoState = _atmoDisabled; _lastPhysState = _physicsHalved;
        _lastConcertState = concertFrozen; _lastWorldState = worldFrozen;

        _sb.Length = 0;
        _sb.Append("FPS  ").Append(fps).Append('\n');
        _sb.Append("ms   ").Append((ms10 / 10f).ToString("0.0")).Append('\n');
        if (cpuMs10 > 0) _sb.Append("cpu  ").Append((cpuMs10 / 10f).ToString("0.0")).Append('\n');
        else             _sb.Append("cpu  --\n");
        if (gpuMs10 > 0) _sb.Append("gpu  ").Append((gpuMs10 / 10f).ToString("0.0")).Append('\n');
        else             _sb.Append("gpu  --\n");
        if (drawsInt >= 0) _sb.Append("draw ").Append(drawsInt).Append('\n');
        if (gcKb >= 0)     _sb.Append("gc   ").Append(gcKb).Append("kb\n");
        _sb.Append("obj  ").Append(_objCount).Append('\n');
        _sb.Append("min  ").Append(minFps);
        if (_atmoDisabled) _sb.Append("\n[atmo OFF]");
        if (_physicsHalved) _sb.Append("\n[phys halved]");
        if (concertFrozen) _sb.Append("\n[concert FROZEN]");
        if (worldFrozen) _sb.Append("\n[world FROZEN]");
        if (cameraFxFrozen) _sb.Append("\n[cam-fx FROZEN]");
        if (aiFrozen) _sb.Append("\n[ai FROZEN]");
        _text.SetText(_sb);

        Color c = minFps < fps - 10 ? new Color(1f, 0.55f, 0.55f, 1f)
                                    : new Color(0.7f, 1f, 0.7f, 1f);
        _text.color = c;
    }

    // ── Freeze helpers ──────────────────────────────────────────────────────
    static void Freeze(List<Behaviour> store, HashSet<string> typeNames)
    {
        store.Clear();
        var all = FindObjectsOfType<MonoBehaviour>(true);
        for (int i = 0; i < all.Length; i++)
        {
            var b = all[i];
            if (b == null) continue;
            if (!typeNames.Contains(b.GetType().Name)) continue;
            if (!b.enabled) continue;
            b.enabled = false;
            store.Add(b);
        }
    }

    static void Unfreeze(List<Behaviour> store)
    {
        for (int i = 0; i < store.Count; i++)
            if (store[i] != null) store[i].enabled = true;
        store.Clear();
    }

    static int CountEntities()
    {
        // Use the existing static `s_all` registries on each type instead of
        // FindObjectsOfType<MonoBehaviour>() — that allocated a giant array
        // of every MonoBehaviour in the scene once per second (~70 KB) just
        // to filter down to ~20 entities.
        int n = 0;
        var enemies = EnemyController.ActiveEnemies;
        if (enemies != null) n += enemies.Count;
        var aliens = SpawnedAlienNPC.AllAliens;
        if (aliens != null) n += aliens.Count;
        var members = AudienceMember.AllMembers;
        if (members != null) n += members.Count;
        return n;
    }

    // ── F4: atmosphere/ocean A/B ────────────────────────────────────────────
    void TryResolvePlanetFx()
    {
        if (_planetFx != null) return;
        var cpp = FindObjectOfType<CustomPostProcessing>();
        if (cpp == null || cpp.effects == null) return;
        foreach (var e in cpp.effects) if (e is PlanetEffects pe) { _planetFx = pe; break; }
    }

    void DisablePlanetFx()
    {
        TryResolvePlanetFx();
        if (_planetFx == null) return;
        _origDisplayAtmospheres = _planetFx.displayAtmospheres;
        _origDisplayOceans = _planetFx.displayOceans;
        _planetFx.displayAtmospheres = false;
        _planetFx.displayOceans = false;
        _atmoDisabled = true;
    }

    void RestorePlanetFx()
    {
        if (_planetFx == null) { _atmoDisabled = false; return; }
        _planetFx.displayAtmospheres = _origDisplayAtmospheres;
        _planetFx.displayOceans = _origDisplayOceans;
        _atmoDisabled = false;
    }

    // ── F5: physics tick A/B ────────────────────────────────────────────────
    void HalvePhysics()
    {
        _origFixedDeltaTime = Time.fixedDeltaTime;
        Time.fixedDeltaTime = _origFixedDeltaTime * 2f;
        _physicsHalved = true;
    }

    void RestorePhysics()
    {
        if (_origFixedDeltaTime > 0f) Time.fixedDeltaTime = _origFixedDeltaTime;
        _physicsHalved = false;
    }

    void BuildUI()
    {
        var canvasGO = new GameObject("Canvas");
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 850;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        canvasGO.AddComponent<GraphicRaycaster>().enabled = false;

        var bgGO = new GameObject("BG");
        bgGO.transform.SetParent(canvasGO.transform, false);
        var bg = bgGO.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);
        bg.raycastTarget = false;
        var bgRT = bg.rectTransform;
        bgRT.anchorMin = new Vector2(0, 1);
        bgRT.anchorMax = new Vector2(0, 1);
        bgRT.pivot = new Vector2(0, 1);
        bgRT.anchoredPosition = new Vector2(8, -8);
        bgRT.sizeDelta = new Vector2(190, 310);

        var txtGO = new GameObject("Text");
        txtGO.transform.SetParent(bgGO.transform, false);
        _text = txtGO.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_text);
        _text.alignment = TextAlignmentOptions.TopLeft;
        _text.fontSize = 16;
        _text.color = new Color(0.7f, 1f, 0.7f, 1f);
        _text.raycastTarget = false;
        _text.enableWordWrapping = false;
        _text.richText = false;
        _text.SetText("FPS  --\nms   --\ncpu  --\ngpu  --\ndraw --\ngc   --\nobj  --\nmin  --");
        var txtRT = _text.rectTransform;
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = new Vector2(8, 4);
        txtRT.offsetMax = new Vector2(-6, -4);
    }
}
