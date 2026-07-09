# Vertical Slice — Main Mission 1: "Cold Company"

> **What this is:** The full build spec for the first main mission — the moon run to
> Constant Companion — that begins once the player has earned their pilot license in the
> opener ("Taken In"). Written to hand to an implementer (Claude Code) with the canon docs
> (`GDD_Full_Development.md`, `GDD_Lore_OriginOfORG.md`) as background. This mission is the
> first beat of the four-main-mission vertical slice.
>
> **Tag legend:**
> - **[EXISTS]** — already built and live; integrate, don't rebuild.
> - **[BUILD]** — new code/systems work for the slice.
> - **[AUTHOR]** — content/writing (dialogue, clue text), not code.
> - **[INTEGRATE]** — wiring existing systems together.
> - **[TEST]** — a verification step that must pass before moving on.
> - **[OPEN]** — an unresolved decision for Sam.
>
> **Status:** DRAFT v1 — Main Mission 1 "Cold Company." HANDOFF — ready to build.

---

## ⚑ For Claude Code — read this first (division of labor)

**Hard rule: Sam places every GameObject. You place nothing.** Sam positions all scene
objects by hand so they sit exactly right in the world. Your job is everything *code* —
scripts, wiring, StoryDirector dialogue nodes, mission-state flags, triggers, gates, and
backend. You do not create, move, or position scene objects.

**Workflow, in strict order:**

1. **Read** this spec + the canon docs (`GDD_Full_Development.md`, `GDD_Lore_OriginOfORG.md`).
2. **Request placement first.** Before writing *any* code that references scene objects,
   present Sam the **Placement Manifest (§8)** — every GameObject to create, with the exact
   name your code will reference. Ask Sam to create and position each one with that name.
3. **Stop and wait.** Do **not** write scene-referencing wiring until Sam confirms the
   objects are placed and hands back the final names/positions. Sam's placed names win — if
   Sam renames anything, use the confirmed name.
4. **Then wire everything.** Once placement is confirmed: scripts, interactables, triggers,
   the license gate, StoryDirector nodes, the death→black-hole-growth hook, Tev trust
   increment, completion flags, and the Main 2 unlock.
5. **Dialogue** in §2 and §4 are drafts Sam may redline. Wire them as authored StoryDirector
   nodes; if wording is unclear or you'd change it, ask before finalizing — don't rewrite
   silently.
6. **Run the §7 test gate** and report what passed.

**If you need a GameObject that isn't in the manifest, do not create it — pause and ask Sam
to place it, then continue.** When in doubt about placement vs. wiring: if it exists in the
scene, it's Sam's; if it's logic, it's yours.

---

## 0. One-paragraph summary

The player has their pilot license. They return to Tev, who — grounded after crashing on
his own reckless Cyclops run — sends the player as his scout to check the abandoned base on
the moon (Constant Companion) for signs of the rebels. The player takes their first real
self-piloted flight to the moon, explores the base, and finds it evacuated in a hurry: a
dated wall of photographs tracking the growing black hole, and a file left on a table that,
when opened, holds a surveillance photo of the player's own crashing pod marked **ORG???**
in red. The rebels caught the player's arrival on radar, mistook them for ORG, and fled. The
handler AI calmly explains ORG away as a galactic force for good — **the first lie.** A
scrubbed nav-route points outward. The player reports back; Tev's own dead-end (an
abandoned, sealed base he found at Cyclops before he was grounded) snaps into place against
the player's evidence, and the two of them deduce the rebels migrated to **Cyclops** — the
seed for Main Mission 2.

---

## 1. Entry & prerequisites

- **[EXISTS]** Opener "Taken In" and its Pilot branch: cold open → survive → village → meet
  Tev → explore → report → three-way fork → **Pilot path built** (drone school → galactic
  pilot license → real ship). Build/Fish branches become the optional missions (separate
  specs).
- **[BUILD]** **License gate.** Main 1 does **not** open until the player holds the galactic
  pilot license flag. If the player talks to Tev without it, he defers with a short line
  (§2.1). This is the intended reason a Build/Fish player can't start Main 1 yet — the
  license is the key.
- **[INTEGRATE]** The player's real ship + `ShipLaunchPad` on Humble Abode (from the opener)
  are the departure point. Moon base on Constant Companion **[EXISTS]** — dress it as the
  evacuated hideout (§4).

---

## 2. Beat 1 — The assignment (Tev, at the village)

**Trigger:** player returns to Tev holding the pilot license.

### 2.1 [AUTHOR] Tev's framing — why it has to be you

