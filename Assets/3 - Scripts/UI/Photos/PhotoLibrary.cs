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
            // finally: don't leak the thumb Texture2D if the disk write throws
            // (this component lives forever via DontDestroyOnLoad).
            try     { System.IO.File.WriteAllBytes(GetThumbPath(id), thumb.EncodeToJPG(ThumbQuality)); }
            finally { Destroy(thumb); }
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
        try
        {
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var thumb = new Texture2D(tw, th, TextureFormat.RGB24, false);
            thumb.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
            thumb.Apply();
            return thumb;
        }
        finally
        {
            RenderTexture.active = oldActive;
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}
