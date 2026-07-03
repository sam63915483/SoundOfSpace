/// <summary>
/// Community-gallery server config. Swap BaseUrl/UploadKey here after the
/// Worker is deployed (docs/cloudflare-setup-guide.md). UploadKey only stops
/// casual scripted abuse — it ships in the build and is extractable; real
/// protection is server-side validation + the approval queue.
/// </summary>
public static class GalleryConfig
{
    // Deployed 2026-07-03. NO trailing slash.
    public const string BaseUrl   = "https://photo-gallery.soundofspace.workers.dev";
    public const string UploadKey = "sos-up-16ef47af9c673b69fa574ba26042fcd55f0a13ad42da2fe9";

    public static bool IsConfigured =>
        !BaseUrl.Contains("REPLACE-ME") && !UploadKey.Contains("REPLACE-ME");
}
