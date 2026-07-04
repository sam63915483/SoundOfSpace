using System.Collections.Generic;
using UnityEngine;

public class SolarSystemMapController : MonoBehaviour
{
    public static SolarSystemMapController Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.isOpen;

    public KeyCode toggleKey = KeyCode.M;
    public KeyCode cursorLockToggleKey = KeyCode.G;

    [Header("Wired by MapBootstrapReal at runtime")]
    public Camera mapCamera;
    public MapCameraRig cameraRig;
    public Camera mainCamera;
    public Canvas legendCanvas;
    public MapLegendUI legendUI;
    public MapOrbitLines orbitLines;
    public MapVelocityHud velocityHud;
    public MapTeleportToPilotButton teleportButton;

    // Map-tutorial observable events. The MapTutorial subscribes to these so
    // each step can detect "the user did the thing" without grovelling for
    // controller state from outside. All fire from inside Update / LateUpdate
    // / OnLegendClick on the same frame the action lands.
    public event System.Action<CelestialBody> OnVelocityMatched;   // LMB in-world hit a body (or focus locked onto one)
    public event System.Action OnVelocityUnmatched;                // LMB cleared followed
    public event System.Action<CelestialBody> OnLegendBodyClicked; // legend entry clicked (any state)
    public event System.Action<CelestialBody> OnLegendBodyMarked;  // 2nd-click-on-same-body → focus + ring stays
    public event System.Action<bool> OnCursorLockChanged;          // G toggled

    [Header("Focus framing")]
    public float focusDistanceMultiplier = 4f;

    CelestialBody[] bodies;
    CelestialBody sunBody;
    int bodyLayerMask;

    CelestialBody followed;
    Ship followedShip;                  // mutually exclusive with `followed`
    Transform followedPlayer;           // mutually exclusive with `followed` AND `followedShip`
    Vector3 followedLastPos;

    // Per-target camera memory — session-only, not saved to disk. When the
    // player has been looking at body A and switches to body B, we snapshot
    // A's camera state (position-offset-from-body + rotation) so that
    // switching back to A restores their adjusted view instead of snapping
    // back to the default lit-side framing. Same for ships, keyed by the
    // Ship reference. Cleared when the SolarSystemMapController is destroyed
    // (i.e. scene reload), so loading a save starts fresh — the user said
    // persistence through saves isn't needed.
    struct CameraView { public Vector3 offset; public Quaternion rotation; }
    Dictionary<CelestialBody, CameraView> _bodyViewCache = new Dictionary<CelestialBody, CameraView>();
    Dictionary<Ship, CameraView> _shipViewCache = new Dictionary<Ship, CameraView>();

    // Legacy 2-circle ring around a marked target. No longer created
    // (EnsureHighlightRing removed); kept as a nullable field so the
    // open/close map code's defensive null-checks stay valid for any
    // future caller that wants to revive it.
    MapHighlightRing highlightRing;
    // pendingHighlight = the "marked planet" with lock-on brackets,
    // shared with ShipHUD's in-cockpit lock-on. Set from the cockpit's
    // LMB-on-planet or from clicking a body in the map's world view —
    // NOT from the legend, which is now pure navigation.
    CelestialBody pendingHighlight;
    Ship pendingHighlightShip;          // mutually exclusive with `pendingHighlight`

    bool isOpen;
    int legendCursor = -1;          // controller D-pad highlight position
    CursorLockMode prevCursorLock;
    bool prevCursorVisible;
    bool mapCursorLocked;           // toggled by G while map is open

    Canvas[] hiddenCanvases;
    bool[] hiddenCanvasesPrev;

    Rigidbody playerRb;
    Vector3 prevPlayerPos;
    EndlessManager endless;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Init(CelestialBody[] allBodies)
    {
        bodies = allBodies;
        sunBody = null;
        if (allBodies != null)
        {
            for (int i = 0; i < allBodies.Length; i++)
            {
                if (allBodies[i] != null && allBodies[i].bodyType == CelestialBody.BodyType.Sun)
                {
                    sunBody = allBodies[i];
                    break;
                }
            }
        }
        int bodyLayer = LayerMask.NameToLayer("Body");
        bodyLayerMask = bodyLayer >= 0 ? (1 << bodyLayer) : ~0;
    }

