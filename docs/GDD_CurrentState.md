# Game Design Document — Current State

> **Purpose of this document:** A complete, accurate snapshot of everything that
> *currently exists* in the game. No planned features, no wishlist items — only
> what is built and playable right now. Once this is verified as 100% accurate,
> it becomes the baseline we build future features on top of.
>
> **Status:** DRAFT v6 — corrected day/night (planets rotate, ~30 min); concerts
> clarified as pole-based and night-gated.
> **Last updated:** May 30, 2026

---

## 1. High Concept

A solar-system-scale, open-world survival game. You crash-land on an alien
world with no memory of how you got there, and you survive — fishing, building,
mining, fighting — while a mystery slowly unfolds around who you are and why you
are here.

The hook is that the **solar system itself is the survival mechanic.** The
celestial bodies orbit under real n-body gravity. Day and night, orbital
position, gravity, and altitude are not backdrop — they are rules that gate when
and where you can do things. You travel seamlessly from a planet's surface, up
through the atmosphere, into space, and down onto another world.

- **Genre:** Open-world survival / exploration with a narrative mystery.
- **Tone reference:** Subnautica (vulnerable, exploration-driven survival).
- **Visual reference:** Low-poly, in the spirit of Outer Wilds.
- **Engine:** Unity.
- **Player count:** Single-player.

---

## 2. Narrative — Current Framing

The deeper story is intentionally **held back**. The current in-game experience
is built around mystery rather than exposition.

- The player wakes after crashing onto the planet **Humble Abode**, with no
  memory of how they got there.
- They are rescued by **Tev**, an alien, and wake up in **Tev's cabin**.
- Tev runs the player through the tutorial and teaches them how the world works.
- The lore — who the player is, why they crashed, what they are meant to do — is
  deliberately **not explained up front.** It is meant to be revealed later.
- A live narrative thread already exists through the **clone / death-count
  system**: the phone AI refers to the player as **"Astronaut Number N,"** where
  N is the player's total death count (see §6.4 and §11).

> **Note:** The full backstory (ORG, the stolen files, etc.) is being reworked
> and is *not* part of the current player-facing experience. It is tracked
> separately and intentionally left out of this current-state document. The AI
> companion's deeper story knowledge is technically gated so it cannot be
> revealed early (see §11).

---

## 3. The World

### 3.1 The Solar System
- The system is simulated under real **n-body gravity** at a fixed **100 Hz**.
- Orbital phase is **persistent** — a save resumes the system exactly where it
  was, with every body in the correct position.
- **All body names below are placeholders.**

| Body | Type | Notes |
|---|---|---|
| **The Sun** | Star | Center of the system (a celestial body with bodyType = Sun). Its gravity holds all the other bodies in orbit. |
| **Humble Abode** | Planet | Starting planet. Crash site, Tev's cabin, village, vendors, concerts. Has trees. |
| **Constant Companion** | Moon | Moon of Humble Abode. No trees. |
| **Cyclops** | Planet | Further out than Humble Abode. Has trees. |
| **Tumbling Bean** | Moon | Moon of Cyclops. No trees. |
| **Watchful Eye** | Moon | Moon of Cyclops. No trees. |
| **Fiery Twin** | Planet | Binary pair — revolves around Icey Twin *and* the sun with real physics. No trees. (Separate plan TBD.) |
| **Icey Twin** | Planet | Binary pair — revolves around Fiery Twin *and* the sun with real physics. No trees. (Separate plan TBD.) |

### 3.2 Day / Night Cycle
- **Planets rotate slowly** — roughly a **full rotation every ~30 minutes** — so a
  fixed point on the surface cycles through day and night as the planet spins (and
  orbits the sun).
- A body's **"night side" is the hemisphere currently facing away from the sun.**
- **Working night-detection logic exists:** the pole concerts use it to **only
  play at night** (see §10.2). This same logic is the basis for the planned
  sun-clock day/save system (tracked in the full development doc).
- **Night-side is dangerous:** enemies spawn on the dark sides of every planet
  and moon (see §9).

