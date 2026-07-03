# Photos App (Local) Implementation Plan — Plan A of 2

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The in-game phone gets a Photos app: captures become JPG + thumbnail + manifest, a Photos tile opens a fullscreen gallery via a rotate-and-grow transition, photos view fullscreen and everything survives restarts.

**Architecture:** A `PhotoLibrary` auto-singleton owns the photo roll on disk (`<game folder>/Photos/`: `{id}.jpg`, `{id}_thumb.jpg`, `manifest.json`); the phone's existing `SnapPhoto()` routes its captured `Texture2D` through it. A `PhotoGalleryUI` auto-singleton renders the fullscreen grid + viewer on its own overlay canvas; `PlayerPhoneUI` gets a fifth app tile and a pair of transition coroutines that rotate/scale the phone chassis until it fills the screen, then crossfade to the gallery (reverse on exit).

**Tech Stack:** Unity 2022.3.62f3, Built-in RP, C# (`Assembly-CSharp`, no asmdefs), uGUI + TextMeshPro, procedurally-built UI (house style — no prefabs). **No CLI tests exist in this project**; verification is `mcp__coplay-mcp__check_compile_errors` after every code step + in-Editor play checks (`mcp__coplay-mcp__play_game`, `mcp__coplay-mcp__get_unity_logs`, `mcp__coplay-mcp__capture_ui_canvas`). This repo's convention (see prior plans) replaces TDD with compile + play verification.

**Spec:** `docs/superpowers/specs/2026-07-03-photos-app-community-gallery-design.md` (Plan B — upload/server/community gallery — is written separately after this plan lands.)

---

## Reference facts (verified against the codebase 2026-07-03)

Line numbers are start-of-plan references into `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` (~2992 lines) unless stated; **re-grep before editing — they drift as tasks land.**

- **Phone**: screen-space overlay canvas built in code; chassis `_phoneRT` is anchor/pivot `(0.5,0.5)`, `sizeDelta (220,440)` (`PhoneWidth`/`PhoneHeight` consts, lines 62-63), `localScale 1.5` (`PhoneScale`, line 66), Y position driven by `AnimatePhone` between `OnScreenY` (=40) and `OffScreenY` (getters, lines 1240-1250). Phone canvas `sortingOrder = 850` (`BuildCanvas`, lines 1571-1588; literal, with comment "above HUDs (800-820), below pause menu (1000)").
- **Screen interior**: `_screenRT` insets the chassis by 12 px sides / 42 px top+bottom → 196×356; its `VerticalLayoutGroup` (padding 8, spacing 8) makes every row ~180 px wide. Rows: status bar, notification strip, `_pageHostRT` (`LayoutElement.preferredHeight = 170`, line 2044), page-nav zone (`preferredHeight 30, flexibleHeight 1`), CAMERA button.
- **App grid** (`BuildAppsPage`, lines 2052-2077): `GridLayoutGroup` padding (8,8,4,4), spacing (10,10), cellSize (78,78), FixedColumnCount = 2; four tiles via `BuildAppTile(AppKind, glyph, label)` (lines 2492-2524, returns `Button`, wires `OnAppClicked(kind)`). `AppKind` enum line 78: `{ Fishingdex, Build, Settings, Map }`. `_appButtons = new Button[4]` line 114.
- **App launch flow**: `OnAppClicked` (2546) → `StartCoroutine(CloseThenOpen(kind))` (2553-2577): `Close(); yield return new WaitWhile(() => _isAnimating);` then a null-guarded `switch` opening the target singleton UI.
- **SnapPhoto** (958-1019): crops `_phoneCameraRT` by `_sliceLeftUV/_sliceWidthUV`, flips rows into `var tex = new Texture2D(cropW, cropH, TextureFormat.RGB24, false)`, `tex.Apply()` at line 985. **Lines 988-1002** are the PNG persist block to replace. `tex` is then assigned to `_capturedTex` (preview) and destroyed later by `SnapLifecycle`/`ExitCameraMode` — a save call must NOT take ownership. Portrait captures when `_isLandscape == false` (narrow vertical slice), full landscape frames when `true`.
- **Rotation tween**: `ApplyOrientation(bool)` (590-609) + `RotatePhoneRoutine` (611-651), `const float RotationDuration = 0.28f` (line 582). It disables `_screenMask` (`RectMask2D` on `_screenRT`) during rotation because the mask mis-culls children while the chassis rotates — the new transition must do the same.
- **Open/Close** (347-392) + `AnimatePhone` (1508-1567): open sets `IsOpen = true`, unlocks cursor, disables `EventSystem.sendNavigationEvents`; close re-locks cursor + re-enables nav events. `ForceCloseNoAnim` (312-334) snaps everything closed; wired to death (`ResourceManager.OnDeath`), NPC conversation start, and `SceneManager.sceneLoaded` (only re-locks cursor when scene != "MainMenu").
- **Update input** (1252-1475): `if (AIChatScreen.IsTypingActive) return;` at 1278; R-rotate 1294-1299; camera-mode block 1320-1385; `if (_isAnimating) return;` 1387; ESC-close 1394-1399; **C enters camera mode 1405-1424 (NOT gated on `isInDialogue` — must be gated against the gallery)**; X toggle 1430-1458 (gated on `PlayerController.isInDialogue` + pause menu); WASD auto-close 1464-1473. `ConsumedEscapeThisFrame` cleared in `LateUpdate` (338).
- **Input gating pattern** (mirror FishingdexManager, `Assets/3 - Scripts/Fishing/FishingdexManager.cs` 128-157): on open set `PlayerController.isInDialogue = true` (static bool, `PlayerController.cs` line 261 — blocks look at line 537 and movement at line 582) + unlock cursor; on close reset + re-lock. **Zero PlayerController edits needed.**
- **ESC priority**: `TabbedPauseMenu.cs` lines 243-255 — ESC opens pause only if no other UI `IsOpen`/`ConsumedEscapeThisFrame`. New gallery must be added to that condition and handle its own ESC.
- **Auto-singleton template** (`Assets/3 - Scripts/Player/SpaceDustInventory.cs` 9-49): `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` + MainMenu early-return + `Instance` guard in `Awake`, cleared in `OnDestroy`. **Trap #1:** must ALSO be seeded in `MainMenuController.EnsureGameplaySingletonsAsync` (`Assets/3 - Scripts/UI/MainMenuController.cs`, starts line 523; seed pattern: `if (X.Instance == null) { var go = new GameObject("X"); DontDestroyOnLoad(go); go.AddComponent<X>(); } tick("label"); yield return null;` — last existing seed is HintTrackRunner ~line 623-624. The `const int Total = 38` at line 525 only drives the loading-bar fraction; already lower than the real count, leave it).
- **Sorting orders**: `Assets/3 - Scripts/UI/UILayer.cs` — constants class; gallery gets a new `PhotoGallery = 960` (above phone 850 + Toast 900, below Map 970/Pause 1000).
- **Shared helpers**: `HudFontResolver.Apply(tmp)` for fonts. Geometry helpers (`NewUI`/`MakeText`/`Stretch`) are deliberately duplicated per-file (house style — every procedural UI carries its own copies).
- **House rules** (CLAUDE.md): append new serialized/instance fields at the END of `PlayerPhoneUI`; `CompareTag`; no `FindObjectOfType`/`Camera.main` in per-frame code; new files need `git add` of both `.cs` AND `.cs.meta` (run `mcp__coplay-mcp__check_compile_errors` first so Unity imports and generates the `.meta`).
- **NOT saved**: photos/manifest are the player's photo roll — no `SaveCollector`, no `NewGameReset` hook (they survive New Game deliberately, per spec).

