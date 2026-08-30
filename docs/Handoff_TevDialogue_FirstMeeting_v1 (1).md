# Handoff: Tev Dialogue Revamp — Rent Out, First-Meeting Tree In (v1)

**Goal:** Remove the rent-era Tev dialogue and replace it with the new first-meeting
tree (designed Aug 29). Tev is no longer a landlord. There is NO rent, NO lawn opener,
NO work-off haggle, NO payment lockout. Tev is a music-store owner who pitches the
player on making music. TRAX now costs $20, bought through this conversation.

**Scope guard:** Dialogue + purchase flow only. Do NOT build the physical shop
interior (that's a later task — shop stays as the existing TevShopUI panel). Do NOT
build the radio-impression system (stub only, see §6).

---

## 0. REPORT FIRST (required)

Per standing convention: before cutting anything, **give Sam the proposed revamp as
a line-by-line report**: every node/line currently reachable in Tev's dialogue, where
it lives, and for each one — STAYS (and why / where it's used), or CUT (and why:
rent-era, fronting-era, superseded by the new tree, etc.), or VAULTED. Wait for his
GO. He cuts/changes lines first.

---

## 1. What exists now (my understanding — verify against the repo)

- `[EXISTS]` Rent system (Aug 15, play-verified): daily rent haggle 50→30→20→10,
  arrears (linear, nothing auto-deducted), TevPaymentUI, 5-day lockout on the
  PLUGINS tab only. Intro flow = lawn opener → haggle → gift line → 3 free blanks.
- `[EXISTS]` TevShopUI.cs (~661 lines): panel with BLANK TAPES / PLUGINS FOR SALE
  tabs, 6 blanks w/ padlocks + accordion cards (Aug 18), affordability clamps.
- `[EXISTS]` FeatureVault flags already vaulting TevFronting and TevDemoTapes.
- `[EXISTS]` TRAX lives on the shuttle computer (ShuttleComputerTerminal); plugins
  install to the computer; plugin price ladder 60/90/130/180.
- `[EXISTS]` verify-rent test suite (~107–119 checks) — will break when rent goes.

## 2. Remove / vault (follow the repo's vault-first convention)

Prefer `FeatureVault.TevRent = false` over deletion, same pattern as
TevFronting/TevDemoTapes, traps recorded in docs/VAULTED_SYSTEMS.md. Delete later in
a vault pass once the new tree is play-proven.

- Rent intro: lawn opener, haggle chain (50/30/20/10), gift line as currently wired.
- Arrears tracking + TevPaymentUI entry points from dialogue.
- **PLUGINS-tab lockout: must be fully neutralized.** With rent gone there is no
  arrears state; the plugins tab must never lock. Verify no lockout path survives.
- Work-off haggle (tape-count rent work-off from Aug 13 Phase 3) — dead with rent.
- Any surviving fronting-era greeting lines outside the vault flags
  ("alright pussy", "ready for more big boy?", etc.). Stash, don't lose — Sam may
  reuse the voice for the post-meeting hub later.
- Gate/skip rent-related tests rather than deleting them (they document the vaulted
  system); CI must be green with the flag off.

## 3. Keep

- TevShopUI panel exactly as is — the new tree's shop hub opens it.
- Money system, bond hooks, TapeValue/print/blank-tape systems. No schema changes.
- The "3 free blanks" grant logic — reuse it in the YES branch below.

## 4. `[BUILD]` New first-meeting tree (exact lines — do not rewrite)

Two states only: `TevMet = false` → this tree (identical on any day). `TevMet = true`
→ hub (§5). Convergence: every branch ends at THE PITCH.

**Greeting:** "Salutations, lost traveller!"
Player options: A / B / C.

**A — "I'm not lost. I just haven't found what I'm looking for."**
Tev: "Ohhh, *deep*. Okay. So what is it you're looking for?"
  - **A1 — "A way back home."**
    Tev: "Sure, easy. Where's home?"
    Player (forced, only option): "..."
    Tev: "Ahhh. See, that right there? That's what lost sounds like. Lucky for you —
    lost folks make the *best* music. Interested?" → PITCH
  - **A2 — "I'm interested in making music."**
    Tev: "Now we're talkin'! You're standing in the only music shop this side of the
    event horizon. TRAX engine, blank tapes, plugins when you've earned 'em.
    Interested in gettin' set up?" → PITCH
  - **A3 — "I don't know."**
    Tev: "...Yeah. Honestly? Me neither. That's kinda why I'm throwin' the festival —
    last day, big send-off, everybody dancin' while the sky eats itself. Give folks
    somethin' good to hold. Between you and me though... I got no clue how it's gonna
    go." (beat) "Anyway! Enough of that. You look like a music-maker to me.
    Interested?" → PITCH

**B — "So what do you sell?"**
Tev: "Isn't it obvious? Anything music, baby! With that big hungry nothin' loomin'
over us, music's the only business still turnin' a profit. ...Like that matters
anyways. S'why I'm throwin' the festival. — You interested in gettin' set up?" → PITCH

**C — "Where am I?"**
Tev: "You're on Humble Abode! Third planet from the sun, home to 'the aliens.' Yes,
that's really what we call ourselves. No, we're not changin' it. — You interested in
making music?" → PITCH

**THE PITCH — "Interested in making music?" YES / NO**
- **YES + player has ≥ $20:**
  Tev: "That's what I like to hear! TRAX music engine, twenty bucks. And 'cause I
  like your face — three blank demo tapes, on the house. Go make somethin' ugly."
  → deduct $20, grant TRAX USB Stick to hotbar, grant 3 blank demo tapes (reuse
  existing grant). Install happens at the computer — see §6.
- **YES + broke (< $20):**
  Tev: "Twenty bucks, traveller. ...You don't *have* twenty bucks. Okay. Planet
  provides — check your locker, shake some pockets, hell, the fish out here
  practically pay you. Come back when you're rich."
  → no purchase; hub offers the pitch again until bought.
- **NO:**
  Tev: "Ah. A shame. Well — you know where to find me. Everybody does. It's the only
  shop with a roof."

Set `TevMet = true` when the tree exits by any path (including NO).

## 5. `[BUILD]` Post-meeting hub (minimal v1)

On talk when `TevMet = true`:
- If TRAX not owned: short re-pitch → the same YES/NO/broke outcomes as §4.
- If TRAX owned: option rows — "Let me see the shop" (opens TevShopUI) / "Later, Tev"
  (exit). Nothing else for v1; festival thread comes later.

## 6. `[BUILD]` TRAX purchase gating + `[OPEN]` questions

Decided flow (Sam, Aug 30) — the USB stick:
- Before purchase, the TRAX app does NOT appear on the shuttle computer at all. Not
  locked, not greyed — absent.
- Buying from Tev grants a **TRAX USB Stick** item to the player's hotbar (new
  ItemId; needs an icon — placeholder fine, flag for Sam's art pass).
- Opening the shuttle computer with the USB stick anywhere in inventory/hotbar
  CONSUMES the stick and the TRAX app tile appears **greyed out with a
  "DOWNLOADING" label and a small progress bar, ~6 seconds**, then becomes usable.
- Download progress is world/computer state: it should survive closing the terminal
  mid-download (finish or resume — don't reset), and must save/load sanely if
  someone saves mid-download (simplest: snap to installed on load).
- Co-op: the stick is a personal item; the INSTALL is on the shared computer, so
  once downloaded TRAX is world-scoped like the shelf/plugins. If a second player
  buys a stick after install, Tev's hub should not offer the TRAX purchase again
  when it's already installed (guard the re-pitch on world install state).
- Watch the app-grid trap: adding/removing a computer tile at runtime has bitten
  before (AppGridCols / packed grid nav) — the tile appearing post-install must not
  break grid navigation.
- `[OPEN]` for Sam: starting money in the player's LOCKER — amount? (Design says
  locker cash + fishing are the intended $20 sources. Locker cash may not exist yet.)
- Do NOT wire TRAX purchase into TevShopUI tabs — it's a dialogue purchase for now;
  moves to a physical counter item when the shop interior gets built.

## 7. Stub only — radio impression hook

Add a single optional prefix-line slot in front of Tev's greeting, keyed on a global
`RadioImpression` enum (None/Star/Fool/Mystery), default None = no line. Do NOT
build the interview or the enum's setters. Just leave the seam so every NPC greeting
can take a prefix later.

## 8. `[TEST]` Acceptance

1. Fresh save: talk to Tev → greeting fires; all of A(A1/A2/A3)/B/C reachable; every
   branch reaches the pitch; A1's "..." is the only reply available at that node.
2. YES with $20+ → money −20, TRAX USB Stick in hotbar, +3 blank demo tapes, hub
   thereafter shows shop access.
2b. Computer before purchase shows NO TRAX tile; open computer with stick in
   inventory → stick consumed, greyed tile + DOWNLOADING bar ~6s, then usable;
   closing the terminal mid-download doesn't reset it; grid nav survives the tile
   appearing; hub never re-offers TRAX once installed (co-op included).
3. YES broke → broke line, no deduction, re-pitch available from hub.
4. NO → exit line; re-talk gives hub re-pitch, not the full tree.
5. No rent anywhere: no lawn opener, no haggle, no arrears, plugins tab NEVER locks.
6. Tree is identical regardless of in-game day. Met-flag persists through save/load.
7. Suites green with TevRent vaulted; port/taste/library suites untouched.
8. Report anything found mid-work that contradicts §1 before improvising.
