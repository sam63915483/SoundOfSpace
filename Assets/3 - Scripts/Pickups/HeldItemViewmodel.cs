using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shows the SELECT-ONLY hotbar items — wood, crystal, space dust, saplings,
/// fish and fish bags — in the player's right hand, floating on a
/// <see cref="ViewmodelMotor"/> like every other equippable.
///
/// Those slots have no controller behind them (Hotbar.IsSelectOnlyItem), so
/// selecting one used to do nothing but highlight the hotbar cell. This gives
/// them a body: pick wood and you're carrying a log, pick a fish and you're
/// holding that actual fish, at the same spot the gun and axe sit.
///
/// Two kinds of content:
///   • 2D icons (wood / crystal / dust / sapling / fish bag) → a thick layered
///     SpriteSlab facing the camera. Flat art, but with real depth so it reads
///     as an object rather than a decal.
///   • Fish → the real 3D prefab from FishingdexManager, tinted and stretched
///     by the caught fish's own colour and weight, exactly like the dex render.
///
/// Auto-singleton (SpaceDustInventory pattern) and seeded in
/// MainMenuController.EnsureGameplaySingletons — RuntimeInitializeOnLoadMethod
/// fires once after the FIRST scene, which in a build is MainMenu, so a
/// MainMenu-skipping singleton never auto-creates in builds (CLAUDE.md trap #1).
/// </summary>
public class HeldItemViewmodel : MonoBehaviour
{
    public static HeldItemViewmodel Instance { get; private set; }

    [Tooltip("Camera-space offset from the shared hold transform. Matches the pistol's PistolMotor.restOffset so held resources sit where the gun sits.")]
    public Vector3 restOffset = new Vector3(0.055f, -0.02f, 0.32f);
    [Tooltip("World size (metres) of the longest edge of a 2D icon slab.")]
    public float iconWorldSize = 0.24f;
    [Tooltip("World size (metres) of the longest edge of a 3D fish model.")]
    public float fishWorldSize = 0.34f;
    [Tooltip("Extra rotation applied to a held fish so it presents side-on rather than nose-first.")]
    public Vector3 fishRotationOffset = new Vector3(0f, -70f, 12f);
    [Tooltip("Degrees per second the held item turns on the spot. 0 = held still, which is the default — a spinning item in your hand reads as a pickup prop, not something you're carrying. The ViewmodelMotor's sway does the 'alive' part.")]
    public float idleSpinSpeed = 0f;
    [Tooltip("Degrees of gentle rocking. Small on purpose — it's a float, not a spin. 0 to hold perfectly rigid.")]
    public float idleRockDegrees = 3.5f;

    [Header("Eating (hold fire on a raw fish)")]
    [Tooltip("Metres in front of the camera the fish is raised to while being eaten.")]
    public float eatForwardDistance = 0.36f;
    [Tooltip("Camera-space vertical offset of the eating pose — slightly low so the fish sits at mouth height rather than over the crosshair.")]
    public float eatVerticalOffset = -0.10f;
    [Tooltip("Seconds to raise the item to the mouth and lower it again.")]
    public float eatTransitionDuration = 0.12f;
    [Tooltip("Metres the fish bobs while being chewed.")]
    public float eatBobAmplitude = 0.035f;
    [Tooltip("Chews per second.")]
    public float eatBobSpeed = 6.5f;
    [Tooltip("Chewing sound, looped while eating. Left empty it borrows the bonfire's authored eat clip.")]
    public AudioClip eatLoopClip;
    [Range(0f, 1f)] public float eatVolume = 0.75f;

