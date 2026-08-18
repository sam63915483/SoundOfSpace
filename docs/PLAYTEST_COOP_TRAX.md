# Co-op TRAX playtest checklist

One session, two machines. Built 2026-08-18 across four commits on
`feat/helmet-hud`; all four headless suites green and both assemblies compile.
Nothing below has been play-verified — that is what this list is for.

**Before you start:** open Unity once so it generates `.meta` files for the six
new scripts, then `git add` them (`TraxSync`, `TraxSongWire`, `PersonalSync`,
`AlienSync`, `TraxSessionSync`, `ShuttleComputerCoopUI`). New files need an
explicit add — `git commit -a` skips them.

Host = whoever creates the session. Guest = whoever joins by code.

---

## 0. Sanity (5 min, do this first — everything else depends on it)

- [ ] Host a session, guest joins by 4-digit code, guest wakes from the pod.
- [ ] Both players can see each other walking around, nametags correct colour.
- [ ] No red errors in either Console. Yellow warnings are fine.

If the guest never wakes or the world is empty, stop — nothing below will work.

---

## 1. The shared computer (Phase B — the new and risky one)

Both players sit at the shuttle computer at the same time.

- [ ] **Ghost cursor.** Each of you sees the other's pointer moving, tinted with
      their suit colour, with their name beside it. It should land on the same
      knob they're actually touching, not offset.
- [ ] **Different screens.** One goes to the shelf, one stays on the arranger:
      the pointer disappears and a chip appears bottom-right saying
      "NAME IS BROWSING THE SHELF".
- [ ] **Live editing.** Guest turns a dial → host sees it move within ~a quarter
      second. Then host turns one → guest sees it. Try it both directions; the
      host→guest direction goes through a different code path than guest→host.
- [ ] **Same knob at once.** Both grab the same dial and drag. Expect the value
      to fight and settle on whoever moved last. It should NOT lock up, snap
      back and forth forever, or desync permanently — let go and both screens
      must agree.
- [ ] **Sections.** Add a section, delete a section, change a section's length.
      The other screen's strip should rebuild to match.
- [ ] **Selection is yours.** While your partner edits, the section YOU have
      selected must not jump. This is the thing most likely to be wrong.
- [ ] **Shared playback.** One presses PLAY TRACK — both hear it, starting in
      the same bar. One presses STOP — both stop. Try LOOP SECTION too.
- [ ] **Ruler seek.** One clicks a bar in the ruler while playing → both jump.
- [ ] **Close one side.** One player walks away from the computer; the other's
      music must keep playing and their ghost cursor must vanish (within 5s at
      worst).

## 2. The shelf, the rack and the machine (Phase A)

- [ ] **Save a project as the guest.** It appears on the host's shelf. Then save
      one as the host and check it reaches the guest.
- [ ] **Overwrite.** Save over an existing project from the other machine; one
      row, not two.
- [ ] **Delete.** Delete a project from one machine; it disappears on both.
- [ ] **Buy a plugin as the GUEST.** Guest's money goes down, and the module
      appears in the rack on BOTH computers. Host's money must not change.
- [ ] Buy one as the host too — same result the other way.
- [ ] **Insert a blank as the guest.** The cassette slides in on both screens,
      one blank leaves the guest's hotbar, none leaves the host's.
- [ ] **Both insert at once.** Have both players press F on the slot in the same
      second. One wins; the loser's blank must come back to their hotbar rather
      than vanishing. This is the money-losing case — check the hotbar count.
- [ ] **Print as the guest.** The tape ejects on both screens.
- [ ] **Take the tape as the host** (the one the guest printed). It goes into the
      host's hotbar and off the machine on both screens.
- [ ] **Eject an unprinted blank** from each side; it returns to that player's pack.

## 3. Rent, Tev and the career (Phase A)

- [ ] **Guest pays rent.** Guest's money goes down; the balance goes down on
      BOTH machines. Host's money untouched.
