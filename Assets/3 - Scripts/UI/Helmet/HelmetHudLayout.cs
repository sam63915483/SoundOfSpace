using UnityEngine;

/// <summary>
/// Housing layout contract in 1920×1080 canvas reference units. The helmet
/// frame pieces AND the three cluster cards both read these, so the recessed
/// screens and their contents stay aligned at every aspect ratio (all HUD
/// canvases share identical CanvasScaler settings — same coordinate space).
/// Windows are corner/center-anchored, which is what survives aspect change.
/// </summary>
public static class HelmetHudLayout
{
    // Window rects: offset from their anchor corner + size.
    public static readonly Vector2 BottomLeftOffset  = new Vector2(16f, 16f);
    public static readonly Vector2 BottomLeftSize    = new Vector2(380f, 230f);

    public static readonly Vector2 BottomRightOffset = new Vector2(16f, 16f); // measured from bottom-RIGHT corner
    public static readonly Vector2 BottomRightSize   = new Vector2(360f, 260f);

    public static readonly float   TopBrowYOffset    = 8f;                    // from top edge, centered horizontally
    public static readonly Vector2 TopBrowSize       = new Vector2(640f, 72f);

    /// Gap between the housing window edge and the cluster card (bezel ring + glass).
    public const float CardInset = 14f;
}
