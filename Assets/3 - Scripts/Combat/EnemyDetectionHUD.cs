using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The suit's threat-detection system: one INDIVIDUAL arrow per enemy that is currently
/// stalking the player. Off-screen threat → the arrow sits at the screen edge pointing
/// whichever way is quicker to turn toward it; as the enemy comes on screen the arrow
/// smoothly transitions to floating above its head, pointing down at it. Three enemies
/// eyeing you = three arrows.
///
/// Modes per arrow:
///   • OBSERVED (red, fills bottom-up): that enemy sees you and its suspicion is building.
///     Disappears when it loses sight of you, or the instant it goes aggro (chase).
///   • SEARCHING (pale, pulsing): that enemy lost you and is sniffing around — fades out
///     as it gives up. Turns red-and-filling if it thinks it sees you again (2× fill).
///
/// Auto-created (RuntimeInitializeOnLoadMethod + MainMenu skip) and seeded in
/// MainMenuController.EnsureGameplaySingletons (CLAUDE.md trap #1). K toggles debug cones.
/// </summary>
// DefaultExecutionOrder(310): LateUpdate must run AFTER the camera is finalised for the
// frame (CameraTransformFX at 100, trailer free-cam at 200, dust/flare at 300) — at the
// default order the arrows read a one-frame-stale camera pose and visibly trailed the
// enemies whenever the player panned the view.
[DefaultExecutionOrder(310)]
public class EnemyDetectionHUD : MonoBehaviour
{
    public static EnemyDetectionHUD Instance { get; private set; }

    [Tooltip("World height above the enemy root the on-screen arrow floats at. ~1.6 sits just over the head.")]
    public float arrowWorldHeight = 1.6f;
    [Tooltip("Radius (canvas units, 1080-ref) of the circular safe zone arrows are confined to. Keeps them inside the helmet border art — they clamp to this circle instead of reaching the screen edges.")]
    public float arrowRadius = 430f;
    [Tooltip("Enemy distance (m) at or below which the arrow renders at full size.")]
    public float fullSizeDistance = 5f;
    [Tooltip("Smallest arrow scale for far-away enemies.")]
    public float minArrowScale = 0.45f;

    Canvas _canvas;
    RectTransform _canvasRt;
    Camera _cam;

    static readonly Color ObservedFill = new Color(1f, 0.18f, 0.18f, 1f);
    static readonly Color ObservedBg   = new Color(0.5f, 0.10f, 0.10f, 0.45f);
    static readonly Color SearchColor  = new Color(0.85f, 0.87f, 0.92f, 1f);

    class Arrow
    {
        public RectTransform Rt;
        public Image Bg;
        public Image Fill;
        public CanvasGroup Group;
        public float RotZ;
        public bool Fresh;       // snap (don't lerp) on the first placement
        public bool WasOnScreen; // last frame's mode, for snap-vs-transition decisions
    }

    readonly Dictionary<EnemyController, Arrow> _arrows = new Dictionary<EnemyController, Arrow>();
    readonly Stack<Arrow> _pool = new Stack<Arrow>();
    static readonly List<EnemyController> _release = new List<EnemyController>();
    // Enemies whose arrow was shown/updated THIS frame — the cleanup pass fades
    // everything else. Keeping one source of truth avoids the selection and
    // cleanup passes disagreeing about who is pingable.
    static readonly HashSet<EnemyController> _shownThisFrame = new HashSet<EnemyController>();
    static readonly RaycastHit[] _losBuf = new RaycastHit[8];

    // ── The suit-camera rule ── Arrows are diegetic: the SUIT's cameras ping enemies.
    // An enemy the suit cannot physically see (wall / hill / ship hull in between) must
    // NEVER be pinged — no wallhack arrows, in any mode. One raycast per candidate arrow.
    bool SuitCanSee(EnemyController ec)
    {
        Vector3 origin = _cam.transform.position;
        Vector3 target = ec.transform.position + ec.transform.up * 1.2f;   // body centre
        Vector3 d = target - origin;
        float dist = d.magnitude;
        if (dist < 2f) return true;
        Vector3 dir = d / dist;
        // Blockers: everything except Sun(11)/FishPreview(12). The SHIP counts as a
        // blocker (unlike enemy vision) — suit cameras can't see through the hull.
        int n = Physics.RaycastNonAlloc(origin, dir, _losBuf, dist, ~((1 << 11) | (1 << 12)),
                                        QueryTriggerInteraction.Ignore);
        float nearestBlock = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var h = _losBuf[i];
            if (h.collider == null) continue;
            if (h.collider.GetComponentInParent<PlayerController>() != null) continue;   // own body
            if (h.collider.transform.IsChildOf(ec.transform)) continue;                   // the enemy itself
            if (h.collider.GetComponentInParent<EnemyController>() == ec) continue;       // its bone colliders
            if (h.distance < nearestBlock) nearestBlock = h.distance;
        }
        // Visible only if nothing solid sits meaningfully in front of the enemy.
        return nearestBlock >= dist - 1.5f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("EnemyDetectionHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<EnemyDetectionHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildCanvas();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 500;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasRt = _canvas.GetComponent<RectTransform>();
    }