### 3.3 Seamless Scale
- The player can fly from a planet surface, up through the atmosphere, into
  orbit/space, and down onto other bodies **without loading screens.**
- A **floating-origin system** keeps the world numerically stable at large
  distances (the world shifts around the player rather than the player moving
  away from origin).

### 3.4 World Spawns (per body)
| Spawn | Where |
|---|---|
| **Crystals** (mineable fuel) | Every planet and moon. |
| **Mushrooms** (edible, cause a trip) | Every planet and moon. |
| **Randomly spawning aliens / NPCs** | Every planet and moon. |
| **Trees** (wood) | **Humble Abode and Cyclops only** — not on moons or the twins. |

> **Current content note:** Apart from Humble Abode, the other bodies currently
> contain only the random spawns above (crystals, mushrooms, aliens, plus trees
> on Cyclops). This is intentional — core systems were built first; bespoke
> per-body content and prefabs come in the next phase.

### 3.5 The Backrooms & Poolrooms (hidden levels)

A hidden, escape-room-style danger dimension reached by clipping into a planet.

- **Entry trigger:** if the player **glitches *into* a planet** (clips through the
  surface into its interior), they hit a **sphere collider inside the planet**
  that **sends them to the Backrooms.**
- **The Backrooms level** has:
  - an **exit** — find it to **escape with all your items intact**, and
  - an **entrance to the Poolrooms.**
- **The Poolrooms level** has **no way out.** If the player gets stuck there,
  they cannot escape.
- **Failure:** getting stuck (and dying) in these levels counts as a death —
  the player **loses their current astronaut** (ties into the clone / death-count
  system, see §6.4).

> **Interaction to confirm:** since normal respawn places the player *where they
> died* (§6.4), dying inside the Poolrooms would presumably respawn them still
> trapped. Confirm whether that's the intended dead-end, and exactly what "losing
> your last astronaut" means mechanically (a normal death that increments the
> count, or a harder game-over/run-ending state).

---

## 4. The Opening / Tutorial

1. Player crash-lands on Humble Abode.
2. Player wakes in **Tev's cabin**.
3. Tev teaches **movement** — WASD to move, Space to jump, mouse to look.
4. Tev gives the player an **axe** and teaches **tree-cutting** (gather wood).
5. Tev teaches **building** — using wood to build (e.g. a cabin).
6. By the end of the tutorial the player knows how to: move, build, drink water,
   catch and eat food, and talk to aliens.
7. Tev directs the player onward to **the village**, where the vendors are.

---

## 5. Core Gameplay Loop

1. **Survive** — keep health, hunger, and thirst up (hunger/thirst drain health).
2. **Gather** — fish for food/money, chop trees for wood, mine crystals for fuel.
3. **Trade** — sell fish to the fish vendor and space dust to wandering aliens.
4. **Buy & build** — purchase gear, ship parts, and ships; build structures.
5. **Travel** — fuel and power ships, fly between planets and moons.
6. **Explore** — discover the system, encounter enemies, find points of interest.

---

## 6. Survival Systems

### 6.1 Vitals
Three needs, each on a **0–100** scale (tracked by `ResourceManager`):
- **Health**
- **Hunger**
- **Thirst**

Hunger and thirst drain on timers. When a need hits **0** it drains health:
- **Hunger at 0:** −2 HP/s.
- **Thirst at 0:** −4 HP/s.
- **These stack** (both empty = −6 HP/s).

At **health ≤ 0** the player dies (see §6.4).

### 6.2 Water
- The player carries a **water bottle**.
- Fill the bottle at a **water source**.
- **Drink:** hold RMB + LMB. Restores thirst.

### 6.3 Food & Consumables
- **Fish**
  - **Eat raw:** put the fish in the hotbar and **right-click**. Eating raw fish
    produces a **visual effect** (a "trip").
  - **Cook:** cook at a **bonfire**, then eat (no trip effect).
  - Or **sell** to the fish vendor.
  - Hunger restored scales with fish rarity (see §7).
