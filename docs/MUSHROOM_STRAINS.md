# Mushroom strains — rarity, potency, price

2026-08-06. Companion to `MUSHROOM_ECONOMY.md`.

**STATUS: BUILT and compiling. NOT play-tested.** Everything below is in the
project — the strain table, the rarity-weighted spawn, the haggle panel, the
hold-and-drag slots and the moon fix. Verified by an editor-side check
(all 23 prefabs have authored rows, tiers land at 47.8 / 30.4 / 21.7 %,
encounter share 67.9 / 25.9 / 6.2 %, and every moon + the Sun + the black hole
report barren). What that check CANNOT tell you is whether any of it feels
right, so the play-test list at the bottom is the real gate.

The pack ships **23 mushroom prefabs**, and all 23 are already live species —
`MushroomRegistry` reads them straight off `MushroomSpawner.mushroomPrefabs`.
Today they are undifferentiated: every species is worth the same, and its trip
is a hash of its own name. This turns that array into an actual product table.

---

## The split

Sam's spec: common 50% / uncommon 30% / rare 20% of the species that grow wild.

| tier | species | share | base value | trip | what it is |
|---|---|---|---|---|---|
| Common | 11 | 47.8% | 8–14 cr | 15–25 s | filler. You'll drown in these. |
| Uncommon | 7 | 30.4% | 22–34 cr | 30–50 s | one strong signature each |
| Rare | 5 | 21.7% | 48–90 cr | 60–120 s | the Amanitas. Worth the walk. |

23 doesn't divide into 50/30/20 cleanly; **11 / 7 / 5** is the closest integer
split and lands within 2 points on every tier.

The tiers fell out of the pack almost for free: the *Amanita* genus — the real
world's actual psychoactive mushroom (fly agaric) and its actual lethal one
(death cap) — is exactly 5 of the 23 prefabs. Rarest and most potent is what
those models already look like. Nothing had to be forced.

### Spawn weighting

With **uniform per-species spawn weight**, species share *is* encounter share:
walking around, ~48% of what you find is common, ~30% uncommon, ~22% rare. That
already matches the spec, and it's what the spawner does today
(`prefabIdx = hash % prefabs.Length`) — so the split needs no spawner change at
all beyond the tier assignment.

**But 22% rare will not feel rare.** One in five wild caps being a Fly Agaric
makes the top tier ordinary. Recommended starting weights:

| tier | weight | encounter share |
|---|---|---|
| Common | 5 | 55/81 = **68%** |
| Uncommon | 3 | 21/81 = **26%** |
| Rare | 1 | 5/81 = **6%** |

Species split stays 50/30/20 exactly as specced; *encounter* rate is the knob
that makes rare feel rare. Start at 5/3/1, and if rare feels too scarce move to
5/4/2 (59/30/11). One line in the spawner either way.

---

## COMMON — 11 species

Cheap, short, mild. These are what the tutorial and the early loop run on.

| prefab key | street name | value | trip | colour | wave | kaleido | heal | feel |
|---|---|---|---|---|---|---|---|---|
| `Champignon_little` | Buttoncap | 8 | 15s | .15 | .10 | .05 | 5 | barely anything. The "is this working?" shroom. |
| `Champignon_big` | Big Button | 10 | 18s | .20 | .15 | .05 | 6 | same, more of it |
| `Agaricus` | Field Grey | 10 | 18s | .10 | .25 | .05 | 5 | gentle sway, no colour |
| `Agaricales_big` | Starter | 12 | 20s | .25 | .15 | .10 | 6 | **Tev's front.** The reference high. |
| `Boletus_big` | Cork | 12 | 20s | .10 | .30 | .00 | 10 | body-heavy, best heal in tier, no visuals |
| `ImleriaBadia_little` | Bay Runt | 9 | 15s | .20 | .10 | .10 | 5 | short and forgettable |
| `ImleriaBadia_big` | Bay Bolete | 12 | 20s | .30 | .15 | .10 | 6 | mild colour lift |
| `Leccinum_little` | Scaber | 10 | 18s | .15 | .20 | .05 | 6 | soft wobble |
| `Leccinum_big` | Roughstem | 13 | 22s | .20 | .25 | .10 | 7 | the best common all-rounder |
| `Cantharellaceae_little` | Chanty | 11 | 18s | .35 | .05 | .05 | 5 | pure colour, no motion — cheap and pretty |
| `Fomes` | Shelf | 14 | 25s | .05 | .35 | .15 | 8 | longest common, dullest common |

