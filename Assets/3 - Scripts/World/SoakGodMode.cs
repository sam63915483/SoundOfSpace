using System.Reflection;
using UnityEngine;

/// <summary>
/// DEV SOAK HARNESS for long orbit measurements (see OrbitClockProbe /
/// docs/DAY_NIGHT_CLOCKS.md). DISABLED by default in the scene — enable the
/// component only for an AFK soak run. While enabled, every frame it:
///   • pins Time.timeScale to <see cref="targetTimeScale"/> (physics math is
///     unchanged — same 0.01 fixed step, just more steps per real second),
///   • keeps the player unkillable (health/hunger/thirst pinned full, suit O2
///     refilled) so a death-reload can't reset the orbits mid-measurement,
///   • mutes audio.
/// Disabling it restores timeScale 1 and audio. Never ship a scene with this
/// enabled.
/// </summary>
public class SoakGodMode : MonoBehaviour
{
    public float targetTimeScale = 20f;

    FieldInfo fHunger, fThirst, fHealth;
    float prevVolume = 1f;

    void OnEnable()
    {
        var t = typeof(ResourceManager);
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        fHunger = t.GetField("hungerCurrent", F);
        fThirst = t.GetField("thirstCurrent", F);
        fHealth = t.GetField("healthCurrent", F);
        prevVolume = AudioListener.volume;
        Application.runInBackground = true;
        Debug.Log($"[SoakGodMode] ON — timeScale {targetTimeScale}x, player unkillable, audio muted");
    }

    void Update()
    {
        Time.timeScale = targetTimeScale;
        Time.maximumDeltaTime = 1f;
        AudioListener.volume = 0f;

        var rm = ResourceManager.Instance;
        if (rm != null)
        {
            fHunger?.SetValue(rm, 100f);
            fThirst?.SetValue(rm, 100f);
            fHealth?.SetValue(rm, 100f);
        }
        var o2 = OxygenManager.Instance;
        if (o2 != null) o2.RefillSuitToFull();
    }

    void OnDisable()
    {
        Time.timeScale = 1f;
        AudioListener.volume = prevVolume;
        Debug.Log("[SoakGodMode] OFF — timeScale restored");
    }
}