- **Mushrooms**
  - Spawn out in the world on planets and moons (like crystals and aliens).
  - Edible — eating them causes a **trip** (kaleidoscope-style visuals).

### 6.4 Death & Respawn
- **Clone / death count:** every death increments a persistent **total death
  count**. This is effectively the player's **clone number**, and the phone AI
  addresses the player as **"Astronaut Number N"** based on it. (This persists in
  the save; a new game resets it to 0.)
- **On death:** input freezes for ~2 seconds, then the player respawns. **There
  are no checkpoints or beds.**
  - If the player was **piloting a ship**, they respawn **just above that ship**.
  - Otherwise, they respawn **where they died**.
- **Respawn state is weak, not full:** **health 25 / hunger 10 / thirst 10**,
  velocity zeroed.
- **New game** starts at **100 / 100 / 100** with **0 deaths**.

---

## 7. Fishing & Fish

The economic and survival backbone.

### 7.1 Fishing
- Buy a **fishing rod** from the goods vendor.
- **Cast:** left-click. **Wait** for a bite. **Reel in:** left-click again.
- Caught fish can be eaten raw (right-click in hotbar), cooked at a bonfire, or
  sold to the fish vendor.
- Every caught fish is logged in the **FishingDex** app, with stats (see §10).
- A **fishing bag** can be bought that holds **5 fish** in only **1 hotbar slot.**

### 7.2 Fish Types
- **Three rarities:** **common**, **uncommon**, **rare**.
- Every fish has a **weight from 1 lb to 50 lb.**
- There are **no distinct fish species yet** — a caught fish is identified simply
  by its **rarity and weight**, and the **FishingDex logs it that way.**
- **Fish are sold by the pound.**
- **Rare** fish restore the most hunger and sell for the most per pound;
  **common** fish restore the least and sell for the least; **uncommon** sits in
  between.

---

## 8. Building

- Cut down **trees** with the **axe** to gather **wood** (Humble Abode & Cyclops).
- Build structures via the **building app** on the phone (see §10).
- **Bonfires** can either be **found and used** in the world, or **built** via
  the build menu; building one **costs wood**.
- Buildable structures include a **cabin** and a **bonfire**.

---

## 9. Combat & Enemies

- **All enemies spawn on the dark sides of every planet and moon, and are
  identical across all worlds.**
- **Zombies (common enemy):** scary zombies that **chase the player down.** If
  the player hides **on top of a tree**, they **back off and spit** at the player.
- **The big enemy:** has a **charge attack that lasts a couple of seconds.**
- **Killstreaks** are implemented and working.
- **Weapons:**
  - **Gun** — equip in the hotbar, shoot, **R to reload.**
  - **Axe** — melee (also trees and mining).
- The ship's **lebron light** doubles as a weapon: it clears dark areas and
  **kills enemies that come within range** (see §13.3).

---

## 10. The Phone

Open with **X**.

### 10.1 Apps
- **Camera app** — take photos.
- **FishingDex app** — logs every fish caught and their stats.
- **Building app** — open the build menu to place/build structures.
- **AI app** — talk to the AI companion (see §11).

### 10.2 Concerts
- There are **two concerts on Humble Abode — one on each pole** — which **only
  play at night** (driven by working night-detection logic; see §3.2).
- Aliens gather to listen and dance — a good place to **sell space dust** (aliens
  are gathered there).

---

## 11. The AI Companion

A locally-running AI that lives in the phone's AI app.

