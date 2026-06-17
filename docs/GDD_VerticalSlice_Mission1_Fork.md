# Vertical Slice — Mission 1: "Taken In" (Three-Way Fork)

> **What this is:** The full build spec for Mission 1 of the vertical slice — the
> game's opening, restructured as a player-chosen three-way fork (**Pilot / Build /
> Fish**) routed through a conversation with Tev. Written to hand directly to an
> implementer (Claude Code) with no other context required, though it cross-references
> the canon docs (`GDD_Full_Development.md` v10, `GDD_Lore_OriginOfORG.md` v3) by section.
>
> **How to read the tags:**
> - **[EXISTS]** — already built and live; integrate, don't rebuild.
> - **[BUILD]** — new work required for the slice.
> - **[AUTHOR]** — content/writing (dialogue lines), not code.
> - **[TEST]** — a verification step that must pass before moving on.
> - **[STUB]** — design is recorded here but it is **out of slice scope**; wire a
>   placeholder/lockout for now, build later.
>
> **Slice build target:** the **PILOT branch only**, content-complete. Build and Fish
> are fully designed below but **stubbed** for the slice (§7–§8). The shared opening
> (§2–§5) is built once and is used by all three branches.
>
> **Status:** DRAFT v1 — Mission 1 three-way fork. **Date:** June 6, 2026.

---

## 0. Context for the implementer (read first)

Single-player Unity solar-system survival-horror game. The player crash-lands with
amnesia, is taken in by an alien named **Tev** (secretly one of ~20 rebels — GDD §5.0),
and survives (fishing, cooking, building, mining, flying) while a slow mystery unfolds.
A **phone AI** acts as a helpful guide; it is secretly ORG's knowing-deceiver handler
(GDD §6) — but in Mission 1 it stays light and friendly. Mission 1 is **Phase 1, Tev's
first main mission** (GDD §5.1). It is deliberately gentle and short (GDD §1.5): survive,
bond with Tev, and pick how you want to begin. The heavier file-hunt framing and the
first choice with teeth (DP1) come in Mission 2.

**The slice rule for this mission:** build the shared opening (§2–§5) and the **Pilot**
branch (§6) end-to-end. **Stub Build (§7) and Fish (§8)** — present them in Tev's menu
but lock them with a short "maybe later" line, or hide them, so the slice has exactly one
playable path. The three-branch design is recorded now because the *choice scaffolding*
(§5) is reusable and worth building once; the *content* of Build/Fish is deferred.