Tev gives the assignment *and* his backstory up front, so the report-back deduction (§6)
can pay off. Keep it warm, a little self-deprecating; he's reckless, not heroic.

Representative lines (redline freely):

- **Hook:** "You've got your wings now. Good — because I need someone who can still legally
  use theirs."
- **His grounding:** "I flew out to Cyclops a while back. Looking for… I don't know what.
  Signs of these rebels people keep whispering about. Found an old base out there — sealed
  up, nobody home. Came back with nothing but a busted ship and a hole in the Henderys'
  shack." *(beat)* "Village decided I don't need to be flying for a while."
- **The culture line (worldbuilding):** "Nobody really flies around here anymore. Too many
  accidents. Folks just… stay put. Watch the concerts. Wait for nothing." *(This explains
  the empty skies and makes Tev the odd one out.)*
- **The ask:** "There's a base on the moon — Constant Companion. If these rebels are real,
  that's where I'd start looking. I can't go. You can. Scope it out, tell me what you find."

> **Design note:** Tev is **not** a rebel and does **not** know what ORG really is (revised
> canon). He is a fellow outsider, suspicious of the rebels, working *with* the player to
> find them. He genuinely believes he's helping the player help him. This keeps the Main 3
> reveal in the rebels' hands, not Tev's — flagged for later, no impact on Main 1.

### 2.2 [INTEGRATE] Handler cue

- **[AUTHOR]** As the player accepts, the phone buzzes once (existing cue) and the handler
  logs the objective in its helpful register — "Recon the lunar structure. Report anything
  relevant to the files." Keep ORG framing to a whisper here; the mission is "find the
  stolen files," nothing sinister yet.

---

## 3. Beat 2 — The flight (Humble Abode → Constant Companion)

- **[EXISTS/INTEGRATE]** First real self-piloted flight. Reuse floating-origin seamless
  surface-to-space + n-body flight. This is the wonder beat: no loading screen, ground to
  moon in the player's own hands.
- **[BUILD]** **Black hole visible from the moon.** On approach / on the lunar surface, the
  black hole is visible in the sky (skybox / world object). This is the *real* one; the
  photo wall (§4.2) is its *documented history*. Seeing both is the intended one-two.
- **[BUILD/INTEGRATE]** **Death → black hole growth (core mechanic, established here).**
  Every player death widens/destabilizes the black hole, visibly. If the player dies en
  route or during the mission, the sky-object reflects it on next view. The player is meant
  to *notice this themselves* over the slice, long before they understand it. Author no
  explanation — the mechanic teaches itself.
- **[BUILD]** `Moon_LandingZone` + `Moon_ArrivalTrigger` **[EXISTS]** from opener — fire
  mission-state "arrived at base."

---

## 4. Beat 3 — The base (the clues)

