using System.Collections;
using UnityEngine;

/// <summary>
/// THE LOST KID (Floorbin / Shllorbin, Humble Abode) -- docs/Handoff_BountyQuest_Grulabu_v1.md
/// with Sam's 2026-09-03 changes: the kid FOLLOWS you home (no despawn/teleport),
/// and the reunion is a beat -- they run to each other, jump for joy, then the
/// parent thanks you and tells you where he saw the bounty fish.
///
/// Put this on any object (the KID empty is fine) and assign both spawners and
/// both talk components. It owns the kid's placement (lost spot / behind the
/// player / home) from the save flags, the follow loop, and the reunion.
///
/// State = StoryDirector flags (world save, shared in co-op via the snapshot):
///   floorbin_name_learned, shllorbin_following, shllorbin_returned,
///   bounty_spot_known, grulabu_caught (BountyZone sets that one).
/// No new save schema.
/// </summary>
public class LostKidQuest : MonoBehaviour
{
    public const string FlagNameLearned   = "floorbin_name_learned";
    public const string FlagFollowing     = "shllorbin_following";
    public const string FlagReturned      = "shllorbin_returned";
    public const string FlagSpotKnown     = "bounty_spot_known";
    public const string FlagGrulabuCaught = "grulabu_caught";

    [Header("NPCs")]
    public AuthoredNPCSpawner parentSpawner;
    public AuthoredNPCSpawner kidSpawner;
    public FloorbinTalk parentTalk;
    public ShllorbinTalk kidTalk;

    [Header("Follow")]
    [Tooltip("Metres behind the player the kid stops.")]
    public float followStopDistance = 2.2f;
    [Tooltip("Beyond this the kid RUNS to catch up.")]
    public float runDistance = 14f;
    [Tooltip("Beyond this he is re-seated behind you (lost him over a ridge / in the water).")]
    public float warpDistance = 90f;
    public float walkSpeedMultiplier = 1.4f;
    public float runSpeedMultiplier = 2.6f;

    [Header("Reunion")]
    [Tooltip("The following kid this close to the parent triggers the reunion.")]
    public float reunionDistance = 5f;
    public float celebrateSeconds = 10f;
    public float hopHeight = 0.35f;
    public float hopsPerSecond = 2.2f;
    [Tooltip("After the celebration the parent's thank-you starts by itself if you are within this range; otherwise it plays when you next talk to him.")]
    public float thankYouAutoStartDistance = 16f;

    Coroutine _follow;
    bool _reunionRunning;

    public bool IsFollowing => _follow != null;

    static bool Flag(string n) => StoryDirector.Instance != null && StoryDirector.Instance.GetFlag(n);
    static void Set(string n, bool v) { if (StoryDirector.Instance != null) StoryDirector.Instance.SetFlag(n, v); }

    void Awake()
    {
        if (kidSpawner != null) kidSpawner.autoSpawn = false;   // placement is decided here
        if (parentTalk == null && parentSpawner != null) parentTalk = parentSpawner.GetComponent<FloorbinTalk>();
        if (kidTalk == null && kidSpawner != null) kidTalk = kidSpawner.GetComponent<ShllorbinTalk>();
    }

    IEnumerator Start()
    {
        if (parentSpawner == null || kidSpawner == null)
        {
            Debug.LogError("[LostKidQuest] assign parentSpawner and kidSpawner.", this);
            yield break;
        }
        // The parent seats first (his raycast waits for the terrain); by then the
        // save has long been applied, so the flags below are the loaded ones.
        yield return new WaitUntil(() => parentSpawner.Spawned);

        Transform player = LocalPlayer();
        if (Flag(FlagReturned))
            kidSpawner.SpawnAtWorld(AuthoredNPCSpawner.Beside(parentSpawner.Body.transform, 2.5f));
        else if (Flag(FlagFollowing) && player != null)
            kidSpawner.SpawnAtWorld(player.position - player.forward * 3f);
        else
            kidSpawner.SpawnAtWorld(kidSpawner.transform.position);

        yield return new WaitUntil(() => kidSpawner.Spawned);
        if (Flag(FlagFollowing) && !Flag(FlagReturned)) BeginFollow();
    }

    static Transform LocalPlayer()
    {
        var all = PlayerRoster.All();
        for (int i = 0; i < all.Count; i++)
            if (all[i].IsLocal && all[i].Transform != null) return all[i].Transform;
        var go = GameObject.FindWithTag("Player");
        return go != null ? go.transform : null;
    }

