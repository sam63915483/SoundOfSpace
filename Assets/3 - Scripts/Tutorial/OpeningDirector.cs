using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The first fifteen minutes. Picks up the moment the shuttle's ramp drops and
/// hands off at the village.
///
/// WHY THIS EXISTS
/// The forced tutorial is retired — TutorialManager.Start() deliberately no-ops
/// and its step list assumes the old cabin wake-up that the shuttle landing
/// replaced. The result was a player standing on a ramp with no idea what to do.
/// StoryDirector's objectives (obj_water, obj_food) were authored for that same
/// dead flow and are never started by anything, so this doesn't reuse them.
///
/// WHAT IT IS
/// Six beats, each a real survival need rather than a lesson, narrated by HAL as
/// suit telemetry:
///
///   1. LOCKER    take the axe and the bottle
///   2. WATER     fill the bottle in the lake and drink
///   3. WOOD      chop trees until you can afford a fire
///   4. FIRE      place a bonfire  → Colonizer LV 1 → blueprints unlock
///   5. BUILD     place one of the things that just unlocked
///   6. VILLAGE   compass waypoint, then hand off to StoryDirector
///
/// Beats 4 and 5 are the important ones: they teach the progression loop with
/// the loop itself. Placing the fire levels Colonizer, which fires the unlock
/// ceremony, which hands the player walls — and beat 5 immediately asks them to
/// use one. Nothing has to explain the system.
///
/// NO NEW SAVE STATE. Every beat's completion is read from something the game
/// already saves — hotbar contents, the story's hasWater flag, wood, the
/// Colonizer score, villageReached — so a load resumes at the right beat by
/// re-deriving it (see CurrentStepFromWorld). That also means a player who does
/// things out of order, or who wanders off and chops a tree before drinking,
/// finds the beats already ticked off rather than being told to redo them.
///
/// HAL is TEXT-ONLY here by design: HALVoiceManifest keys must byte-match a
/// pre-generated TTS clip, and these lines have none yet, so they display on the
/// HUD and play silently. Generating the voice later needs no code change.
///
/// Auto-singleton with MainMenu skip — ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1).
/// </summary>
public class OpeningDirector : MonoBehaviour
{
    public static OpeningDirector Instance { get; private set; }

    // Bonfire costs 15 wood (scene catalogue) and a tree drops 8–20, so this is
    // one lucky tree or two ordinary ones. Read from the catalogue at runtime
    // when it's available so retuning the blueprint's cost can't strand the
    // player on a beat they can't afford to finish.
    const int FallbackFireWood = 15;

    const string VillageWaypointId = "opening_village";
    const float  VillageReachDistance = 60f;   // matches the retired TravelToVillageStep

    // Seconds after the shuttle sequence ends (or after scene start, if there
    // is no sequence — e.g. pressing Play straight into the gameplay scene)
    // before the first beat appears. Long enough that HAL's last landing line
    // has cleared the HUD.
    const float FirstBeatDelay = 3.5f;
    const float AutoBeginPollInterval = 1f;
    // How long to wait for a shuttle sequence to show up before deciding this
    // scene simply doesn't have one (Editor Play straight into gameplay).
    const float NoSequenceGrace = 8f;

    enum Beat { Locker = 0, Water, Wood, Fire, Build, Village, Done }

    Beat _beat = Beat.Locker;
    bool _begun;
    bool _finished;
    Transform _village;
    float _nextPoll;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _sawSequence;      // a landing film ran — wait for its teardown to Begin()

    // Beat.Build must only count structures placed AFTER the fire, so it can't
    // just read the Colonizer score on entry — it latches the score it started
    // from. Re-derived on load from the same score, so this survives a reload.
    int _colonizerAtBuildStart = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("OpeningDirector");
        DontDestroyOnLoad(go);
        go.AddComponent<OpeningDirector>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnEnable()
    {
        GhostPlacement.OnPlaced += HandlePlaced;
        VillageReachTrigger.OnVillageReached += HandleVillageReached;
    }

