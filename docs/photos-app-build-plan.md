# In-Game Photos App + Community Gallery — Build Plan
### *Sound of Space*

Two features, one system:

- **Feature A — Photos app (on the in-game phone):** rotate the phone to landscape, animate it "overtaking" the screen, browse a grid of the photos you've taken, tap one to view fullscreen, back out, then reverse the rotation to return to normal. On a fullscreen photo, an **Upload** button lets you add a title + description and push it to a server.
- **Feature B — Community Gallery (main menu):** a button that pulls every uploaded photo from the server so other players can see them.

Recommended backend: **Cloudflare Workers + R2 + D1** (all free tier). See the chat message for the pricing rationale; the API here is host-agnostic if you swap later.

---

## Part 1 — The rotate-to-fullscreen animation (the tricky bit)

The clean way to do this is a **presentation swap**, not a literal "zoom the camera into the tiny phone screen." Trying to render a scrollable grid + fullscreen viewer + an upload form on a world-space RenderTexture screen and then dolly into it leaves you with low-res UI and painful input raycasting.

Instead, think in **three states** with an animated bridge between them:

1. **Handheld** — the phone as it normally appears (portrait). Apps interactive at this scale.
2. **Transition** — animate rotation to landscape *and* grow/dolly until the phone screen fills the viewport (~0.4–0.6s, ease-in-out).
3. **Fullscreen app** — a dedicated **full-screen landscape UI Canvas** renders the actual gallery. The handheld phone is hidden (or frozen) behind it.

Because the transition ends with the phone filling the frame, cross-fading to the fullscreen canvas at that moment reads as one continuous motion. Reverse the whole thing to exit.

### Two variants depending on how your phone is built

**If the phone is a world-space 3D model (held in hand, rendered by the game camera):**
- Drive a coroutine with an `AnimationCurve` for easing; lerp the phone's local rotation 90° to landscape and either move it toward the camera or dolly a dedicated "phone camera" in so the screen fills the view.
- A separate camera that frames the phone gives a consistent fill regardless of where the player is looking. Blend main → phone camera during the transition.
- At the end of the grow, enable the fullscreen `Screen Space - Overlay` canvas and disable the world phone.

