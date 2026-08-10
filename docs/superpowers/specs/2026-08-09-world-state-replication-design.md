# World-State Replication — design

**Date:** 2026-08-09 · **Branch:** `feat/helmet-hud` · **Status:** design, awaiting approval

Phase B proper: two players harvesting mushrooms, cultivating Humble Abode,
taking texts and closing deals in the same world. Everything up to now has been
the *front door* (join, identity, poses, PvP). This is the world itself.

---

## 0. The finding that shapes everything

Replicating "all the trees and mushrooms" sounds like thousands of networked
objects. It is not.

`MushroomSpawner` and `TreeSpawner` are **deterministic cube-face hash
functions**:

```
SpawnerCubeface.Hash(seed, face, cellU, cellV, salt)
```

Existence, jitter, species, scale, colour and rotation are all pure functions of
`(seed, cell)`. The seed is an authored field, identical in both builds.
**Both machines already generate a byte-identical world.** Nothing about the
base world ever needs to cross the wire.

What differs is only the **delta** — what has been harvested, chopped, planted,
built, killed. And there is already a compact, proven, `JsonUtility`-safe
vocabulary for precisely that delta, because the save system had to solve the
same problem: `PlantedMushroomSave`, `SaplingSave`, `PlacedBuildingSave`,
`EnemySave`, `BuyerLedgerSave`, `WorldPropConsumedSave`.

> **The save schema is the network schema.**

That is the spine of this design. A joining guest is, conceptually, *loading the
host's save* — and then receiving small incremental edits to it.

### What is NOT deterministic

Two systems roll dice at runtime and would therefore **diverge**, not merely
double-tick:

- `EnemySpawner` — `Random.Range` for placement angle and distance.
- `BuyerLedger` / `BuyerDeals` / `BuyerMessageDirector` — `Random.value` for
  bonds, offer sizes, and text timing.

These cannot be left to run independently. They must be host-owned.

---

## 1. Principles

Three rules, each already paid for in blood elsewhere in this codebase.

**1. One owner for every state machine.** The host owns every timer and every
dice roll. Clients report *inputs* ("I chopped cell 4,12,7") and render what
they are told. The stasis pod door cost three attempts to learn this: mirroring
state, then mirroring each machine's "wish", both failed because every machine
was still *running the rules*. See `StasisDoorSync`'s class comment.

**2. Never send world coordinates.** Floating-origin rebases fire while standing
still. Everything is expressed as a **cell id** `(bodyName, face, u, v)` or as
body-relative position — which is what the save already does, and why saves
survive orbital motion.

**3. The authority ignores being told its own state.** `SendNamedMessageToAll`
loops back to the host (`CustomMessageManager.cs:342`). Every handler needs a
`!IsServer` guard on host→client messages, or the host wipes its own pending
work every resend. This is the bug that made the pod door un-closable.

---

## 2. Decisions taken

| Decision | Choice | Consequence |
|---|---|---|
| Whose save is the world? | **The host's, only.** | Guests bring a character and take it home; their own worlds are untouched. No merge logic anywhere. |
| Who gets paid for a co-op deal? | **Whoever closes it.** | One player can text the offer while the other sprints to the NPC. Money is personal, consistent with the character-carries-money plan. |
| Enemy authority | **Host simulates.** | Non-deterministic spawner leaves no alternative. Guests see puppets. |
| Damage to players | **Shooter-authoritative** (unchanged) | Already shipped for PvP; enemies reuse it in reverse — the host tells the victim. |

---

## 3. Phase 0 — Vault and fix (prerequisite)

Shrink the surface before syncing it. Nothing here is deleted; everything is
gated so it returns by flipping one flag.

**New `FeatureVault` flags**, each with the same "why it's held, not failed"
comment style the existing ones use:

- `ConcertVenue` — the stage, `AudienceZone`/`AudienceZone 2`/`Max Audience`,
  `_StrobeRig`, `_StrobeVisual`, cone beams, `AudienceSpawner`, the whole
  `Concert/` script folder's runtime entry points.
- `TevCabinAmbush` — the ship outside Tev's cabin and its jumpscare.
- `ShipSchool` — `Combined_SHIPSCHOOL_0/1/2` and the instructor flow.
- `VillageTev` — Tev's village presence, onboarding and dialogue hooks.

