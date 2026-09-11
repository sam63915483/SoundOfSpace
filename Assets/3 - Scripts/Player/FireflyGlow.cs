using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The firefly status effect (Sam, 2026-09-11): eat a firefly and "the
/// astronaut's entire body glows for 1 minute". Two halves:
///
///   • <b>The body itself glows.</b> Each of the astronaut's renderers gets a
///     GLOW SHELL — a clone with the same mesh and the same bones (the trick
///     <see cref="PlayerShadowProxy"/> uses for the shadow) drawn with
///     <c>SoundOfSpace/FireflyGlowShell</c>: additive, unlit, fresnel-rimmed,
///     HDR so the bloom catches it. It breathes gently, fades in over half a
///     second and out over the last three, so the end of the minute is never
///     a pop. The visor slot stays dark so the faceplate still reads.
///
///     Why not emission: the astronaut's materials are EMBEDDED in
///     Astronaut.fbx (the Suit.mat on disk is not what the model uses), and an
///     embedded material cannot have the Standard shader's _EMISSION keyword
///     switched on — so an _EmissionColor written into its property block
///     draws nothing at all. That was the first version, and it glowed for
///     nobody (Sam: "make it so the actual astronaut body glows, not just have
///     a light inside them"). The shell depends on no material but its own.
///
///   • <b>You become a lantern.</b> A point light rides on the player (grass
///     marker at torch parity) so the ground and blades around you light up.
///     Its culling mask leaves the suit's own layer out — a light at your own
///     centre lighting your arms from inside reads wrong.
///
/// ONE slot, like the cat perk: eating another while glowing sets the timer
/// back to a full minute. Never stacks, never adds. Not saved (a minute is not
/// worth the apply-order risk), cleared in NewGameReset so a DontDestroyOnLoad
/// singleton cannot carry it into a new game.
///
/// Auto-singleton; ALSO seeded in MainMenuController.EnsureGameplaySingletons
/// (CLAUDE.md trap #1 — the MainMenu early-return below means a build would
/// otherwise never create it).
/// </summary>
public class FireflyGlow : MonoBehaviour
{
    public static FireflyGlow Instance { get; private set; }

    public const float DurationSeconds = 60f;

    public float SecondsLeft { get; private set; }
    public float TotalSeconds { get; private set; }
    public bool IsActive => SecondsLeft > 0f;

    /// Fires when the glow starts, is refreshed, or runs out. The HUD chip listens.
    public event Action OnChanged;

    [Header("Body glow")]
    [Tooltip("Colour of the shell over the suit. The shell material's own _Intensity sets how hard it glows (HDR — the Bloom pass catches anything over white).")]
    public Color glowColor = new Color(1f, 0.68f, 0.22f, 1f);
    [Range(0f, 1f)]
    [Tooltip("Bottom of the breathing cycle as a fraction of full. 1 = a steady glow.")]
    public float breatheMin = 0.72f;
    [Tooltip("Breaths per second.")]
    public float breatheHz = 0.45f;
    [Tooltip("Seconds for the glow to come up after eating.")]
    public float fadeInSeconds = 0.6f;
    [Tooltip("Seconds over which the glow dies at the end of the minute.")]
    public float fadeOutSeconds = 3f;
    [Tooltip("Shell strength on the darker suit parts (the 'Suit Dark' slots: joints, gloves). 1 = same as the suit.")]
    [Range(0f, 1f)] public float darkPartsStrength = 0.7f;

    [Header("Light on the player")]
    public float lightIntensity = 1.4f;
    public float lightRange = 9f;
    [Tooltip("Where the light sits in the player's own frame. The camera is at (0, 0.67, 0.28); the feet at -0.97.")]
    public Vector3 lightLocalOffset = new Vector3(0f, 0.25f, 0.05f);
    [Tooltip("Grass response. 0.5 = the lantern/torch value = same brightness as the ground.")]
    public float grassStrength = 0.5f;

    // ── runtime ─────────────────────────────────────────────────────────
    Transform _player;
    Light _light;
    float _elapsed;
    float _nextPlayerSearch;
    float _nextShellCheck;
    bool _visualsOn;

