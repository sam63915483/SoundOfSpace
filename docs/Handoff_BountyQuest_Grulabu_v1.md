# Handoff: Floorbin/Shllorbin Quest + GRULABU Bounty Fish (v1)

## STATUS (2026-09-03, later) — Phase C BUILT too: vendor "Ask about the bounty fish" (story, always), "Turn in GRULABU ($500)" row (only while the fish is on the player; exact fish removed via `Hotbar.RemoveFishEntry`, `$bountyReward` paid through PlayerWallet to the interacting player, `grulabu_turned_in` flag, done-state story), and the sell panel REFUSES the bounty fish with a line ([OPEN-3] default). Also: bounty bite chance 0.2 → 0.28 (Sam), Floorbin frantic (fast pacing, hop bursts, one-time 15 m run-up + auto-talk, flag `floorbin_approached`), aliens wade to half body height, player dialogue slide fixed in PlayerController.

## STATUS (2026-09-03) — Phases A + B built to Sam's revised spec; Phase C (vendor story / turn-in / $500) NOT built

Sam's 2026-09-03 changes override §1.3: the kid is NOT despawned/teleported — he
**follows the player home** (real walking, runs to catch up, re-seats if lost),
and the reunion is a beat: within 5 m of the parent they run to each other,
**jump for joy for 10 s**, then the parent's thank-you starts by itself (or on
the next talk) and delivers the sighting ("north, up the lake shore"). No
compass marker, no picture yet (Sam: later).

Built (all on the Sam-placed markers; wired in the Editor via coplay):
- **Reusable authored-NPC kit**, `World/AuthoredNPCSpawner.cs` + `World/AuthoredNPCBody.cs`
  + `NPC_Dialogue/AuthoredNPCTalk.cs`: drop the spawner + a talk on any EMPTY
  under a planet → a named, wandering, waving, talkable alien seated by radial
  raycast (same formula as the streamed spawner; no damageable). Subclass the
  talk for quest logic.
- `NPC_Dialogue/FloorbinTalk.cs`, `NPC_Dialogue/ShllorbinTalk.cs`, `Story/LostKidQuest.cs`
  (follow / reunion / celebration / placement-from-flags).
- `Fishing/BountyZone.cs` on GRULABUSPOT (forces isTrigger + Ignore Raycast at
  Awake — the saved scene had it SOLID on the Body layer); geometric bobber
  check at bite time (20%, one per world).
- GRULABU = a **bounty row** in `FishingRules.Species` (`bounty=true`, rare
  model, 140–260 lb, 2.4 $/lb, stamina 24–34 s, resist 0.78). Never in the
  ordinary roll (`RollSpeciesInTier` skips bounty rows; tested).
- Flags (StoryDirector, world save, no schema change): `floorbin_name_learned`,
  `shllorbin_following`, `shllorbin_returned`, `bounty_spot_known`, `grulabu_caught`.
  The `QuestFlags` block in §3 was NOT built — StoryDirector already IS a
  string-keyed persisted flag set ([EXISTS] the doc missed).

Not built / deviations: Phase C (vendor bounty story, "turn in" row, $500 payout)
— GRULABU currently sells through the normal fish path at ~$480 for a 200 lb
fish, which lands near the bounty anyway; [OPEN-1..5] untouched. Co-op: flags
ride the world snapshot; no live mid-session delta and the authored NPCs'
walking is not synced (single-player demo first). Dialogue is drafted, not
final — Sam's voice pass in the Inspector.

**Goal:** The game's first real quest chain, ending in the first bounty fish. Keep it simple and working end-to-end first; spice-up passes come later. Nothing in this handoff should touch the save schema in a breaking way — additive blocks only.

**Scope for this handoff:** one quest (Floorbin/Shllorbin on Humble Abode), one bounty fish (GRULABU), one bounty-spot trigger, fish vendor bounty dialogue + turn-in for $500.

