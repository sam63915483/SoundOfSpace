using UnityEngine;

/// <summary>
/// Answers, for the held-item sync: "what is the local player holding?" and
/// "build me that thing so it can be put in a puppet's hand".
///
/// ── Why nothing is sent but an id and a variant string ───────────────────
/// Both machines run the same build, so both already have every prefab, icon
/// and mushroom model. The owner sends an ItemId plus a short variant tag
/// (mushroom species, fish rarity) and each receiver builds the visual locally
/// — the same approach PlanetRelativeSync uses to give the remote flashlight
/// the right colour without sending it.
///
/// ── Two families, both handled ───────────────────────────────────────────
/// EQUIPPABLES (water bottle, rod, guitar, axe, pistol) have a controller and a
/// prefab. SELECT-ONLY items (wood, crystal, space dust, saplings, mushrooms,
/// fish, fish bags) have no controller — but they are absolutely visible in the
/// holder's hand, via HeldItemViewmodel, as sprite slabs / real mushroom models
/// / real fish models. An earlier version of this file wrongly claimed they had
/// no visual; it had only checked Hotbar.IsSelectOnly and stopped there.
///
/// ── Sizing is not optional ───────────────────────────────────────────────
/// ViewmodelMotor.NormalizeSize exists because "world props are authored at
/// world size, which is far too big held 30cm from the eye". The first-person
/// path normalises (WaterBottleController does it explicitly; AxeController
/// applies axeScale; HeldItemViewmodel scales every icon and model to a target
/// edge). A raw Instantiate does not — which is why the synced water bottle came
/// out enormous. Everything built here goes through the same normaliser.
/// </summary>
public static class HeldItemResolver
{
    // ── local player's controllers, found once ───────────────────────────

    static WaterBottleController _water;
    static FishingRodController  _rod;
    static GuitarController      _guitar;
    static AxeController         _axe;
    static PistolController      _pistol;
    static float _nextScan;

    static void EnsureControllers()
    {
        bool anyMissing = _water == null || _rod == null || _guitar == null
                       || _axe == null || _pistol == null;
        if (!anyMissing) return;
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 0.5f;

        // All five live on the scene Player object — note NOT on Player.prefab,
        // which carries none of them.
        var player = Object.FindObjectOfType<PlayerController>();
        if (player == null) return;

        if (_water  == null) _water  = player.GetComponentInChildren<WaterBottleController>(true);
        if (_rod    == null) _rod    = player.GetComponentInChildren<FishingRodController>(true);
        if (_guitar == null) _guitar = player.GetComponentInChildren<GuitarController>(true);
        if (_axe    == null) _axe    = player.GetComponentInChildren<AxeController>(true);
        if (_pistol == null) _pistol = player.GetComponentInChildren<PistolController>(true);
    }

    // ── what am I holding? ───────────────────────────────────────────────

    /// <summary>
    /// The item this machine's player has in hand, plus a variant tag that
    /// distinguishes two stacks sharing one ItemId (mushroom species, fish
    /// rarity). Empty variant for everything else.
    ///
    /// Equippables are read from the controllers' own IsEquipped rather than the
    /// Hotbar slot: the slot can be selected while the equip animation is still
    /// playing, and what we mirror is "is the model actually in my hand".
    /// Select-only items have no controller, so those come from the slot.
    /// </summary>
    public static Hotbar.ItemId LocalHeld(out string variant)
    {
        variant = "";
        EnsureControllers();

        if (_pistol != null && _pistol.IsEquipped) return Hotbar.ItemId.Pistol;
        if (_axe    != null && _axe.IsEquipped)    return Hotbar.ItemId.Axe;
        if (_guitar != null && _guitar.IsEquipped) return Hotbar.ItemId.Guitar;
        if (_rod    != null && _rod.IsEquipped)    return Hotbar.ItemId.FishingRod;
        if (_water  != null && _water.IsEquipped)  return Hotbar.ItemId.WaterBottle;

        // Select-only: mirror exactly what HeldItemViewmodel is showing, and
        // under the same conditions, so the two never disagree.
        var hb = Hotbar.Instance;
        if (hb == null) return Hotbar.ItemId.None;

        var slot = hb.GetEquippedSlot();
        if (slot.id == Hotbar.ItemId.None) return Hotbar.ItemId.None;
        if (!Hotbar.IsSelectOnlyItem(slot.id)) return Hotbar.ItemId.None;
        if (Ship.AnyShipPiloted) return Hotbar.ItemId.None;

        if (Hotbar.IsMushroomItem(slot.id)) variant = slot.mushroomSpecies ?? "";
        else if (slot.id == Hotbar.ItemId.Fish && slot.fishData != null)
            variant = slot.fishData.fishType ?? "";

        return slot.id;
    }