    void Update()
    {
        if (AIChatScreen.IsTypingActive) return;
        // Configurable keyboard toggle (default M) OR controller Back/View button.
        if (TutorialGate.GetKeyDown(toggleKey, TutorialAbility.Map) ||
            TutorialGate.MapTogglePressed(TutorialAbility.Map))
        {
            if (!isOpen && Time.timeScale == 0f) return;
            // Block close while the map tutorial is in flight — the player
            // must finish all map tips before M closes the map again.
            if (isOpen && MapTutorial.Instance != null && MapTutorial.Instance.BlockMapClose) return;
            if (isOpen) CloseMap(); else OpenMap();
        }

        if (isOpen && playerRb != null)
        {
            // Keep the pre-shift player position fresh so OnFloatingOriginShift can compute the delta.
            // We update it here in Update so it reflects the player position BEFORE EndlessManager's LateUpdate runs.
            prevPlayerPos = playerRb.position;
        }

        // G: toggle cursor lock so the user can look around without holding RMB.
        if (isOpen && Input.GetKeyDown(cursorLockToggleKey))
        {
            SetMapCursorLocked(!mapCursorLocked);
        }

        // ESC closes the map (does NOT bubble to the pause menu — see
        // TabbedPauseMenu.Update for the SolarSystemMapController.IsOpen guard
        // that prevents the pause menu from popping on top of the map).
        if (isOpen && (Input.GetKeyDown(KeyCode.Escape) || TutorialGate.PadPressed(TutorialGate.PadButton.B)))
        {
            if (MapTutorial.Instance != null && MapTutorial.Instance.BlockMapClose) return;
            CloseMap();
        }
    }

    void SetMapCursorLocked(bool locked)
    {
        mapCursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
        if (legendUI != null) legendUI.SyncCursorLockHint(locked);
        OnCursorLockChanged?.Invoke(locked);
    }

