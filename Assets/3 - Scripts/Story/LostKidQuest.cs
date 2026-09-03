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
/// player / home) from the save flags, the follow loop, the reunion, and the
/// parent's FRANTIC behaviour while the kid is lost (paces fast, hops about,
/// and the first time you come within greetDistance he runs up and asks).
///
/// State = StoryDirector flags (world save, shared in co-op via the snapshot):
///   floorbin_name_learned, floorbin_approached, shllorbin_following,
///   shllorbin_returned, bounty_spot_known, grulabu_caught (BountyZone),
///   grulabu_turned_in (FishMarketNPC). No new save schema.
/// </summary>
public class LostKidQuest : MonoBehaviour
{
    public const string FlagNameLearned   = "floorbin_name_learned";
    public const string FlagApproached    = "floorbin_approached";
    public const string FlagFollowing     = "shllorbin_following";
    public const string FlagReturned      = "shllorbin_returned";
    public const string FlagSpotKnown     = "bounty_spot_known";
    public const string FlagGrulabuCaught = "grulabu_caught";
    public const string FlagGrulabuTurnedIn = "grulabu_turned_in";

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
    public float reunionDistance = 15f;
    public float celebrateSeconds = 10f;
    public float hopHeight = 0.35f;
    public float hopsPerSecond = 2.2f;
    [Tooltip("They circle each other while jumping: radius of the ring (m) and laps over the celebration.")]
    public float celebrateRingRadius = 1.3f;
    public float celebrateLaps = 2f;
    [Tooltip("After the celebration the parent's thank-you starts by itself if you are within this range; otherwise it plays when you next talk to him.")]
    public float thankYouAutoStartDistance = 16f;

    [Header("Frantic parent (while the kid is lost)")]
    [Tooltip("Pacing speed multiplier while searching.")]
    public float franticSpeedMultiplier = 1.9f;
    [Tooltip("Idle time between strolls, as a fraction of the normal idle (0.35 = barely stands still).")]
    public float franticIdleScale = 0.35f;
    public float franticHopHeight = 0.28f;
    public float franticHopsPerSecond = 2.6f;
    [Tooltip("Seconds of a hopping burst.")]
    public float franticHopBurstMin = 0.8f;
    public float franticHopBurstMax = 1.6f;
    [Tooltip("Seconds between hopping bursts.")]
    public float franticHopGapMin = 2f;
    public float franticHopGapMax = 5f;
    [Tooltip("The first time you come within this many metres he runs up to you and asks about his kid. Once per world.")]
    public float greetDistance = 15f;
    public float greetStopDistance = 2.6f;

    Coroutine _follow;
    bool _reunionRunning;
    bool _greeting;

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
        StartCoroutine(ParentRoutine());

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

    // ── The frantic parent ─────────────────────────────────────────────────
    // Paces fast with hardly any standing still, hops about in bursts, and
    // runs up to the player the first time they come near. All of it stops
    // the moment the kid is home.
    IEnumerator ParentRoutine()
    {
        var pw = parentSpawner.Wander;
        Transform par = parentSpawner.Body.transform;
        float nextHopAt = Time.time + Random.Range(franticHopGapMin, franticHopGapMax);
        float hopUntil = 0f;

        while (!Flag(FlagReturned))
        {
            if (pw == null) { yield return null; continue; }

            bool busy = _reunionRunning || _greeting || pw.Hold;
            if (!busy)
            {
                pw.SpeedMultiplier = franticSpeedMultiplier;
                pw.IdleScale = franticIdleScale;

                if (Time.time >= nextHopAt && hopUntil <= 0f)
                {
                    pw.BounceHeight = franticHopHeight;
                    pw.BounceHz = franticHopsPerSecond;
                    hopUntil = Time.time + Random.Range(franticHopBurstMin, franticHopBurstMax);
                }
                if (hopUntil > 0f && Time.time >= hopUntil)
                {
                    pw.BounceHeight = 0f;
                    hopUntil = 0f;
                    nextHopAt = Time.time + Random.Range(franticHopGapMin, franticHopGapMax);
                }

                // First contact: he spots you and comes running. Once per world.
                Transform player = LocalPlayer();
                if (!Flag(FlagApproached) && player != null && parentTalk != null && !parentTalk.IsTalking
                    && (player.position - par.position).sqrMagnitude <= greetDistance * greetDistance)
                {
                    Set(FlagApproached, true);
                    pw.BounceHeight = 0f; hopUntil = 0f;
                    yield return GreetRunUp(player);
                    continue;
                }
            }
            else if (hopUntil > 0f)
            {
                pw.BounceHeight = 0f;
                hopUntil = 0f;
            }
            yield return null;
        }

        pw.BounceHeight = 0f;
        pw.SpeedMultiplier = 1f;
        pw.IdleScale = 1f;
    }