---

### Task 1: `PhotoLibrary` — JPG + thumbnail + manifest pipeline

**Files:**
- Create: `Assets/3 - Scripts/UI/Photos/PhotoLibrary.cs`
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs` (~line 623, end of `EnsureGameplaySingletonsAsync`)

- [ ] **Step 1: Write `PhotoLibrary.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the player's photo roll: full-res JPGs + ~256px thumbnails + a JSON
/// manifest, all in <game folder>/Photos (the same folder the phone camera
/// has always written to). New captures flow through SavePhoto(); the
/// gallery reads GetPhotosNewestFirst().
///
/// Deliberately NOT part of the save system — photos are the player's photo
/// roll, not game progress. They survive New Game and never touch
/// SaveCollector. Legacy photo_*.png files (pre-manifest era) are ignored
/// (fresh-start decision, spec 2026-07-03). Videos are untouched.
///
/// Auto-singleton (mirrors SpaceDustInventory) — MUST also be seeded in
/// MainMenuController.EnsureGameplaySingletonsAsync (CLAUDE.md trap #1).
/// </summary>
public class PhotoLibrary : MonoBehaviour
{
    public static PhotoLibrary Instance { get; private set; }

    [System.Serializable]
    public class PhotoEntry
    {
        public string id;
        public string capturedAt;   // ISO-8601 round-trip ("o") — ordinal-sortable
        public int width;
        public int height;
        public bool uploaded;       // consumed by the upload flow (Plan B)
        public string uploadedTitle;
    }

    [System.Serializable]
    class Manifest { public List<PhotoEntry> photos = new List<PhotoEntry>(); }

    const int ThumbMaxEdge = 256;
    const int JpgQuality   = 85;
    const int ThumbQuality = 75;

    Manifest _manifest;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("PhotoLibrary");
        DontDestroyOnLoad(go);
        go.AddComponent<PhotoLibrary>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static string PhotosDir =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Photos"));

    string ManifestPath => System.IO.Path.Combine(PhotosDir, "manifest.json");
    public string GetPhotoPath(string id) => System.IO.Path.Combine(PhotosDir, id + ".jpg");
    public string GetThumbPath(string id) => System.IO.Path.Combine(PhotosDir, id + "_thumb.jpg");

    // Lazy load. Entries whose image file vanished from disk are dropped; a
    // corrupt manifest starts empty rather than throwing.
    void EnsureLoaded()
    {
        if (_manifest != null) return;
        _manifest = new Manifest();
        try
        {
            if (!System.IO.File.Exists(ManifestPath)) return;
            var loaded = JsonUtility.FromJson<Manifest>(System.IO.File.ReadAllText(ManifestPath));
            if (loaded == null || loaded.photos == null) return;
            foreach (var p in loaded.photos)
                if (p != null && !string.IsNullOrEmpty(p.id) && System.IO.File.Exists(GetPhotoPath(p.id)))
                    _manifest.photos.Add(p);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PhotoLibrary] Manifest load failed, starting empty: {e.Message}");
            _manifest = new Manifest();
        }
    }

    void SaveManifest()
    {
        try
        {
            System.IO.Directory.CreateDirectory(PhotosDir);
            System.IO.File.WriteAllText(ManifestPath, JsonUtility.ToJson(_manifest, true));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PhotoLibrary] Manifest save failed: {e.Message}");
        }
    }

    /// <summary>Newest-first copy of the photo list (callers may mutate freely).</summary>
    public List<PhotoEntry> GetPhotosNewestFirst()
    {
        EnsureLoaded();
        var copy = new List<PhotoEntry>(_manifest.photos);
        // capturedAt is ISO-8601 "o" (UTC) — ordinal compare IS chronological.
        copy.Sort((a, b) => string.CompareOrdinal(b.capturedAt, a.capturedAt));
        return copy;
    }

    /// <summary>
    /// Persist a captured photo (full JPG + thumb + manifest row). Does NOT
    /// take ownership of tex — the caller's preview lifecycle still destroys
    /// it. Returns the new entry, or null on failure.
    /// </summary>
    public PhotoEntry SavePhoto(Texture2D tex)
    {
        if (tex == null) return null;
        EnsureLoaded();
        var id = System.Guid.NewGuid().ToString("N");
        try
        {
            System.IO.Directory.CreateDirectory(PhotosDir);
            System.IO.File.WriteAllBytes(GetPhotoPath(id), tex.EncodeToJPG(JpgQuality));
            var thumb = MakeThumbnail(tex, ThumbMaxEdge);
            System.IO.File.WriteAllBytes(GetThumbPath(id), thumb.EncodeToJPG(ThumbQuality));
            Destroy(thumb);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PhotoLibrary] Photo save failed: {e.Message}");
            return null;
        }
        var entry = new PhotoEntry
        {
            id = id,
            capturedAt = System.DateTime.UtcNow.ToString("o"),
            width = tex.width,
            height = tex.height,
            uploaded = false,
            uploadedTitle = "",
        };
        _manifest.photos.Add(entry);
        SaveManifest();
        Debug.Log($"[PhotoLibrary] Photo saved: {GetPhotoPath(id)}");
        return entry;
    }

    /// <summary>Flag a photo as uploaded (used by the upload flow, Plan B).</summary>
    public void MarkUploaded(string id, string title)
    {
        EnsureLoaded();
        var entry = _manifest.photos.Find(p => p.id == id);
        if (entry == null) return;
        entry.uploaded = true;
        entry.uploadedTitle = title ?? "";
        SaveManifest();
    }

    // GPU downscale: blit into a temporary RT, read back. Handles portrait
    // and landscape sources (longest edge clamped, aspect preserved).
    static Texture2D MakeThumbnail(Texture2D src, int maxEdge)
    {
        float k = Mathf.Min(1f, (float)maxEdge / Mathf.Max(src.width, src.height));
        int tw = Mathf.Max(1, Mathf.RoundToInt(src.width * k));
        int th = Mathf.Max(1, Mathf.RoundToInt(src.height * k));
        var rt = RenderTexture.GetTemporary(tw, th, 0);
        var oldActive = RenderTexture.active;
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;
        var thumb = new Texture2D(tw, th, TextureFormat.RGB24, false);
        thumb.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
        thumb.Apply();
        RenderTexture.active = oldActive;
        RenderTexture.ReleaseTemporary(rt);
        return thumb;
    }
}
```

- [ ] **Step 2: Compile check (also generates the `.meta`)**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: no errors.

- [ ] **Step 3: Seed the singleton (trap #1)**

In `Assets/3 - Scripts/UI/MainMenuController.cs`, find the LAST seed block of `EnsureGameplaySingletonsAsync` (grep `HintTrackRunner.Instance == null`, ~line 623) and add directly after its `tick(...); yield return null;` line:

```csharp
        if (PhotoLibrary.Instance == null) { var go = new GameObject("PhotoLibrary"); DontDestroyOnLoad(go); go.AddComponent<PhotoLibrary>(); }
        tick("photo library");    yield return null;
