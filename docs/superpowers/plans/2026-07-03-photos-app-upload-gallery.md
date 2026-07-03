# Photos App Upload + Community Gallery Implementation Plan — Plan B of 2

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Let players upload a fullscreen photo (title + description) to a Cloudflare Worker, and browse everyone's *approved* uploads from a COMMUNITY GALLERY button on the main menu.

**Architecture:** A tiny `GalleryConfig` holds the server base URL + upload key. `GalleryApiClient` (UnityWebRequest) does downscale-then-multipart POST, paginated GET, and disk-cached image download. `PhotoGalleryUI` (from Plan A) gains an Upload button + modal on the fullscreen viewer. `CommunityGalleryUI`, built and owned by `MainMenuController` (not an auto-singleton — it never exists in gameplay), reuses the grid/viewer idiom against the remote source. The server (`server/photo-gallery/`) is already drafted + security-reviewed; deployment is a user step.

**Tech Stack:** Unity 2022.3, `UnityWebRequest` (already used in this repo — HALVoicePlayer/StreamingAudio), `MultipartFormDataSection`/`MultipartFormFileSection`. No CLI tests — verify via `mcp__coplay-mcp__check_compile_errors` + play/edit checks. Server verified out-of-band with `curl` against `wrangler dev` before the game talks to it.

**Spec:** `docs/superpowers/specs/2026-07-03-photos-app-community-gallery-design.md`. **Depends on:** Plan A (merged/committed — `PhotoLibrary`, `PhotoGalleryUI`), the deployed Worker, and the user's Cloudflare setup (`docs/cloudflare-setup-guide.md`).

**Review cadence (per user feedback — right-size to risk):** one combined spec+quality review per client task; the server already had a full adversarial security review. Prereqs (server deployed, URL+key known) gate Task 4's live test only — Tasks 1-3 build against the contract and can proceed offline.

---

## Server contract (frozen — from the reviewed `server/photo-gallery/src/index.js`)

- `POST {base}/photos` — multipart: `image` (JPEG bytes, filename `photo.jpg`, type `image/jpeg`), `title` (string, required, ≤100 after trim), `description` (string, ≤500). Header `X-Upload-Key: <key>`. Body must be ≤1 MB total. → `201 { id, imageUrl, title, description, createdAt }` where `imageUrl` is a RELATIVE path `/img/{id}` and `createdAt` is epoch-**milliseconds** (integer). Failures: 400 (bad fields), 401 (bad key), 413 (too big), 415 (not JPEG), 429 (rate-limited, >10/hr/IP).
- `GET {base}/photos?limit=20&cursor=<opaque>` → `200 { items: [ { id, title, description, imageUrl, createdAt } ], nextCursor }`. Approved only, newest first. `nextCursor` is null/absent on the last page.
- `GET {base}/img/{id}` → JPEG bytes (approved only; immutable cache headers). Full URL = `base + imageUrl`.

---

## Reference facts (verified 2026-07-03)

- `PhotoLibrary` (`Assets/3 - Scripts/UI/Photos/PhotoLibrary.cs`): `Instance`, `GetPhotosNewestFirst()`, `GetPhotoPath(id)`, `PhotoEntry { id, capturedAt, width, height, uploaded, uploadedTitle }`, and `MarkUploaded(string id, string title)` — already present, wired to persist. Use it to mark + badge uploaded photos.
- `PhotoGalleryUI` (`Assets/3 - Scripts/UI/Photos/PhotoGalleryUI.cs`): fullscreen viewer built in `EnsureBuilt`; `OpenViewer(PhotoEntry)` loads full-res into `_viewerImage`; the viewer RT (`_viewerRT`) already hosts a caption + "< BACK". The currently-viewed entry is a local in `OpenViewer` — Task 2 adds a field `_viewerEntry` to remember it for the upload button. Palette + `NewUI`/`MakeText`/`Stretch` helpers already in the file.
- Main menu (`Assets/3 - Scripts/UI/MainMenuController.cs`): buttons built at lines 177-179 via `BuildButton(buttonsRT, name, label, System.Action)`; `buttonsRT`/`mainMenuButtonsRoot` is the VerticalLayoutGroup column. Modal pattern = `BuildCreditsPanel()` (own GameObject `creditsPanel`, own `Canvas overrideSorting sortingOrder=200`, dim backdrop, card) toggled by `OnCredits()`/`HideCredits()` (SetActive on `creditsPanel` + `mainMenuButtonsRoot`). Menu main canvas sortingOrder=100. Helpers: `NewUI`, `Stretch(rt, l,t,r,b)`, `BuildButtonContent(rt, label, action)`, `ApplyDefaultFont(tmp)`.
- `UnityWebRequest` usage examples in-repo: `Assets/3 - Scripts/AI/HALVoicePlayer.cs`, `Audio/StreamingAudio.cs`.
- House rules: append fields at class end; `Application.temporaryCachePath` for the download cache; `git add` both `.cs` and `.cs.meta`; verify via Coplay compile checks.
- `server/` is now git-tracked (no ignore rule); its own `.gitignore` excludes `node_modules/`, `.wrangler/`, `.dev.vars`. Commit the Worker source.

