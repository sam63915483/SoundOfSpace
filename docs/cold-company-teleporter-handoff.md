# HANDOFF — Cold Company, Beat 3.5: The Teleporter

**Repo:** github.com/sam63915483/SoundOfSpace
**Scope:** Insert the teleporter beat into the built Cold Company mission (Constant Companion moon base).
**Status of this design:** Ratified canon from design sessions (Jul 9 / Jul 14). It exists nowhere in the repo yet — this document is the design authority. The repo is the technical authority: **verify every [EXISTS] claim against the actual codebase before building.** This doc was written from design-session knowledge, not a fresh clone.

**Workflow reminder:** Sam places all GameObjects manually. Phase 0 (research) and the integration plan come first; scripting and wiring happen only after Sam confirms placement (see Placement Manifest, §8).

---

## 1. CANON — LOCKED (do not change without Sam)

The beat, in order:

1. Somewhere in the base sits the rebels' **one-use teleporter**, reverse-engineered from the stolen ORG files, rated for **objects under one square foot**.
2. Near it, the **first cryptic ORG file fragment**: the rebels were trying to recreate a way to enter and exit the black hole freely; the device was unstable; nobody was willing to test it.
3. **Neither the player nor the phone knows where the teleporter leads.**
4. The handler reviews the rebels' calculations and pronounces them sound: a there-and-back trip is possible **inside 30 seconds**.
5. The handler **volunteers to scout** — it reads as taking one for the team, and genuinely is: it does not know it is about to jump into its own maker's core.
6. The player **physically places the phone** into/onto the teleporter (press-F interaction) and the 30-second jump begins.
7. **30 seconds with no phone.** The player never sees or hears the other side in this mission.
8. Zap-back: the phone returns **cracked**, briefly **glitching**, letting slip more than it should (§6 — return glitch lines). Then the teleporter **burns up**. One use, used. Permanently dead.
9. What actually happened on the other side (canon, invisible here, pays off in the finale): the phone landed in the black hole's core dimension, reached for the mother supercomputer to sync — and instead found **endgame-you**, standing at the table. *"YOU??? BUT HOW??"* Endgame-you drops it; it zaps back.

### Hard rules

- **One-shot.** After the burn there is no reusable gateway, ever. Nothing may resurrect it. (This closes the "loaded gun" problem — the teleporter must never offer a safe route the endgame is supposed to be.)
- **Under one square foot.** A person can never fit. Do not soften or fudge this anywhere (dialogue, fragment text, visuals).
- **The phone remembers everything and will never tell.** It saw the core, it saw you, and its agenda from here on is to keep the player from ever reaching that place. Post-beat handler dialogue may be quietly colored by this, but it must never confess, and the IntentRouter must never have a path that extracts the truth.
- **Same room, same table.** The place the phone lands is the *exact* room — same table — the player stands in during the finale. One location, approached from two times; two ends of one loop. Nothing on the far side is built now, but this contract must be recorded in the repo (see §5, endgame contract) so future endgame work mirrors it.
- **Tev never learns the teleporter existed.** He was never able to get inside the moon base; nobody on Humble Abode messes with space. No dialogue path — including report-back — may leak it to him. Report-back covers the evacuation, the photo wall, and the ORG??? file only.
- **Tone: very cryptic.** Hook players; answer nothing. The mission should end with more questions than it started with.
- **The player never hears the other-side encounter.** The return glitch lines are the *only* leak. "YOU??? BUT HOW??" is never heard in Mission 1 — it plays from the other side of the loop, in the finale.
- **This beat is mandatory.** It is the origin point of the endgame encounter and cannot be missable. Gate mission completion on it.

---

## 2. PLAYER EXPERIENCE — moment to moment

1. **Discovery.** The rig draws the eye (idle hum, standby light — Claude Code proposes FX within existing patterns). Look-at/proximity interaction prompt to inspect. Handler comments on it. Player gets **three tone response options: sarcastic / amazed / disbelieving.** Flavor, not branching — the handler answers each in kind — but **capture the choice** (see §5, choice logging).
2. **The fragment.** A readable document near the rig, using the existing reading interaction (the newspaper-table / monuments system). Reading it is what makes the handler's calc-check make sense, so **gate the jump prompt on the fragment having been read.** (If this conflicts with how the built mission gates beats, propose an alternative and flag it.)
3. **The proposal.** Handler reviews the rebels' math, confirms there-and-back inside 30 seconds, volunteers to scout.
4. **The placement.** Press F → the phone leaves the player (UI locked) and appears as a physical prop seated on the pad/socket.
5. **The jump.** Activation, flash, phone gone. **30 real-time seconds.** Recommendation: **no timer UI, no countdown — pure diegetic silence.** This is the first time since the crash the player has been truly alone, and the beat should feel like it. (Alternative presentations are [OPEN] §10 — do not decide this unilaterally.)
6. **The return.** Zap/flash. The prop is back on the pad, **screen cracked.** Player retrieves it (press F). The **glitch lines** play (§6). Then the rig sparks, ignites, and dies — permanent burned state.
7. **After.** The phone UI carries a **permanent cracked overlay** from this moment for the rest of the game, across every app. The handler recomposes itself — outwardly the same voice as before, with the faintest hairline underneath.