Scene objects are deactivated at runtime by a small gate component rather than
deleted, so the scene file is not rewritten and nothing is lost. Scripts stay
compiled.

**The five review fixes**, in priority order:

1. `CharacterStore` — atomic `tmp` + `File.Replace` save; quarantine an
   unreadable `characters.json` to `.corrupt-<timestamp>` instead of letting the
   next mutation overwrite it. *(Confirmed real: raw `WriteAllText`, and the
   catch starts an empty book.)*
2. `NetworkPlayerCombat` — drop the damage amount from the wire; the victim
   applies the shared `const`.
3. `PistolController.ShotInfo` gains `MaxTracerLength`; delete the per-shot
   `FindObjectOfType`.
4. Surrogate-safe trimming in `CharacterProfile.Sanitize` and
   `NetworkPlayerIdentity.Truncate`.
5. `MultiplayerDeathRespawn` — clear `PlayerController.isInDialogue` in
   `OnSceneLoaded`. *(Confirmed real: nothing else clears it, so an interrupted
   respawn soft-locks the next session.)*

**Exit criteria:** compiles; concert/school/Tev absent in play; a hand-corrupted
`characters.json` is quarantined, not destroyed.

---

## 4. Phase 1 — The sync spine

The only phase that is genuinely new architecture. Everything after it is a
variation.

### `WorldSync` — one transport, one authority model

A `NetworkBehaviour` on a host-owned scene object (not the player), providing:

- **`Report(Delta)`** — client → host. "I did this." The host validates and
  applies.
- **`Broadcast(Delta)`** — host → all. Applied by clients, ignored by the host
  (rule 3).
- **`Snapshot`** — host → one joining client. The full world delta, sent once.

A `Delta` is a small tagged struct: `kind` byte, `bodyName`, cell id, plus at
most two payload ints/floats. Deliberately not a general RPC framework —
enumerable kinds keep it debuggable and keep the wire small.

### The join snapshot

The moment that makes a guest's world match the host's. Reuses `SaveCollector`:

1. Host captures the **world subset** of `SaveData` (planted mushrooms, chopped
   trees/saplings, placed buildings, buyer ledger, enemies, storage) — the same
   capture the autosave already performs.
2. Serialised with `JsonUtility`, chunked over `NetworkDelivery.ReliableFragmentedSequenced`.
3. Guest applies it through the **existing** `SaveCollector.Apply` paths for
   those systems, respecting the documented 17-step order for the steps involved.

This is why the save schema being the network schema matters: the snapshot is
not new code, it is an existing, tested, order-aware code path pointed at a
socket instead of a file.

### Host-gating the tickers

Every singleton that ticks a timer or rolls dice gets a `if (!WorldSync.IsAuthority) return;`
early-out on its *decision* path, keeping its *rendering* path alive. Inventory
of these is produced during implementation; known members: `MushroomSpawner`
respawn loop, `MushroomGrowth`, `EnemySpawner`, `BuyerMessageDirector`,
`PlanetOxygen`, dome fuel.

**Exit criteria:** a guest joining an in-progress world sees the host's chopped
trees, harvested mushrooms and placed buildings. Nothing ticks twice.

---

## 5. Phase 2 — Harvestables

Mushrooms and trees. Pure deltas on the deterministic base.

- **Harvest / chop:** the actor reports the cell id; the host confirms and
  broadcasts; everyone removes it. Host-confirmed so two players cannot both
  bank the same mushroom — the classic co-op duplication bug.
- **Plant:** reported with species + body-relative pose (a planted mushroom is
  *not* seed-derived, so it needs a real id — `PlantedMushroomSave` already
  defines one).
- **Growth and respawn:** host-only timers, broadcast on state change.

**Exit criteria:** both players harvest the same field without duplication;
respawns appear simultaneously; planted spores mature identically.

---

## 6. Phase 3 — Buildables and the locker

- **Placement:** reported to the host, which owns the canonical list and
  broadcasts. Naming stays `<prefab>_Placed` parented to a `CelestialBody`, or
  the save system will not find it.
- **Shuttle locker / storage:** shared container. Host-authoritative slot
  mutations — the only safe way to stop two players withdrawing the same stack.
  Contention here is far more likely than anywhere else, so the host arbitrates
  every move rather than trusting the client.

---

## 7. Phase 4 — Enemies