    // ── build the visual ─────────────────────────────────────────────────

    /// <summary>
    /// Builds an unparented, unscaled instance for `id`, or null if there is
    /// nothing to show. The caller parents, orients, normalises and recentres it.
    /// </summary>
    public static GameObject BuildVisual(Hotbar.ItemId id, string variant)
    {
        EnsureControllers();

        // Equippables — the controller's own prefab.
        GameObject prefab = PrefabFor(id);
        if (prefab != null) return Object.Instantiate(prefab);

        // Mushrooms and spores — the real species model, same as the holder sees.
        if (Hotbar.IsMushroomItem(id))
        {
            // Size is applied by the caller's normaliser; 1 here just means
            // "don't pre-scale", since BuildModel takes a world size.
            var m = MushroomRegistry.BuildModel(variant, "RemoteHeld_Mushroom", 1f);
            return m;
        }

        // Fish — the rarity prefab. Per-catch colour and weight are NOT synced
        // (they would need the whole FishEntry on the wire); a remote player's
        // fish is the right species at the right size, untinted.
        if (id == Hotbar.ItemId.Fish)
        {
            var dex = FishingdexManager.Instance;
            if (dex == null) return null;
            GameObject fp = variant == "Rare"     ? dex.rareFishPrefab
                          : variant == "Uncommon" ? dex.uncommonFishPrefab
                                                  : dex.commonFishPrefab;
            return fp != null ? Object.Instantiate(fp) : null;
        }

        // Everything else select-only (wood, crystal, dust, saplings, fish bag)
        // is a flat icon presented as a thick slab — exactly what the holder is
        // looking at.
        Sprite icon = id == Hotbar.ItemId.FishBag
            ? Hotbar.ResolveFishBagSprite(null)
            : Hotbar.ResourceIcon(id);
        if (icon == null) return null;
        return SpriteSlab.Build(icon, "RemoteHeld_" + id);
    }

    public static GameObject PrefabFor(Hotbar.ItemId id)
    {
        EnsureControllers();
        switch (id)
        {
            case Hotbar.ItemId.Pistol:      return _pistol != null ? _pistol.pistolPrefab      : null;
            case Hotbar.ItemId.Axe:         return _axe    != null ? _axe.axePrefab            : null;
            case Hotbar.ItemId.Guitar:      return _guitar != null ? _guitar.guitarPrefab      : null;
            case Hotbar.ItemId.FishingRod:  return _rod    != null ? _rod.fishingRodPrefab     : null;
            case Hotbar.ItemId.WaterBottle: return _water  != null ? _water.waterBottlePrefab  : null;
            default: return null;
        }
    }

    // ── how big, and which way up ────────────────────────────────────────