```

- [ ] **Step 4: Compile check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/UI/Photos/PhotoLibrary.cs" "Assets/3 - Scripts/UI/Photos/PhotoLibrary.cs.meta" "Assets/3 - Scripts/UI/Photos.meta" "Assets/3 - Scripts/UI/MainMenuController.cs"
git commit -m "feat(photos): PhotoLibrary singleton - JPG+thumb+manifest photo roll"
```

(The new `Photos/` folder gets its own `.meta` — add it too. If `git add` says the folder meta doesn't exist, re-run the compile check so Unity finishes importing.)

---

### Task 2: Route `SnapPhoto()` through PhotoLibrary

**Files:**
- Modify: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` (~lines 988-1002, inside `SnapPhoto()`)

- [ ] **Step 1: Replace the PNG persist block**

In `SnapPhoto()`, find and delete exactly this block:

```csharp
        // Persist a copy to disk before the in-memory texture gets destroyed.
        var bytes = tex.EncodeToPNG();
        var photosDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Photos"));
        try
        {
            System.IO.Directory.CreateDirectory(photosDir);
            var filename = $"photo_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.png";
            var fullPath = System.IO.Path.Combine(photosDir, filename);
            System.IO.File.WriteAllBytes(fullPath, bytes);
            Debug.Log($"[PlayerPhoneUI] Photo saved: {fullPath}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerPhoneUI] Failed to save photo: {e.Message}");
        }
```

Replace with:

```csharp
        // Persist via the photo roll (JPG + thumbnail + manifest entry).
        // SavePhoto encodes synchronously and does NOT take ownership of
        // tex — the preview lifecycle below still destroys it.
        if (PhotoLibrary.Instance != null) PhotoLibrary.Instance.SavePhoto(tex);
        else Debug.LogWarning("[PlayerPhoneUI] PhotoLibrary.Instance is null — photo not persisted");
```

- [ ] **Step 2: Compile check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: no errors.

- [ ] **Step 3: Play-verify the capture pipeline end-to-end**

Run: `mcp__coplay-mcp__play_game` (gameplay scene `Assets/1.6.7.7.7.unity` must be the open scene). Then either drive it or ask the user to: press **X** (phone), **C** (camera), left-click (snap). Then `mcp__coplay-mcp__get_unity_logs` and look for `[PhotoLibrary] Photo saved:`. Stop play (`mcp__coplay-mcp__stop_game`).

On disk in `<repo root>/Photos/` expect three new artifacts: `<32-hex-id>.jpg`, `<32-hex-id>_thumb.jpg`, `manifest.json` (with one entry). Open both JPGs — **verify the thumbnail is not upside-down** (if it ever is on this GPU/driver: flip the rows in `MakeThumbnail` after `ReadPixels`, same row-swap as `SnapPhoto` does). Verify a second snap appends a second manifest entry. Verify old `photo_*.png` files still sit in the folder untouched.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/UI/PlayerPhoneUI.cs"
git commit -m "feat(photos): route SnapPhoto through PhotoLibrary (JPG+thumb+manifest)"
```

---

### Task 3: `PhotoGalleryUI` — fullscreen grid + viewer (+ UILayer + ESC guard + seeding)

**Files:**
- Modify: `Assets/3 - Scripts/UI/UILayer.cs`
- Create: `Assets/3 - Scripts/UI/Photos/PhotoGalleryUI.cs`
- Modify: `Assets/3 - Scripts/UI/TabbedPauseMenu.cs` (~lines 250-254)
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs` (seed, right after Task 1's)

- [ ] **Step 1: Add the sorting layer**

In `UILayer.cs`, add to the constants (keep numeric order) and mirror it in the header comment list:

```csharp
    public const int PhotoGallery     = 960;  // fullscreen photos app (above phone 850 + toasts, below map/pause)
```

- [ ] **Step 2: Write `PhotoGalleryUI.cs`**

```csharp
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Fullscreen Photos app — thumbnail grid + fullscreen viewer over the
/// player's photo roll (PhotoLibrary). Launched from the phone's Photos
/// tile; from Task 5 onward that goes through the rotate-and-grow
/// transition (PlayerPhoneUI.OpenPhotosApp), which drives this class via
/// OpenForTransition/SetTransitionAlpha and exits via CloseForPhoneReturn.
///
/// Input gating mirrors FishingdexManager: PlayerController.isInDialogue =
/// true while open + cursor unlocked. ESC is handled HERE (viewer → grid →
/// exit-to-phone); TabbedPauseMenu skips its ESC branch while IsOpen.
///
/// Auto-singleton (mirrors SpaceDustInventory) — MUST also be seeded in
/// MainMenuController.EnsureGameplaySingletonsAsync (CLAUDE.md trap #1).
/// </summary>
public class PhotoGalleryUI : MonoBehaviour
{
    public static PhotoGalleryUI Instance { get; private set; }
    public static bool IsOpen { get; private set; }
    public static bool ConsumedEscapeThisFrame { get; private set; }

    // ── Palette (mirrors PlayerPhoneUI) ─────────────────────────────
    static readonly Color ScreenBg   = new Color32(0x06, 0x0F, 0x1A, 0xFF);
    static readonly Color AccentCyan = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color LabelWhite = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    static readonly Color TileBg     = new Color32(0x0F, 0x19, 0x2A, 0xD9);
    static readonly Color ButtonGrey = new Color32(0x2A, 0x40, 0x60, 0xFF);

    Canvas          _canvas;
    CanvasGroup     _rootGroup;
    RectTransform   _rootRT;
    RectTransform   _gridContentRT;
    ScrollRect      _scroll;
    TextMeshProUGUI _emptyLabel;
    TextMeshProUGUI _countLabel;
    bool            _built;

    // Grid cell thumbnails — owned by us, destroyed on close.
    readonly List<Texture2D> _thumbTextures = new List<Texture2D>();

    // Fullscreen viewer state.
    RectTransform     _viewerRT;
    RawImage          _viewerImage;
    AspectRatioFitter _viewerFitter;
    TextMeshProUGUI   _viewerCaption;
    Texture2D         _viewerTexture;   // full-res; loaded on view, destroyed on close
    bool              _viewerOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("PhotoGalleryUI");
        DontDestroyOnLoad(go);
        go.AddComponent<PhotoGalleryUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()  { SceneManager.sceneLoaded += OnSceneLoaded; }
    void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }
    void OnSceneLoaded(Scene scene, LoadSceneMode mode) { ForceClose(); }

    void Update()
    {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ConsumedEscapeThisFrame = true;
            Back();
        }
    }

    void LateUpdate() { ConsumedEscapeThisFrame = false; }

    /// <summary>ESC / back-button chain: viewer → grid → exit.</summary>
    public void Back()
    {
        if (_viewerOpen) { CloseViewer(); return; }
        // TEMP until Task 5 adds PlayerPhoneUI.BeginGalleryExit (the reverse
        // transition) — Task 5 Step 3 replaces this hard close.
        ForceClose();
    }

    // ── Open / close ────────────────────────────────────────────────

    /// <summary>Open instantly at full alpha (pre-transition launch path + fallback).</summary>
    public void Open()
    {
        OpenForTransition();
        SetTransitionAlpha(1f);
    }

    /// <summary>Everything Open does, but at alpha 0 — the phone's transition
    /// coroutine fades us in over the grown phone.</summary>
    public void OpenForTransition()
    {
        if (IsOpen) return;
        EnsureBuilt();
        IsOpen = true;
        PlayerController.isInDialogue = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        _rootGroup.alpha = 0f;
        _rootGroup.blocksRaycasts = true;
        CloseViewer();
        PopulateGrid();
    }

    public void SetTransitionAlpha(float a)
    {
        if (_rootGroup != null) _rootGroup.alpha = Mathf.Clamp01(a);
    }

    /// <summary>Close where the phone takes over the screen (reverse transition):
    /// drops gates + textures but does NOT touch the cursor — the still-open
    /// phone owns the unlocked cursor.</summary>
    public void CloseForPhoneReturn()
    {
        if (!IsOpen) return;
        TearDown();
    }

    /// <summary>Hard close (scene load, death, conversation, fallback) — also
    /// re-locks the cursor like FishingdexManager.Close, except in MainMenu
    /// (same guard as PlayerPhoneUI.ForceCloseNoAnim).</summary>
    public void ForceClose()
    {
        if (!IsOpen) return;
        TearDown();
        if (SceneManager.GetActiveScene().name != "MainMenu")
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void TearDown()
    {
        CloseViewer();
        ClearGrid();
        IsOpen = false;
        PlayerController.isInDialogue = false;
        if (_rootGroup != null) { _rootGroup.alpha = 0f; _rootGroup.blocksRaycasts = false; }
    }

    // ── Grid ────────────────────────────────────────────────────────

    void PopulateGrid()
    {
        ClearGrid();
        var photos = PhotoLibrary.Instance != null
            ? PhotoLibrary.Instance.GetPhotosNewestFirst()
            : new List<PhotoLibrary.PhotoEntry>();

        _emptyLabel.gameObject.SetActive(photos.Count == 0);
        string countText = photos.Count + (photos.Count == 1 ? " PHOTO" : " PHOTOS");
        if (_countLabel.text != countText) _countLabel.text = countText;

        foreach (var p in photos)
        {
            var tex = LoadTexture(PhotoLibrary.Instance.GetThumbPath(p.id))
                   ?? LoadTexture(PhotoLibrary.Instance.GetPhotoPath(p.id)); // thumb missing → fall back
            if (tex == null) continue;
            _thumbTextures.Add(tex);
            BuildCell(p, tex);
        }
        _scroll.verticalNormalizedPosition = 1f; // top
    }

    void ClearGrid()
    {
        if (_gridContentRT != null)
            for (int i = _gridContentRT.childCount - 1; i >= 0; i--)
                Destroy(_gridContentRT.GetChild(i).gameObject);
        foreach (var t in _thumbTextures) if (t != null) Destroy(t);
        _thumbTextures.Clear();
    }

    void BuildCell(PhotoLibrary.PhotoEntry entry, Texture2D thumb)
    {
        var cell = NewUI("Cell_" + entry.id, _gridContentRT);
        var bg = cell.gameObject.AddComponent<Image>();
        bg.color = TileBg;
        bg.raycastTarget = true;

        var imgRT = NewUI("Thumb", cell);
        var raw = imgRT.gameObject.AddComponent<RawImage>();
        raw.texture = thumb;
        raw.raycastTarget = false;
        var fit = imgRT.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; // letterbox both orientations
        fit.aspectRatio = (float)thumb.width / Mathf.Max(1, thumb.height);

        var btn = cell.gameObject.AddComponent<Button>();
        var captured = entry;
        btn.onClick.AddListener(() => OpenViewer(captured));
    }

    static Texture2D LoadTexture(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path)) return null;
            var bytes = System.IO.File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!tex.LoadImage(bytes)) { Destroy(tex); return null; }
            return tex;
        }
        catch { return null; }
    }

    // ── Fullscreen viewer ───────────────────────────────────────────

    void OpenViewer(PhotoLibrary.PhotoEntry entry)
    {
        var tex = LoadTexture(PhotoLibrary.Instance != null ? PhotoLibrary.Instance.GetPhotoPath(entry.id) : null);
        if (tex == null) return;
        if (_viewerTexture != null) Destroy(_viewerTexture);
        _viewerTexture = tex;
        _viewerImage.texture = tex;
        _viewerFitter.aspectRatio = (float)tex.width / Mathf.Max(1, tex.height);

        string caption = entry.capturedAt;
        if (System.DateTime.TryParse(entry.capturedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            caption = dt.ToLocalTime().ToString("MMM d, yyyy - h:mm tt");
        _viewerCaption.text = caption;

        _viewerRT.gameObject.SetActive(true);
        _viewerOpen = true;
    }

    void CloseViewer()
    {
        if (!_viewerOpen && _viewerTexture == null) return;
        _viewerOpen = false;
        if (_viewerRT != null) _viewerRT.gameObject.SetActive(false);
        if (_viewerImage != null) _viewerImage.texture = null;
        if (_viewerTexture != null) { Destroy(_viewerTexture); _viewerTexture = null; }
    }

    // ── Build (once, lazily) ────────────────────────────────────────

    void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = UILayer.PhotoGallery;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        _rootRT = NewUI("Root", transform);
        Stretch(_rootRT, 0f);
        _rootGroup = _rootRT.gameObject.AddComponent<CanvasGroup>();
        _rootGroup.alpha = 0f;
        _rootGroup.blocksRaycasts = false;
        var bg = _rootRT.gameObject.AddComponent<Image>();
        bg.color = ScreenBg;
        bg.raycastTarget = true; // swallow clicks so nothing falls through

        // Header: title left, count right.
        var title = MakeText(_rootRT, "PHOTOS", 42, AccentCyan, TextAnchor.MiddleLeft);
        title.fontStyle = FontStyles.Bold;
        var titleRT = title.rectTransform;
        titleRT.anchorMin = new Vector2(0f, 1f); titleRT.anchorMax = new Vector2(0f, 1f);
        titleRT.pivot = new Vector2(0f, 1f);
        titleRT.anchoredPosition = new Vector2(60f, -24f);
        titleRT.sizeDelta = new Vector2(420f, 50f);

        _countLabel = MakeText(_rootRT, "", 20, LabelWhite, TextAnchor.MiddleRight);
        var countRT = _countLabel.rectTransform;
        countRT.anchorMin = new Vector2(1f, 1f); countRT.anchorMax = new Vector2(1f, 1f);
        countRT.pivot = new Vector2(1f, 1f);
        countRT.anchoredPosition = new Vector2(-60f, -36f);
        countRT.sizeDelta = new Vector2(320f, 30f);

        // Footer hint.
        var hint = MakeText(_rootRT, "[ESC] BACK      [CLICK] VIEW", 16, LabelWhite, TextAnchor.MiddleRight);
        var hintRT = hint.rectTransform;
        hintRT.anchorMin = new Vector2(1f, 0f); hintRT.anchorMax = new Vector2(1f, 0f);
        hintRT.pivot = new Vector2(1f, 0f);
        hintRT.anchoredPosition = new Vector2(-60f, 16f);
        hintRT.sizeDelta = new Vector2(420f, 26f);

        // Scrollable thumbnail grid.
        var scrollRT = NewUI("Scroll", _rootRT);
        scrollRT.anchorMin = Vector2.zero; scrollRT.anchorMax = Vector2.one;
        scrollRT.offsetMin = new Vector2(60f, 56f);
        scrollRT.offsetMax = new Vector2(-60f, -86f);
        var scrollBg = scrollRT.gameObject.AddComponent<Image>();
        scrollBg.color = new Color(0f, 0f, 0f, 0.001f); // raycast surface for wheel scroll
        _scroll = scrollRT.gameObject.AddComponent<ScrollRect>();
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 40f;

        var viewportRT = NewUI("Viewport", scrollRT);
        Stretch(viewportRT, 0f);
        viewportRT.gameObject.AddComponent<RectMask2D>();
        _scroll.viewport = viewportRT;

        _gridContentRT = NewUI("Content", viewportRT);
        _gridContentRT.anchorMin = new Vector2(0f, 1f);
        _gridContentRT.anchorMax = new Vector2(1f, 1f);
        _gridContentRT.pivot = new Vector2(0.5f, 1f);
        _gridContentRT.offsetMin = Vector2.zero;
        _gridContentRT.offsetMax = Vector2.zero;
        var grid = _gridContentRT.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(280f, 190f);
        grid.spacing = new Vector2(16f, 16f);
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.childAlignment = TextAnchor.UpperCenter;
        var fitter = _gridContentRT.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _scroll.content = _gridContentRT;

        // Empty state.
        _emptyLabel = MakeText(_rootRT, "No photos yet - press C while your phone is out to open the camera.",
                               22, LabelWhite, TextAnchor.MiddleCenter);
        _emptyLabel.enableWordWrapping = true;
        Stretch(_emptyLabel.rectTransform, 120f);
        _emptyLabel.gameObject.SetActive(false);

        // Fullscreen viewer (sibling AFTER the scroll → draws on top).
        _viewerRT = NewUI("Viewer", _rootRT);
        Stretch(_viewerRT, 0f);
        var vbg = _viewerRT.gameObject.AddComponent<Image>();
        vbg.color = ScreenBg;
        vbg.raycastTarget = true; // block grid interaction underneath

        var photoRT = NewUI("Photo", _viewerRT);
        Stretch(photoRT, 60f);
        _viewerImage = photoRT.gameObject.AddComponent<RawImage>();
        _viewerImage.raycastTarget = false;
        _viewerFitter = photoRT.gameObject.AddComponent<AspectRatioFitter>();
        _viewerFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

        _viewerCaption = MakeText(_viewerRT, "", 20, LabelWhite, TextAnchor.MiddleCenter);
        var capRT = _viewerCaption.rectTransform;
        capRT.anchorMin = new Vector2(0.5f, 0f); capRT.anchorMax = new Vector2(0.5f, 0f);
        capRT.pivot = new Vector2(0.5f, 0f);
        capRT.anchoredPosition = new Vector2(0f, 16f);
        capRT.sizeDelta = new Vector2(900f, 30f);

        var backRT = NewUI("BackBtn", _viewerRT);
        backRT.anchorMin = new Vector2(0f, 1f); backRT.anchorMax = new Vector2(0f, 1f);
        backRT.pivot = new Vector2(0f, 1f);
        backRT.anchoredPosition = new Vector2(24f, -24f);
        backRT.sizeDelta = new Vector2(150f, 44f);
        var backBg = backRT.gameObject.AddComponent<Image>();
        backBg.color = ButtonGrey;
        var backLabel = MakeText(backRT, "< BACK", 18, LabelWhite, TextAnchor.MiddleCenter);
        Stretch(backLabel.rectTransform, 0f);
        var backBtn = backRT.gameObject.AddComponent<Button>();
        backBtn.onClick.AddListener(CloseViewer);

        _viewerRT.gameObject.SetActive(false);
    }

    // ── Local UI helpers (house style: duplicated per procedural UI) ─

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt, float margin)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(margin, margin);
        rt.offsetMax = new Vector2(-margin, -margin);
    }

    static TextMeshProUGUI MakeText(RectTransform parent, string text, float fontSize, Color color, TextAnchor anchor)
    {
        var rt = NewUI("Text", parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(t);
        t.text = text;
        t.fontSize = fontSize;
        t.color = color;
        t.enableWordWrapping = false;
        t.alignment = anchor switch
        {
            TextAnchor.MiddleLeft  => TextAlignmentOptions.MidlineLeft,
            TextAnchor.MiddleRight => TextAlignmentOptions.MidlineRight,
            _                      => TextAlignmentOptions.Midline,
        };
        t.raycastTarget = false;
        return t;
    }
}
```

Note: the file above compiles standalone — `Back()` deliberately hard-closes for now because `PlayerPhoneUI.BeginGalleryExit()` doesn't exist until Task 5 (Task 5 Step 3 swaps in the reverse-transition version).

- [ ] **Step 3: Compile check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: no errors.

- [ ] **Step 4: Add the pause-menu ESC guard**

In `Assets/3 - Scripts/UI/TabbedPauseMenu.cs` (~line 254), find:

```csharp
                  && !PlayerPhoneUI.IsOpen && !PlayerPhoneUI.ConsumedEscapeThisFrame) OpenPause();