Host-simulated, guests see puppets. Reuses the player-puppet architecture:
non-colliding, pose-synced, animation-parameter driven.

- Spawns, AI decisions, and deaths are host-only and broadcast.
- Damage **to** a player uses the existing shooter-authoritative channel in
  reverse: the host tells the victim to apply it.
- Damage **from** a player already works — the pistol's `IDamageable` path only
  needs the host to be the one that actually decrements.

The stealth systems (view cones, LOS, sniff/search) run only on the host, which
also removes their per-client cost.

### Built 2026-08-09 — compiles + edit-mode harnesses pass, TWO-INSTANCE PLAYTEST PENDING

`Multiplayer/EnemySync.cs` + `Multiplayer/PlayerRoster.cs`, per
`plans/2026-08-09-phase4-enemy-sync.md`. Six things the plan did not anticipate,
each found by reading the live code rather than by testing:

1. **The join snapshot double-spawned enemies.** `SaveCollector.ApplyWorldSubset`
   called `ApplyEnemies`, which would have given the guest a second set with no
   `NetId` that no message could ever move, kill or remove. Removed from the
   subset; `EnemySync` owns enemies end-to-end and clears the field itself. Full
   `Apply()` still restores from disk, unchanged.
2. **Contact damage could not reach a guest.** `OnCollisionStay` needs a collider
   and player puppets have none, so `EnemyController.TryBiteRemotePlayers` was
   added, mirroring the existing `TryBiteNearbyNPC` — which exists for exactly
   the same reason.
3. **Nothing spawned near a guest.** `EnemySpawner` anchored on the local rig
   only. It now round-robins the roster, and the no-pop-in occlusion test runs
   from every player's viewpoint.
4. **Melee would have been dead in co-op.** `useClassicSwing` is false, so
   `BladeSweep`'s collider `SphereCastAll` is the only melee path, and a puppet
   has no colliders. It got a separate analytic pass (not a refactor — that loop
   is tuned through seven feel iterations, and with no puppets present it is one
   early return).
5. **Kill credit went to the host.** The killstreak, the slow-mo and the GANGSTA
   REP now travel with the Death message to the machine that earned them.
6. **A guest had no spot-meter.** `Suspicion01` lives on the host, so the stealth
   loop was invisible on the other screen. The pose stream carries suspicion plus
   the target's client id, and a guest only fills its meter for aliens actually
   looking at it.

Deviation from the plan's text: identity is assigned by a per-frame SWEEP over
`EnemyController.ActiveEnemies`, not by an `EnemySync.NextNetId()` call wired
into each `Instantiate`. One mechanism covers every creation path; a path that
forgets to call a hook is how an enemy ends up invisible on the other screen with
nothing in the log.

Known gaps, deliberate: a guest's spit projectile is never drawn (spit only ever
arms against the host's own player, because `PlayerTreeContactTracker` watches
local feet), and a guest's MISSED gunshots do not alert nearby aliens (only the
enemy actually hit is alerted, via the host's own `TakeDamage`).

### Three playtest rounds — Phase 4 is now PLAYTESTED GOOD

Sam, round 3: *"the enemies are synced and working now."* What the rounds cost,
because the lesson generalises to every remaining phase:

**Round 1 — enemies ignored the guest entirely.** Perception evaluated the
NEAREST player and nobody else. Co-op players travel together, so the nearest was
usually the host and the guest was never a *candidate*.
> **Any "nearest/closest player" rule silently becomes "only player" in co-op.
> Audit every one of them before writing a line of the next phase.**

**Round 2 — the guest's meter filled, then the alien turned away.** Two causes,
both introduced by the round-1 fix: an `if (d2 >= seenSqr) continue` optimisation
that reproduced nearest-only, and — the real one — **suspicion was a single float
per enemy**, so with two players the meter did not drain, it was HANDED to the
other. Now one meter per player.
> **State that is per-enemy in single player is often per-enemy-PER-PLAYER in
> co-op. Suspicion, aggro, interaction locks, quest progress: check each.**

**Round 3 — stepping in front of a chasing mob did nothing**, because the alien
was pegged at 1.00 on its quarry and the newcomer needed two seconds to tie.
Suspicion wins while DETECTING, distance wins while CHASING.

