using System.Collections.Generic;
using UnityEngine;

// Legacy TutorialStep subclasses — no longer instantiated by BuildDefault()
// or BonusTutorial, but kept because TutorialManager.ApplyState resolves
// saved tutorial progress by type name. Moved here verbatim to keep the
// active TutorialSteps.cs focused. Do NOT rename or delete these types —
// old save files reference them by name.

class PostCrashExamStep : TutorialStep
{
    // The same Tab keypress that completes this step also satisfies
    // TutorialGate.TutorialAdvancePressed in the same Update tick, so the
    // step advances on the very same frame — the auto-skip "AutoSkip in 3"
    // line never gets a chance to render.
    public override string Tip =>
        "This is your post-crash examination.\nPress <b>TAB</b> to allow us to distribute your test scores with third parties.";
    public override void Tick()
    {
        if (Input.GetKeyDown(KeyCode.Tab)) IsComplete = true;
    }
}

class StandUpStep : TutorialStep
{
    public override string Tip => $"Press {PromptGlyphs.Interact} to stand up.";
    public override void OnEnter() { TutorialGate.Unlock(TutorialAbility.ExitPilot); }
    public override void Tick()
    {
        if (Input.GetKeyDown(KeyCode.F) ||
            TutorialGate.PadPressed(TutorialGate.PadButton.X)) IsComplete = true;
    }
}

class MouseLookStep : TutorialStep
{
    const float Threshold = 50f;
    float accumulated;
    public override string Tip => $"Use the {PromptGlyphs.MouseLook} to look around.";
    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.MouseLook);
        accumulated = 0f;
    }
    public override void Tick()
    {
        accumulated += Mathf.Abs(Input.GetAxisRaw("Mouse X")) + Mathf.Abs(Input.GetAxisRaw("Mouse Y"));
        if (TutorialGate.ControllerEnabled)
        {
            // Right stick produces a steady -1..1 reading per frame (after the
            // deadzone in InputManager.asset). Sample it directly so a held
            // stick deflection accumulates toward the threshold the same way
            // continuous mouse motion does. Scale by deltaTime * 60 so the
            // controller feel matches roughly one second of full-stick push.
            accumulated += (Mathf.Abs(TutorialGate.RightStickX()) + Mathf.Abs(TutorialGate.RightStickY()))
                           * Time.unscaledDeltaTime * 60f;
        }
        if (accumulated >= Threshold) IsComplete = true;
    }
}

class MoveStep : TutorialStep
{
    bool walkedSeen, sprintSeen;
    PlayerController player;
    public override string Tip => $"Use {PromptGlyphs.Move} to move.\nHold {PromptGlyphs.Sprint} to sprint.";
    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.Move);
        walkedSeen = sprintSeen = false;
        player = Object.FindObjectOfType<PlayerController>();
    }
    public override void Tick()
    {
        if (player == null) player = Object.FindObjectOfType<PlayerController>();
        if (player == null) return;
        if (player.IsOnGround && AnyWASD()) walkedSeen = true;
        // Sprint = LeftShift OR L-stick click.
        bool sprintHeld = Input.GetKey(KeyCode.LeftShift) ||
            TutorialGate.PadHeld(TutorialGate.PadButton.L3);
        if (player.IsOnGround && AnyWASD() && sprintHeld) sprintSeen = true;
        if (walkedSeen && sprintSeen) IsComplete = true;
    }
}

class HatchStep : TutorialStep
{
    HatchButton hatch;
    UnityEngine.Events.UnityAction listener;
    public override string Tip => "Press the <b>red hatch button</b> to open the hatch.";
    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.InteractHatch);
        hatch = Object.FindObjectOfType<HatchButton>();
        if (hatch != null)
        {
            listener = () => IsComplete = true;
            hatch.interactEvent.AddListener(listener);
        }
    }
    public override void Tick()
    {
        // listener flips IsComplete; nothing to poll
    }
    public override void OnExit()
    {
        if (hatch != null && listener != null) hatch.interactEvent.RemoveListener(listener);
    }
}

