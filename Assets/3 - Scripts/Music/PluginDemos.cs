/// <summary>
/// One short solo demo per rack module, for Tev's shop LISTEN button
/// (loop-feel pass A4): hear what a plugin ADDS before paying for it.
///
/// Each demo is a real TraxTrack playing only the demoed module over THUMPER
/// (drums alone for THUMPER itself) — the same "no fake audio path" rule as
/// TevDemoTapes. Preset picks are hand-chosen to show the module off; dials
/// stay at the instrument's defaults so the demo sounds like what a new
/// player's own first track will sound like once they own the thing.
///
/// PURE: no Unity types.
/// </summary>
public static class PluginDemos
{
    /// Per-module preset used in its demo (index into that module's bank —
    /// see TraxPresets). Order matches TraxPresets.ModuleNames.
    ///   THUMPER→STOMP, GLOWORM→WALK, MOSS→VAMP, SIREN→SONG, SPINDLE→ROLL,
    ///   CAVE→CANYON (big enough space to hear the send working).
    static readonly int[] DemoPreset = { 2, 2, 3, 1, 2, 2 };

    /// The demo track for a module, or null on an unknown name.
    public static TraxTrack TrackFor(string module)
    {
        int idx = TraxPresets.ModuleIndex(module);
        if (idx < 0) return null;

        TraxTrack t = TraxTrack.Default();
        for (int m = 0; m < TraxPresets.ModuleCount; m++)
        {
            string name = TraxPresets.ModuleNames[m];
            // The demoed module plus the beat under it. CAVE is a space —
            // solo reverb is silence, so it demos as drums through the room.
            bool on = name == module || name == "THUMPER";
            t = t.WithActive(name, on);
            t = t.WithPreset(name, DemoPreset[m]);
        }
        return t;
    }
}
