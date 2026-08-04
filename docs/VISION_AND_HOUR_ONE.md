# The next hour — a proposal

Written 2026-08-04, after the mushroom slice shipped. This is my answer to
"what should the next hour of gameplay be, after Tev sends you out."

It's opinionated on purpose. Sam's problem isn't a shortage of ideas, it's that
there are too many and none of them are a game yet. So this doesn't offer a
menu — it picks one shape, argues for it, and says out loud what gets parked to
make room.

---

## 0. The pitch, in one paragraph

**Schedule 1, but the map is a solar system and the land is dead.** You strip
the mushrooms off a patch of world, sell them to whoever's nearby at whatever
they'll pay, and use the money to make more land able to grow. Trees are
oxygen, oxygen is growth, growth is product. The wild supply never comes back,
so every planet is a patch you exhaust and then either cultivate or leave. You
start with an axe on a half-dead rock. You end with a cultivated world and a
ship pointed at the next one.

---

## 1. The one thing this game already has that nothing else does

Everything below rests on a mechanic that is **already built and working** in
this repo, and I don't think it's being valued at what it's worth:

> `PlanetOxygen`: *"Trees ARE the atmosphere."* A planet's breathable O2 is
> computed from its real living-tree count. Chop trees, the number falls.
> Plant trees, it rises. Your suit's converter refills faster in richer air.
> Mushroom growth speed keys off ambient O2 at the mushroom's own position.

That is a **terraforming supply chain**, and it's load-bearing in a way most
survival-game "planting" isn't. It means:

- Cutting wood has a cost that isn't a number in a menu — it shortens how long
  you can stay outside.
- Planting a tree is not decoration. It extends your range, speeds your crop,
  and raises the ceiling on what the land yields.
- A dead moon isn't "a level with no trees", it's **a business opportunity with
  a capital requirement** (a Bubble Dome, which already exists and already
  makes its own sealed O2).

Schedule 1's supply side is *rent a property, buy seeds, wait*. This game's
supply side is *make a world capable of growing things*. That's the pitch. Lead
with it in everything.

**The second load-bearing fact, also already true in code:** harvested wild
mushroom cells are marked consumed and **persist in the save — they never come
back**. The world does not respawn. That's not a bug to fix, it's the engine of
the whole progression. Within an hour or two the ground around the shuttle is
picked clean, and the player is *forced* from foraging into farming. Nothing
needs to nag them. The land runs out.

---

## 2. The mapping (why almost nothing new needs building)

| Schedule 1 | This game | Status in repo |
|---|---|---|
| The town | Humble Abode (spawn planet) | built |
| Neighbourhoods | Other planets / moons | built (nbody, streaming) |
| Customers | Wandering aliens + village vendors | built; every NPC buys, each at their own stable price |
| The dealer who fronts you | Tev | built this week |
| Properties you buy to grow in | **Bubble Domes** — sealed, self-oxygenating plots you place on dead rock | built (dome fuel/screen/refuel + save) |
| Seeds | Spores (0–2 per mushroom, species-true) | built this week |
| Product quality | **Mushroom size** — 1× runt to 5× monster, size drives yield | built today |
| Product variety | 23 species, species-pure stacks, per-species prices | built |
| Backpack | 7 hotbar slots, 20 per stack | built, and it's already the binding constraint |
| Police / heat | *(park it — see §7)* | enemies built, not needed yet |
| Phone | Phone/tablet with app slots | built (FNAF-style flip-up) |

The honest headline: **the first hour I'm proposing needs roughly two new
systems.** Everything else is wiring, tuning, and deleting objectives that point
at parked content.

---

## 3. The first hour, minute by minute

Assume the pod/wake/ramp intro is unchanged (Sam owns it). The clock below
starts when the ramp drops.

### 0:00–0:03 — Locker (existing)

Axe and water bottle in the shuttle locker. Nothing else happens. No NPC, no
prompt, no objective. This quiet is deliberate and it's already implemented as
Tev's 120-second hidden window.

### 0:03–0:10 — The first thing you break

Trees and mushrooms are both within sight of the ramp. The player will swing at
something. Whichever they hit first teaches them the same lesson: **things here
come apart and give you stuff.**

One change I'd make: the very first mushroom the player chops should be a
**big one, placed by hand near the ramp**, not left to the spawner. Seven caps
tumbling out of a 5× monster on the first swing is the game's thesis statement
in three seconds. A 1× runt giving two caps is not.