class JumpStep : TutorialStep
{
    PlayerController player;
    public override string Tip => $"Press {PromptGlyphs.Jump} to jump.";
    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.Jump);
        player = Object.FindObjectOfType<PlayerController>();
    }
    public override void Tick()
    {
        if (player == null) player = Object.FindObjectOfType<PlayerController>();
        if (player == null) return;
        bool jumpDown = Input.GetKeyDown(KeyCode.Space) ||
            TutorialGate.PadPressed(TutorialGate.PadButton.A);
        if (jumpDown && player.IsOnGround) IsComplete = true;
    }
}

class BoostStep : TutorialStep
{
    PlayerController player;
    public override string Tip => $"While airborne, press {PromptGlyphs.Jump} again to boost upward.";
    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.Boost);
        player = Object.FindObjectOfType<PlayerController>();
    }
    public override void Tick()
    {
        if (player == null) player = Object.FindObjectOfType<PlayerController>();
        if (player == null) return;
        bool jumpDown = Input.GetKeyDown(KeyCode.Space) ||
            TutorialGate.PadPressed(TutorialGate.PadButton.A);
        if (jumpDown && !player.IsOnGround) IsComplete = true;
    }
}

class DirectionalThrustStep : TutorialStep
{
    PlayerController player;
    public override string Tip => $"Jump, then hold {PromptGlyphs.DirThrustHold} to thrust in that direction.";
    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.DirectionalThrust);
        player = Object.FindObjectOfType<PlayerController>();
    }
    public override void Tick()
    {
        if (player == null) player = Object.FindObjectOfType<PlayerController>();
        if (player == null) return;
        // Match the gameplay binding for directional thrust: LeftShift OR
        // L3 (left-stick click). When the player uses L3 mid-air with stick
        // input, that's directional thrust — the tutorial must recognise it
        // exactly the same way the actual mechanic does, otherwise the step
        // can't be completed on controller.
        bool dirThrustHeld = Input.GetKey(KeyCode.LeftShift) ||
            TutorialGate.PadHeld(TutorialGate.PadButton.L3);
        if (!player.IsOnGround && dirThrustHeld && AnyWASD()) IsComplete = true;
    }
}

class DownThrustStep : TutorialStep
{
    PlayerController player;
    public override string Tip => $"Press {PromptGlyphs.DownThrust} mid-air to thrust downward.";
    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.DownThrust);
        player = Object.FindObjectOfType<PlayerController>();
    }
    public override void Tick()
    {
        if (player == null) player = Object.FindObjectOfType<PlayerController>();
        if (player == null) return;
        bool downThrustDown = Input.GetKeyDown(KeyCode.LeftControl) ||
            TutorialGate.PadPressed(TutorialGate.PadButton.R3);
        if (downThrustDown && !player.IsOnGround) IsComplete = true;
    }
}

class FlashlightStep : TutorialStep
{
    public override string Tip => $"Press {PromptGlyphs.Flashlight} to toggle flashlight, adjust brightness with scrollwheel.";
    public override void OnEnter() { TutorialGate.Unlock(TutorialAbility.Flashlight); }
    public override void Tick()
    {
        if (Input.GetKeyDown(KeyCode.E) ||
            TutorialGate.PadPressed(TutorialGate.PadButton.Y)) IsComplete = true;
    }
}

class MapStep : TutorialStep
{
    bool everOpened;
    public override string Tip => $"Press {PromptGlyphs.Map} to use the map.";
    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.Map);
        everOpened = false;
    }
    public override void Tick()
    {
        // The step is "open the map then close it again". SolarSystemMapController flips
        // PlayerController.isMapOpen on each press; we just observe the rising and falling edges.
        if (PlayerController.isMapOpen) everOpened = true;
        else if (everOpened) IsComplete = true;
    }
}

class RepairShipStep : TutorialStep
{
    const string ExtraHintLine = "\n<size=80%><i>* Tip — Use the back hatch to lift yourself onto top of the ship</i></size>";

    ThrusterDetachOnImpact damage;
    public override string Tip =>
        "Repair your ship.\nCollect and reattach all 4 parts.\n<b>0/4</b> attached." + ExtraHintLine;

