# TASK — Readable Newspaper Pickup ("Press F to read" → page through articles → open real source)

**Project:** Sound of Space (Unity 2022.3, Built-in Render Pipeline)
**Goal:** Let the player press **F** on a stack of papers on a table to open a reading view, page through several short articles with on-screen arrows + keyboard, and open the real-world source article directly in a browser. Built as a **reusable, data-driven system** so future monuments/tables can each show their own set of clippings.

---

## 0. Read this first / reuse what already exists

There is already a working **"Press F to play Hunger Strike"** interaction near the lyric monument — it pops up when the player gets close and opens the song on YouTube. Before writing anything new, inspect that interaction and reuse its:
- proximity / "player in range" detection,
- world-space prompt display ("Press F to …"),
- input binding for F (match whatever input path it already uses — old Input Manager vs. new Input System).

**Do not build a second parallel interaction system.** The newspaper interactable should hook into the same prompt + range + input pattern; only the *result* of pressing F differs (open the reader UI instead of a URL).

### IMPORTANT — two F-interactions share the same area
The monument ("Press F to play") and the table papers ("Press F to read") are physically close, and both are bound to **F**. This must not conflict. Required:
1. **Shrink the monument's interaction trigger range** so it no longer overlaps the table — standing at the papers should NOT also arm the monument's play prompt.
2. Add **disambiguation** so only **one** prompt is ever visible and only one action fires: when more than one interactable is technically in range, pick the **nearest** one (or the one the player is looking at — raycast from camera). Whichever you pick, both prompts must never show at once, and F must trigger only the active one.
3. The two prompts stay visually distinct: monument = "Press F to play Hunger Strike", table = "Press F to read".

If the existing interaction logic is generalizable, refactor it into a small shared interface (e.g. `IInteractable.Interact()` + `GetPromptText()`) and let a single "interaction picker" on the player choose the nearest/looked-at target. If that's too invasive right now, mirror the pattern locally but still implement the nearest/look-at pick so the two don't fight.

---

## 1. Decisions (LOCKED — already decided, do not re-ask)