`Agaricales_big` stays the strain Tev fronts you (`MushroomQuest`, unchanged) —
it now reads as deliberately mid-tier rather than arbitrary.

---

## UNCOMMON — 7 species

Each one owns a single dial, so the player can learn them by feel alone.

| prefab key | street name | value | trip | colour | wave | kaleido | heal | signature |
|---|---|---|---|---|---|---|---|---|
| `Cantharellaceae_big` | Goldhorn | 24 | 35s | **.75** | .10 | .10 | 8 | colour blowout, world stays still |
| `Lactarius_little` | Milkcap | 22 | 30s | .25 | **.65** | .10 | 10 | the world breathes |
| `Lactarius_big` | Bleeding Milkcap | 27 | 40s | .30 | **.75** | .15 | 12 | heavy breathing + best uncommon heal |
| `Macrolepiota_little` | Parasol Runt | 24 | 35s | .20 | .20 | **.55** | 8 | first real kaleidoscope |
| `Macrolepiota_big` | Parasol | 30 | 45s | .30 | .25 | **.65** | 10 | full mirror-tile |
| `Agaricus_Atramentarius_little` | Inkcap | 26 | 35s | .40 | creeper | creeper | 8 | **20s of nothing, then it lands** |
| `Agaricus_Atramentarius_big` | Black Ink | 34 | 50s | .50 | creeper | creeper | 10 | 25s of nothing, then it lands hard |

**"Creeper" is free.** `RawFishTripController.StartTrip` already takes
`early*` / `late*` phase values and an `earlyPhaseDuration`, and
`MushroomEffect` currently passes the same numbers for both — every trip in the
game is flat. The Inkcaps just stop doing that: early `kaleido .05 / wave .05`,
then late `.70 / .55` (little) or `.85 / .65` (big). No new system, and it gives
the tier a strain whose whole identity is *timing*.

---

## RARE — 5 species (all Amanita)

| prefab key | street name | value | trip | colour | wave | kaleido | heal | signature |
|---|---|---|---|---|---|---|---|---|
| `Amanita_little` | Redcap | 48 | 60s | .85 | .55 | .60 | 15 | everything at once, one minute |
| `Amanita_big` | Fly Agaric | 65 | 80s | 1.00 | .70 | .80 | 18 | **the flagship.** The iconic red cap. |
| `Amanita_Ovoidea` | Ghost | 55 | 70s | **.00** | .90 | .30 | 20 | inverted: colour drains out, world heaves. Biggest heal in the game. |
| `Amanita_Phalloides_little` | Deathcap Runt | 70 | 90s | .90 | .85 | .90 | **−15** | it hurts you |
| `Amanita_Phalloides_big` | Deathcap | 90 | 120s | 1.00 | 1.00 | 1.00 | **−25** | two-minute whiteout, serious damage |

### The Deathcap tension

The two most valuable things you can grow are the two you must not sample.
Negative heal means a Deathcap is pure product — it can't double as a health
item, and a player who eats their own stock to "test it" pays for the lesson.
That's the most Schedule-1-shaped mechanic in the whole table, and it costs one
sign flip on a number that already exists (`ResourceManager.Heal` takes a float;
`MushroomEffect.HealPerMushroom` is currently a flat `5f` const).

`Ghost` inverting the colour dial instead of maxing it is the other deliberate
one — it stops "rare" from just meaning "all sliders up".

---

## Pricing: base value × buyer multiplier

Today `NPCMushroomPrice` returns flat credits/mushroom in a 12–29 band, and
species is irrelevant — a Fly Agaric sells for the same as a Buttoncap.

Change it to Schedule 1's model:

```
price per cap = round( MushroomSpecies.BaseValue(key) × buyer.multiplier )
```

- `BaseValue` comes from the table above (8 → 90).
- `multiplier` is derived from the buyer's existing stable-identity hash, mapped
  into **0.75 – 1.35** instead of into raw credits.

