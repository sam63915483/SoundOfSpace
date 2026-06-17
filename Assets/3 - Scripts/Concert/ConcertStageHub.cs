using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Per-scene singleton that gives every SpeakerSource its own concert:
//   1. Auto-clones the existing AudienceZone (+AudienceSpawner) for any
//      speaker that doesn't have one yet — so duplicating STAGEGOOD →
//      STAGEGOOD2 in the editor "just works" without manually wiring an
//      audience for the new stage.
//   2. Per-stage night gating — concerts only run when the stage is on the
//      night side of its planet (so stages on opposite poles run at
//      opposite times of the local day/night cycle, automatically).
//   3. Static IsBlockedForEnemy(pos) used by EnemySpawner to refuse spawns
//      within enemyBlockRadius of any speaker.
//
// Auto-spawns at scene load (RuntimeInitializeOnLoadMethod). No need to
// drop it in the scene — mirrors the SpaceDustSellUI / PlayerWallet pattern.
public class ConcertStageHub : MonoBehaviour
{
    public static ConcertStageHub Instance { get; private set; }

    [Header("Night Gate")]
    [Tooltip("dot(stageDir, sunDir) < this → night. 0 = sun on horizon (twilight), negative = require deeper darkness, positive = include sunset/sunrise.")]
    [Range(-1f, 1f)] public float nightDotThreshold = 0.05f;

    [Header("Enemy Block")]
    [Tooltip("Enemies cannot spawn within this distance of any concert speaker.")]
    public float enemyBlockRadius = 150f;
    [Tooltip("Damage per second applied to enemies inside enemyBlockRadius of any speaker. Default 40 = 2x TorchAura.damagePerSecond (20). Applies whether or not the concert is active — the no-enemy zone is a property of the speaker location, not the night state.")]
    public float enemyDamagePerSecond = 40f;

    [Header("Lebron Light Override")]
    [Tooltip("If an active LebronLight (the ship's artificial sun) is within this distance of a stage's speaker, the stage is forced into DAY mode regardless of planet rotation — the artificial sun overrides the natural night/day cycle. The concert shuts down: speaker stops, audience spawn pauses, lights go dark.")]
    public float lebronLightOverrideRadius = 150f;

    [Header("Visual Render Distance")]
    [Tooltip("Concert visuals (lights, lasers, strobes, beams, haze/fog particles) only render while the viewer is within this distance of the stage speaker. Beyond it they're switched off. The Built-in pipeline has no occlusion culling, so a night-side stage on the far side of the planet otherwise renders straight THROUGH the planet — dozens of real-time lights + particle overdraw — and tanks GPU when you happen to look that way. Audio, the audience, the night gate and the enemy-block zone are all unaffected.")]
    public float concertVisualRadius = 160f;

    [Header("Update")]
    [Tooltip("Seconds between day/night re-checks. Day/night transitions are slow; 1–3s is plenty.")]
    public float checkInterval = 3f; // 3s (was 1.5s): halves the BuildStages+night/day rebuild spike; day/night is slow so this is imperceptible

    [Header("Audience zone clone")]
    [Tooltip("When auto-cloning an audience zone for a new stage, position the zone this far in front of the speaker (along the speaker's forward axis). Matches typical concert layout: audience in front of the PA.")]
    public float clonedZoneInFront = 8f;

    [Header("Debug")]
    [Tooltip("Log stage discovery / clone / state events to the Console. Leave on while wiring up new stages so you can verify the hub found what you expected. Turn off once it's stable to keep the Console clean.")]
    // Off by default: BuildStages runs every checkInterval (1.5s) and was
    // emitting two Debug.Log calls per tick — each one allocates ~1KB via
    // StackTraceUtility.ExtractStackTrace. Set to true in the inspector to
    // re-enable when wiring up new stages.
    public bool debugLog = false;

    class StageEntry
    {
        public SpeakerSource speaker;
        public CelestialBody planet;
        public AudienceZone zone;
        public AudienceSpawner spawner;
        public Transform stageRoot;
        public bool active;
        public bool initialized;
        // Combined visual state (distance × night), so we only walk the stage
        // tree on an actual change, not every frame.
        public StageVis vis;
        public bool visualsInitialized;
    }

