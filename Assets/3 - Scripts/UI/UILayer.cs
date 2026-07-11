using UnityEngine;

/// <summary>
/// Single source of truth for canvas sortingOrder values. The order is a
/// contract: lower draws under higher. Centralised here so the relationships
/// are obvious and one tweak cascades everywhere.
///
/// Layer order (low → high):
///   Background      = 0     — main menu background, default canvases
///   HudBackground   = 22    — water-fill HUD, behind hotbar
///   Hotbar          = 200   — build menu, fishingdex, hotbar slot grid
///   VisorGlass      = 803   — visor glass tint/fresnel/scanlines (only visible through the opening)
///   HelmetFrame     = 805   — helmet interior art (under all readouts)
///   CompassHud      = 815   — compass strip (above helmet frame, under letterbox 820)
///   Hud             = 830   — primary HUDs (vitals, wallet, tutorial)
///   HelmetCondensation = 838 — O2 fog creeps OVER the cluster readouts (functional feedback)
///   Toast           = 900   — autosave + story-impact toasts (below pause)
///   Vendor          = 950   — vendor shop UIs (below pause)
///   PhotoGallery    = 960   — fullscreen photos app (above phone + toasts, below map/pause)
///   Map             = 970   — map view legend + orbit lines
///   Pause           = 1000  — pause menu (above all HUDs)
///   Modal           = 1100  — toasts that overlay the pause menu or map
///                              (FlightAssist, teleport-to-pilot button)
///   SaveDialog      = 2000  — save/load picker (above pause menu)
///   ControllerBorder= 32000 — controller-nav border (absolute top)
///
/// Use a value in this class rather than typing a magic number. If you need
/// a new layer, add it here (and document the reason in the comment).
/// </summary>
public static class UILayer
{
    public const int Background       = 0;
    public const int HudBackground    = 22;
    public const int Hotbar           = 200;
    public const int Hud              = 830;
    public const int VisorGlass       = 803;  // visor glass tint/fresnel/scanlines — UNDER the frame art: glass shows only through the visor opening, never over the matte shell
    public const int HelmetFrame      = 805;  // helmet interior art — UNDER the clusters: the art has opaque corners, so readouts mount ON the shell (bezels sell the recessed-screen look)
    public const int CompassHud       = 815;  // compass strip — above the helmet frame/glass, still under letterbox(820) so dialogue cinematics cover it
    public const int HelmetCondensation = 838; // O2 fog over the readouts — a fogged screen IS the low-O2 warning
    public const int Toast            = 900;
    public const int Vendor           = 950;
    public const int PhotoGallery     = 960;  // fullscreen photos app (above phone 850 + toasts, below map/pause)
    public const int Map              = 970;
    public const int Pause            = 1000;
    public const int Modal            = 1100;
    public const int SaveDialog       = 2000;
    public const int ControllerBorder = 32000;
}
