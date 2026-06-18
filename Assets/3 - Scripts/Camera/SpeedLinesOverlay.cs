using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Motion-streak overlay. Each streak is a thin sprite with an asymmetric
/// gradient (sharp leading edge, long fade toward the trailing end) that
/// emanates outward from a perspective-projected vanishing point. The VP
/// is the on-screen projection of the player's velocity vector, so:
///
///   - Moving forward while looking forward → VP at center → radial pattern.
///   - Moving forward while looking 90° to the side → velocity is
///     perpendicular to camera forward → VP off-screen at the edge in the
///     direction the player is actually heading → streaks flow across the
///     view from that side.
///   - Up-thrust while looking level → VP near the top → streaks stream down.
///
/// Per-streak randomization (width, length, brightness, angle jitter) keeps
/// the pattern from reading as a perfect geometric radial. A subtle blue
/// tint and asymmetric fade give it an atmospheric "moving-through-air"
/// look rather than the 90s-anime "warp speed" look.
///
/// Triggered purely by speed (no input gate) above ~8 m/s (player) or
/// ~12 m/s (ship). Saturates at 100 m/s.
/// </summary>
public class SpeedLinesOverlay : MonoBehaviour
{
    // ── Tuning ──────────────────────────────────────────────────────
    // 60+ very thin, very faint streaks. No single streak is visually
    // prominent — density creates the "moving through air" feel rather
    // than a few bold geometric streaks (which always read as anime).
    const int LineCount = 64;
    const float SpawnRadius = 20f;
    const float MinSpeed = 600f;          // wider variance gives "depth"
    const float MaxSpeed = 2800f;
    const float MinLength = 24f;          // short — particle-like
    const float MaxLength = 110f;
    const float MinWidth = 0.45f;         // hair-thin
    const float MaxWidth = 1.4f;
    const float MinBrightness = 0.18f;    // each streak almost invisible
    const float MaxBrightness = 0.55f;
    const float AngleJitterDeg = 9f;      // more random — less radial-perfect

    static readonly Color StreakTint = new Color(0.88f, 0.92f, 0.96f, 1f);

    const float ShipThreshold = 12f;
    const float ShipFullAt = 100f;
    const float JetpackThreshold = 8f;
    const float JetpackFullAt = 100f;

    const float VpMaxX = 1200f;
    const float VpMaxY = 700f;
    const float HalfDiagonal = 1101.5f;

    class Streak
    {
        public float angleRad;       // base direction from VP
        public float angleJitterDeg; // per-streak deflection from pure radial
        public float radius;
        public float speed;
        public float length;
        public float width;
        public float brightness;
        public float activationThreshold; // visible only when _intensity > this
        public RectTransform rt;
        public Image img;
    }

    Streak[] _streaks;
    Ship _ship;
    PlayerController _player;
    float _intensity;
    Vector2 _vp;

    void Awake()
    {
        BuildCanvas();
        BuildStreaks();
    }

    Canvas _canvas;