- **Conversational** and **remembers** conversations with the player.
- Addresses the player as **"Astronaut Number N"** (N = death/clone count).
- **Map / marker requests** — the player can ask it to mark or show things on the
  map; it validates targets against the real list of places (invalid → "not
  found").
- **Ship queries** — e.g. *where is ship 5 orbiting?* / *how much space dust is on
  ship 2?* Tracking requires that ship to have a **satellite dish**; no dish →
  the AI reports it can't track that ship.
- **Story-aware** — learns more about the story as the player progresses.
- **Gated knowledge** — deeper story knowledge is deliberately withheld until the
  right story point, so the mystery can't be extracted early.
- **Guardrails** — never invents ship names, fish, prices, or numbers; it only
  reports real game state.

---

## 12. NPCs & Aliens

### 12.1 Wandering Aliens (random spawns)
- Aliens **spawn randomly** on every planet and moon.
- On interaction, an alien **says a random voiceline.**
- The player can **try to sell them space dust** (collected by ship space nets in
  orbit — see §13). Space dust acts **like a drug** to them; they crave it.
- **Each alien wants a different amount and pays a different price.**

### 12.2 Fixed NPCs
- **Tev** — rescues the player, runs the tutorial.
- **Goods vendor, fish vendor, ship vendor** — see §13 economy.

---

## 13. Economy & Vendors

### 13.1 Currency
- **Cash**, earned primarily by selling **fish** (to the fish vendor) and **space
  dust** (to wandering aliens).

### 13.2 Vendors
- **Goods vendor** (village) — sells the **gun**, **axe**, **jetpack**, **water
  bottles**, **fishing rods**, and **fishing bag**.
- **Fish vendor** (village) — **buys raw fish** for cash (by the pound).
- **Ship vendor** (near the village, not inside it) — sells **fully built ships**,
  **half-built ships**, **bare hulls**, and **individual parts**.

### 13.3 Selling — Drag UI
- The **selling-fish panel** and the **cooking panel** share a similar layout: the
  player **drags the fish they want to cook/sell** out of the hotbar into the
  panel's slots.

---

## 14. Ships & Ship Building

### 14.1 Building a Ship
- A ship is built from a **hull + parts**.
- **Minimum to fly:** a hull plus **two thrusters.**
- Parts can be bought individually from the ship vendor and attached.

### 14.2 Ship Parts
| Part | Function |
|---|---|
| **Thruster** | Propulsion. Minimum two required to fly. |
| **Space net** | One left, one right. Gathers **space dust** over time while in orbit. |
| **Satellite dish** | Makes the ship **trackable on the map** (and queryable by the AI). |
| **Solar panel** | Recharges the ship's **power**. |

### 14.3 Two Separate Energy Systems
**A ship cannot fly if *either* system is at 0 — both ship power and crystal fuel
must be above zero.**

- **Ship Power (electrical)**
  - Recharged by the **solar panel**.
  - Powers the **"lebron light"** (clears dark, kills enemies in range). Running
    the light drains power, and **driving the ship** drains a bit of power too.
- **Crystal Fuel (propulsion)**
  - **Crystals** are the propulsion fuel.
  - Crystals **spawn on every planet and moon** and are **mined** by the player.
  - Mined crystals go into the ship's **reactor**.

### 14.4 Mining
- Currently done with the **axe** (the same axe used for trees).
- Hit a crystal until it breaks; it goes to the hotbar.

### 14.5 Ship Damage
- Crashing too hard causes **parts to physically detach and fly off.**
- The player must **track down detached parts** or **buy replacements** from the
  ship vendor.

### 14.6 Ship Storage
- Each ship has **storage chests.**
- Any item moves from the **hotbar** into **ship storage** by **dragging it in**
  (Minecraft-style).

### 14.7 Orbit
- Functional ships can be **sent up into orbit.**
- A ship in orbit with **space net(s)** gathers **space dust over time.**

---

## 15. Inventory & Items

### 15.1 Inventory Model
- **No separate inventory screen — just the hotbar (7 slots).**
- Items move between the hotbar and ship storage by **dragging** (Minecraft-style).

### 15.2 Stacking Rules
- **Stackable:** crystals, wood, space dust.
- **Single-slot (non-stacking):** everything else.
- **Fishing bag:** holds **5 fish** in **1 slot.**

### 15.3 Current Items & Gear
- **Axe** — trees, mining, melee.
- **Gun** — ranged (R to reload).
- **Jetpack** — movement boost. No fuel; uses a **cooldown** (can't be spammed —
  must recharge between boosts).
- **Fishing rod** — fishing.
- **Water bottle** — fill/drink water.
- **Fishing bag** — holds 5 fish in 1 slot.
- **Cassette tape** — legacy item; tradeable to an alien for a fishing rod. No real
  purpose currently; left in the game.

---

## 16. Points of Interest (current)

- **Tev's cabin** — crash recovery + tutorial.
- **The village** — goods vendor + fish vendor.
- **Ship vendor** — near the village.
- **The two concerts** (Humble Abode, after dark).
- **Crystal & mushroom spawns** — every planet and moon.
- **The Backrooms / Poolrooms** — hidden danger levels reached by clipping into a
  planet (see §3.5).
- **Water sources** — for refilling the bottle.

---

## 17. Technical Architecture (current)

> Included because these systems are core to the game's identity and scope.

- **N-body simulation** — celestial bodies orbit via `NBodySimulation` →
  `CelestialBody.UpdatePosition` at a fixed **100 Hz**. Persistent orbital phase.
  Fiery Twin & Icey Twin orbit each other and the sun.
- **Rotation / day-night** — planets **rotate slowly (~30 min per full
  rotation)**, producing real day/night cycles. Night-detection works in
  practice (the pole concerts only play at night), and this logic will be reused
  for the planned sun-clock day/save system.
- **Floating origin** — the world shifts around the player past a distance
  threshold; runtime physics objects must register with it.
- **Gravity alignment** — the player's up-vector aligns to the nearest gravity
  body each physics step.
- **`ResourceManager`** — scene-placed singleton. Tracks hunger/thirst/health
  (0–100) and ship power; owns death (`OnDeath`), `totalDeaths` (clone count),
  and the respawn sequence (~2s freeze, respawn at 25/10/10, no checkpoints).
- **Save / load** — body-relative positions, strict apply order; persists
  vitals, total deaths, and exact orbital phase. `isDead` is transient (not
  saved). New-game reset = 100/100/100 + 0 deaths.

---

## 18. Controls Reference (current)

### 18.1 On Foot
| Action | Input |
|---|---|
| Move | WASD |
| Jump | Space |
| Look | Mouse |
| Interact | F |
| Open phone | X |
| Cast / reel fishing rod | Left-click |
| Eat raw fish (from hotbar) | Right-click |
| Drink water | Hold RMB + LMB |
| Shoot (gun equipped) | Left-click |
| Reload | R |

### 18.2 Jetpack (after purchase)
| Action | Input |
|---|---|
| Boost up | Space (again, while mid-air) |
| Boost down | Ctrl |
| Directional boost | Shift + WASD |

### 18.3 Ship Flight
| Action | Input |
|---|---|
| Move | WASD |
| Ascend | Space |
| Descend | Ctrl |
| Double boost | Shift (doubles whichever direction is held) |

---

## 19. Verification Status

All previously open questions are now resolved:
- Gun fires on **left-click** — confirmed.
- The **Sun is one of the 8 bodies**; its gravity holds the others in orbit — confirmed.
- Fish are labeled by **rarity + weight only** (no species yet) — confirmed.

**Next step:** full read-through by the developer to flag anything still missing
or inaccurate. Any corrections from that pass become v5, after which this is
treated as the locked, verified baseline.

---

## Parking Lot — Explicitly Planned (NOT yet built)

Captured here only so they aren't lost. **Not** part of the current state:

- **Bespoke per-body content & prefabs** — unique terrain/POIs/features for each
  planet and moon (next major phase).
- **Random side quests** — e.g. an NPC asks the player to *catch 5 fish* for a
  cash reward; on first talk the player can **accept or decline.**
- **Pickaxe** — sold by the goods vendor, for mining crystals (axe does it now).
- **Expanded combat** — more depth/variety wanted in future.
- **Fish species** — distinct named fish types and more variety (currently fish
  are only rarity + weight).
- **Fiery Twin / Icey Twin** — a distinct plan for the binary pair (TBD).

---

*End of current-state document. Once verified, future/planned features will be
added in a separate section so scope stays clear.*