**State-plan-first rule applies:** before writing code, produce a short state plan (what's stored, where, world vs player scope, how it replicates) and get Sam's GO. Do not implement ahead of it.

---

## 1. Player-facing flow (the spec)

1. Player talks to **Floorbin** (frantic parent NPC, Humble Abode). Floorbin is searching for his lost kid and, through dialogue, tells the player the kid's name: **Shllorbin**. Learning the name is the knowledge gate.
2. Player finds **Shllorbin** elsewhere on Humble Abode. If the player has NOT learned the name from Floorbin, Shllorbin is scared of a stranger and won't come (dialogue reflects this). If the player HAS the name, a dialogue option uses it and Shllorbin agrees to go home.
3. v1 return mechanic: on agreeing, Shllorbin despawns from his lost spot and appears next to Floorbin (no follower AI in v1 — that's a later spice-up). Player returns and talks to Floorbin.
4. Floorbin thanks the player and mentions he saw a **crazy-looking fish** at a specific spot on Humble Abode, pointing the player where to go (dialogue describes the spot; optionally a compass marker — see [OPEN-2]).
5. Separately, at any time: talking to the **fish vendor** has a new dialogue option to ask about a bounty fish. The vendor tells a story about a fish that ate a child, and offers **$500** for anyone who catches it and brings it in.
6. At the sighting spot, Sam will have placed a **sphere collider (isTrigger)** in the water. If the player's bobber lands inside it, each bite roll has a **20% chance** of being the bounty fish **GRULABU** instead of a normal fish.
7. GRULABU visual: reuse the existing rare-fish prefab, scaled up and recolored to read as a big legendary fish. No new model.
8. Once caught, GRULABU is an item in the hotbar. Talking to the fish vendor while holding it (or having it in the hotbar) surfaces a **"Turn in bounty fish"** dialogue option: the item is removed and the player is paid **$500**.

---

## 2. Division of labor

**Sam places (manually, as always):**
- Floorbin NPC GameObject + his "home" spot on Humble Abode
- Shllorbin NPC GameObject at the lost spot, plus an empty transform next to Floorbin for the returned position
- The bounty sphere collider in the water (isTrigger) at the sighting spot

**Claude Code does everything else:** scripts, wiring, dialogue data, item, sync. Do not spawn or reposition the NPCs procedurally — wire to what Sam placed. Confirm placement exists before wiring.

---

## 3. Build items

### Phase A — Quest state + Floorbin/Shllorbin

- **[EXISTS]** Deterministic branching dialogue framework (StoryDirector singleton, data-driven authored nodes; Tev/buyers already use dialogue choice rows). Verify what the current cleanest pattern is for a simple talk-NPC with choice rows and reuse it — do NOT build a new dialogue system.
- **[EXISTS]** The knowledge-gate design: "you can only bring the kid back if you've learned the name from Floorbin first, otherwise the kid is scared of a stranger." This is canon; implement exactly this.
- **[BUILD]** `QuestFlags` — a minimal world-save block (additive) holding named booleans. For this handoff: `floorbin_name_learned`, `shllorbin_returned`, `bounty_spot_known`, `grulabu_caught`, `grulabu_turned_in`. World-scoped (shared in co-op — same pattern as shelf/plugins). No generic quest framework, no quest log UI, no state machine classes — flags and dialogue conditions only. Design it so more flags can be added later without schema churn (a string-keyed set is fine).
- **[BUILD]** Floorbin interaction: talkable NPC, dialogue tree with (a) intro/search state, (b) the beat where the player learns the name → sets `floorbin_name_learned`, (c) post-return thank-you beat → sets `bounty_spot_known` and delivers the sighting description, (d) idle post-quest state that can re-state the sighting spot if asked again.
- **[BUILD]** Shllorbin interaction: (a) if `floorbin_name_learned` is false → scared-of-stranger dialogue, nothing happens; (b) if true → name option appears, choosing it sets `shllorbin_returned`, despawns him at the lost spot and enables him at the returned position next to Floorbin. Position swap = toggle two Sam-placed transforms/instances, not a runtime Instantiate.
- **[AUTHOR]** All dialogue lines (Floorbin frantic-parent voice, Shllorbin kid voice, vendor's ate-a-child story). Draft them, keep them short and in the game's voice, and flag them for Sam to punch up — don't ship stiff placeholder text as final.

### Phase B — Bounty spot + GRULABU

- **[EXISTS]** Fishing. **First step: check the repo for whether `Handoff_FishingRevamp_Phase1_v1.md` (Sep 1) has been implemented** (tension-bar fight, sun-angle bite rate, vendor bait, 12 species). Report which world you're in before wiring:
  - If Phase 1 IS in: hook the bounty roll into the bite table — when the bobber is inside a bounty zone, each bite roll is 20% GRULABU, else the normal table. GRULABU gets the hardest fight parameters that exist.
  - If Phase 1 is NOT in (legacy click-to-cast / click-to-catch): hook the 20% roll into wherever the bite is currently decided. Do not build any part of Phase 1 under this handoff.
- **[BUILD]** `BountyZone` component for Sam's sphere collider: isTrigger detection of the bobber. Bobber-side check is fine if the trigger callback is unreliable (see traps: the bobber needs a Rigidbody for OnTrigger to fire at all — verify which side has one). Zone carries the bounty species id (`grulabu`) so future bounty fish reuse the component.
- **[BUILD]** GRULABU catch result: reuse the rare-fish prefab, scale up, recolor via material property/tint — do NOT duplicate the prefab if a variant/tint path exists. Sets `grulabu_caught`, puts a new item in the hotbar.
- **[BUILD]** New item: `BountyFish_Grulabu` (or matching existing fish-item naming). Must round-trip through save/load and survive the hotbar/storage/drag paths. Check how fish items currently carry identity (the CarriesVariant/VariantOf generalization exists for tapes/mushrooms) and follow whatever fish already do. It should NOT be sellable through the normal fish-sale path at normal fish prices — turn-in is the only exit (or see [OPEN-3]).
- **[BUILD]** Post-catch behavior (default, see [OPEN-1]): once `grulabu_caught` is set, the zone stops rolling bounty bites. One GRULABU per world.

### Phase C — Fish vendor: bounty story + turn-in

- **[EXISTS]** Fish vendor NPC (fish market vendor). Reuse its existing interaction pattern.
- **[BUILD]** Dialogue option "Ask about the bounty fish" — always available; plays the story ([AUTHOR]) and states the $500 reward. Available before, during, and after the Floorbin quest (the quest gives you the WHERE; the vendor gives you the WHY and the reward — neither gates the other).
- **[BUILD]** Dialogue option "Turn in bounty fish" — only visible when the GRULABU item is in the player's hotbar/inventory. Choosing it removes the item, pays **$500** to that player, sets `grulabu_turned_in`, and swaps the vendor's bounty dialogue to a done state.
- **[INTEGRATE]** Money: $500 goes through the same money path as sales — money is PERSONAL in co-op, paid to whoever turns it in. Use the existing money mutation path, not a new one.

---

## 4. Co-op (decide in the state plan, defaults below)

- Quest flags are **world-scoped and shared**: if one player learns the name, the crew knows it; if one player returns the kid, it's returned. This matches the shared shelf/plugins pattern and is v1-simple. They ride the world save (join snapshot covers late joiners); mid-session flag changes need a live delta — reuse the existing named-message + snapshot pattern, and remember: never SendNamedMessageToAll (NGO loops it back to the host — this caused the rebroadcast storm).
- Bounty is **one per world**: first player to catch GRULABU gets it; the $500 goes to whoever turns it in (probably the same player, but the item is a normal inventory item so it can be handed off if drop/trade exists).
- The bite roll happens on whichever machine is simulating that player's fishing (fishing is per-player/local). The zone check is local; the resulting `grulabu_caught` flag change is the thing that must replicate.
- **Known repeated trap, check every call site: "nearest player" silently means "only player" in co-op.** This has now bitten four times. Anywhere the quest or vendor code finds "the player" (dialogue focus, who gets paid, whose hotbar is checked), it must be the interacting player, not Player[0] / nearest.

## 5. Traps to respect (all previously recorded in this repo)

- `isInDialogue` soft-lock class of bug — new dialogue states must release cleanly (ESC/exit paths), especially the turn-in flow that also mutates inventory.
- **InteractGaze ignores trigger colliders** — NPC interaction colliders must be non-trigger; the bounty sphere is the opposite (isTrigger, physics only, never gaze-interactable).
- **Instantiate with world transforms under floating origin** — if anything does get instantiated (fish catch visual, item), parent it correctly; spawn clipping was root-caused as physics-vs-render parent frame (`ParentToBodyPhysicsFrame`).
- Scale chains: the rare-fish prefab scale-up must account for parent scale chains (the console chain was ~0.03 — check the fish prefab's chain before picking a multiplier, and eyeball in editor).
- Save schema is the network schema — the QuestFlags block and the new item must be designed once, additively, and never hold references that orphan (string keys, not indices).
- Tutorial/vault interactions: none expected, but grep for anything gating on fishing or these NPCs before wiring (the ChopWoodStep lesson).

## 6. Tests

- **[TEST]** QuestFlags: set/get/save/load round-trip, unknown flag defaults false, world-save additive (old saves load with no flags set and nothing breaks).
- **[TEST]** Turn-in: item present → option visible, item removed exactly once, $500 paid exactly once, double-trigger safe (spam-click the row).
- **[TEST]** Bounty roll: inside zone = 20% ± tolerance over N rolls, outside zone = 0%, post-catch = 0%.
- Suites run headless on Unity's Roslyn like the existing taste/rent/port suites — keep these small and in that harness.

## 7. Open questions for Sam (answer before or during the state plan)

- **[OPEN-1]** After turn-in, is GRULABU gone forever (default: yes, one per world), or does the zone re-arm later as a repeatable rare catch at a lower price?
- **[OPEN-2]** Does Floorbin's sighting reveal place a compass marker / phone marker (the buyer-appointment marker pattern exists and could be reused), or is dialogue description only for v1? Default: dialogue only, marker as spice-up.
- **[OPEN-3]** Should the normal fish-sale path refuse GRULABU with a vendor line ("that's the bounty fish — turn it in properly!") or just hide it from the sale list? Default: refuse with a line, it's funnier and safer.
- **[OPEN-4]** $500 sanity check against the revamped economy (tapes ~$13–32, plugins $60–180, ship $1000): $500 is half a ship for one quest chain. Fine if the bounty is meant to feel huge and is one-time; worth a look if it should be $150–250 instead. Sam's call — build at $500, make it one tunable constant.
- **[OPEN-5]** Cross-loop persistence of `bounty_spot_known` (the "knowledge survives the reset" idea) is NOT in this handoff — there's no meta-save store yet and the loop reset itself isn't built. Flags live in the world save for now. Flag anywhere this assumption is baked in with a comment so the future meta-save pass can find it.

## 8. Definition of done

- Fresh save: full chain playable — Floorbin → name → Shllorbin → return → sighting → vendor story → cast in zone → catch GRULABU → turn in → $500, all without console errors.
- Old save (pre-handoff): loads clean, quest starts normally.
- Co-op smoke: host learns name, guest returns kid, guest catches fish, host watches turn-in — flags consistent on both screens, money only moves for the turn-in player.
- All dialogue drafted and flagged for Sam's voice pass.
- STATUS block at the top of the doc updated honestly on completion, including any [EXISTS] claims that turned out false (per the Aug 17 precedent).
