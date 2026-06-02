using System.Collections.Generic;
using UnityEngine;

public static class TutorialSteps
{
    public static List<TutorialStep> BuildDefault()
    {
        // New early-game flow: player wakes up in the cabin, looks around,
        // walks to the table, reads Tev's note, picks up the fishing rod.
        // Subsequent phases (walk to fishing bank, fishing tutorial, cooking,
        // water bottle, return home, axe/build, village) are appended as we
        // build them out.
        //
        // The legacy step classes (PostCrashExamStep, StandUpStep, etc.) and the
        // pre-merge step classes (CatchFirstFishStep, CatchFiveFishStep,
        // WalkToFireStep, OpenCookPanelStep, MainSwingAxeStep, MainGatherWoodStep,
        // OpenBuildMenuStep, MainBuildCabinStep) are intentionally kept defined
        // further down in this file. They're not in the active list any more, but
        // TutorialManager.ApplyState resolves saved steps by type name — keeping
        // the classes around lets older saves load without errors.
        return new List<TutorialStep>
        {
            // Phase 1 — wake in cabin. WakeUpLookStep + WakeUpWalkStep were
            // removed; mouse-look / move / jump are now unlocked at tutorial
            // start (see TutorialManager.BeginTutorial). The classes are kept
            // defined further down so older saves still resolve by type name.
            new ReadNoteStep(),
            new PickUpRodStep(),
            // Phase 2 — walk to fishing bank, learn fishing
            new WalkToFishingBankStep(),
            new MainCastBobberStep(),
            new CatchFishStep(),
            new MainOpenFishingdexStep(),
            // Phase 3 — walk to fire and cook a fish
            new WalkToFireAndCookStep(),
            new CookAndEatStep(),
            // Phase 4 — water bottle pickup, refill, drink
            new PickUpBottleStep(),
            new RefillBottleStep(),
            new DrinkBottleStep(),
            // Phase 5 — return home, talk to Tev (axe unlock)
            new ReturnAndTalkToTevStep(),
            // Phase 6 — chop wood, build a cabin, talk to Tev again
            new ChopWoodStep(),
            new OpenAndBuildCabinStep(),
            new TalkToTevAgainStep(),
            // Phase 7 — travel to the village, meet the vendors, finish
            new TravelToVillageStep(),
            new MeetVendorsStep(),
            new TutorialFinaleStep(),
        };
    }
}

// Read Tev's note. Completes when the note is in NoteCollection AND the
// reader UI is closed (so the tutorial advance hint doesn't pop up on top of
// the note panel). Sets the EarlyGameProgress flag for downstream gates.
class ReadNoteStep : TutorialStep
{
    public override string Tip => $"Read Tev's note in the cabin. Press {PromptGlyphs.Interact} when you find it.";
    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.Pickup);
    }
    public override void Tick()
    {
        bool noteRead = NoteCollection.Has("tev_intro");
        bool readerClosed = NoteReadUI.Instance == null || !NoteReadUI.Instance.IsOpen;
        if (noteRead && readerClosed)
        {
            EarlyGameProgress.NoteRead = true;
            IsComplete = true;
        }
    }
}

// Pick up Tev's fishing rod. Completes when FishingRodPickup sets
// EarlyGameProgress.RodPickedUp = true.
class PickUpRodStep : TutorialStep
{
    public override string Tip => $"Pick up Tev's fishing rod. Press {PromptGlyphs.Interact}.";
    public override void Tick()
    {
        if (EarlyGameProgress.RodPickedUp) IsComplete = true;
    }
}

// ─────────────────────────────────────────────────────────────────────────
// Phase 2 — walk to fishing bank, learn fishing
// ─────────────────────────────────────────────────────────────────────────