The base reads **evacuated in a hurry.** Soft-gate the clue order so the emotional climax
(the pod file) lands late. Suggested flow: environmental "they fled" reads → the photo wall
→ the review station → **the file on the table (pod / ORG??? + the handler's first lie)** →
the scrubbed route (§5).

### 4.1 [BUILD/AUTHOR] "They left in a hurry" — make it noticeable

Environmental storytelling, no reading required. Dial these up so it's unmistakable:

- Meals abandoned mid-eaten (vacuum-preserved on the airless moon — a nice diegetic reason
  they look fresh).
- Bunks dented, personal effects half-packed, a container dropped and spilled in a doorway.
- Something still running / a light still on. Nobody planned to leave.
- **[AUTHOR]** Handler narrates warmly as the player enters: "Looks like they left in a
  hurry. Whole place, just… dropped." Helpful, unbothered.

### 4.2 [BUILD/AUTHOR] The black hole photo wall (foreshadowing centerpiece)

- A pinned **dated sequence** of photographs of the black hole — same framing, spaced over
  time — each one showing it **wider than the last.** A hand-scrawled tally / worried
  annotation climbs the margin.
- **Canon it encodes (never stated):** the rift opened a few years ago when ORG created the
  dimensional store for its computer/observer; it's unstable and worsening. The rebels
  documented it as an unexplained omen. It rhymes forward to the death-growth mechanic
  (§3) and to the ending (the black hole the player finally enters to reach the tank).
- **[AUTHOR]** One rebel annotation, oblique: e.g. "Still growing. Nobody will say why."
  Fear without fact.

> **Continuity note:** growth is now tied to *death*, so the wall must read as growth the
> rebels witnessed **before the player arrived** (baseline rift instability / other
> observers over years). The player's deaths *accelerate* an already-widening wound — the
> photos and the mechanic tell one continuous story. Keep dates pre-arrival.

### 4.3 [BUILD/AUTHOR] The review station (the files trace — oblique)

- A terminal where the rebels were poring over stolen material. Player gets **metadata
  only**, never the files (canon: files are never collectible; they appear at Tev's/rebels'
  reveal later). A redacted directory tree, frantic annotations, one dull-sounding folder
  name that lands like a stone later.
- **[AUTHOR] Handler tell (subtle):** the AI is a hair *too* smooth here — steering the
  player on-task, incurious about the truth-adjacent material. Nothing catchable live.

### 4.4 [BUILD/AUTHOR] The file on the table — the pod, ORG???, and the first lie

**The climax of the base. A deliberate player action — the file sits on a table; the player
chooses to open it.** (Per Sam: gate this to land last.)

- **[BUILD]** Interactable file/folder object on a table. Opening it displays a
  surveillance photo: **the player's own pod, crashing into Humble Abode**, caught on the
  rebels' camera/radar — with **ORG???** scrawled across it in red.
- **The reveal (player-side):** the player *knows* this is their crash (they lived the cold
  open). The deduction lands: *the rebels saw me come down, thought I was ORG, and ran.* The
  irony now reads as "they panicked over nothing." On replay it reads as "their fear was
  correct" — foreshadowing, protected.
- **[AUTHOR] The handler's first lie — a comfort, not a claim.** Immediately after the
  player opens the file, while they're unsettled, the AI reassures them, calm and kind:
  - It explains ORG as a **galactic force for good** — an organization that helps solar
    systems, keeps the peace, and that the rebels simply *mistook* the player for.
  - Delivered soothingly, unprompted, never defensive. The lie enters through *relief*, so
    the player exhales and moves on without scrutiny.
  - **What the player knows:** they're hunting stolen files; the AI is here to help. **What
    the AI knows (never said):** ORG built it, the "files" are ORG's, this is the leash's
    first pull. Do **not** tip the tone into justification — the moment it sounds like it's
    defending ORG, the player's guard goes up.

  Representative handler lines (redline): "Oh — that's you. Your pod." *(warm)* "They must
  have seen you coming down and panicked. Understandable. Out here, people are frightened of
  ORG — but you have nothing to worry about. ORG helps systems like this one. The rebels
  simply didn't know what they were looking at." *(beat)* "Neither did they, clearly. Come
  on — let's see where they went."

### 4.5 [OPEN] Optional depth (build if time; skippable)

- **The keepsake** — a small personal item a rebel wouldn't abandon unless terrified (a
  carved token, a looping recorded lullaby to an empty room). Makes them *people*; rhymes
  with the player's homesickness.
- **The censored scan** — an image with a region blacked out, circled and question-marked in
  the rebels' hand: witnessing them hit a wall of truth without seeing through it.

> **[DECIDED] The player keeps nothing.** All clues — the pod photo, the photo wall,
> keepsakes — **stay at the base**; they are read/viewed in place and left behind. No clue
> becomes an inventory item. Do not build any "pick up / carry" behavior for clues.

---

## 5. Beat 4 — The pointer (the scrubbed route)

- **[BUILD/AUTHOR]** A partial nav-plot / star-chart with the origin wiped but one
  downstream leg still legible, pointing **outward.** The read: they didn't just flee, they
  fled *toward* something.
- Do **not** name Cyclops here. The player carries a *heading*; Tev's knowledge (§6) is what
  turns the heading into a destination. Deduction, not signpost.
- **[BUILD]** Collecting the route + opening the pod file are the two required completion
  conditions for the base; firing both enables "return to Tev."

---

## 6. Beat 5 — Report back → Cyclops seeded

**Trigger:** player returns to Tev on Humble Abode.

- **[AUTHOR]** Player reports the findings (light dialogue; the evidence does the work): they
  left fast, they were tracking the sky, and they ran outward.
- **[AUTHOR] The two-part deduction (the payoff of §2.1):** Tev connects it — "Empty base,
  left in a hurry, heading out… that's funny. When I made it to Cyclops, there was a base
  out there too. Abandoned. Sealed up tight — I couldn't get in." The player's fresh
  evidence turns Tev's old dead-end into the answer: *the rebels didn't vanish, they
  relocated — and Cyclops is where they went.* Neither could solve it alone.
- **[BUILD]** **Tev trust ↑** on completion.
- **[BUILD]** Set mission-state: Main 1 complete, Main 2 (Cyclops) available. Tev's next
  ask (getting the player out to Cyclops) opens from here.

---

## 7. Completion & test gate

