using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps the moon's cave network stocked with <see cref="CaveSpider"/>s (Sam,
/// 2026-09-23).
///
/// DEPTH IS EVERYTHING
///   • The top level (shallower than <see cref="topLevelDepth"/>) is theirs to
///     leave alone: no spiders live there, none will climb into it, and a chase
///     ends the moment you get back up to it.
///   • Below that, spiders start small and few; the deeper you go the more there
///     are and the bigger they get, up to <see cref="deepScale"/> (3× the base
///     spider) at <see cref="fullDepth"/> and beyond.
///   Depth = metres below the moon's nominal surface (radius), measured at each
///   cave capsule. The network runs from the mouths down to the zero-g core at
///   the centre; most tunnels are 8-24 m down.
///
/// Lives on the Cave_Moon prefab beside its <see cref="CaveVolume"/> and reads
/// that volume's capsules (the real bore of every tunnel and cavern) to place
/// spiders: a point on a capsule's centre-line, then a ray to the rock in a
/// random direction — floors, walls and roofs.
///
/// Spiders exist only while a player is near the moon; leave and they are
/// cleared, come back and a fresh set is laid out. Dead ones are replaced after
/// <see cref="respawnDelay"/>, never in view and never close by. Nothing is
/// saved. Each machine runs its own spiders (co-op): they chase whoever they
/// notice, but only ever damage the player at that keyboard.
/// </summary>
[RequireComponent(typeof(CaveVolume))]
public class CaveSpiderSpawner : MonoBehaviour
{
    [Tooltip("Spider prefabs (built by Tools ▸ Cave ▸ Build Cave Spiders). One is picked at random per spawn.")]
    public GameObject[] spiderPrefabs;

    [Tooltip("How many spiders live in the network at once.")]
    public int population = 30;

    [Tooltip("Spiders appear only while a player is within this distance of the moon's centre.")]
    public float activeRange = 150f;

    [Tooltip("Never spawn closer than this to a player.")]
    public float minSpawnDistance = 14f;

    [Tooltip("Seconds before a killed spider is replaced.")]
    public float respawnDelay = 45f;

    [Tooltip("Spawns per half-second tick, so filling the network never lands in one frame.")]
    public int spawnsPerTick = 4;

    [Header("Depth (metres below the moon's surface)")]
    [Tooltip("The top level: no spiders live here, none climb into it, and a chase ends when you reach it.")]
    public float topLevelDepth = 11f;
    [Tooltip("Spiders never crawl shallower than this (a little above the top-level line, so the edge isn't a hard wall).")]
    public float spiderCeilingDepth = 9.5f;
    [Tooltip("The shallowest a spider is ever placed.")]
    public float spawnMinDepth = 12.5f;
    [Tooltip("At this depth and below: the most spiders, and the biggest.")]
    public float fullDepth = 28f;
    [Tooltip("Size at the shallowest spawn depth (1 = the pack's spider, about 1.1 m leg to leg).")]
    public float shallowScale = 0.7f;
    [Tooltip("Size at full depth — 3× the base spider.")]
    public float deepScale = 3f;
    [Tooltip("How much more crowded the deep is than the shallow edge (per metre of tunnel).")]
    public float deepDensityMult = 6f;

    [Header("The core (zero-g cavern at the centre)")]
    [Tooltip("Giant spiders that live only in the core cavern.")]
    public int coreSpiders = 3;
    [Tooltip("Size of the core giants — 2× the biggest ordinary spider.")]
    public float coreScale = 6f;
    [Tooltip("Capsules at least this deep (and at least coreMinRadius wide) are the core. Ordinary spiders stay out.")]
    public float coreDepth = 40f;
    public float coreMinRadius = 8f;
    [Tooltip("Giants never crawl shallower than this — the core plus the last stretch of the tunnels into it.")]
    public float coreCeilingDepth = 34f;
    [Tooltip("A giant gives up once you are shallower than this.")]
    public float coreGiveUpDepth = 32f;

    readonly List<CaveSpider> _live = new List<CaveSpider>();
    readonly List<float> _refillAt = new List<float>();
    readonly List<CaveSpider> _liveCore = new List<CaveSpider>();
    readonly List<float> _refillCoreAt = new List<float>();
    readonly List<int> _coreCapsules = new List<int>();
    CaveVolume _vol;
    CelestialBody _moon;
    Camera _cam;
    float _nextTick, _nextCamRefind;
    float[] _cumWeight;

    void Awake()
    {
        _vol = GetComponent<CaveVolume>();
    }