```

Replace with:

```csharp
                  && !PlayerPhoneUI.IsOpen && !PlayerPhoneUI.ConsumedEscapeThisFrame
                  && !PhotoGalleryUI.IsOpen && !PhotoGalleryUI.ConsumedEscapeThisFrame) OpenPause();
```

- [ ] **Step 5: Seed the singleton (trap #1)**

In `MainMenuController.EnsureGameplaySingletonsAsync`, directly after Task 1's PhotoLibrary seed:

```csharp
        if (PhotoGalleryUI.Instance == null) { var go = new GameObject("PhotoGalleryUI"); DontDestroyOnLoad(go); go.AddComponent<PhotoGalleryUI>(); }
        tick("photo gallery");    yield return null;
```

- [ ] **Step 6: Compile check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: no errors.

- [ ] **Step 7: Commit**

```bash
git add "Assets/3 - Scripts/UI/Photos/PhotoGalleryUI.cs" "Assets/3 - Scripts/UI/Photos/PhotoGalleryUI.cs.meta" "Assets/3 - Scripts/UI/UILayer.cs" "Assets/3 - Scripts/UI/TabbedPauseMenu.cs" "Assets/3 - Scripts/UI/MainMenuController.cs"
git commit -m "feat(photos): PhotoGalleryUI fullscreen grid + viewer, UILayer.PhotoGallery, pause ESC guard"
```

---

### Task 4: Photos tile on the phone (plain launch, no transition yet)

**Files:**
- Modify: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` — enum (line 78), `_appButtons` (114), `BuildAppsPage` (2052-2077), `CloseThenOpen` (2553-2577), C-key branch (~1405)

