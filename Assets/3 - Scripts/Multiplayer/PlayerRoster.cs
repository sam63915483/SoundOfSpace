using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every player on this machine — the real local rig plus one entry per remote
/// puppet — as plain positions.
///
/// Exists so enemy targeting, vision and damage all ask the same question in the
/// same way. Without it each would grow its own FindObjectsOfType scan, they
/// would disagree at the edges, and an enemy would look at one player while
/// swinging at another.
///
/// Single player returns exactly one entry, so callers never branch on mode.
/// </summary>
public static class PlayerRoster
{
    public struct Entry
    {
        public Transform Transform;
        public ulong ClientId;      // 0 and meaningless in single player
        public bool IsLocal;
        /// Sprinting multiplies how fast an enemy's suspicion fills, so it has to
        /// be answerable for a remote player too — and a puppet has no
        /// PlayerController to ask.
        public bool IsSprinting;
    }

    static readonly List<Entry> _scratch = new List<Entry>();
    static PlayerController _localCached;
    static float _nextLocalScan;

    /// <summary>
    /// Rebuilt per call, into a SHARED list — cheap, and allocation-free.
    ///
    /// ⚠️ The returned list is invalidated by the next call to All() or Nearest().
    /// Never cache it, and never call either one while iterating it: the second
    /// call clears the list you are walking. Copy out the Transforms you need
    /// first if you have to do both.
    /// </summary>
    public static IReadOnlyList<Entry> All()
    {
        _scratch.Clear();

        if (_localCached == null && Time.unscaledTime >= _nextLocalScan)
        {
            _nextLocalScan = Time.unscaledTime + 0.5f;
            _localCached = FindRealLocalPlayer();
        }
        if (_localCached != null)
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            _scratch.Add(new Entry
            {
                Transform   = _localCached.transform,
                ClientId    = nm != null ? nm.LocalClientId : 0,
                IsLocal     = true,
                IsSprinting = _localCached.IsSprinting,
            });
        }

        // Remote players are puppets; PlanetRelativeSync is on every one.
        var puppets = PlanetRelativeSync.AllPuppets;
        for (int i = 0; i < puppets.Count; i++)
        {
            var p = puppets[i];
            if (p == null || p.IsOwner) continue;   // our own puppet is the local rig
            // A puppet is hidden until its first pose lands. Targeting one before
            // then would point every enemy at the default spawn point, which is
            // wherever NGO happened to instantiate it — not where that player is.
            if (!p.RemotePlaced) continue;
            _scratch.Add(new Entry
            {
                Transform   = p.transform,
                ClientId    = p.OwnerClientId,
                IsLocal     = false,
                IsSprinting = p.RemoteSprinting,
            });
        }
        return _scratch;
    }

    /// <summary>
    /// The REAL scene player, never a puppet.
    ///
    /// NetworkPlayerSetup destroys every MonoBehaviour on a spawned puppet, so a
    /// puppet normally has no PlayerController at all — but that strip happens in
    /// OnNetworkSpawn, and a scan landing in the frame before it would otherwise
    /// cache a puppet as "the local player" forever. The NetworkObject test is
    /// the same one PlanetRelativeSync.TryResolveRefs uses.
    /// </summary>
    static PlayerController FindRealLocalPlayer()
    {
        var all = Object.FindObjectsOfType<PlayerController>(true);
        for (int i = 0; i < all.Length; i++)
        {
            var pc = all[i];
            if (pc == null) continue;
            if (pc.GetComponent<Unity.Netcode.NetworkObject>() != null) continue;
            return pc;
        }
        return null;
    }

    /// The player nearest `point`, or null if there are none.
    public static Transform Nearest(Vector3 point, out ulong clientId)
    {
        bool found = NearestEntry(point, out Entry e);
        clientId = e.ClientId;
        return found ? e.Transform : null;
    }

    /// <summary>
    /// The nearest player WITH everything known about them — who they are and
    /// whether they are sprinting. Enemy vision needs both, and resolving them
    /// separately is how the two end up describing different people.
    /// </summary>
    public static bool NearestEntry(Vector3 point, out Entry best)
    {
        best = default;
        bool found = false;
        float bestSqr = float.MaxValue;

        var all = All();
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i].Transform;
            if (t == null) continue;
            float d = (t.position - point).sqrMagnitude;
            if (d >= bestSqr) continue;
            bestSqr = d; best = all[i]; found = true;
        }
        return found;
    }

    /// Which client `t` belongs to, or 0 if it is not a player we know about.
    /// Damage decided by the host has to reach the right machine.
    public static ulong ClientIdFor(Transform t)
    {
        if (t == null) return 0;
        var all = All();
        for (int i = 0; i < all.Count; i++)
            if (all[i].Transform == t) return all[i].ClientId;
        return 0;
    }

    /// True when `t` is this machine's own player rig (as opposed to a puppet).
    /// Damage decided by the host has to know whether to apply locally or post it
    /// to a client, and comparing transforms is the only honest way to ask.
    public static bool IsLocalPlayer(Transform t)
    {
        if (t == null) return false;
        if (_localCached != null) return _localCached.transform == t;
        var real = FindRealLocalPlayer();
        return real != null && real.transform == t;
    }

    public static void Forget() { _localCached = null; _nextLocalScan = 0f; }
}
