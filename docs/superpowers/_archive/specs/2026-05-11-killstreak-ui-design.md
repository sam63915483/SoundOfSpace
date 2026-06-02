# Killstreak UI + Slow-Mo Stacking — Design

## Goal

Show a Halo-style "DOUBLE KILL → TRIPLE KILL → …" popup at the top-center of the screen (just under the compass) whenever the player chains enemy kills within a per-tier time window. Each consecutive kill in the streak also stacks the on-kill slow-mo by 1.2× per tier, resetting to baseline when the streak breaks.

Visual style locked during brainstorming: **B-cyan** — big skewed Impact lettering, no frame, multi-layer cyan glow + deep-navy drop shadow, decay bar underneath. Mockup: `.superpowers/brainstorm/.../killstreak-tiers.html`.

## Non-goals

- No persistence — streaks reset on player death, scene reload, or main-menu exit. Nothing saved to `SaveData`.
- No leaderboard / max-streak tracking. Just the popup + slow-mo.
- No new audio. (If we want a "kill chime" later, that's a follow-up.)
- No changes to the enemy-kill detection itself; we hook the existing `EnemyController.OnAnyEnemyDeath` static event.

## Out of scope

- Animating the decay bar from the world (3D effects, particles). It's a plain UI line.
- Tier-specific copy variants (e.g., randomized callouts like Halo's "Killtacular"). One name per tier.

---

## Tier ladder

10 named tiers. The 11th and any kill after caps at the same visual.

| Streak | Window | Name | Font size |
|---|---|---|---|
| (1, no popup) | 10 s | — | — |
| ×2 | 9 s | DOUBLE KILL | 42 px |
| ×3 | 8 s | TRIPLE KILL | 44 px |
| ×4 | 7 s | QUADRUPLE KILL | 46 px |
| ×5 | 6 s | RAMPAGE | 48 px |
| ×6 | 5 s | KILLING SPREE | 50 px |
| ×7 | 4 s | UNSTOPPABLE | 52 px |
| ×8 | 3 s | DOMINATING | 54 px |
| ×9 | 2 s | GODLIKE | 56 px |
| ×10 | 1 s | LEGENDARY | 58 px |
| ×11+ | 1 s | WICKED SICK | 60 px, white text + red glow halo |

"Window" is the time the player has from THIS kill to land the next one. After the window expires with no new kill, the streak breaks (resets to 0).

- Kill 1: starts the streak; no popup yet but the 10 s window is running.
- Kill 2 within 10 s: pops `DOUBLE KILL`, decay bar takes 9 s to drain.
- Kill 3 within those 9 s: pops `TRIPLE KILL`, decay bar takes 8 s to drain.
- … and so on until the 1 s cap, which is the WICKED SICK tier and stays at 1 s for every kill past ×11.

## Visuals

Top-center canvas, anchored 100 px below the screen top so it sits under the compass (compass at `_canvas.sortingOrder = 300`, sized vertically ~120 px from the top edge — fine to verify in-engine and nudge if needed).

Per popup:

- **Multiplier label** above the name — `×N` in 22 px Impact, color `#b3ecff`, cyan text-shadow.
- **Tier name** — 42–60 px Impact, color `#7BE2FF` (white at ×11), `skewX(-6deg)`, multi-stop text-shadow combining a cyan glow and a 1-px navy outline. Spec values:
  ```
  letter-spacing: 4 px
  text-shadow:
    0 0 18px #5CC8FF,
    0 0 32px rgba(92, 200, 255, 0.7),
    0 3px 0  #04101E,
    -2px 0 0 #04101E, 2px 0 0 #04101E, 0 -2px 0 #04101E;
  ```
- **Horizontal "streak line"** — 240 × 2 px gradient (transparent → cyan → transparent), 70% opacity, sits between the name and the decay bar.
- **Decay bar** — 80%-width × 3 px high. Background `rgba(255,255,255,0.1)`, fill `#5CC8FF`. Fill width decays linearly from 1.0 → 0.0 over the current tier's window. The instant a new kill registers, fill resets to 1.0 and the tier advances (and font size grows).

### Tier-cap special case (×11+)

Font size pinned at 60 px, color shifted to pure white, and the glow layer adds a soft red halo (`rgba(180, 80, 80, 0.4)`) outside the cyan layers. Name stays "WICKED SICK" — every kill past the 11th refreshes the timer but doesn't change the label.

### Animation

- **Enter** — scale `0.6 → 1.0` with `EaseOutBack` overshoot, alpha `0 → 1`, over 0.25 s (`unscaledTime` — must work during the slow-mo dip). Fires on every kill in the streak, including streak advances.
- **Streak advance** — when an existing popup gets a new kill, it stays mounted; the multiplier + name text swap to the new tier, the decay bar snaps back to 1.0, and the whole pill does a quick `1.0 → 1.08 → 1.0` "punch" pulse over 0.15 s for feedback.
- **Streak break** — scale `1.0 → 0.85`, alpha `1 → 0`, over 0.4 s. Fires when the decay bar empties.
- All animations run on `Time.unscaledDeltaTime` so the popup keeps animating during the slow-mo freeze.

---

## Slow-mo stacking

`SlowmoOnKill` already handles a single kill — `timeScale = 0.15` for 0.45 s. New behavior:

- The duration scales by `1.2 ^ max(0, currentStreak - 1)`:

  | Streak | Duration |
  |---|---|
  | × 1 (solo) | 0.45 s |
  | × 2 | 0.54 s |
  | × 3 | 0.648 s |
  | × 4 | 0.778 s |
  | × 5 | 0.933 s |
  | × 10 | 2.323 s |
  | × 11+ (cap) | ~2.79 s |

- `timeScale` value stays at `0.15` regardless of streak — only duration grows. (User asked for "increases the slow mo by 1.2x"; deeper timeScale at high streak would freeze gameplay too long.)
- A new kill that comes in while a previous slow-mo is still active **extends the end-time** rather than starting a competing coroutine. Without this fix, the existing per-routine `WaitForSecondsRealtime(0.45f)` causes the first routine to set `timeScale = 1f` before later kills' routines finish — slow-mo ends 0.4-ish s too early on chained kills.
- When the streak breaks (decay bar empties without a new kill), the NEXT solo kill returns to baseline 0.45 s.

---

## Architecture

Two new files plus one tweak. Existing kill-detection (`EnemyController.OnAnyEnemyDeath` static event) is unchanged.

### File: `Assets/3 - Scripts/Combat/KillstreakManager.cs`

Singleton MonoBehaviour, auto-create + `DontDestroyOnLoad`, skip in MainMenu.

Responsibilities:
- Subscribes to `EnemyController.OnAnyEnemyDeath` on `OnEnable`.
- Maintains:
  - `int CurrentStreak` (0 when no streak active, 1 right after the first kill, etc.).
  - `float DecayTimer` and `float CurrentWindow` (tier-dependent).
- On each kill: increment `CurrentStreak`, set `CurrentWindow` from the tier table, reset `DecayTimer = CurrentWindow`, fire `OnKillRegistered(int newStreak)` event.
- Each `Update` (or coroutine): decrement `DecayTimer` by `Time.unscaledDeltaTime`. When it hits 0, fire `OnStreakBroken()` and reset `CurrentStreak = 0`.
- Public surface: `int CurrentStreak`, `float DecayProgress01` (1.0 = just-killed, 0.0 = about-to-break), `event OnKillRegistered`, `event OnStreakBroken`.
- Resets on `PlayerController` death event (`ResourceManager.OnDeath`).

### File: `Assets/3 - Scripts/UI/KillstreakHUD.cs`

Singleton MonoBehaviour, auto-create + `DontDestroyOnLoad`, skip in MainMenu, registered with `HUDSceneGate` so it hides in MainMenu. Procedural canvas, no scene assets.

Responsibilities:
- Builds the popup canvas in `Awake` (top-center anchor, sortingOrder 830 to match the rest of the recently-bumped HUDs).
- Two `TextMeshProUGUI` children: multiplier (`×N`) and tier name. One thin `Image` for the streak line, one `Image` for the decay bar fill.
- Subscribes to `KillstreakManager.OnKillRegistered` and `.OnStreakBroken`.
- On `OnKillRegistered`:
  - Look up tier from the streak count (table baked in — see Tier Ladder above).
  - Update both texts, font size, and color.
  - Snap decay bar fill width to 1.0.
  - If popup was hidden → play enter animation (scale + alpha).
  - If already visible → play "punch" pulse.
- Every frame while visible, decay bar fill width = `KillstreakManager.DecayProgress01`.
- On `OnStreakBroken`: play exit animation, then disable the popup.

### File modified: `Assets/3 - Scripts/Camera/SlowmoOnKill.cs`

Replace the simple per-kill coroutine with the end-time pattern:

- `Handle()` reads `KillstreakManager.Instance?.CurrentStreak ?? 1`, computes `duration = baseDuration * Mathf.Pow(1.2f, Mathf.Max(0, streak - 1))`, extends `_slowmoEndTime = max(_slowmoEndTime, Time.unscaledTime + duration)`, and starts a single long-lived routine if one isn't already running.
- The routine loops `while (Time.unscaledTime < _slowmoEndTime) yield return null;` then restores `timeScale = 1f` and clears the running flag.

### File modified: `Assets/3 - Scripts/UI/MainMenuController.cs`

Add `KillstreakManager` and `KillstreakHUD` to `EnsureGameplaySingletons` so they're seeded before the gameplay scene loads (same pattern as `PlayerWallet`, `TutorialUI`, etc.). HUDSceneGate keeps both hidden in MainMenu.

---

## Edge cases

- **Multiple kills in one frame** (rare — e.g., AOE damage) — each invocation of `OnAnyEnemyDeath` advances the streak by one. Visuals process them serially on next `Update`; if two arrive on the same frame the popup just lands on the higher tier.
- **Player dies mid-streak** — `ResourceManager.OnDeath` resets the streak immediately (no waiting for the decay bar). Popup fades.
- **Scene reload / save load** — `KillstreakManager` resets on scene change (subscribe to `SceneManager.sceneLoaded`). No saved state.
- **Slow-mo extension across streak break** — if the decay bar empties WHILE the slow-mo is still in its dip, the dip plays out to its scheduled end. The NEXT kill is treated as the start of a fresh streak (`×1` slow-mo duration).

## File list

| File | Why |
|---|---|
| `Assets/3 - Scripts/Combat/KillstreakManager.cs` | New — tracks streak + decay + emits events |
| `Assets/3 - Scripts/UI/KillstreakHUD.cs` | New — popup canvas + animation |
| `Assets/3 - Scripts/Camera/SlowmoOnKill.cs` | Modified — duration stacking + end-time logic |
| `Assets/3 - Scripts/UI/MainMenuController.cs` | Modified — seed singletons in `EnsureGameplaySingletons` |

No changes to `EnemyController.cs`. No changes to save schema.

## Acceptance criteria

1. Killing two enemies within 10 s shows `DOUBLE KILL` ×2 popup with the cyan B-style typography.
2. A third kill within 9 s of the second advances to `TRIPLE KILL` ×3, font scales to 44 px, decay bar snaps to full, and the popup does a quick scale-punch.
3. Letting the decay bar empty with no new kill fades the popup out and resets the streak.
4. The slow-mo dip on a streak kill lasts noticeably longer than a solo kill (×2 = 0.54 s, ×5 ≈ 0.93 s, ×10 ≈ 2.32 s).
5. Two kills landing 0.1 s apart do NOT cause slow-mo to end at 0.45 s — the end-time logic keeps it going for the full scaled duration of the later kill.
6. Popup hidden in MainMenu, visible in gameplay; survives floating-origin shifts (it's UI, anchored to canvas — no shift involved).
7. Player death mid-streak resets the popup and the streak count.