    public void OpenMap()
    {
        isOpen = true;
        legendCursor = -1;
        PlayerController.isMapOpen = true;

        prevCursorLock = Cursor.lockState;
        prevCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Hide every Canvas except the legend (and the tutorial pill, so the
        // MapTutorial / any running main tutorial stays visible on top of the
        // map view). Save state for restore on close.
        Canvas tutorialCanvas = TutorialUI.Instance != null ? TutorialUI.Instance.TutorialCanvas : null;
        Canvas[] all = FindObjectsOfType<Canvas>(true);
        hiddenCanvases = all;
        hiddenCanvasesPrev = new bool[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            hiddenCanvasesPrev[i] = all[i].enabled;
            if (all[i] == legendCanvas) continue;
            if (all[i] == tutorialCanvas) continue;
            all[i].enabled = false;
        }
        if (legendCanvas != null) legendCanvas.enabled = true;

        if (mainCamera != null) mainCamera.enabled = false;
        if (mapCamera != null) mapCamera.enabled = true;
        if (cameraRig != null) cameraRig.Activate();

        // Restore the persistent ring's view-camera + the legend's selected button highlight.
        if (highlightRing != null) highlightRing.viewCamera = mapCamera;
        if (legendUI != null)
        {
            legendUI.SetSelected(pendingHighlight);
            if (orbitLines != null) legendUI.SyncOrbitToggleVisible(orbitLines.Visible);
            legendUI.SyncCursorLockHint(false);
        }
        mapCursorLocked = false;

        if (velocityHud != null) velocityHud.SetVisible(true);
        if (MapTutorial.Instance != null) MapTutorial.Instance.OnMapOpened();
        // Orbit lines: persistent toggle state. If the user had them on
        // before closing the map, SetMapOpen(true) restores rendering
        // without re-toggling. The Visible flag is preserved across close.
        if (orbitLines != null) orbitLines.SetMapOpen(true);

        // Ship legend section: rebuild for whichever dish-equipped ships
        // currently exist. Ships gained/lost between map sessions are
        // reflected immediately on map reopen.
        if (legendUI != null) legendUI.RefreshShipEntries(GatherDishedShips());
        if (teleportButton != null) teleportButton.SetMapOpen(true);

        // Floating-origin compensation: cache player position and subscribe.
        if (playerRb == null)
        {
            var pc = FindObjectOfType<PlayerController>();
            if (pc != null) playerRb = pc.GetComponent<Rigidbody>();
        }
        if (endless == null) endless = FindObjectOfType<EndlessManager>();
        if (playerRb != null) prevPlayerPos = playerRb.position;
        if (endless != null) endless.PostFloatingOriginUpdate += OnFloatingOriginShift;

        // Restore the cached view for the followed target if we have one.
        // Without this, the camera sits at its old world position while the
        // body has orbited away during the close — the catch-up tracking
        // only handles motion FROM NOW ON, so the gap that built up while
        // the map was closed stays. Restoring from the cache against the
        // body's CURRENT position re-syncs the camera to the same relative
        // view the player had when they closed. (Cache is populated by
        // CloseMap and by RememberCurrentView() on focus-switch.)
        if (followed != null && _bodyViewCache.TryGetValue(followed, out var savedBody) && mapCamera != null)
        {
            mapCamera.transform.position = followed.Position + savedBody.offset;
            mapCamera.transform.rotation = savedBody.rotation;
            if (cameraRig != null) cameraRig.Activate();
        }
        else if (followedShip != null && _shipViewCache.TryGetValue(followedShip, out var savedShip) && mapCamera != null)
        {
            mapCamera.transform.position = followedShip.transform.position + savedShip.offset;
            mapCamera.transform.rotation = savedShip.rotation;
            if (cameraRig != null) cameraRig.Activate();
        }

        // Seed followedLastPos to the target's CURRENT position so the
        // LateUpdate tracking-delta starts from zero and tracks orbital
        // motion from here on, instead of trying to "catch up" a gap
        // that's already been zeroed by the cache restore above.
        if (followed != null) followedLastPos = followed.transform.position;
        else if (followedShip != null) followedLastPos = followedShip.transform.position;
    }

    public void CloseMap()
    {
        // Snapshot the currently-followed target's view BEFORE we close —
        // body position and camera position are both valid right now. Doing
        // it post-close (when time has passed and the body has orbited away)
        // would store a stale offset and break restore-on-reopen.
        RememberCurrentView();

        isOpen = false;
        legendCursor = -1;
        PlayerController.isMapOpen = false;
        // Highlight ring persists across close — switch its sizing reference to the player camera so the
        // ping stays visible at the right apparent size in gameplay.
        if (highlightRing != null) highlightRing.viewCamera = mainCamera;

        Cursor.lockState = prevCursorLock;
        Cursor.visible = prevCursorVisible;

        if (hiddenCanvases != null)
        {
            for (int i = 0; i < hiddenCanvases.Length; i++)
            {
                if (hiddenCanvases[i] != null)
                    hiddenCanvases[i].enabled = hiddenCanvasesPrev[i];
            }
            hiddenCanvases = null;
            hiddenCanvasesPrev = null;
        }
        if (legendCanvas != null) legendCanvas.enabled = false;

        if (mainCamera != null) mainCamera.enabled = true;
        if (mapCamera != null) mapCamera.enabled = false;

        if (endless != null) endless.PostFloatingOriginUpdate -= OnFloatingOriginShift;

        // Orbit lines: silence rendering while map is closed but keep the
        // user's Visible state intact, so reopening the map restores the
        // toggle without forcing a re-click.
        if (orbitLines != null) orbitLines.SetMapOpen(false);

        if (velocityHud != null) velocityHud.SetVisible(false);
        if (teleportButton != null) teleportButton.SetMapOpen(false);
        if (MapTutorial.Instance != null) MapTutorial.Instance.OnMapClosed();

        // Keep `followed` and `followedShip` intact across close so the next
        // OpenMap can re-frame on the same target. LateUpdate is gated on
        // `isOpen`, so persisting these fields doesn't cause any actual
        // tracking work while the map is closed.
    }

    /// Toggled by the legend's "ORBIT LINES" button. Recomputes on each Show
    /// from current physics state, so toggling refreshes the prediction.
    public bool ToggleOrbitLines()
    {
        if (orbitLines == null) return false;
        orbitLines.Toggle();
        return orbitLines.Visible;
    }

    void OnFloatingOriginShift()
    {
        if (!isOpen || playerRb == null || mapCamera == null) return;
        Vector3 cur = playerRb.position;
        Vector3 shift = cur - prevPlayerPos;
        // A floating-origin shift produces a sudden large negative delta (~-prevPos).
        // Natural movement is small. Use a sanity threshold.
        if (shift.sqrMagnitude > 500f * 500f)
        {
            mapCamera.transform.position += shift;
            // followedLastPos is in world space — shift it whether we're
            // tracking a body or a ship. Forgetting the ship branch makes
            // the next LateUpdate compute (post-shift cur - pre-shift last)
            // and jolt the camera by an extra full shift amount.
            if (followed != null || followedShip != null) followedLastPos += shift;
        }
        prevPlayerPos = cur;
    }

    void LateUpdate()
    {
        if (!isOpen) return;

        // ── Controller D-pad legend navigation ──────────────────────────
        // D-pad up/down moves the highlight through legend entries, A
        // activates the highlighted entry (same path as a mouse-clicked
        // legend button — first press marks ring, second press focuses).
        // Joystick continues to fly the map camera (MapCameraRig.Tick).
        if (legendUI != null && legendUI.Count > 0 && TutorialGate.ControllerEnabled)
        {
            bool dUp   = TutorialGate.DPadDirectionPressed(0);
            bool dDown = TutorialGate.DPadDirectionPressed(2);
            if (dUp || dDown)
            {
                if (legendCursor < 0)
                {
                    int seed = legendUI.IndexOfSelected();
                    legendCursor = seed >= 0 ? seed : 0;
                }
                else
                {
                    int delta = dDown ? 1 : -1;
                    legendCursor = (legendCursor + delta + legendUI.Count) % legendUI.Count;
                }
                legendUI.HighlightIndex(legendCursor);
            }

            if (TutorialGate.PadPressed(TutorialGate.PadButton.A) && legendCursor >= 0)
            {
                var body = legendUI.GetBody(legendCursor);
                if (body != null) OnLegendClick(body);
            }
        }

        // Mouse click-to-follow against real planet colliders (Body layer).
        // Suppressed for controller A presses while a legend entry is
        // highlighted — that A is consumed by the legend nav above.
        // Also suppressed for one frame after a legend button fires its
        // onClick, so e.g. the ORBIT LINES toggle doesn't bleed through
        // and accidentally unfollow the currently-locked planet.
        bool controllerLegendActive =
            legendUI != null && legendCursor >= 0
            && TutorialGate.PadPressed(TutorialGate.PadButton.A);
        bool legendClickedThisFrame = _suppressWorldClickFrame == Time.frameCount;
        if (TutorialGate.PrimaryActionPressed() && !controllerLegendActive && !legendClickedThisFrame
            && mapCamera != null && !IsPointerOverLegend())
        {
            Ray ray = mapCamera.ScreenPointToRay(Input.mousePosition);
            CelestialBody hitBody = null;
            if (Physics.Raycast(ray, out RaycastHit hit, mapCamera.farClipPlane, bodyLayerMask))
                hitBody = hit.collider.GetComponentInParent<CelestialBody>();

            if (hitBody != null)
            {
                if (followed != hitBody)
                {
                    followed = hitBody;
                    // Seed from transform.position (interpolated visual pose), not
                    // rb.position — see follow-delta block below for the jitter
                    // reasoning.
                    followedLastPos = hitBody.transform.position;
                    OnVelocityMatched?.Invoke(hitBody);
                    if (velocityHud != null) velocityHud.ShowMatched(hitBody);
                }
            }
            else
            {
                if (followed != null)
                {
                    OnVelocityUnmatched?.Invoke();
                    if (velocityHud != null) velocityHud.ShowUnmatched();
                }
                followed = null;
            }
        }

        if (TutorialGate.CancelPressed())
            followed = null;

        if (cameraRig != null) cameraRig.Tick();

        // Track whichever target is currently followed — body, ship, OR player.
        Transform followedT = followed != null ? followed.transform
                              : (followedShip != null ? followedShip.transform
                              : (followedPlayer != null ? followedPlayer : null));
        if (followedT != null && mapCamera != null)
        {
            // Read the interpolated visual pose rather than rb.position. Planet
            // rigidbodies move at the 100 Hz physics step, so reading rb.position
            // each LateUpdate produces a quantized delta (0 most frames, full
            // step every 1/100s). The renderer uses transform.position which is
            // interpolated — mirroring it here keeps the camera locked to the
            // visual pose so there's no relative jitter at close zoom.
            Vector3 cur = followedT.position;
            Vector3 delta = cur - followedLastPos;
            mapCamera.transform.position += delta;
            followedLastPos = cur;
        }

        // Draw the lock-on brackets around the marked body via mapCamera —
        // visually identical to the in-cockpit lock-on so the player gets
        // continuity across views. The 2-circle MapHighlightRing is hidden
        // for body markings while these brackets do the job; ship markings
        // still use the ring (different visual semantics).
        if (pendingHighlight != null && mapCamera != null)
        {
            if (_mapLockOnUI == null)
            {
                var hud = FindObjectOfType<ShipHUD>(true);
                if (hud != null) _mapLockOnUI = hud.GetComponent<LockOnUI>();
            }
            if (_mapLockOnUI != null)
                _mapLockOnUI.DrawLockOnUI(pendingHighlight, true, mapCamera);
        }
    }

    LockOnUI _mapLockOnUI;

    public Ship FollowedShip => followedShip;
    public CelestialBody PendingHighlight => pendingHighlight;

    // Tracks the frame on which a legend UI button was clicked so the
    // world-click handler in LateUpdate (same frame) can skip itself.
    // IsPointerOverGameObject() should already cover this, but the
    // legend canvas's raycaster can be transiently disabled by other
    // modal-suppression code, leaking world clicks through legend UI.
    int _suppressWorldClickFrame = -1;
    /// Public hook: any legend button's onClick should call this so the
    /// world-LMB handler skips itself in the same frame. Prevents
    /// "click orbit toggle → followed body clears + planet flies away".
    public void SuppressWorldClickThisFrame() { _suppressWorldClickFrame = Time.frameCount; }

    Ship[] GatherDishedShips()
    {
        var all = FindObjectsOfType<Ship>();
        var keep = new System.Collections.Generic.List<Ship>(all.Length);
        for (int i = 0; i < all.Length; i++)
        {
            var s = all[i];
            if (s == null) continue;
            var detach = s.GetComponent<ThrusterDetachOnImpact>();
            if (detach == null) continue;
            if (detach.HasDishAttached) keep.Add(s);
        }
        // Stable order: by BoughtShip.shipNumber asc. Ships without a marker
        // (main ship, if it ever has a dish) sort first.
        keep.Sort((a, b) =>
        {
            int ai = a.GetComponent<BoughtShip>() is BoughtShip am ? am.shipNumber : 0;
            int bi = b.GetComponent<BoughtShip>() is BoughtShip bm ? bm.shipNumber : 0;
            return ai.CompareTo(bi);
        });
        return keep.ToArray();
    }

    /// Closes the map and immediately puts the player in the followed ship's
    /// pilot seat. Called by the top-middle "TELEPORT TO PILOT" button when
    /// a ship has been clicked-to-focus in the legend.
    public void TeleportToFollowedShipPilot()
    {
        var target = followedShip;
        if (target == null) return;

        // Already in this ship's cockpit — no-op + nudge so the player
        // knows the click landed but did nothing.
        if (target.IsPiloted)
        {
            if (FlightAssistStatusHUD.Instance != null)
                FlightAssistStatusHUD.Instance.ShowToast("Already piloting ship", 1.8f);
            return;
        }

        // Build a HAL boarding line BEFORE we close the map and PilotShip
        // runs — we want the line to reflect the ship's state at teleport
        // time, not whatever transient state-flicker happens while the
        // pilot transition unfolds.
        string boardingLine = BuildBoardingLine(target);

        // Close the map first so HUDs are restored, then enter pilot. Order
        // matters: PilotShip disables the player gameObject, but the map
        // close path expects the player active for cursor / state restoration.
        CloseMap();
        target.PilotShip();
        // Force the ship's flight-controls Interactable into "player is in
        // my trigger zone" state. The teleport bypassed the OnTriggerEnter
        // path, so without this the F-press to exit the cockpit wouldn't be
        // recognized (Interactable.Update gates Interact() on the in-zone
        // flag) and the player would be stuck piloting.
        if (target.flightControls != null)
            target.flightControls.ForcePlayerInInteractionZone();

        // HAL boarding-confirmation line. Pushed last so the close-map
        // dust settles before HALLineHUD's queue takes the next line.
        if (!string.IsNullOrEmpty(boardingLine) && HALCommentator.Instance != null)
            HALCommentator.Instance.VolunteerExternal(boardingLine);
    }

    // Builds "Boarding Ship N, currently orbiting <planet> at X.XX km/s." or
    // a sensible variant for ships not in orbit. Number reads from the
    // BoughtShip marker (0 for the scene's original ship); planet + speed
    // computed body-relative the same way FleetTelemetry does so the line
    // matches what the AI would report on the same turn.
    static string BuildBoardingLine(Ship ship)
    {
        if (ship == null) return null;
        var marker = ship.GetComponent<BoughtShip>();
        int n = marker != null ? marker.shipNumber : 0;

        var rb = ship.GetComponent<Rigidbody>();
        Vector3 pos = ship.transform.position;
        Vector3 worldV = rb != null ? rb.velocity : Vector3.zero;

        CelestialBody nearest = null;
        float bestDist = float.MaxValue;
        var bodies = NBodySimulation.Bodies;
        if (bodies != null)
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                var b = bodies[i];
                if (b == null) continue;
                float d = Vector3.Distance(pos, b.Position);
                if (d < bestDist) { bestDist = d; nearest = b; }
            }
        }

