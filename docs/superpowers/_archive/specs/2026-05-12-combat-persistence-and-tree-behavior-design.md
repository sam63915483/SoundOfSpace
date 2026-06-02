# Combat persistence and tree-spit behavior — design

Date: 2026-05-12

## Problem

Three combat fixes bundled into one change:

1. **Save-scum exploit.** Saving and reloading despawns every live enemy on the planet — `CLAUDE.md` documents this explicitly ("Combat state is NOT saved — enemies respawn fresh on load"). Players can use the pause-menu save to wipe nearby enemies, then load and walk away while the spawner takes ~10s+ to rebuild them.
2. **Tree-spit instakill.** Enemies switch to the spit attack the moment they detect the player on a tree. There's no grace period and no way to interrupt the cycle short of getting off the tree entirely — and the existing 1.5s detection latch resets each frame the player is detected, so the player can never "ride out" the timer by jumping.
3. **NPC roadblocks.** Enemies that path into an `AlienNPCDamageable` (a villager, vendor, or other alien NPC) collide with the NPC's capsule and get stuck. They can damage the player on contact but not the NPC, so a single NPC body permanently blocks the lane to the player.

## Scope

In scope:
- Persist active enemies (kind + position + health) across save/load.
- Persist the `EnemySpawner` cooldown so save-cycling can't reset spawn timing.
- Add a 10-second pre-arm delay before enemies fire their spit attack on a tree-bound player, with timer-reset rules.
- Make enemies damage `AlienNPCDamageable` on collision contact (3 hits at default config).