    ViewmodelMotor _motor;
    Transform _content;
    Hotbar.ItemId _shownId = Hotbar.ItemId.None;
    FishEntry _shownFish;
    string _shownSpecies;
    string _shownCassette;
    float _spin;
    Transform _holdParent;
    float _nextHoldSearch;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("HeldItemViewmodel");
        DontDestroyOnLoad(go);
        go.AddComponent<HeldItemViewmodel>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void LateUpdate()
    {
        var hb = Hotbar.Instance;
        if (hb == null) { Clear(); return; }

        Hotbar.Slot slot = hb.GetEquippedSlot();
        Hotbar.ItemId id = slot.id;

        // Hotbar.UnequipAll already clears the selection during dialogue and
        // while the phone is open, so the id check covers those. Piloting is
        // the one state it doesn't clear.
        bool show = id != Hotbar.ItemId.None
                 && Hotbar.IsSelectOnlyItem(id)
                 && !Ship.AnyShipPiloted;

        if (!show) { StopEatAudio(); _eatBlend = 0f; Clear(); return; }

        // Fish identity matters as well as the id — swapping between two fish
        // slots has to rebuild, or you'd keep carrying the first one's model.
        // Same for mushroom SPECIES: two stacks share the id but not the model.
        bool fishChanged = id == Hotbar.ItemId.Fish && !ReferenceEquals(slot.fishData, _shownFish);
        bool speciesChanged = Hotbar.IsMushroomItem(id) && slot.mushroomSpecies != _shownSpecies;
        // And the same again for a cassette's SONG: two tapes share the id but
        // not the shell colour, so switching stacks has to rebuild the model.
        bool songChanged = id == Hotbar.ItemId.Cassette && slot.cassetteId != _shownCassette;
        if (id != _shownId || fishChanged || speciesChanged || songChanged
            || _motor == null || _content == null)
        {
            if (!Rebuild(slot)) { Clear(); return; }
        }

        // Slow turn-in-hand so the item reads as an object being carried rather
        // than a billboard stuck to the camera. The motor supplies the sway;
        // this is just the item's own idle motion.
        if (_content != null)
        {
            _spin += idleSpinSpeed * Time.deltaTime;
            float rock = Mathf.Sin(Time.time * 0.8f) * idleRockDegrees;
            _content.localRotation = _baseContentRot * Quaternion.Euler(rock * 0.4f, _spin, rock);
            UpdateEating(hb);
        }
    }

    // ── Eating ─────────────────────────────────────────────────────────────
    // Hold fire on a selected raw fish and the Hotbar fills a progress ring,
    // then consumes it. This gives that its physical half: the fish is raised to
    // the middle of the view (the same camera-space anchoring the pistol's ADS
    // pose uses), bobs as if being bitten, and a chew loop runs for exactly as
    // long as the ring is filling.
    void UpdateEating(Hotbar hb)
    {
        bool eating = hb.IsEatingHeldItem
                      && (_shownId == Hotbar.ItemId.Fish || _shownId == Hotbar.ItemId.Mushroom);

        float step = eatTransitionDuration > 0.0001f ? Time.deltaTime / eatTransitionDuration : 1f;
        _eatBlend = Mathf.MoveTowards(_eatBlend, eating ? 1f : 0f, step);

        if (eating) StartEatAudio(); else StopEatAudio();

        if (_eatBlend <= 0.0001f)
        {
            _content.localPosition = Vector3.zero;
            return;
        }

        // Target expressed in CAMERA space, then converted into the rig's frame,
        // so it lands dead centre regardless of where the hold transform sits.
        // restOffset is subtracted because the rig itself rests there — without
        // it the "hold it further out" dial would shove the eating pose off-centre.
        Vector3 target = Vector3.zero;
        Transform hold = _motor != null ? _motor.transform.parent : null;
        Transform cam = ResolveCamera(hold);
        if (cam != null && hold != null)
        {
            Vector3 camPoint = new Vector3(0f, eatVerticalOffset, eatForwardDistance);
            target = hold.InverseTransformPoint(cam.TransformPoint(camPoint)) - restOffset;
        }

        // Chew bob, strongest at full raise so it doesn't judder on the way up.
        float bob = Mathf.Sin(Time.time * eatBobSpeed * Mathf.PI * 2f) * eatBobAmplitude * _eatBlend;
        _content.localPosition = Vector3.Lerp(Vector3.zero, target, _eatBlend) + Vector3.up * bob;
    }

    void StartEatAudio()
    {
        if (_eatSource == null)
        {
            _eatSource = gameObject.AddComponent<AudioSource>();
            _eatSource.playOnAwake = false;
            _eatSource.loop = true;
            _eatSource.spatialBlend = 0f;   // 2D — it's happening at the player's own mouth
        }
        if (_eatSource.clip == null) _eatSource.clip = ResolveEatClip();
        if (_eatSource.clip == null) return;
        _eatSource.volume = eatVolume;
        if (!_eatSource.isPlaying) _eatSource.Play();
    }

    void StopEatAudio()
    {
        if (_eatSource != null && _eatSource.isPlaying) _eatSource.Stop();
    }

