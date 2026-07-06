# Overnight Sweep — 2026-07-05 (branch `chore/overnight-sweep`)

Autonomous audit + safe-fix pass run overnight, no Unity open. Everything here
is on `chore/overnight-sweep` (branched off `feat/blackhole-dimensions`) so
nothing touches your working branches until you review.

> ⚠️ **Not compile-verified.** No Unity Editor was available, so these changes
> compile-check on your next Editor open. All fixes are small and mechanical,
> and every referenced symbol was verified to exist in source first — but check
> the Console before trusting the branch.

## What ran

Four parallel audit agents (all completed) + a multi-agent code review of the
dimensions branch (stopped early to save usage — only its scoping pass ran):

1. **MainMenu singleton-trap coverage** — all 66 `RuntimeInitializeOnLoadMethod`
   files classified against `EnsureGameplaySingletonsAsync`.
2. **NewGameReset leak audit** — persistent state that survives quit-to-menu →
   New Game in one process run.
3. **Convention sweep** — the six CLAUDE.md bug classes across all user code.
4. **CURRENT_STATE_AUDIT.md drift check** — audit doc vs actual source.

## Fixes applied (6)

| # | File | Fix |
|---|---|---|
| 1 | `UI/MainMenuController.cs` | **Seeded `VelocityMarkersHUD`** — it had the MainMenu early-return but was never seeded, so the prograde/retrograde piloting markers **never existed in builds** (classic trap #1; it was the only ship HUD missing). |
| 2 | `UI/MainMenuController.cs` | Loading-bar divisor `Total` 39 → 47 (actual tick count; bar was overshooting 100%). Cosmetic. |
| 3 | `SaveSystem/NewGameReset.cs` | **Reset `NameStore`** (`PlayerName`/`AIName`/`FirstContactComplete`) — a New Game after a prior session reused the old names and **skipped the first-contact naming conversation**. |
| 4 | `SaveSystem/NewGameReset.cs` + `AI/HALCommentator.cs` | New `HALCommentator.ResetForNewGame()` clears `_visitedBodies`/`_streakMilestonesHit` — HAL's "you have arrived at X" first-visit lines never re-fired in a new run for bodies visited in a previous run. |
| 5 | `Dimensions/ProcessionController.cs` | Hoisted a `new List<Statue>()` allocated **every `FixedUpdate`** into a reused scratch field (same `_toDespawn` pattern as the other dimension controllers). Steady GC churn in D8 gone. |
| 6 | `Scripts/Game/Debug/GravityDebugUI.cs` | Debug gravity panel: cached `Camera.main`, throttled the readout rebuild to 4×/s (was per-frame string churn), and switched `FindObjectsOfType<CelestialBody>` → the null-safe cached `NBodySimulation.Bodies`. Only active while the backtick panel is open. |

Also: `docs/CURRENT_STATE_AUDIT.md` refreshed — new **§32 Black-Hole Observation
Dimensions** section, corrected build-settings scene list (29 scenes / 23
enabled, was "exactly two"), black-hole → D1 boot-flow note, `DimensionDevLoader`
+ `VelocityMarkersHUD` added to the singleton table, JitterDiagnostic /
"untracked file" / Photos-app "planned next" staleness fixed.

## Findings left for you (decisions, not oversights)

1. **`LightingDebugToolbox`** has the same trap-#1 defect (MainMenu skip, not
   seeded, no self-heal) — it silently never appears in builds. Probably fine
   for a dev overlay, but it isn't `#if UNITY_EDITOR`-gated either. Decide:
   seed it, gate it, or leave it.
2. **Phone-AI story phase + merged ORG knowledge leak on New Game.** New Game
   resets `EarlyGameProgress.ORG_Reveal` but NOT `GameKnowledgeBase.CurrentPhase`
   / the merged ORG lore (`AIStoryController.RevealORG()` side effects). A fresh
   game's AI can start in the Phase-2 persona and surface ORG spoilers. The code
   comments call phase persistence *intentional*, so I didn't change it — but the
   flag and the knowledge now disagree. Worth a deliberate decision.
3. **Latent:** `LLMService` chat history isn't cleared on New Game (only
   matters if you ever re-enable the LLM).
4. **Minor allocs (event-driven, low priority):** `LongDarkController.cs:193`
   and `SliverTileSet.cs:86` allocate scratch lists inside `Rearrange(...)`
   (fires on observation changes, can burst while scanning). Same hoist pattern
   as fix #5 if you ever care.

## Verified clean (nice result)

The convention sweep found the codebase in very good shape — these classes came
back **completely clean** across all user code: tag comparisons (everything
already uses `CompareTag`), Rigidbody misuse (all dimension actors use
`MovePosition`/`rb.position` correctly), typewriter O(n²) loops (everything
routes through `DialogueTextStyling`), and floating-origin registration (all
gameplay-world runtime physics spawns register; dimension pocket scenes have no
`EndlessManager`, so it doesn't apply there). All 14 dimension
`FindObjectOfType` player-refinds are properly throttled.

## Suggested next steps

1. Open the Editor on `chore/overnight-sweep`, let it compile, check Console.
2. Sanity-test: New Game after a played session → naming conversation should
   run again; build → fly the ship → velocity markers should appear.
3. Merge into `feat/blackhole-dimensions` (or cherry-pick) when happy.