    void OnDisable()
    {
        GhostPlacement.OnPlaced -= HandlePlaced;
        VillageReachTrigger.OnVillageReached -= HandleVillageReached;
        ClearVillageWaypoint();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        // A LOAD resumes straight away: the shuttle sequence never replays, so
        // nothing else would ever call Begin(). Same "is this boot a load?"
        // test ShuttleExitDoor uses — PendingLoad is consumed between Awake and
        // Start, so check both it and the runner it leaves behind.
        if (PendingLoad.Data != null || FindObjectOfType<SaveLoadRunner>() != null)
            Begin();
    }

    /// Called by ShuttleArrivalSequence once the ramp is down and control is the
    /// player's. Idempotent — the sequence's teardown is itself an abort path
    /// that can run more than once.
    public void Begin()
    {
        // VAULTED (FeatureVault.OpeningBeats) — built, never play-tested, and
        // held back at Sam's request until the caves are finished. Flip that
        // flag to bring the whole six-beat opening back.
        if (!FeatureVault.OpeningBeats) { _finished = true; return; }
        if (_begun || _finished) return;
        _begun = true;
        StartCoroutine(Run());
    }

    void Update()
    {
        // Fallback start, for boots where nothing ever calls Begin() — pressing
        // Play straight into the gameplay scene, or a shuttle sequence that
        // errored out before its teardown.
        //
        // "Not playing" is NOT sufficient on its own: it's also true in the
        // seconds between the scene loading and IntroSequenceController calling
        // Play(), and starting there would drop a tutorial pill over the
        // landing film. So the sequence must either be absent from the scene
        // entirely, or have played and finished.
        if (!_begun && !_finished && Time.time >= _nextPoll)
        {
            _nextPoll = Time.time + AutoBeginPollInterval;
            if (ShuttleArrivalSequence.IsPlaying) { _sawSequence = true; return; }
            if (ShuttleArrivalSequence.HasPlayed) { Begin(); return; }

            // No sequence has started. Give it a grace window to appear before
            // concluding there isn't one — the intro can take a moment to spin
            // up while the planet generates.
            if (!_sawSequence && Time.timeSinceLevelLoad >= NoSequenceGrace
                && FindObjectOfType<ShuttleArrivalSequence>() == null)
                Begin();
        }

        if (!_begun || _finished) return;

        // Distance fallback for the village: VillageReachTrigger needs its
        // collider to be hit, and a player who arrives by ship or over a ridge
        // can miss it entirely.
        if (_beat == Beat.Village) TickVillageDistance();
    }

    IEnumerator Run()
    {
        yield return new WaitForSeconds(FirstBeatDelay);

        // Resume point. On a fresh game this is Beat.Locker; on a load it's
        // wherever the world says the player actually got to.
        _beat = CurrentStepFromWorld();
        if (_beat == Beat.Done) { Finish(); yield break; }

        EnterBeat(_beat, speak: true);

        while (!_finished)
        {
            if (IsBeatComplete(_beat))
            {
                if (TutorialUI.Instance != null) TutorialUI.Instance.PlayCompleteSound();
                var next = (Beat)((int)_beat + 1);
                if (next >= Beat.Done) { Finish(); yield break; }
                _beat = next;
                // A short beat between "done" and the next instruction so the
                // completion chime and the new tip don't land on the same frame.
                yield return new WaitForSeconds(1.2f);
                EnterBeat(_beat, speak: true);
            }
            yield return new WaitForSeconds(0.25f);
        }
    }

    // ── Beats ────────────────────────────────────────────────────────────────

    void EnterBeat(Beat b, bool speak)
    {
        if (b == Beat.Build) _colonizerAtBuildStart = ColonizerScore;
        if (b == Beat.Village) SetVillageWaypoint();
        else ClearVillageWaypoint();

        ShowTip(b);
        if (speak) Say(HalLine(b));
    }