    void BuildCanvas()
    {
        // ScreenSpaceCamera (not Overlay) so the streaks render INSIDE the
        // 3D depth pipeline — opaque cockpit geometry (the ship's hull
        // visible to either side of the windshield) writes depth at a near
        // distance, and the streak quads at planeDistance get depth-tested
        // away there. The window mesh is transparent so it doesn't write
        // depth — streaks remain visible THROUGH the window. End result:
        // speed lines only show in the "out-the-window" portion of the
        // pilot view, never overlaid on cockpit walls.
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceCamera;
        _canvas.planeDistance = 5f;  // close enough that cockpit (~1–3 m) occludes it
        _canvas.sortingOrder = 805;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    void EnsureCameraBinding()
    {
        if (_canvas == null) return;
        if (_canvas.worldCamera != null && _canvas.worldCamera.isActiveAndEnabled) return;
        var mgr = CameraEffectsManager.Instance;
        Camera cam = mgr != null ? mgr.PlayerCamera : Camera.main;
        if (cam == null) cam = FindObjectOfType<Camera>();
        _canvas.worldCamera = cam;
    }

    void BuildStreaks()
    {
        _streaks = new Streak[LineCount];
        var sprite = GetStreakSprite();
        for (int i = 0; i < LineCount; i++)
        {
            var rt = new GameObject("Streak" + i, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(transform, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            // Pivot at left edge so the streak extends outward from the
            // anchored point. Sprite gradient is sharp at the right edge
            // (away from VP, the "head") and fades to 0 at the left edge
            // (toward VP, the "tail").
            rt.pivot = new Vector2(0f, 0.5f);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.color = new Color(StreakTint.r, StreakTint.g, StreakTint.b, 0f);
            img.raycastTarget = false;

            var s = new Streak { rt = rt, img = img };
            // Activation threshold: spread linearly across [0, 0.95) so as
            // intensity rises from 0→1, streaks light up one by one. Low
            // intensity = handful of streaks visible (subtle hint of motion),
            // high intensity = full density.
            s.activationThreshold = (float)i / LineCount * 0.95f;
            Respawn(s);
            // Scatter starting radii so the first frame isn't a stack of
            // lines all coming out of the center at once.
            s.radius = Random.Range(SpawnRadius, HalfDiagonal);
            _streaks[i] = s;
        }
    }

    void Respawn(Streak s)
    {
        s.angleRad = Random.Range(0f, Mathf.PI * 2f);
        s.angleJitterDeg = Random.Range(-AngleJitterDeg, AngleJitterDeg);
        s.radius = SpawnRadius;
        s.speed = Random.Range(MinSpeed, MaxSpeed);
        s.length = Random.Range(MinLength, MaxLength);
        s.width = Random.Range(MinWidth, MaxWidth);
        s.brightness = Random.Range(MinBrightness, MaxBrightness);
        // Stays the same across respawns — set once during build. (Kept here
        // for completeness, but BuildStreaks sets it before Respawn runs.)
    }

    void LateUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled || mgr.Input == null || !mgr.Input.fxSpeedLines)
        { FadeOut(); return; }
        EnsureCameraBinding();

        float target = ComputeTargetIntensity();

        // Hand off to the space dust on atmosphere exit: full speed lines in the
        // lower atmosphere, fading from 75% of the way out to nothing in open
        // space (where the glowing dust conveys motion instead).
        var sdf = SpaceDustField.Instance;
        if (sdf != null)
        {
            Camera fadeCam = mgr.PlayerCamera != null ? mgr.PlayerCamera
                           : (_canvas != null ? _canvas.worldCamera : null);
            if (fadeCam != null) target *= sdf.InAtmosphereFactor(fadeCam.transform.position);
        }

        _intensity = Mathf.MoveTowards(_intensity, target, Time.unscaledDeltaTime * 2.5f);

        Vector2 vpTarget = ComputeVanishingPoint(mgr);
        _vp = Vector2.Lerp(_vp, vpTarget, Time.unscaledDeltaTime * 4f);

        if (_intensity <= 0.001f) { FadeOut(); return; }

        float dt = Time.unscaledDeltaTime;

        for (int i = 0; i < LineCount; i++)
        {
            var s = _streaks[i];

            s.radius += s.speed * _intensity * dt;
            if (s.radius > HalfDiagonal + s.length) Respawn(s);

            // Position emanates from VP along the streak's angle (with jitter
            // applied to the visual orientation only — the angle of motion
            // stays pure radial so streaks travel away from VP cleanly).
            float cos = Mathf.Cos(s.angleRad);
            float sin = Mathf.Sin(s.angleRad);
            s.rt.anchoredPosition = _vp + new Vector2(cos * s.radius, sin * s.radius);
            s.rt.localEulerAngles = new Vector3(0f, 0f,
                s.angleRad * Mathf.Rad2Deg + s.angleJitterDeg);

            // Length stretches with distance (perspective foreshortening fake).
            float radiusT = Mathf.Clamp01(s.radius / HalfDiagonal);
            float currentLen = Mathf.Lerp(s.length * 0.35f, s.length * 1.6f, radiusT);
            s.rt.sizeDelta = new Vector2(currentLen, s.width);

            // Activation gate — soft ramp around this streak's threshold so
            // streaks fade in/out at their threshold instead of popping.
            float activate = Mathf.SmoothStep(0f, 1f,
                (_intensity - s.activationThreshold) * 8f);
            if (activate <= 0.001f) { s.img.color = new Color(0, 0, 0, 0); continue; }

            // Position-based fade in/out so streaks don't pop in/out at VP
            // or at the screen rim.
            float fadeIn  = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(radiusT * 5f));
            float fadeOut = Mathf.SmoothStep(1f, 0.3f, Mathf.Clamp01((radiusT - 0.85f) / 0.15f));
            float alpha = fadeIn * fadeOut * activate * s.brightness;
            s.img.color = new Color(StreakTint.r, StreakTint.g, StreakTint.b, alpha);
        }
    }