---

### Task 1: `GalleryConfig` + `GalleryApiClient` (networking, offline-buildable)

**Files:**
- Create: `Assets/3 - Scripts/UI/Photos/GalleryConfig.cs`
- Create: `Assets/3 - Scripts/UI/Photos/GalleryApiClient.cs`

- [ ] **Step 1: Write `GalleryConfig.cs`** — one place to swap host/key later.

```csharp
/// <summary>
/// Community-gallery server config. Swap BaseUrl/UploadKey here after the
/// Worker is deployed (docs/cloudflare-setup-guide.md). UploadKey only stops
/// casual scripted abuse — it ships in the build and is extractable; real
/// protection is server-side validation + the approval queue.
/// </summary>
public static class GalleryConfig
{
    // e.g. "https://photo-gallery.yourname.workers.dev" — NO trailing slash.
    public const string BaseUrl   = "https://REPLACE-ME.workers.dev";
    public const string UploadKey = "REPLACE-ME";

    public static bool IsConfigured =>
        !BaseUrl.Contains("REPLACE-ME") && !UploadKey.Contains("REPLACE-ME");
}
```

- [ ] **Step 2: Write `GalleryApiClient.cs`** — static coroutine helpers; callbacks (no async/await, matches repo style).

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Talks to the community-gallery Worker. All methods are coroutines that
/// invoke a callback on completion (success flag + payload/error). Downscales
/// before upload (the cost lever that keeps the backend free). Downloaded
/// images are disk-cached under temporaryCachePath keyed by photo id.
/// </summary>
public static class GalleryApiClient
{
    [Serializable] public class RemotePhoto { public string id; public string title; public string description; public string imageUrl; public long createdAt; }
    [Serializable] public class ListResponse { public RemotePhoto[] items; public string nextCursor; }
    [Serializable] class UploadResponse { public string id; public string imageUrl; public string title; public string description; public long createdAt; }

    const int   UploadMaxEdge = 1280;
    const int   UploadQuality = 80;
    const int   TimeoutSeconds = 30;

    static string CacheDir => System.IO.Path.Combine(Application.temporaryCachePath, "GalleryCache");

