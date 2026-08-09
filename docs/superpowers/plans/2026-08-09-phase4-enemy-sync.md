# Phase 4 — Enemy Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement
> this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.
>
> **Resuming after a `/clear`:** everything needed is in this file plus
> `docs/superpowers/specs/2026-08-09-world-state-replication-design.md`. Read both, then
> start at Task 1. The design's three rules (host owns timers and dice, never send world
> coordinates, the authority ignores being told its own state) still apply.

**Goal:** Enemies behave like Minecraft mobs in co-op — one shared set, in the same places
on both machines, hunting whichever player is closest, damaging either of them, and killable
by either of them.

**Architecture:** The host is the only machine that runs enemy AI. Guests render pose-synced
puppets and never decide anything. Shooting an enemy uses the same analytic capsule test PvP
already uses, so enemy puppets stay collider-free and cannot shove anybody. Damage to a
player is decided by the host and sent to that player alone.

**Tech Stack:** Unity 2022.3 Built-in RP, C#, NGO 1.12, named messages via the existing
`WorldSync` transport.

---

## Why this makes guests FASTER, not slower

Worth stating up front because the instinct is the opposite.

Today **every machine runs full enemy AI** — vision cones, line-of-sight raycasts, the
stealth spot timers, search-and-sniff, pathing — for every enemy. After this phase only the
host does. A guest just moves puppets to received positions.

So a guest's per-frame enemy cost goes **down**, and the LOS raycasts (the expensive part of
the stealth revamp) disappear entirely on that machine. The host's cost is unchanged: it was
already running all of it.

The new cost is network, and it is small. Around 20 enemies × (planet-local position,
rotation, a state byte) at 10 Hz is on the order of a few KB/s — less than the player pose
sync already sends, and far less than the solar-system sync. If it ever does matter, the
lever is the tick rate and a distance cull, not the architecture.

**Net expectation: guests gain frames, the host is flat, bandwidth barely moves.**

---

## Design decisions

| Question | Decision | Why |
|---|---|---|
| Who runs AI? | **Host only.** | `EnemySpawner` and the AI both roll dice; two machines running them diverge rather than double-tick. It is also what makes guests cheaper. |
| Who does an enemy chase? | **The closest player, re-targeted continuously.** | Sam's call. You can pull a mob off your friend by getting closer, which is the co-op behaviour that makes them feel shared. |
| Do the stealth rules still apply? | **Yes, unchanged** — view cones, LOS, the 2s spot, sprint-instant, search-and-sniff, sun-death. | They just evaluate against the *closest* player instead of the only one. |
| Damage to a player? | **Host decides, tells that player.** | The host owns the AI, so it is the only machine that knows a swing landed. |
| "Hit by something I can't see"? | **Solved by tick rate, not by moving authority.** | At 10–15 Hz plus interpolation a guest sees an enemy within a few centimetres of where the host has it. Making the victim authoritative instead would mean a guest could simply refuse damage. |
| Shooting an enemy? | **Shooter tests locally, host confirms.** | Identical to PvP, and reuses `NetworkPlayerCombat.RayHitsCapsule`. |
| Enemy colliders on guests? | **Disabled**, like player puppets. | A kinematic collider swept by network poses shoves whatever it overlaps — the "host launched into space" bug. The capsule test removes any need for them. |

---

## File structure

**Create**
- `Assets/3 - Scripts/Multiplayer/EnemySync.cs` — the whole phase: spawn/despawn replication,
  pose streaming, damage in both directions.

**Modify**
- `Assets/3 - Scripts/Combat/EnemyController.cs` — gate the decision path on
  `WorldSync.IsAuthority`; retarget to the nearest player.
- `Assets/3 - Scripts/Combat/EnemySpawner.cs` — assign a network id on spawn and announce it.
- `Assets/3 - Scripts/Combat/EnemyVision.cs` — evaluate against the nearest player.
- `Assets/3 - Scripts/Multiplayer/PlayerRoster.cs` *(new, small)* — "every player position on
  this machine", so targeting has one place to ask.

`EnemySync` is deliberately one file: spawn, pose and damage are one conversation about one
entity, and splitting them would mean three files sharing an id table.

---

## Task 1: PlayerRoster — one place to ask "where is everybody"

Targeting, vision and damage all need the same answer, and none of them should each invent
their own scan.

**Files:**
- Create: `Assets/3 - Scripts/Multiplayer/PlayerRoster.cs`

- [x] **Step 1: Write it**

