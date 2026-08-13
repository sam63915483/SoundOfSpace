# Handoff — Cassette Loop Revamp v1
**Date:** Aug 13, 2026 · **v1.1** — unified selling interaction replaces the free taste test; Tev fronting committed as a repeatable side job
**Builds on:** Handoff_CassettePivot_ShuttleComputer_v2.md (TRAX is BUILT — browser prototype + Unity port, golden-verified). This handoff is everything AROUND the instrument: the vault pass, Tev's music store, printing, customers, and selling.
**Branch:** continue on `feat/helmet-hud` (or branch off it — Sam's call), NOT main.

## §0 Process rules (unchanged, restated)
1. State your build plan for each phase BEFORE implementing; Sam corrects it first.
2. Report Tev's CURRENT conversation flow back to Sam before changing any dialogue — he cuts/changes lines first.
3. Sam places all GameObjects. Build + report object names; he repositions.
4. The shuttle prefab is hand-maintained — patch via LoadPrefabContents (existing TRAX editor tool pattern), never regenerate.
5. Any change to the TRAX engine files: `npm run golden` then `npm run verify:port` (or Tools ▸ TRAX ▸ Verify Engine Port). A diff means already-printed cassettes would change sound — flag it, don't bury it.
6. Multiplayer is load-bearing: every new item, UI state, and NPC interaction must respect the shared-world / separate-wallets model. Note MP behavior in each phase plan.

## §1 Phase 0 — Vault pass [BUILD]
Use the `_Vaulted` pattern from Aug 9.
- [BUILD] Vault the freeform building system (placement UI, buildables, related unlocks). KEEP: tree chopping, saplings/planting, mushrooms (choppable nodes stay), fishing, bonfire cooking. A bonfire near the shuttle: Sam places it (one already exists in-world).
- [BUILD] Vault ALL level systems — general main level, Gangsta Rep, Tree Daddy, every sub-category, their UI, and level-gated unlocks. Nothing in the new loop may reference levels.
- [EXISTS — DO NOT TOUCH] Orientation whiteboard stays as-is. Sam repurposes it after the loop lands.
- [EXISTS — DO NOT TOUCH] Hunger/thirst stay for now.
- [TEST] Game boots and plays with building + levels vaulted; no dangling references; MP join still works.

## §2 Phase 1 — Track struct: active modules + demo library
- [BUILD] Extend the track struct with the ACTIVE MODULE SET (which of the 6 modules are on). Mute state currently lives in the UI only; a printed tape must permanently record what was playing. Mirror in JS engine + C# port. **Golden implication: regenerate vectors, verify:port must pass (rule 5).**
- [BUILD] Plugin gating: player profile owns a set of unlocked plugins. Start = THUMPER + GLOWORM. Locked modules render locked in the TRAX rack (visible, not usable — they're the carrot). Engine guarantee already holds: per-voice seeding means unlocking later never changes existing prints.
- [BUILD] Demo library on the shuttle computer: SAVE current track (named at save time), list saved demos, load one back into TRAX, delete. Library is world-scoped (shared shuttle computer — co-op partners see the same library).
- [OPEN] Library UI layout — mock in the browser prototype first if faster; Sam art-directs.
- [TEST] Save → quit → reload → demo loads and sounds identical (determinism). Locked module can't be activated.

## §3 Phase 2 — Blanks, printing, named cassettes
- [BUILD] Two new items: Blank Cassette T1 ($10), Blank Cassette T2 ($20). Stackable, storable, hotbar-holdable like existing items.
- [BUILD] PRINT flow (replaces the stub): pick a saved demo (or current track) → computer counts blank cassettes IN THE HOTBAR ONLY (locker/inventory blanks do NOT count) → choose quantity up to that count, per tape type → consumes blanks, produces printed cassettes.
- [BUILD] Naming: demo name is set at library-save; printed tape carries it. Held cassette in the hotbar shows the demo name, not "cassette" (reuse the fish/mushroom species-likeness pattern for icon + held model; T1 vs T2 visually distinct).
- [BUILD] Tape identity = hash of the full track struct (dials, key, presets, variations, active set). Renaming does not change identity — aliens recognize the SONG. Store name, type (T1/T2), track hash, and track data on the item.
- [BUILD] MP: printed cassettes replicate like other wire-identity items; either player can print from the shared library; tapes are ordinary transferable items.
- [TEST] Print 3 of 5 blanks → 3 tapes + 2 blanks remain; names display held + in slots; tapes survive save/load and MP transfer.

## §4 Phase 3 — Tev's music store [INTEGRATE + AUTHOR]
Tev is now a MUSIC STORE owner right next to the shuttle. Same character, same beats, new skin.
- **Report current dialogue flow to Sam first (rule 2).**
- [INTEGRATE] Intro = existing rent/onboarding haggle tree reworded for demo tapes: he gets you to sell HIS demo tapes, money split 50/50, teaching the selling loop. Existing step-down haggle structure stays (10 → 8 → 5 → 3). Completing it = buddies; he tells you he's got all your music needs.
- [INTEGRATE] Shop UI (reuse existing shop pattern): Blank T1 $10 · Blank T2 $20 · SIREN $200 · MOSS $200 · SPINDLE $200 · CAVE $200. Plugins are one-time unlocks per player profile (MP: per player, separate wallets).
- [AUTHOR] Reworded intro + shop lines → draft for Sam's cut pass before wiring.
- [BUILD] Repeatable tape fronting (COMMITTED — the simple side job): after the intro, Tev fronts a batch of HIS demo tapes; player offers them to aliens via the §5 unified interaction (Tev's tapes are real TRAX tracks with their own track data, so listening/satisfaction/feedback/negotiation work identically); each sale is split 50/50 with Tev. Adapt the existing fronting dialogue + slot-8 payment UI.
- [BUILD] Skim COMMITTED: Tev's expected cut = 50% of each tape's rough MARKET value, stated when he fronts the batch (mirror the mushroom fronting spec). Sale proceeds land as slot-8 physical money; settling up uses the EXISTING drag / keep-give payment UI — out-negotiate the market and quietly pocket the difference, Tev never knows. Underpay / exact / overpay behaviors carry over from the fronting spec unchanged.
- [TEST] Full intro completes; shop purchases deduct correct wallet; bought plugin appears unlocked in TRAX immediately.

## §5 Phase 4 — Aliens: taste, the unified offer interaction, feedback
**Taste model [BUILD]:** each alien customer gets
- `tastePoint` — a point in the 6-D dial space (same space as TraxClassifier),
- `falloff` — steepness of satisfaction decay with distance (gentle = broad listener, steep = picky),
- `payFactor` — scales INVERSELY with breadth: picky aliens pay premium, broad aliens pay less.
`satisfaction% = clamp(100 − k·falloff·distance(track.dials, tastePoint))`. Per-genre affinity, "wants something similar," requests, and feedback all derive from this — no per-alien authored content.

**The unified offer interaction [BUILD] — ONE interaction for every in-person sale (your tapes, Tev's tapes, text-order handoffs):**
- Offer held tape → alien listens in front of you (play a few seconds of the actual track — the audio engine is right there) → LIKE gate by satisfaction:
  - **≥50:** liked
  - **35–50:** flat 50% roll → liked or not
  - **<35:** not liked
- **Not liked:** no sale, no contact. Feedback given, tape returned to the player (can be offered to other aliens — the repeat rule is per-alien).
- **Liked:** the alien asks "how much?" → player names a price → existing accept/counter/decline negotiation. Their internal max derives from the §6 value formula (satisfaction-scaled), so a track they love supports a higher price. Deal → paid, tape sold, alien becomes a contact (number).
- [BUILD] Greed handling (modifies the existing negotiation flow): push too far and instead of the deal failing outright, the alien issues a FINAL OFFER — take it or leave it — deliberately well below your ask AND below what they'd normally have paid: greed pissed them off, so no top dollar after a swindle attempt. Accept → sale completes at the lowball, contact acquired as normal. Refuse → no sale, tape returned. [ASSUMED — Sam flips if wrong] refusing still earns the contact (they liked the song) with a bond penalty for the burned deal.
- There are NO free demos and NO tip tiers — high satisfaction pays through the negotiation ceiling, not gifts.
- Feedback [BUILD + AUTHOR]: on any rejection (and available on request), computed from the difference vector — name the 1–2 largest gaps as dial advice ("too much CRUNCH, needs more GOO") + their nearest genre ("I'm more of a GLORP guy"). Template lines authored, values computed. Draft templates for Sam's pass.
- Repeat-tape rule [BUILD]: same track hash offered twice to the same alien = denial + bond decrease. Applies everywhere.
- Stinginess touches MONEY ONLY (negotiation tolerance) — never whether they like the music.
- [TEST] Sweep satisfaction bands with forced taste points; verify like gate, negotiation ceiling scaling, contact acquisition, repeat-hash denial and bond hit.

## §6 Phase 5 — Contacts, texts, orders, negotiation
- [INTEGRATE] Number acquired → alien joins phone contacts (existing buyer-messaging system, Aug 7).
- [BUILD] "Music hungry" texts: frequency scales with bond + how much they like your catalog. Request = a genre sampled from their affinity (weighted, non-repeating back-to-back) + optional dial adjective ("a glorpy WARPED song" = genre + high-WARP qualifier).
- [INTEGRATE] Text orders use the existing mushroom flow, unchanged: quote over text → accept / counter / decline → meet in person → honor agreed price or push higher (they may counter or refuse; refusal costs bond). The in-person handoff is the same §5 interaction.
- [BUILD] Pricing [OPEN — Sam tunes all constants]:
  `value = (10 + 8·activeModules) × tapeMult(T1 1.0, T2 1.5) × (0.4 + 0.9·sat/100) × bondMult(1.0–1.4) × requestBonus(1.25 if the tape's classified genre + dial qualifier match the request) × payFactor`
  Alien's accept/counter thresholds derive from this value ± stinginess, same as mushrooms.
- [BUILD] Fulfillment check uses TraxClassifier on the offered tape's stored track — the label the computer showed is the label the alien hears.
- [TEST] End-to-end: text arrives → make requested genre → print → negotiate → paid; wrong genre pays no bonus; identical-tape re-offer denied.

## §7 Explicitly deferred [OPEN]
- Radio milestone measurement + reward flow (goal stays on the design map; not in this handoff)
- Late-game money sinks (preset/variation packs idea parked)
- Whiteboard repurpose (Sam, after the loop works)
- Co-op shared-cursor live editing on the computer (next handoff — track-state sync is cheap by design, but it's its own phase)

## §8 Definition of done
Land → Tev intro → sell his tapes 50/50 → buy blanks → save a named demo → print → offer it, alien likes it and asks "how much?" → name a price and negotiate → paid + contact acquired → receive a genre request text → make it → negotiate → get paid → afford a $200 plugin → richer track sells for visibly more → Tev fronting still works as a side job throughout. Solo AND two-player co-op, no level/building references anywhere.