    // ── Upload ──────────────────────────────────────────────────────
    // onDone(success, remoteId, error). Downscales `full` to <=1280px @ q80.
    public static IEnumerator Upload(Texture2D full, string title, string description,
                                     Action<bool, string, string> onDone)
    {
        byte[] jpg;
        try { jpg = EncodeDownscaled(full, UploadMaxEdge, UploadQuality); }
        catch (Exception e) { onDone?.Invoke(false, null, "Encode failed: " + e.Message); yield break; }

        var form = new List<IMultipartFormSection>
        {
            new MultipartFormFileSection("image", jpg, "photo.jpg", "image/jpeg"),
            new MultipartFormDataSection("title", title ?? ""),
            new MultipartFormDataSection("description", description ?? ""),
        };
        using (var req = UnityWebRequest.Post(GalleryConfig.BaseUrl + "/photos", form))
        {
            req.SetRequestHeader("X-Upload-Key", GalleryConfig.UploadKey);
            req.timeout = TimeoutSeconds;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                onDone?.Invoke(false, null, HttpError(req));
                yield break;
            }
            string id = null;
            try { id = JsonUtility.FromJson<UploadResponse>(req.downloadHandler.text)?.id; } catch { }
            onDone?.Invoke(true, id, null);
        }
    }

    // ── List ────────────────────────────────────────────────────────
    // onDone(success, response, error). cursor may be null/empty for page 1.
    public static IEnumerator List(string cursor, int limit, Action<bool, ListResponse, string> onDone)
    {
        string url = GalleryConfig.BaseUrl + "/photos?limit=" + Mathf.Clamp(limit, 1, 50);
        if (!string.IsNullOrEmpty(cursor)) url += "&cursor=" + UnityWebRequest.EscapeURL(cursor);
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = TimeoutSeconds;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { onDone?.Invoke(false, null, HttpError(req)); yield break; }
            ListResponse resp = null;
            try { resp = JsonUtility.FromJson<ListResponse>(req.downloadHandler.text); } catch (Exception e) { onDone?.Invoke(false, null, "Parse failed: " + e.Message); yield break; }
            onDone?.Invoke(true, resp ?? new ListResponse(), null);
        }
    }

    // ── Image download (disk-cached) ────────────────────────────────
    // onDone(success, texture, error). Texture is owned by the CALLER — destroy it.
    public static IEnumerator LoadImage(string id, string imageUrl, Action<bool, Texture2D, string> onDone)
    {
        string cachePath = System.IO.Path.Combine(CacheDir, id + ".jpg");
        // Cache hit.
        if (System.IO.File.Exists(cachePath))
        {
            Texture2D cached = null;
            try
            {
                var bytes = System.IO.File.ReadAllBytes(cachePath);
                cached = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!cached.LoadImage(bytes)) { UnityEngine.Object.Destroy(cached); cached = null; }
            }
            catch { cached = null; }
            if (cached != null) { onDone?.Invoke(true, cached, null); yield break; }
        }
        using (var req = UnityWebRequestTexture.GetTexture(GalleryConfig.BaseUrl + imageUrl))
        {
            req.timeout = TimeoutSeconds;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { onDone?.Invoke(false, null, HttpError(req)); yield break; }
            var tex = DownloadHandlerTexture.GetContent(req);
            try { System.IO.Directory.CreateDirectory(CacheDir); System.IO.File.WriteAllBytes(cachePath, req.downloadHandler.data); } catch { }
            onDone?.Invoke(true, tex, null);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────
    static string HttpError(UnityWebRequest req)
    {
        long code = req.responseCode;
        if (code == 401) return "Upload rejected (bad key).";
        if (code == 413) return "Image too large.";
        if (code == 415) return "Not a valid JPEG.";
        if (code == 429) return "Too many uploads — try again later.";
        if (code == 400) return "Missing or invalid title/description.";
        if (code >= 500) return "Server error — try again later.";
        if (code == 0)   return "Couldn't reach the server. Check your connection.";
        return "Request failed (" + code + ").";
    }

    static byte[] EncodeDownscaled(Texture2D src, int maxEdge, int quality)
    {
        float k = Mathf.Min(1f, (float)maxEdge / Mathf.Max(src.width, src.height));
        if (k >= 1f) return src.EncodeToJPG(quality); // already small enough
        int tw = Mathf.Max(1, Mathf.RoundToInt(src.width * k));
        int th = Mathf.Max(1, Mathf.RoundToInt(src.height * k));
        var rt = RenderTexture.GetTemporary(tw, th, 0);
        var oldActive = RenderTexture.active;
        try
        {
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var small = new Texture2D(tw, th, TextureFormat.RGB24, false);
            small.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
            small.Apply();
            var bytes = small.EncodeToJPG(quality);
            UnityEngine.Object.Destroy(small);
            return bytes;
        }
        finally { RenderTexture.active = oldActive; RenderTexture.ReleaseTemporary(rt); }
    }
}
```

- [ ] **Step 3:** `mcp__coplay-mcp__check_compile_errors` → clean.
- [ ] **Step 4: Commit**
```bash
git add "Assets/3 - Scripts/UI/Photos/GalleryConfig.cs" "Assets/3 - Scripts/UI/Photos/GalleryConfig.cs.meta" "Assets/3 - Scripts/UI/Photos/GalleryApiClient.cs" "Assets/3 - Scripts/UI/Photos/GalleryApiClient.cs.meta"
git commit -m "feat(photos): GalleryConfig + GalleryApiClient (upload/list/cached image download)"
```

---

### Task 2: Upload button + modal on the fullscreen viewer

**Files:**
- Modify: `Assets/3 - Scripts/UI/Photos/PhotoGalleryUI.cs`

Adds, on the viewer: an **Upload** button (top-right, mirroring the BACK button), which opens a modal (Title single-line `TMP_InputField`, Description multiline, Submit/Cancel). Submit disables + shows a spinner, calls `GalleryApiClient.Upload`, on success calls `PhotoLibrary.Instance.MarkUploaded(id, title)` and swaps the button to an "Uploaded ✓" label; on failure shows the error + re-enables. If `!GalleryConfig.IsConfigured`, the Upload button is hidden (feature simply absent until the dev sets the URL).

- [ ] **Step 1:** Add fields at the END of the class: `PhotoLibrary.PhotoEntry _viewerEntry;` plus modal refs (`_uploadModalRT`, `_uploadTitleInput`, `_uploadDescInput`, `_uploadSubmitBtn`, `_uploadStatusLabel`, `_uploadButton`, `_uploadButtonLabel`, `Coroutine _uploadRoutine`). Full field block + the builder/handlers are specified inline below.

- [ ] **Step 2:** In `OpenViewer(entry)`, store `_viewerEntry = entry;` and refresh the Upload button state (label "UPLOAD" vs "UPLOADED ✓" from `entry.uploaded`; whole button hidden if `!GalleryConfig.IsConfigured`).

- [ ] **Step 3:** In `EnsureBuilt`, after the BACK button, build the Upload button (anchor top-right of `_viewerRT`, `ButtonGrey`, label from a helper) → `OpenUploadModal()`; and build the (hidden) upload modal as a child of `_viewerRT`.

- [ ] **Step 4:** Handlers (verbatim):

```csharp
    void OpenUploadModal()
    {
        if (_viewerEntry == null || !GalleryConfig.IsConfigured) return;
        if (_viewerEntry.uploaded) return; // already up
        _uploadTitleInput.text = "";
        _uploadDescInput.text = "";
        _uploadStatusLabel.text = "";
        SetUploadInteractable(true);
        _uploadModalRT.gameObject.SetActive(true);
    }

    void CloseUploadModal()
    {
        if (_uploadRoutine != null) { StopCoroutine(_uploadRoutine); _uploadRoutine = null; }
        if (_uploadModalRT != null) _uploadModalRT.gameObject.SetActive(false);
    }

    void SubmitUpload()
    {
        string title = (_uploadTitleInput.text ?? "").Trim();
        if (title.Length == 0) { _uploadStatusLabel.text = "Title is required."; return; }
        if (title.Length > 100) title = title.Substring(0, 100);
        string desc = (_uploadDescInput.text ?? "").Trim();
        if (desc.Length > 500) desc = desc.Substring(0, 500);
        if (_viewerTexture == null) { _uploadStatusLabel.text = "No image loaded."; return; }

        SetUploadInteractable(false);
        _uploadStatusLabel.text = "Uploading…";
        var id = _viewerEntry.id;
        _uploadRoutine = StartCoroutine(GalleryApiClient.Upload(_viewerTexture, title, desc, (ok, remoteId, err) =>
        {
            _uploadRoutine = null;
            if (ok)
            {
                if (PhotoLibrary.Instance != null) PhotoLibrary.Instance.MarkUploaded(id, title);
                if (_viewerEntry != null && _viewerEntry.id == id) _viewerEntry.uploaded = true;
                _uploadStatusLabel.text = "Uploaded! It'll appear after review.";
                RefreshUploadButton();
                StartCoroutine(CloseModalAfter(1.2f));
            }
            else
            {
                _uploadStatusLabel.text = err ?? "Upload failed.";
                SetUploadInteractable(true);
            }
        }));
    }

    IEnumerator CloseModalAfter(float sec)
    {
        float t = 0f; while (t < sec) { t += Time.unscaledDeltaTime; yield return null; }
        CloseUploadModal();
    }

    void SetUploadInteractable(bool on)
    {
        if (_uploadTitleInput != null) _uploadTitleInput.interactable = on;
        if (_uploadDescInput != null)  _uploadDescInput.interactable = on;
        if (_uploadSubmitBtn != null)  _uploadSubmitBtn.interactable = on;
    }

    void RefreshUploadButton()
    {
        if (_uploadButton == null) return;
        bool show = GalleryConfig.IsConfigured;
        _uploadButton.gameObject.SetActive(show);
        if (show && _uploadButtonLabel != null)
        {
            bool up = _viewerEntry != null && _viewerEntry.uploaded;
            _uploadButtonLabel.text = up ? "UPLOADED ✓" : "UPLOAD";
            _uploadButton.interactable = !up;
        }
    }