This keeps everything `NPCMushroomPrice` already gets right — derived not
stored, stable across streaming and save/load, "learn which buyer is worth the
walk" — while making *what you carry* matter as much as *who you sell to*. A
generous buyer on Buttoncaps (14cr) still loses to a stingy one on Fly Agaric
(49cr), which is the pressure that makes the rarity tiers mean anything.

---

## Implementation plan

**1. `MushroomSpecies.cs`** (new, `Assets/3 - Scripts/World/`) — a static table
keyed by prefab name: display name, tier, base value, trip duration, three
dials, early-phase split, heal. Unknown keys fall back to the current hash
behaviour, so a prefab Sam drags in later still works before it's in the table.

**2. `MushroomRegistry`** — add `Tier(key)`, `BaseValue(key)`, `DisplayName(key)`
delegating to the table. Display name becomes the street name, so hotbar slots,
world prompts and the sell panel all read "Fly Agaric", not "Amanita big".

**3. `MushroomEffect.GetDials`** — read the table instead of hashing; pass the
real early/late split to `StartTrip`; take heal from the table so Deathcaps can
go negative.

**4. `MushroomSpawner`** — replace `hPI % prefabs.Length` with a weighted pick
(5/3/1 by tier). One function; determinism per cell is preserved because it
still consumes the same hash.

**5. `NPCMushroomPrice`** — swap the credits band for a 0.75–1.35 multiplier and
add `PriceFor(speciesKey)`. `MushroomSellUI` calls that instead of reading a
flat number.

**6. Rarity in the UI** — tier colour on the hotbar slot corner and the sell
panel (grey / blue / purple, as in the mockups). This is what makes the system
legible; without it the player never learns the tiers.

Steps 1–3 are self-contained and independent of the sell-UI rework. 4–6 are
small. None of it touches the save schema: species is already stored as a
string key, and everything above is derived from that key at runtime.

---

## Selling: the haggle panel — RATIFIED 2026-08-06

Sam picked concept **C** from the four mockups. Prototype:
`scratchpad/sellui/index.html`, tab C.

### The negotiation

```
        you name P1
             │
   ┌─────────┼──────────────────┬──────────────────┐
   │         │                  │                  │
 ACCEPT   COUNTER at C      BARRED 5 min      (P1 at/under
 (sale)      │              (too ridiculous)   their rate)
             │
   ┌─────────┼──────────────────┐
   │         │                  │
 TAKE C   PUSH once with P2   LEAVE IT
 (sale)      │                (C stays on the table;
             │                 come back to it later)
      ┌──────┴──────┐
   ACCEPT        BARRED 5 min
   (sale)         — final, no third round
```

Three rules that make it work:

- **The counter is anchored on the buyer's own value, never on your ask.**
  `C = round(fair × 1.00–1.05)`. Lobbing a silly number can't drag the counter
  up with it, so asking high buys you risk with no upside — which is what makes
  the *walk threshold* the real decision instead of "always ask double".
- **The counter is how you learn the buyer.** Once they say 87, you know their
  rate. That's the information loop replacing the readout we just deleted:
  earned by dealing, not printed on arrival.
- **LEAVE IT parks the deal, it doesn't punish you.** Their number is remembered
  and still stands when you come back. Without this, walking away and reopening
  would be a free re-roll on the acceptance dice; with it, there's nothing to
  re-roll. Only *pushing past* a counter can get you barred.

### What the panel may and may not show

Sam's call, and it's the right one: printing `pays 130% of base · patience 34%
over` handed the player both of the buyer's hidden numbers on arrival, and was
unreadable to anyone who hadn't read the source.

| shown | hidden |
|---|---|
| Strain **market value** (`BaseValue`) — a property of the mushroom, identical for every buyer | The buyer's multiplier |
| How far over market **your own ask** is | The buyer's patience / walk threshold |
| What this buyer **has actually paid you before** | Anything about a buyer you've never dealt with |

The greed meter survives, but it now measures your ask against **market**, not
against the buyer's tolerance. That's information the player could work out
themselves from the strain value, so it's a convenience rather than a spoiler —
and critically it reads the same for every buyer, so it can't be used to sniff
out who's generous without dealing.

