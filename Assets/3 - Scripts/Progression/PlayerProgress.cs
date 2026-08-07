using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The five progression tracks. Order is the display order on the phone and is
/// also the SAVE ORDER — the save stores a flat int[] indexed by this enum, so
/// NEVER reorder or insert in the middle. Append only.
/// </summary>
public enum ProgressTrack
{
    TreeKiller = 0,   // +1 per felled tree
    TreeDaddy  = 1,   // +1 per planted sapling
    Colonizer  = 2,   // +1 per structure placed from the Build app
    GangstaRep = 3,   // +1 enemy, +3 Elite, -1 alien NPC
    Explorer   = 4,   // +1 per world reached (discrete — see WorldNames)
}

/// <summary>
/// Central progression counter. Five tracks, each with its own level curve and
/// its own max level; the "general level" shown on the phone is the mean of the
/// five tracks' PERCENT completion, not the mean of their level numbers.
///
/// Why percent and not levels: Explorer can only ever have 9 levels (there are
/// 9 worlds), while the others have 10. Averaging raw level numbers would make
/// a maxed Explorer look permanently worse than a maxed Tree Killer. Averaging
/// percent means every track contributes 0..100% regardless of how many levels
/// it has — so re-scaling a track later (e.g. Tree Daddy becoming "100% oxygen
/// on every planet", which is far more than 10 steps) needs no maths changes
/// anywhere else.
///
/// Auto-singleton with MainMenu skip — ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1).
/// </summary>
public class PlayerProgress : MonoBehaviour
{
    public static PlayerProgress Instance { get; private set; }

    public const int TrackCount = 5;

    /// Fired on every scoring action. `delta` is the signed amount added (so an
    /// Elite kill is +3 and an alien kill is -1); `leveledUp` is true only on the
    /// action that crossed a threshold. ProgressToastUI is the main subscriber.
    public static event Action<ProgressTrack, int, bool> OnTrackChanged;

    /// Fired only when a track crosses a level threshold, with the levels it
    /// crossed FROM and TO. OnTrackChanged already carries a `leveledUp` flag,
    /// but not the old level — and anything that wants to know WHAT a level-up
    /// granted (BuildableUnlocks.UnlockedBetween) needs both ends.
    public static event Action<ProgressTrack, int, int> OnTrackLevelUp;

    /// Fired when the GENERAL level (the mean-percent number on the phone)
    /// increases. Increase only: Gangsta Rep can be spent down by murdering
    /// aliens, and a ceremony for losing a level would be a strange reward.
    /// LevelUpCeremonyUI is the subscriber.
    public static event Action<int, int> OnGeneralLevelUp;

    // ── Curve ────────────────────────────────────────────────────────────────
    // Cumulative score needed to REACH each level, one entry per level 1..10.
    // Deliberately shallow at the start (level 1 on your very first action, so
    // the tutorial toast fires immediately) and steep at the end.
    // Steps are 1,2,3,4,6,8,11,15,20,30.
    static readonly int[] BaseCurve = { 1, 3, 6, 10, 16, 24, 35, 50, 70, 100 };

    // Per-track multiplier on BaseCurve. Kills and placements are much slower
    // actions than axe swings, so their curves are compressed.
    static readonly float[] CurveScale =
    {
        1.0f,   // TreeKiller  → 1,3,6,10,16,24,35,50,70,100
        1.0f,   // TreeDaddy   → same
        0.8f,   // Colonizer   → 1,2,5,8,13,19,28,40,56,80
        0.6f,   // GangstaRep  → 1,2,4,6,10,14,21,30,42,60
        1.0f,   // Explorer    — UNUSED, Explorer has its own discrete curve
    };

    // Explorer is discrete: one level per world reached, so its curve is just
    // 1..WorldCount. Nine worlds — the four planets, the three moons, the Sun
    // ("touching" it) and the Black Hole (entering it).
    public static readonly string[] WorldNames =
    {
        "Cyclops", "Fiery Twin", "Icey Twin", "Humble Abode",
        "Watchful Eye", "Constant Companion", "Tumbling Bean",
        "Sun", "Black Hole",
    };

    // Resolved once — thresholds[track][level-1] = cumulative score for that level.
    static int[][] _thresholds;

    static int[][] Thresholds
    {
        get
        {
            if (_thresholds != null) return _thresholds;
            _thresholds = new int[TrackCount][];
            for (int t = 0; t < TrackCount; t++)
            {
                if (t == (int)ProgressTrack.Explorer)
                {
                    var ex = new int[WorldNames.Length];
                    for (int i = 0; i < ex.Length; i++) ex[i] = i + 1;
                    _thresholds[t] = ex;
                    continue;
                }
                var arr = new int[BaseCurve.Length];
                for (int i = 0; i < arr.Length; i++)
                    arr[i] = Mathf.Max(i + 1, Mathf.RoundToInt(BaseCurve[i] * CurveScale[t]));
                _thresholds[t] = arr;
            }
            return _thresholds;
        }
    }

