/// <summary>
/// The three blank-tape FORMATS. A kind rides BESIDE tier (1/2), never inside
/// it — five separate code sites clamp tier to 1..2 and all of them stay true.
///
/// Demo presses one section; Half and Full press the whole song, capped by
/// bars. Prices and milestones are Sam's 2026-08-18 numbers (spec:
/// docs/superpowers/specs/2026-08-18-tape-formats-design.md).
///
/// PURE — compiled by the library, rent and taste suites.
/// </summary>
public static class TraxKind
{
    public const int Demo = 0;
    public const int Half = 1;
    public const int Full = 2;

    // Blank prices. Tev's shop reads these so the catalogue can never drift.
    public const int DemoT1Price = 5,  DemoT2Price = 12;
    public const int HalfT1Price = 15, HalfT2Price = 25;
    public const int FullT1Price = 22, FullT2Price = 35;

    // Career milestones: total tapes sold before Tev stocks the bigger blanks.
    public const int HalfUnlockSales = 10;
    public const int FullUnlockSales = 25;

    // What a text order QUOTES for a song it has not heard yet (the song does
    // not exist at quote time). ⚠️ PLACEHOLDER — Sam tunes.
    public const double DemoNominalMult = 1.0;
    public const double HalfNominalMult = 2.0;
    public const double FullNominalMult = 3.5;

    public static int Clamp(int kind) { return kind < Demo ? Demo : kind > Full ? Full : kind; }

    /// Longest song this blank can carry. Demo's cap is per-section anyway.
    public static int BarCap(int kind)
    {
        return kind == Full ? 100 : kind == Half ? 50 : TraxSong.SectionMaxBars;
    }

    public static int SectionCap(int kind) { return kind == Demo ? 1 : TraxSong.MaxSections; }

    public static string Label(int kind) { return kind == Full ? "FULL" : kind == Half ? "HALF" : "DEMO"; }

    /// Print-id prefix — 'd'/'h'/'f'. Legacy demo ids keep their old "t" form
    /// on load (TraxPrints.Apply) so saved hotbar tapes still resolve.
    public static char IdPrefix(int kind) { return kind == Full ? 'f' : kind == Half ? 'h' : 'd'; }

    public static double NominalMult(int kind)
    {
        return kind == Full ? FullNominalMult : kind == Half ? HalfNominalMult : DemoNominalMult;
    }

    /// The format multiplier a PRESSED tape's value carries. Demos are the
    /// baseline product (×1.0); songs grow with sections and length.
    public static double FormatMult(int kind, TraxSong song)
    {
        if (kind == Demo || song == null) return 1.0;
        return song.ValueMult();
    }
}