- [ ] **Step 1: Extend the enum and button array**

Line 78 — append (never reorder existing members):

```csharp
    public enum AppKind { Fishingdex, Build, Settings, Map, Photos }
```

Line 114:

```csharp
    Button[]      _appButtons = new Button[5];
```

- [ ] **Step 2: Re-layout the app grid for 5 tiles (3 columns)**

In `BuildAppsPage()`, the grid settings become 3 columns of 54 px cells (2 rows; row width 3×54 + 2×6 + 8 = 178 ≤ the ~180 px row; tile innards overflow their 54 px box by ~2 px of transparent text — invisible). Replace:

```csharp
        var grid = _appGridRT.gameObject.AddComponent<GridLayoutGroup>();
        grid.padding = new RectOffset(8, 8, 4, 4);
        grid.spacing = new Vector2(10f, 10f);
        grid.cellSize = new Vector2(78f, 78f);
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
```

with:

```csharp
        var grid = _appGridRT.gameObject.AddComponent<GridLayoutGroup>();
        // 5 tiles → 3 columns of 54 px cells (two rows). Row width budget is
        // ~180 px inside the screen's VerticalLayoutGroup: 3*54 + 2*6 + 8 = 178.
        grid.padding = new RectOffset(4, 4, 4, 4);
        grid.spacing = new Vector2(6f, 6f);
        grid.cellSize = new Vector2(54f, 54f);
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
```