    /// This singleton is auto-created, so it has no Inspector to wire an asset
    /// into. Preference order: an explicitly-set clip → PlayerSuitAudio (the
    /// Inspector-wired home for player-body sounds, where the generated chew
    /// loop lives) → the bonfire's already-authored eat clip as a last resort.
    /// Only called when a chew actually starts (once per fish), so the lookup
    /// cost is irrelevant and it re-resolves naturally after a scene load.
    /// Null is fine — eating is just silent.
    AudioClip ResolveEatClip()
    {
        if (eatLoopClip != null) return eatLoopClip;

        var suit = PlayerSuitAudio.Instance;
        if (suit != null && suit.EatLoopClip != null)
        {
            eatVolume = suit.EatLoopVolume;
            return suit.EatLoopClip;
        }

        var bonfire = FindObjectOfType<BonfireInteraction>(true);
        if (bonfire != null && bonfire.EatClip != null)
        {
            eatVolume = Mathf.Min(eatVolume, bonfire.EatVolume);
            return bonfire.EatClip;
        }
        return null;
    }

    static Transform ResolveCamera(Transform from)
    {
        for (Transform t = from; t != null; t = t.parent)
            if (t.GetComponent<Camera>() != null) return t;
        return Camera.main != null ? Camera.main.transform : null;
    }

    Quaternion _baseContentRot = Quaternion.identity;
    float _eatBlend;
    AudioSource _eatSource;

    bool Rebuild(Hotbar.Slot slot)
    {
        Clear();

        Transform hold = ResolveHoldParent();
        if (hold == null) return false;

        _motor = ViewmodelMotor.CreateRig(hold, "HeldItemMotorRig", restOffset);

        GameObject content = slot.id == Hotbar.ItemId.Fish
            ? BuildFish(slot.fishData)
            : Hotbar.IsMushroomItem(slot.id)
                ? BuildMushroom(slot)
                : BuildIcon(slot);

        if (content == null)
        {
            Destroy(_motor.gameObject);
            _motor = null;
            return false;
        }

        content.transform.SetParent(_motor.transform, false);
        content.transform.localPosition = Vector3.zero;
        ViewmodelMotor.MakeViewmodel(content);

        _content = content.transform;
        _shownId = slot.id;
        _shownFish = slot.fishData;
        _shownSpecies = slot.mushroomSpecies;
        _shownCassette = slot.cassetteId;
        _spin = 0f;
        return true;
    }

    /// The real species prefab in the player's hand — same model as the world
    /// mushroom it came off and the ground drop it was picked up from. Spores
    /// (mushroom saplings) present as a smaller cap so the two read apart at a
    /// glance without needing separate art.
    GameObject BuildMushroom(Hotbar.Slot slot)
    {
        float size = slot.id == Hotbar.ItemId.MushroomSapling
            ? mushroomWorldSize * 0.55f
            : mushroomWorldSize;
        var go = MushroomRegistry.BuildModel(slot.mushroomSpecies, "Held_Mushroom", size);
        if (go == null) return null;
        _baseContentRot = Quaternion.Euler(mushroomRotationOffset);
        return go;
    }

    GameObject BuildIcon(Hotbar.Slot slot)
    {
        // The fish bag's icon reflects whether it has anything in it, so ask the
        // hotbar rather than the flat resource-icon table.
        Sprite icon = slot.id == Hotbar.ItemId.FishBag
            ? Hotbar.ResolveFishBagSprite(slot.bagContents)
            // A tape's shell is its TIER, which only this stack knows.
            : slot.id == Hotbar.ItemId.Cassette
            ? Hotbar.CassetteSpriteFor(slot.cassetteId)
            : Hotbar.ResourceIcon(slot.id);
        if (icon == null) return null;

        var go = SpriteSlab.Build(icon, $"Held_{slot.id}");
        if (go == null) return null;

        go.transform.localScale = Vector3.one * (iconWorldSize / SpriteSlab.LongestEdge(icon));
        // The slab's art faces +Z; the camera looks down +Z, so spin it round to
        // present the face to the player as its rest pose.
        _baseContentRot = Quaternion.Euler(0f, 180f, 0f);
        return go;
    }

