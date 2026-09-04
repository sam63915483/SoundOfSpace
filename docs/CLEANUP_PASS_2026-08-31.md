# Cleanup / warning-zero pass — 2026-08-31

> **Correction (same day).** The first version of this doc claimed 0 warnings on
> the strength of a compile check that only ever built the **editor** variant of
> Assembly-CSharp. Unity also compiles the same sources with `UNITY_EDITOR`
> **undefined** (that's what a player build does), and that variant had a warning
> the editor one structurally cannot see: `CS0067` on
> `ComputeHelper.shouldReleaseEditModeBuffers`. Fixed, and
> `compile-unity.py` now builds **all three** variants so this class of gap can't
> recur. See §6 for what's actually left in the console — most of it is runtime
> and shader noise that a C# pass was never going to touch.

Branch `feat/helmet-hud`. **Everything below is uncommitted** — review the diff,
keep what you like, `git checkout` anything you don't.

Verified with `python prototypes/shuttle-computer/test/compile-unity.py`:

```
Assembly-CSharp:                  OK   (0 warnings)
Assembly-CSharp-Editor:           OK   (0 warnings)
Assembly-CSharp (player defines): OK   (0 warnings)
compile check: PASS
```

**177 warnings → 0.**

⚠️ **Nothing here has been play-tested.** Sam's playtest happened *before* the
§7 fixes were written — it's what found them. The five runtime bug fixes and the
five diagnostic gates in §7 are compile-verified only and need one more run to
confirm.

---

## 1. Where the 177 warnings actually came from

Four root causes, not 177 problems:

| Count | Code | Root cause |
|------:|------|------------|
| 91 | CS0649 | `[SerializeField]` fields the **Unity serializer** assigns. The compiler can't see that, so it was wrong 91/91 times. |
| 40 | CS0162 | `FeatureVault`'s `public const bool` flags. A `const false` lets the compiler fold the branch and call the whole vaulted body "unreachable". |
| 23 | CS0414 | Orphaned serialized tuning knobs left behind by rewrites. |
| 15 | CS0169 | Genuinely dead fields. |
| 7 | CS0618 | Deprecated Unity Recorder API in the trailer editor scripts. |
| 1 | CS0108 | `LebronLight.light` hiding `Component.light`. |

### What was done

- **`Assets/csc.rsp` (new file)** — contains exactly one line, `-nowarn:0649`.
  This is the standard Unity fix for serializer-assigned fields.
  **0169 / 0414 / 0162 are deliberately NOT suppressed** — they carry real
  signal (dead field / half-wired feature / genuinely unreachable code), so
  don't add codes to this file casually.
  ⚠️ Unity will generate `Assets/csc.rsp.meta` on next open — commit both.
  ⚠️ The file holds **no comment lines on purpose**. Roslyn tolerates `#`
  comments in a response file (verified), but Unity pre-parses `csc.rsp` itself
  and warns on lines it doesn't recognise — unverified either way without the
  Editor, and a console warning is the exact opposite of this pass's point.
  Keep it to bare options; the rationale lives here instead.
- **`FeatureVault.cs`** — all 18 flags `const bool` → `static readonly bool`.
  Reads identically at every call site, JIT-folded so it costs nothing at
  runtime, flipping a flag still works exactly the same — but the compiler can
  no longer fold the branch, so the 40 false "unreachable" warnings are gone.
  A comment in the file says not to "optimise" them back.
  Same treatment for three other const-bool gates: `Universe.cheatsEnabled`,
  `CassetteVisual.LogOrientation`, `ShuttleArrivalSequence.ReleaseDiagnostics`.
- **`prototypes/shuttle-computer/test/make-rsp.py`** — now reads
  `Assets/csc.rsp`, so the offline compile checker matches what the Editor
  actually reports instead of drifting from it.
- **Dead fields deleted** (23 lines): `_shipCached` ×2, `meshHolder`,
  `readyToFlyShip`, `_style`, `_chargingRow`, `_chargingShown`, `_namePlateBg`,
  `_namePlateBorder`, `_legacyHidden`, `_fadingOut`, `_swungIn`,
  `_firstEntryDone`, `_landRequested`, `pendingHighlightShip`, plus their
  now-pointless writes.
- **Stale serialized knobs deleted** — six dead `heartbeat*` knobs in
  `IntroSequenceController`, `fadeInTime` / `doorOpenAngle` / `grogRecoverRate`
  in `ShuttleArrivalSequence` (the door *slides* now and grogginess is
  altitude-driven, so those were left over from superseded implementations),
  and `bobAmount` in `AudienceMember`.
- **Deliberately-parked fields kept, with scoped pragmas** rather than deleted,
  because the code says in writing that they're meant to stay:
  `BlackHoleCapture.swayMax / spinSpeedMax / spinStartRadius`,
  `PlayerController`'s "not yet wired" sound slots, `jumpClip`,
  `TevMushroomOnboarding.rentPaidOfferLineIndex` (vaulted with `TevRent`).
  Their tooltips now say **CURRENTLY NOT APPLIED** so the Inspector stops lying.
- **`LebronLight.light` → `_light`** (13 sites).
- **Recorder CS0618 ×7** — scoped `#pragma warning disable 0618` + a TODO.
  Migrating to the `EncoderSettings` API is a real behaviour change to your
  trailer tooling and can only be verified with the Editor open, so I suppressed
  it rather than guessing. See §4.

---

## 2. Two real bugs found

### a) `jumpClip`'s pragma was the wrong warning number
`PlayerController` had `#pragma warning disable 0414` around `jumpClip`, but the
warning it actually raises is **0169** (nothing assigns it in code either), so
the pragma had never silenced anything. Now `0169`.

### b) Enemy spit standoff only ever implemented two of its three bands
`EnemyController` documents a three-band standoff — closer than
`spitStandoffMin` → back up; inside the band → stop and pause; farther than
`spitStandoffMax` → walk closer. The code was:

```csharp
if (horizDist < spitStandoffMin) horizDir = -horizDir;
else                             horizDist = stoppingDistance;   // anywhere >= min
```

`spitStandoffMax` was never read — that's what the CS0414 was pointing at. The
effect: an enemy that spotted a treed player from 60 m **halted right there and
spat from far outside the intended band** instead of closing to it.

Now three bands, matching the comment. **This is the one behaviour change in the
pass** — it's an isolated, commented hunk in `EnemyController.cs`, easy to revert
on its own if you'd rather keep the old feel.

### c) (bonus) A misleading Inspector knob
`PlayerSuitAudio` showed `breathMinInterval = 15` / `breathMaxInterval = 25`
under a header reading *"Breathing (every 15-25 s)"* — but `ScheduleNextBreath`
hardcodes `Random.Range(10f, 15f)` and deliberately overrides them. The knobs did
nothing. Removed both fields, header now reads **(every 10-15 s)**, and the
comment says to change the cadence at the call site. Behaviour unchanged.

---

## 3. Dead files deleted (10 scripts + metas)

Each verified two ways: **GUID absent from every `.unity` / `.prefab` /
`.asset`**, *and* type name referenced by no other `.cs`.

`Scripts/Game/Test/{CamTest, LODTest, NormalRotTest, RaySphereTest, ShaderMatrix,
SunTest}.cs`, `Scripts/Game/PlanetTest.cs`, `Scripts/Game/Debug/RandomTest.cs`,
`Ship/ShipReassembly.cs`, `Vid/CameraController.cs` (+ the now-empty `Vid/`
folder and its `.meta`).

`SunTest` is worth calling out: `[ExecuteInEditMode]` with **two unguarded
`FindObjectOfType<Light>()` calls every Update**, and it would have thrown a
NullReferenceException in any scene without a Light.

### Deliberately NOT deleted, though the scan flagged them
- `World/WaterlineAlign.cs`, `Scripts/Game/Debug/ViewmodelArtifactProbe.cs` —
  auto-singletons via `RuntimeInitializeOnLoadMethod`. **They run** despite
  nothing referencing them. Deleting these would have been a real bug.
- Every `*Editor.cs` — wired by `[CustomEditor(typeof(X))]`, invisible to a
  reference scan.
- `World/SelectionRoot.cs` — an authoring convenience (`[SelectionBase]`) you'd
  want the day you need it.
- `Story/FaceDownSpot.cs` — unfinished but *designed* story content
  (`docs/story-drafts/staging-scripts.md` §1). That's work in progress, not junk.
- Everything under `Scripts/Celestial/` and `Post Processing/` — CLAUDE.md
  trap #2 forbidden zone, untouched.

---

## 4. Left for you — triage list, nothing done

1. **Recorder API migration** (`TrailerRecorderSetup`, `TrailerTimelineSetup`,
   `TrailerRecorderInspect`) — `OutputFormat` / `VideoBitRateMode` →
   `EncoderSettings`. Needs the Editor to verify a trailer still records.
2. **`IntroWatch` / `MegaTracker` removal** — still live in
   `ShuttleRiderFrame.cs` (~230 lines of diagnostics), started from
   `IntroSequenceController.cs:374` with a 45 s window. Your memory notes flag
   this as cleanup-pending. I left it: it's the instrumentation that solved the
   slide saga, and ripping it out is surgery I couldn't play-test.
3. **`Universe.cheatsEnabled` is hardcoded `true`** — so F6/F7/F9 dev shortcuts
   are live in shipped builds too. I only changed `const` → `static readonly`
   (behaviour identical); gating it on `Debug.isDebugBuild` is your call, since
   you test in builds.
4. **The remaining ~56 "dead" scripts** flagged by the scan are mostly the
   vendored Triangle mesh library under `Scripts/Game/Debug/Debug Viewer/` and
   forbidden-zone editors. Left alone on purpose.

---

## 5. Perf: I looked, and mostly found good news

Scanned every `Update` / `LateUpdate` / `FixedUpdate` body (brace-matched, not
grepped) for `FindObjectOfType`, `Camera.main`, `GameObject.Find`,
`GetComponentsInChildren`. **~65 raw hits, and almost all are correct** — the
`if (_x == null)` lazy-refind and the throttled `_refindCooldown` /
`_nextCamFind` patterns from CLAUDE.md are applied consistently across
Dimensions, HUDs, popups and mounts. Whoever wrote those held the line.

Genuine offenders found: exactly one — `SunTest`, now deleted.

Also fixed: **`Ship.Update`'s headlight watchdog** fired
`Debug.LogWarning` *every frame* while the shadow state was being fought (that's
console flood at frame rate, and string interpolation allocating each frame). It
now logs once per session per condition and still repairs the state every frame.

Two soft notes, not worth changing blind:
- `ThrusterMount` / `SpaceNetMount` re-run `FindObjectOfType<PlayerPickup>()`
  every frame *while it's null*. Harmless in gameplay (it always exists), but
  it's the un-throttled variant of the rule. One-line fix if you ever see it.
- Per your own perf notes, the real costs remain `QualitySettings`, grass
  streaming and village draw calls — not scripts. Nothing here contradicts that.

---

## 6. What's still in the Unity console (read from the live Editor log)

None of this is C# compiler output, and **none of it came from this pass** —
verified below. The C# side is now clean in all three compile variants.

### On project open / recompile

| Item | Verdict |
|---|---|
| `CS0067 ComputeHelper.shouldReleaseEditModeBuffers is never used` | **Fixed.** Player-only warning; see the correction at the top. |
| `Script attached to 'SolarSystemMap' in scene '1.6.7.7.7.unity' is missing` | **Pre-existing, real.** Orphan component, guid `c76a0a9e923d31f4a8a7d9efae18a70a`. That guid has **never** existed as a `.meta` anywhere in this repo's git history, so it predates the current tracking — it is not one of the 10 scripts deleted here (all 10 of those guids were cross-checked against both scenes and appear in neither). Fix is 2 seconds in the Editor: select `SolarSystemMap` → the `Missing (Mono Script)` component → right-click → **Remove Component**. Left alone here because scene edits were explicitly out of scope for this pass. |
| `Script attached to 'Main Camera' in 'PoolroomsDemo.unity' is missing` | Pre-existing, in a third-party **demo** scene that isn't in build settings. Harmless. |
| `Shader warning in 'Hidden/FXAA'` ×2 — uninitialized variable at FXAA.shader(217), loop variable `i` shadowing at (177) | Pre-existing, benign HLSL hygiene. **Deliberately not touched:** there is no offline shader compiler in this repo, so unlike the C# work none of it could be verified — and FXAA is the anti-aliasing post pass. Both are mechanical fixes (rename the inner loop counter; initialise the variable on all branches) but they want an Editor eyeball. |
| `Shader warning in 'GameUI/DottedLine'` — integer modulus may be slower | Pre-existing, a perf *hint* on a UI shader. `int n` → `uint n` at DottedLine.shader:53 would silence it (`n` is derived from a UV so it is never negative). Same reasoning: unverifiable offline. |

### Only in play mode (from the 20:05 session, not the recompile)

Untouched by this pass, listed because they're the actual console noise once
you press Play — and two of them look like real bugs:

1. **`Setting linear velocity of a kinematic body is not supported.` — dozens
   per session.** Something assigns `rb.velocity` on a kinematic Rigidbody. This
   is both spam and a silently-ignored write, so whatever it was trying to do
   isn't happening. Worth tracking down.
2. **`A call to Blit with source and dest set to the same RenderTexture may
   result in undefined behaviour.`** — logged at **Error** level, fires twice per
   session. A post-process is blitting a texture onto itself.
3. `NullReferenceException` ×6 clustered at one instant during play.
4. `[TutorialUI] Keycap atlas build failed; falling back to bold text` — a TMP
   `TMP_SpriteAsset.UpgradeSpriteAsset` NRE at `TutorialUI.cs:935`.
5. `"" mesh has over 2,097,152 triangles ... Fast Midphase` ×9 — this is the 2M-tri
   planet collider already on record as the hidden cabin-perf cost.
6. `[GrassSpawner] No prefabs assigned; spawner idle.`,
   `[PlanetHolePuncher] 'Constant Companion' has no TerrainHole children`,
   `Motion vectors require depth texture for camera 'Main Camera'` — informational.

Items 1–4 are the ones worth a session of their own.

---

## 7. Play-session pass — read from the live console, not guessed

Sam ran a full playtest (menu → intro → landing → village/caves → save) while I
watched. I pulled the whole console, aggregated **every** distinct message with
counts, then pulled stack traces for the errors. That's what found the causes
below — §5's "perf looked fine" was true and beside the point, because the noise
was runtime, not compile-time.

### Real bugs, fixed (each had an exact stack trace)

1. **`BlackHoleCapture.cs:45` — Blit onto itself.** The vignette shader pre-warm
   did `Graphics.Blit(tmp, tmp, mat)` — same RenderTexture as source *and*
   destination, which is undefined behaviour and logged at **Error** level on
   every single scene load. Now blits `src → dst` with two temporaries. The
   pre-warm is unchanged.
2. **`PlayerController.ClampVelocityAgainstWalls` — writing velocity to a
   kinematic body.** `rb.velocity = …` while the rigidbody is kinematic (seated,
   riding the shuttle, cutscene) is silently discarded by PhysX and logs a
   warning **every FixedUpdate** — **332 occurrences** in one session, by far the
   loudest thing in the console. Now early-returns when `rb.isKinematic`, which
   also skips two wasted `SweepTest`s per tick in that state.
3. **`SpaceDustField.Flush` — per-frame NullReferenceException.** `_mpb.Clear()`
   on a null MaterialPropertyBlock. `LateUpdate` can reach `Flush` before the
   one-time buffer build has run. Guarded.
4. **`HALCommentator.PollEarlyGameFlags` — per-frame NullReferenceException.**
   A **duplicate** instance bails out of `Awake` before the tracker table is
   built — but `Destroy(gameObject)` only takes effect at end of frame, so its
   `Update` keeps running and dereferencing null. Guarded.
5. **`Hotbar.Refresh` — per-frame NullReferenceException ×N slots.**
   `v.itemIcon` null when the slot widgets aren't built yet. Now `continue`s
   past unbuilt slots, which covers every widget write in that loop, not just
   the one line that happened to throw first.

Fixes 3–5 follow CLAUDE.md's "guard scene-object references with null checks
rather than redesigning". All three fired at the same instant in one session,
which points at a scene transition invalidating cached references.

### Leftover diagnostics, switched off (not deleted)

Each got a `static readonly bool Verbose = false` in the project's existing
style (`CassetteVisual.LogOrientation`, `ShuttleArrivalSequence.ReleaseDiagnostics`).
Flip one to `true` and the instrumentation returns exactly as it was. **Faults
still log** — only the routine chatter is gated.

| System | Was logging |
|---|---|
| `MenuTourTracker` | accel spikes, clearance, the 30 s window summary |
| `MenuShuttleTour` | `[MenuTour SPIKE]` path forensics (a `LogWarning` in `FixedUpdate`) |
| `OrbitClockProbe` | per-lap day-length readout for 7 bodies. **The CSV is still written** — that's its real output |
| `PlanetHolePuncher` | per-mesh "removed N triangles" / "snapped N rim vertices". The one-line summary stays |
| `ShuttleRiderFrame` | the multi-KB `[MegaTracker]` / `[IntroWatch v5]` dumps on every landing and intro — the rigs that solved the slide saga, kept but quiet |

### Still there on purpose

- **`[CelestialBodyGenerator]` collider lines (~14 per load)** — inside CLAUDE.md
  trap #2's forbidden zone. Not touched. This is now the single biggest
  remaining source, and it needs your call.
- **`"" mesh has over 2,097,152 triangles … Fast Midphase` ×9** — the known 2M-tri
  planet collider. Silencing it means changing a mesh import setting, which is an
  asset edit and was out of scope.
- **`The referenced script (Unknown) on this Behaviour is missing!`** — the
  `SolarSystemMap` orphan from §6. Two seconds in the Editor.
- **`[TutorialUI] Keycap atlas build failed`** — a TMP `UpgradeSpriteAsset` NRE
  swallowed by a try/catch and reported as a fallback. Real, but it needs the
  Editor to diagnose the sprite asset, so I didn't guess at it.
- The 3 shader warnings and `Motion vectors require depth texture`.