- **[TEST] End-to-end:** license held → talk to Tev → assignment + backstory → fly to
  Constant Companion → base clues (rushed-leave + photo wall + review station + **open the
  pod file → handler first-lie**) → scrubbed route → return to Tev → deduction → Cyclops
  unlocked, Tev trust ↑. This is Main 1's success condition.
- **[TEST]** License gate: talking to Tev *without* the license does **not** start Main 1.
- **[TEST]** Death → black hole growth is visible from the moon after a death.

---

## 8. Placement Manifest & wiring split

### 8.1 SAM PLACES THESE (Claude Code: request first, then wait)

Create and position each GameObject in the Constant Companion base scene with the exact
name in the **Object name** column — Claude Code's wiring references these names. If Sam
renames, the confirmed name wins. **Claude Code places none of these.**

| Object name | Type | Where | Notes |
|---|---|---|---|
| `MoonBase_Root` | [EXISTS] | Constant Companion | Existing base; dress as evacuated hideout. |
| `Moon_ArrivalTrigger` | [EXISTS] | Base approach | Fires "arrived" state; confirm still present. |
| `BaseEntry_Trigger` | Trigger vol. | Base entrance | Fires handler "left in a hurry" narration. |
| `Clue_PhotoWall` | Interactable | Base interior | Dated black-hole photo sequence + annotation. |
| `Clue_ReviewStation` | Interactable | Base interior | Terminal; redacted metadata, no readable files. |
| `Clue_PodFile` | Interactable | On a table, deeper in | The pod / **ORG???** file. **Gated to land last.** |
| `Clue_ScrubbedRoute` | Interactable | Base interior | Outward nav-heading; a required completion item. |
| `BlackHole_MoonView` | Sky/world obj | Visible from moon | The real black hole; feeds the death-growth hook. |
| `RushedLeave_Props` (group) | Props | Base interior | Meals, bunks, spill, light-left-on. Dressing only. |
| `Clue_Keepsake` *(optional)* | Interactable | Base interior | Optional depth; skippable. |
| `Clue_CensoredScan` *(optional)* | Interactable | Base interior | Optional depth; skippable. |
| `Tev_Village` | [EXISTS] | Village | Existing Tev NPC; new dialogue nodes attach here. |

> Anything not in this table that the build turns out to need: **Claude Code pauses and asks
> Sam to place it** with a proposed name, then continues.

### 8.2 CLAUDE CODE WIRES THESE (after placement is confirmed)

- [ ] **[BUILD]** License gate — Main 1 won't start without the pilot-license flag (§1, §2.1).
- [ ] **[BUILD]** Mission-state machine: assigned → arrived → clues → route → report → complete.
- [ ] **[BUILD]** Interactable logic for `Clue_PhotoWall`, `Clue_ReviewStation`,
      `Clue_PodFile`, `Clue_ScrubbedRoute` (+ optional clues).
- [ ] **[BUILD]** Soft-gate so `Clue_PodFile` resolves **last** (photo wall + review station
      seen, or proximity-gated) — the first-lie beat must land at the climax (§4.4).
- [ ] **[BUILD]** Completion condition = pod file opened **AND** scrubbed route collected →
      enable "return to Tev."
- [ ] **[INTEGRATE]** Death → `BlackHole_MoonView` growth/instability hook (§3); visible on
      next view after any death. No dialogue.
- [ ] **[INTEGRATE]** First self-piloted flight (existing floating-origin / n-body); arrival
      trigger fires mission state.
- [ ] **[AUTHOR→WIRE]** StoryDirector nodes: Tev assignment + backstory + culture line (§2);
      report-back deduction (§6); handler objective log, base narration, **first-lie comfort
      beat** (§4.4).
- [ ] **[BUILD]** Tev trust ↑ on completion; set Main 1 complete + Main 2 (Cyclops) unlock.
- [ ] **[TEST]** Run the §7 gate.

---

## 9. Resolved / deferred

1. **Keepsake item — [DECIDED]:** the player keeps nothing. All clues stay at the base,
   read in place (§4.5). No inventory/carry behavior for clues.
2. **Handler presence dial — [DECIDED]:** warm/helpful throughout, with two subtle tells
   (review-station smoothness + the first-lie comfort). This is the intended amount of leash
   for Main 1 — build as specced.
3. **Report-back choice-logging — [DEFERRED]:** Sam will polish which choices get logged for
   the endgame mocking-echo playback later. Not required for Main 1 to build. *(When Sam
   returns to it: the logging hook is cheapest to add now while the dialogue nodes are being
   authored — leave a stub.)*

---

*End of v1 — Main Mission 1 "Cold Company." Next: Main Mission 2 (the Cyclops run — first
real death consequence, DP1, the handler's mask drops).*
