# Mission 1 — Intro / Wake-Up Sequence (Build Spec)

> **Purpose:** the game's cold open. The player wakes inside Tev's cabin to the AI's
> voice, clicks to surface out of a black screen, receives the stage-setting briefing,
> and then the body reacts (heartbeat → "vitals irregular") before the AI delivers its
> first clinical reassurance and hands off to the survival segment.
>
> **Scene / prereqs:** player spawns at a fixed position inside **Tev's cabin interior**.
> All spoken lines run through the **existing AI on-screen text + TTS pipeline
> (StoryDirector)** — do **not** build a new dialogue display.
>
> **Tags:** `[EXISTS]` live, integrate · `[BUILD]` new code · `[AUTHOR]` writing (tweak
> freely) · `[TEST]` must pass · `[INTEGRATE]` wire into existing system.
>
> **Status:** DRAFT v1 · **Date:** June 16, 2026.

---

## 1. Sequence (in order)

**1. Black screen + "Wake up" loop.** `[BUILD]`
On new-game start: full-screen black overlay at alpha 1. Player movement + look **locked**;
LMB still read. The AI begins repeating the line **"Wake up"** on a ~2.5s interval through
the existing text/TTS system. `[AUTHOR: "Wake up"]`

**2. "Press LMB" prompt @ 10s.** `[BUILD]`
Ten seconds after the sequence starts, show a centered **"Press LMB"** prompt (a separate
UI element — it does **not** fade with the black). Click-counting becomes active only now;
clicks before this are ignored. `[AUTHOR: "Press LMB"]`

**3. Click to surface — 6 clicks.** `[BUILD]`
Each LMB press lowers the black overlay's alpha by `1/6`, smoothly lerped over ~0.3s
(grogginess feel). The "Wake up" loop continues through this. On the **6th** click the
overlay reaches alpha 0 → hide the "Press LMB" prompt, stop the "Wake up" loop.

**4. Stage-setting briefing.** `[BUILD]` `[AUTHOR]`
With the screen clear, fire these three AI lines in order (each waits for the previous TTS
to finish; fixed-delay fallback if no finished-callback exists):
1. *"Good morning, astronaut. Vital signs stable."*
2. *"You have been asleep for three years in transit to this solar system. You crash-landed two days ago."*
3. *"While you were unconscious, a local took you in. They appear to be a race of aliens native to this planet."*

**5. The body reacts — heartbeat.** `[BUILD]`
Fade in a heartbeat SFX (looping, starts slow). *Optional polish:* a soft red vignette
(UI image) pulsing in sync. `[optional]`

**6. AI re-reads vitals.** `[BUILD]` `[AUTHOR]`
- *"Heart rate elevated. Vitals irregular."*

> **Note:** the **stable → irregular** flip is intentional — the news lands, the body
> responds. It is not a contradiction; do not "fix" it.

**7. First clinical reassurance.** `[BUILD]` `[AUTHOR]`
- *"It is normal for those emerging from cryogenic stasis to have difficulty recalibrating.
  Remember — once the mission is complete, you will be returned home. For now, try not to
  think about it."*

**8. Hand off to survival.** `[INTEGRATE]`
Unlock full player movement + look. Heartbeat fades to subtle/off. Trigger the **existing
food/water survival guidance flow**.

---

## 2. Components to build

- **Fade overlay** `[BUILD]` — Screen Space–Overlay Canvas, full-screen black `Image`,
  **Raycast Target OFF** (clicks are read directly via `Input`, not the UI). Alpha driven
  by the controller. "Press LMB" is a separate TMP element, shown/hidden independently.

- **IntroSequenceController** `[BUILD]` — one MonoBehaviour driving the whole thing as a
  coroutine / state machine. Owns: the "Wake up" loop, the 10s timer, click-counting + alpha
  lerp, the ordered line firing, the heartbeat, and the handoff. Suggested approach for the
  unfade: a `targetAlpha` decremented per click, with `Update` lerping `currentAlpha` toward
  it.

- **Input lock** `[BUILD]` `[INTEGRATE]` — disable the player movement/look controller for
  the duration; read `Input.GetMouseButtonDown(0)` during the click phase; re-enable at
  step 8. Cursor stays locked/hidden as in normal play (no cursor needed to click-wake).

- **Heartbeat** `[BUILD]` — an `AudioSource` for the heartbeat loop with a fade-in; optional
  vignette pulse.

---

## 3. Integration / guards

- **Use the existing AI text+TTS system** for every spoken line, for consistency with the
  rest of the game. `[INTEGRATE]`
- **Pace by TTS-finished**, not fixed timers, wherever the system exposes it — lines must
  not talk over each other. `[BUILD]`
- **Play once.** Gate the whole sequence behind an `introPlayed` flag so it does **not**
  replay on load or on clone-respawn. `[BUILD]` `[TEST]`
- **Ignore clicks before the prompt** (step 2) so the player can't mash through the first
  10 seconds. `[BUILD]`

---

## 4. Acceptance test `[TEST]`

Fresh new game → black screen with "Wake up" repeating → at 10s "Press LMB" appears → 6
clicks fully clear the screen → the three briefing lines play in order without overlapping →
heartbeat fades in → "Heart rate elevated. Vitals irregular." → reassurance line → control
unlocks and the food/water flow begins. Reload/respawn does **not** replay the intro.

---

*Optional, not for the slice: the wake-up motif could later be reused in shortened form for
clone-respawns — it rhymes with the death loop ("waking each morning to the same impossible
thing"). Flagging only; don't build it now.*