    /// <summary>
    /// Target longest edge in metres, fed to ViewmodelMotor.NormalizeSize.
    ///
    /// A held object is the same real size whoever is looking at it, so these
    /// match the first-person world sizes wherever one is authored
    /// (bottleWorldSize, iconWorldSize, mushroomWorldSize, fishWorldSize) and
    /// are real-world estimates for the rest. Normalising rather than trusting
    /// prefab scale is the whole fix for "the water bottle is huge".
    /// </summary>
    public static float WorldSizeFor(Hotbar.ItemId id)
    {
        EnsureControllers();
        var vm = HeldItemViewmodel.Instance;

        switch (id)
        {
            case Hotbar.ItemId.Pistol:      return 0.28f;
            case Hotbar.ItemId.Axe:         return 0.80f;
            case Hotbar.ItemId.Guitar:      return 1.00f;
            case Hotbar.ItemId.FishingRod:  return 1.30f;
            case Hotbar.ItemId.WaterBottle: return _water != null ? _water.bottleWorldSize : 0.22f;
            case Hotbar.ItemId.Fish:        return vm != null ? vm.fishWorldSize : 0.34f;

            case Hotbar.ItemId.Mushroom:    return vm != null ? vm.mushroomWorldSize : 0.26f;
            case Hotbar.ItemId.MushroomSapling:
                                            return (vm != null ? vm.mushroomWorldSize : 0.26f) * 0.55f;

            default:                        return vm != null ? vm.iconWorldSize : 0.24f;
        }
    }

    /// <summary>
    /// Rotation for the model inside the hold anchor.
    ///
    /// Every first-person hold point (CameraHoldPos / GuitarHoldPos /
    /// BottleHoldPos) is a child of the player's CAMERA, so a controller's
    /// authored `holdRotationOffset` already means "the rotation that makes this
    /// model look correctly gripped by a holder facing forward".
    /// NetworkAvatarDetail builds its anchor with that same meaning — at the
    /// hand, oriented to body-forward × look-pitch — so the numbers transfer
    /// verbatim, and retuning first person retunes this too.
    ///
    /// Icons get 180° because SpriteSlab's art faces +Z and so does the anchor,
    /// which would otherwise present the slab's back to onlookers — the same
    /// correction HeldItemViewmodel.BuildIcon applies.
    /// </summary>
    public static Vector3 RotationFor(Hotbar.ItemId id)
    {
        EnsureControllers();
        var vm = HeldItemViewmodel.Instance;

        switch (id)
        {
            case Hotbar.ItemId.Pistol:      return _pistol != null ? _pistol.holdRotationOffset : Vector3.zero;
            case Hotbar.ItemId.Axe:         return _axe    != null ? _axe.holdRotationOffset    : Vector3.zero;
            case Hotbar.ItemId.Guitar:      return _guitar != null ? _guitar.holdRotationOffset : Vector3.zero;
            case Hotbar.ItemId.FishingRod:  return _rod    != null ? _rod.holdRotationOffset    : Vector3.zero;
            case Hotbar.ItemId.WaterBottle: return Vector3.zero;   // authored upright

            case Hotbar.ItemId.Fish:
                return vm != null ? vm.fishRotationOffset : new Vector3(0f, -70f, 12f);

            case Hotbar.ItemId.Mushroom:
            case Hotbar.ItemId.MushroomSapling:
                return vm != null ? vm.mushroomRotationOffset : new Vector3(-12f, 160f, 0f);

            default:
                return new Vector3(0f, 180f, 0f);   // slab faces the onlooker
        }
    }

    // ── where a held item actually lives ─────────────────────────────────

