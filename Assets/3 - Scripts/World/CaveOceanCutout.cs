using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tells the ocean post-process which volumes are CAVE, so it doesn't draw water
/// there. This is the actual fix for water in caves; everything before it was a
/// workaround.
///
/// WHY IT HAS TO WORK THIS WAY
/// The ocean is an analytic sphere rendered as a post-process. It has no concept
/// of a cave, so a tunnel dug below sea level is, as far as it knows, full of
/// water — and because PlanetEffects composites the underwater material AFTER
/// the atmosphere, being "submerged" in a cave also painted the sky black.
/// Nothing outside the shader can fix that:
///
///   • suppressing PlanetEffects.displayOceans killed the whole planet's sea
///   • a depth lid over the mouth only helped the one view that looks straight in
///
/// So OceanEffect.shader now takes a list of cave capsules and skips water whose
/// span lies inside one. This script is what fills that list. The shader change
/// is strictly additive: with zero capsules registered it behaves exactly as it
/// always did, so every scene without a cave is untouched.
///
/// Capsules come straight from the CaveVolume components already in the scene —
/// the same shapes used for the swim test — so there is one description of
/// "where is the cave" and it can't drift.
///
/// Auto-singleton with MainMenu skip — ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1).
/// </summary>
[DefaultExecutionOrder(-100)]   // globals set before any camera renders
public class CaveOceanCutout : MonoBehaviour
{
    public static CaveOceanCutout Instance { get; private set; }

    /// Must match MAX_CAVE_CAPSULES in OceanEffect.shader.
    public const int MaxCapsules = 32;

    /// <summary>Bisect switch for GrassPopDiagnostic. False publishes zero
    /// capsules, which CLAUDE.md documents as making both cave-aware shaders
    /// behave EXACTLY as they did before the cave feature existed — so it is a
    /// complete, zero-risk test of "is the cave cutout involved?" without
    /// deleting anything.</summary>
    public static bool CutoutEnabled = true;

    static readonly int NumId = Shader.PropertyToID("_NumCaveCapsules");
    static readonly int AId = Shader.PropertyToID("_CaveCapsuleA");
    static readonly int BId = Shader.PropertyToID("_CaveCapsuleB");

    readonly Vector4[] _a = new Vector4[MaxCapsules];
    readonly Vector4[] _b = new Vector4[MaxCapsules];
    int _lastCount = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("CaveOceanCutout");
        DontDestroyOnLoad(go);
        go.AddComponent<CaveOceanCutout>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        // Leave no cutouts behind, or a scene without caves would keep holes in
        // its ocean.
        Shader.SetGlobalInt(NumId, 0);
    }

    void LateUpdate()
    {
        // The planet MOVES (it orbits, and the floating origin rebases the whole
        // world), so these are world-space and must be refreshed every frame.
        // A cached set would leave the cutouts behind after the first origin
        // shift and the water would come back.
        int n = 0;
        var caves = CaveVolume.All;
        for (int c = 0; c < caves.Count && n < MaxCapsules; c++)
        {
            var cave = caves[c];
            if (cave == null || cave.capsuleA == null) continue;

            int count = Mathf.Min(cave.capsuleA.Length,
                        Mathf.Min(cave.capsuleB.Length, cave.capsuleR.Length));
            for (int i = 0; i < count && n < MaxCapsules; i++)
            {
                Vector3 a = cave.transform.TransformPoint(cave.capsuleA[i]);
                Vector3 b = cave.transform.TransformPoint(cave.capsuleB[i]);
                // Padded, so the cutout reaches the rock rather than stopping
                // short of it and leaving a rind of water against the walls.
                float r = cave.capsuleR[i] * cave.radiusPadding * cave.oceanCutoutPadding;
                _a[n] = new Vector4(a.x, a.y, a.z, r);
                _b[n] = new Vector4(b.x, b.y, b.z, 0f);
                n++;
            }
        }

        if (n == 0 && _lastCount == 0) return;   // nothing to do, and nothing to clear

        Shader.SetGlobalVectorArray(AId, _a);
        Shader.SetGlobalVectorArray(BId, _b);
        Shader.SetGlobalInt(NumId, CutoutEnabled ? n : 0);

        if (n >= MaxCapsules && _lastCount < MaxCapsules)
            Debug.LogWarning($"[CaveOceanCutout] Hit the {MaxCapsules}-capsule limit — " +
                             "some of the cave will still show water. Raise MaxCapsules here " +
                             "AND MAX_CAVE_CAPSULES in OceanEffect.shader together.");
        _lastCount = n;
    }
}