        // Same orbit classification thresholds as FleetTelemetry.
        if (nearest != null && bestDist <= nearest.radius * 5f)
        {
            Vector3 relV = worldV - nearest.velocity;
            float speedKms = relV.magnitude / 1000f;
            if (bestDist <= nearest.radius * 1.05f && relV.magnitude < 5f)
                return $"Boarding Ship {n}, parked on {nearest.bodyName}.";
            return $"Boarding Ship {n}, currently orbiting {nearest.bodyName} at {speedKms:0.00} km/s.";
        }
        return $"Boarding Ship {n}, currently in deep space.";
    }

    /// Legend click on a body is now pure navigation: snap the map camera
    /// onto the body and lock onto its velocity so the camera rides with
    /// the planet. The "marked planet" state (lock-on brackets, shared
    /// with the in-cockpit ShipHUD) is owned by the LMB-on-world-body
    /// path and the ship's LMB handler — clicking the legend row no
    /// longer touches pendingHighlight or the legend's selection pip
    /// state machine.
    public void OnLegendClick(CelestialBody body)
    {
        if (body == null) return;
        SuppressWorldClickThisFrame();
        OnLegendBodyClicked?.Invoke(body);
        OnLegendBodyMarked?.Invoke(body); // map-tutorial step still wants this signal
        FocusOn(body);
        if (legendUI != null) legendUI.SetSelected(body); // visual hint: this row is what the camera is following
    }

    /// Mark a body as the "selected planet" — single source of truth shared
    /// by the in-cockpit ShipHUD lock-on AND the map legend / highlight ring.
    /// Pass null to clear. ShipHUD's LMB-on-planet handler calls this so the
    /// player's in-flight selection persists when they open the map.
    public void SetMarkedBody(CelestialBody body)
    {
        // Body and ship highlights are mutually exclusive.
        pendingHighlightShip = null;
        followedShip = null;
        followedPlayer = null;
        pendingHighlight = body;
        if (legendUI != null)
        {
            legendUI.SetSelected(body);
            legendUI.SetShipSelected(null);
        }
    }

    /// Legend click on a ship is now pure navigation: focus the map
    /// camera onto the ship and lock onto its velocity. The "TELEPORT TO
    /// PILOT" banner still appears via the followedShip state set inside
    /// FocusOnShip, so the player can still get into the cockpit from
    /// here — just without the previous mark→focus→unmark cycle.
    public void OnLegendShipClick(Ship ship)
    {
        if (ship == null) return;
        SuppressWorldClickThisFrame();
        FocusOnShip(ship);
        if (legendUI != null) legendUI.SetShipSelected(ship);
    }

    /// Like FocusOn(CelestialBody) but for a Ship. Frames the map camera on
    /// the ship and sets it as the followed target so the camera tracks it.
    // Snapshot the currently-focused target's camera state into the cache
    // before switching. Called by both Focus paths so the player's adjusted
    // view of A is preserved when they switch to B and back.
    void RememberCurrentView()
    {
        if (mapCamera == null) return;
        if (followed != null)
        {
            _bodyViewCache[followed] = new CameraView
            {
                offset = mapCamera.transform.position - followed.Position,
                rotation = mapCamera.transform.rotation,
            };
        }
        else if (followedShip != null)
        {
            _shipViewCache[followedShip] = new CameraView
            {
                offset = mapCamera.transform.position - followedShip.transform.position,
                rotation = mapCamera.transform.rotation,
            };
        }
    }

    public void FocusOnShip(Ship ship)
    {
        if (ship == null || mapCamera == null) return;

        RememberCurrentView();

        // If we've previously focused this ship, restore the adjusted view
        // instead of computing default lit-side framing.
        if (_shipViewCache.TryGetValue(ship, out var saved))
        {
            mapCamera.transform.position = ship.transform.position + saved.offset;
            mapCamera.transform.rotation = saved.rotation;
            if (cameraRig != null) cameraRig.Activate();
            followed = null;
            followedShip = ship;
            followedPlayer = null;
            followedLastPos = ship.transform.position;
            return;
        }

        Vector3 anchorPos = ship.transform.position;
        // Use the nearest CelestialBody (its host planet/sun) to position the
        // map camera on the lit side, same trick as FocusOn(body).
        CelestialBody host = null;
        float bestSqr = float.MaxValue;
        var bodyList = NBodySimulation.Bodies;
        if (bodyList != null)
        {
            for (int i = 0; i < bodyList.Length; i++)
            {
                var b = bodyList[i];
                if (b == null) continue;
                float d = (b.Position - anchorPos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; host = b; }
            }
        }

        Vector3 viewDir;
        if (host != null && sunBody != null && host != sunBody)
        {
            Vector3 sunDir = (sunBody.Position - anchorPos).normalized;
            Vector3 right = Vector3.Cross(sunDir, Vector3.up);
            Vector3 up = Vector3.Cross(right, sunDir);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            viewDir = (sunDir * 0.85f + up.normalized * 0.45f).normalized;
        }
        else
        {
            viewDir = (mapCamera.transform.position - anchorPos);
            if (viewDir.sqrMagnitude < 0.01f) viewDir = -mapCamera.transform.forward;
            viewDir = viewDir.normalized;
        }

        // Ships are tiny relative to planets — pick a small fixed distance.
        const float kShipFramingDistance = 30f;
        mapCamera.transform.position = anchorPos + viewDir * kShipFramingDistance;
        mapCamera.transform.LookAt(anchorPos);
        if (cameraRig != null) cameraRig.Activate();
        followed = null;
        followedShip = ship;
        followedPlayer = null;
        followedLastPos = anchorPos;
    }

    /// Frames the map camera on the player AND sets followedPlayer so the
    /// camera continues to track the player every frame (matches the
    /// player's world-space velocity, like FocusOnShip does for ships).
    /// The anchor is the player's position plus 10 m in their forward
    /// direction so the area they are currently looking at is in frame.
    public void FocusOnPlayer()
    {
        if (mapCamera == null) return;
        var pc = FindObjectOfType<PlayerController>();
        if (pc == null) return;

        RememberCurrentView();

        // 10 m in front of the player. Player's transform.forward respects
        // their current look direction.
        Vector3 anchorPos = pc.transform.position + pc.transform.forward * 10f;

        // Frame from the same "lit side" trick FocusOnShip uses, but the
        // player is tiny, so a small distance is fine.
        CelestialBody host = null;
        float bestSqr = float.MaxValue;
        var bodyList = NBodySimulation.Bodies;
        if (bodyList != null)
        {
            for (int i = 0; i < bodyList.Length; i++)
            {
                var b = bodyList[i];
                if (b == null) continue;
                float d = (b.Position - anchorPos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; host = b; }
            }
        }

        Vector3 viewDir;
        if (host != null && sunBody != null && host != sunBody)
        {
            Vector3 sunDir = (sunBody.Position - anchorPos).normalized;
            Vector3 right = Vector3.Cross(sunDir, Vector3.up);
            Vector3 up = Vector3.Cross(right, sunDir);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            viewDir = (sunDir * 0.85f + up.normalized * 0.45f).normalized;
        }
        else
        {
            viewDir = (mapCamera.transform.position - anchorPos);
            if (viewDir.sqrMagnitude < 0.01f) viewDir = -mapCamera.transform.forward;
            viewDir = viewDir.normalized;
        }

        const float kPlayerFramingDistance = 25f;
        mapCamera.transform.position = anchorPos + viewDir * kPlayerFramingDistance;
        mapCamera.transform.LookAt(anchorPos);
        if (cameraRig != null) cameraRig.Activate();

        // followedPlayer drives the per-frame delta-track in Update so the
        // camera stays glued to the player as they move (velocity match).
        // followedLastPos must seed to the PLAYER position, not the offset
        // anchor — the per-frame block reads followedT.position which is
        // the raw player transform, so seeding from the anchor would
        // produce a one-frame jump on next tick.
        followed = null;
        followedShip = null;
        followedPlayer = pc.transform;
        followedLastPos = pc.transform.position;
    }

    /// Legend "PLAYER" click handler — mirror of OnLegendShipClick.
    public void OnLegendPlayerClick()
    {
        SuppressWorldClickThisFrame();
        FocusOnPlayer();
        if (legendUI != null) legendUI.SetShipSelected(null);
    }

    public void FocusOn(CelestialBody body)
    {
        if (body == null || mapCamera == null) return;

        RememberCurrentView();

        // Restore a previously-tuned view if we have one cached.
        if (_bodyViewCache.TryGetValue(body, out var saved))
        {
            mapCamera.transform.position = body.Position + saved.offset;
            mapCamera.transform.rotation = saved.rotation;
            if (cameraRig != null) cameraRig.Activate();
            followed = body;
            followedShip = null;
            followedPlayer = null;
            followedLastPos = body.transform.position;
            return;
        }

        Vector3 viewDir;
        if (body == sunBody || sunBody == null)
        {
            // Looking at the sun itself, or no sun reference: keep current viewing angle.
            viewDir = (mapCamera.transform.position - body.Position);
            if (viewDir.sqrMagnitude < 0.01f) viewDir = -mapCamera.transform.forward;
            viewDir = viewDir.normalized;
        }
        else
        {
            // Place the camera between the sun and the planet so the lit hemisphere faces us.
            // Bias slightly upward for a more cinematic 3/4 lit angle instead of dead-center solar eclipse view.
            Vector3 sunDir = (sunBody.Position - body.Position).normalized;
            Vector3 right = Vector3.Cross(sunDir, Vector3.up);
            Vector3 up = Vector3.Cross(right, sunDir);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            viewDir = (sunDir * 0.85f + up.normalized * 0.45f).normalized;
        }

        float dist = Mathf.Max(body.radius * focusDistanceMultiplier, body.radius + 50f);
        mapCamera.transform.position = body.Position + viewDir * dist;
        mapCamera.transform.LookAt(body.Position);
        if (cameraRig != null) cameraRig.Activate();
        followed = body;
        followedShip = null;
        followedPlayer = null;
        followedLastPos = body.transform.position;
    }

    bool IsPointerOverLegend()
    {
        if (UnityEngine.EventSystems.EventSystem.current == null) return false;
        return UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
    }
}
