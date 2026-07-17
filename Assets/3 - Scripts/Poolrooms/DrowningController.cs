using UnityEngine;
using UnityEngine.UI;

// Drowning in the flooding Poolrooms. While the player's HEAD (camera) is below the
// rising water surface, an air-out timer runs. The soundscape escalates in three
// stages — subtle bubbles → building bubbles + muffled heartbeat → a panicked
// breath-holding struggle — and at holdSeconds the player drowns (lethal damage
// through the normal ResourceManager → death/respawn flow). Surfacing at any point
// resets the timer and fades the audio back out.
//
// Submersion reuses PoolFlood's flat-surface height, so this needs no colliders and
// is fully decoupled from the spherical-ocean water model.
public class DrowningController : MonoBehaviour
{
    [Header("Head / timing")]
    [Tooltip("The player's eye/head transform. Drowning counts only while THIS is below the water surface. Leave empty to auto-find a child 'Camera' or Camera.main.")]
    public Transform head;
    [Tooltip("Seconds head-underwater before the player drowns.")]
    public float holdSeconds = 30f;
    [Tooltip("Subtle stage ends here (seconds). 0 → this end.")]
    public float stage1End = 10f;
    [Tooltip("Building stage ends / struggle begins here (seconds).")]
    public float stage2End = 25f;

    [Header("SFX (2D, played from the player)")]
    public AudioClip bubblesClip;     // stage 1+ ambient, rises through the whole descent
    public AudioClip heartbeatClip;   // fades in during the building stage
    public AudioClip struggleClip;    // panicked breath-holding, last few seconds
    public AudioClip deathClip;       // final choke/gurgle at the drown moment
    [Range(0f, 1f)] public float masterVolume = 1f;

    [Header("Breath-out screen fade")]
    [Tooltip("As breath runs out the screen darkens toward black, reaching FULLY black exactly at holdSeconds (the drown moment). Curve exponent shapes it: >1 keeps the early swim clear and darkens hard near the end.")]
    public float fadeCurve = 1.7f;
    [Tooltip("Colour the screen fades to as you drown. Black by default.")]
    public Color fadeColor = Color.black;

    AudioSource _bubbles, _heartbeat, _struggle, _death;
    float _t;
    bool _drowned;

    Image _fade;       // full-screen overlay driven toward `fadeColor`
    float _fadeAlpha;  // smoothed current alpha (avoids a pop when crossing the waterline)

    void Awake()
    {
        _bubbles   = MakeSource("DrownBubbles",   true);
        _heartbeat = MakeSource("DrownHeartbeat", true);
        _struggle  = MakeSource("DrownStruggle",  true);
        _death     = MakeSource("DrownDeath",     false);
        BuildFadeOverlay();
    }

    // A dedicated screen-space canvas + stretched black Image, on top of the HUD.
    void BuildFadeOverlay()
    {
        var canvasGO = new GameObject("DrownFadeCanvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;   // above the helmet HUD and gameplay UI
        canvasGO.AddComponent<CanvasScaler>();

        var imgGO = new GameObject("DrownFade");
        imgGO.transform.SetParent(canvasGO.transform, false);
        _fade = imgGO.AddComponent<Image>();
        _fade.raycastTarget = false;
        var rt = _fade.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        SetFadeAlpha(0f);
    }

    void SetFadeAlpha(float a)
    {
        _fadeAlpha = a;
        if (_fade != null)
        {
            var c = fadeColor; c.a = a;
            _fade.color = c;
            _fade.enabled = a > 0.001f;   // fully clear = don't draw at all
        }
    }

    // Ease the overlay toward `target`. Darkening (running out of air) is quicker
    // than the recovery fade-out when you surface and catch your breath.
    void DriveFade(float target)
    {
        float rate = (target > _fadeAlpha) ? 1.5f : 0.8f;
        SetFadeAlpha(Mathf.MoveTowards(_fadeAlpha, target, Time.deltaTime * rate));
    }

    AudioSource MakeSource(string n, bool loop)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.spatialBlend = 0f;   // the player's own drowning — 2D, in your head
        s.volume = 0f;
        return s;
    }

    Transform ResolveHead()
    {
        if (head != null) return head;
        var cam = transform.Find("Camera");
        if (cam != null) head = cam;
        else if (Camera.main != null) head = Camera.main.transform;
        return head;
    }

