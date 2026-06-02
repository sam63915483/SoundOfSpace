using System.Collections.Generic;
using UnityEngine;

// Scatters audience aliens inside an AudienceZone in front of a stage.
// Polls InputSettings.maxAudienceSize every updateInterval and adds/removes
// members so the slider drives the crowd live.
//
// Mirrors the per-spawn component setup in AlienNPCSpawner (BoxCollider trigger,
// RandomAlienDialogue, AlienNPCDamageable) so audience members are damageable
// and can be talked to. Skips the cell-hash region streaming — audience is
// concentrated in one zone, so a flat list is the right shape.
//
// Audience is cosmetic and NOT saved (matches the enemy/tree cosmetic-entity
// pattern in CLAUDE.md). The slider value IS persisted via InputSettings.
public class AudienceSpawner : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("The zone that aliens scatter inside. Auto-found on the same GameObject if not assigned.")]
    public AudienceZone zone;
    [Tooltip("Drag the same alien prefabs AlienNPCSpawner uses.")]
    public GameObject[] alienPrefabs;
    [Tooltip("Reads maxAudienceSize from this asset every tick — the slider drives it live.")]
    public InputSettings inputSettings;

    [Header("Spawn")]
    [Tooltip("Fallback cap when InputSettings is not assigned.")]
    public int fallbackAudienceSize = 25;
    [Tooltip("Seconds between spawn/despawn passes.")]
    public float updateInterval = 0.5f;
    [Tooltip("Seed for the RNG that picks scatter positions and prefabs. Different from AlienNPCSpawner/TreeSpawner so distributions don't overlap.")]
    public int seed = 13579;

    [Header("Variation")]
    public float minScale = 1.5f;
    public float maxScale = 2.5f;

    [Header("Trigger Collider (added at spawn for F-to-talk)")]
    public Vector3 triggerSize = new Vector3(2.5f, 4f, 2.5f);
    public Vector3 triggerCenter = new Vector3(0f, 2f, 0f);

    [Header("Combat Audio (forwarded to AlienNPCDamageable)")]
    public AudioClip hitSound;
    [Range(0f, 1f)] public float hitVolume = 0.7f;
    public AudioClip deathSound;
    [Range(0f, 1f)] public float deathVolume = 0.8f;

    [Header("Health Bar / Hit Box")]
    public float healthBarWorldHeight = 2.5f;
    public float hitBoxWorldHeight = 2.5f;
    public float hitBoxWorldRadius = 0.9f;
    [Range(0f, 1f)] public float liveKnockbackScale = 0.2f;

    [Header("Ground Seat")]
    [Tooltip("Push the audience member this far into the ground along the planet-radial axis. Positive = into the ground. Constant — not scaled by the random per-instance scale.")]
    public float groundOffset = 0f;
    [Tooltip("Additional downward push proportional to spawn scale. Catches the residual gap that varies per prefab. 0.04 = 4cm of extra embed per scale unit — matches AlienNPCSpawner / MushroomSpawner.")]
    public float groundEmbedPerScale = 0.04f;

    readonly List<GameObject> _spawned = new List<GameObject>();
    System.Random _rng;
    float _tickTimer;
    // Permanent kill counter — once an audience member dies (AlienNPCDamageable.
    // IsDying flips true), the spawner subtracts that from the effective
    // population target so killed members STAY dead, matching the behavior the
    // user expects from AlienNPCSpawner's killed-cells system. Tracked by
    // GameObject instance ID so each member is counted at most once even
    // across multiple Update ticks while the corpse is still in `_spawned`.
    readonly HashSet<int> _deathCounted = new HashSet<int>();
    int _killedCount;
    // Per-prefab "minimum Y in prefab-root local space" — same shape and
    // measurement approach as AlienNPCSpawner._prefabLocalBottomY. One-time
    // runtime Instantiate per unique prefab at Awake, cached for spawn use.
    float[] _prefabLocalBottomY;

    void Awake()
    {
        _rng = new System.Random(seed);
        if (zone == null) zone = GetComponent<AudienceZone>();
        if (alienPrefabs != null)
        {
            _prefabLocalBottomY = new float[alienPrefabs.Length];
            for (int i = 0; i < alienPrefabs.Length; i++)
                _prefabLocalBottomY[i] = ComputeLocalBottomY(alienPrefabs[i]);
        }
    }

    // Returns the lowest Y of the prefab's rendered geometry in prefab-root
    // local space — identical approach to AlienNPCSpawner.ComputeLocalBottomY.
    // Renderer.bounds reflects what Unity actually renders, sidestepping the
    // SkinnedMeshRenderer.localBounds padding artifact that was making the
    // analytic measurement over-lift rigged characters.
    static float ComputeLocalBottomY(GameObject prefab)
    {
        if (prefab == null) return 0f;
        Vector3 sentinel = new Vector3(0f, -100000f, 0f);
        GameObject temp = Instantiate(prefab, sentinel, Quaternion.identity);
        temp.transform.localScale = Vector3.one;

        var anims = temp.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < anims.Length; i++)
            if (anims[i] != null) anims[i].enabled = false;

        Physics.SyncTransforms();

        float bottomY = 0f;
        bool any = false;
        Bounds combined = default;
        var renderers = temp.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer || r is SpriteRenderer) continue;
            if (!any) { combined = r.bounds; any = true; }
            else combined.Encapsulate(r.bounds);
        }
        if (any) bottomY = combined.min.y - sentinel.y;
        Destroy(temp);
        return bottomY;
    }

    // When the hub toggles the spawner via .enabled (concert active ↔ day),
    // these flip every already-spawned audience member into / out of idle
    // mode so they stop dancing the moment the concert ends and resume when
    // it starts again. New spawns inherit the current state via Spawn().
    void OnEnable()  { SetAudienceIdle(false); }
    void OnDisable() { SetAudienceIdle(true);  }

    void SetAudienceIdle(bool idle)
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] == null) continue;
            var am = _spawned[i].GetComponent<AudienceMember>();
            if (am != null) am.SetIdleMode(idle);
        }
    }

    void Update()
    {
        _tickTimer -= Time.deltaTime;
        if (_tickTimer > 0f) return;
        _tickTimer = updateInterval;

        if (zone == null || alienPrefabs == null || alienPrefabs.Length == 0) return;

        // Drop dead members (destroyed by player damage, etc.) so the count
        // converges back to the slider target. Kills are tracked separately
        // by AudienceMemberDeathWatcher → NotifyMemberKilled, which runs
        // independently of this spawner's enabled state so deaths during
        // day-mode (spawner disabled) still get permanently recorded.
        for (int i = _spawned.Count - 1; i >= 0; i--)
            if (_spawned[i] == null) _spawned.RemoveAt(i);

        int baseTarget = inputSettings != null ? inputSettings.maxAudienceSize : fallbackAudienceSize;
        // Subtract permanent kills so killed audience members never respawn —
        // matches the user expectation that "if I kill an audience member, they
        // stay dead". The slider value remains the spawner's UPPER BOUND.
        int target = Mathf.Clamp(baseTarget - _killedCount, 0, 200);

        if (_spawned.Count < target)
        {
            int need = target - _spawned.Count;
            // Pass current world positions so the zone rejects candidates
            // overlapping existing members (prevents stacked aliens when the
            // slider is bumped up mid-concert).
            var existing = new List<Vector3>(_spawned.Count);
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) existing.Add(_spawned[i].transform.position);
            var poses = zone.SamplePositions(need, _rng, existing);
            for (int i = 0; i < poses.Count; i++)
                Spawn(poses[i]);
        }
        else if (_spawned.Count > target)
        {
            int kill = _spawned.Count - target;
            for (int i = 0; i < kill; i++)
            {
                int last = _spawned.Count - 1;
                if (last < 0) break;
                var go = _spawned[last];
                _spawned.RemoveAt(last);
                if (go != null) Destroy(go);
            }
        }
    }

    void Spawn(Pose pose)
    {
        int prefabIdx = _rng.Next(alienPrefabs.Length);
        var prefab = alienPrefabs[prefabIdx];
        if (prefab == null) return;

        GameObject alien = Instantiate(prefab, pose.position, pose.rotation);
        float scale = Mathf.Lerp(minScale, maxScale, (float)_rng.NextDouble());
        alien.transform.localScale = Vector3.one * scale;

        // Seat the alien's mesh-bottom on pose.position regardless of where
        // the prefab pivot is, plus a small scale-proportional embed. Same
        // formula as AlienNPCSpawner / MushroomSpawner — kept in lockstep so
        // tuning is consistent across all three spawners.
        float bottomY = (_prefabLocalBottomY != null && prefabIdx < _prefabLocalBottomY.Length)
            ? _prefabLocalBottomY[prefabIdx]
            : 0f;
        Vector3 up = alien.transform.up;
        alien.transform.position = pose.position - up * (bottomY * scale + groundOffset + groundEmbedPerScale * scale);

        // Parent to the planet so floating-origin shifts carry the crowd
        // along with the rest of the world.
        var body = zone.Body;
        if (body != null) alien.transform.SetParent(body.transform, true);

        // Replace any pre-attached NPCWaveAnimation with AudienceMember —
        // they fight over the same bones.
        var wave = alien.GetComponent<NPCWaveAnimation>();
        if (wave != null) Destroy(wave);
        if (alien.GetComponent<AudienceMember>() == null)
            alien.AddComponent<AudienceMember>();

        // BoxCollider trigger (F-to-talk).
        BoxCollider trigger = null;
        var existing = alien.GetComponents<BoxCollider>();
        for (int i = 0; i < existing.Length; i++)
            if (existing[i].isTrigger) { trigger = existing[i]; break; }
        if (trigger == null)
        {
            trigger = alien.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = triggerSize;
            trigger.center = triggerCenter;
        }

        if (alien.GetComponent<RandomAlienDialogue>() == null)
            alien.AddComponent<RandomAlienDialogue>();

        // Damageable — forward audio + size settings, same shape as AlienNPCSpawner.
        var dmg = alien.GetComponent<AlienNPCDamageable>();
        if (dmg == null) dmg = alien.AddComponent<AlienNPCDamageable>();
        dmg.hitSound = hitSound;
        dmg.hitVolume = hitVolume;
        dmg.deathSound = deathSound;
        dmg.deathVolume = deathVolume;
        dmg.healthBarWorldHeight = healthBarWorldHeight;
        dmg.liveColliderWorldHeight = hitBoxWorldHeight;
        dmg.liveColliderWorldRadius = hitBoxWorldRadius;
        dmg.liveKnockbackScale = liveKnockbackScale;
        dmg.RefreshHealthBarPosition();
        dmg.RefreshLiveCollider();

        // Feet-to-ground is handled above via the pre-measured bottomY +
        // scale-embed seat — same formula AlienNPCSpawner / MushroomSpawner
        // use, so a future tuning pass tunes all three spawners identically.

        // NOTE: do NOT call EndlessManager.RegisterPhysicsObject here. The
        // alien is parented to a CelestialBody, and CelestialBody itself is
        // already shifted by EndlessManager — registering the child as well
        // causes a double-shift, which was making the crowd teleport away
        // when the camera transferred from ship to player on hatch exit.

        // Death watcher: notifies this spawner via NotifyMemberKilled the
        // moment AlienNPCDamageable.IsDying flips true. Independent of this
        // spawner's enabled state, so kills during concert-day-mode (when
        // the spawner is disabled) still get counted permanently.
        if (alien.GetComponent<AudienceMemberDeathWatcher>() == null)
        {
            var watcher = alien.AddComponent<AudienceMemberDeathWatcher>();
            watcher.spawner = this;
        }

        _spawned.Add(alien);
    }

    // Called from AudienceMemberDeathWatcher on each member's first frame of
    // AlienNPCDamageable.IsDying. We dedup by GameObject instance ID so a
    // misbehaving watcher (or a future "multiple watchers per member" mistake)
    // can't double-count a single kill.
    public void NotifyMemberKilled(GameObject member)
    {
        if (member == null) return;
        if (_deathCounted.Add(member.GetInstanceID())) _killedCount++;
    }

    void OnDestroy()
    {
        for (int i = 0; i < _spawned.Count; i++)
            if (_spawned[i] != null) Destroy(_spawned[i]);
        _spawned.Clear();
    }

}