    public override void OnEnter() { TutorialGate.Unlock(TutorialAbility.Pickup); damage = null; }

    public override void Tick()
    {
        // ThrusterDetachOnImpact lives on the active ship instance, which may
        // be destroyed and respawned — re-find lazily when ours dies. Unity's
        // overloaded == handles destroyed instances so this catches the swap.
        if (damage == null) damage = Object.FindObjectOfType<ThrusterDetachOnImpact>();
        if (damage == null) return;

        bool leftOK  = damage.leftThrusterChild  == null || damage.leftThrusterChild.activeSelf;
        bool rightOK = damage.rightThrusterChild == null || damage.rightThrusterChild.activeSelf;
        bool dishOK  = damage.dishChild          == null || damage.dishChild.activeSelf;
        bool solarOK = damage.solarPanelChild    == null || damage.solarPanelChild.activeSelf;

        int count = (leftOK ? 1 : 0) + (rightOK ? 1 : 0) + (dishOK ? 1 : 0) + (solarOK ? 1 : 0);

        if (TutorialUI.Instance != null)
            TutorialUI.Instance.SetTip(
                $"Repair your ship.\nCollect and reattach all 4 parts.\n<b>{count}/4</b> attached." + ExtraHintLine);

        if (leftOK && rightOK && dishOK && solarOK) IsComplete = true;
    }
}

class LebronLightStep : TutorialStep
{
    SunlightControlButton button;
    UnityEngine.Events.UnityAction listener;
    public override string Tip => "Interact with the second red button to activate lebron light.";
    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.InteractSunlight);
        TrySubscribe();
    }
    public override void Tick()
    {
        // Retry once per frame if the button wasn't in the scene at OnEnter
        // (it might be spawned later). TrySubscribe is idempotent — it
        // only attaches the listener if we haven't already subscribed,
        // preventing duplicate listeners that would fire IsComplete = true
        // multiple times.
        if (button == null) TrySubscribe();
    }
    void TrySubscribe()
    {
        if (listener != null) return; // already subscribed once
        button = Object.FindObjectOfType<SunlightControlButton>();
        if (button == null) return;
        listener = () => IsComplete = true;
        button.interactEvent.AddListener(listener);
    }
    public override void OnExit()
    {
        if (button != null && listener != null) button.interactEvent.RemoveListener(listener);
        button = null;
        listener = null;
    }
}

class BackHatchStep : TutorialStep
{
    BackHatchButton button;
    UnityEngine.Events.UnityAction listener;
    public override string Tip => "Interact with the third red button to toggle the back hatch.";
    public override void OnEnter()
    {
        TrySubscribe();
    }
    public override void Tick()
    {
        // Idempotent retry — BackHatchButton may not be in the scene yet at
        // OnEnter (e.g. ship prefab swap mid-tutorial). Same pattern as
        // LebronLightStep.
        if (button == null) TrySubscribe();
    }
    void TrySubscribe()
    {
        if (listener != null) return;
        button = Object.FindObjectOfType<BackHatchButton>();
        if (button == null) return;
        listener = () => IsComplete = true;
        button.interactEvent.AddListener(listener);
    }
    public override void OnExit()
    {
        if (button != null && listener != null) button.interactEvent.RemoveListener(listener);
        button = null;
        listener = null;
    }
}

class TalkToNPCsStep : TutorialStep
{
    const int Required = 3;
    readonly System.Collections.Generic.HashSet<int> _talkedTo = new System.Collections.Generic.HashSet<int>();
    System.Action<MonoBehaviour> _handler;

    // Auto-skip would otherwise fire while the player is still mid-dialogue
    // with the third NPC (the step satisfies on conversation start). The
    // performance-review modal then pops over the dialogue UI, fighting it
    // for cursor + input. Force the player to press TAB explicitly.
    public override bool BlocksAutoSkip => true;