    void Update()
    {
        Transform h = ResolveHead();
        bool headUnder = h != null && PoolFlood.Active && h.position.y < PoolFlood.SurfaceY;

        if (!headUnder)
        {
            // Surfaced (or no flood) — reset and let the audio + screen ease back
            // out (catching your breath fades the darkness away).
            _t = 0f;
            _drowned = false;
            Ease(_bubbles,   0f, null);
            Ease(_heartbeat, 0f, 1f);
            Ease(_struggle,  0f, null);
            DriveFade(0f);
            return;
        }

        _t += Time.deltaTime;

        // Screen darkens as breath runs out — fully black exactly at holdSeconds.
        float fadeTarget = Mathf.Pow(Mathf.Clamp01(_t / holdSeconds), Mathf.Max(0.01f, fadeCurve));
        DriveFade(fadeTarget);

        // ── stage volumes ──
        float bub, beat, beatPitch, strg;
        if (_t < stage1End)                       // subtle
        {
            float k = Mathf.InverseLerp(0f, stage1End, _t);
            bub = Mathf.Lerp(0.15f, 0.4f, k);
            beat = 0f; beatPitch = 0.9f; strg = 0f;
        }
        else if (_t < stage2End)                  // building
        {
            float k = Mathf.InverseLerp(stage1End, stage2End, _t);
            bub = Mathf.Lerp(0.4f, 0.75f, k);
            beat = Mathf.Lerp(0.0f, 0.7f, k);
            beatPitch = Mathf.Lerp(0.9f, 1.25f, k);
            strg = 0f;
        }
        else                                      // struggle → drown
        {
            float k = Mathf.InverseLerp(stage2End, holdSeconds, _t);
            bub = 0.8f;
            beat = Mathf.Lerp(0.7f, 1f, k);
            beatPitch = Mathf.Lerp(1.25f, 1.6f, k);
            strg = Mathf.Lerp(0.2f, 1f, k);
        }

        Ease(_bubbles,   bub  * masterVolume, null);
        Ease(_heartbeat, beat * masterVolume, beatPitch);
        Ease(_struggle,  strg * masterVolume, null);

        if (!_drowned && _t >= holdSeconds)
            Drown();
    }

    void Drown()
    {
        _drowned = true;
        SetFadeAlpha(1f);   // guarantee a fully-black screen at the drown moment
        if (_death != null && deathClip != null)
        {
            _death.clip = deathClip;
            _death.volume = masterVolume;
            _death.Play();
        }
        // Silence the loops — the death gurgle carries the moment.
        if (_bubbles   != null) _bubbles.volume = 0f;
        if (_heartbeat != null) _heartbeat.volume = 0f;
        if (_struggle  != null) _struggle.volume = 0f;

        var rm = ResourceManager.Instance;
        if (rm != null)
        {
            // Real flow: lethal damage → OnDeath → the normal death/respawn cutscene.
            rm.TakeDamage(1000f);
        }
        else
        {
            // Direct-play testing (pressing Play on this scene, so the game's
            // DontDestroyOnLoad singletons were never seeded): just reload the scene so
            // the drown still reads as a death and resets the flood.
            Debug.LogWarning("[Drowning] No ResourceManager (direct-play) — reloading scene as a death fallback.");
            var scn = gameObject.scene;
            if (scn.IsValid())
                UnityEngine.SceneManagement.SceneManager.LoadScene(scn.name);
        }
    }

    // Lerp a looping source toward a target volume, starting/stopping it as needed.
    void Ease(AudioSource s, float target, float? pitch)
    {
        if (s == null) return;
        if (pitch.HasValue) s.pitch = pitch.Value;

        if (target > 0.001f)
        {
            if (!s.isPlaying)
            {
                AudioClip c = s == _bubbles ? bubblesClip : s == _heartbeat ? heartbeatClip : struggleClip;
                if (c == null) return;
                s.clip = c;
                s.Play();
            }
            s.volume = Mathf.MoveTowards(s.volume, target, Time.deltaTime * 1.2f);
        }
        else
        {
            s.volume = Mathf.MoveTowards(s.volume, 0f, Time.deltaTime * 2f);
            if (s.volume <= 0.001f && s.isPlaying) s.Stop();
        }
    }
}