    void FadeOut()
    {
        if (_streaks == null) return;
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < _streaks.Length; i++)
        {
            var c = _streaks[i].img.color;
            if (c.a > 0f) { c.a = Mathf.MoveTowards(c.a, 0f, dt * 2.5f); _streaks[i].img.color = c; }
        }
    }

    float ComputeTargetIntensity()
    {
        // Only consider the CURRENTLY piloted ship — without this, after the
        // player exits, an orbiting abandoned ship keeps tripping speed
        // lines because FindObjectOfType returns the first ship found.
        // Use the cached static rather than FindPilotedShip — when the player
        // is on foot and the cache is null, the !IsPiloted branch was firing
        // FindObjectsOfType every frame.
        if (_ship == null || !_ship.IsPiloted) _ship = Ship.PilotedInstance;
        float shipSpeed = _ship != null ? _ship.RelativeVelocity.magnitude : 0f;
        float shipT = Mathf.Clamp01((shipSpeed - ShipThreshold) / (ShipFullAt - ShipThreshold));

        if (_player == null) _player = FindObjectOfType<PlayerController>(true);
        float playerT = 0f;
        if (_player != null && _player.isActiveAndEnabled && !_player.IsOnGround)
        {
            float relSpeed = _player.RelativeVelocity.magnitude;
            playerT = Mathf.Clamp01((relSpeed - JetpackThreshold) / (JetpackFullAt - JetpackThreshold));
        }

        return Mathf.Max(shipT, playerT);
    }

    Vector2 ComputeVanishingPoint(CameraEffectsManager mgr)
    {
        var cam = mgr.PlayerCamera;
        if (cam == null) return Vector2.zero;

        Vector3 worldVel = Vector3.zero;
        if (_player != null && _player.isActiveAndEnabled && !_player.IsOnGround)
            worldVel = _player.RelativeVelocity;
        else if (_ship != null)
        {
            // Planet-relative velocity here too so the vanishing point points
            // along travel direction, not along the orbital path of the planet.
            worldVel = _ship.RelativeVelocity;
        }
        if (worldVel.sqrMagnitude < 1f) return Vector2.zero;

        Vector3 vCam = cam.transform.InverseTransformDirection(worldVel);

        float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
        float focalLen = 540f / Mathf.Tan(fovRad * 0.5f);

        Vector2 vp;
        if (vCam.z > 0.5f)
        {
            vp = new Vector2(vCam.x / vCam.z, vCam.y / vCam.z) * focalLen;
        }
        else
        {
            Vector2 lateral = new Vector2(vCam.x, vCam.y);
            vp = lateral.sqrMagnitude < 0.001f
                ? Vector2.zero
                : lateral.normalized * 2000f;
        }

        vp.x = Mathf.Clamp(vp.x, -VpMaxX, VpMaxX);
        vp.y = Mathf.Clamp(vp.y, -VpMaxY, VpMaxY);
        return vp;
    }

    // ── Procedural asymmetric streak sprite ────────────────────────
    // Long horizontal gradient. The LEFT edge (toward VP, the "trailing
    // tail") fades to 0 over the first ~70%. The RIGHT edge (away from VP,
    // the "leading head") is near-opaque with a small fade only at the
    // very tip. This asymmetry is what makes the streak read as a true
    // motion-blur trail rather than a symmetric line.
    static Sprite _streakSprite;
    static Sprite GetStreakSprite()
    {
        if (_streakSprite != null) return _streakSprite;
        const int w = 128, h = 4;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[w * h];
        for (int x = 0; x < w; x++)
        {
            float t = (float)x / (w - 1);
            // Tail (t→0): cubic fade to 0. Trail-tip end barely visible.
            // Head (t→1): ramp up to peak around t=0.92, then small fade-off
            // at the very tip. Peak is at ~95% of the rect.
            float tail = Mathf.Pow(t, 1.6f);
            float headFade = 1f - Mathf.SmoothStep(0.92f, 1f, t) * 0.55f;
            float a = tail * headFade;
            for (int y = 0; y < h; y++)
                pixels[y * w + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        _streakSprite = Sprite.Create(tex, new Rect(0, 0, w, h),
            new Vector2(0.5f, 0.5f), 100f);
        return _streakSprite;
    }
}
