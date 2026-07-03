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
        // Defense-in-depth: the server generates 32-hex ids, but never let a
        // response id build a path that escapes the cache dir (../.. or an
        // absolute path that Path.Combine would rebase to). Bad id → no cache,
        // just download to memory.
        bool cacheable = !string.IsNullOrEmpty(id) &&
            System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-fA-F0-9]{1,64}$");
        string cachePath = cacheable ? System.IO.Path.Combine(CacheDir, id + ".jpg") : null;
        // Cache hit.
        if (cachePath != null && System.IO.File.Exists(cachePath))
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
            if (cachePath != null)
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
        Texture2D small = null;
        try
        {
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            small = new Texture2D(tw, th, TextureFormat.RGB24, false);
            small.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
            small.Apply();
            return small.EncodeToJPG(quality);
        }
        finally
        {
            RenderTexture.active = oldActive;
            RenderTexture.ReleaseTemporary(rt);
            if (small != null) UnityEngine.Object.Destroy(small); // free the temp even if encode threw
        }
    }
}
