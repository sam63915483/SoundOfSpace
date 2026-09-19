using UnityEngine;

/// <summary>
/// The football field's geometry — the one place its size lives
/// (docs/Handoff_AlienFootball_Phase1_v1.md §3, §10).
///
/// Everything football is expressed in FIELD space: metres, relative to the
/// FieldRoot transform this sits on. +Z runs down the field from the HOME end
/// zone (−Z) to the AWAY end zone (+Z); X is across the field; up is
/// FieldRoot.up, never Vector3.up. Convert to world only to render or to feed
/// physics, and always through ToWorld/ToLocal so a field parented to a planet
/// (curved "down", floating origin) keeps working (§10).
///
/// Yards: the markings and the play-by-play talk in yards; the sim runs in
/// metres. MetresPerYard is the single conversion. It is 1.0 here on purpose —
/// a real yard is 0.914 m, but 1 m per yard fills the stadium's 105 m pitch
/// with a 120-yard field (goal lines 8 m short of the grass ends) and makes
/// every number readable at a glance. Retune here and every line, number and
/// route moves with it.
/// </summary>
public class FootballField : MonoBehaviour
{
    public const float MetresPerYard = 1.0f;

    // Regulation proportions, in yards.
    public const float PlayingLengthYd = 100f;      // goal line to goal line
    public const float EndZoneYd       = 10f;
    public const float WidthYd         = 53.333f;   // 160 ft
    public const float FirstDownYd     = 10f;

    public static float PlayingLength => PlayingLengthYd * MetresPerYard;
    public static float EndZone       => EndZoneYd * MetresPerYard;
    public static float Width         => WidthYd * MetresPerYard;
    public static float HalfWidth     => Width * 0.5f;
    /// Goal lines sit at z = ±GoalLineZ; end lines at ±EndLineZ.
    public static float GoalLineZ     => PlayingLength * 0.5f;
    public static float EndLineZ      => GoalLineZ + EndZone;

    public static float Yards(float yd) => yd * MetresPerYard;
    public static float ToYards(float metres) => metres / MetresPerYard;

    // ── frame conversions ─────────────────────────────────────────────────

    public Vector3 Up => transform.up;

    public Vector3 ToWorld(Vector3 fieldPos) => transform.TransformPoint(fieldPos);
    public Vector3 ToLocal(Vector3 worldPos) => transform.InverseTransformPoint(worldPos);
    public Vector3 DirToWorld(Vector3 fieldDir) => transform.TransformDirection(fieldDir);
    public Vector3 DirToLocal(Vector3 worldDir) => transform.InverseTransformDirection(worldDir);

    /// Field-space position of a spot given in yards from the HOME goal line
    /// (0 = home goal line, 50 = midfield, 100 = away goal line) and yards
    /// across from the centre (+ = +X).
    public static Vector3 SpotFromYardLine(float yardsFromHomeGoal, float acrossYd = 0f)
        => new Vector3(Yards(acrossYd), 0f, -GoalLineZ + Yards(yardsFromHomeGoal));

    /// The yard-line label a field-space z reads as, the way a scoreboard says
    /// it: 50 at midfield, counting DOWN toward either goal line (0 at both).
    public static float YardLineLabel(float z)
    {
        float fromHome = ToYards(z + GoalLineZ);
        return fromHome <= PlayingLengthYd * 0.5f ? fromHome : PlayingLengthYd - fromHome;
    }
}