    public void BeginFollow()
    {
        if (_follow != null || kidSpawner == null || !kidSpawner.Spawned) return;
        Set(FlagFollowing, true);
        _follow = StartCoroutine(FollowRoutine());
    }

    /// <summary>"Wait here": he stops and makes this spot his new stroll centre.</summary>
    public void StopFollow()
    {
        if (_follow != null) { StopCoroutine(_follow); _follow = null; }
        Set(FlagFollowing, false);
        var w = kidSpawner != null ? kidSpawner.Wander : null;
        if (w != null) { w.EndApproach(); w.SpeedMultiplier = 1f; w.ReHome(); }
    }

    IEnumerator FollowRoutine()
    {
        var w = kidSpawner.Wander;
        Transform kid = kidSpawner.Body.transform;
        float retryAt = 0f;

        while (true)
        {
            Transform player = LocalPlayer();
            if (player == null || w == null) { yield return null; continue; }

            // Reunion: the following kid gets close to the parent.
            if (parentSpawner.Spawned
                && (kid.position - parentSpawner.Body.transform.position).sqrMagnitude
                   <= reunionDistance * reunionDistance)
            {
                _follow = null;
                yield return Reunion();
                yield break;
            }

            float dist = (player.position - kid.position).magnitude;
            if (dist > warpDistance)
            {
                w.EndApproach();
                kidSpawner.TeleportNear(player.position - player.forward * 3f);
                dist = 3f;
            }
            w.SpeedMultiplier = dist > runDistance ? runSpeedMultiplier : walkSpeedMultiplier;

            // Hold while he is talking to the player (the talk script sets Hold).
            if (w.Hold) { yield return null; continue; }

            if (w.ApproachBlocked)
            {
                // Water or a cliff between us: stand down, try again shortly
                // (the player usually walks on and a path opens).
                if (Time.time >= retryAt)
                {
                    w.EndApproach();
                    retryAt = Time.time + 0.6f;
                }
            }
            else if (!w.Approaching || (w.ApproachArrived && dist > followStopDistance + 1.5f))
            {
                if (Time.time >= retryAt) w.BeginApproach(player, followStopDistance, 1e9f);
            }
            yield return null;
        }
    }

    IEnumerator Reunion()
    {
        if (_reunionRunning) yield break;
        _reunionRunning = true;
        Set(FlagFollowing, false);

        var kw = kidSpawner.Wander;
        var pw = parentSpawner.Wander;
        Transform kid = kidSpawner.Body.transform;
        Transform par = parentSpawner.Body.transform;

        // They run to each other.
        kw.EndApproach(); pw.EndApproach();
        kw.SpeedMultiplier = runSpeedMultiplier;
        pw.SpeedMultiplier = runSpeedMultiplier;
        kw.BeginApproach(par, 1.4f, 8f);
        pw.BeginApproach(kid, 1.4f, 8f);
        float until = Time.time + 6f;
        while (Time.time < until)
        {
            bool kidDone = kw.ApproachArrived || kw.ApproachBlocked;
            bool parDone = pw.ApproachArrived || pw.ApproachBlocked;
            if (kidDone && parDone) break;
            if ((kid.position - par.position).sqrMagnitude < 1.8f * 1.8f) break;
            yield return null;
        }
        kw.EndApproach(); pw.EndApproach();
        kw.SpeedMultiplier = 1f; pw.SpeedMultiplier = 1f;

        // Jump for joy: both bodies hop on their local radial for celebrateSeconds.
        kw.Hold = true; pw.Hold = true;
        Vector3 kidBase = kid.localPosition, parBase = par.localPosition;
        Vector3 kidUp = kidBase.normalized, parUp = parBase.normalized;
        float t0 = Time.time;
        while (Time.time - t0 < celebrateSeconds)
        {
            float t = Time.time - t0;
            float y = Mathf.Abs(Mathf.Sin(t * Mathf.PI * hopsPerSecond)) * hopHeight;
            kid.localPosition = kidBase + kidUp * y;
            par.localPosition = parBase + parUp * (y * 0.8f);
            yield return null;
        }
        kid.localPosition = kidBase;
        par.localPosition = parBase;
        kw.Hold = false; pw.Hold = false;

        // Then they chill: the kid lives here now.
        kw.ReHome(); pw.ReHome();
        Set(FlagReturned, true);
        _reunionRunning = false;

        // The thank-you (and the bounty sighting) starts by itself if you're nearby.
        Transform player = LocalPlayer();
        if (parentTalk != null && player != null
            && (player.position - par.position).sqrMagnitude
               <= thankYouAutoStartDistance * thankYouAutoStartDistance)
            parentTalk.ForceStart();
    }
}