**If the phone is a screen-space UI panel (a RectTransform on a canvas):**
- Animate the panel's `localRotation` (Z: 0 → ±90), `localScale` (up to fill), and anchored position (to center), again via coroutine + `AnimationCurve`.
- At full size, enable the fullscreen gallery canvas on top (or swap the panel's content to the gallery layout).

### Details that matter
- **Aspect ratio:** you run a 3440×1440 ultrawide, so don't hardcode 16:9. Put the fullscreen gallery on its own canvas with a `CanvasScaler` set to *Scale With Screen Size* + sensible anchoring so the grid reflows on any aspect.
- **Input gating:** disable player look/move while the fullscreen gallery is open and route input to the UI (you likely already gate input when the phone is raised).
- **Back buttons:** wire them to state transitions, not scene loads — fullscreen photo → grid → (reverse rotation) → handheld.
- **Tweening:** coroutine + `AnimationCurve` is dependency-free and fine. If you already have DOTween, `DORotate`/`DOScale`/`DOMove` on a sequence is tidier. Your call.

---

## Part 2 — Local photo storage & capture

Photos you take should persist between sessions, so write them to disk.

- **Capture:** in-game camera → `RenderTexture` → `ReadPixels` into a readable `Texture2D` → `EncodeToJPG(quality)` (JPG, not PNG — far smaller for screenshots).
- **Save location:** `Application.persistentDataPath/Photos/{guid}.jpg`. This survives game updates.
- **Save a thumbnail too:** downscale to ~256px and save `{guid}_thumb.jpg` alongside. Cheapest option at browse time (no on-the-fly resizing).
- **Index:** keep a small JSON manifest (`id`, `filename`, `capturedAt`, plus anything in-world you want later like location/context). Sort by `capturedAt` for the grid. (You *can* just enumerate the folder, but an explicit manifest makes ordering + future metadata easier.)

---

## Part 3 — Gallery UI (grid + fullscreen viewer)

- **Grid:** `ScrollRect` + `GridLayoutGroup`, one cell per photo, showing **thumbnails only**. Cell = a button that opens the fullscreen viewer for that photo id.
- **Fullscreen viewer:** loads the **full-res** JPG on open into a single `RawImage`/`Image`, `Destroy()`s that texture on close. Never hold every full-res texture in memory at once.
- **Scale:** a simple all-thumbs grid is fine up to a few hundred photos. Beyond that, virtualize (pool cells, only instantiate visible ones) or paginate. For a personal photos app you'll likely never need this.
- **Back navigation:** fullscreen → grid (unload full-res) → exit (reverse the Part 1 rotation).

---

## Part 4 — Upload flow (client)

- On a **fullscreen photo**, show an **Upload** button → opens a small modal: `Title` (single-line `InputField`), `Description` (multiline `InputField`), Submit / Cancel. Require a title; cap description length.
- **Downscale before upload.** Re-encode to a max edge of ~1280px at JPG quality ~80 → roughly 200–400 KB per image. This one setting directly controls your storage + bandwidth (and therefore whether you ever leave the free tier), so don't skip it.
- **Send it:** `UnityWebRequest.Post` with a multipart body — `MultipartFormFileSection` for the image bytes plus `MultipartFormDataSection` for `title` and `description`.
- **UX:** disable Submit + show a spinner while uploading; on success show a confirmation and optionally mark that photo as "uploaded" in your manifest; on failure show an error with a retry.

---

## Part 5 — Community Gallery (main menu)

- **View Photos** button → panel/scene that `GET`s `/photos` (paginated).
- Populate a scrollable thumbnail grid by downloading each `thumbUrl` via `UnityWebRequestTexture`. Tap → fullscreen with the title + description shown; download the full `imageUrl` on demand.
- **Cache** downloaded textures to `Application.temporaryCachePath` keyed by photo id so you don't re-download on every visit.
- **Infinite scroll / pagination:** fetch pages of ~20 and load more as the user scrolls. Keeps memory and request counts bounded.

---

## Part 6 — Server API contract (host-agnostic)

```
POST /photos
  Content-Type: multipart/form-data
  fields: image (jpg file), title (string), description (string)
  -> 201 { id, imageUrl, thumbUrl, title, description, createdAt }

GET /photos?limit=20&cursor=<opaque>
  -> 200 { items: [ { id, title, description, imageUrl, thumbUrl, createdAt } ], nextCursor }

GET  /photos/{id}            (optional — single photo)
DELETE /photos/{id}          (moderation — protected by a secret only you hold)
POST /photos/{id}/report     (optional — community reporting)
```

---

## Part 7 — Recommended backend mapping (Cloudflare Workers + R2 + D1)

One Worker handles all routes:

- **POST /photos:** validate content-type + size → generate `id` → `env.BUCKET.put(id, bytes)` into R2 → `INSERT` metadata row into D1 → return URLs.
- **GET /photos:** query D1 for a page (order by `createdAt`, cursor pagination) → return JSON.
- **Serving images:** either expose a public R2 URL, or (cleaner) serve through a Worker route that streams from R2 so you can keep the bucket private and add cache headers.
- **Thumbnails — keep it dumb:** server-side image processing is the awkward part on Workers. Simplest free approach: the client uploads **one** downscaled ~1280px image and you use it for both grid and fullscreen (screenshots at that size look fine shown small in a grid). If you later want true thumbs, have the client upload a second tiny image, or add Cloudflare Images (paid). Don't build server-side resizing for v1.
- **Cache headers (important):** serve images with `Cache-Control: public, max-age=31536000, immutable`. Cloudflare's edge then serves cached copies, so repeat views don't count as R2 read ops — this is what keeps a popular gallery inside the free tier.

---

## Part 8 — Abuse & moderation (do not ship without this)

A public, player-visible upload endpoint is a magnet for junk and worse. Because the intended content is in-game screenshots, casual misuse is lower — but a determined actor can reverse-engineer the endpoint, so don't trust the payload server-side.

**Endpoint hardening:**
- Reject non-image content-types and oversized bodies (e.g. > 1 MB) at the Worker.
- Rate-limit per IP (Cloudflare rate-limiting rules, or a KV/D1 counter).
- Optional shared-secret header the game sends. It's extractable, but it stops casual scripted abuse.

**Moderation — pick your tolerance:**
- **Floor:** a protected `DELETE /photos/{id}` (admin) + a `report` button.
- **Safer for a solo dev with a Steam launch:** an `approved` flag. Uploads land as `approved = false`; the public `GET` only returns `approved = true`. Nothing bad is ever publicly visible. Cost: upload→visible latency and your time approving (a simple protected admin view, or just flipping the flag in D1). Given reputational stakes, I'd lean this way at least for launch.
- **On-brand touch:** watermark/overlay photos at capture time so the gallery stays clearly "of the game" (doesn't stop a reverse-engineer, but keeps casual uploads looking right).

---

## Part 9 — Suggested build order (hand to Claude Code in chunks)

1. **Local capture + storage.** Save full + thumb JPGs to `persistentDataPath`, write the JSON manifest. Verify photos persist across a restart.
2. **Grid + fullscreen viewer** (still portrait, no animation yet). Thumbs in grid, full-res on open, unload on close, back buttons.
3. **The rotate-to-fullscreen animation.** Build the three states + transition for *your* phone type (world-space vs screen-space). Get the forward + reverse feeling right in isolation, then hook the gallery into the fullscreen state.
4. **Upload modal + client networking.** Title/description form, downscale-before-upload, multipart POST to a stub endpoint. Confirm the payload shape.
5. **The Worker + R2 + D1.** Stand up `POST` and `GET` against the contract in Part 6. Single-image (no server thumbs), cache headers, size/type validation.
6. **Community Gallery in the main menu.** Paginated GET, thumb grid, fullscreen with metadata, disk caching.
7. **Moderation + hardening.** Rate limit, delete endpoint, and your chosen approval/report path.

---

*Backend note: everything above sits on Cloudflare free tiers (Workers 100K req/day, R2 10 GB + zero egress, D1 5 GB). The single biggest cost lever is Part 4's downscale step — keep uploads ~300 KB and this stays effectively free even with a healthy community.*