    void Update()
    {
        if (!FeatureVault.CaveSpiders) return;
        if (spiderPrefabs == null || spiderPrefabs.Length == 0) return;

        // TEST KEY (Editor / dev builds only): Backslash drops a spider on whatever
        // you are looking at, on any planet — no trip into the caves needed.
        if ((Application.isEditor || Debug.isDebugBuild) && Input.GetKeyDown(KeyCode.Backslash))
            DebugSpawnInView();

        if (Time.time < _nextTick) return;
        _nextTick = Time.time + 0.5f;

        if (_moon == null) _moon = GetComponentInParent<CelestialBody>();
        if (_moon == null || _vol == null || _vol.capsuleA == null || _vol.capsuleA.Length == 0) return;

        _live.RemoveAll(s => s == null);
        _liveCore.RemoveAll(s => s == null);

        if (!AnyPlayerNear())
        {
            if (_live.Count > 0 || _liveCore.Count > 0) Clear();
            _refillAt.Clear();
            _refillCoreAt.Clear();
            return;
        }

        if (_cam == null && Time.time >= _nextCamRefind)
        {
            _nextCamRefind = Time.time + 2f;
            _cam = Camera.main;
        }

        // Due replacements count as open slots; ones still waiting do not.
        int waiting = 0;
        for (int i = _refillAt.Count - 1; i >= 0; i--)
        {
            if (Time.time >= _refillAt[i]) _refillAt.RemoveAt(i);
            else waiting++;
        }

        int want = population - _live.Count - waiting;
        for (int n = 0; n < Mathf.Min(want, spawnsPerTick); n++)
            TrySpawn();

        int waitingCore = 0;
        for (int i = _refillCoreAt.Count - 1; i >= 0; i--)
        {
            if (Time.time >= _refillCoreAt[i]) _refillCoreAt.RemoveAt(i);
            else waitingCore++;
        }
        if (_liveCore.Count + waitingCore < coreSpiders) TrySpawnCore();
    }

    bool AnyPlayerNear()
    {
        var all = PlayerRoster.All();
        float r2 = activeRange * activeRange;
        Vector3 c = _moon.Position;
        for (int i = 0; i < all.Count; i++)
            if (all[i].Transform != null && (all[i].Transform.position - c).sqrMagnitude < r2) return true;
        return false;
    }

    void Clear()
    {
        for (int i = 0; i < _live.Count; i++)
            if (_live[i] != null) Destroy(_live[i].gameObject);
        for (int i = 0; i < _liveCore.Count; i++)
            if (_liveCore[i] != null) Destroy(_liveCore[i].gameObject);
        _live.Clear();
        _liveCore.Clear();
    }

    public void OnSpiderDied(CaveSpider s)
    {
        if (_live.Remove(s)) _refillAt.Add(Time.time + respawnDelay);
        else if (_liveCore.Remove(s)) _refillCoreAt.Add(Time.time + respawnDelay * 2f);
    }

    public void OnSpiderGone(CaveSpider s) { _live.Remove(s); _liveCore.Remove(s); }

    // ── Placement ───────────────────────────────────────────────────────────

    float Depth(Vector3 worldPoint) => _moon.radius - (worldPoint - _moon.Position).magnitude;

    /// 0 at the shallowest spawn depth, 1 at full depth and below.
    float Deepness(float depth) => Mathf.Clamp01(Mathf.InverseLerp(spawnMinDepth, fullDepth, depth));