    GameObject BuildFish(FishEntry entry)
    {
        var dex = FishingdexManager.Instance;
        if (dex == null) return null;

        string type = entry != null ? entry.fishType : null;
        GameObject prefab = type == "Rare"     ? dex.rareFishPrefab
                          : type == "Uncommon" ? dex.uncommonFishPrefab
                                               : dex.commonFishPrefab;
        if (prefab == null) return null;

        var go = Instantiate(prefab);
        go.name = "Held_Fish";

        // Same presentation the Fishingdex uses: weight drives length along X,
        // and the catch's stored colour tints it.
        //
        // `renderer.material.color` on purpose, matching FishingdexManager
        // .RenderFish exactly. An earlier pass used a MaterialPropertyBlock to
        // avoid instancing a material per rebuild — but the tint silently didn't
        // take and every held fish came out the prefab's flat green, because
        // Material.color resolves the shader's MAIN colour property (whatever
        // it's named) while a property block has to guess at "_Color". Matching
        // the dex is what guarantees the fish in your hand is the fish in the
        // hotbar. The instanced materials go with the GameObject on Clear().
        if (entry != null)
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                r.material.color = entry.fishColor;

        // Normalise to a WEIGHT-DRIVEN length, not a constant. The old flow
        // stretched X by weight and then normalised the longest edge to
        // fishWorldSize -- and a fish's length IS its longest edge, so the
        // normalise cancelled the weight scaling exactly and every fish in
        // hand came out the same size. Sam, 2026-09-02: "a 50 lb rare looks
        // the same weight as a 5 pound common." Same cube-root law as the
        // fish on the line, scaled down for the hand.
        float targetLen = entry != null
            ? FishingRules.BodyLengthForWeight(entry.weightLbs) * heldFishScale
            : fishWorldSize;
        float longest = LongestRendererEdge(go);
        if (longest > 0.0001f)
            go.transform.localScale = Vector3.Scale(go.transform.localScale,
                                                    Vector3.one * (targetLen / longest));
        // The SAME girth law the fish on the line uses (models face -Z: X =
        // width full factor, Y = belly 60%) -- one shape, everywhere, always.
        if (entry != null)
        {
            float girth = FishingRules.GirthFactorForWeight(entry.weightLbs);
            go.transform.localScale = Vector3.Scale(go.transform.localScale,
                new Vector3(girth, 1f + (girth - 1f) * 0.6f, 1f));
        }

        _baseContentRot = Quaternion.Euler(fishRotationOffset);
        return go;
    }

    static float LongestRendererEdge(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return 0f;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        Vector3 s = b.size;
        return Mathf.Max(s.x, Mathf.Max(s.y, s.z));
    }

    /// The shared right-hand hold transform. Every equippable controller points
    /// at the same one (CLAUDE.md: "axeHoldPosition itself is shared with the
    /// rod/pistol"), so take whichever is wired up. Cached; re-searched at most
    /// twice a second while missing rather than per-frame.
    Transform ResolveHoldParent()
    {
        if (_holdParent != null) return _holdParent;
        if (Time.time < _nextHoldSearch) return null;
        _nextHoldSearch = Time.time + 0.5f;

        var pistol = FindObjectOfType<PistolController>();
        if (pistol != null && pistol.pistolHoldPosition != null) return _holdParent = pistol.pistolHoldPosition;
        var axe = FindObjectOfType<AxeController>();
        if (axe != null && axe.axeHoldPosition != null) return _holdParent = axe.axeHoldPosition;
        var rod = FindObjectOfType<FishingRodController>();
        if (rod != null && rod.rodHoldPosition != null) return _holdParent = rod.rodHoldPosition;
        var bottle = FindObjectOfType<WaterBottleController>();
        if (bottle != null && bottle.bottleHoldPosition != null) return _holdParent = bottle.bottleHoldPosition;
        return null;
    }

    void Clear()
    {
        if (_motor != null) Destroy(_motor.gameObject);
        _motor = null;
        _content = null;
        _shownId = Hotbar.ItemId.None;
        _shownFish = null;
        _shownSpecies = null;
    }

    // -- appended after initial release; keep field order (serialization) --

    [Header("Mushrooms")]
    [Tooltip("World size (metres) of the longest edge of a held mushroom. Spores (mushroom saplings) render at 55% of this.")]
    public float mushroomWorldSize = 0.26f;
    [Tooltip("Extra rotation applied to a held mushroom so the cap presents to the camera rather than pointing away.")]
    public Vector3 mushroomRotationOffset = new Vector3(-12f, 160f, 0f);

    [Header("Fish Size (weight-driven)")]
    [Tooltip("Fraction of the fish's true body length (FishingRules.BodyLengthForWeight) used for the in-hand display. 1 = life size; smaller keeps a 50 lb beast from blocking the whole camera. At 0.75, 1 lb ~ 0.26 m and 50 lb ~ 0.94 m in hand.")]
    [Range(0.25f, 1f)] public float heldFishScale = 0.75f;
}
