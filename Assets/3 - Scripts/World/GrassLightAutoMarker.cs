using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gives the PLAYER's own lights and the home SHUTTLE's lights a say in the
/// grass (Sam, 2026-08-18: "the player's light that illuminates stuff near
/// them, and the lights on the shuttle, should also light up the grass").
///
/// The instanced grass never receives Unity's additive lights — every light
/// is faked through <see cref="GrassPointLight"/> markers that
/// InstancedGrassRenderer injects as shader globals. Lanterns, torches and the
/// concert rig add their own markers in their own Awakes; the player rig and
/// the shuttle are HAND-AUTHORED objects (the Shuttle_Lander prefab is Sam's,
/// never regenerated), so their lights get marked here at runtime instead of
/// by editing the prefab.
///
/// A throttled sweep (never per-frame — CLAUDE.md convention) finds:
///   • every point/spot light under the local player, EXCEPT the flashlight —
///     that one already lights grass through its own _Flashlight* shader path
///     and marking it too would double it up;
///   • every point/spot light under the home shuttle (found via its computer
///     terminal, the one component that uniquely lives on it).
/// Directional lights are never marked (the sun has its own paths), and
/// anything already carrying a marker is left alone. Lights that are toggled
/// OFF stay dark on the grass — the injector skips disabled/zeroed lights.
///
/// Auto-singleton per the SpaceDustInventory pattern; ALSO seeded in
/// MainMenuController.EnsureGameplaySingletonsAsync (CLAUDE.md trap #1: the
/// MainMenu early-return below means a build would otherwise never create it).
/// </summary>
public class GrassLightAutoMarker : MonoBehaviour
{
    public static GrassLightAutoMarker Instance { get; private set; }

    /// Matches the lantern default: with the material's _PointLightBoost 2.0
    /// this lands at ~1.0 effective, so grass answers these lights about as
    /// strongly as the real ground does.
    const float GrassStrength = 0.5f;

    /// <summary>Grass response for the PLAYER's own lights, separate from the
    /// shuttle's.
    ///
    /// The player's fill light (ViewmodelFillLight) is deliberately tiny —
    /// intensity 0.55 over a 4.5 m range — because its job is lighting the held
    /// item, and its header explicitly wants "the world past the player's hands
    /// untouched". At the shared 0.5 strength that came out as roughly 12% on
    /// grass a metre away at night and ~2% in daylight: technically working,
    /// practically invisible, which is exactly what Sam reported.
    ///
    /// He wants the blades right around him to pick it up, so the player's
    /// lights get their own multiplier. It only scales the FAKED grass response
    /// — the real light is untouched, so held items, the world and stealth all
    /// behave exactly as before.
    ///
    /// 2026-09-04: was 1.5. The shader multiplies every injected light by the
    /// material's _PointLightBoost, which is 4.5 on the live grass material (the
    /// "2.0" the lantern note above assumes is the shader DEFAULT, not the
    /// asset) — so 1.5 made the fill light ~6.75× stronger on grass than on the
    /// ground beside it, which is a large part of "the grass glows while the
    /// ground barely lights up". 0.35 × 4.5 ≈ 1.6× the real light: still
    /// visible on the blades at your feet, no longer a glowing pool.</summary>
    /// 2026-09-06: 0.35 -> 0.13. Same root cause as the torch (see the grass
    /// shader's flashlight block): the SUN reaches grass through a half-Lambert
    /// wrap that effectively halves the blade colour, but the lantern/eye-light
    /// path applies the full colour, so by day (wrap on, lampDay 0) it was fine
    /// and at night grass lit ~2x harder than the ground beside it.
    /// 0.13 x _PointLightBoost 4.5 = 0.59 -- the sun path's gain.
    /// 2026-09-06 (later): the real cause was the grass receiving the REAL
    /// light on top of this faked one (shader lacked noforwardadd /
    /// novertexlights). With the real path gone, parity with the ground is
    /// 1 / _PointLightBoost = 0.22.
    public static float PlayerLightGrassStrength = 0.22f;

    /// Lights appear late (the shuttle streams in, held items spawn, the
    /// thrust FX builds its own lights), so sweep on a slow clock forever
    /// rather than once. Cheap: two throttled finds + a child scan.
    const float SweepInterval = 3f;

    float _nextSweep;
    ShuttleComputerTerminal _terminal;
    static readonly List<Light> _scratch = new List<Light>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("[GrassLightAutoMarker]");
        DontDestroyOnLoad(go);
        go.AddComponent<GrassLightAutoMarker>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (Time.unscaledTime < _nextSweep) return;
        _nextSweep = Time.unscaledTime + SweepInterval;

        // ── the player's own lights (fill light, viewmodel light, …) ──
        var player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            // The flashlight's Light is excluded by REFERENCE — its grass
            // response already ships through PlayerFlashlight's _Flashlight*
            // globals, and a marker on top would light the beam twice.
            var torch = player.GetComponentInChildren<PlayerFlashlight>(true);
            Light torchLight = torch != null ? torch.flashlight : null;
            MarkLightsUnder(player.transform, torchLight, PlayerLightGrassStrength);
        }

        // ── the home shuttle's lights ──
        // Found via the computer terminal: the one component that uniquely
        // lives on the Shuttle_Lander, so a bought second ship or a random
        // lit prop can never be mistaken for it. Lazy, throttled refind.
        if (_terminal == null) _terminal = FindObjectOfType<ShuttleComputerTerminal>();
        if (_terminal != null) MarkLightsUnder(_terminal.transform.root, null, GrassStrength);
    }

    static void MarkLightsUnder(Transform root, Light exclude, float strength)
    {
        _scratch.Clear();
        root.GetComponentsInChildren(true, _scratch);
        for (int i = 0; i < _scratch.Count; i++)
        {
            Light l = _scratch[i];
            if (l == null || l == exclude) continue;
            if (l.type == LightType.Directional) continue;
            if (l.GetComponent<GrassPointLight>() != null) continue;

            var marker = l.gameObject.AddComponent<GrassPointLight>();
            marker.grassStrength = strength;
        }
    }
}
