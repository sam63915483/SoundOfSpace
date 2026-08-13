# Build Plan — Cassette Loop Revamp

**Source:** `Handoff_CassetteLoop_Revamp_v1.md` · **Branch:** `feat/helmet-hud` (stay on it)
**Date:** Aug 13, 2026

Sam's decisions folded in: mushrooms vault as a *commodity* only (finding, cutting,
replanting and reharvesting all stay); plugins + demo library are **world-scoped, on
the computer** — one player buys, both use; alien taste derives from their id hash so
it never needs saving; **the vault pass runs LAST**, not first.

Phase order is deliberate: nothing destructive happens until the loop is proven.

---

## Phase 1 — A track becomes a saveable thing

**Build**
- Add the **active module set** to the track struct (which of the 6 are on), JS + C#.
  Mute currently lives in the UI only; a tape has to record what was actually playing.
- **Check, don't assume, the golden implication.** Voices seed from their own
  preset+variation on constant-keyed streams and every voice draws a fixed count per
  step — so muting should change the *mix*, not the *patterns*. Expect the golden
  vectors to come out byte-identical and only `TrackId` to change. If they do move,
  stop and flag it before touching anything else.
- **Demo library** on the computer: SAVE current track under a name, list, LOAD back
  into TRAX, DELETE. World-scoped — both players see the same shelf.
- **Plugin gating**: the computer owns the set of installed plugins. Start = THUMPER +
  GLOWORM. The other four render locked in the rack — visible, not usable.
- **Decouple the audio engine from the terminal UI** so a track can play from
  something that isn't the computer screen. Phase 4 needs an alien to play a tape at
  you out in the world; doing this now is nearly free, doing it later is not.

**Sam does:** nothing yet.

**Done when:** save a demo → quit → reload → it loads and sounds identical. Locked
modules can't be switched on. `npm run golden` + `verify:port` both clean.

---

## Phase 2 — Blanks, printing, named cassettes

**Build**
- Two items: **Blank Cassette T1 ($10)** and **T2 ($20)**. Stackable, storable,
  holdable like any other item.
