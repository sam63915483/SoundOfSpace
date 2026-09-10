🟢 ACTIVE — playtest checklist, 2026-09-10

# Audio pass — playtest

## ⚠️ Do this first: **save the scene** (Ctrl+S in Unity)

Eleven clip assignments were written into `1.6.7.7.7.unity` **in memory**, not to
disk — saving the gameplay scene is yours to do, never a script's. Play works
either way right now, but if Unity reloads the scene before you save, the
fishing / crystal / breathing wiring is gone and has to be redone.

Everything else is already on disk: the two door prefabs, the moon-base prefab,
the shuttle prefab, all the audio files, and all the code.

---

## New sounds — what to listen for

Each of these was picked from three takes in the Sound Lab. Every file was
loudness-matched before wiring (see "Levels" below), so if one is *wrong* it
should be wrong in character, not just in volume.

| Where | What should happen |
|---|---|
| **Hook a fish, then reel** | A creaking tension bed comes up under the fight. Reel harder and it gets **louder and faster**; let go and it drops back to almost nothing. At full tension it is running at 2.5× and should feel like it is about to go. |
| **Let tension hit 100** | One short dry wooden snap. Under half a second — if it feels like it has a tail, that is the wrong take. |
| **A fish makes a run** | **Two layers at once**: the fish kicking away (splash) and the reel giving up line (a ratchet loop) that lasts as long as the run. If you only hear one, that is a bug, not a mix problem. |
| **Reeling anything in** | A steady reel loop the whole time you hold reel — on land or in water, fish or no fish. Deliberately quiet: you hear it constantly, so it must never nag. It also drops in pitch while a fish is fighting you. |
| **Land a fish** | No longer `YAY.mp3` (which also plays for tutorial steps and chopping trees). Now a wet catch with a short shimmer over it. |
| **Hit a crystal with the axe** | A high ching instead of the wooden thump every other target uses. |
| **Break a crystal** | A crystalline shatter with shards scattering. Previously silent. |
| **Moon base lever** | A heavy breaker CLACK. Previously silent. All six switches. |
| **Village house doors** | A creak both opening and closing (your call — the same take does both). Previously silent. Both door prefabs, `_01` and `_02`. |
| **Just walking around** | Breathing every 10–15 s, close and helmet-y. Three variants so it does not repeat identically. |
| **Jumping, repeatedly** | This is the one to hammer. Run and hop a lot. You should get an occasional quiet suit exhale — **not** the sped-up breathing loop from before. |

## Also changed this pass (not from the Sound Lab)

| Where | What should happen |
|---|---|
| **Shuttle firing thrusters** | The ship's rocket loop now plays on the shuttle too — liftoff, landing, hover. Follows engine power, tightens near the ground. The main-menu shuttle stays silent on purpose. |
| **Shuttle exit door** | Plays the hatch sound instead of the ramp sound. |
| **Walk toward a lit bonfire** | It crackles from ~26 m and gets louder as you approach. It used to be silent unless something was cooking. |
| **Enemies** | There are none — vaulted this session (`FeatureVault.Enemies`). Weapons still work, they just have nothing hostile to hit. |

---

## Levels

The generated clips arrived spanning **24 dB** (fish-landed at −16 dBFS, breathing
at −40). A volume slider only goes DOWN, so the quiet ones could never have been
fixed with a knob — which is how a mix ends up "some things super loud, other
things super quiet".

So the **files** were matched to −20 dBFS first (−1 dB peak ceiling, 12× boost
cap), and the volume knobs then do the editorial balance only:

| Sound | Vol | Why |
|---|---|---|
| Rod snap | 1.00 | a failure you must not miss |
| Fish landed | 0.90 | the payoff |
| Crystal break | 0.85 | a moment |
| Fish run — splash | 0.85 | the event |
| Fish run — drag | 0.70 | sits **under** the splash, or they fight |
| Moon lever | 0.70 | |
| Village doors | 0.65 | |
| Crystal hit | 0.65 | every swing — loud here becomes fatigue fast |
| Tension bed | 0.55 | it swells on its own; the ceiling is what matters |
| Breathing | 0.45 | intimate, not present |
| Reel loop | 0.40 | audible for most of every cast |
| Jump exhale | 0.30 | fires constantly |

Spread after matching: **9.2 dB**, and what is left is honest crest factor — a
snap peaks harder than a breath at the same loudness.

