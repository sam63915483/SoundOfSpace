using UnityEngine;

/// <summary>
/// One team's identity and its five 0–1 stats (handoff §4 "team stat block").
/// The stats feed the timers and error terms in the brains — pocket lifetime
/// (blocking vs passRush), coverage reaction delay, throw error, run speed —
/// and are what makes two teams distinguishable and what betting odds will
/// read from later. Keep it to five numbers.
/// </summary>
[System.Serializable]
public class FootballTeam
{
    public string name = "Team";
    public string shortName = "TM";         // 3-4 letters for the scoreboard
    public Color color = Color.white;

    [Range(0f, 1f)] public float blocking   = 0.5f;
    [Range(0f, 1f)] public float passRush   = 0.5f;
    [Range(0f, 1f)] public float coverage   = 0.5f;
    [Range(0f, 1f)] public float qbAccuracy = 0.5f;
    [Range(0f, 1f)] public float speed      = 0.5f;

    // Match bookkeeping (not stats) — reset by FootballMatch on a new game.
    [System.NonSerialized] public int score;
    [System.NonSerialized] public int attackDir = 1;    // +1 attacks toward +Z, −1 toward −Z; flips at the half
    [System.NonSerialized] public int index;            // 0 = home, 1 = away

    public static FootballTeam DefaultHome() => new FootballTeam
    {
        name = "Cyclops Crushers", shortName = "CYC", color = new Color(0.85f, 0.15f, 0.12f),
        blocking = 0.6f, passRush = 0.5f, coverage = 0.5f, qbAccuracy = 0.65f, speed = 0.5f,
    };

    public static FootballTeam DefaultAway() => new FootballTeam
    {
        name = "Twin Terrors", shortName = "TWN", color = new Color(0.15f, 0.35f, 0.95f),
        blocking = 0.5f, passRush = 0.65f, coverage = 0.6f, qbAccuracy = 0.5f, speed = 0.6f,
    };
}