### 0:10 — Tev appears

He's been absent exactly two minutes. He's not summoned, marked, or announced —
he's just there now, outside his cabin, 25 m from where you parked. Player finds
him or doesn't.

### 0:10–0:14 — The front

Three caps, on the house. "Find a buyer." No waypoint, no quest marker. The
village is a 3–4 minute walk and **you can see it from the ramp** — that's the
whole direction system.

### 0:14–0:22 — The first sale, and the lesson that prices differ

This is the most important eight minutes in the game and it needs one addition
to land.

Right now every alien quotes a different stable price (12–29 credits). But the
player has no way to *know* that without walking to each one. So:

> **NEW SYSTEM 1 — the Contacts app.** The phone already exists. Give it one
> app: every alien you've talked to gets a row — their name, their price per
> mushroom, which species they favour, and how long since you last sold to them.
> Aliens you haven't met show as "???".

Now the walk to the village is a *route*, not a corridor. The player passes 2–3
wandering aliens on the way, each quoting differently, and learns the shape of
the market by walking through it. Selling all three caps to the first alien at
14 credits is a mistake the player makes once.

The first sale should land somewhere around **60–200 credits**. Enough to feel
like money. Not enough to buy anything.

### 0:22–0:28 — Back to Tev, onboarding closes

He teaches the loop: *caps grow wild, they like oxygen, more trees means faster
shrooms, chop one and you get spores, put them in the ground.* Onboarding ends.
This is the moment the player is "sent out" — everything below is the answer to
Sam's actual question.

### 0:28–0:40 — The first real haul, and the first wall

The player goes wide. This stretch is pure foraging and it should feel great:
mushrooms are dense, they wobble and squelch, caps tumble out, the pack fills.

Three pressures bite, in this order, and **all three have the same solution**:

1. **The pack fills.** 7 slots, 20 per stack, species-pure. Five species and
   you're out of room with 40 caps. The player starts making decisions: dump the
   runts, keep the monsters. *(This is already true and needs no work — it's the
   best constraint in the build.)*
2. **The suit drains.** Walk away from trees and the converter stops keeping up.
   The player's leash is visible in the O2 bar.
3. **The ground runs out.** The area near the shuttle is picked clean and
   doesn't come back.

### 0:40–0:48 — The sell run, and the first purchase

Full pack, walk the route, sell high. Expect **1,200–2,000 credits** for a first
full haul. Then, at the goods vendor:

> **The first thing the player buys is carrying capacity.** A pack upgrade:
> +2 hotbar slots, ~1,200 credits. It is the correct first purchase because it
> is the constraint they just spent twelve minutes fighting.

This is Schedule 1's backpack beat and it works for exactly the same reason.

### 0:48–0:58 — Planting, and the click

Player has spores from everything they chopped. They plant.

This needs the second new system, and it's the one that makes the hour *mean*
something:

> **NEW SYSTEM 2 — the plot readout.** When you're standing in a planted area,
> a small diegetic readout (suit HUD or a placeable stake) shows: **ambient O2
> here**, **growth speed multiplier**, and **time to harvest**. Planting a tree
> next to your mushrooms visibly moves those numbers.

Without it, the trees→O2→growth chain is invisible and the player never learns
the game has a supply side. With it, planting a ring of trees around your
mushroom patch is a discovery the player makes *themselves*, and it's the
discovery the whole rest of the game is built on.

### 0:58–1:00 — The hook out

The hour should end on a want, not a task. Two candidates, and I'd do both:

- **A rare species.** One alien mentions (or the Contacts app shows a "???"
  row at 90 credits) that somebody pays five times normal for a species that
  doesn't grow here. It grows on the moon. The moon is dead rock — you'd need a
  dome.
- **The sky.** The player looks up at a planet they can see and can't reach.

Hour 1 ends with: a picked-clean patch of home ground, a small planted plot
that's visibly filling in, a bigger bag, ~500 credits left, and a specific
reason to want a dome and a ship.

---

## 4. The economy, in numbers

Rough targets to tune against, not gospel.