The "you remember: last paid 87 a cap" line is the one concession, and it's
player-earned: it only appears for buyers you've already sold to, and it records
what *they did*, not what they *would* do.

### Consequence for `NPCMushroomPrice`

The multiplier model from the pricing section above still stands — but nothing
renders it now. `PriceFor(speciesKey)` becomes internal to the deal logic, and
the panel asks it three questions instead of one: *would they accept P?*,
*what would they counter?*, *is P ridiculous?*

### Slot rules — final

Sam's call after seeing the prototype: **keep left click = whole stack and right
click = one in / one out exactly as they are today.** The only change is
**hold + drag**, which carries the whole stack and drops it where you release.

This is a much smaller edit than the click-to-count scheme originally sketched:
nothing already in muscle memory moves, `SlotOps.HandleLeftClick` /
`HandleRightClick` keep their current semantics untouched, and the new
behaviour is additive — a drag threshold in `StorageUI`'s slot handler that
routes to the existing `PickUpFull` path, plus a drop target. It also removes
the ambiguity the click-to-count scheme created (clicking the source slot again
while holding: take one more, or deposit?), which no longer needs a rule.

**Trap, hit and fixed in the prototype: every slot must be a drop TARGET, not
just a drag source.** The first pass only registered the sell panel's offer zone
as a drop target, so dragging a cap back onto its own stack found no target
under the cursor, fell through to "return to source", and snapped back to where
it came from — it only worked via click-release-click. In the Unity build this
means the drop half has to go on **every** slot in `StorageUI` (and the hotbar),
not only on the new sell panel. Release-over-nothing must still return to source.

The drop path also has to be a real deposit, not an append: merge only up to
`Hotbar.StackMax` and keep the remainder on the cursor, and **swap** when the
destination holds a different species. `SlotOps.Deposit` already does exactly
this — the drag should call into it rather than growing a parallel code path,
which is the same table-driven rule the Hotbar follows elsewhere.

---

## Panel revision 2 — 2026-08-06, after first look in-engine

Sam's notes on the built panel, all applied:

- **The bar showed every hotbar item**, axe and water bottle included. Now it
  lists **mushroom stacks only**, packed left, with an empty-state line. Showing
  un-draggable items in a drag-to-sell panel implied they were draggable.
  Tiles are no longer 1:1 with hotbar slots, so each tile carries a `realIndex`
  the click/drag closures read at call time.
- **The price slider is gone.** A slider for a number the player wants to set
  exactly is a second, worse copy of the field-and-steppers next to it.
- **The unlabelled coloured meter is gone**, replaced by a sentence:
  *"asking 40% over market value — pushing it"*. Same information, still
  measured against market and never against the buyer, but legible. The meter
  failed the only test that matters: the person who commissioned it asked what
  it did.
- **The "left click = whole stack…" helper line is gone.**

### Revision 3 — the labels ran off the bottom

Reported as "maybe a scaling issue because of the resolution I play at". It
wasn't, and it would have been wrong on every monitor.

`Txt()` chose top-vs-centre anchoring from **the parent's pivot**
(`parent.pivot.y > 0.9f`). That test is meaningless: a child's anchor is relative
to the parent's **rect**, and has nothing to do with the parent's pivot.
`_panelRT`'s pivot is `(0.5, 0.5)`, so every label on the panel anchored to the
panel's CENTRE while every `Panel()`-built piece anchored to its TOP. "TOTAL FOR
THE LOT" at `y=-542` therefore landed 542 px below centre — 182 px past the
bottom edge. The whole panel scales as one unit under CanvasScaler, so resolution
could never have caused or fixed it.

`Txt()` now always anchors top-centre, matching `Panel()`, so every `y` in
`BuildUI` reads as "pixels down from the top of the parent". Verified in play
mode by walking every child of the panel and comparing its corners against the
panel rect: **0 elements outside** (lowest is the button row ending at −346
against a −360 edge). `MoneyBadge` sits above the panel deliberately.

Also nudged `result` / `cooldown` apart — both render while barred and they
overlapped by 2 px.

**Lesson worth keeping:** in a code-built UGUI panel, pick ONE anchoring
convention and apply it everywhere. A helper that guesses per-call is a layout
bug waiting for a specific parent.

