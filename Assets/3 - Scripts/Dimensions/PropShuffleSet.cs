using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The shared "the room rearranges while you aren't looking" toy for bounded
/// dimensions. Feed it anchor poses and prop roots (props ≤ anchors); every
/// time its zone loses observation (ObservationTracker justLost) the props
/// are dealt onto a fresh random subset of anchors with small pose jitter,
/// optional creep modes, and a muffled one-shot played behind the player so
/// you HEAR the room move. D1's infinite maze doesn't use this — its
/// furniture lives inside the per-cell reroll.
/// </summary>
public class PropShuffleSet : MonoBehaviour
{
    struct Anchor { public Vector3 pos; public float yaw; }

    public Bounds zone;
    public string shuffleSfxKey = "sfx_furniture_drag";
    public float observeMaxDistance = 80f;
    /// <summary>Props rotate to face the camera on each unseen shuffle (chairs turning toward you).</summary>
    public bool facePlayer;
    /// <summary>Occasionally one prop hides for a cycle — "wasn't there one more chair?".</summary>
    public bool countJitter;
    public float posJitter = 0.15f;
    public float yawJitterDeg = 12f;
    public float sfxVolume = 0.55f;

    readonly List<Anchor> _anchors = new List<Anchor>();
    readonly List<GameObject> _props = new List<GameObject>();
    readonly List<int> _slotScratch = new List<int>();
    readonly ObservationTracker _tracker = new ObservationTracker();
    bool _initialDealt;

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

    public void AddProp(GameObject prop) => _props.Add(prop);

    void Update()
    {
        if (!_initialDealt)
        {
            if (_props.Count > 0 && _anchors.Count >= _props.Count)
            {
                Deal(playSfx: false);
                _initialDealt = true;
            }
            return;
        }
        _tracker.Tick(zone, out bool justLost, observeMaxDistance);
        if (justLost) Deal(playSfx: true);
    }

    void Deal(bool playSfx)
    {
        // Random distinct anchor per prop (partial Fisher-Yates over indices).
        _slotScratch.Clear();
        for (int i = 0; i < _anchors.Count; i++) _slotScratch.Add(i);
        for (int i = 0; i < _props.Count && i < _slotScratch.Count; i++)
        {
            int j = Random.Range(i, _slotScratch.Count);
            (_slotScratch[i], _slotScratch[j]) = (_slotScratch[j], _slotScratch[i]);
        }

        var cam = ObserverState.Cam;
        int hidden = countJitter && _props.Count > 1 && Random.value < 0.35f
            ? Random.Range(0, _props.Count) : -1;

        for (int i = 0; i < _props.Count; i++)
        {
            var prop = _props[i];
            if (prop == null) continue;
            prop.SetActive(i != hidden);
            var a = _anchors[_slotScratch[i]];
            Vector2 j2 = Random.insideUnitCircle * posJitter;
            prop.transform.position = a.pos + new Vector3(j2.x, 0f, j2.y);
            float yaw = a.yaw + Random.Range(-yawJitterDeg, yawJitterDeg);
            if (facePlayer && cam != null)
            {
                Vector3 to = cam.transform.position - a.pos;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f) yaw = Quaternion.LookRotation(to).eulerAngles.y;
            }
            prop.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        if (playSfx && cam != null)
        {
            // Just behind the player — the room moved where you can't see.
            Vector3 pos = cam.transform.position - cam.transform.forward * 5f + Vector3.up;
            DimensionSceneUtil.PlayOneShot3D(shuffleSfxKey, pos, sfxVolume, 30f);
        }
    }
}
