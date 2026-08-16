# Handoff — Rent Revamp + Physical Cassette Printing (v1)

**Date:** 2026-08-14
**Follows:** the Aug 13–14 cassette loop build (Plan_CassetteLoop_Build_v1.md, Phases 1–5) and the money revamp (Plan_MoneyRevamp_v1.md).

---

## STATUS — BUILT 2026-08-14, PLAY-TEST PENDING

Parts A and B are implemented. Both assemblies compile; `verify-port` (2024
checks, goldens byte-identical), `verify-library` (97), `verify-taste` (113) and
the new `verify-rent` (101) all pass.

**Sam's two decisions on the OPEN items, taken before the build:**

1. **Rent is paid BY HAND, never auto-deducted.** The balance grows every game
   day and only moves when the player opens `TevPaymentUI` and hands money over.
   That is what gives the 5-day lockout teeth — you can be rich and locked out
   for ignoring your landlord, which an auto-deducting collector could not say.
2. **The old "did you sell any of my tapes?" return talk is dead.** First talk =
   lawn → haggle → gift line → 3 blanks. Every talk after = nag (if owed) →
   shop. The lines are vaulted, not deleted.

Doc defaults taken as-written: the held item must be the **selected** hotbar
slot; F on an occupied slot ejects an unprinted blank; arrears do not touch bond.

### What Sam has to do

1. Add **`CassetteSlot`** to the cassette-insert mesh on the console stand.
   That is the whole setup — one component handles insert, print and eject.
2. Tune `approachOffset` so the tape flies in along the axis pointing OUT of
   the insert, and `tapeEuler` so it lines up with the mouth. Both are live in
   Play mode: drag them while a tape is in the slot and it moves as you drag.
3. **Ctrl+S.** Play mode uses the Inspector's in-memory value; a BUILD reads the
   scene file. An unsaved tweak works in the Editor and is wrong in the build,
   which is exactly what happened once already.
4. Fresh save, play.

### The eject is the same slot (2026-08-14, Sam's call after playtest)

Originally the printed tape moved to a SEPARATE object that you then looked at
and picked up. Sam's revision, which is better and is what ships:

> press print → the computer closes → the cassette slides back OUT of the slot
> it went into → it lands in your hotbar

So there is no second object to place and no manual pickup step. `ShuttleComputerUI`
closes BEFORE it touches the deck — that ordering is the feature, since the
player has to be looking at the machine to see the tape emerge. `PrintedTapePickup`
was deleted; `CassetteSlot` owns both directions.

The tape is only added to the hotbar AFTER the slide finishes — the movement is
the receipt. If the hotbar is full it stays sticking out of the mouth and F takes
it once room is made. It is never destroyed.

### ⚠️ The editor-tool version of this was REPLACED (2026-08-14, second pass)

The first build generated the slot and eject into the shuttle prefab from an
editor tool. **It did not work in play**, and the reason is worth keeping:

> The generated objects were invisible, carried only a **trigger** collider, and
> had `gazeTarget` pointed at `ConsoleScreen`. `InteractGaze` is a crosshair
> SphereCast that **ignores triggers** and requires a hit on geometry belonging
> to the aim target — so aiming at the slot on the stand meant aiming at the
> *stand*, and the prompt never appeared.