    public override string Tip => $"Talk to at least 3 NPCs.\n<b>{_talkedTo.Count}/{Required}</b> talked to.";

    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.TalkToNPC);
        _talkedTo.Clear();
        _handler = npc =>
        {
            if (npc == null) return;
            _talkedTo.Add(npc.GetInstanceID());
        };
        NPCConversationTracker.OnConversationStarted += _handler;
    }

    public override void Tick()
    {
        if (TutorialUI.Instance != null)
            TutorialUI.Instance.SetTip($"Talk to at least 3 NPCs.\n<b>{_talkedTo.Count}/{Required}</b> talked to.");
        if (_talkedTo.Count >= Required) IsComplete = true;
    }

    public override void OnExit()
    {
        if (_handler != null) NPCConversationTracker.OnConversationStarted -= _handler;
        _handler = null;
    }
}

// Helper: lazy-cached ship lookup. The active Ship instance may be destroyed
// (e.g. mid-step) so each step caches lazily and re-finds when its cached ref
// becomes null (Unity's overloaded == handles destroyed objects).
static class ShipFinder
{
    public static Ship Get(ref Ship cached)
    {
        if (cached == null) cached = Object.FindObjectOfType<Ship>();
        return cached;
    }
}

class PilotShipStep : TutorialStep
{
    Ship ship;
    public override string Tip => $"Press {PromptGlyphs.Interact} in the pilot seat to fly the ship.";
    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.EnterPilot);
        TutorialGate.Unlock(TutorialAbility.ShipMouseLook);
        ship = null;
    }
    public override void Tick()
    {
        var s = ShipFinder.Get(ref ship);
        if (s != null && s.IsPiloted) IsComplete = true;
    }
}

class ShipUpThrustStep : TutorialStep
{
    Ship ship;
    public override string Tip => $"Hold {PromptGlyphs.Jump} for upward thrust.";
    public override void OnEnter() { TutorialGate.Unlock(TutorialAbility.ShipUpThrust); ship = null; }
    public override void Tick()
    {
        var s = ShipFinder.Get(ref ship);
        if (s == null || !s.IsPiloted) return;
        bool up = Input.GetKey(KeyCode.Space) ||
            TutorialGate.PadHeld(TutorialGate.PadButton.A);
        if (up) IsComplete = true;
    }
}

class ShipMoveStep : TutorialStep
{
    Ship ship;
    public override string Tip => $"Use {PromptGlyphs.Move} to fly the ship.";
    public override void OnEnter() { TutorialGate.Unlock(TutorialAbility.ShipMove); ship = null; }
    public override void Tick()
    {
        var s = ShipFinder.Get(ref ship);
        if (s != null && s.IsPiloted && AnyWASD()) IsComplete = true;
    }
}

class ShipDownThrustStep : TutorialStep
{
    Ship ship;
    public override string Tip => $"Hold {PromptGlyphs.DownThrust} for downward thrust.";
    public override void OnEnter() { TutorialGate.Unlock(TutorialAbility.ShipDownThrust); ship = null; }
    public override void Tick()
    {
        var s = ShipFinder.Get(ref ship);
        if (s == null || !s.IsPiloted) return;
        bool down = Input.GetKey(KeyCode.LeftControl) ||
            TutorialGate.PadHeld(TutorialGate.PadButton.R3);
        if (down) IsComplete = true;
    }
}

class ShipRollStep : TutorialStep
{
    Ship ship;
    public override string Tip => $"Press {PromptGlyphs.RollLeft} / {PromptGlyphs.RollRight} to roll.";
    public override void OnEnter() { TutorialGate.Unlock(TutorialAbility.ShipRoll); ship = null; }
    public override void Tick()
    {
        var s = ShipFinder.Get(ref ship);
        if (s == null || !s.IsPiloted) return;
        bool rollKey = Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E);
        bool rollPad = TutorialGate.PadPressed(TutorialGate.PadButton.LB)
                    || TutorialGate.PadPressed(TutorialGate.PadButton.RB);
        if (rollKey || rollPad) IsComplete = true;
    }
}

// Player wakes up in the cabin — actually look around with the mouse to
// complete. Has a small startup delay (StartDelay) before checking input so
// the panel's swing-in animation + the first frames of typewriter reveal
// happen before the step can complete. Without that delay the step would
// register the player's mouse motion immediately, mark itself complete during
// swing-in, and auto-skip ~3 seconds later to step 2 before the player even
// finished reading tip 1.
class WakeUpLookStep : TutorialStep
{
    const float Threshold = 100f;
    const float StartDelay = 1.0f; // wait past swing-in (~0.7s) + a beat

