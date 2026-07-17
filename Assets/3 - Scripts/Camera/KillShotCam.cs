using UnityEngine;

/// <summary>
/// Kill-shot bullet cinematic, v2 (clean killcam view). On a killing pistol shot the moment
/// is handed here: slow-mo starts, the HUD/helmet overlays are force-hidden and the pistol
/// viewmodel disappears, and the PLAYER'S OWN camera (kept — a fresh camera would lose the
/// ocean/atmosphere image effects that live on it, CLAUDE.md trap #2) flies after the bullet
/// with a COD-style vignette + scanline treatment. Right BEFORE impact it hard-cuts back to
/// the astronaut POV (HUD/gun restored instantly) so the player watches the bullet land and
/// the blood fly in slow motion; the target's real hit-collider wireframes + targeting
/// bracket stay painted through the aftermath and fade out as the slow-mo releases.
///
/// The player body/rig never moves — only the camera transform is overridden, and the
/// "cut back" is simply ceasing the override (CameraTransformFX rewrites the natural pose
/// every frame). Anchors are transform-relative (muzzle→player rig, hit→enemy root) so
/// orbital motion / floating-origin rebases can't bend the path.
/// </summary>
// DefaultExecutionOrder(250): after CameraTransformFX(100)/free-cam(200) write the natural
// pose; before dust/flare(300) read the final camera.
[DefaultExecutionOrder(250)]
public class KillShotCam : MonoBehaviour
{
    public static KillShotCam Instance { get; private set; }

    const float FlightSeconds = 1.15f;   // muzzle → body (unscaled)
    const float CutFraction   = 0.86f;   // hard-cut back to player POV at this flight fraction
    const float FadeSeconds   = 0.35f;   // viz fade once slow-mo has released
    // Shutter transition: two black bars slide in (top+bottom) to close over the camera
    // switch — the killcam "ending" — then open to reveal the helmet POV. Kept short.
    const float BarCloseSeconds = 0.12f;
    const float BarOpenSeconds  = 0.15f;
    const float CamBack = 1.7f, CamSide = 0.55f, CamUp = 0.35f;
    static readonly Color VizColor    = new Color(0.36f, 0.78f, 1f, 0.95f);   // suit-camera cyan
    static readonly Color BulletColor = new Color(1f, 0.96f, 0.80f, 1f);

    Camera _cam;
    EnemyController _enemy;
    Transform _enemyT;
    Vector3 _targetLocal;
    Transform _muzzleRef;
    Vector3 _muzzleLocal, _muzzleWorldFallback;
    System.Action _applyKill;
    PistolController _pistol;
    Transform _rig;
    float _hbScale = 1f;

    int _phase = -1;          // -1 idle, 0 fly (killcam view), 1 aftermath (player POV, viz fading)
    float _t;
    bool _cutBack;            // true once we've returned the view to the player
    float _vizAlpha = 1f;
    float _fadeT;
    Vector3 _bulletPos, _bulletDir;
    bool _impactDone;
    Material _glMat;

    // Shutter bars live on their own ScreenSpaceOverlay canvas at a very high sorting
    // order — GL draws happen in the 3D pass and can NEVER cover overlay canvases (the
    // helmet art), which is why the GL version opened up BEHIND the helmet.
    Canvas _barCanvas;
    RectTransform _barTop, _barBottom;