- **PRINT flow** (replaces the stub): pick a saved demo → the computer counts blanks
  **in your hotbar only** (locker blanks don't count) → choose how many, per type →
  consumes blanks, produces printed tapes.
- **Tape identity + storage.** Mirror the mushroom pattern exactly: the hotbar slot
  carries a `trackId` string the way it carries `mushroomSpecies` today, and a
  world-scoped `TrackLibrary` holds the data. That gives us stack purity, save, MP
  replication and the species-likeness icon/held-model for free.
  **Printing copies the track into an immutable printed-track record** — separate from
  the editable demo shelf — so deleting a demo can never orphan tapes already made.
- Held tape shows the **demo name**, not "cassette". T1 and T2 visually distinct.
- MP: tapes are ordinary transferable items; either player can print from the shared
  shelf.

**Sam does:** art-directs the tape models/icons once they exist.

**Done when:** print 3 of 5 blanks → 3 tapes + 2 blanks. Names show held and in slots.
Tapes survive save/load and hand-off to the other player. Delete the demo → the
printed tapes still play.

---

## Phase 3 — Tev's music store

Same Tev, same beats, new skin. **His current dialogue flow gets reported to Sam
before a single line changes** (`docs/tev-dialogue-current-flow.md` is the starting
point).

**Build**
- **Intro**: the existing rent/onboarding haggle tree, reworded — he fronts you HIS
  demo tapes, 50/50 split, and that teaches selling. Existing 10 → 8 → 5 → 3 step-down
  structure survives untouched. Finish it, you're buddies.
- **Shop** (existing shop pattern): Blank T1 $10 · Blank T2 $20 · SIREN $200 ·
  MOSS $200 · SPINDLE $200 · CAVE $200. Plugins install to the computer — bought once,
  both players get them, wallet charged is the buyer's.
- **Repeatable fronting** as the side job: Tev fronts a batch of his tapes, you sell
  them through the Phase 4 interaction, settle up on the existing slot-8 drag/keep-give
  payment UI. His cut = 50% of rough market value, stated up front. Out-negotiate the
  market and you quietly pocket the difference. Underpay/exact/overpay behaviours carry
  over unchanged.
- Draft all reworded lines for Sam's cut pass **before** wiring them.

**Sam does:** cuts/rewrites the dialogue draft. Places the store if it needs a spot.

**Done when:** intro completes, purchases hit the right wallet, a bought plugin is
usable in TRAX immediately — on both machines.

---

## Phase 4 — Aliens: taste, the offer, feedback

The big one. Budget accordingly.

**Build — the taste model**
Every alien gets a `tastePoint` (a spot in the same 6-D dial space the classifier
uses), a `falloff` (picky vs. broad) and a `payFactor` (picky pays premium). All three
**derived by hashing their id** — permanent, identical on both machines, zero save
schema.

`satisfaction = clamp(100 − k · falloff · distance(track.dials, tastePoint))`

Likes, prices, feedback and requests all come out of that one number. No per-alien
writing.

**Build — the offer interaction** (one interaction for every in-person sale: your
tapes, Tev's tapes, text-order handoffs)
- Offer held tape → **the alien actually listens**, a few seconds of the real track →
  like gate: ≥50 liked · 35–50 coin flip · <35 not liked.
- **Not liked:** no sale, no number. Feedback given, tape handed back — try it on
  someone else.
- **Liked:** "how much?" → you name a price → accept / counter / decline. Their
  ceiling scales with satisfaction, so a track they love supports a higher ask. Deal →
  paid, tape gone, you get their number.
- **Greed:** push too far and instead of the deal collapsing they issue a **final
  offer** — deliberately below your ask *and* below what they'd have paid. Swindle
  attempt, no top dollar. Take it and the sale completes; refuse and you keep the tape.
  Either way you get the number (they liked the song), with a bond ding for refusing.
- **Repeat rule:** offering the same song to the same alien twice = refusal + bond
  hit. Matched on *closeness in dial space*, not exact hash — otherwise nudging one
  variation resets the whole thing and you can farm one alien forever.
- Stinginess affects **money only**, never whether they like the music. No free demos,
  no tips.

**Build — feedback**
Computed from the difference vector: name the 1–2 biggest gaps as dial advice ("too
much CRUNCH, needs more GOO"), plus their nearest genre ("I'm more of a GLORP guy").
Early rejections lead with the **dial**, not the genre — the dial is actionable at the
console before you understand where the genre centres are. Templates drafted for Sam.

**Note on code reuse:** `BuyerDeals.cs` is mushroom-typed all the way down
(`MushroomTier`, `MushroomRegistry`, `NPCMushroomPrice`). Tapes get a **parallel
`TapeDeals.cs` with the same shapes** rather than tape branches threaded through
mushroom code. Same for the sell UI — `MushroomSellUI` is 1354 lines and its flow runs
the other way round (NPC prices your goods); the tape UI is built from its skeleton,
not reskinned from it.

**Sam does:** tunes the like-gate bands and `k` once he can feel them.

**Done when:** forced taste points sweep every satisfaction band correctly; contacts
get acquired; a near-identical re-offer gets refused.

---

## Phase 5 — Contacts, texts, orders

- Number acquired → they join phone contacts (existing buyer-messaging system).
- **"Music hungry" texts**: frequency scales with bond and how much they like your
  catalogue. The request is a genre sampled from their affinity, optionally with a dial
  qualifier — "a glorpy WARPED song".
- Text orders use the **existing mushroom order flow unchanged**: quote over text →
  accept/counter/decline → meet in person → honour the price or push it. The handoff
  itself is the Phase 4 interaction.
- **Pricing** (every constant Sam's to tune):
  `value = (10 + 8·activeModules) × tapeMult(T1 1.0, T2 1.5) × (0.4 + 0.9·sat/100)
   × bondMult(1.0–1.4) × requestBonus(1.25 on a match) × payFactor`
  Heads-up: with 2 modules and a bad match this floors around $10 — the same price as
  the blank. Early tapes can print at a loss until plugins land. Tension, or a knob to
  turn — Sam's call once he feels it.
- Fulfilment checked with `TraxClassifier` on the tape's stored track: the label the
  computer showed is the label the alien hears.

**Done when:** text arrives → make it → print → negotiate → paid. Wrong genre pays no
bonus. Identical tape re-offer refused.

---

## Phase 6 — The vault pass (LAST)

Only once the loop above works end to end. Everything here uses the `_Vaulted` pattern
— flags, not deletions.

- **Vault the freeform building system** (placement UI, buildables, unlocks). Keep tree
  chopping, saplings, fishing, bonfire cooking. Sam places a bonfire near the shuttle.
- **Vault the Grow Pot and Bubble Dome with it** — both are injected build-menu entries
  (`GrowPotRegistrar`, `DomeBuildRegistrar`), so they go when the menu goes. Flags stay
  so they come back cleanly.
- **Vault all level systems** — general level, Gangsta Rep, Tree Daddy, every
  sub-category, their UI, and level-gated unlocks. ~72 references across 18 files;
  nothing in the new loop may reference levels.
- **Vault mushrooms as a commodity only.** Aliens stop buying them and stop texting
  about them. Finding, cutting, replanting and reharvesting all stay working. One flag
  flips the whole trade back on later.
- Orientation whiteboard, hunger and thirst: untouched.

**Done when:** the game boots and plays with all of it off, no dangling references, MP
join still works, and nothing in the cassette loop references a level or a buildable.

---

## Definition of done (whole thing)

Land → Tev intro → sell his tapes 50/50 → buy blanks → save a named demo → print →
offer it, alien likes it, asks how much → negotiate → paid + number → genre request
text arrives → make it → negotiate → paid → afford a $200 plugin → a richer track
visibly sells for more → Tev fronting still works as a side job throughout.
**Solo and two-player co-op.** No level or building references anywhere.

---

## Deferred

- **Key as a taste factor.** Right now dials decide genre and genre decides who wants
  it; plugins are a flat value buff; key is cosmetic. Sam's idea, worth doing later:
  aliens with a preferred key, so two identical tracks in different keys land
  differently. Parked, not dropped.
- Radio milestone measurement + reward flow.
- Late-game money sinks (preset/variation packs).
- Whiteboard repurpose (Sam, after the loop works).
- Co-op shared-cursor live editing on the computer — its own phase.
- Full-length track recording (RECORD + dial automation). Already protected for at zero
  cost in the engine; not built.
