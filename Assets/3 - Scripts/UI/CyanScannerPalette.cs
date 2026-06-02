using UnityEngine;

// Single source of truth for the cyan "scanner" palette used by the
// FishingdexManager and BuildMenuUI. Mirrors the AccentCool / SubtitleColor
// values from MainMenuController so all UI surfaces (main menu, pause menu,
// compass HUD, tutorial pill, killstreak HUD, the two scanner menus) live in
// the same colour space.
//
// All values are Color32 so Image.color assignments don't go through implicit
// gamma conversion — Color32 matches what the rest of the codebase uses for
// uGUI tinting.
public static class CyanScannerPalette
{
    public static readonly Color32 PanelBg        = new Color32(0x0A, 0x12, 0x28, 0xF0); // dark navy, slightly translucent
    public static readonly Color32 PanelBorder    = new Color32(0x1C, 0x3A, 0x5C, 0xFF);
    public static readonly Color32 InnerBg        = new Color32(0x0C, 0x1A, 0x32, 0xFF);
    public static readonly Color32 InnerDivider   = new Color32(0x12, 0x28, 0x45, 0xFF);

    public static readonly Color32 Accent         = new Color32(0x5B, 0xD8, 0xFF, 0xFF); // primary cyan
    public static readonly Color32 AccentDim      = new Color32(0x88, 0xC4, 0xDC, 0xFF); // muted cyan
    public static readonly Color32 Text           = new Color32(0xA8, 0xE6, 0xFF, 0xFF);
    public static readonly Color32 TextBright     = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
    public static readonly Color32 TextMuted      = new Color32(0x88, 0xC4, 0xDC, 0xCC);

    public static readonly Color32 SelectionFill  = new Color32(0x14, 0x30, 0x55, 0xFF);

    public static readonly Color32 BtnNormal      = new Color32(0x14, 0x30, 0x55, 0xFF);
    public static readonly Color32 BtnNormalEdge  = new Color32(0x2A, 0x50, 0x78, 0xFF);
    public static readonly Color32 BtnNormalHover = new Color32(0x1F, 0x44, 0x70, 0xFF);
    public static readonly Color32 BtnPrimary     = Accent;
    public static readonly Color32 BtnPrimaryHover= new Color32(0x8C, 0xE6, 0xFF, 0xFF);
    public static readonly Color32 BtnPrimaryText = new Color32(0x0A, 0x12, 0x28, 0xFF);

    public static readonly Color32 CostAfford     = Accent;
    public static readonly Color32 CostUnafford   = new Color32(0xFF, 0x5A, 0x5A, 0xFF);

    public static readonly Color32 GridLine       = new Color32(0x5B, 0xD8, 0xFF, 0x10); // ~6% alpha
    public static readonly Color32 BracketColor   = Accent;

    // Rarity stripe colours for the fishingdex list rows.
    public static readonly Color32 RarityRare     = Accent;
    public static readonly Color32 RarityUncommon = new Color32(0x88, 0xC4, 0xDC, 0xFF);
    public static readonly Color32 RarityCommon   = new Color32(0x3A, 0x60, 0x80, 0xFF);
}
