using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reusable D3-style bridge: N fixed slots, fewer tiles hopping between them. A tile
/// is solid only while any sliver of it is on screen (FULL FLAT footprint test — tall
/// boxes cause the look-up bounce bug); off-screen tiles jump to a different empty
/// slot (once per look-away, hidden slots preferred, 60% bias to the nearest empty
/// slot ahead of the player along the progress axis). Owners call Tick() every frame.
/// Used by D14 Glacier Throat, D17 Tide Pools, D21 Archive Stacks.
/// </summary>
public class SliverTileSet
{
    class Tile
    {
        public Transform tf;
        public Renderer rend;
        public Collider col;
        public MaterialPropertyBlock mpb;
        public ObservationTracker tracker = new ObservationTracker(0.25f);
        public float alpha = 1f;
        public float unobservedSince = -1f;
        public bool rearrangedSinceSeen;
        public int slotIndex;
    }

    readonly Vector3[] _slots;
    readonly Tile[] _tiles;
    readonly Vector3 _testSize;          // flat footprint for the observation test
    readonly Vector3 _progressAxis;      // "ahead of the player" direction for the hop bias
    readonly Color _tint;
    readonly float _colliderDropDelay;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    public SliverTileSet(Vector3[] slots, GameObject[] tiles, Vector3 testSize,
        Vector3 progressAxis, Color tint, float colliderDropDelay = 0.3f)
    {
        _slots = slots;
        _testSize = testSize;
        _progressAxis = progressAxis.normalized;
        _tint = tint;
        _colliderDropDelay = colliderDropDelay;
        _tiles = new Tile[tiles.Length];
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i].transform.position = slots[i];
            _tiles[i] = new Tile
            {
                tf = tiles[i].transform,
                rend = tiles[i].GetComponent<Renderer>(),
                col = tiles[i].GetComponent<Collider>(),
                mpb = new MaterialPropertyBlock(),
                slotIndex = i,
            };
        }
    }

    public void Tick(Vector3 playerPos)
    {
        foreach (var t in _tiles)
        {
            var b = new Bounds(t.tf.position, _testSize);
            bool observed = t.tracker.Tick(b, out _, float.PositiveInfinity);

            if (!observed && t.unobservedSince < 0f) t.unobservedSince = Time.time;
            if (observed) { t.unobservedSince = -1f; t.rearrangedSinceSeen = false; }

            Vector3 flat = t.tf.position - playerPos; flat.y = 0f;
            if (!observed && !t.rearrangedSinceSeen && t.unobservedSince > 0f
                && Time.time - t.unobservedSince > 0.35f && flat.magnitude > 1.6f)
            {
                Rearrange(t, playerPos);
                continue;
            }

            float targetAlpha = observed ? _tint.a : 0.02f;
            t.alpha = Mathf.MoveTowards(t.alpha, targetAlpha, Time.deltaTime / 0.2f);
            t.mpb.SetColor(ColorId, new Color(_tint.r, _tint.g, _tint.b, t.alpha));
            t.rend.SetPropertyBlock(t.mpb);
            t.col.enabled = observed || Time.time - t.unobservedSince <= _colliderDropDelay;
        }
    }

    void Rearrange(Tile t, Vector3 playerPos)
    {
        var candidates = new List<int>();
        for (int i = 0; i < _slots.Length; i++)
        {
            if (i == t.slotIndex) continue;
            bool occupied = false;
            foreach (var other in _tiles)
                if (other != t && other.slotIndex == i) occupied = true;
            if (!occupied) candidates.Add(i);
        }
        if (candidates.Count == 0) return;

        var hidden = candidates.FindAll(i => !ObserverState.IsObserved(new Bounds(_slots[i], _testSize + Vector3.up)));
        var pool = hidden.Count > 0 ? hidden : candidates;

        float playerProgress = Vector3.Dot(playerPos, _progressAxis);
        int pick = -1;
        if (Random.value < 0.6f)
        {
            float best = float.MaxValue;
            foreach (int i in pool)
            {
                float prog = Vector3.Dot(_slots[i], _progressAxis);
                if (prog > playerProgress + 1f && prog < best) { best = prog; pick = i; }
            }
        }
        if (pick < 0) pick = pool[Random.Range(0, pool.Count)];

        t.slotIndex = pick;
        t.tf.position = _slots[pick];
        t.tracker.Reset();
        t.unobservedSince = -1f;
        t.rearrangedSinceSeen = true;
        t.alpha = 1f;
        t.col.enabled = true;
    }
}

/// <summary>Generic fall-catcher: entering it puts the player back at a respawn point.</summary>
public class DimensionRespawnVolume : MonoBehaviour
{
    [HideInInspector] public Vector3 respawnPoint;

    PlayerController _player;
    int _playerRefindCooldown;

    void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<PlayerController>() == null) return;
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null) return;
        _player.Rigidbody.position = respawnPoint;
        _player.Rigidbody.velocity = Vector3.zero;
    }
}