    void ShowTip(Beat b)
    {
        var ui = TutorialUI.Instance;
        if (ui == null) return;
        ui.SetLeftSide(false);
        ui.ShowStep(TipFor(b), (int)b + 1, (int)Beat.Done);
    }

    string TipFor(Beat b)
    {
        switch (b)
        {
            case Beat.Locker:
                return $"Open the locker in the shuttle. Press {PromptGlyphs.Interact} to take the axe and the water bottle.";
            case Beat.Water:
                return $"Stand in the lake and hold {PromptGlyphs.SecondaryFire} to fill your bottle, then hold {PromptGlyphs.PrimaryFire} to drink.";
            case Beat.Wood:
                return $"Equip the axe and chop trees until you have {FireWoodCost} wood. Hold {PromptGlyphs.PrimaryFire} to wind up a swing.";
            case Beat.Fire:
                return $"Press {PromptGlyphs.BuildMenu} to open the build menu, then place a Bonfire.";
            case Beat.Build:
                return $"Your colony level went up and unlocked new blueprints. Press {PromptGlyphs.BuildMenu} and place one of them.";
            case Beat.Village:
                return "There's a settlement to the north. Head for the marker on your compass.";
        }
        return string.Empty;
    }

    // HAL's read on each beat. Written as suit telemetry rather than as
    // instructions — the pill above already says what to press.
    static string HalLine(Beat b)
    {
        switch (b)
        {
            case Beat.Locker:
                return "Atmosphere is breathable. Marginal, but breathable. Your kit is in the locker behind you — take all of it.";
            case Beat.Water:
                return "Hydration is your first problem. That lake reads clean enough. I'd drink it before I'd drink what's in your suit.";
            case Beat.Wood:
                return "Second problem: it gets cold here, and dark. You'll want wood. The axe is not ceremonial.";
            case Beat.Fire:
                return "Build the fire before the light goes. I would rather not narrate what happens if you don't.";
            case Beat.Build:
                return "Noted: you can build. Your colonisation index just moved, and it came with schematics. Try one.";
            case Beat.Village:
                return "I'm reading structures to the north. Something down here is organised. Go and look.";
        }
        return string.Empty;
    }

    bool IsBeatComplete(Beat b)
    {
        switch (b)
        {
            case Beat.Locker:
                return HasItem(Hotbar.ItemId.Axe) && HasItem(Hotbar.ItemId.WaterBottle);
            case Beat.Water:
                // The story flag is the durable record — StoryDirector sets it
                // from ResourceManager.OnCleanWaterDrunk and saves it.
                return StoryFlag("hasWater");
            case Beat.Wood:
                // Either enough wood banked, or they already built something
                // (they got there another way — don't make them chop for a
                // requirement they've already met).
                return Wood >= FireWoodCost || ColonizerScore >= 1;
            case Beat.Fire:
                return ColonizerScore >= 1;
            case Beat.Build:
                return ColonizerScore >= Mathf.Max(2, _colonizerAtBuildStart + 1);
            case Beat.Village:
                return StoryFlag("villageReached");
        }
        return true;
    }

    /// Which beat the world says the player is on. Used to resume after a load
    /// and to skip anything already done. Walks forward from the first beat and
    /// stops at the first incomplete one, so an out-of-order player is credited
    /// for everything they've actually achieved.
    Beat CurrentStepFromWorld()
    {
        if (StoryFlag("villageReached")) return Beat.Done;
        var sd = StoryDirector.Instance;
        if (sd != null && sd.CurrentStoryStep >= StoryStep.Explore) return Beat.Village;

        for (int i = 0; i < (int)Beat.Done; i++)
        {
            var b = (Beat)i;
            // Beat.Build's latch isn't known yet on a cold resume; treat "two
            // or more structures placed" as done, which is what the latch would
            // have produced for a player who reached that beat honestly.
            if (b == Beat.Build) { if (ColonizerScore >= 2) continue; return b; }
            if (!IsBeatComplete(b)) return b;
        }
        return Beat.Done;
    }