```csharp
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
    }

    static readonly List<Entry> _scratch = new List<Entry>();
    static PlayerController _localCached;
    static float _nextLocalScan;

    /// Rebuilt per call, into a shared list — cheap, and never handed out to be
    /// held onto. Do not cache the returned list.
    public static IReadOnlyList<Entry> All()
    {
        _scratch.Clear();

        if (_localCached == null && Time.unscaledTime >= _nextLocalScan)
        {
            _nextLocalScan = Time.unscaledTime + 0.5f;
            _localCached = Object.FindObjectOfType<PlayerController>();
        }
        if (_localCached != null)
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            _scratch.Add(new Entry
            {
                Transform = _localCached.transform,
                ClientId  = nm != null ? nm.LocalClientId : 0,
                IsLocal   = true,
            });
        }

        // Remote players are puppets; PlanetRelativeSync is on every one.
        var puppets = PlanetRelativeSync.AllPuppets;
        for (int i = 0; i < puppets.Count; i++)
        {
            var p = puppets[i];
            if (p == null || p.IsOwner) continue;   // our own puppet is the local rig
            _scratch.Add(new Entry
            {
                Transform = p.transform,
                ClientId  = p.OwnerClientId,
                IsLocal   = false,
            });
        }
        return _scratch;
    }

    /// The player nearest `point`, or null if there are none.
    public static Transform Nearest(Vector3 point, out ulong clientId)
    {
        clientId = 0;
        Transform best = null;
        float bestSqr = float.MaxValue;

        var all = All();
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i].Transform;
            if (t == null) continue;
            float d = (t.position - point).sqrMagnitude;
            if (d >= bestSqr) continue;
            bestSqr = d; best = t; clientId = all[i].ClientId;
        }
        return best;
    }

    public static void Forget() { _localCached = null; _nextLocalScan = 0f; }
}
```

- [x] **Step 2: Add the puppet list `PlayerRoster` reads**

`PlanetRelativeSync` needs a static instance list, following the `AllInstances` convention
already used by `EnemyController.ActiveEnemies` and `SpawnedTree.AllTrees`.

In `PlanetRelativeSync`, add near the top of the class:

```csharp
    /// Live puppets, for anything that needs "where is every player" — enemy
    /// targeting, mainly. Maintained in OnNetworkSpawn/OnNetworkDespawn rather
    /// than by scanning, per the AllInstances convention.
    static readonly List<PlanetRelativeSync> s_all = new List<PlanetRelativeSync>();
    public static IReadOnlyList<PlanetRelativeSync> AllPuppets => s_all;
```

and in its `OnNetworkSpawn` add `s_all.Add(this);`, in `OnNetworkDespawn` add
`s_all.Remove(this);`. If either override does not exist yet, create it and call `base`.

- [x] **Step 3: Clear the cache on scene load**

In `WorldSync.OnSceneLoaded`, add `PlayerRoster.Forget();` — the cached `PlayerController`
belongs to the old scene.

- [x] **Step 4: Verify it compiles**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [x] **Step 5: Harness**

Write to `<scratchpad>/TestPlayerRoster.cs` and run via `execute_script`:

```csharp
using System.Text;
using UnityEngine;

public static class TestPlayerRoster
{
    public static string Execute()
    {
        var sb = new StringBuilder();
        int fail = 0;
        void Check(string l, bool ok, string d)
        { if (!ok) fail++; sb.AppendLine((ok ? "PASS  " : "FAIL  ") + l + "  ->  " + d); }

        var all = PlayerRoster.All();
        Check("single player yields exactly one entry", all.Count == 1, all.Count + " entr(ies)");
        if (all.Count > 0) Check("and it is the local player", all[0].IsLocal, "IsLocal");

        var t = PlayerRoster.Nearest(Vector3.zero, out ulong id);
        Check("Nearest returns the local player", t != null, t != null ? t.name : "null");

        // A point far away must still resolve to the only player there is.
        var t2 = PlayerRoster.Nearest(new Vector3(99999f, 0f, 0f), out _);
        Check("Nearest never returns null while a player exists", t2 != null, "ok");

        sb.Insert(0, fail == 0 ? "ALL PASS\n\n" : fail + " FAILURE(S)\n\n");
        return sb.ToString();
    }
}
```

Expected: `ALL PASS`

- [x] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/Multiplayer/PlayerRoster.cs" "Assets/3 - Scripts/Multiplayer/PlayerRoster.cs.meta" "Assets/3 - Scripts/Multiplayer/PlanetRelativeSync.cs" "Assets/3 - Scripts/Multiplayer/WorldSync.cs"
git commit -m "feat(mp): PlayerRoster - one answer to 'where is every player'