    // ── State ────────────────────────────────────────────────────────────────
    // Raw score per track. GangstaRep is the only one that can go NEGATIVE —
    // it's stored signed so the phone can show "-7 REP" and call you a menace,
    // but LevelOf() floors the level at 0 so a negative can never drag the
    // general level below zero.
    readonly int[] _score = new int[TrackCount];

    // Worlds already counted, so revisiting Humble Abode forever doesn't farm
    // Explorer. Stored as names because CelestialBody has no stable id.
    readonly HashSet<string> _visited = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("PlayerProgress");
        DontDestroyOnLoad(go);
        go.AddComponent<PlayerProgress>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ── Queries ──────────────────────────────────────────────────────────────
    public int ScoreOf(ProgressTrack t) => _score[(int)t];

    public static int MaxLevelOf(ProgressTrack t) => Thresholds[(int)t].Length;

    /// Current level 0..MaxLevelOf(t). Floors at 0 (see the GangstaRep note).
    public int LevelOf(ProgressTrack t) => LevelForScore(t, _score[(int)t]);

    /// Level a HYPOTHETICAL score would sit at. The toast uses this to work out
    /// where the bar was BEFORE the action that spawned it, so it can animate
    /// from there to now instead of appearing already-full.
    public static int LevelForScore(ProgressTrack t, int score)
    {
        int s = Mathf.Max(0, score);
        var th = Thresholds[(int)t];
        int lv = 0;
        for (int i = 0; i < th.Length && s >= th[i]; i++) lv = i + 1;
        return lv;
    }

    /// 0..1 progress across the level a hypothetical score sits in. Pairs with
    /// LevelForScore for the toast's "animate from where you were" bar.
    public static float LevelProgressForScore(ProgressTrack t, int score)
    {
        var th = Thresholds[(int)t];
        int lv = LevelForScore(t, score);
        if (lv >= th.Length) return 1f;
        int lo = lv <= 0 ? 0 : th[lv - 1];
        int hi = th[lv];
        if (hi <= lo) return 1f;
        return Mathf.Clamp01((Mathf.Max(0, score) - lo) / (float)(hi - lo));
    }

    /// 0..1 completion of the whole track — this is what the general level averages.
    public float PercentOf(ProgressTrack t) => LevelOf(t) / (float)MaxLevelOf(t);

    /// Cumulative score at which the NEXT level lands, or the final threshold
    /// when the track is already maxed (so UI can show "100 / 100").
    public int NextThresholdOf(ProgressTrack t)
    {
        var th = Thresholds[(int)t];
        int lv = LevelOf(t);
        return lv >= th.Length ? th[th.Length - 1] : th[lv];
    }

    /// Score at which the CURRENT level started — for drawing a bar that fills
    /// across the current level rather than from zero.
    public int CurrentThresholdOf(ProgressTrack t)
    {
        int lv = LevelOf(t);
        return lv <= 0 ? 0 : Thresholds[(int)t][lv - 1];
    }

    /// 0..1 progress across the CURRENT level. Returns 1 on a maxed track.
    public float LevelProgressOf(ProgressTrack t)
    {
        if (LevelOf(t) >= MaxLevelOf(t)) return 1f;
        int lo = CurrentThresholdOf(t), hi = NextThresholdOf(t);
        if (hi <= lo) return 1f;
        return Mathf.Clamp01((Mathf.Max(0, _score[(int)t]) - lo) / (float)(hi - lo));
    }

    public bool IsMaxed(ProgressTrack t) => LevelOf(t) >= MaxLevelOf(t);

    /// Mean percent completion across all five tracks, 0..1.
    public float GeneralPercent
    {
        get
        {
            float sum = 0f;
            for (int t = 0; t < TrackCount; t++) sum += PercentOf((ProgressTrack)t);
            return sum / TrackCount;
        }
    }

    /// The number shown on the phone status bar, 0..10.
    public int GeneralLevel => Mathf.RoundToInt(GeneralPercent * 10f);

    public bool HasVisited(string worldName) => _visited.Contains(worldName);
    public int VisitedCount => _visited.Count;

    // ── Scoring ──────────────────────────────────────────────────────────────
    public void AddTreeFelled()      => Add(ProgressTrack.TreeKiller, 1);
    public void AddSaplingPlanted()  => Add(ProgressTrack.TreeDaddy, 1);
    public void AddStructurePlaced() => Add(ProgressTrack.Colonizer, 1);