    void LateUpdate()
    {
        // Dev toggle for the red vision cones. K is unused (V=match velocity, B=fishingdex,
        // N=build menu, H=tutorial, J=blackhole-grow, F10/F11=lighting toolbox).
        if (Input.GetKeyDown(KeyCode.K))
            EnemyVision.ShowDebugCones = !EnemyVision.ShowDebugCones;

        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
        if (_cam == null) return;

        // ── Assign/update one arrow per stalking enemy ──
        _shownThisFrame.Clear();
        var list = EnemyController.ActiveEnemies;
        for (int i = 0; i < list.Count; i++)
        {
            var ec = list[i];
            if (ec == null || ec.IsDying) continue;
            var v = ec.Vision;
            if (v == null) continue;

            bool chasing = ec.State == EnemyController.AIState.Chasing;
            bool observed = !chasing && v.CanSeePlayerNow && v.Suspicion01 > 0.001f;
            bool searching = !chasing && !observed && ec.State == EnemyController.AIState.Searching;
            if (!chasing && !observed && !searching) continue;

            // Suit-camera rule: no line of sight from the suit to this enemy → no ping,
            // regardless of mode. (Its existing arrow quick-fades via the cleanup pass.)
            if (!SuitCanSee(ec)) continue;
            _shownThisFrame.Add(ec);

            if (!_arrows.TryGetValue(ec, out var a))
            {
                a = GetArrow();
                a.Fresh = true;
                _arrows.Add(ec, a);
            }

            // Look + fill per mode.
            if (chasing)
            {
                // Locked on: arrow stays (at half size, see PlaceArrow) with a full red fill.
                a.Bg.color = ObservedBg;
                a.Fill.enabled = true;
                a.Fill.fillAmount = 1f;
                a.Group.alpha = Mathf.MoveTowards(a.Group.alpha, 1f, Time.deltaTime * 6f);
            }
            else if (observed)
            {
                a.Bg.color = ObservedBg;
                a.Fill.enabled = true;
                a.Fill.fillAmount = v.Suspicion01;
                a.Group.alpha = Mathf.MoveTowards(a.Group.alpha, 1f, Time.deltaTime * 6f);
            }
            else
            {
                a.Bg.color = SearchColor;
                a.Fill.enabled = false;
                float pulse = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(Time.time * 2.2f));
                a.Group.alpha = pulse * (1f - ec.SearchProgress01);
            }

            PlaceArrow(a, ec);
        }

