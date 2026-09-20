using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Phase 2 (Sam, 2026-09-19): the player plays quarterback for the blue team.
///
/// A red button on the home sideline (Interactable, press F) subs the player
/// in: from then on, whenever the blue team has the ball at scrimmage, its QB
/// alien jogs to the bench and disappears, and the QB SLOT is driven by the
/// player — position and facing copied from the real PlayerController every
/// sim step (FootballPlayer.SyncHuman), a HumanQBBrain instead of the CPU one.
/// Nothing else in the sim knows: catches, tackles, throws and the broadcast
/// all work on the slot as before. On kickoffs and defense the alien plays.
///
/// The snap: once everyone is lined up a green cylinder shows the QB spot;
/// walk into it, it vanishes, a second later the centre snaps to your hands.
/// The ball goes into the hotbar (ItemId.Football, auto-equipped) and is held
/// out in front (PlayerPickup.holdPosition). Hold LMB to charge a throw (up to
/// 2 s), aim with the camera; the arc and the landing spot are drawn while you
/// charge; release to throw (to whichever receiver is nearest the spot). Or
/// cross the line and run it. Tackled = the play ends, you're planted for a
/// moment. The ball is dropped at your feet after the whistle for the centre.
/// </summary>
public class FootballHumanQB : MonoBehaviour
{
    public Sprite hotbarIcon;
    public float chargeSeconds = 3f;                 // Sam: 2 s too fast, 4 s too slow
    public float minThrowSpeed = 13f, maxThrowSpeed = 25f;   // m/s at a tap and at full charge (harder = faster, not further by itself)
    public float baseLoftDeg = 8f, maxLoftDeg = 50f;         // looking level throws a slight upward bullet; look up to loft it
    public float fieldMoveScale = 0.8f;              // the astronaut's stride on the field (walk 8 / run 14 would make him uncatchable)
    public float knockdownSeconds = 1.6f;
    [Tooltip("Where the ball sits in the hands, in camera space (right, up, forward).")]
    public Vector3 handOffset = new Vector3(0.16f, -0.26f, 0.55f);

