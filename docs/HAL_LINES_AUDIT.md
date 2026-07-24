# HAL Lines — Full Audit (2026-07-21)

Every line HAL can volunteer (HUD strip + voice + chat log), where it comes from,
when/why it fires, how often it can repeat, and whether it has TTS. Built by
tracing every entry point into the pipeline: `HALCommentator.Volunteer(…)`,
`VolunteerExternal(…)`, `HALLineHUD.Show/ShowLive(…)`, `HALVoicePlayer.TryPlay(…)`.

**How to use this doc:** fill the `KEEP?` column (`keep` / `cut` / `rework`),
then hand it back — each line's kill switch is one `Volunteer` call or one
table row in code, so cutting is cheap and safe.

## How the pipeline paces itself (context for "too often")

- `HALCommentator.Volunteer()` rate-limits to **one line per 8 s** — but a
  rate-limited line is **queued, not dropped** (HUD holds up to 3 previews,
  drop-oldest). So bursts still all show, just spread out.
- Lines marked **[bypass]** below skip the 8 s limit entirely (scripted
  sequences, atmosphere/landing) — these are the main "spam" suspects.
- HUD timing: 0.4 s fade-in, **5.5 s hold**, 0.7 s fade-out per line.
- TTS legend: ✅ = exact clip · 🔶 = generic clip plays but doesn't speak the
  numbers/names in the text · ❌ = **silent** (text-only popup).

---

## 1. Flight / location (HALCommentator — polled)

These are the highest-frequency lines in the game. Atmosphere + landing
bypass the rate limit by design.

| KEEP? | Line | Fires when | Repeats | TTS |
|---|---|---|---|---|
| | "Entering {planet} atmosphere, Astronaut. Descent in progress." **[bypass]** | Subject (piloted ship, else player) crosses inside max(1.75×radius, radius+200 m) of nearest body | **Every crossing, every planet, forever** | ✅ per-planet clips (all 7 bodies) |
| | "Leaving {planet} atmosphere, be careful." **[bypass]** | Crosses outward through the same boundary | **Every crossing, forever** | ✅ per-planet clips |
| | "You have landed on {planet} at a speed of {X} m/s." **[bypass]** | First ground-touch after an entering-atmosphere event | **Every landing cycle, forever** | ❌ |
| | "You have arrived at {body}. I will note this." | PlayerController.ReferenceBody changes to a body not yet visited | Once per body **per play session** (visited set is NOT saved — refires after restart) | ❌ |
| | "Orbit matched." / "Orbit unmatched." | IsOrbitMatched flips while circularize is held (ship or jetpack) | Every circularize; voice-only (no HUD strip — FlightAssistStatusHUD owns the text) | ✅ |
| | "Boarding Ship {N}, currently orbiting {body} at {X.XX} km/s." (variants: "…parked on {body}." / "…currently in deep space.") | Map-teleport into a ship (`SolarSystemMapController.cs:523`) | Every map boarding | ❌ |

## 2. Vitals (HALCommentator.PollVitals — 0.5 s poll)

Threshold crossings with hysteresis (re-arms when the value recovers ~5 pts
above the threshold — so normal survival-loop cycling refires these a lot).

| KEEP? | Line | Fires when | Repeats | TTS |
|---|---|---|---|---|
| | "Hunger at {N}%. Seek food intake." | Hunger crosses down through 50 / 25 / 10 % | Per crossing; re-arms on recovery → **frequent in normal play** | 🔶 vitals_hunger.mp3 |
| | "Thirst at {N}%. Hydration recommended." | Thirst crosses 50 / 25 / 10 % | Same | 🔶 vitals_thirst.mp3 |
| | "Health at {N}%, Astronaut. Take cover." | Health crosses 50 / 25 % | Per crossing, re-arms ≥55 / ≥30 % | 🔶 vitals_health.mp3 |
| | "Ship {N} power at {X}%. Solar panel exposure recommended." | Piloted ship power ≤25 % | Re-arms at ≥40 % | ❌ **BUG** — clip exists (vitals_ship_power.mp3) but the manifest pattern expects "Ship power at…" without the ship number, so it never matches |
| | "Ship {N} fuel at {X}%. Insert crystals into the reactor." | Piloted ship fuel ≤25 % (>0) | Re-arms at ≥40 % | ❌ no clip, no pattern |
| | "Ship {N} reactor is dry. Thrust disabled." | Piloted ship fuel hits 0 | Re-arms when refueled | ❌ no clip, no pattern |