**If something is still wrong,** say whether it is *too loud/quiet* (a knob) or
*the wrong sound* (a different take). The 26 losing candidates are still in
`Assets/Audio/Studio/_Candidates/` — about 20 MB — deliberately **not** deleted
until this playtest passes, so swapping to a runner-up costs nothing.

## Nothing was thrown away

- Old breath pool → `StreamingAssets/Audio/Breaths/_retired/` (26 files).
- Losing candidate takes → `Assets/Audio/Studio/_Candidates/`.
- Old `catchClip` (`YAY.mp3`) is still used by tutorial steps and tree chopping;
  only fishing stopped using it. `spinCatchClip` was left on its old sound —
  say the word if the spin catch should match.

---

# Round 2 — 2026-09-10, after the first playtest

## Fixed: falling through the ground while the rod is wound up

Reproducible cause found, and it explains why *only* the wound-up rod does it.

A bobber that has been **cast** has live colliders. Reeling it all the way home
never turned them back off — only the freshly-equipped path did, and that path
already carries the warning *"a prop on the rod tip must not collide with
anything"*. Worse, both wind-home paths **destroy the Rigidbody**, and a collider
with no Rigidbody is a **static** collider — which also drops the
`IgnoreCollision` pairs that were keeping the bobber out of the player.

Then the last piece: **winding up draws the rod tip BACK**, sweeping the bobber
into the player's own capsule. Held forward it never overlaps — which is exactly
why the same spot was safe without the rod drawn.

So you ended up with a static collider parked inside you and moving with you: an
overlap that can never resolve. The player's depenetration is capped at 1 m/s, so
it does not launch you — it **presses**, every frame, forever. Somewhere with
thin ground under you, that press goes straight through it.

Fix: the bobber leaves the physics world entirely whenever it is on the rod
(`Bobber.DetachFromPhysicsWorld`), and `AttachPhysics` remains the single place
that turns it back on at the throw. **Test it by standing in the spot that
reproduced it and walking around with the rod wound up.**

## Fixed: crystals still thudded

My miss. There are two swing paths and I patched the dead one — `useClassicSwing`
is off, so the live path is the **physics swing** in `BladeSweep`, which plays the
generic hit clip and knew nothing about crystals. The impact sound now goes
through `AxeController.HitClipFor(crystal)`, used at all four sound sites
(charged hit, uncharged hit, scrape), and a crystal keeps its own pitch instead of
being dropped to 0.8 like the wooden knock.

## Mix

- **Tension up**: `0.55 → 0.90`, and its floor moved `0.05 → 0.28`. At 0.05 the
  bed only became audible in the top third of the gauge — so for most of a fight,
  which is where you decide whether to keep reeling, there was nothing there.
- **Run splash down**: `0.85 → 0.45`. It fires on top of the drag loop, and the
  two together read as one very loud splash instead of two layers.

## New: FlingWatch (the "launched into space" bug)

Not reproducible on demand, and the usual cause is already guarded —
`maxDepenetrationVelocity` is clamped to 1 m/s, which is why the fishing bug
pressed you through the ground rather than firing you off the planet.

So rather than guess, there is now a **detector**: if the player ever moves more
than 220 m/s in one physics step, the console logs `[FlingWatch]` once with the
speed, the nearest body, your altitude and whether you were grounded. It
deliberately does **not** clamp anything — silently correcting a fling would hide
the thing we need to see.

**If it happens again: check the Console for `[FlingWatch]` and tell me what you
were doing.** That turns "it happened again" into something fixable.

---

# Round 3 — 2026-09-10

## Fish splash now falls off with distance

It was a 2D one-shot on the rod, so a fish bolting at the end of a 40 m cast was
exactly as loud as one thrashing at your feet. It now plays from a 3D source
placed **at the bobber** — full volume within 5 m, inaudible past 55 m, linear
rolloff (logarithmic spends its whole range in the first few metres, so the cast
would have been silent almost immediately).

The tension bed, drag loop and reel loop stay 2D on purpose: those are the rod,
and the rod is in your hands.

## Hover sound — where it now plays

The main menu's hover tone is `UiSfxPlayer.Hover()`, already a global. Added to:

| Where | How |
|---|---|
| **NPC dialogue choices** | One hook in `PostGreetingChoicePanel`'s row, which is the shared panel every graph NPC and every vendor's spoken menu uses — so this covers all of them at once. |
| **Ship computer app tiles** | NAV, TRAX and every future tile. |
| **NAV map planets** | Fires on a *change* of hovered body, not while over one, or it would be a tone generator. Muted while dragging the map — sweeping the system past a parked cursor is a camera move, not a series of hovers. |
| **NAV TRAVEL button** | Hover + click. |
| **Pause menu** | Already had it on the main buttons and tab headers. |