---

## 3. [EXISTS] — verify all of this against the repo before building

- **Cold Company** was built on `feat/dimension-polish`, since landed in `main`: mission flow for the evacuation scene, the dated photo wall, and the ORG??? surveillance file + first-lie conversation.
- **StoryDirector** singleton; data-driven authored dialogue nodes; JSON dialogue files; handler personality via swappable profile text assets (Trusting → Strained → Hostile).
- **IntentRouter + templated HAL responses** (the deterministic system that replaced the local LLM).
- **Interaction prompt system** from the lyric-monuments work: proximity + look-at "Press F", nearest-target disambiguation, movement/camera suppression while reading, page navigation for multi-page documents.
- **Face Down assets:** committed `face_down` JSONs; FaceDownSpot wiring exists, some of it inside the Mission2 scaffolding residue on `feat/dimension-polish`. That scaffolding is otherwise **non-canon** (rejected draft) — salvage the *phone-placement machinery only*, nothing narrative.
- **Phone UI** is substantial (dialogue, Photos app / PhotoLibrary / gallery). Everything on it must be inaccessible while the phone is physically away.
- **Known repo-wide cautions** (from prior reviews — confirm which still apply):
  - `FindClip` exact-string clip binding **fails silently** when JSON lines and C# lookup tables don't match byte-for-byte.
  - A **single pending-conversation slot** can silently drop queued dialogue beats.
  - Save/load fragility around carried physics objects.
  - Monolithic scene YAML churn — prefer prefab-level / ScriptableObject tunables over scene-serialized fields wherever practical.

---

## 4. PHASE 0 — RESEARCH (required before any code; report findings to Sam first)

Sam's explicit instruction: you know the mission, systems, and code — **do the research and figure out the right integration.** Answer these, then produce a short integration plan for Sam's sign-off before building:

1. **The physical phone.** How does Face Down implement it? Is there already a world-space phone prop with a press-F placement interaction? Can it be generalized into one shared component (e.g., a `PhonePropController`) that Face Down, this beat, and the future endgame loop-close all use? How does the UI↔world handoff work today — is there an existing "phone is away / UI suppressed" state, or does that need building?
2. **Beat sequencing.** How does the Cold Company director order and gate its beats (states, flags, triggers)? Where exactly do the ORG??? file beat and the downstream beats hook in, and what is the cleanest insertion point for 3.5 **after the file/first-lie beat and before the pointer/report-back flow**? If the built order differs from the design doc, propose placement and flag it.
3. **Persistence.** Where does mission state live, and where should *global-permanent* world state live? Required flags: `fragment_read`, `teleporter_tone_choice`, `teleporter_burned` (global, forever), `phone_cracked` (global, forever). The cracked phone and dead rig must survive save/load and persist for the entire rest of the game.
4. **The pending-conversation slot.** Guarantee the return-glitch conversation cannot be silently dropped if something else is queued. This beat plays exactly once and must never be lost.
5. **Audio path.** Confirm the handler TTS/clip pipeline for new lines and the exact clip-binding requirements, so authored strings can be locked before binding.
6. **Timer affordances.** Note what countdown/timer affordances exist (HUD or diegetic) — for Sam's [OPEN] decision on the 30-second wait, not to pre-empt it.
7. **Death/save edge cases during the jump.** What happens today if the player dies or saves mid-sequence? Propose the simplest safe policy (see [TEST] §9 and [OPEN] §10).

---

## 5. [BUILD]