    // the shells: one clone per body renderer, plus the per-slot strengths
    readonly List<Renderer> _shells = new List<Renderer>();
    readonly List<float[]> _shellStrengths = new List<float[]>();
    readonly List<Renderer> _shellSources = new List<Renderer>();
    Material _shellMat;
    MaterialPropertyBlock _block;
    static readonly int LevelId    = Shader.PropertyToID("_Level");
    static readonly int StrengthId = Shader.PropertyToID("_Strength");
    static readonly int ColorId    = Shader.PropertyToID("_GlowColor");
    const string ShellPrefix = "~glow ";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        // Trap #1: also seeded in MainMenuController.EnsureGameplaySingletons.
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("[FireflyGlow]");
        DontDestroyOnLoad(go);
        go.AddComponent<FireflyGlow>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The player rig, its renderers, the shells and the light child all
        // died with the old scene. Keep the timer (a reload mid-glow simply
        // carries on), drop every cached reference.
        _player = null;
        _light = null;
        _shells.Clear();
        _shellStrengths.Clear();
        _shellSources.Clear();
        _visualsOn = false;
        _nextPlayerSearch = 0f;
        _nextShellCheck = 0f;
    }

    // ── granting ────────────────────────────────────────────────────────

    /// Start (or restart) the minute. Always a full minute, never topped up.
    public void Grant()
    {
        bool wasActive = IsActive;
        TotalSeconds = DurationSeconds;
        SecondsLeft = DurationSeconds;
        // A refresh mid-glow should not re-run the fade-in from black.
        if (!wasActive) _elapsed = 0f;
        OnChanged?.Invoke();
    }

    public void Clear()
    {
        bool had = IsActive;
        SecondsLeft = 0f;
        TotalSeconds = 0f;
        _elapsed = 0f;
        SetVisuals(0f);
        if (had) OnChanged?.Invoke();
    }

    // ── per frame ───────────────────────────────────────────────────────

    void Update()
    {
        if (!IsActive)
        {
            if (_visualsOn) SetVisuals(0f);
            return;
        }

        // Real seconds. Pause here is an input block, not a timeScale freeze
        // (co-op cannot stop time), so deltaTime is the honest clock — the
        // same call CatPerkManager makes.
        float dt = Time.deltaTime;
        _elapsed += dt;
        SecondsLeft -= dt;
        if (SecondsLeft <= 0f)
        {
            SecondsLeft = 0f;
            TotalSeconds = 0f;
            SetVisuals(0f);
            OnChanged?.Invoke();
            return;
        }

        float envelope = Mathf.Min(1f, _elapsed / Mathf.Max(0.01f, fadeInSeconds))
                       * Mathf.Min(1f, SecondsLeft / Mathf.Max(0.01f, fadeOutSeconds));
        float breath = 0.5f + 0.5f * Mathf.Sin(Time.time * breatheHz * Mathf.PI * 2f);
        float level = envelope * Mathf.Lerp(breatheMin, 1f, breath);
        SetVisuals(level);
    }

    /// Push one glow level (0 = off) to the shells and the light.
    void SetVisuals(float level)
    {
        if (level <= 0f)
        {
            if (_visualsOn)
            {
                for (int i = 0; i < _shells.Count; i++)
                    if (_shells[i] != null) _shells[i].enabled = false;
                if (_light != null) _light.enabled = false;
                _visualsOn = false;
            }
            return;
        }

        if (!ResolvePlayer()) return;
        _visualsOn = true;

        // The suit model is attached to the rig a little after it spawns and
        // can be swapped, so the shells are re-checked on a slow clock while
        // the glow runs — never per frame.
        if (Time.unscaledTime >= _nextShellCheck)
        {
            _nextShellCheck = Time.unscaledTime + 2f;
            EnsureShells();
        }

        if (_block == null) _block = new MaterialPropertyBlock();
        for (int i = 0; i < _shells.Count; i++)
        {
            var r = _shells[i];
            if (r == null) continue;
            if (!r.enabled) r.enabled = true;
            var strengths = _shellStrengths[i];
            // Per-SLOT blocks (a per-material block replaces the renderer-wide
            // one, so the level has to travel in each of them).
            for (int slot = 0; slot < strengths.Length; slot++)
            {
                r.GetPropertyBlock(_block, slot);
                _block.SetFloat(LevelId, level);
                _block.SetFloat(StrengthId, strengths[slot]);
                _block.SetColor(ColorId, glowColor);
                r.SetPropertyBlock(_block, slot);
            }
        }

        EnsureLight();
        if (_light != null)
        {
            _light.enabled = true;
            _light.intensity = lightIntensity * level;
            _light.range = lightRange;
            _light.color = glowColor;
        }
    }

    bool ResolvePlayer()
    {
        if (_player != null) return true;
        if (Time.unscaledTime < _nextPlayerSearch) return false;
        _nextPlayerSearch = Time.unscaledTime + 0.5f;
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go == null) return false;
        _player = go.transform;
        _nextShellCheck = 0f;
        // A new rig (death respawn) means new renderers: the old shells are
        // gone with it, so start the list over.
        _shells.Clear();
        _shellStrengths.Clear();
        _shellSources.Clear();
        return true;
    }

    // ── the shells ──────────────────────────────────────────────────────

    /// One clone per body renderer that does not have one yet. Renderers on
    /// the PlayerReflect layer are the astronaut (the same test
    /// PlayerShadowProxy uses); everything else under the player — held
    /// items, the flashlight, the shadow proxies, our own shells — is skipped.
    void EnsureShells()
    {
        if (_player == null) return;
        if (_shellMat == null)
        {
            // A REAL asset in Resources, for the usual reason: a runtime
            // new Material(Shader.Find(...)) can be stripped from a build.
            _shellMat = Resources.Load<Material>("FireflyGlowShell");
            if (_shellMat == null)
            {
                var sh = Shader.Find("SoundOfSpace/FireflyGlowShell");
                if (sh == null) return;
                _shellMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                Debug.LogWarning("[FireflyGlow] Resources/FireflyGlowShell.mat missing — using a runtime "
                               + "material, which may draw nothing in a build. Restore the asset.");
            }
        }

        int reflect = LayerMask.NameToLayer("PlayerReflect");
        foreach (var r in _player.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (reflect >= 0 && r.gameObject.layer != reflect) continue;
            if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
            if (r.name.StartsWith(ShellPrefix) || r.name.StartsWith("~shadow ") || r.name == "GazeOutline") continue;
            if (_shellSources.Contains(r)) continue;

            Renderer made = null;
            var srcMats = r.sharedMaterials;
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                // Same mesh, same bones: the shell deforms exactly with the body.
                var go = new GameObject(ShellPrefix + r.name);
                go.transform.SetParent(r.transform, false);
                var p = go.AddComponent<SkinnedMeshRenderer>();
                p.sharedMesh = smr.sharedMesh;
                p.bones      = smr.bones;
                p.rootBone   = smr.rootBone;
                p.quality    = smr.quality;
                p.localBounds = smr.localBounds;
                p.updateWhenOffscreen = smr.updateWhenOffscreen;
                made = p;
            }
            else if (r is MeshRenderer)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var go = new GameObject(ShellPrefix + r.name);
                go.transform.SetParent(r.transform, false);   // rides the source exactly
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                made = go.AddComponent<MeshRenderer>();
            }
            if (made == null) continue;

            // Default layer, not the suit's: the reflect layer is what
            // PlayerShadowProxy proxies, and a ShadowsOnly copy of a glow is
            // a shadow drawn twice. The shell's shader has no shadow pass and
            // takes no light, so the layer changes nothing else about it.
            made.gameObject.layer = 0;
            var mats = new Material[Mathf.Max(1, srcMats.Length)];
            var strengths = new float[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i] = _shellMat;
                strengths[i] = SlotStrength(i < srcMats.Length ? srcMats[i] : null);
            }
            made.sharedMaterials = mats;
            made.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            made.receiveShadows = false;
            made.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            made.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            made.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            made.enabled = false;   // SetVisuals switches it on with a level

            _shells.Add(made);
            _shellStrengths.Add(strengths);
            _shellSources.Add(r);
        }
    }

    /// How hard each material slot glows. Matched by material NAME, the same
    /// way SuitTinter tells the suit from the visor: the visor stays dark so
    /// the faceplate still reads, the darker suit parts glow a little less.
    float SlotStrength(Material m)
    {
        if (m == null) return 1f;
        string n = m.name;
        if (n.StartsWith("Visor", StringComparison.OrdinalIgnoreCase)) return 0f;
        if (n.StartsWith("Suit Dark", StringComparison.OrdinalIgnoreCase)) return darkPartsStrength;
        return 1f;
    }

    void EnsureLight()
    {
        if (_light != null || _player == null) return;
        var go = new GameObject("FireflyGlowLight");
        go.transform.SetParent(_player, false);
        go.transform.localPosition = lightLocalOffset;
        _light = go.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.shadows = LightShadows.None;
        _light.color = glowColor;
        _light.range = lightRange;
        _light.intensity = 0f;
        // Light the world, not the suit: the suit glows through its shell, and
        // a point light at the astronaut's own centre would light the inner
        // faces of the arms from inside. The reflect layer is the suit's.
        int reflect = LayerMask.NameToLayer("PlayerReflect");
        if (reflect >= 0) _light.cullingMask = ~(1 << reflect);
        // Grass never receives real additive lights; the marker feeds this one
        // into the grass shader. GrassLightAutoMarker would mark it anyway on
        // its 3 s sweep, at its own strength — marking it here first wins.
        var marker = go.AddComponent<GrassPointLight>();
        marker.grassStrength = grassStrength;
    }
}