```
`CloseViewer` must also `CloseUploadModal()`. The modal builder (title/desc `TMP_InputField`s, Submit/Cancel, dim backdrop) follows the same `NewUI`/`MakeText` idiom as the rest of the file — full code in this step; ESC while the modal is open closes the MODAL first (add that branch to `Back()` before the viewer branch).

- [ ] **Step 5:** compile check → clean. Commit `feat(photos): upload button + modal on fullscreen viewer`.

Note: live upload isn't testable until the server is deployed (Task 4). Here, verify the modal opens/closes, validates an empty title, and that with `!GalleryConfig.IsConfigured` the Upload button is hidden. A stubbed success path can be eyeballed by pointing BaseUrl at a `wrangler dev` instance in Task 4.

---

### Task 3: COMMUNITY GALLERY on the main menu

**Files:**
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs`
- Create: `Assets/3 - Scripts/UI/Photos/CommunityGalleryUI.cs`

`CommunityGalleryUI` is a plain component (NOT an auto-singleton) that MainMenuController creates and shows. It builds its own overlay canvas (sortingOrder 200, matching the credits modal tier), paginated grid (fetch 20, load-more on scroll-near-bottom), tap → fullscreen with title + description, images via `GalleryApiClient.LoadImage` (disk-cached). Offline/unreachable → "Couldn't reach the community gallery" + Retry. Never blocks the menu.