Out of scope (intentionally still NOT saved, listed for clarity):
- Knockback velocity / timer (transient)
- Charge cycle phase (Toy3 walk→charge→cooldown — restarts fresh)
- Spit attack mid-cycle (resets)
- Ragdolling / dying enemies (already on their way out)
- Tree-episode timer (resets on load — player won't be mid-jump the instant they reload)
- `EnemyHealthBar` / shared `OnAnyEnemyDeath` killstreak HUD state

## Design

### Feature 1 — Save & restore enemies

#### Schema additions (`Assets/3 - Scripts/SaveSystem/SaveData.cs`)

```csharp
[Serializable]
public class EnemySave
{
    public string kind;                                          // "regular" or "elite"
    public BodyRelativeTransform xform = new BodyRelativeTransform();
    public float health;
}
```

On `SaveData`:
```csharp
public List<EnemySave> enemies = new List<EnemySave>();
public float enemySpawnTimer;                                    // EnemySpawner.timer
public int enemyRegularsSinceElite;                              // EnemySpawner._regularsSinceLastEnemy2
```

JsonUtility-compatible — no dictionaries, no polymorphism. Older saves with missing fields default to empty list and zero counters, which behave like "no live enemies, fresh spawner cooldown" — graceful.

#### `EnemyController.cs` API additions

```csharp
public enum EnemyKind { Regular, Elite }

[SerializeField] EnemyKind kind = EnemyKind.Regular;             // set Elite on Toy3 prefab
public EnemyKind Kind => kind;

public float CurrentHealth => currentHealth;
public bool IsDying => _dying;

public void SetHealthOnLoad(float h)
{
    currentHealth = Mathf.Clamp(h, 0f, maxHealth);
    if (healthBar != null && currentHealth < maxHealth)
    {
        healthBar.Show();
        healthBar.SetFill(Mathf.Clamp01(currentHealth / maxHealth));
    }
}
```

`kind` is serialized on the prefab. Toy10 stays `Regular` (default). Toy3 prefab is set to `Elite` in the inspector as part of this change.

#### `EnemySpawner.cs` API additions

```csharp
public void SpawnFromSave(EnemySave save)
{
    GameObject prefab = save.kind == "elite" && enemy2Prefab != null
        ? enemy2Prefab
        : enemyPrefab;
    if (prefab == null) return;

    CelestialBody planet = ResolveBodyByName(save.xform.bodyName);
    if (planet == null) return;

    Vector3 worldPos = planet.Position + planet.transform.rotation * save.xform.localPos;
    Quaternion worldRot = planet.transform.rotation * save.xform.localRot;

    GameObject go = Instantiate(prefab, worldPos, worldRot);
    go.transform.SetParent(planet.transform, true);

    var ec = go.GetComponent<EnemyController>();
    if (ec != null)
    {
        ec.SetHealthOnLoad(save.health);
        activeEnemies.Add(ec);
    }
}

public void RestoreTimerState(float timerValue, int regularsSinceElite)
{
    timer = timerValue;
    _regularsSinceLastEnemy2 = regularsSinceElite;
}
```

`ResolveBodyByName` is a small helper that walks `NBodySimulation.Bodies` and matches by `bodyName`. (If `EnemySpawner` already has equivalent it'll be reused.)

#### `SaveCollector.cs` — capture

```csharp
static void CaptureEnemies(SaveData data)
{
    foreach (var ec in EnemyController.ActiveEnemies)
    {
        if (ec == null || ec.IsDying) continue;
        var rb = ec.GetComponent<Rigidbody>();
        if (rb == null) continue;

        var planet = GetNearestPlanet(rb.position);
        if (planet == null) continue;

        var save = new EnemySave
        {
            kind = ec.Kind == EnemyKind.Elite ? "elite" : "regular",
            xform = CaptureBodyRelative(rb.position, rb.rotation, Vector3.zero, planet),
            health = ec.CurrentHealth,
        };
        data.enemies.Add(save);
    }

    if (EnemySpawner.Instance != null)
    {
        data.enemySpawnTimer = EnemySpawner.Instance.TimerForSave;
        data.enemyRegularsSinceElite = EnemySpawner.Instance.RegularsSinceEliteForSave;
    }
}
```

Velocity is zero — enemies are kinematic.

`EnemySpawner.TimerForSave` / `RegularsSinceEliteForSave` are simple property getters added for capture; the matching setter path is `RestoreTimerState` above.

#### `SaveCollector.cs` — apply

```csharp
static void ApplyEnemies(SaveData data)
{
    // Snapshot first, then destroy — modifying ActiveEnemies during iteration
    // would be invalidated by OnDisable's list.Remove.
    var existing = new List<EnemyController>(EnemyController.ActiveEnemies);
    foreach (var ec in existing)
        if (ec != null) Object.Destroy(ec.gameObject);

    if (EnemySpawner.Instance == null) return;

    foreach (var save in data.enemies)
        EnemySpawner.Instance.SpawnFromSave(save);

    EnemySpawner.Instance.RestoreTimerState(data.enemySpawnTimer, data.enemyRegularsSinceElite);
}
```

Called from `Apply(SaveData)` between `ApplyLooseParts` and `ApplyHeldItem`. Apply order rationale:
- After `ApplyCelestialBodies` — body-relative positions resolve correctly.
- After `ApplyLooseParts` — independent, just keeping spawn/destroy work batched.
- Before `ApplyHeldItem` — held-item resolution touches the player only, no dependency.

#### Edge cases

- **Spawner Instance is null at apply time.** Early-out. Existing enemies (none — we already destroyed them) and saved enemies (skipped). World stays clean.
- **`bodyName` from save no longer exists in scene.** `ResolveBodyByName` returns null → `SpawnFromSave` early-outs. Enemy is silently dropped. (Shouldn't happen with the current static planet set.)
- **Save written before this feature was added.** `data.enemies` deserializes empty, timer 0 — load behaves like the old "fresh spawn" path.
- **Enemy was on a planet that's now out of view (player moved).** Still respawns in place — they're parented to the planet which is in world space. The spawner's despawn-distance check in `FixedUpdate` will clean them up naturally if they're outside `despawnDistance` from the player.
- **`Destroy()` is end-of-frame.** `SpawnFromSave` calls don't see ghosts because the new instances are added to `ActiveEnemies` via the new enemies' `OnEnable`, and `Destroy()`'d ones are still in the list this frame but flagged null next frame. We snapshot before destroying so the iteration order is stable; the spawner's `activeEnemies` list prunes nulls each `Update`. No collisions between old and new instances because the spawner only iterates its own list and `EnemyController.ActiveEnemies` (which both prune nulls).

### Feature 2 — 10-second tree-spit delay

#### New file `Assets/3 - Scripts/Combat/PlayerTreeContactTracker.cs`

Singleton MonoBehaviour. Bootstrapped from `EnemyController.Start()` via `PlayerTreeContactTracker.EnsureExists()` so it auto-creates as soon as combat happens. Survives scene transitions via `DontDestroyOnLoad` (re-finds the player after a reload).

```csharp
public class PlayerTreeContactTracker : MonoBehaviour
{
    public const float SpitDelaySeconds = 10f;
    const float TrunkRadius = 1.5f;
    const float OnTopVerticalThreshold = 1.5f;          // matches EnemyController.playerOnTreeHeightThreshold

    static PlayerTreeContactTracker _instance;

    PlayerController _player;
    Transform _playerT;
    float _episodeStartTime = -1f;

    public static void EnsureExists()
    {
        if (_instance != null) return;
        var go = new GameObject("PlayerTreeContactTracker");
        _instance = go.AddComponent<PlayerTreeContactTracker>();
        DontDestroyOnLoad(go);
    }

    public static bool IsActive => _instance != null && _instance._episodeStartTime >= 0f;
    public static float SecondsActive =>
        IsActive ? Time.time - _instance._episodeStartTime : 0f;
    public static bool SpitArmed => IsActive && SecondsActive >= SpitDelaySeconds;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
    }
    void OnDestroy() { if (_instance == this) _instance = null; }

    void Update()
    {
        if (_player == null) _player = FindObjectOfType<PlayerController>();
        if (_player == null) { _episodeStartTime = -1f; return; }
        if (_playerT == null) _playerT = _player.transform;

        Vector3 up = ResolvePlayerUp();
        bool onTreeNow = OnAnyTree(_playerT.position, up);

        if (onTreeNow && _episodeStartTime < 0f)
            _episodeStartTime = Time.time;
        else if (_player.IsOnGround && !onTreeNow)
            _episodeStartTime = -1f;
        // else: preserve (mid-air, jumping in place, tree-to-tree)
    }

    Vector3 ResolvePlayerUp()
    {
        // referenceBody is private on PlayerController. Use the player's
        // transform.up (kept aligned to gravity by PlayerController.FixedUpdate).
        return _playerT.up;
    }

    static bool OnAnyTree(Vector3 playerPos, Vector3 up)
    {
        var trees = SpawnedTree.AllTrees;
        for (int i = 0; i < trees.Count; i++)
        {
            var t = trees[i];
            if (t == null || t.IsDead) continue;
            Vector3 toPlayer = playerPos - t.transform.position;
            Vector3 horiz = Vector3.ProjectOnPlane(toPlayer, up);
            float vertical = Vector3.Dot(toPlayer, up);
            if (horiz.sqrMagnitude < TrunkRadius * TrunkRadius
                && vertical > OnTopVerticalThreshold)
                return true;
        }
        return false;
    }
}
```

The detection logic is the same as `EnemyController.IsPlayerOnATree`, now centralised so all enemies share a single per-frame scan instead of each enemy re-scanning every tree.

#### `EnemyController.cs` — replace per-enemy tree state

Delete:
- `bool _playerOnTree`
- `float _playerOnTreeLatchUntil`
- `const float PlayerOnTreeLatchSeconds`
- `bool IsPlayerOnATree(Vector3 up)`

Add to `Start()`:
```csharp
PlayerTreeContactTracker.EnsureExists();
```

In `FixedUpdate`, replace the tree-detection block:

```csharp
// Was:
//   if (IsPlayerOnATree(up))
//       _playerOnTreeLatchUntil = Time.time + PlayerOnTreeLatchSeconds;
//   _playerOnTree = Time.time < _playerOnTreeLatchUntil;
//   if (_playerOnTree) { ...standoff locomotion + start spit cycle... }
// Becomes:
if (PlayerTreeContactTracker.IsActive)
{
    if (horizDist < spitStandoffMin)
        horizDir = -horizDir;
    else if (horizDist <= spitStandoffMax)
        horizDist = stoppingDistance;

    // Spit cycle only ARMS after the tracker's delay has elapsed. Locomotion
    // still backs the enemy up to standoff range during the pre-arm window.
    if (!_spitting
        && Time.time >= _nextSpitTime
        && PlayerTreeContactTracker.SpitArmed)
    {
        _spitting      = true;
        _spitFired     = false;
        _spitPhaseTime = 0f;
        _nextSpitTime  = Time.time + Random.Range(spitIntervalMin, spitIntervalMax);
    }
}
```

Behavior summary:
- t = 0s: player jumps on a tree → `_episodeStartTime = Time.time`, `IsActive = true`, enemies start backing up to standoff range.
- 0 < t < 10s: enemies stay at standoff, spit cycles do not arm. (`SpitArmed` is false.)
- t ≥ 10s: spit cycles arm — first cycle starts whenever `Time.time >= _nextSpitTime` (which began at 0 in Awake, so the first spit fires immediately at t = 10s, then on a 5-10s random cadence).
- Player jumps in place: `IsOnGround` flips false during airtime, `onTreeNow` stays true (or flickers but episode preserves) — timer keeps running.
- Player leaps tree → tree: `IsOnGround` false the whole time, episode preserves, timer keeps running.
- Player lands on ground: `IsOnGround = true && onTreeNow = false` → `_episodeStartTime = -1` → `IsActive = false` → enemies switch back to chase locomotion next tick, in-flight spits already in motion (`_spitting = true`) complete normally.

#### Edge cases

- **Player dies on a tree (ragdoll respawn).** Death freezes input and triggers respawn. The respawn places the player somewhere on the ground, not on a tree. Next tracker tick: `onTreeNow = false`, `IsOnGround = true` → episode clears. Good.
- **Player saves on a tree, reloads.** Tracker has no save state — fresh `_episodeStartTime = -1`. On load the player may still be at the on-tree position; the very next tracker tick will start a fresh episode at `Time.time`, restarting the 10s delay. This is acceptable; it's a quirk of the no-save policy and isn't an exploit vector (player still has to wait 10s for spit).
- **All trees on the planet are chopped while player is on one.** When the last tree is felled, `IsDead` flips true → tracker's `onTreeNow` becomes false next frame → episode ends on next ground contact.
- **No `PlayerController` exists (main-menu / load-in transition).** `Update` early-outs, episode stays cleared.

### Feature 3 — Enemies damage NPCs on contact

#### `EnemyController.cs` change

Add field:
```csharp
[Header("NPC Damage")]
[SerializeField] float npcContactDamage = 34f;       // 3 hits at AlienNPCDamageable's default 100 HP
```

Modify `OnCollisionStay`:

```csharp
void OnCollisionStay(Collision collision)
{
    if (_dying) return;
    if (Time.time < nextDamageTime) return;
    if (ResourceManager.Instance == null) return;

    if (collision.collider.CompareTag("Player"))
    {
        ResourceManager.Instance.TakeDamage(contactDamage);
        nextDamageTime = Time.time + contactDamageInterval;
        if (_oneShotSource != null && attackClip != null)
            _oneShotSource.PlayOneShot(attackClip, attackVolume);
        return;
    }

    var npc = collision.collider.GetComponentInParent<AlienNPCDamageable>();
    if (npc != null && !npc.IsDying)
    {
        npc.TakeDamage(npcContactDamage);
        nextDamageTime = Time.time + contactDamageInterval;
        if (_oneShotSource != null && attackClip != null)
            _oneShotSource.PlayOneShot(attackClip, attackVolume);
    }
}
```

`AlienNPCDamageable` currently has `bool _dying` as a private field — need to expose `public bool IsDying => _dying;` (one-line accessor).

The same `nextDamageTime` cooldown gates both player and NPC damage from the same enemy. Re-uses the existing `contactDamageInterval = 0.5f` so an enemy can't double-tap.

#### Why this doesn't false-positive

- Other enemies' colliders → `GetComponentInParent<AlienNPCDamageable>()` returns null. No friendly fire.
- Dead/ragdolling NPC bones → bones are children of the unparented ragdoll root, not the original NPC root that holds `AlienNPCDamageable`. `GetComponentInParent` returns null on bones. Also `_liveCollider` is disabled when `Die()` is called.
- Pre-placed NPC trigger BoxColliders → triggers don't fire `OnCollisionStay`. Only the runtime-added `_liveCollider` (non-trigger CapsuleCollider) generates contacts. ✓
- Self-collision → `_dying` early-out catches the enemy's own ragdoll spawning, but the enemy isn't an `AlienNPCDamageable` so this isn't a path that exists anyway.

#### Why the existing `AlienNPCDamageable.Die()` works as-is

- Plays hit sounds on each non-fatal hit via the path already wired in.
- On death: ragdolls, disables dialogue scripts (NPCDialogue, BonfireNPCDialogue, Alien7Vendor, RandomAlienDialogue), hides health bar, disables triggers, story-impact banner if `isStoryImpactful`. Notifies `AlienNPCSpawner` so the kill persists in `AlienKillsSave`.
- Mid-dialogue kills: if the player is talking to an NPC who is killed by an enemy, the dialogue scripts get disabled mid-conversation — same as a player-fired pistol kill mid-talk. That path already works; we inherit it.

## CLAUDE.md update

The `### Combat` section currently ends with:

> **Combat state is NOT saved** — enemies respawn fresh on load.

Replace with:

> **Active enemies are saved** (kind + position + health) via `EnemySave` and re-instantiated through `EnemySpawner.SpawnFromSave` on load. The spawner's cooldown timer is also saved so save-cycling can't reset spawn timing. Still NOT saved (transient mid-action state, resets to neutral on load): knockback velocity/timer, charge cycle phase, spit attack mid-cycle, ragdolling enemies, the tree-episode timer.

The `### Apply order (in `SaveCollector.Apply`)` section gains a new step:

> 9.5. **`ApplyEnemies`** — destroys existing live enemies, re-instantiates each `EnemySave` via `EnemySpawner.SpawnFromSave`, restores spawner timer + elite counter.

## Testing notes

There are no automated tests in this project — verification is Unity Play-mode only. Manual test plan:

1. **Save & restore.** Spawn enemies, walk close, save, reload from main menu → enemies should be at the same positions with the same health bars visible.
2. **Save-cycle spawn-timer.** Save right before a spawn fires, reload → spawn doesn't fire instantly; cooldown resumes from saved value.
3. **Elite vs regular.** Kill 3 regulars to queue an elite, save before the elite spawns, reload → next spawn should be the elite.
4. **Spit delay.** Climb a tree. Verify enemies back up but don't spit for 10s. After 10s, first spit fires. Continue to verify normal cadence.
5. **Spit timer preservation.** Jump on the tree, then jump off into the air and onto another tree — timer should keep running. Total time on trees should reach 10s for spit to arm even though you jumped between them.
6. **Spit timer reset.** Climb a tree, wait 5s, jump down to ground, climb again — second episode should be a fresh 10s.
7. **NPC damage.** Lead an enemy into a vendor / random alien — enemy should damage them, NPC health bar appears, NPC dies in 3 hits, ragdolls, story banner if applicable.
8. **NPC death clears blockage.** After NPC dies and ragdolls, enemy should resume pathing toward player.
9. **No friendly-fire / no self-damage.** Two enemies bumping into each other shouldn't damage either. Ragdolling NPC bones shouldn't damage the player or other enemies on touch.

## Risks / known gotchas

- The `[RuntimeInitializeOnLoadMethod]` / `EnsureGameplaySingletons` pattern isn't used for `PlayerTreeContactTracker` — it bootstraps from `EnemyController.Start()` instead. If a future change creates a path where the tracker is needed before any enemy exists, add `PlayerTreeContactTracker.EnsureExists()` to `MainMenuController.EnsureGameplaySingletons` at that point. (Not needed today — tree-spit behavior only matters when enemies exist.)
- `Destroy()` is end-of-frame. Apply order is fine because `SpawnFromSave` adds to `activeEnemies` directly (the new enemies don't rely on the destroyed ones being gone yet), and the `OnEnable` static-list registration is also immediate at `Instantiate` time.
- `EnemyKind` is serialized — adding it to existing prefabs is a one-time inspector edit. Toy3 prefab must be set to `Elite` as part of this change or saves will round-trip Toy3 enemies as `Regular`.
- `SpitArmed` only gates the *start* of a new spit cycle. An in-flight spit cycle that began before the player jumped completes normally. This matches the existing "spit cycle runs to completion regardless" behavior and is the intended feel.