    IEnumerator GreetRunUp(Transform player)
    {
        _greeting = true;
        var pw = parentSpawner.Wander;
        pw.EndApproach();
        pw.SpeedMultiplier = runSpeedMultiplier;
        pw.BeginApproach(player, greetStopDistance, 12f);
        float until = Time.time + 12f;
        while (Time.time < until && !pw.ApproachArrived && !pw.ApproachBlocked) yield return null;
        bool close = (player.position - parentSpawner.Body.transform.position).sqrMagnitude <= 7f * 7f;
        pw.EndApproach();
        pw.SpeedMultiplier = franticSpeedMultiplier;
        _greeting = false;
        if (close && parentTalk != null) parentTalk.ForceStart();
    }

    // ── The follower ───────────────────────────────────────────────────────
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
                // Water too deep or a cliff between us: stand down, try again
                // shortly (the player usually walks on and a path opens).
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

    // One dancer: seated on the terrain under the ring point, lifted by the hop,
    // turned to face the other. Planet-local throughout.
    static void PlaceOnRing(Transform body, AlienWander w, Vector3 ringLocal, Vector3 otherLocal, float hop)
    {
        Vector3 seat = ringLocal;
        if (w.TryGroundAt(ringLocal, out Vector3 g)) seat = g - g.normalized * w.SeatDepth;
        Vector3 up = seat.normalized;
        body.localPosition = seat + up * hop;
        Vector3 face = Vector3.ProjectOnPlane(otherLocal - seat, up);
        if (face.sqrMagnitude > 1e-6f)
            body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.LookRotation(face.normalized, up), 10f * Time.deltaTime);
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
        pw.BounceHeight = 0f;
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

        // Jump for joy: both circle their midpoint on opposite sides, hopping,
        // facing each other, feet seated on the terrain every frame.
        kw.Hold = true; pw.Hold = true;
        kw.BounceHeight = 0f; pw.BounceHeight = 0f;
        Vector3 mid = (kid.localPosition + par.localPosition) * 0.5f;
        Vector3 up = mid.normalized;
        Vector3 t1 = Vector3.ProjectOnPlane(kid.localPosition - par.localPosition, up);
        if (t1.sqrMagnitude < 1e-4f) t1 = Vector3.ProjectOnPlane(Vector3.right, up);
        t1.Normalize();
        Vector3 t2 = Vector3.Cross(up, t1).normalized;
        float t0 = Time.time;
        while (Time.time - t0 < celebrateSeconds)
        {
            float t = Time.time - t0;
            float ang = t / celebrateSeconds * celebrateLaps * Mathf.PI * 2f;
            float hop = Mathf.Abs(Mathf.Sin(t * Mathf.PI * hopsPerSecond)) * hopHeight;
            Vector3 ring = t1 * Mathf.Cos(ang) + t2 * Mathf.Sin(ang);
            PlaceOnRing(kid, kw, mid + ring * celebrateRingRadius, mid - ring * celebrateRingRadius, hop);
            PlaceOnRing(par, pw, mid - ring * celebrateRingRadius, mid + ring * celebrateRingRadius, hop * 0.8f);
            yield return null;
        }
        // Land exactly where each stands (walker state follows).
        if (kw.TryGroundAt(kid.localPosition, out Vector3 kg)) kw.TeleportLocal(kg - kg.normalized * kw.SeatDepth);
        if (pw.TryGroundAt(par.localPosition, out Vector3 pg)) pw.TeleportLocal(pg - pg.normalized * pw.SeatDepth);
        kw.Hold = false; pw.Hold = false;

        // Then they chill: the kid lives here now.
        kw.ReHome(); pw.ReHome();
        pw.IdleScale = 1f;
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
