using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// GALACTIC STANDARD TIME — one clock the whole galaxy runs on.
///
/// 24 in-game hours pass in 24 real minutes, which lands on a very clean
/// conversion: <b>1 real second = 1 in-game minute</b>. An in-game hour is a
/// real minute; an in-game day is 24 real minutes; a week is 2 hours 48 minutes
/// of real play.
///
/// ── Why one galaxy-wide clock, and what it deliberately is NOT ───────────
/// This is a STANDARD, like UTC — not local solar time. It does not describe
/// the sky above the player's head and is not supposed to.
///
/// That matters here because this game already has a day/night cycle, and it
/// is purely geometric: nothing stores a "time of day", and planets do not spin
/// on their axis. "Night" is recomputed on demand as a dot product between a
/// surface point and the sun's position (ConcertStageHub, EnemySpawner,
/// EnemyController's sunburn, TutorialSteps' flashlight tip all do their own),
/// so a planet's local day changes only as it ORBITS. Every body therefore has
/// its own day length, and two points on the same planet are at opposite local
/// times at once.
///
/// Making this clock drive the sky would mean giving planets axial rotation
/// inside NBodySimulation — a core system that is off-limits, and one the
/// floating-origin and multiplayer layers are welded to. So the clock stays
/// independent, and GST reading 03:00 under a bright sky is expected, not a
/// bug. Anything that needs to know whether it is dark locally must keep doing
/// the dot-product test; do NOT reroute those through this class.
///
/// ── Scaled time, on purpose ──────────────────────────────────────────────
/// Advances on Time.deltaTime, so it stops dead when the game is paused and
/// stretches under slow-mo. That matches the systems a player will actually
/// correlate the clock against — SaplingGrowth, MushroomGrowth and BubbleDome
/// fuel all run on scaled time — so "my crop is ready around 14:00" stays true.
///
/// The buyer/messages economy (BuyerLedger, BuyerMessageDirector,
/// MushroomDealState) runs on Time.unscaledTime instead, deliberately, so that
/// pausing can't stretch a delivery window. Those two families already disagree
/// across a long pause today; this clock does not make that worse, but it does
/// make it VISIBLE. If deal windows are ever re-expressed in galactic hours,
/// change them and their countdown UI together.
/// </summary>
public class GalaxyTime : MonoBehaviour
{
    public static GalaxyTime Instance { get; private set; }

    public const double MinutesPerHour = 60.0;
    public const double HoursPerDay    = 24.0;
    public const double MinutesPerDay  = MinutesPerHour * HoursPerDay;   // 1440
    /// Days in a galactic week — what rent and any other recurring bill count in.
    public const int    DaysPerWeek    = 7;

    /// Total in-game minutes since the epoch (day 1, 00:00). Absolute, saved as
    /// such. NOT stored relative the way buyer deadlines are — those persist a
    /// remaining duration, this persists a point in time.
    double _minutes;

    /// Day number, 1-based — the player lands on Day 1.
    public int Day => (int)(_minutes / MinutesPerDay) + 1;
    /// Week number, 1-based.
    public int Week => (Day - 1) / DaysPerWeek + 1;
    public int Hour => (int)(_minutes % MinutesPerDay / MinutesPerHour);
    public int Minute => (int)(_minutes % MinutesPerHour);

    public double TotalMinutes => _minutes;
    public double TotalDays => _minutes / MinutesPerDay;

    /// "14:07" — zero-padded 24-hour. Allocates, so call it only when the
    /// displayed minute has actually changed (see GalaxyTimeHUD).
    public string ClockString => $"{Hour:00}:{Minute:00}";

    /// Fires when the day number rolls over, with the NEW day. This is the hook
    /// recurring costs should use — rent, upkeep, anything billed per day or
    /// per week — rather than each system polling for a rollover itself.
    public static event Action<int> OnDayChanged;

    int _lastDaySeen;

    // ── Lifecycle ────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        // Trap #1: this early-return means AutoCreate never fires in a BUILD,
        // because the build's first scene IS MainMenu. GalaxyTime is therefore
        // also seeded from MainMenuController.EnsureGameplaySingletons — if you
        // remove that line, the clock silently never exists outside the Editor.
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("GalaxyTime");
        DontDestroyOnLoad(go);
        go.AddComponent<GalaxyTime>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // A fresh run starts mid-morning rather than at 00:00 so the player's
        // first day has a shape to it and the first rent day is a full cycle away.
        if (_minutes <= 0.0) _minutes = startHour * MinutesPerHour;
        _lastDaySeen = Day;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        _minutes += Time.deltaTime * inGameMinutesPerRealSecond;

        int day = Day;
        if (day != _lastDaySeen)
        {
            _lastDaySeen = day;
            OnDayChanged?.Invoke(day);
        }
    }

    // ── Save / New Game ──────────────────────────────────────────────────

    /// Absolute minutes, for SaveCollector.
    public double MinutesForSave => _minutes;

    /// Restore from a save. A pre-feature save has 0, which reads as "start of
    /// day 1" — so old saves land on the same opening the game already gives.
    public void RestoreMinutes(double minutes)
    {
        _minutes = minutes > 0.0 ? minutes : startHour * MinutesPerHour;
        _lastDaySeen = Day;
    }

    /// New Game must reset this explicitly — the clock lives on a
    /// DontDestroyOnLoad singleton, and New Game runs no Apply, so without this
    /// the previous run's date leaks straight through the main menu.
    public void ResetForNewGame()
    {
        _minutes = startHour * MinutesPerHour;
        _lastDaySeen = Day;
    }

    // ── Tunables ─────────────────────────────────────────────────────────

    [Header("Rate")]
    [Tooltip("In-game minutes that pass per real second. 1 = Sam's spec: an in-game hour takes a real minute, so a full 24-hour day is 24 real minutes and a 7-day week is 2h48m of play.\n\nRaise it to make days fly by (2 = a 12-minute day); lower it to stretch them out. Everything that schedules against the clock — rent, and anything added later — follows automatically.")]
    [SerializeField] double inGameMinutesPerRealSecond = 1.0;

    [Tooltip("Hour of day a new game starts at, 0-23. 8 = the player lands at 08:00 on Day 1.")]
    [SerializeField] double startHour = 8.0;
}