- [ ] **Host pays rent** — same.
- [ ] **Day roll.** Wait for a day to tick over (24 real minutes, or use the dev
      day-skip). Both players see ONE rent notice, and the balance goes up by
      exactly one day's rent — not two.
- [ ] **Lockout.** Get 5 days behind. Tev refuses plugins to both players, still
      sells blanks to both.
- [ ] **Career count.** Sell tapes as both players; the 10/25 milestones should
      count both players' sales toward one total.
- [ ] **Fresh join.** Have the guest disconnect and rejoin. They should arrive
      knowing the current rent debt and story state immediately — not a clean
      slate. (This was a real bug before this work.)

## 4. Walking customers (Phase C)

- [ ] **Both see the strollers.** Stand near the same aliens; they should be in
      the same places on both screens and walk in step, not teleport or moonwalk.
- [ ] **Legs.** Their legs animate on the guest's screen, not a slide.
- [ ] **The ambush.** Get a buyer's craving up (sell them things they like, then
      leave them a day). When one walks up, BOTH players should see it happen.
- [ ] **Ambush the guest.** The important one: an ambush must be able to target
      the guest, not just the host. Have the guest stand alone near aliens.
- [ ] **Proximity pause.** Talk to an alien as the guest — it must not stroll
      away mid-conversation.
- [ ] **Walk apart.** Separate by 500m. Aliens near the guest should still walk
      around (locally) rather than freezing.

## 5. Saving (Phase D — check this last, it's the destructive one)

Back up `%AppData%\..\LocalLow\DefaultCompany\Solar System 2\saves\` first.

- [ ] **No save button.** The pause menu no longer has SAVE GAME. Nothing
      autosaves on a timer any more.
- [ ] **Host saves at the pod.** Seal in, upload completes, a file appears.
- [ ] **Guest saves at the pod.** Same ritual; a file appears on the GUEST's
      machine, under the guest's own slot name. Should take a beat longer (it
      fetches the world from the host).
- [ ] **Both players' stuff is in there.** After a guest save, load that file on
      the guest's machine in single player. The guest's own hotbar, money and
      equipment should come back — not the host's.
- [ ] **Load as the host on the host's file** — host's own belongings come back.
- [ ] **Second character.** Make a new character and load an existing world with
      it. You should start empty-handed with a blank orientation board, and NOT
      be handed the other character's hotbar.
- [ ] **Old saves still load.** Load a save from before today. Everything should
      restore exactly as it used to.
- [ ] **Orientation board.** It's world progress now: a character who finished
      it in one world gets a blank board in a NEW world. Confirm ticking a line
      and then uploading at the pod keeps it after a reload.
- [ ] **Backrooms round trip** still works (it uses the old autosave slot as a
      transfer, which was deliberately kept).
- [ ] **Die early in a new game** — it should reload the fresh run, not an older
      one.

---

## Known limits, so they don't read as bugs

- Aliens are only pose-synced where both machines have streamed the same one in.
  Walk far apart and each of you drives your own; that's intended.
- A guest's save is a copy of the host's world at that moment. If the host is
  mid-fight, that's what gets written.
- Editing the same knob simultaneously settles on last-write-wins by design —
  no locks, per your call.
- Money stays personal everywhere. Only the rent DEBT is shared.
- Unread message counts are still shared, not per-player (pre-existing).

## If something's wrong

Console filter `[TraxSync]`, `[PersonalSync]`, `[AlienSync]`, `[TraxSession]`
and `[WorldSync]` cover the new paths. The most likely failure shapes:

| Symptom | First suspect |
|---|---|
| Edits go one way only | the host relay skipping the wrong client |
| Cursor offset from where they're pointing | the normalisation rect |
| A blank tape vanished | the insert refusal/refund round trip |
| Guest's save has the host's pockets | `SelectPersonalBlock` picking the wrong id |
| Rent double-charges | the guest's day-roll accrual gate |
| Aliens frozen for the guest | the 2-second remote-drive expiry |