    public bool Active { get; private set; }
    /// Hotbar: the ball is an item while you hold it.
    public bool IsUnlocked => _holding;
    public bool IsEquipped => _holding && _equipped;
    public void ForceEquip() { _equipped = true; }
    public void ForceUnequip() { _equipped = false; }
    public HumanQBBrain Brain => _brain;
    /// Beside the button, off the home sideline: where the alien QB waits.
    public Vector3 BenchSpot => new Vector3(-(FootballField.HalfWidth + 3.5f), 0f, 4f);
    /// The astronaut body under the player (the "Mesh" child), for the replay double.
    public Transform BodyRoot
    {
        get
        {
            if (_body != null || _player == null) return _body;
            // The skinned astronaut, not the placeholder capsule child named "Mesh":
            // the Animator's object (Player/Astronaut: Armature + Body), else the skin's parent.
            var smr = _player.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null)
            {
                var an = smr.GetComponentInParent<Animator>();
                _body = an != null && an.transform != _player.transform ? an.transform : smr.transform.parent;
            }
            if (_body == null) _body = _player.transform.Find("Astronaut");
            return _body;
        }
    }
    Transform _body;
    Renderer[] _liveRenderers;
    /// Everything of the player's that the replay camera must not see: his body and the cylinder.
    public Renderer[] LiveRenderers()
    {
        if (_liveRenderers == null || _liveRenderers.Length == 0)
        {
            var list = new List<Renderer>();
            var body = BodyRoot; if (body != null) list.AddRange(body.GetComponentsInChildren<Renderer>(true));
            if (_cylinder != null) list.AddRange(_cylinder.GetComponentsInChildren<Renderer>(true));
            // (not the aim arc: the broadcast re-enables everything in this list after its render, and the arc is enabled only while charging)
            _liveRenderers = list.ToArray();
        }
        return _liveRenderers;
    }
    FootballBroadcast _broadcast;
    // The huddle: three cards, pick one.
    Canvas _menuCanvas; readonly RawImage[] _cards = new RawImage[3]; readonly Image[] _cardFrames = new Image[3]; readonly TextMeshProUGUI[] _cardTitles = new TextMeshProUGUI[3];
    readonly FootballPlay[] _offer = new FootballPlay[3]; readonly FootballFormation[] _offerForm = new FootballFormation[3];
    int _menuSel; bool _menuOpen; PlayInstance _menuPlay; System.Random _menuRng = new System.Random();

    FootballMatch _match;
    Transform _fieldRoot;
    PlayerController _player;
    Transform _hold;
    CameraTransformFX _camFx;
    FootballPlayer _slot;
    HumanQBBrain _brain;
    GameObject _button, _cylinder;
    LineRenderer _arc, _landing;
    bool _holding, _equipped, _wasHolding, _charging;
    float _charge, _downUntil = -1f, _equipRetry = -1f;
    bool _throwReady; Vector3 _throwTarget; FootballPlayer _throwTo; float _throwFlight;
    bool _scaled;

    void Start()
    {
        _match = FootballMatch.Instance;
        if (_match == null) { enabled = false; return; }
        _fieldRoot = transform.parent != null ? transform.parent : transform;
        _player = FindObjectOfType<PlayerController>();
        // The hands: an anchor under the camera's view frame (the pistol's
        // trick — PlayerPickup.holdPosition is a scene-authored point and in the
        // proto scene it sits 2 m under the turf, so the snap flew into the ground).
        if (_player != null)
        {
            Transform eye = _player.Camera != null ? CameraTransformFX.ViewFrameOf(_player.Camera.transform) : null;
            if (eye == null) eye = _player.Camera != null ? _player.Camera.transform : _player.transform;
            var hg = new GameObject("FootballHold");
            hg.transform.SetParent(eye, false);
            hg.transform.localPosition = handOffset;
            hg.transform.localRotation = Quaternion.identity;
            _hold = hg.transform;
        }
        _brain = new HumanQBBrain(this);
        if (hotbarIcon == null) hotbarIcon = BuildIcon();
        BuildButton();
        BuildAimVisuals();
        _match.PlayEnded += OnPlayEnded;
    }

    void OnDestroy()
    {
        if (_match != null) _match.PlayEnded -= OnPlayEnded;
        RestoreMoveScale();
    }

    // ── the button ─────────────────────────────────────────────────────────

    void BuildButton()
    {
        _button = new GameObject("QB Button");
        _button.transform.SetParent(_fieldRoot, false);
        _button.transform.localPosition = new Vector3(-(FootballField.HalfWidth + 3.5f), 0f, 0f);
        var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = "Post";
        post.transform.SetParent(_button.transform, false);
        post.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        post.transform.localScale = new Vector3(0.35f, 0.5f, 0.35f);
        post.GetComponent<Renderer>().sharedMaterial = new Material(FootballShader.Standard) { color = new Color(0.25f, 0.25f, 0.28f) };
        var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "Button";
        dome.transform.SetParent(_button.transform, false);
        dome.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        dome.transform.localScale = new Vector3(0.45f, 0.3f, 0.45f);
        var dm = new Material(FootballShader.Standard) { color = new Color(0.95f, 0.1f, 0.08f) };
        dm.EnableKeyword("_EMISSION"); dm.SetColor("_EmissionColor", new Color(0.6f, 0.02f, 0.02f));
        dome.GetComponent<Renderer>().sharedMaterial = dm;
        // The shipped interaction: a trigger zone for range, the dome as the gaze
        // target, F to press.
        var zone = _button.AddComponent<SphereCollider>();
        zone.isTrigger = true; zone.radius = 2.6f; zone.center = new Vector3(0f, 1f, 0f);
        var inter = _button.AddComponent<Interactable>();
        inter.interactMessage = "Press F to play QB";
        inter.gazeTarget = dome.transform;
        inter.interactEvent = new UnityEngine.Events.UnityEvent();
        inter.interactEvent.AddListener(OnButton);
        // The post's collider lets the gaze cast land; the dome keeps its own.
    }

    public void OnButton()
    {
        Active = !Active;
        var inter = _button.GetComponent<Interactable>();
        if (inter != null) inter.interactMessage = Active ? "Press F to sub out" : "Press F to play QB";
        InteractPromptUI.ShowOneShot(Active ? "You're in at QB for the " + _match.away.name + " — you take the field when they have the ball" : "Back to the sideline", 4f);
        _match.humanQb = Active ? this : null;
    }

    // ── per frame ──────────────────────────────────────────────────────────

    void Update()
    {
        if (_match == null || _player == null) return;
        var play = _match.CurrentPlay;
        bool driving = play != null && play.HumanDriven && play.humanSlot != null && play.humanSlot.humanDriven;
        _slot = play != null ? play.humanSlot : null;
        var ball = _match.Ball;
        _holding = driving && ball != null && ball.state == FootballBall.State.Held && ball.holder == _slot;

        // The snap spot.
        // The huddle: the cylinder marks your place in it; walk in and the play
        // cards come up. Nothing moves on until you have called one.
        bool huddling = driving && play.phase == PlayInstance.Phase.Setup && !play.Broke && !play.humanPlayChosen;
        if (_menuOpen && (!huddling || play != _menuPlay)) CloseMenu();
        if (huddling && !_menuOpen)
        {
            Vector3 hs = play.HumanHuddleSpot;
            _cylinder.transform.localPosition = hs + Vector3.up * 1.25f;
            Vector3 me = _fieldRoot.InverseTransformPoint(_player.transform.position);
            if (Vector3.Distance(new Vector3(me.x, 0f, me.z), hs) < 1.3f) OpenMenu(play);
        }
        if (_menuOpen) TickMenu(play);

        bool showSpot = driving && play.phase == PlayInstance.Phase.Setup && play.LinedUp && !play.humanAtSpot;
        if (_cylinder != null) _cylinder.SetActive(showSpot || (huddling && !_menuOpen));
        if (showSpot)
        {
            Vector3 spot = play.HumanSnapSpot;
            _cylinder.transform.localPosition = spot + Vector3.up * 1.25f;
            Vector3 me = _fieldRoot.InverseTransformPoint(_player.transform.position);
            if (Vector3.Distance(new Vector3(me.x, 0f, me.z), spot) < 1.0f)
            {
                play.humanAtSpot = true;
                InteractPromptUI.ShowOneShot("Set — the snap is coming", 1.5f);
            }
        }

        // The hotbar: the ball appears while held; equip it when it arrives.
        if (_holding && !_wasHolding)
        {
            _equipRetry = 0.6f;
            InteractPromptUI.ShowOneShot("Hold LMB to charge a throw, release to throw — or run it", 3f);
        }
        if (!_holding) { _charging = false; _charge = 0f; if (_arc != null) _arc.enabled = false; if (_landing != null) _landing.enabled = false; }
        if (_equipRetry > 0f)
        {
            _equipRetry -= Time.deltaTime;
            if (Hotbar.Instance != null && Hotbar.Instance.HasItem(Hotbar.ItemId.Football)) { Hotbar.Instance.EquipItem(Hotbar.ItemId.Football); _equipRetry = -1f; }
        }
        _wasHolding = _holding;

        // Throwing.
        bool live = play != null && play.phase == PlayInstance.Phase.Live;
        if (_holding && live && !_brain.Tucked && Time.time >= _downUntil)
        {
            if (Input.GetMouseButtonDown(0)) { _charging = true; _charge = 0f; }
            if (_charging && Input.GetMouseButton(0))
            {
                _charge = Mathf.Min(chargeSeconds, _charge + Time.deltaTime);
                DrawAim(Aim(out float f), f);
            }
            if (_charging && Input.GetMouseButtonUp(0))
            {
                _charging = false;
                Vector3 target = Aim(out float flight);
                _throwTarget = target; _throwFlight = flight;
                _throwTo = NearestReceiver(play, target);
                _throwReady = _throwTo != null;
                if (_arc != null) _arc.enabled = false; if (_landing != null) _landing.enabled = false;
            }
        }

        // Stride: a football stride on the field; planted after a tackle, still while calling the play.
        if (driving)
        {
            float scale = Time.time < _downUntil || _menuOpen ? 0f : fieldMoveScale;
            _player.introMoveScale = scale; _scaled = true;
        }
        else RestoreMoveScale();
    }

    void RestoreMoveScale()
    {
        if (_scaled && _player != null) { _player.introMoveScale = 1f; _scaled = false; }
    }

    void LateUpdate()
    {
        // The ball in the player's hands: the hold point, after the camera has moved.
        if (_holding && _hold != null && _match.Ball != null)
        {
            var b = _match.Ball.transform;
            b.position = _hold.position + _hold.forward * 0.08f - _hold.up * 0.02f;
            b.rotation = _hold.rotation * Quaternion.Euler(0f, 90f, 20f);
        }
    }

    /// Where the throw comes down (field space): along the camera's flat
    /// forward, further the longer LMB was held, inside the field.
    /// Where the throw comes down (field space) and how long it flies: the
    /// charge is the ARM (ball speed), the camera's pitch is the ANGLE. Level =
    /// a bullet that carries the short routes; look up to loft it deep (Sam).
    Vector3 Aim(out float flight)
    {
        var cam = _player.Camera != null ? _player.Camera.transform : _player.transform;
        Vector3 look = _fieldRoot.InverseTransformDirection(cam.forward);
        Vector3 fwd = look; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward; fwd.Normalize();
        Vector3 me = _fieldRoot.InverseTransformPoint(_player.transform.position); me.y = 0f;
        float pitchUp = Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg;
        float loft = Mathf.Clamp(pitchUp, -6f, maxLoftDeg) + baseLoftDeg;
        float speed = Mathf.Lerp(minThrowSpeed, maxThrowSpeed, _charge / chargeSeconds);
        float h = _slot != null ? _slot.BallHoldPoint().y : 1.4f;
        float vy = speed * Mathf.Sin(loft * Mathf.Deg2Rad), vh = speed * Mathf.Cos(loft * Mathf.Deg2Rad);
        // Time to come back down to catch height, then how far it carried.
        float disc = vy * vy + 2f * FootballBall.Gravity * (h - FootballBall.CatchHeight);
        flight = Mathf.Max(0.3f, (vy + Mathf.Sqrt(Mathf.Max(0f, disc))) / FootballBall.Gravity);
        float dist = vh * flight;
        Vector3 t = me + fwd * dist;
        t.x = Mathf.Clamp(t.x, -(FootballField.HalfWidth - 0.5f), FootballField.HalfWidth - 0.5f);
        t.z = Mathf.Clamp(t.z, -(FootballField.EndLineZ - 0.5f), FootballField.EndLineZ - 0.5f);
        return t;
    }

    static FootballPlayer NearestReceiver(PlayInstance play, Vector3 target)
    {
        FootballPlayer best = null; float bd = float.MaxValue;
        foreach (var p in play.view.players)
        {
            if (p.team != play.view.offense || p == play.humanSlot || p.role == FootballRole.OL || p.role == FootballRole.C) continue;
            float d = Vector3.Distance(p.Pos, target);
            if (d < bd) { bd = d; best = p; }
        }
        return best;
    }

    /// The brain asks once per tick: is there a throw to make?
    public bool TakeThrow(out Vector3 target, out FootballPlayer to, out float flight)
    {
        target = _throwTarget; to = _throwTo; flight = _throwFlight;
        if (!_throwReady) return false;
        _throwReady = false;
        return true;
    }

    /// Field-space point the camera looks at (for the slot's head).
    public Vector3 LookPoint()
    {
        var cam = _player.Camera != null ? _player.Camera.transform : _player.transform;
        return _fieldRoot.InverseTransformPoint(cam.position + cam.forward * 12f);
    }

    /// Copy the real player into the QB slot (called by the match before each sim step).
    public void SyncSlot(float dt)
    {
        if (_slot == null || !_slot.humanDriven || _player == null) return;
        Vector3 pos = _fieldRoot.InverseTransformPoint(_player.transform.position); pos.y = 0f;
        var cam = _player.Camera != null ? _player.Camera.transform : _player.transform;
        Vector3 facing = _fieldRoot.InverseTransformDirection(cam.forward); facing.y = 0f;
        Vector3 hands = _hold != null ? _fieldRoot.InverseTransformPoint(_hold.position) : pos + Vector3.up * 1.1f + facing.normalized * 0.4f;
        _slot.SyncHuman(pos, facing, hands, dt);
    }

    void OnPlayEnded(PlayInstance.PlayResult r)
    {
        if (_slot == null || r.carrier != _slot || r.isKickoff) return;
        if (r.outcome == PlayInstance.Outcome.Run || r.outcome == PlayInstance.Outcome.Sack || r.outcome == PlayInstance.Outcome.Fumble || r.outcome == PlayInstance.Outcome.Whistle)
        {
            _downUntil = Time.time + knockdownSeconds;
            // Fall the way the hit sent you: away from the tackler (or straight ahead).
            Vector3 hit = r.tackler != null ? _slot.Pos - r.tackler.Pos : Vector3.zero; hit.y = 0f;
            Vector3 world = hit.sqrMagnitude > 1e-3f ? _fieldRoot.TransformDirection(hit.normalized)
                : (_player.Camera != null ? _player.Camera.transform.forward : _player.transform.forward);
            if (_camFx == null) _camFx = FindObjectOfType<CameraTransformFX>();
            if (_camFx != null) _camFx.TriggerKnockdown(world, Mathf.Max(0.3f, knockdownSeconds - 0.7f));
            InteractPromptUI.ShowOneShot(r.outcome == PlayInstance.Outcome.Sack ? "Sacked!" : "Tackled", 1.5f);
        }
        else if (r.touchdown) InteractPromptUI.ShowOneShot("TOUCHDOWN!", 3f);
    }

    // ── visuals: the snap spot, the aim arc ────────────────────────────────

    // ── the huddle play cards ─────────────────────────────────────────────

    void OpenMenu(PlayInstance play)
    {
        if (_menuCanvas == null) BuildMenu();
        _menuPlay = play; _menuOpen = true; _menuSel = 0;
        // Three different pass calls (the trick plays hand off by brain; you have none).
        var pool = new List<FootballPlay>();
        foreach (var p in FootballPlay.All) if (p.kind == FootballPlay.Kind.Pass || p.kind == FootballPlay.Kind.Rollout) pool.Add(p);
        for (int i = 0; i < 3; i++)
        {
            FootballPlay pick = pool.Count > 0 ? pool[_menuRng.Next(pool.Count)] : null;
            if (pick != null) pool.Remove(pick);
            _offer[i] = pick; _offerForm[i] = pick != null ? FootballFormation.Pick(pick, _menuRng) : null;
            if (pick == null) continue;
            var old = _cards[i].texture as Texture2D; if (old != null) Destroy(old);
            _cards[i].texture = DrawPlayArt(pick, _offerForm[i]);
            _cardTitles[i].text = pick.name.ToUpperInvariant() + "\n<size=60%>" + _offerForm[i].name + " · " + (pick.kind == FootballPlay.Kind.Rollout ? "rollout" : "pass") + "</size>";
        }
        _menuCanvas.gameObject.SetActive(true);
        Highlight();
        InteractPromptUI.ShowOneShot("Call it: 1 / 2 / 3, or arrows + Enter", 3f);
    }

    void CloseMenu()
    {
        _menuOpen = false; _menuPlay = null;
        if (_menuCanvas != null) _menuCanvas.gameObject.SetActive(false);
    }

    void TickMenu(PlayInstance play)
    {
        int pick = -1;
        if (Input.GetKeyDown(KeyCode.Alpha1)) pick = 0;
        if (Input.GetKeyDown(KeyCode.Alpha2)) pick = 1;
        if (Input.GetKeyDown(KeyCode.Alpha3)) pick = 2;
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) { _menuSel = (_menuSel + 2) % 3; Highlight(); }
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) { _menuSel = (_menuSel + 1) % 3; Highlight(); }
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetMouseButtonDown(0)) pick = _menuSel;
        if (pick < 0 || _offer[pick] == null) return;
        play.ChoosePlay(_offer[pick], _offerForm[pick]);
        // Called it fast? The screens go back to live so the snap is not missed.
        if (_broadcast == null) _broadcast = FindObjectOfType<FootballBroadcast>();
        if (_broadcast != null) _broadcast.SkipReplay();
        CloseMenu();
    }

    void Highlight()
    {
        for (int i = 0; i < 3; i++) if (_cardFrames[i] != null) _cardFrames[i].color = i == _menuSel ? new Color(1f, 0.82f, 0.25f, 1f) : new Color(0.12f, 0.12f, 0.14f, 0.85f);
    }

    void BuildMenu()
    {
        var go = new GameObject("FootballPlayMenu", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        _menuCanvas = go.AddComponent<Canvas>();
        _menuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _menuCanvas.sortingOrder = 300;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1f;
        var title = NewText(go.transform, "Title", "CALL THE PLAY", 34f, FontStyles.Bold);
        title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        title.rectTransform.anchoredPosition = new Vector2(0f, 250f); title.rectTransform.sizeDelta = new Vector2(800f, 50f);
        for (int i = 0; i < 3; i++)
        {
            var frame = new GameObject("Card" + (i + 1), typeof(RectTransform)).GetComponent<RectTransform>();
            frame.SetParent(go.transform, false);
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(400f, 380f); frame.anchoredPosition = new Vector2((i - 1) * 440f, 0f);
            _cardFrames[i] = frame.gameObject.AddComponent<Image>();
            var art = new GameObject("Art", typeof(RectTransform)).GetComponent<RectTransform>();
            art.SetParent(frame, false); art.anchorMin = new Vector2(0.5f, 1f); art.anchorMax = new Vector2(0.5f, 1f); art.pivot = new Vector2(0.5f, 1f);
            art.anchoredPosition = new Vector2(0f, -10f); art.sizeDelta = new Vector2(380f, 285f);
            _cards[i] = art.gameObject.AddComponent<RawImage>();
            var t = NewText(frame, "Name", "", 26f, FontStyles.Bold);
            t.rectTransform.anchorMin = new Vector2(0f, 0f); t.rectTransform.anchorMax = new Vector2(1f, 0f); t.rectTransform.pivot = new Vector2(0.5f, 0f);
            t.rectTransform.anchoredPosition = new Vector2(0f, 8f); t.rectTransform.sizeDelta = new Vector2(0f, 74f);
            t.alignment = TextAlignmentOptions.Center;
            _cardTitles[i] = t;
            var num = NewText(frame, "Num", (i + 1).ToString(), 22f, FontStyles.Bold);
            num.rectTransform.anchorMin = num.rectTransform.anchorMax = new Vector2(0f, 1f); num.rectTransform.pivot = new Vector2(0f, 1f);
            num.rectTransform.anchoredPosition = new Vector2(14f, -14f); num.rectTransform.sizeDelta = new Vector2(40f, 30f);
            num.color = new Color(1f, 0.82f, 0.25f, 1f);
        }
        go.SetActive(false);
    }

    static TextMeshProUGUI NewText(Transform parent, string name, string text, float size, FontStyles style)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(t);
        t.text = text; t.fontSize = size; t.fontStyle = style; t.color = Color.white; t.alignment = TextAlignmentOptions.Center;
        return t;
    }

    /// Madden-style play art: the line, the QB, the three receivers and their
    /// routes (the first read in gold), the defence's shells faint.
    static Texture2D DrawPlayArt(FootballPlay play, FootballFormation form)
    {
        const int W = 256, Hh = 192; const float px = 4.2f;           // pixels per yard
        var tex = new Texture2D(W, Hh, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var c = new Color32[W * Hh];
        var grass = new Color32(28, 78, 36, 255); var line = new Color32(70, 120, 78, 255);
        for (int i = 0; i < c.Length; i++) c[i] = grass;
        float losY = 40f;
        for (int yd = -10; yd <= 40; yd += 5) { int y = Mathf.RoundToInt(losY + yd * px); if (y >= 0 && y < Hh) for (int x = 0; x < W; x++) c[y * W + x] = line; }
        int ly = Mathf.RoundToInt(losY); for (int x = 0; x < W; x++) { c[ly * W + x] = new Color32(235, 235, 235, 255); if (ly + 1 < Hh) c[(ly + 1) * W + x] = new Color32(235, 235, 235, 255); }
        System.Func<float, float, Vector2Int> P = (xYd, dYd) => new Vector2Int(Mathf.RoundToInt(W * 0.5f + xYd * px), Mathf.RoundToInt(losY + dYd * px));
        var white = new Color32(240, 240, 240, 255); var gold = new Color32(255, 205, 70, 255); var blue = new Color32(110, 170, 255, 255); var red = new Color32(230, 90, 80, 255);
        // Line, QB, defence shells.
        Dot(c, W, Hh, P(0f, -0.5f), 4, white); Dot(c, W, Hh, P(-1.6f, -0.5f), 4, white); Dot(c, W, Hh, P(1.6f, -0.5f), 4, white);
        Dot(c, W, Hh, P(0f, -4.5f), 5, blue);
        Dot(c, W, Hh, P(-1.6f, 1.0f), 3, red); Dot(c, W, Hh, P(1.6f, 1.0f), 3, red); Dot(c, W, Hh, P(0f, 4.5f), 3, red); Dot(c, W, Hh, P(0f, 12f), 3, red);
        int first = play.priority != null && play.priority.Length > 0 ? play.priority[0] : -1;
        for (int i = 0; i < 3; i++)
        {
            float wx = form.wrX[i]; float side = Mathf.Sign(wx);
            var start = P(wx, i == 2 ? -1.2f : -0.8f);
            Dot(c, W, Hh, P(wx, 3.5f + (i == 2 ? 1.2f : 0f)), 3, red);
            var col = i == first ? gold : white;
            var prev = start;
            foreach (var wp in play.routes[i].points) { var nxt = P(wx + wp.x * side, wp.y); Line(c, W, Hh, prev, nxt, col); prev = nxt; }
            Dot(c, W, Hh, prev, 3, col);
            Dot(c, W, Hh, start, 5, blue);
        }
        tex.SetPixels32(c); tex.Apply(false, false);
        return tex;
    }
    static void Dot(Color32[] c, int W, int H, Vector2Int p, int r, Color32 col)
    {
        for (int y = -r; y <= r; y++) for (int x = -r; x <= r; x++)
        { if (x * x + y * y > r * r) continue; int px = p.x + x, py = p.y + y; if (px >= 0 && px < W && py >= 0 && py < H) c[py * W + px] = col; }
    }
    static void Line(Color32[] c, int W, int H, Vector2Int a, Vector2Int b, Color32 col)
    {
        int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y), 1);
        for (int s = 0; s <= steps; s++)
        {
            float t = s / (float)steps; int x = Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t)), y = Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t));
            for (int dy = 0; dy <= 1; dy++) for (int dx = 0; dx <= 1; dx++) { int px = x + dx, py = y + dy; if (px >= 0 && px < W && py >= 0 && py < H) c[py * W + px] = col; }
        }
    }

    void BuildAimVisuals()
    {
        _cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(_cylinder.GetComponent<Collider>());
        _cylinder.name = "SnapSpot";
        _cylinder.transform.SetParent(_fieldRoot, false);
        _cylinder.transform.localScale = new Vector3(2.2f, 1.25f, 2.2f);
        var m = new Material(FootballShader.Standard) { color = new Color(0.2f, 1f, 0.3f, 0.35f) };
        m.SetFloat("_Mode", 3f); m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0); m.EnableKeyword("_ALPHABLEND_ON"); m.renderQueue = 3000;
        var mr = _cylinder.GetComponent<Renderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _cylinder.SetActive(false);

        _arc = MakeLine("ThrowArc", new Color(1f, 0.9f, 0.3f), 0.06f, false);
        _landing = MakeLine("ThrowLanding", new Color(1f, 0.9f, 0.3f), 0.08f, true);
        _landing.positionCount = 36;
    }

    LineRenderer MakeLine(string name, Color c, float width, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_fieldRoot, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; lr.loop = loop; lr.widthMultiplier = width;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
        var sh = FootballShader.Sprite;
        lr.sharedMaterial = new Material(sh != null ? sh : FootballShader.Unlit) { color = Color.white };
        lr.startColor = lr.endColor = c;
        lr.enabled = false;
        return lr;
    }

    /// The parabola the ball will fly (same maths as FootballBall.Launch) and a ring where it lands.
    void DrawAim(Vector3 target, float flight)
    {
        if (_arc == null || _slot == null) return;
        Vector3 start = _slot.BallHoldPoint();
        Vector3 end = new Vector3(target.x, FootballBall.CatchHeight, target.z);
        Vector3 v = (end - start) / flight + Vector3.up * (0.5f * FootballBall.Gravity * flight);
        const int N = 24;
        _arc.positionCount = N;
        for (int i = 0; i < N; i++)
        {
            float t = flight * i / (N - 1);
            _arc.SetPosition(i, start + v * t + Vector3.down * (0.5f * FootballBall.Gravity * t * t));
        }
        _arc.enabled = true;
        for (int i = 0; i < 36; i++) { float a = i * Mathf.PI * 2f / 36f; _landing.SetPosition(i, new Vector3(target.x + Mathf.Cos(a) * 1.2f, 0.05f, target.z + Mathf.Sin(a) * 1.2f)); }
        _landing.enabled = true;
    }

    /// A football drawn in code for the hotbar (brown, laces), like the grapple / beer icons.
    static Sprite BuildIcon()
    {
        const int S = 96;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color32[S * S];
        var clear = new Color32(0, 0, 0, 0);
        var brown = new Color32(120, 62, 30, 255); var dark = new Color32(80, 40, 18, 255); var white = new Color32(245, 240, 230, 255);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                // A tilted ellipse.
                float u = (x - S * 0.5f) / (S * 0.46f), w = (y - S * 0.5f) / (S * 0.28f);
                float ru = u * 0.7071f - w * 0.7071f, rw = u * 0.7071f + w * 0.7071f;
                float d = ru * ru + rw * rw;
                Color32 c = clear;
                if (d <= 1f) { c = d > 0.85f ? dark : brown; if (Mathf.Abs(ru) < 0.35f && Mathf.Abs(rw) < 0.05f) c = white; if (Mathf.Abs(rw) < 0.22f && Mathf.Abs(ru) < 0.03f) c = white; }
                px[y * S + x] = c;
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 96f);
    }
}

