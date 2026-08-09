using UnityEngine;

/// <summary>
/// The fixed list of suit colours a character can wear.
///
/// ⚠️ NEVER RENUMBER OR REORDER THIS LIST. The index is persisted in
/// characters.json and sent over the network as an int. Reordering silently
/// recolours every existing character and desyncs two players on different
/// builds. Appending to the end is always safe; changing a hex in place is safe
/// (it is a retune, not a remap); removing or reordering is not.
///
/// ── 2026-08-09 retune ────────────────────────────────────────────────────
/// The first pass was a set of muted "designer" shades and Sam's verdict was
/// blunt and correct: "kinda washed out". Replaced with the actual primaries —
/// white, black, red, orange, yellow, green, cyan, blue, purple, pink — at full
/// saturation, because a suit colour is an IDENTITY at fifty metres in the dark,
/// not an interior-decor choice. Two players must be able to say "the red one"
/// and mean the same person.
///
/// This was a retune in place, not a reorder, so no index moved. Characters
/// created before it keep their slot and simply wear the punchier colour.
///
/// The old indices 1–4 used to mirror the retired NetworkPlayerSetup.ClientColors
/// array. That mattered only while those hard-coded colours still shipped; they
/// are gone now, so the palette is free to be its own thing.
///
/// Index 0 is the default for a brand-new character.
///
/// Values are sRGB hex as authored. They land on Suit.mat, which is Standard
/// shader with NO texture, so the albedo reads almost exactly as picked —
/// except "Black", which is deliberately #232326 rather than #000000: a true
/// black albedo returns no light at all and reads as a hole in the world
/// rather than a black suit.
/// </summary>
public static class SuitPalette
{
    public struct Swatch
    {
        public readonly string Name;
        public readonly Color  Color;
        public Swatch(string name, Color color) { Name = name; Color = color; }
    }

    /// Helper so the table below reads as hex like the design does.
    static Color Hex(int rgb) => new Color(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8)  & 0xFF) / 255f,
        ( rgb        & 0xFF) / 255f,
        1f);

    public static readonly Swatch[] Swatches =
    {
        new Swatch("White",  Hex(0xF2F2F2)),  // 0 — default
        new Swatch("Black",  Hex(0x232326)),  // 1 — see note: not pure #000000
        new Swatch("Red",    Hex(0xE01E1E)),  // 2
        new Swatch("Orange", Hex(0xF77F00)),  // 3
        new Swatch("Yellow", Hex(0xFFC300)),  // 4
        new Swatch("Green",  Hex(0x22B14C)),  // 5
        new Swatch("Cyan",   Hex(0x00BCD4)),  // 6
        new Swatch("Blue",   Hex(0x1E63E9)),  // 7
        new Swatch("Purple", Hex(0x8E3FEC)),  // 8
        new Swatch("Pink",   Hex(0xFF4D9D)),  // 9
    };

    public static int Count => Swatches.Length;

    /// Any index from disk or the wire goes through here. A character saved on a
    /// build with a longer palette must not throw on a build with a shorter one.
    public static int Clamp(int index) => (index < 0 || index >= Swatches.Length) ? 0 : index;

    public static Color ColorAt(int index) => Swatches[Clamp(index)].Color;
    public static string NameAt(int index) => Swatches[Clamp(index)].Name;
}