    /// <summary>
    /// The two numbers that place a held item, measured off THIS machine's own
    /// player rig (both machines run the same rig, so they are the same numbers
    /// for everyone).
    ///
    /// <paramref name="camInRoot"/> — the eye position in player-root space,
    /// measured (0.000, 0.670, 0.276).
    /// <paramref name="itemInCam"/> — where the item sits relative to the eye,
    /// measured (0.250, -0.200, 0.600): 25cm right, 20cm down, 60cm forward.
    ///
    /// ⚠️ A HELD ITEM IS NOT IN THE HAND. Every hold point in this game
    /// (CameraHoldPos and friends) is a child of the CAMERA, so an item orbits
    /// the eye and tracks the look direction. The astronaut's Hand.R bone sits
    /// at (0.334, -0.190, -0.080) — down at the hip and slightly behind — so
    /// parenting to it puts the item roughly a metre below and a metre behind
    /// where its owner is actually carrying it. That was the bug: to onlookers
    /// the axe hung at the hip while its owner had it up in front of them.
    ///
    /// Returns false if the local rig is not resolvable yet; callers fall back
    /// to the measured constants above, which are correct for the current rig.
    /// </summary>
    public static bool TryGetViewGeometry(out Vector3 camInRoot, out Vector3 itemInCam)
    {
        camInRoot = DefaultCamInRoot;
        itemInCam = DefaultItemInCam;

        var pc = Object.FindObjectOfType<PlayerController>();
        if (pc == null) return false;
        var cam = pc.GetComponentInChildren<Camera>(true);
        if (cam == null) return false;

        camInRoot = pc.transform.InverseTransformPoint(cam.transform.position);

        // ResolveSharedHoldPoint deliberately avoids the water bottle's own hold
        // field, which points at a BONE and would park items down at the side —
        // the very mistake this method exists to stop repeating.
        var hold = ViewmodelMotor.ResolveSharedHoldPoint(pc.gameObject, null);
        if (hold == null) return false;

        Vector3 world = hold.TransformPoint(ViewmodelMotor.ReferenceRestOffset(pc.gameObject));
        itemInCam = cam.transform.InverseTransformPoint(world);
        return true;
    }

    /// Measured from the live rig 2026-08-09. Used only if the local player rig
    /// cannot be found (a puppet spawning before the scene player exists).
    public static readonly Vector3 DefaultCamInRoot = new Vector3(0f, 0.670f, 0.276f);
    public static readonly Vector3 DefaultItemInCam = new Vector3(0.250f, -0.200f, 0.600f);

    // ── aiming down the sights ───────────────────────────────────────────

    /// This machine's pistol ADS blend: 0 hip, 1 fully aimed. 0 when no pistol.
    public static float LocalAimBlend()
    {
        EnsureControllers();
        return (_pistol != null && _pistol.IsEquipped) ? _pistol.AimBlend : 0f;
    }

    /// <summary>
    /// Where the pistol sits when aimed, in VIEW space — the same frame the hip
    /// offset uses, so a remote can simply lerp between the two.
    ///
    /// PistolController.GetAimPose anchors ADS "in CAMERA space: dead ahead,
    /// nudged down so the sights land on the crosshair", which is exactly this
    /// frame — so the controller's three ADS dials transfer with no conversion
    /// at all. Measured defaults: (0, -0.075, 0.42), i.e. centred and 42cm out,
    /// versus the hip's (0.25, -0.20, 0.60). That difference is the gun visibly
    /// sliding in from the right and up to the face.
    /// </summary>
    public static Vector3 AimPointInView()
    {
        EnsureControllers();
        if (_pistol == null) return new Vector3(0f, -0.075f, 0.42f);
        return new Vector3(_pistol.adsHorizontalOffset,
                           _pistol.adsVerticalOffset,
                           _pistol.adsForwardDistance);
    }

    /// Aimed rotation, view-space. The barrel ends up parallel to the view axis,
    /// which in this frame is just the authored ADS offset (zero by default).
    public static Vector3 AimRotation()
    {
        EnsureControllers();
        return _pistol != null ? _pistol.adsRotationOffset : Vector3.zero;
    }

    // ── cache lifetime ───────────────────────────────────────────────────

    /// Statics survive scene loads; the objects they point at do not. Unity's
    /// overloaded == already reports a destroyed object as null so
    /// EnsureControllers self-heals, but only after the 0.5s scan throttle —
    /// clearing on load makes the next lookup immediate.
    public static void Forget()
    {
        _water = null; _rod = null; _guitar = null; _axe = null; _pistol = null;
        _nextScan = 0f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void HookSceneLoad()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnAnySceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnAnySceneLoaded;
    }

    static void OnAnySceneLoaded(UnityEngine.SceneManagement.Scene s,
                                 UnityEngine.SceneManagement.LoadSceneMode m) => Forget();
}