**Why Pilot is the one to build:** it is the only branch that feeds the critical path.
Mission 2 is the Cyclops run, which needs a ship, and the Pilot branch ends with you
flying a real ship to Constant Companion — so it hands straight into M2. (Build and Fish
players never learn to fly; the full game solves this with a convergence beat — §9 —
which the slice doesn't need yet.)

---

## 1. Mission shape (the loop)

```
CRASH → SURVIVE (marinate: food / water, no shelter) → VILLAGE → meet TEV
   → "go explore, then tell me what you learned"
   → EXPLORE (2–3 discoverables) → RETURN & REPORT
   → TEV: "what do you want to do now?"  →  [ PILOT | BUILD | FISH ]
   → branch plays out → resolve + Tev trust ↑ → (Pilot) fly to Constant Companion → Mission 2
```

The explore → report → choose loop is the reusable spine. Build it once; all three
branches sit on the far side of the same menu.

---

## 2. Beat A — Crash & Survive  *(shared; all branches)*

The opening. Keep the player alive and curious before anything is explained.

- **[EXISTS]** Crash-land cold open; player has amnesia (GDD §1.2). Player spawns with
  the suit, the phone, and the survival HUD.
- **[EXISTS]** Survival loop: oxygen (suit tank / hull tank), hunger, thirst; catching
  food, eating, drinking water. This is the **marinate** stretch — let it breathe.
- **[BUILD]** Soft-gate the opening so the player **cannot build a shelter yet** — the
  build tutorial is deferred into the Build branch (§7). Until the village, survival is
  just food + water + not dying. *(This is the change from the old flow, on purpose:
  don't explain building so early; let food/water marinate first.)*
- **[EXISTS / integrate]** Phone AI boots shortly after the crash. In Mission 1 it is
  **only** the friendly guide: the hardcoded tips (low health, enemy nearby, entering/
  exiting atmosphere) and the **"Astronaut N"** death line (GDD §2.6 — Layer 1 of the
  reveal, plays as merely eerie). **Keep ORG's file-framing to a whisper here** — at most
  one soft line ("there's something we'll need to track down… but first, get your feet
  under you"). The real tasking escalates in M2. *(Matches the "don't explain too early"
  intent and GDD §4.4's soft-sell.)*
- **[TEST]** Player can survive the trek to the village on food/water alone, with no
  shelter, without hitting a dead end.

**Assumption to confirm (see §11):** the marinate stretch is the **trek from the crash
site to Tev's village** — rescue and village are the same arc, not two separate places.

---

## 3. Beat B — The Village & Tev  *(shared)*

- **[BUILD]** The village (small; a handful of structures + Tev's cabin). Tev's cabin
  holds the **sun clock** (GDD §2.1) and is the player's safety anchor **in place of a
  built shelter** for now.
- **[AUTHOR]** First Tev conversation: he found you in the wreck and took you in (GDD
  §5.0, kept warm — he reads as a kind stranger; the rebel reveal is far off).
- **[AUTHOR]** Tev's hook line: **"Go have a look around, then come back and tell me what
  you found."** This sets the explore beat and, quietly, the rhythm of the whole game
  (see §4 design note).

---

## 4. Beat C — Explore & Report  *(shared; the reusable rhythm)*

- **[BUILD]** Seed **2–3 discoverables** in the starting zone so "tell me what you
  learned" has real substance and looking around is rewarded. Suggestions: a vista with
  Constant Companion overhead, a strange structure, a creature or a fishing spot.
  **[AUTHOR]** a short "observed it" line for each.
- **[BUILD]** Return-and-report dialogue with Tev that **branches lightly on what the
  player actually saw/did** (found the structure → Tev says X; only fished → Tev says Y).
  Lightweight — flavor and bonding, not a gate.
- **DESIGN NOTE — build this on purpose:** this explore → report → choose loop teaches the
  player, at zero stakes on day one, that **conversations with Tev are where you make the
  calls that steer the game.** That is the exact shape of the endgame (GDD §5.3 — you, Tev,
  a conversation, a branching choice). Planting the rhythm here makes the private meeting
  later land like a callback the player can't quite name. Treat the report + menu as the
  **first instance of a recurring pattern,** not a one-off.

---

## 5. Beat D — "What do you want to do now?"  *(shared scaffolding — build once)*

- **[BUILD]** After the report, Tev offers **three choices**:
  1. **Learn to pilot a ship** → §6 (Pilot)
  2. **Build your own humble abode (cabin)** → §7 (Build)
  3. **Go on a fishing trip** → §8 (Fish)
- **[BUILD]** This menu + the report scaffolding (§4) is a **single reusable choice
  system** (ties to GDD §8 Tier 0 #2, the dialogue/choice system). Build it once.
- **[BUILD]** Completing **any** branch raises **Tev trust** (GDD §5.2). In the full game
  the player can return and do the others; each is one of Tev's early trust-builders.
- **SLICE SCOPE:** wire choice **1 (Pilot)** to its full content. For **2 and 3**, hide
  them or show them disabled with a one-line "let's save that for later" (so the menu still
  demonstrates the pattern). See §7–§8.

---

## 6. Branch 1 — PILOT — [SLICE BUILD TARGET] *(content-complete)*

The player learns to fly and ends Mission 1 in space, handing into Mission 2.

**6.1 Pilot school — drones** *[BUILD] (controls [EXISTS])*
- The player trains on **small drones that mimic the real ship's flight controls** (reuse
  the existing ship control scheme). A safe, low-stakes sandbox to learn pitch/yaw/throttle
  before risking a real ship.
- **[BUILD]** A short course: a few gated drills (take off, fly through gates/waypoints,
  land on a pad). Pass/fail with retry, no death.
- **[AUTHOR]** Instructor lines — Tev, or a dedicated flight-instructor NPC (your call;
  keeping it Tev preserves the bond focus — see §11).

**6.2 The galactic license** *[BUILD]*
- On completing the drills the player **earns their galactic pilot license** — a clear
  "you passed" beat / item / flag that unlocks real-ship piloting.
- **[TEST]** License flag gates boarding the real ship (can't fly the ship until licensed).

**6.3 The real ship → fly to Constant Companion** *[BUILD] (ship & flight [EXISTS])*
- The player boards the **real ship** and flies it for the first time.
- **First real flight:** lift off Humble Abode and make the **seamless surface-to-space**
  trip to **Constant Companion** (its moon) — the no-loading-screen scale moment, now in
  the player's own hands. (Floating-origin / seamless transition and n-body flight already
  exist — integrate.)
- **[AUTHOR]** A short Tev bond beat around departure — a radio check-in or a few words at
  the cabin before you go ("first time off the ground — you'll be fine"). Mainly character.
- **[BUILD]** Arrival at Constant Companion is the **Mission 1 → Mission 2 hand-off**:
  Tev's next task (the Cyclops run, M2) opens from here.

**6.4 On completion** *[BUILD]*
- **Tev trust ↑.** Player is now ship-capable — the prerequisite M2 assumes.
- **[TEST] End-to-end:** crash → survive → village → Tev → explore → report → choose Pilot
  → drones → license → real ship → land on Constant Companion → M2 trigger fires. This is
  the slice's Mission 1 success condition.

---

## 7. Branch 2 — BUILD  *(DESIGN — [STUB] for the slice)*

*Full design recorded; build later. For the slice, lock or hide this option.*

- **Premise:** the player chooses to make their **own humble abode** — this is where the
  **deferred shelter** (§2) now lives. Tev's cabin covered safety until now; this branch is
  the player graduating to their own.
- **[FULL-GAME] Building tutorial:** teach the build system (already exists) by having the
  player **place a required set** — walls, some floors, a table, a chair. Simple placement
  checklist; completes when the basics are down.
- **[AUTHOR][FULL-GAME]** Tev bond beat: he comes to see the finished cabin / comments on
  it. Mainly character.
- **On completion (full game):** Tev trust ↑; player has a personal base.
- **Convergence (full game, §9):** a build-path player still can't fly — before M2 they hit
  the short "let's get you a ship" beat.
- **[STUB] Slice action:** disable/hide this menu option; no build content wired.

---

## 8. Branch 3 — FISH  *(DESIGN — [STUB] for the slice)*

*Full design recorded; build later. For the slice, lock or hide this option.*

- **Premise:** a fishing trip with Tev — the most character-forward branch.
- **[FULL-GAME] The trek:** the player **walks a fair distance** to a fishing bank.
- **[FULL-GAME] The fishing bag:** Tev gives the player a **fishing bag** (increased fish
  carry capacity) — a tangible reward and a reason to pick this branch.
- **[FULL-GAME] The goal:** catch **3 of each kind** of fish in the area (ties to the
  existing FishingDex).
- **[AUTHOR][FULL-GAME]** Meet **Tev on the fishing bank** and fish together with a
  **bonding conversation** — explains a little, but mainly **builds the bond with Tev.**
  This is the emotional heart of the fish branch.
- **On completion (full game):** Tev trust ↑.
- **Convergence (full game, §9):** same as Build — a short "get a ship" beat before M2.
- **[STUB] Slice action:** disable/hide this menu option; no fish-trip content wired.

---

## 9. The convergence problem  *(full game — NOT the slice)*

In the full game, a player who picked **Build** or **Fish** never learned to fly — but
**Mission 2 (Cyclops) needs a ship.** Solution: all three branches converge on a short
**"let's get you a ship / a quick flying lesson"** beat before the Cyclops run, so every
path arrives at M2 ship-capable. (The Pilot branch satisfies this inherently.)

**In the slice this problem does not exist** — only Pilot is wired, and Pilot ends
ship-capable on Constant Companion. Do **not** build the convergence beat for the slice.

---

## 10. Slice build checklist  *(Pilot path only)*

Shared opening:
- [ ] **[BUILD]** Soft-gate: no shelter-building before the village (§2).
- [ ] **[BUILD]** Village + Tev's cabin (sun-clock anchor) (§3).
- [ ] **[BUILD]** 2–3 explore discoverables + "observed it" lines (§4).
- [ ] **[BUILD]** Report-to-Tev dialogue (light branch on what you saw) (§4).
- [ ] **[BUILD]** "What now?" three-choice menu — reusable scaffolding (§5).
- [ ] **[AUTHOR]** Tev's village intro + hook line + report responses + menu lines.
- [ ] **[INTEGRATE]** Phone AI: tips + Astronaut-N live; ORG framing kept to a whisper (§2).

Pilot branch:
- [ ] **[BUILD]** Drone pilot-school drills (reuse ship controls) (§6.1).
- [ ] **[BUILD]** Galactic license flag → gates real-ship boarding (§6.2).
- [ ] **[BUILD]** First real-ship flight: Humble Abode → Constant Companion (§6.3).
- [ ] **[AUTHOR]** Departure bond beat with Tev (§6.3).
- [ ] **[BUILD]** Constant Companion arrival → Mission 2 trigger (§6.3).
- [ ] **[BUILD]** Tev trust ↑ on completion (§6.4).

Stubs:
- [ ] **[STUB]** Build + Fish menu options hidden/locked with a "later" line (§7–§8).

Gate:
- [ ] **[TEST]** Full run: crash → … → land on Constant Companion → M2 fires (§6.4).

---

## 11. Open / assumptions

- **Cabin-in-village assumption:** this doc assumes the rescue and the village are the same
  arc — the marinate stretch is the trek from the crash to Tev's cabin in the village. If the
  rescue spot and the village are **separate places,** flag it; it changes where Mission 1
  opens (§2–§3).
- **Where ORG's file-framing first lands:** kept to a whisper in M1 by design (§2). Confirm
  you want the first explicit "find the files" tasking to land in **M2**, not here.
- **Pilot-school instructor:** Tev vs. a dedicated flight-instructor NPC (§6.1) — Tev keeps
  the bond focus; an NPC frees Tev for the menu/report beats. Your call.
- **Tev trust values:** exact trust granted per branch is a balancing pass once the three
  trust meters exist (GDD §5.2).

---

*End of v1 — Mission 1 three-way fork. Slice target: Pilot branch (§6). Next: build the
shared opening + Pilot, then spec Mission 2 (the Cyclops run).*
