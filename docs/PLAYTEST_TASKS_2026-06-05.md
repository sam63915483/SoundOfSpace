# Playtest Tasks — Sound of Space — 2026-06-05

*Organized from playtest notes for Claude Code. Each task includes desired behaviour,
edge cases, and relevant existing systems to reference. Research the codebase before
implementing — do not guess at class names or field locations.*

---

## 1. Main Menu — Audio & Content Polish

**What's missing:** The main menu currently has only Start Game, Credits, and Exit.
It needs ambient audio and additional content to feel like a real game front-end.

**Tasks:**
- Add ambient/atmospheric audio to the main menu scene (space-themed, looping).
  Check whether an `AudioManager` or similar singleton already handles music — if so,
  use that pattern rather than a one-off `AudioSource`.
- Investigate what additional menu content is appropriate (settings screen, a brief
  visual of the solar system, etc.) and surface options to Sam before implementing.

---

## 2. HUD — Move Suit O2 Bar into Vitals HUD

**Current behaviour:** The suit O2 refill bar exists as a separate UI element.

**Desired behaviour:** Redesign the suit O2 indicator so it lives inside the existing
vitals HUD in the bottom-right corner alongside hunger, thirst, and health.

**Notes:**
- Find the existing vitals HUD component (likely `ResourceManager` or a dedicated
  HUD class) and identify how the existing bars are constructed.
- The O2 bar should follow the same visual language as the existing vitals bars.
- The current standalone O2 element should be removed or disabled once the vitals
  version is in place.

---

## 3. Phone — Force First-Open on New Message

**Current behaviour:** When a message arrives, a "you have received a message" prompt
appears briefly.

**Desired behaviour:**
- The prompt must also display: *"Press X to open your phone."*
- The prompt must **not disappear** until the player actually opens their phone —
  it should persist on screen until that input is received.
- Once the player has opened the phone at least once, the prompt can revert to normal
  (brief display, no forced persistence). This is a first-contact forcing function only.

**Notes:**
- Locate where the "received message" prompt is triggered and displayed.
- The persistence condition is: prompt stays visible until `PhoneUI` (or equivalent)
  is opened. Hook into the phone-open event to dismiss the prompt.

---

## 4. Oxygen — "Hull Sealed" Notification with Live Timer

**Trigger condition:** Player opens the ship hatch on a planet that has breathable
atmosphere (hatch open → hull fills with air), then closes the hatch, sealing that
air inside. *This should NOT trigger if the hatch was never opened on an oxygen planet.*

**Desired behaviour on seal:**
- Display: *"Hull sealed — X minutes Y seconds of air remaining."*
- The time shown is a **live countdown** that updates in real time while the prompt
  is visible — not a snapshot.
- The prompt disappears shortly after appearing, the same way the existing
  *"Re-oxygenating the hull"* prompt fades out. Mirror that timing and fade behaviour
  exactly.

**Milestone warnings — separate prompts, same trigger source:**
Display a text prompt (no TTS required unless already consistent with existing pattern)
at each of the following hull O2 thresholds, counting down from when the hull was sealed:

| Threshold | Message |
|-----------|---------|
| 4 minutes remaining | *"4 minutes of hull air remaining."* |
| 2 minutes remaining | *"2 minutes of hull air remaining."* |
| 1 minute remaining | *"1 minute of hull air remaining."* |
| 30 seconds remaining | *"30 seconds of hull air remaining."* |

These warnings should only fire once each per seal event. If the hull is refilled and
resealed, the warning set resets.

**Notes:**
- The `OxygenManager` tracks hull O2 as a seconds-of-air float. Convert to
  minutes/seconds for display.
- The "was opened on an oxygen planet" condition requires a state flag — add one to
  `OxygenManager` (e.g. `hullWasFilledOnGround`) that sets true when the hatch opens
  in a breathable zone and clears when the hull O2 drops to zero or a new fill begins.

---

## 5. Ship-Specific Prompts — 25-Metre Proximity Gate

