using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Category volume buses (Music / SFX / Ambience / UI) on top of the master
/// AudioListener.volume — the runtime-router answer to "the game has no
/// AudioMixer and ~100 code-created AudioSources".
///
/// How it works: a source is registered once with its bus right after it's
/// created; its volume AT REGISTRATION is captured as the authored level, and
/// the source then plays at authored × bus. PlayOneShot inherits its source's
/// volume, so registering a source covers every one-shot it plays. Sliders
/// live in the pause menu (TabbedPauseMenu), persistence in InputSettings.
///
/// ⚠️ Only register sources whose volume is SET ONCE. A source whose volume is
/// animated by code every frame (suit wind, heartbeat fades) must NOT be
/// registered — the router would capture a mid-fade value as "authored" and
/// then fight the animation. Those stay master-only, which is correct: their
/// loudness is tuned at their own serialized volume fields.
///
/// Why not a real AudioMixer: none exists in the project, mixer assets can't
/// be authored reliably outside the Editor UI, and 97 of the ~107 sources are
/// created in code anyway — so a code router reaches them all with no asset
/// and no prefab churn. The one thing this cannot do is LIMIT the summed
/// signal; the conservative master default (InputSettings) is the headroom
/// until a real mixer asset is created in the Editor and sources are pointed
/// at its groups.
/// </summary>
public static class GameAudioBus
{
    public enum Bus { SFX, Ambience, UI, Music }

    struct Entry
    {
        public AudioSource src;
        public float authored;   // src.volume at registration = the tuned level
        public Bus bus;
    }

    static readonly List<Entry> s_entries = new List<Entry>();

    /// <summary>
    /// Route a source through a bus. Call once, right after creating the source
    /// and setting its tuned volume. Safe to call again after re-tuning — the
    /// current volume is re-captured as authored.
    /// </summary>
    public static void Register(AudioSource src, Bus bus)
    {
        if (src == null) return;
        Prune();
        for (int i = 0; i < s_entries.Count; i++)
        {
            if (s_entries[i].src != src) continue;
            s_entries[i] = new Entry { src = src, authored = src.volume, bus = bus };
            src.volume = s_entries[i].authored * Level(bus);
            return;
        }
        var e = new Entry { src = src, authored = src.volume, bus = bus };
        s_entries.Add(e);
        src.volume = e.authored * Level(bus);
    }

    /// Current 0..1 level of a bus. Multiply this in by hand at call sites the
    /// router can't own (PlayClipAtPoint, animated-volume sources).
    public static float Level(Bus bus)
    {
        var s = InputSettings.Active;
        if (s == null) return 1f;
        switch (bus)
        {
            case Bus.Music:    return Mathf.Clamp01(s.musicVolume);
            case Bus.Ambience: return Mathf.Clamp01(s.ambienceVolume);
            case Bus.UI:       return Mathf.Clamp01(s.uiVolume);
            default:           return Mathf.Clamp01(s.sfxVolume);
        }
    }

    /// Re-apply every bus level to every registered source. Called by the
    /// settings sliders and by InputSettings.Begin after loading prefs.
    public static void ApplyAll()
    {
        Prune();
        for (int i = 0; i < s_entries.Count; i++)
        {
            var e = s_entries[i];
            if (e.src != null) e.src.volume = e.authored * Level(e.bus);
        }
    }

    static void Prune()
    {
        for (int i = s_entries.Count - 1; i >= 0; i--)
            if (s_entries[i].src == null) s_entries.RemoveAt(i);
    }
}