// Adds a compass waypoint pointing at the FishingBank-tagged transform.
// Completes when the player gets within ReachDistance of the bank. Removes
// the waypoint on exit so the compass is empty during the cast/catch steps
// (no destination needed — the player is already at the water).
class WalkToFishingBankStep : TutorialStep
{
    const string FishingBankTag = "FishingBank";
    const float ReachDistance = 5f;

    Transform _bank;
    PlayerController _player;

    public override string Tip => "Head out to the fishing bank.";

    public override void OnEnter()
    {
        if (CompassHUD.Instance != null)
            CompassHUD.Instance.AddWaypointByTag("fishing_bank", FishingBankTag, "Fishing Bank");
        var go = SafeFindWithTag(FishingBankTag);
        if (go != null) _bank = go.transform;
        _player = Object.FindObjectOfType<PlayerController>();
    }

    public override void Tick()
    {
        // Lazy-find the bank in case it spawned after OnEnter.
        if (_bank == null)
        {
            var go = SafeFindWithTag(FishingBankTag);
            if (go == null) return;
            _bank = go.transform;
        }
        if (_player == null) _player = Object.FindObjectOfType<PlayerController>();
        if (_player == null) return;

        float dist = Vector3.Distance(_player.transform.position, _bank.position);
        if (dist <= ReachDistance) IsComplete = true;
    }

    // GameObject.FindWithTag throws UnityException if the tag isn't defined in
    // Project Settings — happens during dev when scene authoring is ahead of
    // tag setup. Swallow the throw so the step polls cleanly until the tag is
    // added; the step just stays incomplete until then.
    static GameObject SafeFindWithTag(string tag)
    {
        try { return GameObject.FindWithTag(tag); }
        catch (UnityException) { return null; }
    }

    public override void OnExit()
    {
        if (CompassHUD.Instance != null)
            CompassHUD.Instance.RemoveWaypoint("fishing_bank");
    }
}

// Cast the bobber. Subscribes to FishingRodController.OnBobberCast — fires
// once per cast. OnEnter unlocks TutorialAbility.Cast so the rod actually
// responds to LMB / RT.
//
// Named MainCastBobberStep (rather than CastBobberStep) because BonusTutorial
// already defines a CastBobberStep that inherits BonusStep, and C# doesn't
// allow two top-level types with the same name in one assembly. Same reason
// for MainOpenFishingdexStep below.
class MainCastBobberStep : TutorialStep
{
    System.Action _handler;

    public override string Tip => $"{PromptGlyphs.PrimaryClickCap} to cast the bobber.";

    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.Cast);
        _handler = () => IsComplete = true;
        FishingRodController.OnBobberCast += _handler;
    }

    public override void Tick() { }

    public override void OnExit()
    {
        if (_handler != null) FishingRodController.OnBobberCast -= _handler;
        _handler = null;
    }
}

// Open the Fishingdex. Subscribes to FishingdexManager.OnFishingdexOpened.
// OnEnter unlocks TutorialAbility.Fishingdex so the B / RB key actually opens
// the panel. (Named MainOpenFishingdexStep to avoid clashing with the
// BonusTutorial OpenFishingdexStep — see MainCastBobberStep comment above.)
class MainOpenFishingdexStep : TutorialStep
{
    System.Action _handler;

    public override string Tip =>
        $"Press {PromptGlyphs.Fishingdex} to open the Fishingdex and see the fish you've caught.";

    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.Fishingdex);
        _handler = () => IsComplete = true;
        FishingdexManager.OnFishingdexOpened += _handler;
    }

    public override void Tick() { }

    public override void OnExit()
    {
        if (_handler != null) FishingdexManager.OnFishingdexOpened -= _handler;
        _handler = null;
    }
}

// Cook a fish and eat it, THEN close the cook panel. Two-stage tip:
// before eating it instructs Add → Cook → Eat; after eating it switches to
// "Press F to close the menu". Step completes when the player has both eaten
// AND closed the panel (PlayerController.isInDialogue flips back to false).
//
// Subscribes to BonfireInteraction.OnEat — checking hunger delta isn't
// reliable because hunger caps at 100%, so eating with a full meter looks
// like a no-op.
class CookAndEatStep : TutorialStep
{
    System.Action _handler;
    bool _hasEaten;