### Revision 4 — spoken line overlapped the choice list

Closing the deal panel dropped you back on the "Sell mushrooms / Leave" rows with
the alien's greeting still drawn underneath them.

The greeting was never actually cleared: **every** NPC turns `dialogueText` on in
`StartConversation` and only off in `StopConversation`, so it stays on screen for
the whole conversation. Both it and the choice list are full-width and sit low,
so they collide. It was there from the greeting onward — returning from the sell
panel just made it obvious, because that redraws the rows straight over a line
that had been sitting there the whole time.

Fixed at **two choke points** rather than in the four NPC scripts (plus
`NPCDialogue`), because a fifth NPC would forget:

- `PostGreetingChoicePanel.Show()` hides the spoken line. The label is shared and
  owned by `NPCDialogue`; resolved lazily and re-resolved whenever it's null, so
  a gameplay-scene reload can't strand a destroyed reference in a static.
- `DialogueTextStyling.RevealCharsTMP` / `RevealCharsLegacy` re-activate the label
  before typing. Every typewriter in the game funnels through there, which is what
  makes hiding it unconditionally safe — a line spoken *after* a choice (Tev's
  branching questions) still shows. Without this half, the fix would have silently
  broken Tev's onboarding.

Also: the panel's back button says **CLOSE**, not DONE. "Done" reads as "finish
the sale" sitting next to a sell button, when it actually just leaves.

### Species use their REAL names

Sam's call: the invented street names (Buttoncap, Goldhorn, Fly Agaric) lost what
made the pack feel like actual mushrooms. Display names are now the real species
names off the prefabs — Amanita, Boletus, Cantharellaceae, Amanita Phalloides.

Where the pack ships a size pair, the smaller gets a `Small ` prefix so the two
are still tellable apart in a slot; that prefix is the only invented word in the
column. Two long ones are shortened to keep them in a slot label:
`Agaricus_Atramentarius_little` → "Small Atramentarius" and
`Amanita_Phalloides_little` → "Small Phalloides".

### Aliens have names now

"Wandering Alien" was actively working against the design. The whole learning
loop is *remember which buyer pays well*, and you cannot build a mental map of a
route when every stop has the same name — "you remember: last paid 87 a cap"
means nothing attached to a generic noun.