    void Finish()
    {
        if (_finished) return;
        _finished = true;
        ClearVillageWaypoint();
        if (TutorialUI.Instance != null) TutorialUI.Instance.HideAll();

        // Hand the player to the story system at the documented seam. Only ever
        // pushes forward — a player already past Explore keeps their place.
        var sd = StoryDirector.Instance;
        if (sd != null && sd.CurrentStoryStep < StoryStep.Explore)
        {
            sd.SetStoryStep(StoryStep.Explore);
            sd.StartObjective("obj_village");
        }
    }

    // ── Event hooks ──────────────────────────────────────────────────────────
    // The beat loop polls, so these only exist to make the two placement beats
    // feel instant rather than up to a quarter-second late.

    void HandlePlaced(BuildableEntry entry)
    {
        if (!_begun || _finished || entry == null || entry.isSapling) return;
        // Nothing to do but let the poll pick it up next tick — placing is
        // already reflected in the Colonizer score by ProgressHooks.
    }

    void HandleVillageReached()
    {
        if (!_begun || _finished) return;
        if (_beat == Beat.Village) Finish();
    }

    void TickVillageDistance()
    {
        if (_village == null)
        {
            var marker = FindObjectOfType<VillageMarker>();
            if (marker == null) return;
            _village = marker.transform;
        }
        // Cached, lazily refound — never FindObjectOfType per frame (CLAUDE.md).
        // This only runs on the village beat, but the rule still holds.
        if (_player == null)
        {
            if (--_playerRefindCooldown > 0) return;
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 30;
            if (_player == null) return;
        }
        if ((_player.transform.position - _village.position).sqrMagnitude
            <= VillageReachDistance * VillageReachDistance)
            Finish();
    }

    void SetVillageWaypoint()
    {
        if (CompassHUD.Instance == null) return;
        if (_village == null)
        {
            var marker = FindObjectOfType<VillageMarker>();
            if (marker != null) _village = marker.transform;
        }
        if (_village == null) return;
        var t = _village;
        CompassHUD.Instance.AddWaypoint(VillageWaypointId, () => t.position, "Village");
    }

    void ClearVillageWaypoint()
    {
        if (CompassHUD.Instance != null) CompassHUD.Instance.RemoveWaypoint(VillageWaypointId);
    }

    // ── Small readers ────────────────────────────────────────────────────────

    static void Say(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        // Same routing the shuttle sequence uses: through HAL when he exists so
        // the line lands in his log and HUD, straight to the line HUD if not.
        if (HALCommentator.Instance != null) HALCommentator.Instance.VolunteerExternal(line, true);
        else if (HALLineHUD.Instance != null) HALLineHUD.Instance.Show(line);
    }

    static bool HasItem(Hotbar.ItemId id)
    {
        var hb = Hotbar.Instance;
        if (hb == null) return false;
        var slots = hb.GetSlotsForSave();
        for (int i = 0; i < slots.Count; i++)
            if (slots[i].id == id) return true;
        return false;
    }

    static int Wood => WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;

    static int ColonizerScore
        => PlayerProgress.Instance != null
           ? Mathf.Max(0, PlayerProgress.Instance.ScoreOf(ProgressTrack.Colonizer))
           : 0;

    static bool StoryFlag(string name)
        => StoryDirector.Instance != null && StoryDirector.Instance.GetFlag(name);

    /// Bonfire's real wood cost, straight from the scene catalogue so a retune
    /// can't ask the player for the wrong amount. Falls back to the authored 15.
    static int FireWoodCost
    {
        get
        {
            var menu = BuildMenuUI.Instance;
            if (menu != null && menu.Buildables != null)
                foreach (var b in menu.Buildables)
                    if (b != null && b.displayName != null
                        && b.displayName.Trim().Equals("Bonfire", StringComparison.OrdinalIgnoreCase))
                        return Mathf.Max(1, b.woodCost);
            return FallbackFireWood;
        }
    }
}