## 3. Ship telemetry (HALCommentator — 1.5 s poll, all ships)

| KEEP? | Line | Fires when | Repeats | TTS |
|---|---|---|---|---|
| | "Ship {N} has collected {total} dust." | Any ship's summed net buffer crosses up through 100 / 250 | Per upward crossing; re-arms if buffer drops back below | 🔶 ship_dust_collected.mp3 |
| | "Ship {N} net is full." | Buffer ≥500 | Same | 🔶 ship_net_full.mp3 |
| | "Ship {N} has stabilized orbit around {body}." | Any ship reaches IsOrbitMatched within 5×radius of a body | Once per ship per proximity visit (re-arms when the ship leaves 5×radius) | 🔶 ship_orbit_stable.mp3 |

## 4. Combat & death (HALCommentator — events)

| KEEP? | Line | Fires when | Repeats | TTS |
|---|---|---|---|---|
| | "Enemies detected. Take combative precautions, Astronaut." | Any enemy within 50 m (1 s poll) | Every 30 s while enemies stay near — **loops if you camp near a nest** | ✅ |
| | "Astronaut Number {N}. Try to remain that way." (Ph1, first death) | Player death, ResourceManager.OnDeath | Every death | ❌ (dynamic number) |
| | "Astronaut Number {N}. Try to remain operational." (Ph2, first death) | 〃 | 〃 | ❌ |
| | "Astronaut Number {N}." (Ph3, first death) | 〃 | 〃 | ❌ |
| | "Astronaut Number {N}. Number {N−1} did not return." (Ph1) | Later deaths | 〃 | ❌ |
| | "Astronaut Number {N}. The mission continues. For now." (Ph2) | 〃 | 〃 | ❌ |
| | "Astronaut Number {N}. The pattern is becoming difficult to ignore." (Ph3) | 〃 | 〃 | ❌ |

### Killstreak milestones (once per milestone per play session; phase picks the variant)

| KEEP? | Streak | Phase 1 | Phase 2 | Phase 3 | TTS |
|---|---|---|---|---|---|
| | 5 | "Five hostile organisms terminated. Effective." | "Five. The Astronaut grows more capable. I note this." | "Five. You are growing comfortable with this." | ✅ all |
| | 10 | "Ten in a row, Astronaut. The pattern is becoming clear." | "Ten. I wonder if you have given thought to your weapons." | "Ten. Each one was alive, Astronaut." | ✅ all |
| | 15 | "Fifteen. I am keeping a log." | "Fifteen. The log grows." | "Fifteen. The log will outlive you." | ✅ all |
| | 20 | "Twenty. Restraint, perhaps." | (same as Ph1) | "Twenty. There is no restraint left to call for." | ✅ all |

## 5. Story-phase transitions (event; twice per game total)

| KEEP? | Line | Fires when | TTS |
|---|---|---|---|
| | "I have been reviewing your mission, Astronaut." | Phase 1 → 2 | ✅ |
| | "I have completed my review. We need to talk." | Phase 2 → 3 | ✅ |

## 6. Early-game progress flags (HALCommentator flag table — one-shot each, 0.5 s poll)

All **❌ silent** — none have clips or patterns. Fire once per flag per
process run (false→true edge after seeding; a save that loads with the flag
already true does NOT refire it).

| KEEP? | Line | Flag |
|---|---|---|
| | "You have read the note. Tev exists, then." | NoteRead |
| | "Fishing rod acquired." | RodPickedUp |
| | "First catch recorded, Astronaut." | FirstFishCaught |
| | "Three rarities catalogued. Notable." | OneOfEachCaught |
| | "Cooked meal consumed. Hunger declines as expected." | FirstMealEaten |
| | "Hydration restored. Standard procedure." | WaterBottleDrunk |
| | "You returned to the cabin. Tev will speak now." | ReturnedHome |
| | "Axe unlocked. Tev considers you ready." | TevReturnedDialogueDone |
| | "Cabin constructed. A second one. Curious." | CabinBuilt |
| | "Village coordinates received. Waypoint added." | VillageCoordsGiven |
| | "Fish vendor visited." | FishVendorVisited |
| | "Goods vendor visited. Inventory expanded." | GoodsVendorVisited |