        // ── Fade + release arrows whose enemy stopped stalking ──
        _release.Clear();
        foreach (var kv in _arrows)
        {
            var ec = kv.Key;
            var a = kv.Value;
            // Single source of truth: only enemies that passed EVERY gate this frame
            // (threat mode + suit line of sight) keep their arrow.
            if (ec != null && _shownThisFrame.Contains(ec)) continue;

            // Dead → vanish instantly; lost sight / went docile → quick fade.
            bool instant = ec == null || ec.IsDying;
            a.Group.alpha = instant ? 0f : Mathf.MoveTowards(a.Group.alpha, 0f, Time.deltaTime * 5f);
            if (a.Group.alpha <= 0.02f) _release.Add(ec);
        }
        for (int i = 0; i < _release.Count; i++)
        {
            var a = _arrows[_release[i]];
            a.Rt.gameObject.SetActive(false);
            _pool.Push(a);
            _arrows.Remove(_release[i]);
        }
    }

    // Edge-of-screen (off-screen threat, pointing the quicker turn direction) or floating
    // above the enemy pointing down (on-screen). Position + rotation lerp continuously, so
    // the edge→overhead handoff reads as one smooth transition.
    void PlaceArrow(Arrow a, EnemyController ec)
    {
        // Scale the hover height with the enemy's size — the brute (scaled-up root) needs
        // the arrow proportionally higher or it sinks into its body.
        float heightMul = Mathf.Max(1f, ec.transform.localScale.y);
        Vector3 worldPos = ec.transform.position + ec.transform.up * (arrowWorldHeight * heightMul);
        Vector3 vp = _cam.WorldToViewportPoint(worldPos);
        Vector2 canvasSize = _canvasRt.rect.size;

        Vector2 targetPos;
        float targetRot;
        bool onScreen = vp.z > 0f && vp.x > 0.06f && vp.x < 0.94f && vp.y > 0.08f && vp.y < 0.92f;
        if (onScreen)
        {
            targetPos = new Vector2((vp.x - 0.5f) * canvasSize.x, (vp.y - 0.5f) * canvasSize.y);
            targetRot = 180f;   // chevron points DOWN at the enemy
        }
        else
        {
            // Quicker-turn side: enemy direction in camera-local space; x sign says left/right.
            Vector3 local = _cam.transform.InverseTransformDirection(
                (ec.transform.position - _cam.transform.position).normalized);
            bool right = local.x >= 0f;
            targetPos = new Vector2((right ? 1f : -1f) * arrowRadius, 0f);
            targetRot = right ? -90f : 90f;   // chevron points toward that screen edge
        }

        // Confine every arrow to the circular safe zone — the helmet border art rings the
        // outer screen, so arrows near the edges were hiding behind it. Clamp, don't render
        // over the helmet.
        if (targetPos.sqrMagnitude > arrowRadius * arrowRadius)
            targetPos = targetPos.normalized * arrowRadius;

        // Distance scale: full size at fullSizeDistance and closer, shrinking for far enemies.
        float dist = Vector3.Distance(_cam.transform.position, ec.transform.position);
        float scale = Mathf.Clamp(fullSizeDistance / Mathf.Max(fullSizeDistance, dist), minArrowScale, 1f);
        // Chasing = already locked on; keep the arrow but drop it to half size so it
        // marks the chaser without dominating the screen during the fight.
        if (ec.State == EnemyController.AIState.Chasing) scale *= 0.5f;
        a.Rt.localScale = new Vector3(scale, scale, 1f);

        if (a.Fresh)
        {
            a.Rt.anchoredPosition = targetPos;
            a.RotZ = targetRot;
            a.Fresh = false;
        }
        else if (onScreen && a.WasOnScreen)
        {
            // Steady on-screen tracking must be LATENCY-FREE: any screen-space smoothing
            // here reads as the arrow dragging behind the enemy whenever the camera pans.
            // Snap position; the smoothing exists only for the edge↔overhead transition.
            a.Rt.anchoredPosition = targetPos;
            a.RotZ = targetRot;
        }
        else
        {
            // Mode transition (edge→overhead or back): fast eased glide, ~3x the old rate.
            float k = 1f - Mathf.Exp(-28f * Time.deltaTime);
            a.Rt.anchoredPosition = Vector2.Lerp(a.Rt.anchoredPosition, targetPos, k);
            a.RotZ = Mathf.LerpAngle(a.RotZ, targetRot, k);
        }
        a.WasOnScreen = onScreen;
        a.Rt.localRotation = Quaternion.Euler(0f, 0f, a.RotZ);
    }

    Arrow GetArrow()
    {
        if (_pool.Count > 0)
        {
            var pooled = _pool.Pop();
            pooled.Rt.gameObject.SetActive(true);
            pooled.Group.alpha = 0f;
            return pooled;
        }

        var go = new GameObject("ThreatArrow", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(92f, 76f);

        var group = go.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        group.alpha = 0f;

        var sprite = GetChevronSprite();
        var bg = go.AddComponent<Image>();
        bg.sprite = sprite;
        bg.color = ObservedBg;
        bg.raycastTarget = false;

        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(go.transform, false);
        var fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;
        var fill = fillGo.AddComponent<Image>();
        fill.sprite = sprite;
        fill.color = ObservedFill;
        fill.raycastTarget = false;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Vertical;
        fill.fillOrigin = (int)Image.OriginVertical.Bottom;   // fills bottom → up
        fill.fillAmount = 0f;

        return new Arrow { Rt = rt, Bg = bg, Fill = fill, Group = group };
    }

    // ── Chevron sprite: SHARP-edged military-style ∧ — an angled strip between two V
    // lines with a pointed apex, crisp inner notch, and flat-cut arm ends. No rounded
    // caps, no soft feather; 2×2 supersampling keeps the diagonals clean, not blurry. ──
    static Sprite _chevronSprite;
    static Sprite GetChevronSprite()
    {
        if (_chevronSprite != null) return _chevronSprite;
        const int s = 64;
        const float halfW = 0.40f;   // arm reach from center
        const float slope = 1.55f;   // V steepness
        const float apexV = 0.90f;   // top of the point
        const float thick = 0.30f;   // vertical strip thickness
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        float u = (x + 0.25f + sx * 0.5f) / s;
                        float v = (y + 0.25f + sy * 0.5f) / s;
                        float dx = Mathf.Abs(u - 0.5f);
                        if (dx > halfW) continue;                  // flat vertical arm-end cut
                        float top = apexV - slope * dx;
                        if (v <= top && v >= top - thick) hits++;  // between the two V lines
                    }
                px[y * s + x] = new Color(1f, 1f, 1f, hits / 4f);
            }
        tex.SetPixels(px); tex.Apply();
        _chevronSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        _chevronSprite.name = "ThreatArrowChevron";
        return _chevronSprite;
    }
}