| | value |
|---|---|
| Cap price | 12–29 per alien, stable per alien, avg ~20 |
| Caps per mushroom | 2–4 (1×) → 7–12 (5×), avg ~6 |
| Value per mushroom chopped | ~120 credits |
| Pack capacity, realistic mixed species | 80–100 caps |
| Full haul | ~1,600–2,000 credits |
| Round trip (out, fill, sell, back) | 8–12 min |
| **Earn rate, hour 1** | **~2,500–3,500 credits** |

Sinks, in the order the player should meet them:

| Item | Cost | Unlocks |
|---|---|---|
| Pack +2 slots | 1,200 | hour 1 |
| Spore stock (5 of a species) | ~300 | hour 1–2 |
| Pack +2 more | 4,000 | hour 2 |
| **Bubble Dome kit** | ~5,000 | hour 2–3 — *the first "property"* |
| Dome fuel refill | ~500 | ongoing |
| **First ship** | ~25,000 | hour 3–4 — *the first new "neighbourhood"* |

The shape that matters: **hour 1 buys a bag, hour 2–3 buys a plot, hour 3–4
buys a planet.**

---

## 5. What makes minutes 20–60 not a grind

Foraging alone gets boring in about fifteen minutes. Four things stop it:

1. **Depletion forces a change of activity.** You can't forage forever; the
   ground stops giving. The game changes underneath you without a cutscene.
2. **The route is a decision.** Different buyers, different prices, decaying
   demand *(see below)*. Where you sell is as interesting as what you pick.
3. **Size is a gamble worth taking.** A 5× cap is three times a 1× for the same
   three axe swings. Players will walk past runts to reach monsters, and that's
   a real choice about time and O2.
4. **Planting pays off on a timer you can watch.** Something is always cooking.

I'd add one small thing to the sell side to make the route genuinely tactical:

> **Buyer saturation.** Sell an alien 20 caps and their price drops for a while,
> recovering over ~10 real minutes. Sell them a species they favour and they pay
> a premium.

That's maybe 60 lines on top of `NPCMushroomPrice`, and it converts "walk to
the highest number" into "run a round". It's the single highest-leverage
addition on this list after the Contacts app.

---

## 6. The shape past hour 1 (so hour 1 sets up correctly)

Sketched only, to prove the hour points somewhere:

- **Hour 2–3 — the plot.** Buy a Bubble Dome, place it, farm inside it. Domes
  need fuel, which is a recurring cost, which is what keeps you selling. First
  processing step arrives: **drying**. A dried cap is worth ~2× fresh and stacks
  the same. That's the first time a player *adds value* instead of moving it,
  and it's what gives the shuttle interior a purpose.
- **Hour 4–8 — the second world.** Buy a ship. Other bodies have different O2
  baselines, different native species, and different buyers. A dead moon is
  cheap land you can only farm under a dome. Cultivating a whole planet — enough
  trees to lift its baseline O2 — becomes a multi-hour project with a visible
  number attached.
- **Later — other product lines.** Sam's "and soon other things as well". The
  slots are obvious: crystals (already built and mineable), wood, water, and
  the fish that already have a market. Each is an existing system that becomes a
  *second commodity with its own buyers and prices* rather than a separate game.

That last point is the strategic one. **The way to shrink this game's scope is
not to delete systems — it's to make them all feed one economy.** Fishing isn't
a mini-game any more, it's a commodity. Mining isn't a mini-game, it's a
commodity. That's how twelve unrelated features become one game without
throwing any of them away.

---

## 7. What gets parked, and why

Parked = **still in the build, still working, not deleted, but nothing in the
first hour references it and no objective points at it.** Everything here can
come back as a content island once the spine is fun.

| System | Call | Why |
|---|---|---|
| Missions / story / notes / B-1 / Cold Company | **park** (already on hold) | It's a different game's spine. It will fight the economy for the player's attention. |
| Fishing (rod, dex, bag, cooking) | **park the activity, keep the buyer** | A whole second economy. The fish market NPC stays as a mushroom buyer. Fish return later as a *commodity*, not an activity. |
| Concerts, guitar, cassettes | **park** | Lovely, unrelated. |
| Black hole dimensions, backrooms, poolrooms | **park hard** | Enormous, brilliant, and from a different game. Leave them reachable for people who go looking. |
| Combat, enemies, pistol | **park for hour 1** | The first hour has no threat and doesn't need one. Bring enemies back as a *night hazard on the far worlds* — a reason to not be caught out at range. |
| Drone pilot school | **cut from the spine** | Buy the ship with money. Money is the game. |
| HAL | **reduce to hints** | Keep the voice, drop the story-awareness. |
| Photos app, gallery | **park** | |
| Phone | **KEEP — repurpose** | This is the biggest win on the list. The phone becomes Schedule 1's phone: Contacts (buyers + prices), Plots (your domes and plantings, growth timers), Map. |
| Ship market / fleet | **keep one ship** | The ship is the hour-3 unlock, not a collection. |
| Progression tracks / levels | **keep, retarget** | Retarget them at the economy: caps sold, credits earned, worlds cultivated, species discovered. |

