using UnityEngine;

/// <summary>
/// Every fishing knob in one place, per the handoff's "expose all as
/// [SerializeField] on a FishingTuning ScriptableObject".
///
/// <b>It works with no asset assigned.</b> Creating and wiring a .asset needs
/// the Editor, which this build pass cannot drive, and a null tuning reference
/// would null-ref the whole loop. So every field defaults to the value the
/// headless tests in verify-fishing.py run against (<see cref="FishingRules"/>),
/// and <see cref="Active"/> hands back a built-in instance until Sam creates one
/// via Assets > Create > Fishing > Fishing Tuning and drops it on the Bobber
/// prefab. Nothing breaks either way; assigning an asset just makes the knobs
/// editable without a recompile.
/// </summary>
[CreateAssetMenu(fileName = "FishingTuning", menuName = "Fishing/Fishing Tuning")]
public class FishingTuning : ScriptableObject
{
    [Header("Bite timing")]
    [Tooltip("Seconds between bites before the sun-angle multiplier. The multiplier is 0.5x at twilight, 0.75x at night, 0.85x in daylight; no bait adds a mild 1.15x. Worst case (noon, bare hook) is ~5-14s -- the bands differ in WHAT bites, not whether anything does.")]
    public float baseWaitMin = 5f;
    public float baseWaitMax = 14f;

    [Tooltip("How long the hook window stays open, per tier. Miss it and the bait is gone.")]
    public float hookWindowCommon   = 3f;
    public float hookWindowUncommon = 2f;
    public float hookWindowRare     = 1.5f;

    [Header("Fight")]
    [Tooltip("Tension gained per second of reeling, multiplied by the tier's pull.")]
    public float reelRate = FishingRules.ReelRate;
    [Tooltip("Tension shed per second while the reel is released.")]
    public float relaxRate = FishingRules.RelaxRate;
    [Tooltip("Stamina spent per second of reeling. 1 means the species table's stamina numbers are literally seconds of reeling.")]
    public float drainRate = FishingRules.DrainRate;
    [Tooltip("Seconds of slack at zero tension before the fish shakes the hook. Anti-stuck-state, not a mechanic.")]
    public float slackEscapeSeconds = FishingRules.SlackEscapeSeconds;

    [Header("Fight — stamina scale")]
    [Tooltip("Multiplier on how much RUN each fish has in it. Stamina is no longer the win condition (distance is) — it is how long the fish keeps surging before it gives up. Below 1 = fish tire sooner. At 1.0 the headless sim reports median fights of 3.0s common, 7.1s uncommon, 17.5s rare.")]
    [Range(0.2f, 2f)] public float staminaScale = 1f;

    [Header("Landing")]
    [Tooltip("Metres along the WATER (height ignored, so a clifftop works) at which the fish counts as landed. The bobber touching terrain also lands it, whichever happens first.")]
    public float landDistance = FishingRules.LandDistance;

    [Header("Line")]
    [Tooltip("Seconds for the line to come tight once you pull (or the fish does). NOTHING downstream happens until it gets there: no rod bend, no fish moving, no bar filling.")]
    public float lineTautSeconds = FishingRules.TautSeconds;
    [Tooltip("Seconds for the line to fall slack again once the pressure comes off. DELIBERATELY the slowest thing in the chain -- the rod springs back first, then the line slowly droops.")]
    public float lineSlackSeconds = FishingRules.SlackSeconds;

    [Header("Reeling (distance model)")]
    [Tooltip("Metres per second the reel gains on a fish that isn't resisting. The fight is won by bringing the fish IN, so this is the main pacing dial.")]
    public float reelSpeed = FishingRules.ReelSpeed;

    [Header("Rod — hauling back (whole rod)")]
    [Tooltip("Degrees the rod is hauled BACK and up while you hold the reel. This is you pulling away from the fish, not the rod flexing -- and it happens IMMEDIATELY on the click, before the line is tight and long before the rod bends.")]
    public float reelPullBackAngle = 18f;
    [Tooltip("Extra degrees of haul when you reel against a running fish — the big heave.")]
    public float runPullBackExtra = 7f;

    [Header("Rod — flex (mesh deformation)")]
    [Tooltip("Degrees the rod's MESH bows at full load. The prefab has no bones, so RodBend deforms the vertices; the tip swings and the handle stays put.")]
    public float maxRodBend = 38f;
    // Load shares live on FishFightSim.ActiveLoad (reeling 0.45, running 0.55)
    // and the shape of the bend on FishingRules.BendCurve, so the rod, the line
    // and the snap all read the same number. Two places computing "how loaded is
    // this" is exactly how the promise/grade class of bug starts.
    [Tooltip("How quickly the rod LOADS UP under strain. Higher = stiffer.")]
    public float rodBendResponse = 12f;
    [Tooltip("How quickly the rod springs BACK when the strain comes off. Faster than it loads, but NOT instant -- 30 looked like a glitch. It must finish well before the line has finished drooping.")]
    public float rodReleaseResponse = 7f;
    [Tooltip("Load at which the bend stops being gentle and starts running away toward maximum.")]
    [Range(0.1f, 0.9f)] public float bendKnee = FishingRules.BendKnee;
    [Tooltip("Fraction of the full bend reached AT the knee. Raise for a rod that works visibly under light load; lower to save the bow for the breaking point.")]
    [Range(0f, 1f)] public float bendAtKnee = FishingRules.BendAtKnee;

    // ── Built-in fallback ────────────────────────────────────────────────────

    static FishingTuning _builtIn;

    /// <summary>The assigned asset if there is one, otherwise built-in defaults.</summary>
    public static FishingTuning Active
    {
        get
        {
            if (_assigned != null) return _assigned;
            if (_builtIn == null)
            {
                _builtIn = CreateInstance<FishingTuning>();
                _builtIn.name = "FishingTuning (built-in defaults)";
                _builtIn.hideFlags = HideFlags.HideAndDontSave;
            }
            return _builtIn;
        }
    }

    static FishingTuning _assigned;

    /// <summary>Called by the Bobber when it has an asset in its inspector slot.</summary>
    public static void Use(FishingTuning asset)
    {
        if (asset != null) _assigned = asset;
    }

    public float HookWindowFor(FishTier tier) => tier switch
    {
        FishTier.Rare     => hookWindowRare,
        FishTier.Uncommon => hookWindowUncommon,
        _                 => hookWindowCommon,
    };
}