/// The player's brain for the QB slot: no movement (the slot is synced to the
/// real player), a throw when the controller has one ready, a tuck the moment
/// he crosses the line with it.
public class HumanQBBrain : IPlayerBrain
{
    readonly FootballHumanQB _h;
    bool _tucked;
    public bool Tucked => _tucked;
    public HumanQBBrain(FootballHumanQB h) { _h = h; }
    public bool KeepsControlWhenCarrying => true;
    public void Reset() { _tucked = false; }

    public void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput o)
    {
        o.move = Vector3.zero;
        o.look = _h.LookPoint();
        if (!view.snapped || view.Carrier != self) return;
        if (_h.TakeThrow(out Vector3 target, out FootballPlayer to, out float flight))
        {
            o.action = BrainAction.Throw; o.target = target; o.targetPlayer = to; o.power = 0.5f; o.flight = flight;
            o.say = self.team.shortName + " QB (you) throws toward " + to.Label + ", " + Mathf.RoundToInt(FootballField.ToYards(view.Downfield(target))) + " yds downfield";
            return;
        }
        if (!_tucked && view.Downfield(self.Pos) > -0.3f)
        {
            _tucked = true;
            o.action = BrainAction.Handoff; o.targetPlayer = self;
            o.say = self.team.shortName + " QB (you) crosses the line — it's a run";
        }
    }
}