Enemy targeting, vision and damage all need it. Without a shared source each
would grow its own scan, they would disagree at the edges, and an enemy would
look at one player while swinging at another."
```

---

## Task 2: Retarget to the nearest player

**Files:**
- Modify: `Assets/3 - Scripts/Combat/EnemyController.cs`
- Modify: `Assets/3 - Scripts/Combat/EnemyVision.cs`

- [x] **Step 1: Find every place the enemy resolves "the player"**

Run: `grep -n "FindObjectOfType<PlayerController>\|FindWithTag(\"Player\")\|_player\b" "Assets/3 - Scripts/Combat/EnemyController.cs" "Assets/3 - Scripts/Combat/EnemyVision.cs"`

Record each. Each is a place that currently assumes one player.

- [x] **Step 2: Replace the cached single player with a per-tick nearest lookup**

In `EnemyController`, wherever the player transform is cached, replace the resolution with:

```csharp
    /// Re-evaluated every decision tick, not cached: Sam's rule is that an enemy
    /// chases whoever is CLOSEST, and that has to be able to change mid-chase so
    /// you can pull a mob off your friend by stepping in.
    ///
    /// Only the identity of the target is re-picked here. The stealth state
    /// machine (view cone, LOS, the 2s spot, search-and-sniff) is untouched and
    /// simply evaluates against whoever this returns.
    Transform ResolveTarget()
    {
        var t = PlayerRoster.Nearest(transform.position, out _targetClientId);
        return t;
    }

    ulong _targetClientId;
```

Then call `ResolveTarget()` at the top of the AI tick and use its result in place of the
cached field. Do NOT change the vision or spot-timer logic — only which transform it looks at.

- [x] **Step 3: Same in EnemyVision**

Wherever `EnemyVision` resolves the player, use `PlayerRoster.Nearest(transform.position, out _)`.

- [x] **Step 4: Verify it compiles, and that single player is unchanged**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

`PlayerRoster.Nearest` returns the one local player in single player, so behaviour there is
identical by construction. Confirm by playing single player and checking an enemy still
spots, chases and loses you exactly as before.

- [x] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Combat/EnemyController.cs" "Assets/3 - Scripts/Combat/EnemyVision.cs"
git commit -m "feat(mp): enemies chase whoever is closest, re-targeted every tick

Only the target's identity changes - the stealth state machine (view cones,
LOS, the 2s spot, search-and-sniff, sun-death) is untouched and simply
evaluates against whoever is nearest. Single player is identical by
construction: the roster returns exactly one player there."
```

---

## Task 3: EnemySync — replicate spawns, poses and deaths

**Files:**
- Create: `Assets/3 - Scripts/Multiplayer/EnemySync.cs`
- Modify: `Assets/3 - Scripts/Combat/EnemySpawner.cs`
- Modify: `Assets/3 - Scripts/Combat/EnemyController.cs`

- [x] **Step 1: Give every enemy a stable network id**

Enemies are runtime-spawned, so there is no cell id to key on. The host assigns an
incrementing `uint` at spawn; guests key on it.

In `EnemyController`, append (serialized fields go at the END — CLAUDE.md):

```csharp
    /// Host-assigned identity for the sync layer. 0 means "not replicated".
    /// NOT serialized: it is assigned at spawn and meaningless on disk.
    [System.NonSerialized] public uint NetId;
```

In `EnemySpawner`, immediately after each `Instantiate`, assign one:

```csharp
        var ec = go.GetComponent<EnemyController>();
        if (ec != null) ec.NetId = EnemySync.NextNetId();
```

- [x] **Step 2: Write EnemySync**

Create `Assets/3 - Scripts/Multiplayer/EnemySync.cs`, following `StorageSync`'s shape exactly
(auto-singleton, named messages, explicit client addressing, never `SendNamedMessageToAll`).

Message kinds:

| Kind | Direction | Payload |
|---|---|---|
| `Spawn` | host → clients | netId, prefabIndex, bodyName, planet-local pos + rot |
| `Pose` | host → clients | count, then per enemy: netId, planet-local pos + rot, anim state byte |
| `Death` | host → clients | netId |
| `Hit` | client → host | netId, damage |
| `PlayerDamage` | host → one client | amount |

**Poses go in ONE batched message per tick**, not one per enemy — twenty separate named
messages every tick is where the bandwidth would actually go.

Rules to preserve, each already paid for elsewhere in this codebase:
- **Planet-local coordinates only.** Floating-origin rebases fire while standing still.
- **Never `SendNamedMessageToAll`** — it loops back to the host. Address clients explicitly,
  as `WorldSync.Dispatch` does.
- **Guests never spawn or despawn an enemy themselves** — only on a message.
- **Guest enemy colliders disabled** on receipt, mirroring `NetworkPlayerSetup`.

Send poses at **10 Hz** and interpolate on the receiving side, exactly as `PlanetRelativeSync`
does for players. Do not send every frame.

- [x] **Step 3: Gate the AI**

At the top of `EnemyController`'s decision tick:

```csharp
        // HOST ONLY. A client running this would not merely double-tick - the AI
        // rolls dice and reads its own local player, so two machines produce
        // DIFFERENT chases. Guests move enemies only from received poses.
        //
        // This is also why guests get FASTER: the vision cones and LOS raycasts
        // below stop running on every machine but one.
        if (!WorldSync.IsAuthority) { ApplyNetworkPose(); return; }
```