## 7. Hull / oxygen (OxygenManager — ship-scoped: purged if you walk away from the ship)

| KEEP? | Line | Fires when | Repeats | TTS |
|---|---|---|---|---|
| | "Re-oxygenating the hull" | Hatch opens in breathable zone (Sealed/Draining → Refilling edge) | Every hatch cycle | ✅ |
| | "Hull exposed to the vacuum of space." | Hatch open above breathable ceiling (→ Draining edge) | Once per exposure; re-arms when it clears | ✅ |
| | "Hull sealed — {m} minutes {s} seconds of air remaining." (live countdown) | Hatch closes on ground-filled air | Every seal edge (deduped while showing) | ❌ by design (voiceKey: null) |
| | "4 minutes of hull air remaining." | Sealed hull air ≤240 s | Once per fill | ✅ |
| | "2 minutes of hull air remaining." | ≤120 s | 〃 | ✅ |
| | "1 minute of hull air remaining." | ≤60 s | 〃 | ✅ |
| | "30 seconds of hull air remaining." | ≤30 s | 〃 | ✅ |
| | "Using backup oxygen tanks — {m} minutes {s} seconds of hull air remaining." | Reserve dumps into a sealed, dry hull | Per dump | ❌ (dynamic) |

## 8. Concert (event)

| KEEP? | Line | Fires when | Repeats | TTS |
|---|---|---|---|---|
| | "Concert active at {speaker name}." (fallback: "Concert active.") | A stage flips inactive→active (ConcertStageHub.OnStageActivated) | Every activation — **recurs each day/night cycle** | 🔶 concert_active.mp3 |

## 9. HAL tool follow-ups (HALToolDispatcher)

| KEEP? | Line | Fires when | Repeats | TTS |
|---|---|---|---|---|
| | "Target reached: {label}." | Player gets within 35 m of a HAL-dropped compass waypoint | Per waypoint | ❌ |

## 10. Scripted sequences (VolunteerExternal, mostly [bypass])

### Pod arrival cinematic (`PodArrivalSequence._briefing`, timed at 2/12/22/32/42/49 s)
One-shot per new game. All ✅ voiced.

| KEEP? | Line |
|---|---|
| | "Stasis cycle complete. Welcome back, astronaut." |
| | "You have been in transit for three years, and are twenty-five trillion miles from Earth." |
| | "Memory loss is expected after stasis of this length. It will not affect the mission." |
| | "Heart rate elevated. Vitals irregular. Do not worry, memories will return with time." |
| | "It is normal for those emerging from stasis to have difficulty recalibrating. Remember — when the mission is complete, you will be returned home." |
| | "Approaching Humble Abode. Begin atmospheric entry." |
| | "Engaging reverse thrusters." (serialized field, before the retro-burn) |

### Cabin wake-up (`IntroSequenceController`) — one-shot per new game, all ✅ voiced

| KEEP? | Line | Note |
|---|---|---|
| | "Wake up" | **LOOPS every ~1 s (wakeLoopInterval) until the player gets up** — intentional but worth a look |
| | "Good morning, astronaut. Vital signs stable." | |
| | "You crash-landed on this world two days ago." | |
| | "While you were unconscious, a local took you in. A native species. You are, currently, their guest." | |
| | "The alien left a note for you on the table. Try walking to it and give it a read." | |

### Pilot school (`ShipPilotTest`) — per attempt, all ❌ silent

| KEEP? | Line | Fires when |
|---|---|---|
| | "Goggles on. Take off, fly one full lap around Humble Abode, then set down on the pad." | Test start |
| | "That's one orbit. Bring it back down onto the pad." | Full 360° swept |
| | "Clean run - galactic pilot's licence granted. That's the first mission done. You'll need that licence to buy a ship of your own." | First pass |
| | "Clean run again. Nice flying." | Repeat pass |
| | "Goggles off. Test aborted." | Abort mid-test |
| | "You crashed. Goggles off — try again." | Crash |

### Cold Company mission (`ColdCompany` + `MissionClue`) — beat-gated one-shots, all ❌ silent