    float accumulated;
    float elapsed;

    public override string Tip => $"Use the {PromptGlyphs.MouseLook} to look around.";

    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.MouseLook);
        accumulated = 0f;
        elapsed = 0f;
    }

    public override void Tick()
    {
        elapsed += Time.unscaledDeltaTime;
        if (elapsed < StartDelay) return;

        accumulated += Mathf.Abs(Input.GetAxisRaw("Mouse X")) + Mathf.Abs(Input.GetAxisRaw("Mouse Y"));
        if (TutorialGate.ControllerEnabled)
        {
            accumulated += (Mathf.Abs(TutorialGate.RightStickX()) + Mathf.Abs(TutorialGate.RightStickY()))
                           * Time.unscaledDeltaTime * 60f;
        }
        if (accumulated >= Threshold) IsComplete = true;
    }
}

// Walk anywhere — relaxed from the legacy MoveStep, no sprint requirement
// (Phase 1 doesn't introduce sprint). Teaches both move and jump in one tip
// since the cabin is small and we don't want the player to hit a separate
// JumpStep later when they're already comfortable. Both abilities unlock on
// enter so the tip's instructions actually work as the player reads them.
// Step completes once the player has done BOTH — walked at least once AND
// jumped at least once.
class WakeUpWalkStep : TutorialStep
{
    const float StartDelay = 0.5f;

    PlayerController player;
    float elapsed;
    bool walked;
    bool jumped;

    public override string Tip =>
        $"Use {PromptGlyphs.Move} to move. Press {PromptGlyphs.Jump} to jump.";

    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.Move);
        TutorialGate.Unlock(TutorialAbility.Jump);
        player = Object.FindObjectOfType<PlayerController>();
        elapsed = 0f;
        walked = false;
        jumped = false;
    }

    public override void Tick()
    {
        elapsed += Time.unscaledDeltaTime;
        if (elapsed < StartDelay) return;

        if (player == null) player = Object.FindObjectOfType<PlayerController>();
        if (player == null) return;

        if (!walked && player.IsOnGround && AnyWASD()) walked = true;

        // Jump detection: rising edge of the jump input WHILE the player was
        // on the ground last frame. Catching the press (not the airborne state
        // alone) means falling off a step doesn't satisfy this.
        if (!jumped)
        {
            bool jumpDown = Input.GetKeyDown(KeyCode.Space) ||
                (TutorialGate.ControllerEnabled && TutorialGate.PadPressed(TutorialGate.PadButton.A));
            if (jumpDown && player.IsOnGround) jumped = true;
        }

        if (walked && jumped) IsComplete = true;
    }
}

// Wait for the bobber to wiggle, then click to reel. Subscribes to
// FishingRodController.OnFishCaught (fires only on a successful catch, not
// on every reel attempt). BlocksAutoSkip because catching can take a while
// and we don't want the tutorial advancing while the player is mid-cast.
class CatchFirstFishStep : TutorialStep
{
    System.Action<float> _handler;

    public override bool BlocksAutoSkip => true;

    public override string Tip =>
        $"Wait for the bobber to wiggle, then {PromptGlyphs.PrimaryClick} to reel it in.\nYou have to be quick!";

    public override void OnEnter()
    {
        _handler = spin =>
        {
            EarlyGameProgress.FirstFishCaught = true;
            IsComplete = true;
        };
        FishingRodController.OnFishCaught += _handler;
    }

    public override void Tick() { }

    public override void OnExit()
    {
        if (_handler != null) FishingRodController.OnFishCaught -= _handler;
        _handler = null;
    }
}

// Catch a small quota of fish to get comfortable with the rod. Tip live-
// updates the counter via TutorialUI.SetTip (handled by TutorialManager.Update
// polling step.Tip). BlocksAutoSkip because catching takes time and we don't
// want the tutorial advancing mid-cast.
class CatchFiveFishStep : TutorialStep
{
    const int Required = 5;

    public override bool BlocksAutoSkip => true;