**Problem:** All ship-specific audio and text prompts (fuel warnings, O2 warnings,
hull state messages, etc.) currently play regardless of where the player is. This
causes confusion when the player is far from their ship, and will break entirely when
multiple ships exist.

**Desired behaviour:**
- Any prompt that is specific to a ship (hull O2, fuel, hull state, etc.) should only
  display/play if **either**:
  - The player is within **25 metres** of that ship, **or**
  - The player is currently **piloting** that ship.
- This applies to Ship 02 and any future ships as well as the current Ship 44 — each
  ship's prompts are scoped to its own 25 m radius.

**Implementation approach:**
- Add a proximity check helper (or extend an existing one) on the ship or a companion
  manager: `bool PlayerIsNearOrPiloting(float radius = 25f)`.
- Gate every ship-specific prompt call behind this check.
- Do **not** invent a new FindObjectOfType pattern — use cached references or the
  existing static instance pattern.
- Note: this is groundwork for multi-ship support, not just a UI fix. Make sure the
  check is per-ship-instance, not global.

---

## 6. New AI Tip — "Hull Exposed to the Vacuum of Space"

**Trigger condition:** Hatch is open AND player/ship is in vacuum (above the atmosphere
midpoint, or anywhere off-world except Cyclops). This is the same altitude logic already
used by `OxygenManager`.

**Desired behaviour:**
- Trigger the existing AI tip system with the message:
  *"Hull exposed to the vacuum of space."*
- This should fire once when the condition becomes true, not continuously.
  Add a cooldown or a "has fired this exposure event" flag so it doesn't spam.
- Route through the same tip/commentary system as existing tips (see §7 below —
  implement §7 first so this tip benefits from the queue).

---

## 7. Tip Queue System — Investigation & Refactor

**Suspected bug:** When two tips are triggered in quick succession, the second may be
silently destroyed rather than displayed. This is unconfirmed — investigation is
required first.

**Investigation step:**
- Find the class responsible for displaying AI tips / hull commentary prompts.
- Add temporary logging: log every time a tip is requested, every time one is displayed,
  and every time one is discarded or cancelled. Reproduce the overlap scenario and
  check the logs.
- Report findings before rewriting anything.

**Desired queue behaviour (implement after investigation confirms the bug):**

1. **No tip currently showing:** display immediately at full size, play TTS as normal.
2. **Tip already showing:** add new tip to a queue. Display it below the current tip
   at a slightly smaller size so the player can see it is waiting.
3. **Current tip finishes and fades out:** the next queued tip slides up to the primary
   position, scales up to full size, then TTS plays for it.
4. **No tip should ever be silently dropped** — every triggered tip must either display
   immediately or enter the queue.

**Visual spec for queued tips:**
- Primary (active) tip: full size, full opacity, TTS playing.
- Queued tip(s) below: ~80% size, slightly reduced opacity, no TTS yet.
- Transition: animate slide-up + scale-up over ~0.3 s when promoted from queue to
  primary. TTS fires at the end of the transition.

**Notes:**
- Keep the queue length reasonable — consider a max of 3 queued tips. If the queue is
  full, the oldest queued item (not the incoming one) should be dropped, and a warning
  logged.
- The proximity gate from §5 should be evaluated **before** a tip enters the queue —
  don't queue tips the player can't see anyway.

---

## Order of Attack (suggested)

1. **§7 Investigation first** — everything that displays a prompt is downstream of
   this system. Understand it before adding more tips.
2. **§5 Proximity gate** — architectural, touches many prompt call sites. Do this
   before adding new ship prompts.
3. **§4 Hull sealed notification + warnings** — builds on §5 and §7.
4. **§6 Vacuum tip** — one new tip, routes through the now-fixed queue.
5. **§3 Phone force-open** — self-contained, do any time.
6. **§2 Vitals HUD O2 bar** — self-contained UI task.
7. **§1 Main menu** — discuss scope with Sam before starting.