    // How much of a stage is rendered:
    //   Off          – too far to see: ALL renderers/lights/particles disabled
    //                  (stops a far-side stage being drawn THROUGH the planet,
    //                  which the Built-in pipeline can't occlusion-cull).
    //   GeometryOnly – close enough to see, but daytime: static stage meshes
    //                  show; lights/lasers/haze/beam meshes off.
    //   Full         – close + night: everything on.
    enum StageVis { Off, GeometryOnly, Full }
    readonly List<StageEntry> _stages = new List<StageEntry>();

    /// Fired when a stage transitions from inactive → active (begins playing).
    /// Passes the newly-active SpeakerSource. Not fired on deactivation, init,
    /// or force-update; only on the real inactive→active edge.
    public event Action<SpeakerSource> OnStageActivated;

    /// Returns the Transform of the first currently-active stage (the one
    /// playing right now, gated by night-side rotation). Null if no stage
    /// is active. Used by HALToolDispatcher so the AI can drop a waypoint
    /// to "the concert" without callers reaching into the private list.
    public Transform FindActiveStageRoot()
    {
        for (int i = 0; i < _stages.Count; i++)
        {
            var s = _stages[i];
            if (s != null && s.active && s.stageRoot != null) return s.stageRoot;
        }
        return null;
    }

    /// Returns the Transform of the center-stage SpeakerSource of the
    /// currently-active stage. Each stage's `speaker` is the SpeakerSource
    /// child that sits at the geometric centre of the stage (in the active
    /// scene this is the GameObject named `speaker.005`). HALToolDispatcher
    /// prefers this over FindActiveStageRoot for "mark the concert" so the
    /// compass marker lands centre-stage instead of on the stage root pivot.
    /// Null if no stage is active or the active stage has no speaker.
    public Transform FindActiveStageSpeaker()
    {
        for (int i = 0; i < _stages.Count; i++)
        {
            var s = _stages[i];
            if (s != null && s.active && s.speaker != null) return s.speaker.transform;
        }
        return null;
    }