    public static bool TryPlay(Vector3 muzzleWorld, Vector3 hitWorld, EnemyController enemy,
                               System.Action applyKill, PistolController pistol = null)
    {
        if (enemy == null || applyKill == null) return false;
        if (Ship.PilotedInstance != null) return false;
        if (Instance != null && Instance._phase >= 0) return false;
        var mgr = CameraEffectsManager.Instance;   // no slow-mo setting → no bullet cam
        if (mgr != null && mgr.Input != null && !mgr.Input.fxSlowmoOnKill) return false;

        if (Instance == null)
        {
            var go = new GameObject("KillShotCam");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<KillShotCam>();
        }
        return Instance.Begin(muzzleWorld, hitWorld, enemy, applyKill, pistol);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    bool Begin(Vector3 muzzleWorld, Vector3 hitWorld, EnemyController enemy,
               System.Action applyKill, PistolController pistol)
    {
        _cam = Camera.main;
        if (_cam == null) return false;

        _enemy = enemy;
        _enemyT = enemy.transform;
        _targetLocal = _enemyT.InverseTransformPoint(hitWorld);
        var player = FindObjectOfType<PlayerController>();
        _muzzleRef = player != null ? player.transform : null;
        if (_muzzleRef != null) _muzzleLocal = _muzzleRef.InverseTransformPoint(muzzleWorld);
        else _muzzleWorldFallback = muzzleWorld;
        _applyKill = applyKill;
        _pistol = pistol;

        _rig = null;
        for (int i = 0; i < _enemyT.childCount; i++)
        {
            var c = _enemyT.GetChild(i);
            if (c.name.StartsWith("Visual_")) { _rig = c; break; }
        }
        _hbScale = enemy.HitboxRadiusScale;

        _phase = 0; _t = 0f; _cutBack = false; _impactDone = false;
        _vizAlpha = 0f; _fadeT = 0f;
        _bulletPos = muzzleWorld;

        // Clean killcam view: hide every registered HUD/helmet canvas + the gun viewmodel.
        HudVisibility.SetForceHidden(true);
        _pistol?.SetViewmodelVisible(false);

        // Slow the world for the whole flight; the kill at impact routes through
        // KillstreakManager → SlowmoOnKill, which extends/owns the timescale after us.
        Time.timeScale = 0.15f;
        return true;
    }

    Vector3 MuzzleWorld => _muzzleRef != null ? _muzzleRef.TransformPoint(_muzzleLocal) : _muzzleWorldFallback;
    Vector3 TargetWorld => _enemyT != null ? _enemyT.TransformPoint(_targetLocal) : _bulletPos;

    void CutBackToPlayer()
    {
        if (_cutBack) return;
        _cutBack = true;
        // Just stop overriding — CameraTransformFX rewrites the natural pose every frame,
        // so the next frame IS the astronaut POV. Restore the cockpit dressing instantly.
        HudVisibility.SetForceHidden(false);
        _pistol?.SetViewmodelVisible(true);
    }

    void LandImpact()
    {
        if (_impactDone) return;
        _impactDone = true;
        _applyKill?.Invoke();   // blood + damage + ragdoll + streak/slow-mo event
        _applyKill = null;
    }

    void Finish()
    {
        CutBackToPlayer();      // never leave the HUD hidden
        LandImpact();           // never lose the kill
        _phase = -1;
    }

    void LateUpdate()
    {
        if (_phase < 0)
        {
            if (_barCanvas != null && _barCanvas.enabled) _barCanvas.enabled = false;
            return;
        }
        if (_cam == null) { Finish(); return; }
        float dt = Time.unscaledDeltaTime;
        _t += dt;
        UpdateShutterBars();

        if (_phase == 0)
        {
            if (_enemyT == null) { Finish(); return; }
            float u = Mathf.Clamp01(_t / FlightSeconds);
            float eased = 1f - (1f - u) * (1f - u);
            Vector3 mw = MuzzleWorld, tw = TargetWorld;
            Vector3 path = tw - mw;
            _bulletDir = path.sqrMagnitude > 0.0001f ? path.normalized : _cam.transform.forward;
            _bulletPos = Vector3.Lerp(mw, tw, eased);
            _vizAlpha = Mathf.Clamp01(_t / 0.3f);   // bracket/capsules frame-in

            if (!_cutBack)
            {
                Vector3 naturalPos = _cam.transform.position;
                Quaternion naturalRot = _cam.transform.rotation;
                Vector3 upRef = naturalRot * Vector3.up;
                Vector3 side = Vector3.Cross(_bulletDir, upRef);
                if (side.sqrMagnitude < 0.001f) side = Vector3.Cross(_bulletDir, Vector3.right);
                side.Normalize();
                Vector3 wantPos = _bulletPos - _bulletDir * CamBack + side * CamSide + upRef * CamUp;
                Quaternion wantRot = Quaternion.LookRotation((tw - wantPos).normalized, upRef);
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / 0.25f));
                _cam.transform.SetPositionAndRotation(
                    Vector3.Lerp(naturalPos, wantPos, blend),
                    Quaternion.Slerp(naturalRot, wantRot, blend));
            }

            // The camera switch happens BEHIND the closed shutter bars: they finish
            // closing exactly at the cut time, the view swaps, then they slide open
            // on the astronaut POV (see BarCover01).
            if (_t >= FlightSeconds * CutFraction) CutBackToPlayer();
            if (u >= 1f)
            {
                LandImpact();
                _phase = 1; _t = 0f;
            }
        }
        else
        {
            // Aftermath: player POV, slow-mo still running (SlowmoOnKill owns it now).
            // Keep the collider viz + bracket painted on the corpse; once the slow-mo
            // releases, fade them out and finish.
            if (Time.timeScale >= 0.95f)
            {
                _fadeT += dt;
                _vizAlpha = 1f - Mathf.Clamp01(_fadeT / FadeSeconds);
                if (_vizAlpha <= 0f) _phase = -1;
            }
            else _vizAlpha = 1f;
        }
    }

    // ── Suit targeting viz: capsule wireframes, bracket, bullet, killcam vignette+lines ──
    void OnRenderObject()
    {
        if (_phase < 0) return;
        if (Camera.current != _cam) return;
        EnsureGlMat();
        _glMat.SetPass(0);

        if (_rig != null)
        {
            GL.PushMatrix();
            GL.Begin(GL.LINES);
            GL.Color(new Color(VizColor.r, VizColor.g, VizColor.b, 0.85f * _vizAlpha));
            foreach (var bc in EnemyRagdollBuilder.GetBoneCapsules(_rig, _hbScale))
            {
                if (bc.bone == null) continue;
                float wr = bc.radius * bc.bone.lossyScale.x;
                Vector3 a = bc.bone.position;
                Vector3 b = bc.tip != null ? bc.tip.position : a;
                Vector3 axis = (b - a).sqrMagnitude > 0.0001f ? (b - a).normalized : Vector3.up;
                DrawWireRing(a, axis, wr);
                if (bc.tip != null)
                {
                    DrawWireRing(b, axis, wr);
                    Vector3 s1 = Vector3.Cross(axis, Vector3.up);
                    if (s1.sqrMagnitude < 0.001f) s1 = Vector3.Cross(axis, Vector3.right);
                    s1.Normalize();
                    Vector3 s2 = Vector3.Cross(axis, s1);
                    GL.Vertex(a + s1 * wr); GL.Vertex(b + s1 * wr);
                    GL.Vertex(a - s1 * wr); GL.Vertex(b - s1 * wr);
                    GL.Vertex(a + s2 * wr); GL.Vertex(b + s2 * wr);
                    GL.Vertex(a - s2 * wr); GL.Vertex(b - s2 * wr);
                }
            }
            GL.End();
            GL.PopMatrix();
        }

        if (_phase == 0 && !_impactDone)
        {
            Vector3 camFwd = _cam.transform.forward;
            Vector3 right = Vector3.Cross(camFwd, _bulletDir);
            if (right.sqrMagnitude < 0.001f) right = _cam.transform.up;
            right = right.normalized * 0.05f;
            Vector3 tail = _bulletPos - _bulletDir * 0.45f;
            GL.PushMatrix();
            GL.Begin(GL.QUADS);
            GL.Color(BulletColor);
            GL.Vertex(tail - right); GL.Vertex(tail + right);
            GL.Vertex(_bulletPos + right); GL.Vertex(_bulletPos - right);
            GL.End();
            GL.PopMatrix();
        }

        if (_rig != null) DrawBracket();
        if (_phase == 0 && !_cutBack) DrawKillcamOverlay();   // vignette + lines, killcam view only
    }

    // 0 = bars fully open, 1 = screen fully covered. Closes over BarCloseSeconds so the
    // bars meet exactly at the cut time, then opens over BarOpenSeconds on the helmet POV.
    float BarCover01()
    {
        if (_phase != 0) return 0f;
        float cutTime = FlightSeconds * CutFraction;
        if (_t < cutTime - BarCloseSeconds) return 0f;
        if (_t < cutTime) return Mathf.SmoothStep(0f, 1f, (_t - (cutTime - BarCloseSeconds)) / BarCloseSeconds);
        return 1f - Mathf.SmoothStep(0f, 1f, (_t - cutTime) / BarOpenSeconds);
    }

    void EnsureBarCanvas()
    {
        if (_barCanvas != null) return;
        var go = new GameObject("KillShotShutter");
        go.transform.SetParent(transform, false);
        _barCanvas = go.AddComponent<Canvas>();
        _barCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _barCanvas.sortingOrder = 2000;   // above the helmet art / every HUD canvas
        _barBottom = MakeBar(go.transform, bottom: true);
        _barTop = MakeBar(go.transform, bottom: false);
        _barCanvas.enabled = false;
    }

    static RectTransform MakeBar(Transform parent, bool bottom)
    {
        var go = new GameObject(bottom ? "BarBottom" : "BarTop", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = bottom ? new Vector2(0f, 0f) : new Vector2(0f, 1f);
        rt.anchorMax = bottom ? new Vector2(1f, 0f) : new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, bottom ? 0f : 1f);
        rt.sizeDelta = new Vector2(0f, 0f);
        var img = go.AddComponent<UnityEngine.UI.Image>();
        img.color = Color.black;
        img.raycastTarget = false;
        return rt;
    }

    void UpdateShutterBars()
    {
        float cover = BarCover01();
        if (cover <= 0f)
        {
            if (_barCanvas != null && _barCanvas.enabled) _barCanvas.enabled = false;
            return;
        }
        EnsureBarCanvas();
        _barCanvas.enabled = true;
        float barH = cover * Screen.height * 0.5f;   // no scaler → canvas units = pixels
        _barBottom.sizeDelta = new Vector2(0f, barH);
        _barTop.sizeDelta = new Vector2(0f, barH);
    }

    void DrawWireRing(Vector3 center, Vector3 axis, float r)
    {
        Vector3 s1 = Vector3.Cross(axis, Vector3.up);
        if (s1.sqrMagnitude < 0.001f) s1 = Vector3.Cross(axis, Vector3.right);
        s1.Normalize();
        Vector3 s2 = Vector3.Cross(axis, s1);
        const int Seg = 10;
        Vector3 prev = center + s1 * r;
        for (int i = 1; i <= Seg; i++)
        {
            float ang = i / (float)Seg * Mathf.PI * 2f;
            Vector3 p = center + (s1 * Mathf.Cos(ang) + s2 * Mathf.Sin(ang)) * r;
            GL.Vertex(prev); GL.Vertex(p);
            prev = p;
        }
    }

    void DrawBracket()
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        bool any = false;
        foreach (var bc in EnemyRagdollBuilder.GetBoneCapsules(_rig, _hbScale))
        {
            if (bc.bone == null) continue;
            AccumulateScreen(bc.bone.position, ref minX, ref minY, ref maxX, ref maxY, ref any);
            if (bc.tip != null) AccumulateScreen(bc.tip.position, ref minX, ref minY, ref maxX, ref maxY, ref any);
        }
        if (!any) return;
        const float pad = 26f;
        minX -= pad; minY -= pad; maxX += pad; maxY += pad;

        float closeIn = _phase == 0 ? Mathf.Clamp01(_t / 0.3f) : 1f;
        float over = Mathf.Lerp(0.6f, 0f, closeIn);
        float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
        float hw = (maxX - minX) * 0.5f * (1f + over), hh = (maxY - minY) * 0.5f * (1f + over);
        minX = cx - hw; maxX = cx + hw; minY = cy - hh; maxY = cy + hh;

        float arm = Mathf.Min(34f, Mathf.Min(hw, hh) * 0.5f);
        GL.PushMatrix();
        GL.LoadPixelMatrix();
        GL.Begin(GL.LINES);
        GL.Color(new Color(VizColor.r, VizColor.g, VizColor.b, 0.95f * _vizAlpha));
        GL.Vertex3(minX, minY, 0); GL.Vertex3(minX + arm, minY, 0);
        GL.Vertex3(minX, minY, 0); GL.Vertex3(minX, minY + arm, 0);
        GL.Vertex3(maxX, minY, 0); GL.Vertex3(maxX - arm, minY, 0);
        GL.Vertex3(maxX, minY, 0); GL.Vertex3(maxX, minY + arm, 0);
        GL.Vertex3(minX, maxY, 0); GL.Vertex3(minX + arm, maxY, 0);
        GL.Vertex3(minX, maxY, 0); GL.Vertex3(minX, maxY - arm, 0);
        GL.Vertex3(maxX, maxY, 0); GL.Vertex3(maxX - arm, maxY, 0);
        GL.Vertex3(maxX, maxY, 0); GL.Vertex3(maxX, maxY - arm, 0);
        GL.End();
        GL.PopMatrix();
    }

    // COD-style killcam treatment: soft dark vignette (edge quads with per-vertex alpha)
    // + faint interlace scanlines + one brighter roll bar. Screen-space, killcam view only.
    void DrawKillcamOverlay()
    {
        float w = Screen.width, h = Screen.height;
        float band = Mathf.Min(w, h) * 0.22f;
        var edge = new Color(0f, 0f, 0f, 0.75f);
        var clear = new Color(0f, 0f, 0f, 0f);

        GL.PushMatrix();
        GL.LoadPixelMatrix();
        GL.Begin(GL.QUADS);
        // vignette: four gradient bands
        GL.Color(edge); GL.Vertex3(0, 0, 0); GL.Vertex3(w, 0, 0);
        GL.Color(clear); GL.Vertex3(w, band, 0); GL.Vertex3(0, band, 0);
        GL.Color(edge); GL.Vertex3(0, h, 0); GL.Vertex3(w, h, 0);
        GL.Color(clear); GL.Vertex3(w, h - band, 0); GL.Vertex3(0, h - band, 0);
        GL.Color(edge); GL.Vertex3(0, 0, 0); GL.Vertex3(0, h, 0);
        GL.Color(clear); GL.Vertex3(band, h, 0); GL.Vertex3(band, 0, 0);
        GL.Color(edge); GL.Vertex3(w, 0, 0); GL.Vertex3(w, h, 0);
        GL.Color(clear); GL.Vertex3(w - band, h, 0); GL.Vertex3(w - band, 0, 0);
        // interlace scanlines
        var line = new Color(0f, 0f, 0f, 0.10f);
        GL.Color(line);
        for (float y = 0f; y < h; y += 36f)
        {
            GL.Vertex3(0, y, 0); GL.Vertex3(w, y, 0);
            GL.Vertex3(w, y + 2f, 0); GL.Vertex3(0, y + 2f, 0);
        }
        // slow rolling bright bar
        float roll = Mathf.Repeat(Time.unscaledTime * 90f, h + 120f) - 60f;
        var bar = new Color(1f, 1f, 1f, 0.045f);
        GL.Color(bar);
        GL.Vertex3(0, roll, 0); GL.Vertex3(w, roll, 0);
        GL.Vertex3(w, roll + 26f, 0); GL.Vertex3(0, roll + 26f, 0);
        GL.End();
        GL.PopMatrix();
    }

    void AccumulateScreen(Vector3 world, ref float minX, ref float minY, ref float maxX, ref float maxY, ref bool any)
    {
        Vector3 sp = _cam.WorldToScreenPoint(world);
        if (sp.z <= 0f) return;
        if (sp.x < minX) minX = sp.x;
        if (sp.y < minY) minY = sp.y;
        if (sp.x > maxX) maxX = sp.x;
        if (sp.y > maxY) maxY = sp.y;
        any = true;
    }

    void EnsureGlMat()
    {
        if (_glMat != null) return;
        var sh = Shader.Find("Hidden/Internal-Colored");
        _glMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        _glMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _glMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _glMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        _glMat.SetInt("_ZWrite", 0);
        _glMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
    }
}
