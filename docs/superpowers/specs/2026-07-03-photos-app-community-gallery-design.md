# Photos App + Community Gallery — Design

**Date:** 2026-07-03
**Source plan:** `docs/photos-app-build-plan.md` (reviewed against the actual codebase; this spec supersedes it where they differ)

Two features, one system:

- **Feature A — Photos app (in-game phone):** a Photos tile on the phone opens a fullscreen gallery of the player's captured photos via a rotate-and-grow transition. Fullscreen photos can be uploaded (title + description) to a community server.
- **Feature B — Community Gallery (main menu):** a main-menu button that browses every *approved* uploaded photo from the server.

**Backend:** Cloudflare Workers + R2 + D1 (all free tier).

---

## What already exists (verified in code, 2026-07-03)

The build plan was written without knowledge of the current phone implementation. Verified reality:

- `PlayerPhoneUI.cs` (~3,000 lines, `Assets/3 - Scripts/UI/`) is a **screen-space overlay canvas**, procedurally built, auto-singleton (seeded in `MainMenuController.EnsureGameplaySingletons` per CLAUDE.md trap #1).
- **Camera mode already exists** (C key): live world feed via a dedicated Camera → RenderTexture, iPhone-style shutter, `SnapPhoto()` with preview animation, and video recording to Motion-JPEG AVI (`PhoneAviMjpegWriter`).
- **Photos already save to disk**: `SnapPhoto()` writes PNGs to `<game folder>/Photos/photo_<timestamp>.png` (`Application.dataPath/../Photos`).
- **The phone already rotates portrait ↔ landscape** (R key) with an eased tween (`ApplyOrientation` / `RotatePhoneRoutine`) — the core building block for the rotate-and-grow transition.
- **App-launch pattern**: tiles call `OnAppClicked` → `CloseThenOpen` → phone slides away, a separate fullscreen UI singleton opens (Fishingdex, BuildMenu, TabbedPauseMenu, SolarSystemMap). The app grid is a 2-column `GridLayoutGroup` with 4 tiles on page 0 of 4 phone pages.
- **Main menu** (`MainMenuController.cs`) is procedural: three buttons (START GAME / CREDITS / EXIT GAME) built by `BuildButton`.
- **`UnityWebRequest` already in use** (HAL voice streaming, streaming audio) — no new dependencies.

Photos can be **portrait or landscape** — camera-mode rotation changes the captured crop. The gallery must letterbox, not assume one aspect.

---

## Decisions (locked with user, 2026-07-03)

| Decision | Choice |
|---|---|
| Photos app transition | **Rotate-and-grow** — phone rotates to landscape while scaling until its screen fills the viewport, crossfade to fullscreen gallery canvas; reversed on exit |
| Moderation | **Approval queue** — uploads land `approved = 0`; public GET returns approved only; admin page to approve/reject |
| Videos | **Photos only for v1** — video recording keeps working as-is; videos do not appear in the gallery and cannot be uploaded |
| Storage | **Keep `<game folder>/Photos/`, fresh start** — new JPG + thumbnail + manifest pipeline; pre-existing `photo_*.png` files stay on disk but are NOT adopted into the gallery |
| Code structure | **Approach A** — separate components with a thin `PlayerPhoneUI` hook (file is too large to grow further; main-menu gallery reuses the grid/viewer with a different photo source) |

---

## Architecture

New scripts (all under `Assets/3 - Scripts/UI/Photos/` unless noted):

| Component | Role |
|---|---|
| `PhotoLibrary` | `DontDestroyOnLoad` auto-singleton. Owns `Photos/manifest.json`, saves full JPG + thumb from a captured `Texture2D`, exposes the photo list. **Must be seeded in `EnsureGameplaySingletons`** (trap #1). |
| `PhotoGalleryUI` | Fullscreen gallery auto-singleton (grid + viewer + upload modal) on its own `Screen Space – Overlay` canvas with `CanvasScaler` (Scale With Screen Size). Also seeded in `EnsureGameplaySingletons`. |
| `PhotoGridWidget` | Reusable grid + fullscreen-viewer building blocks, parameterized by an `IPhotoSource` (local disk vs. remote server). Shared by `PhotoGalleryUI` and `CommunityGalleryUI`. |
| `GalleryApiClient` | `UnityWebRequest` wrapper for the server contract: multipart POST upload, paginated GET, image download with disk cache. |
| `CommunityGalleryUI` | Main-menu-only panel built/owned by `MainMenuController` (not an auto-singleton — it never exists in gameplay). Uses `PhotoGridWidget` with the remote source. |
| Worker (separate folder `server/photo-gallery/`, not under `Assets/`) | Cloudflare Worker: routes, R2 storage, D1 metadata, admin page. Deployed with `wrangler`. |

`PlayerPhoneUI` changes are minimal and appended per conventions:
1. A **Photos tile** in the app grid. Intended layout: the apps page gains a third row (its `PageHost` preferred height grows; the page-nav "reserved zone" below it has `flexibleHeight = 1` and absorbs the difference), keeping the existing 78 px cells. Fallback if the screen gets crowded: shrink cells to fit.
2. `SnapPhoto()` hands its captured `Texture2D` to `PhotoLibrary.SavePhoto(tex)` instead of writing a PNG itself.
3. A transition hook: the Photos tile triggers rotate-and-grow instead of the standard `CloseThenOpen`.

---

## 1. Local photo pipeline (`PhotoLibrary`)

On `SavePhoto(Texture2D tex)`:

- Generate `id` (GUID). Write `Photos/{id}.jpg` (`EncodeToJPG(85)`).
- Downscale (GPU blit to a small RT, or CPU) so the longest edge is 256 px → `Photos/{id}_thumb.jpg`.
- Append a manifest entry and write `Photos/manifest.json` (JsonUtility-friendly: a wrapper class with a `List<PhotoEntry>`):
  `id`, `capturedAt` (ISO-8601 string), `width`, `height`, `uploaded` (bool), `uploadedTitle` (string, empty until uploaded).
- Manifest is loaded lazily on first access; missing/corrupt manifest → rebuilt as empty (photos with no entry are ignored, per fresh-start decision). A manifest entry whose files are missing on disk is dropped at load.

Not part of the save system: photos are the player's photo roll, not game progress. They survive New Game (**no `NewGameReset` hook**) and never touch `SaveCollector` (no apply-order risk). Old PNGs and videos in the folder are untouched.

## 2. Photos app UI + rotate-and-grow transition

**Entry:** Photos tile on the phone home screen.

**Transition (forward, ~0.5 s, eased):**
1. Drive the existing chassis `RectTransform`: rotate Z to −90° (reuse/extend `RotatePhoneRoutine`), scale up from 1.5× toward screen-filling, and move to canvas center — one coroutine animating all three.
2. Target scale is computed from the canvas rect vs. the rotated chassis so the *screen interior* covers the viewport on any aspect (slight overshoot is fine — chassis bezel goes off-screen).
3. At peak, fade in `PhotoGalleryUI`'s canvas (0.1–0.15 s), then hide the phone and restore its transform state for next open.

**Exit:** exact reverse — show the phone pre-scaled/rotated behind the gallery, fade the gallery out, shrink/rotate back to the portrait home screen.

**Edge cases:** entering from camera mode is out of scope (tile lives on the home screen); phone force-close paths (`ForceCloseNoAnim`, conversation start, scene load) must also close the gallery and cancel the transition coroutine.

**Gallery layout:**
- Grid: `ScrollRect` + `GridLayoutGroup`, thumbnails only, newest first, letterboxed cells (photos are mixed-aspect). Plain instantiation is fine to a few hundred photos; virtualization is explicitly out of scope for v1.
- Fullscreen viewer: loads full-res JPG into one `RawImage` (`LoadImage` on a file-read byte array), `Destroy()`s the texture on close. Shows an Upload button (or "Uploaded ✓" badge if already uploaded).
- Back chain: viewer → grid → (reverse transition) → phone home. ESC follows the chain; input gating mirrors the phone's `LookBlocked` pattern (a `PhotoGalleryUI.IsOpen` gate consumed the same way other fullscreen UIs gate the player).

## 3. Upload flow (client)

- Modal over the fullscreen photo: Title (single-line, required), Description (multiline, capped ~500 chars), Submit / Cancel.
- Before POST: re-encode so the longest edge ≤ 1280 px, JPG quality 80 (~200–400 KB). This is the cost lever that keeps the backend free — non-negotiable.
- `UnityWebRequest.Post` multipart: `MultipartFormFileSection("image", bytes, "photo.jpg", "image/jpeg")` + `MultipartFormDataSection` for `title`, `description`. A shared-secret header (`X-Upload-Key`) is sent; it's extractable from the build but stops casual scripted abuse.
- UX: Submit disabled + spinner during upload; success → mark manifest `uploaded = true` + `uploadedTitle`, confirmation, badge in viewer/grid; failure → readable error + retry. Timeout ~30 s.
- Uploads are one-way; there is no edit/delete-from-server in the game client (moderation happens on the admin page).

## 4. Server — Cloudflare Worker + R2 + D1

Contract (host-agnostic, matches the plan's Part 6 plus approval):

```
POST /photos            multipart: image, title, description   [X-Upload-Key required]
  -> 201 { id, imageUrl, title, description, createdAt }       (lands approved = 0)
GET  /photos?limit=20&cursor=<opaque>                          (approved = 1 only, newest first)
  -> 200 { items: [{ id, title, description, imageUrl, createdAt }], nextCursor }
GET  /img/{id}          streams from R2, Cache-Control: public, max-age=31536000, immutable
                        (approved photos only — otherwise the bucket becomes free anonymous
                         image hosting for never-approved uploads; admin page passes the
                         admin secret to view pending images)
GET  /admin             tiny HTML page: pending photos w/ Approve / Reject   [admin secret]
POST /admin/{id}/approve | /admin/{id}/reject                  [admin secret]
```

- **One image** (the client's ≤1280 px upload) serves as both thumb and full view — no server-side resizing (plan's "keep it dumb" call). `thumbUrl` is dropped from the contract; clients use `imageUrl` everywhere.
- **Validation at the Worker:** content-type must be `image/jpeg`, body ≤ 1 MB, title required/length-capped, JPEG magic bytes checked. Reject otherwise.
- **D1 schema:** `photos(id TEXT PK, title TEXT, description TEXT, created_at INTEGER, approved INTEGER DEFAULT 0, size INTEGER)`. Cursor pagination on `created_at DESC, id`.
- **R2:** private bucket; images only reachable through the Worker route (lets cache headers + future takedowns work).
- **Rate limit:** per-IP counter (KV or D1) on POST — e.g. 10 uploads/hour/IP.
- **Secrets:** `UPLOAD_KEY` (game client header) and `ADMIN_KEY` (admin page) via `wrangler secret put`. Reject requests missing them.
- **Reject** deletes the R2 object and D1 row. No report button in v1 — nothing unapproved is ever visible.

## 5. Community Gallery (main menu)

- Fourth `BuildButton` in `MainMenuController`: **COMMUNITY GALLERY** (between CREDITS and EXIT GAME).
- Panel matches the menu's procedural style: paginated grid (20/page, load-more-on-scroll) of downloaded images, tap → fullscreen with title + description.
- Downloaded images cached to `Application.temporaryCachePath/GalleryCache/{id}.jpg`; cache checked before network.
- Failure handling: unreachable server → "Couldn't reach the community gallery" + Retry; the menu never blocks or breaks offline.
- The server URL and upload key live in one config constant file (`GalleryConfig.cs`) so swapping hosts later is a one-line change.

## 6. One-time setup the user performs (~20 min, free, detailed steps in the implementation plan)

1. Create a free Cloudflare account.
2. Install Node.js LTS (nodejs.org installer).
3. `npx wrangler login` (browser, one click).
4. Create resources: `npx wrangler r2 bucket create ...`, `npx wrangler d1 create ...`, paste the printed IDs into `wrangler.toml`, apply the schema, `npx wrangler deploy` → free `*.workers.dev` URL.
5. Set two secrets (`UPLOAD_KEY`, `ADMIN_KEY`) via `wrangler secret put`; put the URL + upload key into `GalleryConfig.cs`.

No domain, no credit card. Free-tier envelope: Workers 100K req/day, R2 10 GB + zero egress, D1 5 GB; the ≤1280 px client-side downscale keeps a healthy community inside it.

## 7. Build order (each chunk playable/testable alone)

1. **`PhotoLibrary`** — JPG + thumb + manifest; `SnapPhoto` routed through it. Verify: photos persist across restart; old PNGs ignored; videos unaffected.
2. **Gallery grid + viewer** — launched plainly from the new Photos tile (no animation yet). Verify: mixed-aspect letterboxing, full-res load/unload, back chain, input gating.
3. **Rotate-and-grow transition** — forward + reverse; force-close paths cancel cleanly. Verify on 3440×1440 and a 16:9 window.
4. **Upload modal + client networking** — POST against a local stub; confirm payload shape and downscale size.
5. **Worker + R2 + D1** — deploy (user does the one-time setup here), wire the real URL, admin approval page.
6. **Community Gallery** in the main menu — pagination, disk cache, offline behavior.
7. **Hardening** — rate limit, validation tightening, polish (badges, spinners, error copy).

## Testing notes

No CLI test runner in this project — verification is manual in the Editor plus (for trap #1) a Windows build check that both new singletons exist when booting via MainMenu. Server pieces are testable outside Unity with `curl` against `wrangler dev` before the game ever talks to it.

## Out of scope for v1

Video playback/upload, adopting legacy PNGs, gallery virtualization/pagination for local photos, in-game photo deletion, community report button, likes/usernames/social features, server-side thumbnails.