    /// +1 for a regular enemy, +3 for an Elite ("big") one.
    public void AddEnemyKill(EnemyKind kind)
        => Add(ProgressTrack.GangstaRep, kind == EnemyKind.Elite ? 3 : 1);

    /// -1 for murdering a peaceful alien. One enemy kill exactly cancels it.
    public void AddAlienKill() => Add(ProgressTrack.GangstaRep, -1);

    /// Marks a world reached. No-op (returns false) if it was already counted,
    /// so flying past Humble Abode a hundred times only ever scores once.
    public bool VisitWorld(string worldName)
    {
        if (string.IsNullOrEmpty(worldName)) return false;
        if (!_visited.Add(worldName)) return false;
        Add(ProgressTrack.Explorer, 1);
        return true;
    }

    /// Core mutator — everything above funnels here so there's exactly one place
    /// that decides whether an action produced a level-up.
    public void Add(ProgressTrack t, int delta)
    {
        if (delta == 0) return;
        int before = LevelOf(t);
        int generalBefore = GeneralLevel;
        _score[(int)t] += delta;
        int after = LevelOf(t);
        int generalAfter = GeneralLevel;

        OnTrackChanged?.Invoke(t, delta, after > before);
        if (after > before) OnTrackLevelUp?.Invoke(t, before, after);
        // Last, so the grand ceremony queues behind the track toast that caused
        // it rather than cutting in front of it.
        if (generalAfter > generalBefore) OnGeneralLevelUp?.Invoke(generalBefore, generalAfter);
    }

    // ── Save / load ──────────────────────────────────────────────────────────
    // Flat arrays only — SaveData is JsonUtility, which can't do dictionaries.
    public int[] CaptureScores() => (int[])_score.Clone();
    public string[] CaptureVisited() { var a = new string[_visited.Count]; _visited.CopyTo(a); return a; }

    public void ApplyState(int[] scores, string[] visited)
    {
        for (int i = 0; i < TrackCount; i++)
            _score[i] = (scores != null && i < scores.Length) ? scores[i] : 0;
        _visited.Clear();
        if (visited != null)
            foreach (var v in visited)
                if (!string.IsNullOrEmpty(v)) _visited.Add(v);
    }

    /// Called by NewGameReset — a fresh run must not inherit the previous
    /// session's levels through the DontDestroyOnLoad singleton.
    public void ResetAll()
    {
        for (int i = 0; i < TrackCount; i++) _score[i] = 0;
        _visited.Clear();
    }

    // ── Presentation helpers (shared by the toast and the phone page) ────────
    public static string DisplayName(ProgressTrack t)
    {
        switch (t)
        {
            case ProgressTrack.TreeKiller: return "TREE KILLER";
            case ProgressTrack.TreeDaddy:  return "TREE DADDY";
            case ProgressTrack.Colonizer:  return "COLONIZER";
            case ProgressTrack.GangstaRep: return "GANGSTA REP";
            case ProgressTrack.Explorer:   return "EXPLORER";
        }
        return t.ToString();
    }

    // Matches the mockup palette exactly.
    public static Color ColorOf(ProgressTrack t)
    {
        switch (t)
        {
            case ProgressTrack.TreeKiller: return new Color32(0xFF, 0x9F, 0x45, 0xFF);
            case ProgressTrack.TreeDaddy:  return new Color32(0x6F, 0xE3, 0x8A, 0xFF);
            case ProgressTrack.Colonizer:  return new Color32(0x5C, 0xC8, 0xFF, 0xFF);
            case ProgressTrack.GangstaRep: return new Color32(0xFF, 0xD2, 0x4A, 0xFF);
            case ProgressTrack.Explorer:   return new Color32(0xB9, 0x8C, 0xFF, 0xFF);
        }
        return Color.white;
    }

    /// Red for the one case where a track can go backwards.
    public static readonly Color NegativeColor = new Color32(0xFF, 0x5C, 0x5C, 0xFF);

    /// "41 / 55 felled" style sub-label for the phone page.
    public string SubLabelOf(ProgressTrack t)
    {
        if (t == ProgressTrack.Explorer)
            return $"{VisitedCount} / {WorldNames.Length} worlds";
        int score = Mathf.Max(0, _score[(int)t]);
        string noun;
        switch (t)
        {
            case ProgressTrack.TreeKiller: noun = "felled";  break;
            case ProgressTrack.TreeDaddy:  noun = "planted"; break;
            case ProgressTrack.Colonizer:  noun = "placed";  break;
            default:                       noun = "rep";     break;
        }
        if (t == ProgressTrack.GangstaRep)
            return $"{_score[(int)t]:+#;-#;0} rep";      // signed, so -7 reads as -7
        if (IsMaxed(t)) return $"{score} {noun} · MAX";
        return $"{score} / {NextThresholdOf(t)} {noun}";
    }
}