And after the `_appButtons[3] = BuildAppTile(AppKind.Map, ...)` line add:

```csharp
        _appButtons[4] = BuildAppTile(AppKind.Photos,     "P", "Photos");
```

- [ ] **Step 3: Launch case in `CloseThenOpen`**

Add to the `switch` (after the `AppKind.Map` case):

```csharp
            case AppKind.Photos:
                if (PhotoGalleryUI.Instance != null) PhotoGalleryUI.Instance.Open();
                else Debug.LogWarning("[PlayerPhoneUI] PhotoGalleryUI.Instance is null");
                break;
```

- [ ] **Step 4: Gate the global C-key camera shortcut against the gallery**

The C branch (~line 1405) is NOT gated on `isInDialogue`, so C would open the phone camera UNDER the gallery. Change its outer condition from:

```csharp
        if (Input.GetKeyDown(KeyCode.C) && !_isAnimating)
```

to:

```csharp
        if (Input.GetKeyDown(KeyCode.C) && !_isAnimating && !PhotoGalleryUI.IsOpen)
```

- [ ] **Step 5: Compile check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: no errors.

- [ ] **Step 6: Play-verify the full flow**

`mcp__coplay-mcp__play_game`, then: **X** → home screen shows FIVE tiles in a 3-wide grid (screenshot via `mcp__coplay-mcp__capture_ui_canvas` and eyeball crowding — if tiles collide with the page dots, drop `cellSize` to 50) → click **Photos** → phone slides away, gallery opens with the thumbnails from Task 2 → click a thumb → full-res viewer with date caption → **ESC** back to grid → **ESC** closes (temporary hard-close; the phone-return transition arrives in Task 5) → confirm cursor re-locked, look works, pause menu did NOT open on those ESC presses. Also verify: with the gallery open, **C**, **X**, and **WASD** do nothing gallery-breaking (movement stays blocked). `mcp__coplay-mcp__get_unity_logs` for errors; `mcp__coplay-mcp__stop_game`.

- [ ] **Step 7: Commit**

```bash
git add "Assets/3 - Scripts/UI/PlayerPhoneUI.cs"
git commit -m "feat(photos): Photos app tile on phone home screen (plain launch)"
```

---

### Task 5: Rotate-and-grow transition (forward + reverse)

**Files:**
- Modify: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` — new fields (END of class), new methods, `OnAppClicked`, `CloseThenOpen`, `ForceCloseNoAnim`, `Update`
- Modify: `Assets/3 - Scripts/UI/Photos/PhotoGalleryUI.cs` — `Back()` final version

**How it reads:** tap Photos → the handheld phone rotates to landscape while growing/centering until its screen overflows the viewport (~0.45 s, smoothstep) → the gallery fades in over it (0.12 s) → phone parks hidden. Exit reverses: gallery fades out revealing the pre-staged oversized phone → it shrinks/rotates back into the hand.

- [ ] **Step 1: Add transition state (fields at the very END of the PlayerPhoneUI class body, per house rule) and methods**

Fields (append immediately before the closing brace of the class):

```csharp
    // ── Photos-app rotate-and-grow transition (fields appended at class
    //    end per repo convention; see GalleryEnterRoutine) ────────────
    Coroutine _galleryTransition;
    bool      _inGalleryTransition;
    const float GalleryGrowDuration = 0.45f;
    const float GalleryFadeDuration = 0.12f;