- **TeleporterRig** behaviour with explicit states: `Idle → Inspected → Armed (fragment read) → PhonePlaced → Jumping (30s) → Returned → Burned (terminal)`. Visual/audio treatment per state, per existing FX patterns. `Burned` is permanent and persisted globally.
- **Phone prop handling:** reuse/generalize the Face Down prop. Placement socket (child transform) on the pad; press-F place; press-F retrieve on return; cracked state = material/mesh swap or decal on the prop.
- **Phone-away mode:** hard lock on opening the phone UI while the phone is placed/jumping; hide any HUD elements that belong to the phone; audit for systems that assume the phone exists (null-ref and soft-lock safe).
- **Cracked-phone persistent visual:** a permanent cracked-glass overlay on the phone UI, visible across all apps, from the return onward — driven by the global `phone_cracked` flag so it also applies to the world prop in any future use (endgame loop-close).
- **Fragment readable:** new document asset using the existing reading system (proximity prompt, movement/camera suppression, page nav if needed).
- **Choice capture:** record the tone choice (sarcastic / amazed / disbelieving) into whatever choice-logging exists for the endgame echo-playback plan; if none exists yet, stub the record with a clearly marked TODO tied to that plan rather than dropping the data.
- **Endgame contract record:** add a short canon note to `CLAUDE.md` (or the repo's canon doc if one exists): *the room the phone reaches in Beat 3.5 is the finale room — same table; the finale must include the phone-arrival moment from the other side ("YOU??? BUT HOW??"); reserve a named event/hook for it.* Pick the hook name per codebase conventions and document it.

---

## 6. [AUTHOR] — dialogue and text

> **Clip-binding warning:** final strings must be **locked by Sam before any clip binding** — `FindClip` matching is exact-string and fails silently.

Needed lines (write in the handler's established voice/profile stage for this point in the game):

1. **Discovery** — handler notices/assesses the rig.
2. **Three tone options** for the player (sarcastic / amazed / disbelieving) + a handler response to each. Flavor only.
3. **Calc-check** — handler reviews the rebels' math: numbers hold, there-and-back inside thirty seconds, and it volunteers to go.
4. **Pre-jump** — brief. Placement prompt, activation.
5. **Return glitch lines — DRAFT ONLY, Sam must ratify or rewrite before binding.** Requirements: fragmented and corrupted; reveals more than it should while answering nothing; must NOT name the tank, Patient Zero, or state the destination outright; should re-read as obvious *after* the finale. Draft candidates (pick/trim/edit — fewer is better):
   - `—table. there was a—`
   - `sync… sync fail— who authorized a second—`
   - `Thirty seconds. I was gone thirty seconds. …That is the correct duration.` *(said as if convincing itself — inside is timeless)*
   - `you were— [static] —how are you—`
   - `Do not go there.` *(flat, once — and if the player ever asks about it, the handler denies having said it)*
6. **Fragment text — DRAFT, Sam to ratify.** Rebel-authored, functional, short. Must establish: recreating ORG's way in and out of the black hole; the device is unstable; nobody was willing to test it. Recommended: a capacity line that diegetically justifies the one-square-foot cap (e.g., a chamber note that it "won't take anything bigger than a toolbox"). Keep it cryptic — no mention of what ORG is.
7. **Post-beat deflection template** — if the player asks about the jump via IntentRouter, one composed deflection. It never confesses, in any phrasing, ever.

---

## 7. [INTEGRATE]

- Insert Beat 3.5 **after** the ORG??? file/first-lie beat and **before** the pointer/report-back flow (verify against built order; flag conflicts per Phase 0).
- Mission completion gating **includes** the teleporter beat — it cannot be skipped or sequence-broken around.
- Wire the four persistence flags (§4.3) into the save system at the correct scopes (two mission-scoped, two global-permanent).
- Protect the return-glitch conversation from the pending-conversation slot (§4.4).
- Prefer prefab/ScriptableObject tunables to keep scene YAML churn down; all scene placement is Sam's (§8).
- Confirm no Tev dialogue anywhere (including report-back) references the teleporter.

---

## 8. PLACEMENT MANIFEST — Sam's part (wiring waits on this)

Sam will place, then confirm names/locations:

- **Teleporter rig** model, somewhere in the moon base (location is Sam's call — [OPEN] §10).
- **Pad/socket point** — a child transform where the phone prop snaps.
- **Fragment document** spot near the rig.
- **FX anchors** if needed (burn-up smoke/sparks, return flash).

Claude Code: after Phase 0, produce the exact list of GameObjects/anchors you need (names, hierarchy, any collider/trigger requirements), hand it to Sam, and **wait for placement confirmation before wiring.**

---

## 9. [TEST]

- Save/quit/reload at **every** rig state — especially around the 30-second jump. Enforce whatever policy Sam picks in [OPEN] (recommend: sequence is atomic/uninterruptible; saving is blocked or the sequence resolves safely on load).
- Player death during the 30 seconds (if reachable in the base): respawn must restore a coherent phone state — no permanently lost phone, no duplicate props.
- Phone UI fully inaccessible while the phone is away; no null refs from systems that assume the phone; no soft-locks.
- Glitch conversation plays **exactly once** and can never be dropped by the pending slot.
- `teleporter_burned` persists across sessions — revisiting the base later shows the dead rig, no interaction to re-fire it.
- Cracked overlay persists across sessions and appears in **every** phone app/screen.
- Clip-binding validation pass: every new JSON line has a bound clip; add a cheap validation step if feasible so `FindClip` can't fail silently.
- Fragment gating works: jump prompt unavailable until the fragment is read.
- Tone choice is captured and survives save/load.
- Tev report-back unchanged: no teleporter references.

---

## 10. [OPEN] — decisions that belong to Sam (surface these, don't decide them)

1. **Ratify/edit the glitch lines** (§6.5) and **fragment text** (§6.6) before any clip binding.
2. **Where in the base does the rig live?** (e.g., a sealed lower room; somewhere the evacuation implies they abandoned it mid-work.)
3. **The 30-second wait presentation.** Recommendation: no timer UI, pure diegetic silence. Alternative: a diegetic countdown on the rig console.
4. **Who runs the timer?** Minimal version: the handler self-times and the player just waits. Darker alternative: the player sets the 30-second timer *on the phone itself* before placing it.
5. **Save-during-jump policy** (block saving vs. atomic resolve on load).
6. Anything Phase 0 turns up that conflicts with this document — the repo may have drifted; flag rather than silently adapt.
