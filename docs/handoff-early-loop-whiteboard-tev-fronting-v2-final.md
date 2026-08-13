# Handoff — Early loop: Orientation whiteboard + Tev fronting (v2 FINAL, 2026-08-10)

All open questions are resolved — this version is build-ready. The early game gets its shape: the player wakes, the stasis door opens, a whiteboard of optional orientation objectives teaches the survival systems without forcing anything, and Tev evolves from the one-off onboarding deal into a repeatable per-player 50/50 fronting loop — the player's first income source and first hustle. There is one **hard stop point** (Part 3): do not touch Tev's dialogue until you've reported the current conversation flow back to me and I've edited it.

Per the plan-first rule (CLAUDE.md / GDD §0 rule 4): state your build plan for each part before implementing. Tags: `[EXISTS]` already built (verify, don't assume), `[BUILD]` new work, `[AUTHOR]` draft copy for my approval, `[INTEGRATE]` wire into existing systems, `[TEST]` verification.

House rules from the multiplayer work still apply: the host owns every timer and every dice roll; never `SendNamedMessageToAll`; planet-local coordinates on the wire; the save schema is the network schema; `PlayerWallet` stays out of the sync layer (harness-asserted); preserve `"_Placed"` naming. Exception to the usual placement rule, granted for this build only: you place the whiteboard yourself (see Part 2).

---

## Part 1 — Money as an item: hotbar slot 8

Prerequisite for the payment UI, and a feature in its own right.

`[BUILD]` A dedicated 8th hotbar slot that holds money and only money:
- The slot **is** the wallet — single source of truth. Stack count = balance, uncapped (the 20-stack rule doesn't apply to slot 8). Deals pay into it; shop/build purchases decrement it. If `PlayerWallet` currently holds the balance, refactor so the slot is canonical and anything reading the wallet reads the slot (or the wallet becomes a thin view over it) — never two representations of money; that's how dupes are born.
- Money is a real item: floating held model consistent with the physics-item direction, hotbar icon (a cash stack), draggable into the locker like anything else — co-op sharing rides the existing StorageSync lock model with zero new netcode.
- Splitting a money stack uses the existing split UI so partial deposits and withdrawals work.
- Slot 8 cannot hold other items; other slots cannot hold money.
- Dying with cash: keep it for now; leave the hook for a later risk rule.

`[INTEGRATE]` Every current money mutation point (buyer deal payouts, any shop/unlock spend) routed through the slot. `[TEST]` earn → count rises; spend → falls; drag half into the locker; second player withdraws it; balance survives save/load.

## Part 2 — Orientation whiteboard

`[EXISTS]` (verify each in your plan): locker stocked with axe + water bottle; thirst/drink; water-bottle filling; fishing; a bonfire already placed in the world (players can also place their own — the objective completes with either); hunger/eating; tree chopping; sapling planting; stasis-pod save button; the fishing rod as a look-at + press-button pickup located in **Tev's cabin**. All seven objectives are wiring onto existing systems — if any system turns out not to exist, stop and tell me rather than building it.

`[BUILD]` A physical whiteboard inside the shuttle. **You create it AND place it in the shuttle interior yourself** — pick a sensible wall the player faces early. When it's done, tell me it's placed and give me the exact GameObject name/path so I can move it and get the position perfect. Header: **ORIENTATION OBJECTIVES**. Seven lines, each getting a strike-through drawn across it when completed. No order enforcement, no rewards, no popups, no tracking UI anywhere else — the board is the whole feature.

**Per-player completion.** The objective list is identical for everyone, but progress is tracked separately per player: the board renders the *viewing* player's own strikethroughs, so the host and a guest looking at the same board each see their own progress, and a brand-new player who joins a finished world still sees all seven uncrossed. Persist each player's completion with their character/per-player save state so it survives save/load and rejoin; propose where it lives in your plan.

Objectives (final player-facing phrasing is `[AUTHOR]` — draft it, I approve):
1. Take the axe and water bottle from the locker (completes when both are taken)
2. Fill the water bottle and drink it (completes on drinking a filled bottle)
3. Retrieve the fishing rod and catch a fish (rod is in Tev's cabin)
4. Cook a fish on a bonfire and eat it (completes on eating the cooked fish; any bonfire counts)
5. Chop down a tree
6. Plant a sapling
7. Save by pressing the button and entering the stasis pod

`[INTEGRATE]` Hook each line to its existing event (locker withdrawal, drink, catch, consume-cooked-fish, tree death, sapling placement, stasis upload). Completion events are local player actions — follow the established report/apply pattern where sync is needed, but since state is per-player, most of it only ever renders locally.

`[TEST]` Each event crosses exactly its own line; no objective can un-cross; state survives save/load; in co-op, a fresh guest sees an uncrossed board while the host's view stays crossed.

## Part 3 — STOP: report Tev's current dialogue first

Before changing a single line: extract Tev's entire current conversation flow — every node, line, option, greyed-condition, and state gate from the Aug 4 onboarding (lawn opener → drug segue → the free 3-cap first deal → return conditionals → the oxygen/trees teaching) — and present it to me as a readable tree. I will remove and rewrite lines. **Do not proceed to Part 4 until I hand the edited flow back.**

## Part 4 — Tev the contact, and the fronting loop (after my dialogue edits)

**Everything Tev is per-player.** Each player has their own bond, own front, own debt with him, and both players can carry active fronts simultaneously. The existing buyer ledger is *shared* state, so Tev is the one contact whose state must be keyed per player — decide in your plan whether that's per-player keying inside the existing ledger/contact system or a dedicated Tev state synced host-authoritatively alongside it. Either way it lives in `SaveData` + the join snapshot and the host owns every roll and timer.

**The trigger: Tev texts you.** When a player completes the first (free, already-built) 3-cap deal, Tev sends that player a text and is added as a contact in their phone — with a visible bond, like other contacts. `[AUTHOR]` the message; content: swing by anytime and he'll front you caps, you just split the profit 50/50 with him.

**Random funny texts.** From then on, Tev texts that player something random and funny every 2–5 minutes. `[AUTHOR]` a batch of ~20 short texts in Tev's voice, all referencing the game's world (shrooms, the aliens, the village, oxygen, the player parking on his lawn, space, his revoked pilot license, etc.) — I'll cut and approve the batch. Host rolls the timing and the pick, per house rules. Make the interval a single tunable constant; I expect I'll want it to slow down after the first hour, so build it so that's a one-number change.

**The fronting state machine, per player:**

*No front active* (after onboarding completes):
- First conversation: the pitch. `[AUTHOR]` — my draft is a run-on, reword it; two candidates to start from:
  - "Looking for cash? Swing by anytime — I front the shrooms, we split it fifty-fifty. One rule: my half comes back before you get more."
  - "Anytime you're after a bit of cash, come see me. I'll front you the shrooms and we split it fifty-fifty — but my half comes home before you get another front."
- Options: **"I'm ready, give me what you got"** → front issued. **"Sounds good, I'll be back soon"** → conversation ends, Tev: **"alright pussy"** (verbatim).
- After the first completed cycle, the idle greeting becomes: **"ready for more big boy?"** — yes → front issued; no → **"good things come to those who grind"** (verbatim). The full pitch does not repeat.

*Front issued:*
- Random strain + random quantity. Early bounds: small and cheap for the first few fronts, scaling with completed-front count (propose the curve in your plan).
- Owed = **50% of market value**: `owed = ceil(0.5 × MarketPrice(strain) × qty)`. If a prior underpayment left an outstanding balance, it's already blocking new fronts (below), so owed is always a single front's debt plus any remainder.
- Tev's line `[AUTHOR]`, shape: "Splendid! Here you go — {qty} {strain}. They go for ${MarketPrice} each at market, so bring me back ${owed}." Stating the per-cap market price out loud is deliberate: it teaches the player what "market" means, which is what lets them later realize they can sell above it.
- **The skim is a feature, not a bug:** Tev's cut is pinned to market value, not the actual sale. Selling above market pockets the difference and Tev never knows. Do not "fix" this.

*Fronted (debt open):*
- Talking to Tev: **"Wonderful to see you, do you have my ${owed}?"**
- **Yes** → payment UI (Part 5). **No** → **"then get back out there and get it!"** — ends, state unchanged.
- No new front while any balance is outstanding.
- If the player disposed of the product (ate it, lost it): the debt stands; wild shrooms are always harvestable so it's grindable, never a softlock. `[AUTHOR]` one Tev ridicule line for returning empty-handed having eaten the product (consistent with the existing ate-all-three joke).

`[INTEGRATE]` `MarketPrice(strain)`: locate the canonical per-strain base price the buyer/offer system derives from (`BuyerLedger` / offer generation). If no single table exists, extract one and make it the shared source for both buyer offers and Tev's math. **Verify buyer offers can exceed base price** (bond-boosted) so the skim is actually reachable — and report the current per-strain price ranges back to me; I need them for tuning.

## Part 5 — Payment UI

Opens from the **Yes** answer while a balance is outstanding:
- Small panel: Tev's money slot on one side, your slot-8 stack on the other, the outstanding amount shown.
- Click-drag the money from slot 8 toward Tev's slot; **scroll wheel adjusts the split** — how much you keep vs. how much goes to him — both numbers updating live. Pre-fill the give-amount to exactly the outstanding balance.
- **Done** commits any amount above zero. Three outcomes:
  - **Underpay** → Tev takes it, balance decreases by the amount paid, and he tells you there's still money outstanding that must be paid before he fronts you more. `[AUTHOR]` the line, shape: "I'll take it — but you're still ${remaining} short. No more fronts till I'm square."
  - **Exact** → clean success: **"thanks come back anytime and we can do more buisness"** `[AUTHOR` — keep the sentiment, fix the wording`]`. Balance clears, state returns to no-front.
  - **Overpay** → he's extra appreciative and **the overage builds your bond faster** — bond gain scales with how much extra you gave; propose the scaling in your plan. `[AUTHOR]` an extra-appreciative line. Balance clears, state returns to no-front.
- Cancel/Esc returns all money to slot 8 and changes nothing.
- Money moves as items (slot 8 → despawn on Tev's side); the wallet-out-of-sync-layer invariant is untouched because nothing here syncs wallet state.

Bond sources so far: completing a repayment builds bond normally; overpayment builds it faster. Propose baseline numbers in your plan — I'll tune.

`[TEST]` Full solo cycle: pitch → front → sell above market → pay exact → "ready for more" → second front. Underpay twice, then clear the remainder, then get a new front. Overpay and see bond jump. Decline paths. Save/load mid-debt (front, remainder, and bond survive). Co-op: both players fronted simultaneously, one underpaid and one clear, states fully independent; each player receives their own Tev texts.

## Part 6 — Later, not now

Bond-gated supplier mode (high Tev bond converts him from 50/50 fronter to a wholesale supplier of chosen strains/spores) is designed but **not in this build**. The per-player bond you're building in Parts 4–5 is exactly the stat that will gate it — record front count and repayment history alongside it. Do not build the supplier shop or wholesale pricing.

## Done means
- Compiles; harnesses pass; whiteboard + slot 8 + fronting loop + Tev texts all function in a solo run start to finish.
- The Part 3 dialogue report was delivered and my edits incorporated; all `[AUTHOR]` copy was presented for approval.
- The whiteboard is placed, and you've told me the exact GameObject name/path so I can reposition it.
- Closing summary: per-strain market prices found, the front-size curve and bond numbers you chose, any assumption you made, and what needs a two-instance manual test.
