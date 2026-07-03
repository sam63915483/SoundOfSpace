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