Two details worth knowing:

- The dialogue hook sits on the **transition into lit**, not on `OnPointerEnter`.
  Pointer-enter moves the EventSystem selection to that row (so pad A acts on the
  row under the mouse), which also fires `OnSelect` — hooking the handlers
  directly would have played the tone twice per mouse move. Riding the transition
  also means the **pad gets it for free**, with no second code path.
- It is muted for a row's first 0.25 s. The panel selects a row the instant it
  appears, and a burst of hovers on open is noise, not feedback. Disabled rows
  are silent too.

## Removed: hover in the pause menu's settings

You asked for it *not* in settings — and it was already there. `BuildToggleRow`
was calling the full `Attach`, so every settings toggle you swept past made the
sound. That is almost certainly what prompted the note.

The **click** is deliberately kept (new `UiSfxPlayer.AttachClickOnly`): you meant
to change that setting and should hear that you did. Only the incidental
hover-on-the-way-past is gone. Slider rows never had either.

---

# Round 4 — 2026-09-10

⚠️ **Needs one Ctrl+S**: the jump and splash volumes are scene values.

- **Jump sound removed.** `jumpEffortVolume = 0` on both player prefabs. `PlayJump`
  already early-outs at 0, so it costs nothing and raising it brings the exhale back.
- **Run splash down again**, 0.45 → 0.28.
- **Shuttle door was inaudible, and the reason was `PlayClipAtPoint`.** It gives a
  LOGARITHMIC source with minDistance 1 — half volume at 2 m, a tenth at 10 m. The
  ramp is at the back of the shuttle and you stand up front, so nothing survived
  the trip. It now uses a real source with linear rolloff (full through the cabin,
  gone by 70 m) and a `doorVolume` knob. **This bug shape applies to every other
  `PlayClipAtPoint` in the project** — the moon lever and village doors use it too,
  so if those also sound thin, that is why.
- **Shuttle thrusters:** the clip was borrowed from the scene's `Ship` and nothing
  logged when that lookup came back empty — a silent single point of failure. There
  are now three routes (serialized clip → Ship → `StreamingAssets/Audio/ShuttleThrust.mp3`)
  and it logs which one it used. If it is still silent, the Console says why.

---

# Round 5 — 2026-09-10 (not audio)

⚠️ **Needs a Ctrl+S** — the dwarf gravity values are scene data.

## Dwarf gravity raised

Every small body felt like almost no gravity, and on Pebble a run-and-jump
literally escaped: run 9.23 + jump 6.15 = **11.09 m/s** against an escape velocity
of **10.95**.

The cause was RADIUS, not gravity. At 30–90 m across, orbital velocity is only
8–20 m/s — running speed. Both orbital and escape velocity are `sqrt(g*r)`, and
radius is fixed (it is how big the world looks), so gravity was the only lever.

Values are solved backwards from a target **jump height of 4.0 m** — clearly a
moon (Humble Abode is 2.4 m) but a jump you obviously come back down from.

| body | g | jump was → now | run+jump vs escape |
|---|---|---|---|
| Pebble | 2.0 → 5.4 | 13.8 m → 4.0 m | 62% |
| Tumbling Bean | 2.0 → 5.2 | 12.4 → 4.0 | 54% |
| Shard / Bruise | 2.5 → 5.2 | ~9 → 4.0 | 53–54% |
| Hearth / Puddle / Constant Companion | 3.0 → 5.1 | ~7 → 4.0 | 49–52% |
| Slag / Ember | 3.5 → 5.0–5.1 | ~6 → 4.0 | 45–47% |
| Anvil | 4.0 → 5.0 | 5.1 → 4.0 | 42% |
| Watchful Eye | 4.5 → 4.9 | 4.4 → 4.0 | 37% |

Set in the scene AND in `DwarfPlanetInstaller`'s table, so a re-install cannot
quietly revert it.

**Orbits are unaffected, and this is provable rather than hopeful:** every one of
these bodies is on a clockwork RAIL or follows a leader, so its path is scripted
and mass is not an input to it. Nothing in the solar system is free n-body any
more — the Sun is pinned, the Black Hole is static, the rest are railed.

**What DOES change and wants a look:** anything that flies or is dropped near a
dwarf. Ship approach and landing, the shuttle autopilot's descent, and thrown or
dropped objects all feel about 1.5–2.5× more pull than they did.