- **Game does NOT pause.** The monument/table sits in an oxygen-safe zone, so there's no suffocation risk while reading. No `Time.timeScale = 0`, no oxygen freeze, no enemy freeze.
- **No confirm popup on the source button.** Pressing "Read the real article" calls `Application.OpenURL(sourceUrl)` **directly** and takes the player straight to the site. (Set the build to borderless/windowed if it isn't, so the browser surfaces cleanly.)
- **While the reader UI is open, suppress player movement and all camera "juice."** Specifically: lock movement input, and neutralize **sprint FOV kick, strafe tilt/lean, and head bobbing** so the camera is perfectly steady while reading. Restore everything on close. (Details in §3 — note the reset-to-neutral requirement, not just freezing input.)

---

## 2. Data (already provided — do not retype the article text)

Use the file **`NewspaperArticles_DataCenters.json`** (shipping with this task) as the content source. Schema:

```
setId      : string   // unique id for this stack of papers
setTitle   : string   // optional flavor title for the reader header
articles[] :
    id         : string
    headline   : string
    date       : string   // display only
    body       : string   // paragraphs separated by "\n\n"
    sourceName : string   // label for the link button, e.g. "Tom's Hardware"
    sourceUrl  : string   // opened directly by the link button
```

**Implementation choice (recommended): ScriptableObject.**
- Create a serializable `NewspaperArticle` class matching the fields above.
- Create a `NewspaperArticleSet : ScriptableObject` holding `setId`, `setTitle`, and `List<NewspaperArticle> articles`.
- Populate one asset (`DataCenterWaterClippings.asset`) from the JSON. A tiny editor utility or one-off import script is fine; the JSON is just the seed. Inspector-editable afterward so I can add articles without touching code.
- (Acceptable alternative: load the JSON at runtime from `StreamingAssets`. Unity gotcha — `JsonUtility` can't deserialize a top-level array, which is why the JSON is wrapped in an object with an `articles` field. Deserialize into a wrapper class, not `NewspaperArticle[]` directly.)

Each table prop references its own `NewspaperArticleSet`, so adding a future table = new asset + new prop, **zero new code**.

---

## 3. Components to build

**`NewspaperInteractable` (MonoBehaviour on the table-papers prop)**
- Serialized field: `NewspaperArticleSet articleSet`.
- Reuses the existing range/prompt system + the nearest/look-at picker from §0.
- When it's the active in-range interactable, show prompt `"Press F to read"`.
- On F (only when active and reader not already open): call `NewspaperReaderUI.Open(articleSet)`.
- Hide the world prompt while the reader is open; restore when closed.

**`NewspaperReaderUI` (MonoBehaviour on a UI Canvas, one shared instance)**
- `Open(NewspaperArticleSet set)`:
  - store set, set `currentIndex = 0`, show canvas, render page,
  - unlock + show cursor,
  - **enter reading state** (see "Reading state" below).
- `Close()`:
  - hide canvas, re-lock + hide cursor,
  - **exit reading state** (restore movement + camera effects),
  - return control to player.
- `Next()` / `Prev()`: clamp `currentIndex` to `[0, articles.Count-1]`, re-render, reset scroll to top. (Default: hard clamp at ends and grey out the dead arrow. No wrap.)
- `OpenCurrentSource()`: `Application.OpenURL(current.sourceUrl)` directly — no popup.
- Render method fills: header (`setTitle`), `headline`, `date`, `body` (multi-paragraph — §4), source button label (`"Read the real article — {sourceName} ↗"`), and page indicator (`"{currentIndex+1} / {count}"`).

**Reading state (movement + camera suppression)**
This is the part to get right. When the reader opens:
- **Lock movement/look input** on the player controller (don't let WASD/mouse-look drive the character behind the UI; F-interact also suppressed).
- **Neutralize camera juice — reset to base, don't just freeze:**
  - **Sprint FOV kick:** set camera back to its base/default FOV (lerp or snap), and stop any FOV-kick coroutine/driver so it can't fight you.
  - **Strafe tilt / lean:** zero out the camera roll/tilt (return Z-rotation/lean to 0) and disable the tilt driver.
  - **Head bob:** reset the bob offset to neutral (origin) and disable the bob driver.
  - The goal: a dead-steady camera while reading. Simply blocking input often leaves a mid-interpolation tilt/FOV/bob frozen on screen — explicitly return each to its rest value.
- On `Close()`, re-enable the controller + all three drivers so sprint kick, tilt, and bob behave normally again.
- Implement enter/exit as a single `SetReadingState(bool)` so it's one switch. If these effects live on known components (e.g. a head-bob script, a FOV-kick script, a camera-tilt script), expose enable flags on them and toggle from here; leave `// TODO` notes if a driver isn't found so I can point you at it.

---

## 4. UI layout (uGUI Canvas, Screen Space – Overlay)

A document/paper panel, centered, styled like a newspaper clipping (off-white, dark text — legible against the game's dark palette). Hierarchy:

```
NewspaperReaderCanvas
  Dimmer (full-screen semi-transparent black, blocks clicks behind)
  PaperPanel
    HeaderText        (setTitle — small, kicker style)
    HeadlineText      (large, bold)
    DateText          (small, muted)
    BodyScroll (ScrollRect)
        Viewport / Content
            BodyText  (TMP, anchored to grow vertically)
    Footer
        PrevButton    ("◀")
        PageIndicator ("1 / 6")
        NextButton    ("▶")
    SourceButton      ("Read the real article — {sourceName} ↗")
    CloseButton       ("✕  (Esc)")
```

Notes:
- Use **TextMeshPro** for body text. Paragraph breaks: keep `\n\n` as-is (TMP honors newlines).
- ScrollRect so long articles never clip (Georgia and "264 Billion" are the longest); reset scroll to top on every page change.
- Make `PaperPanel` a prefab so the look is tweakable in one place.

---

## 5. Input

- **F** — open (handled by the interactable, via existing input path + nearest/look-at picker).
- **Left Arrow / A** — previous page; **Right Arrow / D** — next page.
- **Esc** — close reader.
- On-screen buttons mirror all of the above for mouse users.
- While the reader is open, player look/move + F-interact are suppressed (per Reading state, §3).

---

## 6. Build order

1. Inspect the existing "Press F to play Hunger Strike" interaction (§0). Decide reuse vs. mirror, and add the nearest/look-at picker.
2. **Shrink the monument's interaction range** and verify the two prompts no longer overlap at the table.
3. Add `NewspaperArticle` + `NewspaperArticleSet` ScriptableObject. Import `NewspaperArticles_DataCenters.json` into `DataCenterWaterClippings.asset`.
4. Build the `NewspaperReaderCanvas` + `PaperPanel` prefab (§4), wired but content-empty.
5. Implement `NewspaperReaderUI` (open/close, render, next/prev, page indicator, cursor).
6. Implement **Reading state** — movement lock + camera reset/disable for FOV kick, strafe tilt, head bob; restore on close (§3).
7. Implement `OpenCurrentSource()` → direct `Application.OpenURL`. Set build to borderless/windowed if needed.
8. Implement `NewspaperInteractable`, wire its `articleSet` to the asset, place it on the table-papers prop.
9. Run the test checklist (§7).

---

## 7. Acceptance criteria / test checklist

- [ ] Standing at the papers shows **"Press F to read"** and the monument's **"Press F to play Hunger Strike"** is NOT armed; standing at the monument shows the play prompt and not the read prompt. Never both at once.
- [ ] F opens the reader on page 1; cursor appears and is usable.
- [ ] Arrows (on-screen **and** keyboard) move through all 6 articles; page indicator updates; scroll resets to top each page.
- [ ] At first/last page the out-of-range arrow is greyed/disabled — no index errors.
- [ ] Each page's source button shows the correct outlet name and opens the correct URL **immediately** (no popup).
- [ ] While reading: player can't move or look; camera is **dead steady** — no head bob, no strafe tilt, no sprint FOV kick, even if the player was sprinting/strafing the instant they pressed F.
- [ ] On close (Esc or ✕): cursor re-locks/hides; movement, look, F-interact, head bob, strafe tilt, and sprint FOV kick all behave normally again.
- [ ] Long articles (Georgia, "264 Billion") scroll fully and never clip.
- [ ] Dropping a **second** table with a different `NewspaperArticleSet` works with no code changes (reusability check).

---

## 8. Future-proofing notes

- The whole point is repetition — more of these tables will be added, tied to other lyrics/themes. Keep `NewspaperReaderUI` content-agnostic; everything specific lives in the `NewspaperArticleSet` asset.
- Consider a per-article `read` flag later (for journal/collectible tracking) — not needed now, but leave room on the data class.
- Source names/URLs in the JSON point to the outlet currently hosting each story; any can be swapped for an original-publisher link later.