    public override bool BlocksAutoSkip => true;

    public override string Tip
    {
        get
        {
            if (_hasEaten)
                return $"Press {PromptGlyphs.Interact} to close the menu.";
            return "Add a fish, click <b>Cook</b>, then click <b>Eat</b> to restore hunger.";
        }
    }

    public override void OnEnter()
    {
        _hasEaten = false;
        _handler = () => { _hasEaten = true; };
        BonfireInteraction.OnEat += _handler;
    }

    public override void Tick()
    {
        // Complete only after the player has eaten AND closed the panel.
        // PlayerController.isInDialogue toggles to false when BonfireInteraction
        // closes the panel (player walks out OR presses F again).
        if (_hasEaten && !PlayerController.isInDialogue)
        {
            EarlyGameProgress.FirstMealEaten = true;
            IsComplete = true;
        }
    }

    public override void OnExit()
    {
        if (_handler != null) BonfireInteraction.OnEat -= _handler;
        _handler = null;
    }
}

// ─────────────────────────────────────────────────────────────────────────
// Phase 4 — water bottle pickup, refill, drink
// ─────────────────────────────────────────────────────────────────────────

// Pick up the water bottle prop placed near the fire. WaterBottlePickup
// component on the prop calls WaterBottleController.Unlock() + ForceEquip
// on F; this step polls IsUnlocked to detect that.
class PickUpBottleStep : TutorialStep
{
    public override string Tip =>
        $"There's a water bottle near the fire. Press {PromptGlyphs.Interact} to pick it up.";

    public override void Tick()
    {
        var bottle = Object.FindObjectOfType<WaterBottleController>();
        if (bottle != null && bottle.IsUnlocked) IsComplete = true;
    }
}

// Walk to water and hold RMB to fill. Compass repoints at the FishingBank
// (only Water-tagged source in the scene). Completes when the bottle's
// fillPercent crosses a small threshold — proves the player held the
// fill input while standing in water.
class RefillBottleStep : TutorialStep
{
    const string FishingBankTag = "FishingBank";
    const float MinFillToComplete = 5f;

    public override bool BlocksAutoSkip => true;

    public override string Tip =>
        $"Stand in the water and hold {PromptGlyphs.SecondaryFire} to fill your bottle.";

    public override void OnEnter()
    {
        if (CompassHUD.Instance != null)
            CompassHUD.Instance.AddWaypointByTag("water_source", FishingBankTag, "Water");
    }

    public override void Tick()
    {
        var bottle = Object.FindObjectOfType<WaterBottleController>();
        if (bottle == null) return;
        if (bottle.FillPercent >= MinFillToComplete) IsComplete = true;
    }