- [ ] **Step 1:** `MainMenuController` — add a 4th button between CREDITS and EXIT (line 178-179):
```csharp
        BuildButton(buttonsRT, "GalleryButton", "COMMUNITY GALLERY", OnCommunityGallery);
```
- [ ] **Step 2:** `MainMenuController` — add `OnCommunityGallery()` / `HideCommunityGallery()` mirroring `OnCredits`/`HideCredits` (SetActive toggle of `mainMenuButtonsRoot` + a lazily-created `CommunityGalleryUI`). If `!GalleryConfig.IsConfigured`, `OnCommunityGallery` shows a small "Community gallery isn't set up yet." card instead of the grid (dev hasn't deployed the server) — keeps the button honest without hiding it.
- [ ] **Step 3:** Write `CommunityGalleryUI.cs` — grid/viewer against `GalleryApiClient.List` + `LoadImage`, pagination, texture cleanup on close, error+retry panel. (Full code in this step; reuses the letterboxed-cell + fullscreen-viewer idiom from PhotoGalleryUI, adapted to remote fetch + metadata display.)
- [ ] **Step 4:** compile check → clean. Commit `feat(photos): community gallery on main menu`.

Verify in the MainMenu scene: button appears; with `IsConfigured` false, the "not set up yet" card shows; no errors. Live browsing verified in Task 4.

---

### Task 4: Deploy the server + end-to-end live test (GATED on user's Cloudflare setup)

**Files:** `server/photo-gallery/*` (already drafted + security-reviewed); `Assets/3 - Scripts/UI/Photos/GalleryConfig.cs` (fill in real values).

This task is done WITH the user (needs their Cloudflare account). Steps:
- [ ] **Step 1 (user + Claude):** follow `server/photo-gallery/README.md`: paste `database_id` into `wrangler.toml`, `wrangler d1 execute sound-of-space-gallery --remote --file=schema.sql`, `wrangler secret put UPLOAD_KEY` + `ADMIN_KEY`, `wrangler deploy`. Capture the deployed `*.workers.dev` URL.
- [ ] **Step 2:** `curl` smoke test against the live URL (upload → 201, GET /photos → empty until approved, GET /admin with key → shows pending, approve → GET /photos now lists it). Commands in the README.
- [ ] **Step 3:** Put the URL + UPLOAD_KEY into `GalleryConfig.cs`. Commit (this is the only secret-bearing client file; the UPLOAD_KEY is intentionally shippable per the spec).
- [ ] **Step 4:** In-game E2E: take a photo → open Photos app → fullscreen → Upload (title/desc) → success + "UPLOADED ✓" + manifest `uploaded=true`. Approve it on `/admin`. Main menu → COMMUNITY GALLERY → the photo appears with its title/description; disk cache populated; second visit doesn't re-download.
- [ ] **Step 5:** Commit `GalleryConfig.cs` + any tweaks. Update `docs/CURRENT_STATE_AUDIT.md` §30 to mark upload/community-gallery live.

---

### Task 5: Finish the branch

- [ ] Merge `feat/photos-app` per superpowers:finishing-a-development-branch (the whole photos feature — Plan A + B — lands together). Confirm a Windows build boots via MainMenu with both new gameplay singletons seeded (trap #1) and the menu button present.

---

## Out of scope

Video upload, server-side thumbnails/resizing, likes/usernames/comments, in-game moderation UI (moderation is the `/admin` web page), report button. All deferred per the spec.