`AlienNames` (64 names, register matched to Tev and Kolb, both deliberately
excluded so a wanderer can't collide with a story character) derives a name from
`AlienIdentity` — the same stable spawn-cell key the price hash uses, pulled out
into its own helper so names and prices can't drift apart. Nothing is stored; an
alien 300 m behind you is called the same thing when you walk back.

The name is **salted separately from the price**, so two aliens that happen to
share a name still pay differently — otherwise the player would learn the name
instead of the individual. Verified over 300 identities.

It also shows on the interact prompt ("Press F to talk to Vorn"), which is where
the player actually learns it.

**Knob:** with 64 names, two aliens on screen at once can occasionally share one.
Add rows to the array if that shows up in play — the hash spreads over whatever
length the array is and no saved data depends on the order.

---

### Revision 5 — no way to put caps back

Making the bar "mushrooms only, packed left" hid the empty hotbar slots, and
those empty slots were the only drop target for putting a carried stack DOWN.
You could drag the offer onto another stack and swap with it, but there was
nowhere to drop it so that nothing was selected — the offer could go in and never
come out. A regression I introduced two revisions earlier.

Two fixes, both wanted:

- **One trailing empty tile** in the bar, pointing at the first free hotbar slot.
  Standard inventory language, and it restores drag-out without bringing back the
  axe and the water bottle. Only drawn when a free slot actually exists.
- **`+` / `−` on the offer slot**, flush with its top-right and bottom-right
  corners, stepping one cap on and off the table. `−` emptying the table clears
  the species (so a different strain can go on) and drops any counter, which was
  priced against the old strain.

Both buttons are parented to the **panel, not the zone**. A `Button` doesn't
implement `IBeginDragHandler`, so inside the zone a press-and-twitch on one of
them would bubble up to the zone's `SlotDragProxy` and drag the whole offer out.

Positions verified in play mode against the panel rect: `Mini+` at y 259..233 and
`Mini−` at 201..175 sit exactly flush with the 84px slot's 259..175 span, 5 px
clear of it, both inside the panel.

## Buyer demand + taste — BUILT 2026-08-06

The hole this fills: a buyer would take infinite caps at a fixed rate, so the
optimal play was to find the most generous alien and stand there. Per-buyer
pricing, their names and the rarity tiers had nothing to bite on.

Three additions, all on top of `NPCMushroomPrice` and `MushroomDealState` — no
new UI:

- **Appetite.** Each buyer takes 6–24 caps (derived from identity) and is then
  full. Regenerates in real time over `AppetiteRefillSeconds` (600s to go from
  stuffed to empty).
- **Saturation.** Price sags toward 0.8× as they fill. Deliberately driven by the
  *same* number as appetite rather than a second hidden quantity — one thing to
  learn per buyer, not two.
- **Taste.** Each buyer is keen on one tier (×1.35) and cold on another (×0.72).
  Combined with their 0.75–1.35 multiplier the spread is ×0.54 to ×1.82 of market.

### Partial sales are how appetite is discovered

Offer 40 to a buyer who wants 12 and they take 12, pay for 12, and hand the other
28 back. No readout, no refusal — one interaction teaches you their size. The
taste note (`keen on rare`) only appears once you've actually sold them that
tier, same earned-knowledge rule as the rest of the panel.

Two "no" states, and they read differently because one is a punishment and one is
a timer: **barred** (you pushed past their counter, 5 min) vs **full up**.

### Measured

Eight buyers, 60 rare caps, no waiting:

| strategy | sold | credits |
|---|---|---|
| camp on the single most generous buyer | 18/60 | 2124 |
| walk the route | 60/60 | **4683** |

The route is 2.2× better, which is the whole point. Same-strain prices ranged
41–118 cr across the eight, so knowing who's who is worth real money.
Taste distribution verified over 4000 identities: 3.9% off uniform, and
favourite never equals disliked.

### One thing the measurement caught

Appetite regenerates continuously, so a stuffed buyer had room for one more cap
after 47 seconds — the sell row would un-grey and the player would walk back for
a single cap. A buyer now only counts as available once they'd take a quarter of
their appetite (`WorthStopping`), which moved that to ~3 minutes. Sales already
in progress aren't gated by it: if they have room for 3 and you're stood there,
they take 3.

**Knobs:** `AppetiteRefillSeconds` (600) is the pacing dial for the whole route.
`minAppetite`/`maxAppetite`, the taste multipliers, and the 0.25 worth-stopping
fraction are all inspector/constant-level.

---

## Play-test list

Everything below compiles and passes an editor-side data check; none of it has
been played. Ordered so a failure early on explains failures later.

**Slots (test in the ship locker first — it's the fastest way in)**
- [ ] Left click still takes the whole stack; right click still takes/places one.
      Nothing about the old behaviour should have moved.
- [ ] Hold and drag a stack to an empty slot — it goes there.
- [ ] Right-click one cap out, drop it in an empty slot, then **drag it back onto
      its own stack** — it must MERGE, not snap back. (This is the exact bug from
      the prototype; the fix was making every slot a drop target, not just the
      sell panel's.)
- [ ] Drag a stack onto a DIFFERENT species — they swap, and no caps vanish.
- [ ] Drag 15 onto a stack of 10 — it fills to 20 and 5 stay on the cursor.
- [ ] Release a drag over empty space — the stack returns where it came from.
- [ ] Close the locker mid-drag — nothing is lost.

**Strains**
- [ ] Hotbar and locker slots show a rarity pip: grey / blue / purple.
- [ ] Names read as street names ("Fly Agaric", not "Amanita big").
- [ ] Eat a common — short, mild. Eat a Fly Agaric — 80s and much stronger.
- [ ] Eat an **Inkcap** — should be almost nothing for ~20s, then land. This is
      the one that proves the early/late phase split actually works.
- [ ] Eat a **Deathcap** — it should HURT you, red flash and all. Check at low
      health that it can actually kill (it routes through TakeDamage).
- [ ] Chop wild mushrooms for a while — commons should dominate hard, and a
      rare should feel like a find (~6% of spawns).

**Moons**
- [ ] Fly to Constant Companion / Tumbling Bean / Watchful Eye — **zero**
      mushrooms. Planting was already blocked; confirm it still is.
- [ ] Planets still grow them normally.

**Names**
- [ ] Walk up to a wandering alien — the prompt reads "Press F to talk to <name>",
      not "to talk".
- [ ] Same alien, walk away past 300 m and come back → **same name**.
- [ ] Two different aliens have different names (mostly — 64-name pool).
- [ ] The sell panel header and every line in it uses that name.

**Getting caps back off the table**
- [ ] Drag the offer onto the trailing empty tile in the bar → it comes off the
      table and nothing is selected.
- [ ] `+` / `−` beside the offer slot step one cap on and off.
- [ ] `−` down to zero clears the offer entirely, so a different strain can go on.
- [ ] `+` greys out when you've got no more of that species in the bar.
- [ ] Press-and-slightly-move on `+` must NOT drag the whole offer out.

**Demand and taste**
- [ ] Offer a buyer far more than they want → they take some, pay for those, and
      the rest come back to your bar with a message saying so.
- [ ] Sell a buyer their fill → the "Sell mushrooms" row greys with a countdown
      reading **"they're full"**, which must read differently from the barred
      "not talking to you".
- [ ] Wait it out → the row comes back, and they want a worthwhile amount (not 1 cap).
- [ ] Same strain to two different aliens → noticeably different money.
- [ ] Sell someone their favourite tier → "keen on <tier>" appears in the memory
      line. It must NOT appear before you've sold them that tier.
- [ ] Walking a route should out-earn camping on one alien. If it doesn't, say so —
      that's the whole point of this pass.

**The deal**
- [ ] Talk to any alien → "Sell mushrooms" → the panel opens showing **only your
      mushrooms** — no axe, no water bottle, no empty tool slots.
- [ ] Carrying no mushrooms → "you're not carrying any mushrooms".
- [ ] No slider anywhere; price is the number field plus − / +.
- [ ] The greed line reads as a sentence ("asking 40% over market value").
- [ ] Drag a stack onto the table. Ask seeds at MARKET value.
- [ ] **Nothing on screen states what this buyer pays or how far they'll go.**
      Only market value, your over-market band, and "last paid" once you've sold.
- [ ] Drop a second species — refused, with a message, not silently merged.
- [ ] Ask at/below their rate → instant accept, money lands.
- [ ] Ask well over → they COUNTER. Take it → sale at their number.
- [ ] Counter, then PUSH slightly over → sometimes lands.
- [ ] Counter, then PUSH hard → barred. Sell row greys out with a countdown,
      and stays greyed on a second conversation. **Your mushrooms come back to
      the bar, not lost.**
- [ ] Counter, then LEAVE IT, walk off, come back → their number is still there.
- [ ] Close the panel with caps on the table → they're back in your bar.
- [ ] Two different aliens quote different numbers for the same strain; the same
      alien quotes the same number twice.
- [ ] Selling still advances Tev's onboarding count.
- [ ] New Game after being barred → not barred any more.

**Known gap:** the 5-minute ban and parked counters are session state, not saved.
A save/load or scene reload clears them. Deliberate for v1 (JsonUtility can't do
dictionaries, and reloading to dodge a 5-minute timer is slower than waiting) —
see `MushroomDealState`'s header for how to persist it if it matters later.

---

## Open decisions

- **Are 23 species too many?** `MUSHROOM_ECONOMY.md` already flagged this:
  species-pure stacks × 7 hotbar slots means indiscriminate chopping fills the
  bar fast. The tier table makes it worse in one way (you now *want* to hold
  the rare ones) and better in another (you can safely ignore commons). If it
  bites, cut the little/big duplicate pairs — that drops 23 → 15 with no
  design loss, since each pair currently differs only in size.
- **Do trip dials need to scale with the number of caps eaten?** Right now one
  cap = one full trip. A 120s Deathcap trip from a single cap is a long time.
- **Should selling a rare strain to a buyer who's never seen it pay a bonus?**
  Natural hook for the "more drugs later" expansion, but not needed for v1.