    public override void OnExit()
    {
        if (CompassHUD.Instance != null)
            CompassHUD.Instance.RemoveWaypoint("water_source");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// Phase 5 — return home, talk to Tev (axe + pistol unlock)
// ─────────────────────────────────────────────────────────────────────────

// Walk back home AND talk to Tev — merged into one tip so the player isn't
// staring at two consecutive "head this way / now press F" prompts. Compass
// points at Tev so the player walks to him; the world-space "Press F to talk"
// prompt on Tev fires once the player is in range. Step completes when
// TevReturnedDialogueDone is set at the end of the give-axe dialogue.
//
// BlocksAutoSkip because dialogue takes time and we don't want auto-skip
// blowing past the conversation mid-line.
class ReturnAndTalkToTevStep : TutorialStep
{
    public override bool BlocksAutoSkip => true;

    public override string Tip =>
        $"Tev should be home by now — head back and talk to him. Press {PromptGlyphs.Interact} when you're close.";

    public override void OnEnter()
    {
        var tev = Object.FindObjectOfType<TevDialogue>();
        if (tev != null && CompassHUD.Instance != null)
        {
            var tevTransform = tev.transform;
            CompassHUD.Instance.AddWaypoint("home", () => tevTransform.position, "Home");
        }
    }

    public override void Tick()
    {
        if (EarlyGameProgress.TevReturnedDialogueDone) IsComplete = true;
    }

    public override void OnExit()
    {
        if (CompassHUD.Instance != null)
            CompassHUD.Instance.RemoveWaypoint("home");
    }
}

// Hold LMB to drink. Detects "drink ATTEMPT" by tracking how long the player
// has held the fire input while the bottle is equipped and has water — this
// is reliable even when WaterBottleController.thirstBlocked == true (which
// happens when thirst is already at 100%, so a fill-delta or thirst-delta
// check never fires for a freshly-spawned player).
class DrinkBottleStep : TutorialStep
{
    const float RequiredHoldTime = 0.5f;
    float _heldDuration;

    public override bool BlocksAutoSkip => true;

    public override string Tip =>
        $"Hold {PromptGlyphs.PrimaryFire} to drink from the bottle.";

    public override void OnEnter()
    {
        _heldDuration = 0f;
    }

    public override void Tick()
    {
        var bottle = Object.FindObjectOfType<WaterBottleController>();
        // Only count as a drink attempt when the bottle is equipped and has
        // some water. Reset the timer if the player lets go or the bottle
        // empties — they have to deliberately hold the input.
        if (bottle == null || !bottle.IsEquipped || bottle.FillPercent <= 0f)
        {
            _heldDuration = 0f;
            return;
        }
        if (TutorialGate.FireHeld())
        {
            _heldDuration += Time.unscaledDeltaTime;
        }
        else
        {
            _heldDuration = 0f;
        }
        if (_heldDuration >= RequiredHoldTime)
        {
            EarlyGameProgress.WaterBottleDrunk = true;
            IsComplete = true;
        }
    }
}

// Walk back to Tev for the village-coordinates dialogue. Compass repoints at
// Tev's transform. Completes when TevDialogue's village branch sets the flag.
class TalkToTevAgainStep : TutorialStep
{
    Transform _tev;

    public override bool BlocksAutoSkip => true;

    public override string Tip => $"Head back to Tev. Press {PromptGlyphs.Interact} to talk.";

    public override void OnEnter()
    {
        var tev = Object.FindObjectOfType<TevDialogue>();
        if (tev != null) _tev = tev.transform;
        if (_tev != null && CompassHUD.Instance != null)
        {
            var t = _tev;
            CompassHUD.Instance.AddWaypoint("tev", () => t.position, "Tev");
        }
    }

    public override void Tick()
    {
        if (EarlyGameProgress.VillageCoordsGiven) IsComplete = true;
    }

    public override void OnExit()
    {
        if (CompassHUD.Instance != null) CompassHUD.Instance.RemoveWaypoint("tev");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// Phase 7 — travel to the village, meet the two vendors, finish tutorial
// ─────────────────────────────────────────────────────────────────────────

// Travel to the village — compass points at the VillageMarker placed on
// Humble Abode. Completes when the player gets within ReachDistance.
//
// While walking, the tip swaps to a flashlight prompt the moment the player
// crosses into the planet's dark hemisphere AND hasn't yet turned the
// flashlight on this step. Once they toggle it, the tip flips back to the
// "head to the village" line and stays there even if they re-enter darkness
// (we don't keep nagging once they've shown they know how it works). The
// step itself still completes only by reaching the village — the flashlight
// prompt is purely a tip swap, not a separate sub-step.
class TravelToVillageStep : TutorialStep
{
    const float ReachDistance = 60f; // village is a place, not a person — generous radius, tripled so the player just has to reach the general area before the tip flips to the vendor step
    // Mirrors EnemySpawner's dark-side check: dot(playerDir, sunDir) < this
    // means the player is past the terminator and on the night-side hemisphere.
    const float DarkDotThreshold = 0.05f;

    Transform _village;
    PlayerController _player;
    PlayerFlashlight _flashlight;
    bool _flashlightUsed;

    public override bool BlocksAutoSkip => true;

    public override string Tip
    {
        get
        {
            if (!_flashlightUsed && IsDark())
                return $"It's getting dark. Press {PromptGlyphs.Flashlight} to use your flashlight.";
            return "Head to the village. Tev marked it on your compass.";
        }
    }

    public override void OnEnter()
    {
        // Players reach this step well before the gate is fully released by
        // TutorialFinaleStep, so explicitly unlock flashlight here so pressing
        // E actually toggles the light when the dark-side tip prompts them.
        TutorialGate.Unlock(TutorialAbility.Flashlight);

        var marker = Object.FindObjectOfType<VillageMarker>();
        if (marker != null) _village = marker.transform;
        _player = Object.FindObjectOfType<PlayerController>();
        _flashlight = Object.FindObjectOfType<PlayerFlashlight>();
        _flashlightUsed = false;

        if (_village != null && CompassHUD.Instance != null)
        {
            var t = _village;
            CompassHUD.Instance.AddWaypoint("village", () => t.position, "Village");
        }
    }

    public override void Tick()
    {
        if (_village == null)
        {
            var marker = Object.FindObjectOfType<VillageMarker>();
            if (marker == null) return;
            _village = marker.transform;
        }
        if (_player == null) _player = Object.FindObjectOfType<PlayerController>();
        if (_player == null) return;
        if (_flashlight == null) _flashlight = Object.FindObjectOfType<PlayerFlashlight>();

        // Latch on the first time we see the flashlight enabled this step.
        // Don't unlatch when the player turns it off — they've demonstrated
        // they know the binding, no need to prompt again.
        if (!_flashlightUsed && _flashlight != null && _flashlight.flashlight != null && _flashlight.flashlight.enabled)
            _flashlightUsed = true;

        if (Vector3.Distance(_player.transform.position, _village.position) <= ReachDistance)
            IsComplete = true;
    }

    // Same math as EnemySpawner's dark-side spawn check: pick the nearest
    // non-sun body, compare playerDir-from-its-center against the sun
    // direction. Avoids depending on EnemySpawner being in the scene.
    bool IsDark()
    {
        if (_player == null) return false;
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return false;
        CelestialBody planet = null;
        CelestialBody sun = null;
        float minDist = float.PositiveInfinity;
        foreach (var b in bodies)
        {
            if (b == null) continue;
            if (b.bodyType == CelestialBody.BodyType.Sun) { sun = b; continue; }
            float d = (b.Position - _player.transform.position).magnitude - b.radius;
            if (d < minDist) { minDist = d; planet = b; }
        }
        if (planet == null || sun == null) return false;
        Vector3 playerDir = (_player.transform.position - planet.Position).normalized;
        Vector3 sunDir = (sun.Position - planet.Position).normalized;
        return Vector3.Dot(playerDir, sunDir) < DarkDotThreshold;
    }

    public override void OnExit()
    {
        if (CompassHUD.Instance != null) CompassHUD.Instance.RemoveWaypoint("village");
    }
}

// Meet both village vendors. Tip and compass switch dynamically based on
// which vendor hasn't been spoken to yet — keeps the player from juggling
// two prompts at once. Subscribes to NPCConversationTracker so we capture
// the exact moment a conversation begins (whether via greeting or the
// vendor's shop UI). Completes when both flags are set.
class MeetVendorsStep : TutorialStep
{
    System.Action<MonoBehaviour> _convoHandler;
    Transform _fishVendor;
    Transform _goodsVendor;
    string _activeWaypointKey;

    public override bool BlocksAutoSkip => true;

    public override string Tip
    {
        get
        {
            if (!EarlyGameProgress.FishVendorVisited && !EarlyGameProgress.GoodsVendorVisited)
                return $"Two vendors run the village — a <b>fish vendor</b> and a <b>goods vendor</b>. Press {PromptGlyphs.Interact} to greet them.";
            if (!EarlyGameProgress.FishVendorVisited)
                return $"Now find the <b>fish vendor</b> and press {PromptGlyphs.Interact} to greet them.";
            if (!EarlyGameProgress.GoodsVendorVisited)
                return $"Now find the <b>goods vendor</b> and press {PromptGlyphs.Interact} to greet them.";
            return "Vendors met!";
        }
    }

    public override void OnEnter()
    {
        var fish = Object.FindObjectOfType<FishMarketNPC>();
        if (fish != null) _fishVendor = fish.transform;
        var goods = Object.FindObjectOfType<Alien7Vendor>();
        if (goods != null) _goodsVendor = goods.transform;

        _convoHandler = npc =>
        {
            if (npc is FishMarketNPC) EarlyGameProgress.FishVendorVisited = true;
            else if (npc is Alien7Vendor) EarlyGameProgress.GoodsVendorVisited = true;
            RefreshCompass();
        };
        NPCConversationTracker.OnConversationStarted += _convoHandler;

        RefreshCompass();
    }

    public override void Tick()
    {
        if (EarlyGameProgress.FishVendorVisited && EarlyGameProgress.GoodsVendorVisited)
            IsComplete = true;
    }

    public override void OnExit()
    {
        if (_convoHandler != null) NPCConversationTracker.OnConversationStarted -= _convoHandler;
        _convoHandler = null;
        if (CompassHUD.Instance != null && _activeWaypointKey != null)
        {
            CompassHUD.Instance.RemoveWaypoint(_activeWaypointKey);
            _activeWaypointKey = null;
        }
    }

    void RefreshCompass()
    {
        if (CompassHUD.Instance == null) return;
        // Drop the previous waypoint before swapping so we never have both pips up at once.
        if (_activeWaypointKey != null)
        {
            CompassHUD.Instance.RemoveWaypoint(_activeWaypointKey);
            _activeWaypointKey = null;
        }

        // Prefer pointing at whichever vendor the player still hasn't met.
        // Fish vendor first (closer to the village edge in our layout — minor
        // ergonomic preference, swap the order if the village is rearranged).
        if (!EarlyGameProgress.FishVendorVisited && _fishVendor != null)
        {
            var t = _fishVendor;
            _activeWaypointKey = "vendor_fish";
            CompassHUD.Instance.AddWaypoint(_activeWaypointKey, () => t.position, "Fish Vendor");
        }
        else if (!EarlyGameProgress.GoodsVendorVisited && _goodsVendor != null)
        {
            var t = _goodsVendor;
            _activeWaypointKey = "vendor_goods";
            CompassHUD.Instance.AddWaypoint(_activeWaypointKey, () => t.position, "Goods Vendor");
        }
    }
}

// Final tutorial step. Unlocks the full build menu (so the player can build
// anything from here on out) and shows a closing tip. Standard MarkComplete
// flow handles the "press TAB to finish" prompt; once that fires the
// tutorial enters its finished state and stops showing tips.
class TutorialFinaleStep : TutorialStep
{
    public override string Tip =>
        "You're all set. Welcome to Humble Abode.";

    public override void OnEnter()
    {
        // Lift the per-blueprint restriction set during Phase 6's build tutorial
        // so every BuildableEntry shows in the menu from now on.
        BuildMenuLock.UnlockAll();
        // Auto-complete on entry — there's no remaining gameplay action to wait
        // for. The tutorial UI then runs its standard "press TAB" / autoskip
        // flow before the manager finishes the tutorial.
        IsComplete = true;
    }

    public override void Tick() { }
}

// ─────────────────────────────────────────────────────────────────────────
// Merged steps — replace pairs of older steps in BuildDefault. Each pair was
// two consecutive prompts the player had to advance through; the merged
// version runs the same gameplay flow under a single tip whose text swaps
// stage-by-stage. The pre-merge classes (CatchFirstFishStep,
// CatchFiveFishStep, WalkToFireStep, OpenCookPanelStep, MainSwingAxeStep,
// MainGatherWoodStep, OpenBuildMenuStep, MainBuildCabinStep) are kept above
// for save compatibility; only these merged versions appear in BuildDefault.
// ─────────────────────────────────────────────────────────────────────────

// Merge of CatchFirstFishStep + CatchFiveFishStep. Tip teaches the wiggle/reel
// timing and shows a 0/5 counter; completes once the player has caught five.
// Sets both EarlyGameProgress flags the originals set so any downstream gates
// keying off either continue to fire.
class CatchFishStep : TutorialStep
{
    const int Required = 5;

    public override bool BlocksAutoSkip => true;

    public override string Tip
    {
        get
        {
            int count = FishInventory.Instance != null ? FishInventory.Instance.AllFish.Count : 0;
            int shown = Mathf.Min(count, Required);
            return $"Catch <b>{Required}</b> fish — wait for the bobber to wiggle, then {PromptGlyphs.PrimaryClick} to reel.\n<b>{shown}/{Required}</b> caught.";
        }
    }

    public override void Tick()
    {
        if (FishInventory.Instance == null) return;
        if (FishInventory.Instance.AllFish.Count >= Required)
        {
            EarlyGameProgress.FirstFishCaught = true;
            EarlyGameProgress.OneOfEachCaught = true;
            IsComplete = true;
        }
    }
}

// Merge of WalkToFireStep + OpenCookPanelStep. Two-stage tip: directs the
// player to the bonfire with a compass waypoint, then swaps to the F-prompt
// once they're close enough. Completes when BonfireInteraction's panel opens
// (same event the original OpenCookPanelStep fired on). AdvancesDuringModalUI
// is true because the next step (CookAndEatStep) instructs actions inside the
// panel that just opened.
class WalkToFireAndCookStep : TutorialStep
{
    const string BonfireTag = "Bonfire";
    const float ReachDistance = 5f;

    Transform _fire;
    PlayerController _player;
    System.Action _panelHandler;
    bool _atFire;

    public override bool AdvancesDuringModalUI => true;

    public override string Tip
    {
        get
        {
            if (_atFire) return $"Press {PromptGlyphs.Interact} at the fire to cook.";
            return "Head over to the fire to cook your fish.";
        }
    }

    public override void OnEnter()
    {
        // Bonfire interaction is gated on TalkToNPC (same gate NPC dialogue
        // uses). Unlock it now so the F prompt actually opens the cook panel
        // when the player arrives.
        TutorialGate.Unlock(TutorialAbility.TalkToNPC);

        if (CompassHUD.Instance != null)
            CompassHUD.Instance.AddWaypointByTag("bonfire", BonfireTag, "Fire");

        var go = SafeFindWithTag(BonfireTag);
        if (go != null) _fire = go.transform;
        _player = Object.FindObjectOfType<PlayerController>();

        _panelHandler = () => IsComplete = true;
        BonfireInteraction.OnPanelOpened += _panelHandler;
    }

    public override void Tick()
    {
        if (_fire == null)
        {
            var go = SafeFindWithTag(BonfireTag);
            if (go != null) _fire = go.transform;
        }
        if (_player == null) _player = Object.FindObjectOfType<PlayerController>();
        if (_player == null || _fire == null) return;

        if (!_atFire && Vector3.Distance(_player.transform.position, _fire.position) <= ReachDistance)
        {
            _atFire = true;
            // Drop the compass waypoint once the player can see the fire — the
            // arrow becomes noise once they're standing on top of it.
            if (CompassHUD.Instance != null) CompassHUD.Instance.RemoveWaypoint("bonfire");
        }
    }

    public override void OnExit()
    {
        if (_panelHandler != null) BonfireInteraction.OnPanelOpened -= _panelHandler;
        _panelHandler = null;
        if (CompassHUD.Instance != null) CompassHUD.Instance.RemoveWaypoint("bonfire");
    }

    static GameObject SafeFindWithTag(string tag)
    {
        try { return GameObject.FindWithTag(tag); }
        catch (UnityException) { return null; }
    }
}

// Merge of MainSwingAxeStep + MainGatherWoodStep. Drops the dedicated "try out
// your axe" prompt — the player learns to swing by chopping toward the wood
// quota directly. Live counter, threshold matches the cabin's woodCost.
class ChopWoodStep : TutorialStep
{
    const int Required = 50;

    public override bool BlocksAutoSkip => true;

    public override string Tip
    {
        get
        {
            int wood = WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;
            int shown = Mathf.Min(wood, Required);
            return $"Chop trees with {PromptGlyphs.PrimaryClick} until you have <b>{Required}</b> wood.\n<b>{shown}/{Required}</b> gathered.";
        }
    }

    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.ChopAxe);
    }

    public override void Tick()
    {
        if (WoodInventory.Instance == null) return;
        if (WoodInventory.Instance.Wood >= Required) IsComplete = true;
    }
}

// Merge of OpenBuildMenuStep + MainBuildCabinStep. Three-stage tip:
//   1. "Press N to open the build menu" — until BuildMenuUI.OnOpened fires.
//   2. "Pick the Cabin and place it…" — until GhostPlacement.OnPlaced fires
//      with the Cabin entry.
//   3. "Press N to close…" — until BuildMenuUI closes (PlayerController
//      .isInDialogue flips back to false).
// AdvancesDuringModalUI = true because the player is inside the build menu /
// placement modal for the entire step duration.
class OpenAndBuildCabinStep : TutorialStep
{
    System.Action _openHandler;
    System.Action<BuildableEntry> _placedHandler;
    bool _menuOpened;
    bool _placedCabin;

    public override bool BlocksAutoSkip => true;
    public override bool AdvancesDuringModalUI => true;

    public override string Tip
    {
        get
        {
            if (!_menuOpened) return $"Press {PromptGlyphs.BuildMenu} to open the build menu.";
            if (!_placedCabin) return "Pick the <b>Cabin</b> and place it. Scroll to adjust distance, RMB to rotate, LMB to confirm.";
            return $"Nice cabin. Press {PromptGlyphs.BuildMenu} to close the build menu.";
        }
    }

    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.BuildMenu);
        BuildMenuLock.LockAllExcept("Cabin", "Torch", "Bonfire");

        _openHandler = () => _menuOpened = true;
        BuildMenuUI.OnOpened += _openHandler;

        _placedHandler = e =>
        {
            if (e == null || e.displayName != "Cabin") return;
            EarlyGameProgress.CabinBuilt = true;
            // Cabin can't be placed again — drop it from the unlocked set so the
            // menu shows only the other tutorial entries when it re-opens.
            BuildMenuLock.LockAllExcept("Torch", "Bonfire");
            // Force placement to end after this placement and re-open the build
            // menu, so the player ends up inside the menu (with no cabin
            // available) instead of free-roaming, matching the pre-merge flow.
            GhostPlacement.s_finishAfterNextPlacement = true;
            BuildMenuUI.RequestReopenAfterPlacement();
            _placedCabin = true;
        };
        GhostPlacement.OnPlaced += _placedHandler;
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
        if (_openHandler != null) BuildMenuUI.OnOpened -= _openHandler;
        _openHandler = null;
        if (_placedHandler != null) GhostPlacement.OnPlaced -= _placedHandler;
        _placedHandler = null;
    }
}