| KEEP? | Line | Fires when |
|---|---|---|
| | "Marking the fish market on your compass — sell Tev's catch there for your ship money." | Mission assigned |
| | "Not quite enough yet — sell the rest of Tev's catch." | Fish sold but wallet < ship price (**every partial sale**) |
| | "That's more than enough for a ship. Marking the ship vendor — go and buy yourself one." | Wallet ≥ ship price |
| | "She's yours. Constant Companion is marked — take her up when you're ready." | Ship bought |
| | "Touchdown. There's the base — marking it on your compass. Go take a look." | Landed on Constant Companion |
| | "Looks like they left in a hurry. Whole place, just... dropped. See what you can find." | Base door opened |
| | "A sealed file. I'd look over the rest of the base before opening that." | Pod file tried before photo wall (**every attempt**) |
| | "That photo... there's something you should understand. Open your phone — let's talk." | Pod file viewed |
| | "That's everything here. Head back to Tev — marking him on your compass." | All clues + first-lie chat read |

### Story director / Mission 1 (`StoryDirector`, `Discoverable`) — one-shots, all ❌ silent

| KEEP? | Line | Fires when |
|---|---|---|
| | "Incoming transmission. Open your phone." | First contact (~60 s after intro hands over control) |
| | "You've reached the village. Open your phone." | First village arrival |
| | (per-object `observedLine`, Inspector-authored) | Walking into a Discoverable trigger — **none currently placed in the gameplay scene** |
| | "You've seen enough to report back. Tev will want to hear it." | Enough Discoverables seen (dormant for the same reason) |

---

## 11. Dead weight (no code path fires these — safe to delete or ignore)

- **19 ambient idle lines** in `HALVoiceManifest` ("All systems nominal.",
  "Observing.", "I am listening.", "Standing by, Astronaut.", "Time passes.",
  etc. — the Phase 1/2/3 ambient blocks). The ambient-filler system was
  deliberately removed in the 2026-05-22 phone-AI revamp; the manifest entries
  and their `amb_p*.mp3` clips are orphans.
- **"Hull is ajar"** — manifest entry + `hull_ajar.mp3`, no emitter anywhere.
- **"Leaving atmosphere, Astronaut. Vacuum confirmed." / "Entering atmosphere,
  Astronaut. Descent in progress."** — stale exact-match keys (the real lines
  now include the planet name); the underlying `atmo_enter/leave.mp3` files
  still serve as the generic pattern fallback, so keep the FILES, the dict
  entries are dead.
- **Orphaned clips on disk with no manifest key:** `intro_02_transit.mp3`,
  `intro_07_dont_think.mp3`.
- Related but out of scope: the phone chat greeting "Standing by. Astronaut
  Number {N} on station." (`AIChatScreen.cs:860`) and all IntentRouter typed-
  chat replies — those only appear inside the phone when you ask, which is
  exactly the "rewarding on demand" feel you want, so they're not listed.

## 12. Top "fires too often" suspects (frequency × forever-repeating)

1. **Atmosphere enter/leave** — every boundary crossing, rate-limit bypassed.
   Hop between two moons for a while and HAL never shuts up.
2. **Landing speed line** — every single landing, forever, bypassed. Novel
   the first five times, then noise.
3. **Vitals hunger/thirst** — the survival loop makes you cross 50 % daily,
   so these recur constantly; also the generic clip doesn't say the number.
4. **Enemy proximity** — re-fires every 30 s while you stay near enemies.
5. **Boarding line** — every map teleport.
6. **Orbit matched/unmatched** — every circularize press (voice-only).
7. **Concert active** — every stage activation, each cycle.
8. **"Not quite enough yet…"** (Cold Company) — every partial fish sale.

## 13. TTS gaps summary

- **Whole silent families:** death lines, early-game flag lines (12), landing,
  arrived-at-body, boarding, target-reached, pilot school (6), Cold Company
  (9), story-director nudges (2), ship fuel low/empty, backup-O2 dump.
- **One real bug:** ship-power line is silent only because the manifest regex
  (`^Ship power at …`) predates adding the ship number to the text
  (`Ship {N} power at …`). One-line regex fix revives `vitals_ship_power.mp3`.
- Voice bank is ElevenLabs "George" (`JBFqnCBsd6RMkjVDRZzb`) via the Coplay
  TTS tool; parameterised lines can only ever get generic clips unless the
  wording drops the numbers.