    float _nextCheckTime;
    bool _stagesBuilt;
    Camera _viewerCam;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        // RuntimeInitializeOnLoadMethod fires ONCE per app launch — if we
        // skip MainMenu here, and the user clicks New Game / Load to enter
        // the gameplay scene, the hub is never created (the callback won't
        // re-fire on a scene change). Always create; we re-discover stages
        // on every sceneLoaded event below.
        if (Instance != null) return;
        var go = new GameObject("[ConcertStageHub]");
        DontDestroyOnLoad(go);
        go.AddComponent<ConcertStageHub>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (debugLog) Debug.Log("[ConcertStageHub] Awake — singleton created");
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // New scene loaded: drop any stage entries (they reference objects
        // from the previous scene that no longer exist) and rebuild now.
        if (debugLog) Debug.Log($"[ConcertStageHub] Scene loaded: {scene.name} — clearing {_stages.Count} stage(s) and rebuilding");
        _stages.Clear();
        _stagesBuilt = false;
        BuildStages();
        UpdateNightDay(force: true);
    }

    void Start()
    {
        BuildStages();
        // First-frame state: apply night/day immediately so we don't have one
        // tick of "wrong" state before the periodic check kicks in.
        UpdateNightDay(force: true);
    }

    void Update()
    {
        // Lazy rebuild: if speakers get spawned later (extra ships with their
        // own SpeakerSource, future content), pick them up automatically.
        if (Time.time >= _nextCheckTime)
        {
            _nextCheckTime = Time.time + checkInterval;
            BuildStages();
            UpdateNightDay(force: false);
        }

        // Per-frame: damage any enemy that's wandered inside a speaker's
        // protection radius. Same shape as TorchAura — independent damage
        // per stage, but capped to "one damage tick per enemy per frame" so
        // overlapping stages don't double-up. Always-on (not gated by night)
        // so the concert area is permanently no-man's-land for enemies.
        ApplyEnemyDamage();

        // Per-frame: enforce that each stage's speaker matches its active
        // state. Closes a race where SpeakerSource.Start (runs AFTER our
        // OnSceneLoaded ApplyActive) auto-plays the clip on the day-side
        // stage, with no later state transition to Stop it again.
        EnforceSpeakerState();

        // Per-frame: distance-gate the concert VISUALS so a far-away night-side
        // stage doesn't render through the planet. Cheap — just a sqr-distance
        // test per stage; only walks the stage tree when the on/off state flips.
        UpdateStageVisuals();
    }

    // Desired visual state for a stage = it's the active (night) stage AND the
    // viewer is close enough to see it. Only toggles the (expensive) light /
    // particle / beam tree when that combined state actually changes.
    void UpdateStageVisuals()
    {
        if (_stages.Count == 0) return;
        Vector3 viewer = GetViewerPosition();
        float r2 = concertVisualRadius * concertVisualRadius;
        bool anyFull = false;
        for (int i = 0; i < _stages.Count; i++)
        {
            var s = _stages[i];
            if (s == null || s.stageRoot == null) continue;
            float distSq = s.speaker != null
                ? (s.speaker.transform.position - viewer).sqrMagnitude : float.MaxValue;
            bool within = distSq <= r2;
            StageVis desired = !within ? StageVis.Off
                             : (s.active ? StageVis.Full : StageVis.GeometryOnly);
            if (desired == StageVis.Full) anyFull = true;
            if (!s.visualsInitialized || desired != s.vis)
            {
                ApplyStageVis(s.stageRoot, desired);
                s.vis = desired;
                s.visualsInitialized = true;
            }
        }
        // The global ConcertLightProgram manager (~2.2 ms Update + GC) is a
        // DontDestroyOnLoad singleton, NOT under any stage root, so ApplyStageVis
        // can't reach it. Only run it while a stage is actually showing its full
        // light show; disable it otherwise.
        var clp = ConcertLightProgram.Instance;
        if (clp != null && clp.enabled != anyFull) clp.enabled = anyFull;
    }

    PlayerController _viewerPlayer;
    Vector3 GetViewerPosition()
    {
        // Camera-based (not player-based) so it's correct while piloting a ship
        // too — the camera is what actually submits the concert geometry.
        if (_viewerCam == null) _viewerCam = Camera.main;
        if (_viewerCam != null) return _viewerCam.transform.position;
        // Fallback to the player if the main camera is momentarily missing, so
        // the distance gate never measures from the hub's origin (which would
        // leave a far concert's visuals switched on).
        if (_viewerPlayer == null) _viewerPlayer = FindObjectOfType<PlayerController>();
        if (_viewerPlayer != null) return _viewerPlayer.transform.position;
        return transform.position;
    }

    void EnforceSpeakerState()
    {
        for (int i = 0; i < _stages.Count; i++)
        {
            var s = _stages[i];
            if (s.speaker == null) continue;
            if (s.active && !s.speaker.IsPlaying) s.speaker.Play();
            else if (!s.active && s.speaker.IsPlaying) s.speaker.Stop();
        }
    }

    int _lastDamagedCount = -1;
    float _nextDamageLogTime;

    void ApplyEnemyDamage()
    {
        if (enemyDamagePerSecond <= 0f || _stages.Count == 0) return;
        var enemies = EnemyController.ActiveEnemies;
        if (enemies == null || enemies.Count == 0) return;

        float r2 = enemyBlockRadius * enemyBlockRadius;
        float dmg = enemyDamagePerSecond * Time.deltaTime;

        int damaged = 0;
        // Iterate backwards — TakeDamage may destroy/unregister the enemy
        // mid-iteration (matches TorchAura pattern).
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            var e = enemies[i];
            if (e == null) continue;
            Vector3 ep = e.transform.position;
            for (int j = 0; j < _stages.Count; j++)
            {
                var s = _stages[j];
                if (s.speaker == null) continue;
                if ((s.speaker.transform.position - ep).sqrMagnitude > r2) continue;
                e.TakeDamage(dmg, creditPlayer: false);
                damaged++;
                break; // one tick per enemy per frame regardless of overlap count
            }
        }

        // Throttled log so the Console isn't spammed each frame. Only logs
        // when the count changes OR once per second when nonzero.
        if (debugLog && (damaged != _lastDamagedCount || (damaged > 0 && Time.time >= _nextDamageLogTime)))
        {
            if (damaged > 0)
                Debug.Log($"[ConcertStageHub] Damaging {damaged} enemy(ies) inside concert radius ({enemyBlockRadius}m, {enemyDamagePerSecond} dps)");
            _lastDamagedCount = damaged;
            _nextDamageLogTime = Time.time + 1f;
        }
    }

    void BuildStages()
    {
        var speakers = FindObjectsOfType<SpeakerSource>(true);
        if (speakers == null || speakers.Length == 0)
        {
            if (debugLog) Debug.Log("[ConcertStageHub] BuildStages: no SpeakerSource components in scene — nothing to do");
            return;
        }
        var zones = FindObjectsOfType<AudienceZone>(true);

        // Template zone — first existing zone in the scene. We need this to
        // clone the inspector config (alien prefabs, hit/death audio, scale
        // range, trigger size, etc.) for new stages. If there's no zone at
        // all in the scene we can't auto-create — user has to drop one in
        // manually for the first concert (and we'll clone for the rest).
        AudienceZone templateZone = zones != null && zones.Length > 0 ? zones[0] : null;
        if (debugLog) Debug.Log($"[ConcertStageHub] BuildStages: found {speakers.Length} speaker(s), {(zones != null ? zones.Length : 0)} zone(s), template = {(templateZone != null ? templateZone.gameObject.name : "<none>")}");

        for (int i = 0; i < speakers.Length; i++)
        {
            var spk = speakers[i];
            if (spk == null) continue;

            // Already tracked? Skip.
            bool exists = false;
            for (int j = 0; j < _stages.Count; j++)
                if (_stages[j].speaker == spk) { exists = true; break; }
            if (exists) continue;

            // Find audience zone targeting this speaker.
            AudienceZone zone = FindZoneForSpeaker(zones, spk);
            if (zone == null && templateZone != null && templateZone.speaker != spk.transform)
            {
                zone = CloneZoneForSpeaker(templateZone, spk);
                if (debugLog) Debug.Log($"[ConcertStageHub]   speaker '{spk.gameObject.name}' had no zone — cloned '{templateZone.gameObject.name}' for it");
            }
            else if (debugLog)
            {
                Debug.Log($"[ConcertStageHub]   speaker '{spk.gameObject.name}' → zone '{(zone != null ? zone.gameObject.name : "<none>")}'");
            }

            // Snap the zone's rotation so its up axis matches the planet's
            // radial up at the zone's position, and its forward points away
            // from the speaker (audience extends back from the stage). The
            // position is NOT touched — manually-placed zones keep where the
            // user put them, we just realign the orientation. Without this,
            // a tilted local XZ plane causes TransformPoint to fan sample
            // points unevenly, the raycasts cluster onto the same surface
            // strip, and audience members stack on top of each other.
            SnapZoneRotation(zone, spk);

            var entry = new StageEntry
            {
                speaker = spk,
                planet = FindNearestPlanet(spk.transform.position),
                zone = zone,
                spawner = zone != null ? zone.GetComponent<AudienceSpawner>() : null,
                stageRoot = FindStageRoot(spk),
                initialized = false,
            };
            _stages.Add(entry);
        }

        _stagesBuilt = true;
        if (debugLog) Debug.Log($"[ConcertStageHub] BuildStages complete — {_stages.Count} stage(s) total");
    }

    static AudienceZone FindZoneForSpeaker(AudienceZone[] zones, SpeakerSource spk)
    {
        if (zones == null) return null;
        for (int i = 0; i < zones.Length; i++)
            if (zones[i] != null && zones[i].speaker == spk.transform) return zones[i];
        return null;
    }

    AudienceZone CloneZoneForSpeaker(AudienceZone template, SpeakerSource spk)
    {
        // Duplicate the template GameObject (carrying AudienceZone +
        // AudienceSpawner with all inspector config), retarget the speaker,
        // and place it in front of the new speaker so audience faces the PA.
        var clone = Instantiate(template.gameObject, template.transform.parent);
        clone.name = "AudienceZone (auto " + spk.gameObject.name + ")";

        var zone = clone.GetComponent<AudienceZone>();
        if (zone != null) zone.speaker = spk.transform;

        // CRITICAL: the zone's rotation must align transform.up with the
        // planet's RADIAL up at that location, because AudienceZone.SamplePositions
        // uses `transform.TransformPoint` to scatter sample points across its
        // local XZ plane. If transform.up isn't planet-radial-up, the points
        // fan out off-tangent and the surface raycasts miss.
        //
        // Stages on opposite poles have opposite local-up directions —
        // copying the speaker's rotation directly only works if the speaker
        // itself was authored upright in world space, which isn't guaranteed
        // after the user manually places a duplicate stage on the far pole.
        // Computing rotation from planet → speaker is robust to placement.
        CelestialBody planet = FindNearestPlanet(spk.transform.position);
        Vector3 zonePos;
        Quaternion zoneRot;
        if (planet != null)
        {
            Vector3 spkPos = spk.transform.position;
            Vector3 localUp = (spkPos - planet.Position).normalized;
            // Speaker.forward projected onto the tangent plane = the
            // direction the speaker is pointing along the ground. The zone
            // sits that distance in front, with the audience facing back
            // at the speaker.
            Vector3 forwardTangent = Vector3.ProjectOnPlane(spk.transform.forward, localUp);
            if (forwardTangent.sqrMagnitude < 0.0001f)
                forwardTangent = Vector3.ProjectOnPlane(Vector3.right, localUp);
            forwardTangent.Normalize();
            zonePos = spkPos + forwardTangent * clonedZoneInFront;
            zoneRot = Quaternion.LookRotation(forwardTangent, localUp);
        }
        else
        {
            // No planet — fall back to the speaker's rotation as authored.
            zonePos = spk.transform.position + spk.transform.forward * clonedZoneInFront;
            zoneRot = spk.transform.rotation;
        }
        clone.transform.SetPositionAndRotation(zonePos, zoneRot);

        // The spawner has its own seed; give the clone a different one so
        // the two stages' RNG streams don't synchronise their layouts.
        var spawner = clone.GetComponent<AudienceSpawner>();
        if (spawner != null) spawner.seed = unchecked(spawner.seed ^ spk.gameObject.GetInstanceID());

        return zone;
    }

    // Aligns an audience zone's rotation so its local +Y points radially
    // away from the planet center (so TransformPoint of localPoint=(lx,0,lz)
    // produces world points on a tangent plane at the zone's altitude) and
    // its local +Z points away from the speaker (so SamplePositions' Z
    // axis = depth-into-the-crowd as the zone tooltip documents). Position
    // is preserved. Without this, a tilted XZ plane causes raycast clustering
    // and audience members stack on a narrow strip.
    void SnapZoneRotation(AudienceZone zone, SpeakerSource spk)
    {
        if (zone == null || spk == null) return;
        CelestialBody planet = FindNearestPlanet(zone.transform.position);
        if (planet == null) return;

        Vector3 zonePos = zone.transform.position;
        Vector3 localUp = (zonePos - planet.Position).normalized;
        Vector3 awayFromSpk = zonePos - spk.transform.position;
        Vector3 fwdTan = Vector3.ProjectOnPlane(awayFromSpk, localUp);
        if (fwdTan.sqrMagnitude < 0.0001f)
            fwdTan = Vector3.ProjectOnPlane(zone.transform.forward, localUp);
        if (fwdTan.sqrMagnitude < 0.0001f)
            fwdTan = Vector3.ProjectOnPlane(Vector3.right, localUp);
        fwdTan.Normalize();
        Quaternion newRot = Quaternion.LookRotation(fwdTan, localUp);

        // Only write if the change is significant — avoids spurious "dirty
        // scene" prompts in the editor if the user already aligned it well.
        if (Quaternion.Angle(zone.transform.rotation, newRot) > 1f)
        {
            zone.transform.rotation = newRot;
            if (debugLog) Debug.Log($"[ConcertStageHub]   snapped zone '{zone.gameObject.name}' rotation to planet up");
        }
    }

    // Walk up from the speaker to find the outermost ancestor that's not the
    // planet (the stage root). Lights / scripts under this root are toggled
    // together by SetStageLightingActive.
    static Transform FindStageRoot(SpeakerSource speaker)
    {
        if (speaker == null) return null;
        Transform t = speaker.transform;
        Transform stageRoot = t;
        while (stageRoot.parent != null && stageRoot.parent.GetComponent<CelestialBody>() == null)
            stageRoot = stageRoot.parent;
        return stageRoot;
    }

    // Apply a stage's combined visual state. Walked FRESH each transition (not
    // cached) because concert-light scripts create their Light components in
    // Start, after our first BuildStages pass. Runs only on state changes.
    //   lights/scripts/particles → on only when Full (close + night)
    //   beam/glow renderers      → on only when Full
    //   static stage meshes      → on whenever NOT Off (so the stage is still
    //                               visible up close in daylight, but a far-side
    //                               stage is fully removed from rendering)
    void ApplyStageVis(Transform stageRoot, StageVis mode)
    {
        if (stageRoot == null) return;
        bool lightsOn      = mode == StageVis.Full;
        bool staticVisible = mode != StageVis.Off;
        int lightCount = 0, scriptCount = 0, rendCount = 0;

        var lights = stageRoot.GetComponentsInChildren<Light>(true);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] == null) continue;
            lights[i].enabled = lightsOn;
            lightCount++;
        }

        // Concert-light MonoBehaviours — disabled unless Full so their per-frame
        // Update (ConcertLightProgram ~2.2 ms + GC) doesn't run when culled.
        var lasers   = stageRoot.GetComponentsInChildren<ConcertLaser>(true);
        var cones    = stageRoot.GetComponentsInChildren<ConcertConeLight>(true);
        var strobes  = stageRoot.GetComponentsInChildren<ConcertStrobeLight>(true);
        var blinders = stageRoot.GetComponentsInChildren<ConcertBlinder>(true);
        var fogs     = stageRoot.GetComponentsInChildren<ConcertFogPuff>(true);
        var hazes    = stageRoot.GetComponentsInChildren<ConcertHaze>(true);
        var programs = stageRoot.GetComponentsInChildren<ConcertLightProgram>(true);
        for (int i = 0; i < lasers.Length;   i++) { if (lasers[i]   != null) { lasers[i].enabled   = lightsOn; scriptCount++; } }
        for (int i = 0; i < cones.Length;    i++) { if (cones[i]    != null) { cones[i].enabled    = lightsOn; scriptCount++; } }
        for (int i = 0; i < strobes.Length;  i++) { if (strobes[i]  != null) { strobes[i].enabled  = lightsOn; scriptCount++; } }
        for (int i = 0; i < blinders.Length; i++) { if (blinders[i] != null) { blinders[i].enabled = lightsOn; scriptCount++; } }
        for (int i = 0; i < fogs.Length;     i++) { if (fogs[i]     != null) { fogs[i].enabled     = lightsOn; scriptCount++; } }
        for (int i = 0; i < hazes.Length;    i++) { if (hazes[i]    != null) { hazes[i].enabled    = lightsOn; scriptCount++; } }
        for (int i = 0; i < programs.Length; i++) { if (programs[i] != null) { programs[i].enabled = lightsOn; scriptCount++; } }

        // Renderers: beam/glow/laser meshes follow lightsOn; everything else
        // (the actual stage structure) follows staticVisible — so a far stage
        // (Off) submits NO draw calls and can't render through the planet.
        var rends = stageRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i];
            if (r == null) continue;
            string n = r.gameObject.name;
            bool isBeam = n.IndexOf("Beam",    System.StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("Cone",    System.StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("Bulb",    System.StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("Strobe",  System.StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("Laser",   System.StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("Blinder", System.StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("Visual",  System.StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("Glow",    System.StringComparison.OrdinalIgnoreCase) >= 0;
            r.enabled = isBeam ? lightsOn : staticVisible;
            rendCount++;
        }

        // ParticleSystems (haze, fog puffs) — play only when Full, else stop+clear.
        int psCount = 0;
        var psyses = stageRoot.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < psyses.Length; i++)
        {
            var ps = psyses[i];
            if (ps == null) continue;
            if (lightsOn) { if (!ps.isPlaying) ps.Play(true); }
            else ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            psCount++;
        }

        if (debugLog) Debug.Log($"[ConcertStageHub]   ApplyStageVis({mode}) on '{stageRoot.name}': {lightCount} Light(s), {scriptCount} script(s), {rendCount} renderer(s), {psCount} particle system(s)");
    }

    CelestialBody FindNearestPlanet(Vector3 worldPos)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        CelestialBody best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null || b.bodyType == CelestialBody.BodyType.Sun) continue;
            float d = (b.Position - worldPos).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = b; }
        }
        return best;
    }

    CelestialBody FindSun()
    {
        // NBodySimulation.Bodies throws NullReferenceException if Instance is
        // null (which happens in the MainMenu scene where NBodySimulation
        // doesn't exist). UpdateNightDay runs every checkInterval whether or
        // not we're in a gameplay scene, so a try/catch here keeps the log
        // free of spurious exception spam in MainMenu.
        CelestialBody[] bodies;
        try { bodies = NBodySimulation.Bodies; }
        catch { return null; }
        if (bodies == null) return null;
        for (int i = 0; i < bodies.Length; i++)
            if (bodies[i] != null && bodies[i].bodyType == CelestialBody.BodyType.Sun) return bodies[i];
        return null;
    }

    void UpdateNightDay(bool force)
    {
        var sun = FindSun();
        if (sun == null) return;

        // LebronLight override: the ship's artificial sun illuminates the
        // area around the ship. Any concert speaker within
        // lebronLightOverrideRadius of an active LebronLight is forced to
        // "day" — the LebronLight is functionally a sun, so the concert's
        // night-only logic should treat the area as daylit.
        bool lebronActive = LebronLight.Instance != null && LebronLight.Instance.IsActive;
        Vector3 lebronPos = lebronActive ? LebronLight.Instance.transform.position : Vector3.zero;
        float lebronR2 = lebronLightOverrideRadius * lebronLightOverrideRadius;

        for (int i = 0; i < _stages.Count; i++)
        {
            var s = _stages[i];
            if (s.speaker == null || s.planet == null) continue;
            Vector3 sunDir = (sun.Position - s.planet.Position).normalized;
            Vector3 stageDir = (s.speaker.transform.position - s.planet.Position).normalized;
            bool isNight = Vector3.Dot(stageDir, sunDir) < nightDotThreshold;

            // Force day if a LebronLight is nearby and active.
            if (isNight && lebronActive
             && (s.speaker.transform.position - lebronPos).sqrMagnitude < lebronR2)
            {
                isNight = false;
            }

            if (force || !s.initialized || isNight != s.active)
            {
                // Capture transition state before the change.
                bool wasActive = s.active;
                bool wasInitial = !s.initialized;

                // Apply the new state.
                s.active = isNight;
                s.initialized = true;
                ApplyActive(s);

                // Fire OnStageActivated only on a real inactive→active transition
                // (not on init, not on deactivation, only on the specific edge).
                // Wrapped in try/catch so a subscriber exception cannot break
                // the rest of the per-stage loop or the per-tick UpdateNightDay
                // pass — concert state must keep flowing even if a downstream
                // listener (HALCommentator, etc.) misbehaves.
                if (!wasInitial && !wasActive && s.active && s.speaker != null)
                {
                    try { OnStageActivated?.Invoke(s.speaker); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
        }
    }

    void ApplyActive(StageEntry s)
    {
        if (debugLog)
            Debug.Log($"[ConcertStageHub] Stage '{(s.speaker != null ? s.speaker.gameObject.name : "<null>")}' → {(s.active ? "ACTIVE (night)" : "INACTIVE (day)")} (zone={(s.zone != null ? s.zone.gameObject.name : "<null>")}, spawner={(s.spawner != null ? "yes" : "no")}, stageRoot={(s.stageRoot != null ? s.stageRoot.name : "<null>")})");

        // Speaker: Play on entry, Stop on exit. SpeakerSource.Play() already
        // checks clip-not-null and assigns the clip before playing.
        if (s.speaker != null)
        {
            if (s.active) { if (!s.speaker.IsPlaying) s.speaker.Play(); }
            else          { s.speaker.Stop(); }
        }

        // Audience: enable the spawner. The spawner's despawn-on-disable
        // isn't built in, so for a smoother "concert ended" feel we leave
        // existing crowd; new spawns just stop. They'll attrition as the
        // player walks away (out of view distance) — same UX as wandering
        // alien NPCs after their spawner is disabled.
        if (s.spawner != null) s.spawner.enabled = s.active;

        // Lights / lasers / strobes / cones / haze / fog are NOT toggled here.
        // They're owned by UpdateStageVisuals (every frame), which combines
        // this `active` (night) state with a distance-to-viewer gate so a
        // far-side stage never renders through the planet. Forcing visuals on
        // here would flash the whole rig on for one frame before the distance
        // gate culls it. Force a re-evaluation next frame.
        s.visualsInitialized = false;
    }

    // Public helpers ─────────────────────────────────────────────────────

    public bool IsPositionBlockedForEnemy(Vector3 worldPos)
    {
        if (!_stagesBuilt) return false;
        float r2 = enemyBlockRadius * enemyBlockRadius;
        for (int i = 0; i < _stages.Count; i++)
        {
            var s = _stages[i];
            if (s.speaker == null) continue;
            if ((s.speaker.transform.position - worldPos).sqrMagnitude < r2) return true;
        }
        return false;
    }

    public static bool IsBlockedForEnemy(Vector3 worldPos)
    {
        return Instance != null && Instance.IsPositionBlockedForEnemy(worldPos);
    }
}
