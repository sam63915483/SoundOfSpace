# Playtest Fixes — 2026-06-05 — Design

Source: `docs/PLAYTEST_TASKS_2026-06-05.md` (Sam's playtest notes).
Branch: `feat/oxygen-atmosphere-system`.

This is a multi-system effort. Nothing here touches the forbidden atmosphere /
shader / procedural-planet zone (trap #2). All altitude/atmosphere queries go
through `OxygenManager`'s existing gameplay accessors (`CelestialBody.Position`,
`body.radius`), never the generation/shading code.

## Decisions locked with Sam (2026-06-05)

- **§7 tip queue:** Build the full **visual stacked queue** AND fix the real
  silent-drop. (Investigation finding below.)
- **§1 menu audio:** **Generate** a space-themed loop via Coplay `generate_music`.
- **§2 vitals O2:** **Suit only** moves into the bottom-right vitals card; the
  hull O2 bar stays as its own contextual element.
- **§3 phone:** **Persistent nag prompt** ("Press X to open your phone.") that
  does not fade until the player opens the phone the first time. Player keeps
  control (no auto-open).

## Build order (dependency-driven)

1. §5 ship proximity gate (architectural; many call sites)
2. §7 tip queue (visual stack + drop fix) — everything that shows a prompt is downstream
3. §4 hull-sealed notification + countdown + milestone warnings
4. §6 vacuum tip
5. §3 phone persistent nag
6. §2 suit O2 into vitals
7. §1 menu ambient audio (Coplay credits — done last, clip confirmed with Sam)

---

## §7 — Tip queue (investigation result + design)

**Investigation (spec required reporting before rewriting):** The premise is
half-right but in a different place than assumed.

- `HALLineHUD.Show()` (`Assets/3 - Scripts/UI/HALLineHUD.cs:65`) **already has a
  robust FIFO queue** — it enqueues before checking the coroutine
  (`_processRoutine`), loops `while (_queue.Count > 0)`, and never clobbers an
  in-progress line. No bug at this layer.
- The real silent-drop is one level up: `HALCommentator.Volunteer()`
  (`Assets/3 - Scripts/AI/HALCommentator.cs:~329-341`) enforces an 8s global rate
  limit (`MinSecondsBetweenLines = 8f`) and **silently discards** any line that
  arrives too soon unless the caller passes `bypassRateLimit:true`. That is the
  "second tip vanishes" behaviour Sam saw.

**Design:**

(a) *Fix the drop.* `HALCommentator.Volunteer()` no longer discards a
rate-limited line — it routes the line into the HUD queue instead (subject to the
cap below). `bypassRateLimit` callers behave as before.

(b) *Visual stacked queue in `HALLineHUD`.*
- Active (primary) tip: full size, full opacity, TTS plays via
  `HALVoicePlayer.TryPlay` (current behaviour).
- Up to **3** queued tips render **below** the primary at ~80% scale and reduced
  opacity, **no TTS** yet.
- When the primary finishes its fade-out, the next queued tip animates **slide-up
  + scale-up over ~0.3s** to the primary slot; TTS fires at the **end** of that
  transition.
- Queue cap = 3. On overflow, drop the **oldest queued** item (not the incoming
  one) and `Debug.LogWarning`.
- Existing fade timing preserved: 0.4s in / 5.5s hold / 0.7s out / 0.3s gap.

(c) *Proximity gate ordering.* The §5 gate is evaluated **before** a ship-specific
tip is enqueued, so tips the player can't reach never enter the queue.

---

## §5 — Ship-specific prompts: 25 m proximity gate

**Existing scaffolding (reuse, don't reinvent):**
- `PlayerController` already exposes `shipProximityRadius = 25f` and a throttled
  `FindNearestShipInRange()` (0.2s interval, `sqrMagnitude`, skips piloted ships)
  — `Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs:~45,1062-1087`.
- `Ship` exposes `IsPiloted` (`Ship.cs:1185`), `PilotedInstance` (`:213`),
  `AnyShipPiloted` (`:211`), and caches the `pilot` PlayerController (`:208`).

**Design:** Add a per-instance helper on `Ship`:

```csharp
public bool PlayerIsNearOrPiloting(float radius = 25f)
```

True if `IsPiloted` for this instance, OR the player Rigidbody is within `radius`
of this ship's `rb.position`. Throttled re-check (~0.2s) and null-safe (false when
no player / ship inactive). Per-instance so multi-ship works.

**Gate these call sites** behind `ship.PlayerIsNearOrPiloting()`:
- `ShipReactor.cs:99` (insert-crystals prompt), `:89` (feed SFX)
- `BackHatchButton.cs:45` (open/close prompt), `:53` (toggle SFX)
- `OxygenManager.cs:171/174/180` (hull VO: re-oxy / ajar / ajar-repeat)
- `VitalsHUD.cs:213` (ship power/fuel warning SFX) — already partly gated on
  `AnyShipPiloted`; tighten to this ship's proximity.

---

## §4 — "Hull sealed" notification + live countdown + milestone warnings

**State in `OxygenManager`:** new flag `hullWasFilledOnGround`.
- Set **true** when hull enters `Refilling` while in a breathable zone
  (`InRefillZone()` true).
- Cleared when `HullO2` reaches 0, or a fresh fill begins.

**Trigger:** on the `Refilling → Sealed` edge **with `hullWasFilledOnGround`
true** (player opened the hatch on an O2 planet, filled the hull, then closed it).
Does **not** fire if the hatch was never opened on an O2 planet.

**On seal:**
- Show a **live-countdown** prompt variant: `"Hull sealed — X minutes Y seconds of
  air remaining."`, where the X/Y update in real time while the prompt is visible.
- Same fade/timing as the existing "Re-oxygenating the hull" prompt
  (`HALLineHUD`: 0.4 in / 5.5 hold / 0.7 out). Requires a small HUD addition: a
  line whose text is re-evaluated each frame while shown (a delegate/format-source),
  rather than a static string.

**Milestone warnings** (separate prompts, same seal source), each **once per seal**,
reset on refill/reseal:

| Threshold | Message |
|-----------|---------|
| 4 min | "4 minutes of hull air remaining." |
| 2 min | "2 minutes of hull air remaining." |
| 1 min | "1 minute of hull air remaining." |
| 30 s  | "30 seconds of hull air remaining." |

All §4 prompts gated by §5 (player near or piloting this ship). `HullO2` is a
seconds-of-air float; convert to mm:ss for display.

---

## §6 — AI tip: "Hull exposed to the vacuum of space."

**Trigger:** hatch open AND in vacuum (above atmosphere midpoint, or off-world
except Cyclops) — the same altitude logic `OxygenManager` already computes for
draining.

**Behaviour:** fire **once** when the condition becomes true. Flag
`hullVacuumTipFired` prevents spam; resets when the condition clears (back in
atmosphere or hatch closed). Routes through the §7 queue. Gated by §5.

---

## §3 — Phone: persistent first-message nag

**State:** new `bool hasEverOpenedPhone` in `SaveData` → capture/apply in
`SaveCollector` → reset in `NewGameReset` (it gates a one-time UX).

**Current flow:** at ~45s in ColdOpen, `StoryDirector.cs:228` calls
`PlayerPhoneUI.FlashNotification("Incoming transmission")` (on-phone strip, hidden
unless open) + `HALCommentator.VolunteerExternal(...)` (HUD line, brief).

**Design:** when the first message arrives AND `hasEverOpenedPhone == false`, show
a **persistent** "Press X to open your phone." prompt (no fade) until
`PlayerPhoneUI.Open()` fires. Opening sets the flag (persisted) and dismisses the
prompt. Later messages use the normal brief notification. Key is hardcoded
`KeyCode.X` in `PlayerPhoneUI` — display "X" literally.

---

## §2 — Suit O2 into the vitals HUD (suit only)

`VitalsHUD` (`Assets/3 - Scripts/Survival/VitalsHUD.cs`, bottom-right card,
`BuildStatRow()` pattern, fill via `localScale.x`, cached gradient sprites) gains a
4th `StatRow` for suit O2 (cyan gradient, e.g. `#5CC8FF`), reading
`OxygenManager.Instance.SuitPercent`, updated in the existing `Update()` loop with
change-detected percent text.

The standalone `OxygenHUD` (`Assets/3 - Scripts/Survival/OxygenHUD.cs`, top-left):
disable its **suit** bar; keep its **hull** bar as the contextual element (shown
only when piloting / inside ship / draining). OxygenHUD is auto-seeded in
`MainMenuController.EnsureGameplaySingletons()` — leave seeding intact.

---

## §1 — Main menu ambient audio (Coplay-generated)

No `AudioManager` exists in the project (confirmed). Generate a space-themed
**looping** ambient track via Coplay `generate_music`, import to
`Assets/.../Audio`, and drive it from a looping `AudioSource` set up by
`MainMenuController` (respect `AudioListener.volume`). Run last (Coplay credits);
confirm the clip with Sam before wiring.

**Additional menu content** (settings screen / solar-system visual): spec gates
this behind "surface options to Sam first." **Not built this round** — listed as
discussion options after the audio lands.

---

## Out of scope / non-goals

- No new `FindObjectOfType` in Update/Fixed/LateUpdate (use cached/throttled
  patterns; trap and CLAUDE conventions).
- No changes to the forbidden atmosphere/shader/celestial-generation zone.
- No main-menu settings/visual content built this round (discussion-gated).