**The test for anything asking to be in the first hour:** *does it put product
in the bag, take product out of the bag, or change how much product the land
makes?* If not, it's parked.

---

## 8. Build order

Ordered by leverage per hour of work. First three are the hour.

1. **Contacts app on the phone** *(new — biggest single win)*
   Buyers you've met, their price, their favoured species, time since last sale.
   The market becomes legible; the walk becomes a route.
2. **Buyer saturation + species preference** *(new, small)*
   Sits on `NPCMushroomPrice`. Prices sag when you flood a buyer, recover over
   ~10 min. Converts "find the best number" into "run a round".
3. **Plot readout — O2, growth rate, time to harvest** *(new, small)*
   Makes the trees→oxygen→growth chain visible. Without it the supply side is
   invisible and the player never finds the actual game.
4. **Pack upgrade in the goods vendor's shop** *(tiny — `ShopItem` exists)*
   Hotbar slot count becomes a variable. The hour-1 purchase.
5. **Hand-place a 5× mushroom by the ramp** *(minutes)*
   The thesis statement in the first three seconds of swinging.
6. **Retarget the objective/HUD text at the economy** *(cleanup)*
   Delete or park every objective pointing at fishing, piloting, missions.
7. **Drying rack** *(hour 2 — the first value-add)*
   Dried caps worth ~2×. Gives the shuttle interior a job.
8. **Dome as a purchasable "plot"** *(hour 2–3 — mostly wiring; domes exist)*

---

## 9. Risks, honestly

- **Foraging is more fun than farming, and farming is the plan.** Mitigated by
  depletion, but watch it. If planting feels like homework, the fix is to make
  cultivated mushrooms *better* than wild ones (bigger sizes, rarer species,
  denser packing) rather than to make wild ones scarcer faster.
- **The solar system is enormous and mostly empty.** Hour 1 must never leave
  walking distance. Resist putting anything interesting more than 5 minutes
  away until the player has a ship.
- **Species purity vs 7 slots.** 23 species will overflow the bag constantly.
  That's a good constraint *now* and a bad one at hour 5. The pack upgrades are
  the release valve; if it's still bad, cut the spawner's species list to 6–8 on
  the home planet and save the rest for other worlds. **Species per planet is a
  much better use of 23 species than 23 species in one field.**
- **Money curve.** ~120 credits per mushroom chopped is a lot. If hour 1 earns
  much past 3,500 the whole ladder compresses. Tune price down before touching
  yield — yield is where the feel lives.
- **The parked content is a temptation.** Every one of those systems will ask to
  come back. The rule that keeps this honest: *it comes back as a commodity or a
  hazard inside the economy, or it doesn't come back yet.*

---

## 10. Changes I'd make to what we just built

Small, from having just been inside it:

- **Tev's five free batches is too generous** now that the economy is real. Two
  refronts, then he's done. The joke lands the first time and gets thin.
- **Spores should be worth something to a buyer** (say 5 credits) so a player
  who doesn't want to farm can still cash them, and a player who does feels the
  cost of planting instead of selling.
- **Mushroom growth is pinned to the tree rate (90s) by request.** Once the plot
  readout exists and the O2 link is visible, mushrooms should probably be
  *faster* than trees again — trees are infrastructure, mushrooms are the crop,
  and infrastructure ought to be the slower investment.
- **The wild mushroom cap (20 visible) will feel thin once foraging is the
  activity.** Worth raising for the first hour and letting depletion do the
  limiting instead of the streamer.

---

## 11. The one-sentence version

*You are a broke colonist on a half-dead world where trees are the atmosphere;
you strip the land of mushrooms, sell them door to door at whatever each alien
will pay, and spend the money making more land able to grow — one plot, one
planet, one commodity at a time.*