```

Methods (add near `CloseThenOpen`):

```csharp
    /// <summary>Photos tile entry point — rotate-and-grow into the gallery.</summary>
    public void OpenPhotosApp()
    {
        if (_inGalleryTransition || !IsOpen || _isAnimating) return;
        if (_galleryTransition != null) StopCoroutine(_galleryTransition);
        _galleryTransition = StartCoroutine(GalleryEnterRoutine());
    }

    /// <summary>Gallery's back-out entry point — reverse transition to the hand.</summary>
    public void BeginGalleryExit()
    {
        if (_inGalleryTransition) return;
        if (_galleryTransition != null) StopCoroutine(_galleryTransition);
        _galleryTransition = StartCoroutine(GalleryExitRoutine());
    }

    // Rotated -90° the chassis is PhoneHeight wide × PhoneWidth tall on
    // screen; scale so it overflows the canvas on both axes (the bezel ends
    // off-screen and the screen interior covers the viewport). 1.10 = margin.
    float GalleryTargetScale()
    {
        var parent = (RectTransform)_phoneRT.parent;
        return Mathf.Max(parent.rect.width / PhoneHeight, parent.rect.height / PhoneWidth) * 1.10f;
    }

    System.Collections.IEnumerator GalleryEnterRoutine()
    {
        _inGalleryTransition = true;
        HideHintNow();
        // Kill competing tweens (orientation / slide).
        if (_rotateCoroutine != null) { StopCoroutine(_rotateCoroutine); _rotateCoroutine = null; }
        if (_animCoroutine   != null) { StopCoroutine(_animCoroutine);   _animCoroutine = null; _isAnimating = false; }
        // RectMask2D mis-culls children while the chassis rotates — same
        // reason RotatePhoneRoutine disables it for its tween.
        if (_screenMask != null) _screenMask.enabled = false;

        yield return GalleryTween(0f, -90f, PhoneScale, GalleryTargetScale(),
                                  _phoneRT.anchoredPosition.y, 0f, GalleryGrowDuration);

        // Crossfade: the gallery fades in over the now screen-filling phone.
        var gallery = PhotoGalleryUI.Instance;
        if (gallery != null)
        {
            gallery.OpenForTransition(); // gates on, grid populated, alpha 0
            float t = 0f;
            while (t < GalleryFadeDuration)
            {
                t += Time.unscaledDeltaTime;
                gallery.SetTransitionAlpha(Mathf.Clamp01(t / GalleryFadeDuration));
                yield return null;
            }
            gallery.SetTransitionAlpha(1f);
        }
        else Debug.LogWarning("[PlayerPhoneUI] PhotoGalleryUI.Instance is null");

        // Park the phone closed WITHOUT touching the cursor — the gallery's
        // isInDialogue gate + unlocked cursor own the input state now.
        HideForGallery();
        _inGalleryTransition = false;
        _galleryTransition = null;
    }

    System.Collections.IEnumerator GalleryExitRoutine()
    {
        _inGalleryTransition = true;
        // Stage the phone exactly where the enter transition left it —
        // rotated, screen-filling, visible — hidden UNDER the gallery.
        if (_screenMask != null) _screenMask.enabled = false;
        GoToPage(0);
        IsOpen = true;
        _phoneGroup.alpha = 1f;
        _phoneGroup.blocksRaycasts = true;
        float bigScale = GalleryTargetScale();
        _phoneRT.localRotation = Quaternion.Euler(0f, 0f, -90f);
        _phoneRT.localScale = new Vector3(bigScale, bigScale, 1f);
        _phoneRT.anchoredPosition = Vector2.zero;

        // Gallery fades out revealing the oversized phone…
        var gallery = PhotoGalleryUI.Instance;
        if (gallery != null)
        {
            float t = 0f;
            while (t < GalleryFadeDuration)
            {
                t += Time.unscaledDeltaTime;
                gallery.SetTransitionAlpha(1f - Mathf.Clamp01(t / GalleryFadeDuration));
                yield return null;
            }
            // Cursor stays unlocked — the (open) phone owns it now.
            gallery.CloseForPhoneReturn();
        }

        // …then the phone shrinks + rotates back into the hand.
        yield return GalleryTween(-90f, 0f, bigScale, PhoneScale, 0f, OnScreenY, GalleryGrowDuration);

        if (_screenMask != null) _screenMask.enabled = true;
        _isLandscape = false;
        _inGalleryTransition = false;
        _galleryTransition = null;
    }

    System.Collections.IEnumerator GalleryTween(float fromDeg, float toDeg, float fromScale, float toScale,
                                                float fromY, float toY, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / duration);
            float eased = u * u * (3f - 2f * u); // smoothstep ease-in-out
            _phoneRT.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(fromDeg, toDeg, eased));
            float s = Mathf.Lerp(fromScale, toScale, eased);
            _phoneRT.localScale = new Vector3(s, s, 1f);
            _phoneRT.anchoredPosition = new Vector2(0f, Mathf.Lerp(fromY, toY, eased));
            yield return null;
        }
        _phoneRT.localRotation = Quaternion.Euler(0f, 0f, toDeg);
        _phoneRT.localScale = new Vector3(toScale, toScale, 1f);
        _phoneRT.anchoredPosition = new Vector2(0f, toY);
    }

    // Park the phone in its normal closed state without cursor/nav changes
    // (the gallery owns input while it's up).
    void HideForGallery()
    {
        IsOpen = false;
        _phoneGroup.alpha = 0f;
        _phoneGroup.blocksRaycasts = false;
        _isLandscape = false;
        _phoneRT.localRotation = Quaternion.identity;
        _phoneRT.localScale = new Vector3(PhoneScale, PhoneScale, 1f);
        _phoneRT.anchoredPosition = new Vector2(0f, OffScreenY);
        if (_screenMask != null) _screenMask.enabled = true;
    }
```

- [ ] **Step 2: Route the Photos tile through the transition + make force-close transition-safe**

`OnAppClicked` (line 2546) becomes:

```csharp
    void OnAppClicked(AppKind kind)
    {
        // Photos zooms INTO the phone instead of sliding it away.
        if (kind == AppKind.Photos) { OpenPhotosApp(); return; }
        // Slide the phone out, THEN open the target UI — like tapping an
        // app on a real phone (home screen exits, app launches).
        StartCoroutine(CloseThenOpen(kind));
    }