Also fixed across the rounds: gunshots from a guest aggroed nothing
(`AlertNearby` is local, and a guest's enemies are puppets whose AI never runs);
sprint did not travel; PvP bullets missed (0.45 m capsule against a ~0.5 m
smoothing trail); a player hit produced no feedback at all; and
`VillageWard` now makes the village a real safe haven.

**A wrong hypothesis, shipped and backed out.** Round 1 I "fixed" an LOS ray I
believed grazed the ground at a collider-less player's feet. Measuring said the
player root is at the BODY CENTRE — the ray runs at chest height and local and
remote were symmetric all along.
> **Drive the real method by reflection against real colliders before believing
> a geometry theory — and test the layer you CHANGED, not the one beneath it.
> Round 2's hole survived precisely because the harness called `CanSee` directly
> and never went through `ScanForPlayers`.**

---

## 8. Phase 5 — Economy: bonds, texts, deals

The most design-sensitive phase, and the reason for the ordering.

- **Bonds and buyer state:** host-owned `BuyerLedger`, broadcast on change.
- **Texts:** the host rolls and broadcasts the message; **every player receives
  the identical text** in their Messages app.
- **Responding:** any player can reply. **First response wins** — the host
  arbitrates and broadcasts the accepted offer; late responders see it already
  answered rather than a silent no-op.
- **Closing:** whoever presses F on the NPC first completes the deal and banks
  the money. Host arbitrates; the loser gets a clear "already sold" rather than
  a dead interaction.

Both races resolve on the host, so there is exactly one arbiter and no
tie-breaking ambiguity.

### Built 2026-08-09 — compiles, harness passes, TWO-INSTANCE PLAYTEST PENDING

`Multiplayer/EconomySync.cs`. The buyer is shared (bond, regular status, open
conversation, thread, and **how full they are**); the money is personal.
`PlayerWallet` appears nowhere in the sync layer and a harness asserts it stays
that way.

Sam's added requirement — *"if you make a deal with an npc and they are full,
player 2 walks up and tries to sell to the same npc and sees they are full"* —
is why `MushroomDealState` replicates at all. It was session-only statics with
no save schema, so it gained `Capture`/`Apply` with **relative** times.

**Whole-snapshot replication, on a version counter.** The host ships the entire
ledger plus the entire deal state whenever either changes, coalesced behind a
0.25 s floor. There is then no such thing as a missed delta, so the machines
cannot drift into disagreeing about a bond and stay that way. The counter is
*watched* rather than hooked per-mutator because `BuyerMessageDirector` writes
buyer fields directly (`b.convo`, `b.offerPerCap`) — but always alongside a
`Log()`, so bumping there catches everything.

**"First response wins" comes for free.** `Accept`/`Counter`/`Decline` each
refuse to act unless the conversation is still in the right state, so the host
applies replies in arrival order and the second finds it answered.

Known limits, deliberate: **unread is shared**, so one player opening a thread
clears the badge for both; and a genuine simultaneous sale to the same buyer
(both players stood at the same alien, both confirming inside ~250 ms) can
over-fill them — the appetite just takes longer to recover. Neither is worth
per-player state or a round trip mid-UI at this stage.

---

## 9. Risks

- **Snapshot size.** The world delta grows with play. If it exceeds comfortable
  fragmentation, chunk it across frames. Measure before optimising.
- **Host-gating misses.** A ticker left ungated double-ticks or diverges
  silently. Mitigation: audit by grepping `Random.` and `Time.` in singletons,
  and check divergence with a two-instance test rather than by reading.
- **`SaveCollector` apply-order.** The snapshot path must respect the documented
  order. It reuses the existing code specifically so it inherits that ordering
  rather than reimplementing it.
- **Guest disconnect mid-deal.** Host owns the ledger, so a dropped guest leaves
  the world consistent; the deal simply stays open.

## 10. Testing

Each phase ships with a two-instance playtest and, where the logic is invisible
(hit tests, delta ids, snapshot round-trips), an edit-mode harness — the
approach that caught the point-blank-chest-shot miss in the PvP work. Behaviour
that only manifests over a real relay is called out as untested rather than
assumed.

## 11. Explicitly not in scope

Grind transfer between worlds (levels/money on the character), the concert
venue, ship school, Tev's village presence and cabin ambush, world-state for
the backrooms/poolrooms dimensions, and any change to floating origin, n-body
gravity, or the puppet architecture.
