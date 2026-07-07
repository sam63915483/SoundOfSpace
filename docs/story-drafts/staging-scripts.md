# Staging Scripts — the four set-piece scenes

Beat-by-beat direction for the scenes that live or die on presentation.
Every effect named here is a system that already ships — module names are
from `CameraEffectsManager` (audit §18) and the concert rig (§14).

---

## 1. "Face Down" (N-1)

The scene is sixty seconds of nothing, staged so the nothing is loud.

1. Player places the phone (prop spawns screen-down) and steps back past
   3 m. A timer starts. **No UI acknowledges any of this.**
2. At T+0: suppress `HALLineHUD`. Duck music to silence over 2 s (if any is
   playing). Leave ONLY wind/ambient. Do not letterbox, do not grade — the
   game should look completely normal, because for the phone this is the
   most abnormal minute of its existence and the player should get nothing.
3. T+20 s: one subtle beat — if on Constant Companion, the Eye is visible in
   the sky. Do NOT brighten it. (Players who look will *swear* it did.
   Let them.)
4. T+60 s: no chime. The phone screen simply lights where it lies.
5. On pickup: run `conv_face_down_after`. Restore `HALLineHUD` after the
   conversation, not before.
6. If the player walks >60 m away or mounts the ship: quietly cancel; the
   phone returns to inventory; HAL never mentions it. (One retry allowed —
   `FaceDown_Accepted` stays set.)

**Anti-staging rule:** nothing may happen during the minute. No enemy spawn
(`ConcertStageHub.IsBlockedForEnemy`-style exclusion at the spot), no
vitals warning, no autosave toast (delay `AutosaveManager` if pending).
The scene's only special effect is the game's refusal to fill the silence.

---

## 2. "Cover Set" (A2-6 / N-3)

Ninety seconds on stage; the trick is the player is the distraction and can
*catch* the real event only by not doing their job.

1. Precondition: north stage active (night side), guitar equipped, standing
   in the stage trigger. Phone confirmed absent (in ship) — if carried,
   Pell's voice from offstage: "Phone. Ship. We talked about this."
2. On set start: `ConcertLightProgram` switches to a chase pattern centered
   on the player position. `LetterboxBars` OFF (this is diegetic, not
   cinematic — the player is working).
3. Play-along: no rhythm minigame. Holding the set = staying in the trigger
   with the guitar out. Strums map to the existing guitar play input.
   `AudienceZone` density bump +50% for the duration.
4. **The real event:** at T+30–55 s, two silhouettes move a long crate from
   behind the stage-left truss toward the treeline — visible ONLY if the
   player turns from the crowd mid-set. No marker, no camera hint. (Theme:
   attention spent here is attention not spent there — the player performs
   the Eye's job and learns its blind spot from inside.)
5. T+80 s: blinders (`ConcertBlinder`) fire toward the AUDIENCE — for two
   seconds the player can see nothing but light. When they fade, the crate
   and the silhouettes are gone regardless of whether the player watched.
6. T+90: final chord → single `CameraShake` pulse + the killstreak-banner
   treatment (KillstreakHUD style, one-off): **"ENCORE"**. Crowd audio swell.
7. Fire `OnCoverSetPlayed`. Pell finds the player after: if the player saw
   the crate (a look-direction check during the window), one extra line:
   "You looked. Nobody looks. …He's a friend. You'll meet him in a grey
   room soon enough." (seeds the sympathetic interrogator.)

---

## 3. "The Interview" (A2-7)

Reuses the Interrogation cinematic scaffolding. Two rules make the scene:

1. **The phone is a prop on the table**, face up, screen dark, between
   player and interrogator for the entire conversation. Frame it in every
   shot (it is the third character).
2. **HAL reacts once.** If the player picks the lie ("No.") at q3, the
   phone screen — in frame — blinks its red eye open and shut, once,
   silently (`HALVisuals` flicker). The interrogator does not see it. The
   player cannot un-see it. No line, no log entry.
3. `VignetteOverlay` dialogue-focus driver on for the scene;
   `MoodColorGrade` two points cooler; all other camera FX modules
   untouched (the room should feel like the game with the warmth removed,
   not like a different game).
4. On "parting": the interrogator slides the phone across the table — give
   it a real physics slide, let the player pick it up themselves. The
   `ORG_Reveal` bridge fires on pickup, not on dialogue end: the phase
   transition line ("We need to talk.") must NOT play inside this room.
   First HAL line waits until the player is under open sky.

---

## 4. "The Door" (A3-3)

1. **Approach on foot.** Ship lands ~400 m out (invisible wall for the ship,
   diegetic: "Landing systems decline to go closer. I have logged their
   opinion." — HAL). The walk passes structures built with the dimension
   kit's vocabulary — the visual rhyme IS the exposition; no line comments
   on it.
2. `MoodColorGrade` ramps toward the D24 WaitingRoom palette over the walk
   (the player has seen this grade before and will feel it before they
   place it). `FilmGrainOverlay` +1 step.
3. Six's suit at 30 m from the door: kneeling, hands open, helmet against
   the stone (examine text in notes-and-logs.md). `Discoverable` fires the
   Owner-Six HAL line here.
4. At 10 m: **the phone leaves the hotbar on its own** (hotbar slot empties
   with the standard unequip animation, then the phone prop drifts —
   `GravityObjectSimple`, gentle spin, registered with `EndlessManager` —
   to hover at the door). `LetterboxBars` ON from this moment through the
   ending choice.
5. `conv_door` runs with the phone hovering as the visual anchor. During
   the "reading" node, flicker every light source the player carries
   (`FlickerLight` burst) — the archive is reading *everything*, not just
   HAL.
6. Per ending (checklist, all flags reset in `NewGameReset.Apply()`):
   - **Release:** Eye pupil emission fades over 60 real seconds (players
     will watch the whole thing from the door; let them). HAL systems
     offline: `HALCommentator` gate, AI page dark-eye state.
     `EnemySpawner` global gate off. One-time gallery-silence easter egg
     armed (hal-lines.md).
   - **Stay:** pupil emission dims 50% in a single deliberate step —
     a *blink* — then holds. TorchAura swaps to calm-variant in a radius
     around player-built structures. HAL post-ending line set armed.
   - **Handover:** no emission change. Audio-mix swap on Humble Abode
     (rebels: concert stems +2 dB in ambience; ORG: -2 dB). A new NPC
     (Marlo, if rescued — otherwise a rebel courier) waits at the ship for
     the physical handoff on landing.
7. The flight home has no music and one HAL line (Stay ending only), fired
   on entering Humble Abode atmosphere: "The sunward bank, Astronaut. I
   meant it about the fish."
