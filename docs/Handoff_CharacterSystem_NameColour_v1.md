# Handoff — Character System v1 (Name + Suit Colour)

**Game:** Sound of Space · **Date:** Aug 9, 2026 · **Branch:** `feat/helmet-hud` (the real trunk — `main` is stale at Jul 14)
**Sequencing:** build this BEFORE world-state sync. Every later synced system keys off a real character identity instead of "Player 2".

---

## 0. Protocol (non-negotiable)

1. **State your build plan first** and wait for Sam's approval before implementing anything. No exceptions.
2. **Core systems are untouchable:** floating origin, n-body gravity (100Hz), PlanetRelativeSync / SolarSystemSync, the puppet architecture. This feature integrates ON TOP of them. Remember origin rebases fire even while standing still (planetary orbital motion counts toward rebase distance).
3. **Inspect, then adapt:** every name under [EXISTS] must be verified against the repo before planning. If reality differs from this doc, reality wins — say so in your plan.
4. **/docs is mixed freshness.** `GDD_StoryBible_v2.md` predates the Schedule 1 pivot — do not design from it. Current references: `Handoff_HumbleAbode_CultivationDealing_v2.md`, `unity-services-setup-guide.md`, the main-menu MP mockups.
5. Where new scene objects/prefabs are needed, provide Sam a placement manifest and wait for his confirmation before wiring. UI built inside existing menu canvases can be wired directly.

---

## 1. Why this exists

Sam committed to a Terraria-style character/world split: a character (name + suit colour now; levels + hotbar later) that persists across worlds and travels into friends' sessions, while world stuff (customers, planets, story) stays with the save. This handoff builds **only the character identity layer** — create/pick/edit characters, and make name + suit colour visible to everyone in multiplayer.

---

## 2. [EXISTS] — verify against the repo before planning

- Internet co-op shipped Aug 8: Unity Relay + Lobby (NGO 1.12), join by 4-digit code + password, DTLS, no port forwarding. `MultiplayerSession` carries the allocation across the menu→gameplay scene load. MP sits behind `FeatureVault.Multiplayer`.
- Main menu has a MULTIPLAYER button; pause menu has a SESSION tab; guests wake in the pod; rejoin works; host-authoritative pod door.
- Puppet architecture: the real player is never despawned; remote players render as non-colliding puppets synced via PlanetRelativeSync (planet-local pose) + SolarSystemSync.
- **Placeholder labels today:** when a player joins, session text says "Player 2", and "Player 2" floats above their astronaut in-game. Locate where BOTH strings originate — those are the exact things this handoff replaces.
- Stasis-pod save system exists and is not touched here.

---

## 3. [BUILD] — Character data

- `CharacterProfile`: `id` (GUID string), `name`, `swatchIndex` (int), `schemaVersion` (int, start at 1), `createdAt`.
- **Built to grow, not growing yet.** Levels/hotbar/inventory move into this profile later, on Sam's word — not now. Requirements today: (a) loading tolerates missing/unknown fields, (b) `schemaVersion` gates future migration, (c) nothing anywhere assumes the profile is only name+colour, (d) `id` is the stable key future systems (grind transfer, ownership) will reference. Do NOT add level/hotbar fields yet.
- Storage: local JSON at `Application.persistentDataPath` (e.g. `characters.json` holding the character list + `lastSelectedId`). No cloud, no per-world copies.
- The selected character must be available to the gameplay scene in BOTH solo and MP — ride `MultiplayerSession` or a parallel plain singleton for solo; inspect and propose.

---

## 4. [BUILD] — Menu flow + create UI

```
BOOT → MAIN MENU
  ├─ 0 characters? ──► "create a character" popup ──► CREATE SCREEN
  ├─ START GAME  ──► CHARACTER PICKER ──► SAVE SELECT ──► world
  ├─ MULTIPLAYER ──► CHARACTER PICKER ──► host / join (code + password) ──► session
  └─ CHARACTERS  (button directly below MULTIPLAYER) ──► list ──► view / edit / add / delete
```

- **CREATE SCREEN:** name field (required — reject empty/whitespace) + suit colour as **preset swatches only** (no RGB/HSV picker), over a **live astronaut preview** that re-tints as swatches are clicked. The **visor stays black** on every swatch — visor is never tintable.
- **EDIT:** rename + recolour an existing character; same screen as create, prefilled.
- **PICKER:** shows each character's name + colour, remembers last selection, confirm flows onward (save select for solo, host/join for MP).

---

## 5. [BUILD] — Multiplayer identity sync

- On join, each player's `name` + `swatchIndex` reach everyone — connection payload or NetworkVariables on the player object; inspect how puppets spawn and propose the simplest fit. Sync the swatch **index**, never raw RGB.
- Apply: puppet suit material tinted with the swatch; the floating overhead label shows the character name.
- **Replace every placeholder player string with the character name** — join/leave session text, overhead labels, the SESSION pause-tab player list, and the host's own label (whatever currently renders the host as "Player 1"/host gets their character name too). Both directions, all clients.
- The character system itself is NOT gated behind `FeatureVault.Multiplayer` (solo uses it). Only the network-sync pieces sit behind the existing MP gating.
- Late join / rejoin must receive names + colours correctly.

---

## 6. Defaults (Sam hasn't ruled on these — use unless he objects)

- Swatch palette: 8–10 high-contrast suit colours readable in dark space and planet daylight; include a default white and a classic orange. Propose the exact hex values in your build plan.
- Name cap ~16 chars, trimmed; overhead label ellipsizes rather than scales.
- Duplicate names allowed (`id` is the real key).
- Delete allowed from the CHARACTERS list behind a confirm step; deleting the last character re-triggers the no-characters popup.
- No cap on character count; list scrolls.

---

## 7. NOT in this handoff

- Levels / hotbar / inventory inside the profile, or any grind transfer between worlds — later, on Sam's word. Only the *shape* allows it.
- World-state sync (trees, mushrooms, buildables, buyer ledger replication) — that is the NEXT handoff, after this works.
- Any change to core systems, the save-pod flow, or Phase A economy tunables.

---

## 8. [TEST] — acceptance

1. Fresh boot, zero characters → popup → create "Zib" (orange) → shows in CHARACTERS; restart the game → still there, still orange.
2. START GAME → picker → save select → in-world; solo play otherwise unchanged.
3. Two machines over relay: host "Zib"/orange, guest "Bo"/green. Host sees "Bo joined", a green-suited puppet, "Bo" overhead; guest sees "Zib"/orange the same way; SESSION tab lists Zib and Bo. **Zero "Player 2" strings anywhere.**
4. Edit Bo → "Bob"/blue → next session shows the change everywhere.
5. Guest leaves and rejoins → names/colours still correct.
6. 30-char name → capped/ellipsized, nameplate stays sane.
7. Regression: floating-origin rebases + planet-relative sync behave exactly as before (walk, wait through an orbital rebase, confirm no new jitter).

---

## 9. After this ships

World-state sync (Phase B proper): trees, mushrooms, buildables, economy/buyer ledger over the network — separate handoff.