    void TrySpawn()
    {
        if (!BuildWeights()) return;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int ci = PickCapsule();
            float r = _vol.capsuleR[ci];
            Vector3 centre = transform.TransformPoint(Vector3.Lerp(_vol.capsuleA[ci], _vol.capsuleB[ci], Random.value));

            // Half the time aim at the floor, otherwise anywhere — walls and roofs too.
            Vector3 down = (_moon.Position - centre).normalized;
            Vector3 dir = Random.value < 0.5f
                ? (down + Random.insideUnitSphere * 0.6f).normalized
                : Random.onUnitSphere;

            if (!Physics.Raycast(centre, dir, out RaycastHit hit, r * 2.5f, ~((1 << 2) | (1 << 9) | (1 << 11) | (1 << 12)),
                                 QueryTriggerInteraction.Ignore)) continue;
            if (hit.collider.GetComponentInParent<CaveSpider>() != null) continue;
            if (hit.collider.GetComponentInParent<PlayerController>() != null) continue;
            float depth = Depth(hit.point);
            if (depth < spawnMinDepth) continue;
            if (TooCloseToPlayer(hit.point)) continue;
            if (Visible(hit.point + hit.normal * 0.4f)) continue;

            float scale = Mathf.Lerp(shallowScale, deepScale, Deepness(depth)) * Random.Range(0.85f, 1.15f);
            Spawn(hit.point, hit.normal, _moon, scale, topLevelDepth, spiderCeilingDepth);
            return;
        }
    }

    void TrySpawnCore()
    {
        if (!BuildWeights() || _coreCapsules.Count == 0) return;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int ci = _coreCapsules[Random.Range(0, _coreCapsules.Count)];
            Vector3 centre = transform.TransformPoint(Vector3.Lerp(_vol.capsuleA[ci], _vol.capsuleB[ci], Random.value));
            if (!Physics.Raycast(centre, Random.onUnitSphere, out RaycastHit hit, _vol.capsuleR[ci] * 2.5f,
                                 ~((1 << 2) | (1 << 9) | (1 << 11) | (1 << 12)), QueryTriggerInteraction.Ignore)) continue;
            if (hit.collider.GetComponentInParent<CaveSpider>() != null) continue;
            if (hit.collider.GetComponentInParent<PlayerController>() != null) continue;
            if (TooCloseToPlayer(hit.point)) continue;
            if (Visible(hit.point + hit.normal * 0.4f)) continue;
            var s = Spawn(hit.point, hit.normal, _moon, coreScale * Random.Range(0.9f, 1.1f), coreGiveUpDepth, coreCeilingDepth);
            if (s != null) { _live.Remove(s); _liveCore.Add(s); }
            return;
        }
    }

    // Weight = tunnel length × how deep it is: nothing above the spawn line,
    // then rising to deepDensityMult× at full depth.
    bool BuildWeights()
    {
        int n = _vol.capsuleA.Length;
        if (_cumWeight != null && _cumWeight.Length == n) return _cumWeight[n - 1] > 0f;
        _cumWeight = new float[n];
        _coreCapsules.Clear();
        float acc = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 mid = transform.TransformPoint((_vol.capsuleA[i] + _vol.capsuleB[i]) * 0.5f);
            float depth = Depth(mid);
            float w = 0f;
            if (depth >= coreDepth && _vol.capsuleR[i] >= coreMinRadius) _coreCapsules.Add(i);   // the giants' lair
            else if (depth >= spawnMinDepth - _vol.capsuleR[i])
            {
                float len = Vector3.Distance(_vol.capsuleA[i], _vol.capsuleB[i]) + _vol.capsuleR[i] * 2f;
                w = len * Mathf.Lerp(1f, deepDensityMult, Deepness(depth));
            }
            acc += w;
            _cumWeight[i] = acc;
        }
        return acc > 0f;
    }

    int PickCapsule()
    {
        int n = _cumWeight.Length;
        float pick = Random.value * _cumWeight[n - 1];
        int lo = 0, hi = n - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_cumWeight[mid] < pick) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    bool TooCloseToPlayer(Vector3 p)
    {
        var all = PlayerRoster.All();
        float r2 = minSpawnDistance * minSpawnDistance;
        for (int i = 0; i < all.Count; i++)
            if (all[i].Transform != null && (all[i].Transform.position - p).sqrMagnitude < r2) return true;
        return false;
    }

    bool Visible(Vector3 p)
    {
        if (_cam == null) return false;
        return !Physics.Linecast(_cam.transform.position, p, ~((1 << 2) | (1 << 9) | (1 << 11) | (1 << 12)),
                                 QueryTriggerInteraction.Ignore);
    }

    void DebugSpawnInView()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;
        var t = _cam.transform;
        if (!Physics.Raycast(t.position + t.forward * 0.5f, t.forward, out RaycastHit hit, 25f,
                             ~((1 << 2) | (1 << 9) | (1 << 11) | (1 << 12)), QueryTriggerInteraction.Ignore))
        { Debug.Log("[CaveSpiders] Backslash test key: look at a surface within 25 m."); return; }
        var body = hit.collider.GetComponentInParent<CelestialBody>();
        if (body == null) body = _moon != null ? _moon : GetComponentInParent<CelestialBody>();
        if (body == null) return;

        // Inside this moon's caves the test spider obeys the depth rules (size and
        // territory) like a real one; anywhere else it's a plain base-size spider.
        bool here = body == (_moon != null ? _moon : GetComponentInParent<CelestialBody>());
        if (_moon == null) _moon = GetComponentInParent<CelestialBody>();
        float depth = here ? Depth(hit.point) : 0f;
        float scale = here && depth >= spawnMinDepth ? Mathf.Lerp(shallowScale, deepScale, Deepness(depth)) : 1f;
        var s = Spawn(hit.point, hit.normal, body, scale, here ? topLevelDepth : 0f, here ? spiderCeilingDepth : 0f);
        Debug.Log($"[CaveSpiders] Test spider on '{body.bodyName}' ({hit.collider.name}) depth {depth:F1} m, size ×{scale:F2}: {(s != null ? "ok" : "failed")}.");
    }

    CaveSpider Spawn(Vector3 point, Vector3 normal, CelestialBody body, float scale, float giveUpDepth, float minDepth)
    {
        var prefab = spiderPrefabs[Random.Range(0, spiderPrefabs.Length)];
        if (prefab == null) return null;
        var go = Instantiate(prefab, point, Quaternion.identity);   // Init sets the real pose
        go.transform.SetParent(body.transform, true);
        var spider = go.GetComponent<CaveSpider>();
        if (spider == null) { Destroy(go); return null; }
        spider.Init(this, body, point, normal, scale, giveUpDepth, minDepth);
        if (body == _moon) _live.Add(spider);
        return spider;
    }
}