Keep animation, audio and rendering outside the gate — a guest still has to see the enemy
walk.

- [x] **Step 4: Verify it compiles**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [x] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Multiplayer/EnemySync.cs" "Assets/3 - Scripts/Multiplayer/EnemySync.cs.meta" "Assets/3 - Scripts/Combat/EnemySpawner.cs" "Assets/3 - Scripts/Combat/EnemyController.cs"
git commit -m "feat(mp): replicate enemy spawns, poses and deaths

Host-only AI; guests render pose-synced puppets at 10Hz with interpolation.
Poses are ONE batched message per tick - twenty separate named messages would
be where the bandwidth actually went. Planet-local coordinates throughout,
because floating-origin rebases fire while standing still."
```

---

## Task 4: Shooting enemies, and enemies hurting players

**Files:**
- Modify: `Assets/3 - Scripts/Multiplayer/EnemySync.cs`
- Modify: `Assets/3 - Scripts/Pickups/PistolController.cs`

- [x] **Step 1: Guests report hits instead of applying them**

`PistolController.TriggerShot` already finds `IDamageable` via a raycast. On a guest that
raycast will miss, because enemy puppet colliders are disabled — so guests test analytically,
reusing the PvP capsule test.

In `EnemySync`, subscribe to `PistolController.OnLocalShotFired` (the hook Phase-4-PvP added)
and, on a guest, test the ray against every enemy capsule using
`NetworkPlayerCombat.RayHitsCapsule`. On the nearest hit within `shot.WorldHitDistance`, send
`Hit(netId, damage)` to the host.

On the host, apply the damage through the enemy's normal `TakeDamage`, so death, ragdoll,
loot and the kill-cam all run exactly as they do in single player.

- [x] **Step 2: Enemy damage to a player**

When the host's AI lands a hit, it knows the victim's `clientId` from `ResolveTarget`. If that
is the host, apply locally; otherwise send `PlayerDamage` to that client alone, which applies
it via `ResourceManager.TakeDamage`.

This is the one place the guest is not authoritative over its own health. That is deliberate:
the host owns the AI, so it is the only machine that knows a swing connected. The 10 Hz pose
stream is what stops it feeling unfair — see the design table.

- [x] **Step 3: Verify it compiles**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [x] **Step 4: Harness the hit test against enemy capsules**

Reuse the approach from `TestCombatMaths.cs` — that harness caught a sign error that made
point-blank chest shots miss. Same risk here.

- [x] **Step 5: Commit**

---

## Task 5: Whole-phase verification

- [x] **Step 1: Compile**

Run: `mcp__coplay-mcp__check_compile_errors` → `No compile errors`

- [x] **Step 2: No editor scripts leaked into the project**

Run: `grep -rln "using UnityEditor" --include="*.cs" "Assets/3 - Scripts" | grep -v "/Editor/"`
Expected: no output.

- [x] **Step 3: No broadcast in any sync file**

Run: `grep -n "SendNamedMessageToAll" "Assets/3 - Scripts/Multiplayer/"*.cs`
Expected: comments only. A live call is the Phase 2 rebroadcast storm.

- [x] **Step 4: Playtest list for Sam**

- Both players see the SAME enemies in the SAME places.
- An enemy chases whoever is closest; walking past your friend pulls it onto you.
- Stealth still works: crouch/hide and it loses you; sprint and it spots you instantly.
- Either player can shoot and kill one; it dies on both screens.
- An enemy damages whoever it is actually attacking, and only them.
- Nobody takes damage from an enemy they cannot see.
- Sun-death still kills them at dawn, on both screens.
- Single player is completely unchanged.
- **Frame rate on the GUEST should be the same or better than before.**

---

## Self-review notes

- **Spec coverage:** the design's Phase 4 calls for host-simulated enemies, guests as puppets,
  damage reusing the shooter-authoritative channel in reverse, and stealth running only on the
  host. Tasks 2–4 cover all four. The closest-player rule is Sam's later addition and is Task 2.
- **Placeholders:** Task 3 Step 2 describes `EnemySync` by its message table and rules rather
  than full source, because the file's shape must follow `StorageSync`, which will have been
  read by then. Every other step has complete code.
- **Type consistency:** `EnemyController.NetId` (Task 3 Step 1) is used by every message in
  Step 2. `PlayerRoster.Nearest(Vector3, out ulong)` (Task 1) is called in Task 2 Step 2 and
  Task 4 Step 2 with that exact signature.
- **Risk:** Task 2 touches the stealth revamp, which is tuned and easy to break. The mitigation
  is that only the TARGET changes, never the rules — and single player must be re-tested, since
  the roster returns one player there and behaviour should be bit-identical.