```

Delete the now-unreachable `case AppKind.Photos:` block from `CloseThenOpen` (added in Task 4 Step 3).

In `ForceCloseNoAnim()` (line 312), after the existing `_rotateCoroutine` stop-line, add:

```csharp
        if (_galleryTransition != null) { StopCoroutine(_galleryTransition); _galleryTransition = null; }
        _inGalleryTransition = false;
        if (PhotoGalleryUI.Instance != null) PhotoGalleryUI.Instance.ForceClose();
        // The gallery tween may have left the chassis rotated/oversized.
        if (_phoneRT != null)
        {
            _phoneRT.localRotation = Quaternion.identity;
            _phoneRT.localScale = new Vector3(PhoneScale, PhoneScale, 1f);
        }
        if (_screenMask != null) _screenMask.enabled = true;
```

In `Update()`, directly after the `if (AIChatScreen.IsTypingActive) return;` line (1278), add:

```csharp
        if (_inGalleryTransition) return; // no phone input while zooming to/from the gallery
```

- [ ] **Step 3: Final `Back()` in PhotoGalleryUI**

Replace the TEMP body from Task 3 with:

```csharp
    /// <summary>ESC / back-button chain: viewer → grid → exit to phone.</summary>
    public void Back()
    {
        if (_viewerOpen) { CloseViewer(); return; }
        if (PlayerPhoneUI.Instance != null) PlayerPhoneUI.Instance.BeginGalleryExit();
        else ForceClose(); // no phone to return to — just drop the gates
    }
```

- [ ] **Step 4: Compile check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: no errors.

- [ ] **Step 5: Play-verify the transition (user feel-check required)**

`mcp__coplay-mcp__play_game`, then: **X** → tap **Photos** → phone rotates+grows until the screen fills the frame, gallery crossfades in → ESC → gallery fades revealing the giant phone, which shrinks back into the hand in portrait, home screen intact, cursor still unlocked, phone still open → **X** closes phone normally (cursor re-locks). Repeat open/exit 3-4 times rapidly to shake out state bugs (double-click the tile, ESC mid-fade — the `_inGalleryTransition` guards should make these no-ops). Verify look/movement blocked for the WHOLE transition (no camera jump mid-zoom). Then the subjective pass: **the user must watch it and sign off on the feel** (duration/easing constants `GalleryGrowDuration`, smoothstep, the 1.10 overshoot are the knobs). `mcp__coplay-mcp__stop_game`.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/UI/PlayerPhoneUI.cs" "Assets/3 - Scripts/UI/Photos/PhotoGalleryUI.cs"
git commit -m "feat(photos): rotate-and-grow transition between phone and gallery"
```

---

### Task 6: Audit update + final sweep

**Files:**
- Modify: `docs/CURRENT_STATE_AUDIT.md`

- [ ] **Step 1: Document the system in the audit (CLAUDE.md: material changes update the audit)**

Append a new numbered section (follow the doc's existing numbering, e.g. §21 — check the last section number in the file):

```markdown
## §N Photos App (local) — added 2026-07-03

The phone camera's captures now flow through **PhotoLibrary** (auto-singleton,
`Assets/3 - Scripts/UI/Photos/`): `<game folder>/Photos/{id}.jpg` (q85) +
`{id}_thumb.jpg` (≤256px, q75) + `manifest.json` (JsonUtility; id/capturedAt/
size/uploaded flag). Deliberately NOT in the save system — photos survive New
Game; legacy `photo_*.png` files are ignored; videos unchanged.

**PhotoGalleryUI** (auto-singleton, canvas `UILayer.PhotoGallery = 960`) is the
fullscreen Photos app: thumbnail grid (ScrollRect + GridLayoutGroup, letterboxed
mixed-aspect cells) + full-res viewer (one texture at a time, destroyed on
close). Input gating mirrors Fishingdex (`PlayerController.isInDialogue` +
cursor unlock); it handles its own ESC (viewer → grid → phone) and is guarded
in TabbedPauseMenu's ESC branch. Both singletons are seeded in
`EnsureGameplaySingletonsAsync` (trap #1).

Entry: fifth app tile ("P / Photos", grid now 3×54px columns) →
`PlayerPhoneUI.OpenPhotosApp()` — the rotate-and-grow transition (chassis
rotates -90° + scales to overfill the viewport, gallery crossfades in;
`BeginGalleryExit` reverses it). `ForceCloseNoAnim` also force-closes the
gallery and resets the chassis transform.

Planned next (see specs/2026-07-03-photos-app-community-gallery-design.md):
upload flow + Cloudflare Worker backend + main-menu Community Gallery (Plan B).
```

- [ ] **Step 2: Full regression pass in the Editor**

`mcp__coplay-mcp__play_game`; run this checklist (with the user driving where feel matters):

1. Take a portrait photo AND a landscape photo (R in camera mode) → both appear in the grid correctly letterboxed.
2. Restart play mode → photos + manifest persist; grid repopulates.
3. Camera mode, video record start/stop → `.avi` still lands in `Photos/`, untouched by the new pipeline.
4. Fishingdex / Build / Settings / Map tiles all still launch from the resized grid.
5. Phone R-rotate on the home screen still works (landscape ↔ portrait) and doesn't fight the gallery transition.
6. ESC chain everywhere: camera → home → closed; gallery viewer → grid → phone; pause menu never double-opens.
7. Scene-reload force-close: with photos in the grid, ESC back to phone → X to close → pause menu → load a save → after the reload, no stuck unlocked cursor, no lingering gallery canvas, phone opens normally. (Conversation/death force-close paths run through the same `ForceCloseNoAnim` hook added in Task 5.)
8. Aspect ratios (spec requirement): repeat the tile-grid look, gallery layout, and both transition directions in a 16:9 Game view AND the user's native 3440×1440 — the `CanvasScaler` + `GalleryTargetScale()` math must fill both.
9. `mcp__coplay-mcp__get_unity_logs`: no errors/warnings from PhotoLibrary/PhotoGalleryUI beyond intentional ones.

- [ ] **Step 3: Commit**

```bash
git add "docs/CURRENT_STATE_AUDIT.md"
git commit -m "docs: audit section for local Photos app"
```

---

## Out of scope for Plan A (lands in Plan B)

Upload modal + downscale-before-upload, `GalleryApiClient`, `GalleryConfig.cs`, the Cloudflare Worker (`server/photo-gallery/` — remember to gitignore its `node_modules/`), admin approval page, main-menu Community Gallery. The user's one-time Cloudflare account setup is independent and already documented in `docs/cloudflare-setup-guide.md`.