    public override string Tip
    {
        get
        {
            int count = FishInventory.Instance != null ? FishInventory.Instance.AllFish.Count : 0;
            int shown = Mathf.Min(count, Required);
            return $"Catch <b>{Required}</b> fish.\n<b>{shown}/{Required}</b> caught.";
        }
    }

    public override void Tick()
    {
        if (FishInventory.Instance == null) return;
        if (FishInventory.Instance.AllFish.Count >= Required)
        {
            // Reusing the existing OneOfEachCaught flag — same semantics for
            // downstream gates ("the player has caught enough fish to proceed").
            EarlyGameProgress.OneOfEachCaught = true;
            IsComplete = true;
        }
    }
}

// Adds a compass waypoint pointing at any GameObject tagged "Bonfire" and
// completes when the player gets within range. Same pattern as
// WalkToFishingBankStep.
class WalkToFireStep : TutorialStep
{
    const string BonfireTag = "Bonfire";
    const float ReachDistance = 5f;

    Transform _fire;
    PlayerController _player;

    public override string Tip => "Head over to the fire to cook your fish.";

    public override void OnEnter()
    {
        if (CompassHUD.Instance != null)
            CompassHUD.Instance.AddWaypointByTag("bonfire", BonfireTag, "Fire");
        var go = SafeFindWithTag(BonfireTag);
        if (go != null) _fire = go.transform;
        _player = Object.FindObjectOfType<PlayerController>();
    }

    public override void Tick()
    {
        if (_fire == null)
        {
            var go = SafeFindWithTag(BonfireTag);
            if (go == null) return;
            _fire = go.transform;
        }
        if (_player == null) _player = Object.FindObjectOfType<PlayerController>();
        if (_player == null) return;

        float dist = Vector3.Distance(_player.transform.position, _fire.position);
        if (dist <= ReachDistance) IsComplete = true;
    }

    public override void OnExit()
    {
        if (CompassHUD.Instance != null)
            CompassHUD.Instance.RemoveWaypoint("bonfire");
    }

    static GameObject SafeFindWithTag(string tag)
    {
        try { return GameObject.FindWithTag(tag); }
        catch (UnityException) { return null; }
    }
}

// Open the cook panel by pressing F at the bonfire. Subscribes to
// BonfireInteraction.OnPanelOpened (added in this round) — fires once per
// open. OnEnter unlocks TutorialAbility.TalkToNPC because BonfireInteraction
// gates F on that ability (same gate NPCs use for "press F to talk").
//
// AdvancesDuringModalUI = true: the next step (CookAndEatStep) tells the
// player how to USE the cook panel they just opened, so we want to advance
// to it WHILE the panel is still up — not wait for the player to close it.
class OpenCookPanelStep : TutorialStep
{
    System.Action _handler;

    public override bool AdvancesDuringModalUI => true;

    public override string Tip => $"Press {PromptGlyphs.Interact} at the fire to cook.";

    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.TalkToNPC);
        _handler = () => IsComplete = true;
        BonfireInteraction.OnPanelOpened += _handler;
    }

    public override void Tick() { }

    public override void OnExit()
    {
        if (_handler != null) BonfireInteraction.OnPanelOpened -= _handler;
        _handler = null;
    }
}

// Swing the axe for the first time. Tev's dialogue auto-equipped the axe at
// the end of TalkToTevStep, so the player just needs to click. OnEnter unlocks
// TutorialAbility.ChopAxe so the swing input actually fires the AxeController.
class MainSwingAxeStep : TutorialStep
{
    public override string Tip => $"Try out your new axe — {PromptGlyphs.PrimaryClick} to swing.";

    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.ChopAxe);
    }

    public override void Tick()
    {
        var axe = Object.FindObjectOfType<AxeController>();
        if (axe != null && axe.IsEquipped && TutorialGate.FirePressed())
            IsComplete = true;
    }
}

// Chop trees until the player has gathered enough wood for a cabin. Live
// counter updates the tip via TutorialUI.SetTip (handled by the manager
// polling step.Tip every frame). Threshold matches the cabin's woodCost so
// the player has exactly enough when this step ends.
class MainGatherWoodStep : TutorialStep
{
    const int Required = 50;