Two rules came out of it, enforced in code rather than documented:
`CassetteSlot`/`PrintedTapePickup` force `gazeTarget = null` in `Awake` (so the
gaze test falls through to the object's own mesh silhouette), and the spawned
cassette is **stripped of every collider** so it can't eat the crosshair cast to
the slot behind it.

The prefab was reverted (the diff was 550 pure insertions, nothing of Sam's
touched). `Tools ▸ TRAX ▸ Remove Generated Cassette Slot Objects` remains as a
sweep-up; **there is deliberately no "add" menu item any more** — placement is
Sam's, not a script's.

### Known gaps

- The tape model is Sam's existing `Assets/1 - samsPrefabs/CasettePickup.prefab`,
  instantiated and stripped to pure geometry by `CassetteVisual` (it ships with
  `GravityObjectSimple`, `CassettePickup`, `PickupHoldOffset`, a Rigidbody and
  two colliders — all destroyed at spawn, or the tape would fall through the
  shuttle and offer its own pickup prompt).
- **If no `PrintedTapePickup` exists in the world, printing falls back to putting
  the tape straight in the hotbar** rather than blocking. Deliberate, so the loop
  is never gated on level dressing being positioned.
- **Co-op replication of the slot/eject is NOT implemented.** State is world-
  scoped and rides the world save (so the host's machine is what persists), but
  a guest will not see the host's tape appear in real time. The handoff calls
  host-authoritative "fine for v1"; this is the piece of that still outstanding.
- Tev's new lines are **drafts in Tev's voice** — they ship from C# because the
  fields are new, so editing them in the Inspector wins from then on.
**Process (non-negotiable):** State your full build plan FIRST and wait for Sam's correction before implementing (GDD_StoryBible_v2.md §0 rule 4). Report Tev's CURRENT conversation flow back to Sam before touching any dialogue — he cuts/changes lines first. Report the names of any objects Sam needs to place or reposition.

---

## Why this revamp

Selling Tev's fronted tapes is cut. Three reasons, all real:

1. **The customers were his, not yours.** BuyerLedger bond, threads, and "songs heard" all accrue from what a contact bought. Under fronting, every early customer's history is Tev's demos — so their requests ask for HIS genres. The player's early career was being steered toward someone else's sound.
2. **50/50 made no sense for tapes.** Fronting + the skim was mushroom logic — caps are fungible, songs are not. Why would he give a stranger half?
3. **The loop is the game.** The player should experience the true loop (make your own music, sell it, get rejected) from minute five, not run Tev's errands first.

Tev becomes exactly two things: your **landlord** (you're parked on his lawn) and your **music store** (blanks + plugins). The intro squares the relationship away fast and drops the player straight into: **buy tapes → record music → sell music → make money → repeat.**

---

## Part A — Tev: landlord + store owner

### [EXISTS] — do not rebuild

- The **money-rent system** from Aug 8: silenced in Phase 3 via `SettleRent(0)` + an early return; the **arrears system is dormant, not deleted**; no-eviction stands. This is a reactivation + retune, not a new system.
- **TevShopUI** (Aug 14, layout F, two tabs: BLANK TAPES / PLUGINS FOR SALE) — becomes MORE central, unchanged structurally.
- **Slot-8 physical money + drag payment UI** — this is now how rent gets paid.
- **TevFronting.cs**, the tape-count work-off haggle (10→8→5→3), and Tev's 3 demo prints (SLUDJ/CHIRP/DRIFT) — all being vaulted below.
- Tev appears at his cabin ~2 min after the shuttle door opens (existing beat).
- MP economy is per-wallet (P5: money personal, world shared). NOTE — Sam's Aug 14 decision: **rent is HOUSEHOLD-SHARED in co-op**, superseding the old "Tev treats players separately" rule (that rule was for fronting debts, which are cut). One lawn, one rent ledger: either player can pay it from their own money, and the 5-day plugin lockout hits **both** players. Rent state is world-scoped.

### [BUILD]

**A1. Vault the fronting economy.** `FeatureVault` flags per the VAULTED_SYSTEMS.md pattern — flags, not deletions, restore table updated:
- TevFronting (the 50/50 repeatable front + skim quote path).
- The tape-count work-off rent haggle from Phase 3.
- Tev's 3 demo prints. (Optional flavor later: they play in his store. Not now.)

**A2. Reactivate daily rent.**
- Rent is **per game day** (GalaxyTime: 24 in-game h = 24 real min — so rent ticks every 24 real minutes; constants must assume that cadence).
- The rate is set ONCE by the intro haggle chain: **$50 → $30 → $20 → $10 per day.** Four beats, each a real refusal/counter; the $10 floor is final — **never free** (rent is the loop's money pressure; haggling it away deletes the pressure).
- **Arrears stack linearly:** owed = agreed rate × unpaid days. $10/day × 3 missed days = $30. **NO compounding, NO interest.** Sam was explicit.

**A3. Consequences at 5 unpaid days.**
- Tev **stops selling plugins** until the balance is zero. **Blanks are ALWAYS purchasable** — the loop must never be able to soft-lock. The ladder freezes; the treadmill doesn't.
- Every interaction with Tev opens with a pay-me-back line until square. Escalate the tone with the size of the debt.
- Paying to zero (via the existing drag-payment UI; underpay reduces the balance as it already does) restores plugin sales immediately. [OPEN → default: arrears do NOT touch bond; the lockout + nagging are the whole penalty.]

**A4. New intro beats** (report current flow to Sam first, then rewrite):
1. Parked-on-my-lawn opener (existing tone).
2. Rent demand → the $50→30→20→10 haggle. This is deliberately the player's FIRST negotiation — it tutorializes the push-your-luck instinct every alien sale uses.
3. Player is broke. Tev's gift line, **verbatim, locked:**
   > "Big dreams, empty pockets. Seen it a hundred times. Here's three blanks to get you started — the rest you're buying."
4. **3× Blank Tape I** granted to the hotbar. Store available from here on.
- Rent starts accruing from the confrontation — the blanks are genuinely free; the debt IS the rent.

### [AUTHOR]
- The four haggle beats and the escalating nag lines, in Tev's established voice (dry, crude, transactional). Draft them, Sam edits.

### [TEST]
- Headless where the pattern allows: arrears math (3 × $10 = $30), lockout triggers at exactly 5 days and not 4, blanks purchasable while locked, plugins refused while locked, full payment clears lockout + nag, partial payment reduces balance without clearing lockout unless it hits zero, haggle chain reaches all four landings, household rent in co-op (one shared ledger: either player's payment reduces it, lockout applies to both), rent state rides the world save with relative times and clears on New Game.

---

## Part B — Physical cassette printing

The goal is hands-on feel: printing stops being a menu action and becomes a ritual you perform on the machine.

### [EXISTS]
- ShuttleComputerTerminal: look at ConsoleScreen + press F opens the computer.
- TRAX print flow: PRINT dialog with a quantity, count limited by blank tapes in the hotbar, player names the tape, TraxPrints append-only, print id derived from identity+tier so stacking falls out.
- Look-at + press-F world pickup pattern (the fishing rod in Tev's cabin).
- The shuttle prefab editor patch tool (LoadPrefabContents, no baked world placement).
- Dev blanks on T / Shift+T.

### [BUILD]

**B1. The cassette slot.**
- A physical slot on/next to the computer. With a blank cassette as the held hotbar item, looking at the slot prompts **"Insert blank cassette [F]"**. Pressing F consumes one blank from the stack and visibly seats a cassette in the slot.
- **One at a time.** Inserting while occupied is refused with a status line. [OPEN → default: F on an occupied slot with an UNPRINTED blank ejects it back to the hotbar, so a mis-insert isn't a trap.]
- The inserted blank's tier (I / II) IS the print tier. The in-app tier choice goes away.
- [OPEN → default: "held item must be the blank" (selected hotbar slot), since Sam wants to SEE the cassette go in. If he meant merely "anywhere in hotbar," it's a one-line change — ask in the build plan.]

**B2. Print gating.**
- PRINT with no cassette inserted → **"PLEASE INSERT CASSETTE."** Nothing else happens.
- PRINT with a cassette seated → **"READY TO PRINT"** → player confirms → exactly **one** tape prints. The quantity dialog and hotbar-blank counting for prints are removed — the slot is the gate now.
- Naming stays where it is in the current flow (the tape gets its name at print).

**B3. The eject.**
- On the confirmed print: the computer UI **fully closes**, and the printed tape moves out of the slot to an eject position on the machine — a world item.
- Look-at + press-F picks it up into the hotbar (existing pickup pattern; stacks by print identity as today).

**B4. Persistence + multiplayer.**
- An inserted-but-unprinted blank persists in the slot across save/load; an ejected, unclaimed tape persists as a world item. Both ride SaveCollector; both clear on New Game.
- Co-op: the computer is shared, so slot state + the ejected tape are world-scoped. Host-authoritative is fine v1 (the locker's one-open-lock pattern is the precedent). The ejected tape belongs to whoever grabs it.
- Walkman, shelf, plugins, goldens: untouched. The engine doesn't change — if any golden vector moves, something is wrong.

**B5. Assets + placement.**
- Needs a real cassette model eventually. Until Sam sources one, a simple placeholder mesh (box with the existing cassette sprite as its face texture) is fine — flag it visibly as placeholder.
- Sam places GameObjects. Build the slot + eject anchor, patch the prefab with the existing tool, then **report the object names** so Sam can reposition the slot and eject point exactly where he wants them.

### [TEST]
- Insert/refuse/eject matrix (empty, occupied, wrong held item, no blanks).
- Both gating strings appear in the right states; a print consumes exactly one blank and produces exactly one tape of the right tier, name, and identity.
- UI closes on confirmed print; the ejected tape is grabbable and stacks correctly.
- Save round-trip with a cassette seated, and with a tape ejected but unclaimed.
- Dev blanks (T/Shift+T) still work; goldens byte-identical; both assemblies compile; the standard suites (port checks, library, taste, engine) all pass.

---

## Sequencing, and explicitly OUT of scope

- **The Phase 6 vault pass stays LAST** (building system, Grow Pot/Dome, level systems) — after this handoff's loop is proven in play, per Plan_CassetteLoop_Build_v1.md.
- **Cassette duplicator: NOT in this handoff.** (Future Tev purchase, much later: load up to 10 blanks + 1 music tape, copies the music onto the blanks. Recorded so nobody re-designs it from scratch — do not build any of it now.)
- Orientation whiteboard, hunger/thirst: untouched, as before.
- Fresh-save context: the money revamp already requires a fresh save; the rent constants join it. No conversion.

**Definition of done:** fresh save → land → Tev confronts → haggle rent to a daily rate → broke → the gift line → 3 blanks in hand → insert one → make a track → READY TO PRINT → confirm → computer closes → grab the tape off the machine → sell it → pay Tev → buy blanks → repeat. Skip rent for 5 days and the plugin tab refuses you while the blanks tab doesn't.
