using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The shared "things rearrange while you aren't looking" toy for bounded
/// dimensions. Feed it anchor poses and prop roots (props ≤ anchors); each
/// prop carries its OWN ObservationTracker — the moment a prop leaves your
/// view (grace window included) it re-deals to a free anchor, preferring
/// anchors that are currently unobserved, with pose jitter, optional creep
/// modes, and a muffled throttled one-shot behind the player so you HEAR it
/// move. Per-prop tracking is deliberate: a single zone tracker is inert
/// whenever the camera stands inside the zone (an AABB you're inside always
/// intersects the frustum), which is exactly the case in room-scale scenes.
/// D1's infinite maze doesn't use this — its furniture lives in the cell reroll.
/// </summary>
public class PropShuffleSet : MonoBehaviour
{
    struct Anchor { public Vector3 pos; public float yaw; }

    /// <summary>Legacy/optional — kept for controllers that set it; per-prop
    /// tracking made the zone irrelevant for shuffle detection.</summary>
    public Bounds zone;
    public string shuffleSfxKey = "sfx_furniture_drag";
    public float observeMaxDistance = 80f;
    /// <summary>Props rotate to face the camera whenever they move unseen.</summary>
    public bool facePlayer;
    /// <summary>Occasionally a prop hides for a cycle — "wasn't there one more chair?".</summary>
    public bool countJitter;
    public float posJitter = 0.15f;
    public float yawJitterDeg = 12f;
    public float sfxVolume = 0.55f;
    /// <summary>Min seconds between audible shuffle stingers for this set.</summary>
    public float sfxMinInterval = 4f;

    static readonly Vector3 PropBoundsSize = new Vector3(1.6f, 2f, 1.6f);
    static readonly Vector3 AnchorProbeSize = new Vector3(1.2f, 2f, 1.2f);

    readonly List<Anchor> _anchors = new List<Anchor>();
    readonly List<GameObject> _props = new List<GameObject>();
    readonly List<ObservationTracker> _trackers = new List<ObservationTracker>();
    readonly List<int> _propAnchor = new List<int>();     // index into _anchors per prop
    readonly List<int> _freeScratch = new List<int>();
    bool _initialDealt;
    float _lastSfxTime;

    public static PropShuffleSet Create(string name, Transform parent, Bounds zone,
        string sfxKey = "sfx_furniture_drag", bool facePlayer = false, bool countJitter = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var set = go.AddComponent<PropShuffleSet>();
        set.zone = zone;
        set.shuffleSfxKey = sfxKey;
        set.facePlayer = facePlayer;
        set.countJitter = countJitter;
        return set;
    }

    public void AddAnchor(Vector3 worldPos, float yawDeg)
        => _anchors.Add(new Anchor { pos = worldPos, yaw = yawDeg });

    public void AddProp(GameObject prop)
    {
        _props.Add(prop);
        _trackers.Add(new ObservationTracker());
        _propAnchor.Add(-1);
    }

    void Update()
    {
        if (!_initialDealt)
        {
            if (_props.Count > 0 && _anchors.Count >= _props.Count)
            {
                InitialDeal();
                _initialDealt = true;
            }
            return;
        }

        for (int i = 0; i < _props.Count; i++)
        {
            var prop = _props[i];
            if (prop == null) continue;
            var b = new Bounds(prop.transform.position + Vector3.up, PropBoundsSize);
            _trackers[i].Tick(b, out bool justLost, observeMaxDistance);
            if (justLost) MoveProp(i);
        }
    }

    void InitialDeal()
    {
        // Random distinct anchors (partial Fisher-Yates over indices).
        _freeScratch.Clear();
        for (int i = 0; i < _anchors.Count; i++) _freeScratch.Add(i);
        for (int i = 0; i < _props.Count; i++)
        {
            int j = Random.Range(i, _freeScratch.Count);
            (_freeScratch[i], _freeScratch[j]) = (_freeScratch[j], _freeScratch[i]);
            Place(i, _freeScratch[i]);
        }
    }

    void MoveProp(int i)
    {
        // Free anchors, split into unobserved (preferred) and observed.
        _freeScratch.Clear();
        int unobservedCount = 0;
        for (int a = 0; a < _anchors.Count; a++)
        {
            bool used = false;
            for (int p = 0; p < _propAnchor.Count; p++)
                if (p != i && _propAnchor[p] == a) { used = true; break; }
            if (used) continue;
            bool seen = ObserverState.IsObserved(
                new Bounds(_anchors[a].pos + Vector3.up, AnchorProbeSize), observeMaxDistance);
            if (!seen) { _freeScratch.Insert(unobservedCount, a); unobservedCount++; }
            else _freeScratch.Add(a);
        }
        if (_freeScratch.Count == 0) return;

        int pick = unobservedCount > 0
            ? _freeScratch[Random.Range(0, unobservedCount)]
            : _freeScratch[Random.Range(0, _freeScratch.Count)];
        Place(i, pick);

        // Count jitter: sometimes the prop just... isn't there this time.
        if (countJitter && Random.value < 0.18f) _props[i].SetActive(false);
        else if (!_props[i].activeSelf) _props[i].SetActive(true);

        var cam = ObserverState.Cam;
        if (cam != null && Time.time - _lastSfxTime > sfxMinInterval)
        {
            _lastSfxTime = Time.time;
            Vector3 pos = cam.transform.position - cam.transform.forward * 5f + Vector3.up;
            DimensionSceneUtil.PlayOneShot3D(shuffleSfxKey, pos, sfxVolume, 30f);
        }
    }

    void Place(int propIdx, int anchorIdx)
    {
        var prop = _props[propIdx];
        if (prop == null) return;
        _propAnchor[propIdx] = anchorIdx;
        var a = _anchors[anchorIdx];
        Vector2 j2 = Random.insideUnitCircle * posJitter;
        prop.transform.position = a.pos + new Vector3(j2.x, 0f, j2.y);
        float yaw = a.yaw + Random.Range(-yawJitterDeg, yawJitterDeg);
        var cam = ObserverState.Cam;
        if (facePlayer && cam != null)
        {
            Vector3 to = cam.transform.position - a.pos;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f) yaw = Quaternion.LookRotation(to).eulerAngles.y;
        }
        prop.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }
}