    public override bool BlocksAutoSkip => true;

    public override string Tip
    {
        get
        {
            int wood = WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;
            int shown = Mathf.Min(wood, Required);
            return $"Chop trees until you have <b>{Required}</b> wood.\n<b>{shown}/{Required}</b> gathered.";
        }
    }

    public override void Tick()
    {
        if (WoodInventory.Instance == null) return;
        if (WoodInventory.Instance.Wood >= Required) IsComplete = true;
    }
}

// Open the build menu (N key). OnEnter unlocks TutorialAbility.BuildMenu and
// also locks the menu down to cabin / torch / bonfire only — every other
// blueprint is hidden from the picker until Phase 7 finishes and unlocks all.
//
// AdvancesDuringModalUI = true so the next step (place a cabin) shows up
// while the build menu is still open — same pattern as OpenCookPanelStep.
class OpenBuildMenuStep : TutorialStep
{
    System.Action _handler;

    public override bool AdvancesDuringModalUI => true;

    public override string Tip => $"Press {PromptGlyphs.BuildMenu} to open the build menu.";

    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.BuildMenu);
        BuildMenuLock.LockAllExcept("Cabin", "Torch", "Bonfire");
        _handler = () => IsComplete = true;
        BuildMenuUI.OnOpened += _handler;
    }

    public override void Tick() { }

    public override void OnExit()
    {
        if (_handler != null) BuildMenuUI.OnOpened -= _handler;
        _handler = null;
    }
}

// Place a cabin AND close the build menu — merged into one step with two tip
// phases so the player sees a single "press N to close" prompt right after
// placing instead of an extra "step transition". Flow:
//   1. Tip: "Pick the Cabin and place it…"
//   2. Player places the cabin → Tip switches to "press N to close the build
//      menu". We also (a) lock cabin out of the picker so they can't place
//      another, (b) force GhostPlacement to exit placement after this one
//      placement, and (c) tell BuildMenuUI to re-open instead of returning
//      straight to gameplay — so the player is inside the menu with no
//      cabin card visible.
//   3. Player presses N → menu closes → step completes (and the standard
//      "press TAB to skip" prompt then appears via the usual MarkComplete
//      flow).
//
// AdvancesDuringModalUI = true because the player is inside the build menu /
// placement UI for the whole duration of this step.
class MainBuildCabinStep : TutorialStep
{
    System.Action<BuildableEntry> _handler;
    bool _placedCabin;

    public override bool BlocksAutoSkip => true;
    public override bool AdvancesDuringModalUI => true;

    public override string Tip => _placedCabin
        ? $"Nice cabin. Press {PromptGlyphs.BuildMenu} to close the build menu."
        : "Pick the <b>Cabin</b> and place it. Scroll to adjust distance, RMB to rotate, LMB to confirm.";

    public override void OnEnter()
    {
        _placedCabin = false;
        _handler = e =>
        {
            if (e != null && e.displayName == "Cabin")
            {
                EarlyGameProgress.CabinBuilt = true;
                // Cabin can't be placed again — drop it from the unlocked
                // set so the menu shows only the other tutorial entries.
                BuildMenuLock.LockAllExcept("Torch", "Bonfire");
                // Force placement to end after this placement and re-open the
                // build menu, so the player ends up inside the menu (with no
                // cabin available) instead of free-roaming.
                GhostPlacement.s_finishAfterNextPlacement = true;
                BuildMenuUI.RequestReopenAfterPlacement();
                _placedCabin = true;
            }
        };
        GhostPlacement.OnPlaced += _handler;
    }

    public override void Tick()
    {
        if (!_placedCabin) return;
        // BuildMenuUI sets PlayerController.isInDialogue while open. Once the
        // menu closes (player pressed N or Esc), the gate clears and we mark
        // the step complete — the standard "press TAB to skip" prompt then
        // takes over via MarkComplete.
        if (!PlayerController.isInDialogue) IsComplete = true;
    }

    public override void OnExit()
    {
        if (_handler != null) GhostPlacement.OnPlaced -= _handler;
        _handler = null;
    }
}
