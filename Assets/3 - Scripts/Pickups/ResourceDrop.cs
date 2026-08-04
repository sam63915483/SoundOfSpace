using UnityEngine;

/// <summary>
/// Minecraft-style world drop. Chopping a tree / mining a crystal no longer
/// hands the resource straight to the hotbar — it spawns one of these at the
/// stump: a hotbar-icon sprite that pops out, settles hovering above the
/// ground, bobs + spins, and is collected by walking over it.
///
/// Deliberately NOT a physics object: it's parented to the CelestialBody the
/// resource grew on and animated in that body's local space, so it rides
/// planet rotation and floating-origin shifts for free (no Rigidbody, no
/// EndlessManager.RegisterPhysicsObject, no collider cost). Everything after
/// the one-time spawn raycast is a couple of trig calls per frame.
/// </summary>
public class ResourceDrop : MonoBehaviour
{
    // ── Tuning (static so all drops share one set of knobs; edit here) ──────
    const float kSpriteSize      = 0.55f;  // world metres, longest sprite edge
    // Slab depth / slice count live in SpriteSlab — shared with the held
    // hotbar viewmodel so both read as the same object.
    const float kHoverHeight     = 0.30f;  // metres above the ground it rests at (about a foot)
    const float kPopDuration     = 0.5f;   // seconds of the toss-out arc
    const float kPopUp           = 1.3f;   // arc apex height (metres)
    const float kPopSpread       = 1.1f;   // max horizontal scatter from the stump
    const float kSpinSpeed       = 95f;    // degrees/sec around the surface normal
    const float kBobAmplitude    = 0.12f;  // metres
    const float kBobSpeed        = 2.2f;   // radians/sec
    // No magnetism — the drop stays put, hovering and spinning, and is collected
    // by walking over it. The radius is measured from the PLAYER TRANSFORM's
    // origin (roughly chest height) to the hovering drop, so it has to cover
    // that vertical gap as well as the horizontal one; too tight and standing
    // right on top of an item doesn't register.
    const float kCollectRadius   = 1.4f;
    const float kPickupDelay     = 0.35f;  // seconds before it can be collected (so it visibly pops)
    const float kLifetime        = 300f;   // despawn after 5 minutes
    const float kFadeDuration    = 4f;     // fade out over the last N seconds
    const float kFullRetryDelay  = 2f;     // re-try interval while the inventory is full
    const float kMushroomDropSize = 0.42f; // world metres, longest edge of a dropped cap model

    // ── Spawning ───────────────────────────────────────────────────────────

    /// <summary>
    /// Scatter <paramref name="amount"/> of <paramref name="id"/> as world
    /// drops around <paramref name="worldPos"/>. Falls back to awarding the
    /// resource instantly (old behaviour) if the item has no hotbar icon to
    /// draw, so a missing sprite can never eat the player's loot.
    /// </summary>
    public static void Drop(Hotbar.ItemId id, int amount, Vector3 worldPos, Transform bodyParent)
    {
        if (amount <= 0) return;

        // Fall back to the old instant award when we can't make a good drop:
        //   • no hotbar icon to draw (e.g. saplings, which ship without a sprite)
        //   • no hotbar yet
        //   • no celestial body to parent to — an unparented drop wouldn't ride
        //     floating-origin shifts and would visibly slide away from the stump.
        Sprite icon = Hotbar.ResourceIcon(id);
        if (icon == null || Hotbar.Instance == null || bodyParent == null)
        {
            AwardDirect(id, amount, worldPos);
            return;
        }

        // Split the payload into a handful of visible chunks — one sprite for a
        // 20-log tree reads as a bug, five reads as a felled tree.
        int chunks = Mathf.Clamp(Mathf.CeilToInt(amount / 5f), 1, 5);
        int remaining = amount;
        for (int i = 0; i < chunks; i++)
        {
            int share = (i == chunks - 1) ? remaining : Mathf.Max(1, Mathf.RoundToInt((float)amount / chunks));
            share = Mathf.Min(share, remaining);
            remaining -= share;
            if (share <= 0) continue;
            SpawnOne(id, share, icon, worldPos, bodyParent);
            if (remaining <= 0) break;
        }
    }

    /// <summary>
    /// Mushroom drops. Unlike wood/crystal these are NOT sprite slabs — each one
    /// is a real (render-only) instance of the chopped species' prefab, so the
    /// thing lying on the ground is visibly the mushroom you just felled. They
    /// pop, hover, bob and spin identically to a log, and are collected by
    /// walking over them.
    ///
    /// One object per unit rather than the chunked split logs use: a mushroom
    /// yields 3–9, and nine little caps scattered round the stump reads exactly
    /// right where a single icon carrying "×9" would not.
    /// </summary>
    public static void DropMushrooms(Hotbar.ItemId id, string species, int amount,
                                     Vector3 worldPos, Transform bodyParent)
    {
        if (amount <= 0) return;
        if (Hotbar.Instance == null || bodyParent == null || !Hotbar.IsMushroomItem(id))
        {
            AwardDirect(id, amount, worldPos, species);
            return;
        }

        int spawned = 0;
        for (int i = 0; i < amount && i < 12; i++)
        {
            var go = MushroomRegistry.BuildModel(species, $"ResourceDrop_{id}", kMushroomDropSize);
            if (go == null) break;
            var drop = go.AddComponent<ResourceDrop>();
            drop._species = species;
            drop.Init(id, 1, null, null, worldPos, bodyParent);
            spawned++;
        }
        // No model available (registry not resolved yet) — never eat the loot.
        if (spawned < amount) AwardDirect(id, amount - spawned, worldPos, species);
    }

    static void AwardDirect(Hotbar.ItemId id, int amount, Vector3 worldPos, string species = null)
    {
        if (Hotbar.Instance == null) return;
        int leftover = Hotbar.Instance.AddResource(id, amount, species);
        if (leftover > 0) InventoryFullPopup.Show();
        ShowPopup(id, amount - leftover, worldPos);
    }

    static void SpawnOne(Hotbar.ItemId id, int amount, Sprite icon, Vector3 worldPos, Transform bodyParent)
    {
        // Thick layered slab rather than a flat quad — shared with the held
        // hotbar viewmodel so a log on the ground and a log in your hand are
        // literally the same mesh. See SpriteSlab for why it's sliced.
        var go = SpriteSlab.Build(icon, $"ResourceDrop_{id}");
        if (go == null) return;

        var drop = go.AddComponent<ResourceDrop>();
        drop.Init(id, amount, icon, go.GetComponent<MeshRenderer>(), worldPos, bodyParent);
    }

    // ── Instance ───────────────────────────────────────────────────────────

    Hotbar.ItemId _id;
    int _amount;
    string _species;             // mushroom drops only — the species this cap is
    MeshRenderer _renderer;
    Vector3 _baseScale;
    Transform _body;             // celestial body we're parented to (may be null)
    Vector3 _restLocal;          // resting position, body-local
    Vector3 _upLocal;            // surface normal, body-local
    Quaternion _facingLocal;     // base orientation the spin rotates around
    Vector3 _popStartLocal;      // where the toss arc begins, body-local
    float _age;
    float _spin;
    float _bobPhase;
    float _nextFullRetry;
    bool _collected;

    static PlayerController s_player;
    static float s_nextPlayerSearch;

    void Init(Hotbar.ItemId id, int amount, Sprite icon, MeshRenderer renderer, Vector3 worldPos, Transform bodyParent)
    {
        _id = id;
        _amount = amount;
        _renderer = renderer;

        _body = bodyParent;
        if (_body != null) transform.SetParent(_body, worldPositionStays: false);

        // Surface normal: away from the planet centre when we know the body,
        // otherwise fall back to the local gravity-free world up.
        Vector3 up = _body != null
            ? (worldPos - _body.position).normalized
            : Vector3.up;
        if (up.sqrMagnitude < 0.0001f) up = Vector3.up;

        // One raycast, at spawn only, to find the ground under the stump. The
        // WorldProp layer is excluded so the tree we just felled (and its
        // neighbours) can't be mistaken for terrain.
        int mask = ~SpawnerCubeface.WorldPropLayerMask;
        Vector3 restWorld = worldPos + up * kHoverHeight;
        if (Physics.Raycast(worldPos + up * 2f, -up, out RaycastHit hit, 8f, mask, QueryTriggerInteraction.Ignore))
            restWorld = hit.point + up * kHoverHeight;

        // Scatter: a random tangent offset so a cluster doesn't stack into one sprite.
        Vector3 tangent = Vector3.Cross(up, Vector3.right);
        if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(up, Vector3.forward);
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(up, tangent);
        float ang = Random.value * Mathf.PI * 2f;
        float rad = Random.Range(0.25f, 1f) * kPopSpread;
        restWorld += (tangent * Mathf.Cos(ang) + bitangent * Mathf.Sin(ang)) * rad;

        _restLocal      = ToLocalPoint(restWorld);
        _popStartLocal  = ToLocalPoint(worldPos + up * 0.4f);
        _upLocal        = ToLocalDir(up);
        _facingLocal    = Quaternion.LookRotation(ToLocalDir(tangent), _upLocal);
        _bobPhase       = Random.value * Mathf.PI * 2f;
        _spin           = Random.value * 360f;

        // Normalise the sprite to a fixed world size regardless of its import
        // PPU, and divide out any scale on the celestial body we parent to.
        // Mushroom drops arrive with no icon — MushroomRegistry.BuildModel has
        // already sized the real 3D model, so just divide out the body scale.
        float parentScale = _body != null ? Mathf.Max(0.0001f, _body.lossyScale.x) : 1f;
        if (icon != null)
        {
            Vector3 size = icon.bounds.size;
            float longest = Mathf.Max(0.0001f, Mathf.Max(size.x, size.y));
            _baseScale = Vector3.one * (kSpriteSize / longest / parentScale);
        }
        else
        {
            _baseScale = transform.localScale / parentScale;
        }
        transform.localScale = _baseScale;

        transform.localPosition = _popStartLocal;
        transform.localRotation = _facingLocal;
    }

    Vector3 ToLocalPoint(Vector3 world) => _body != null ? _body.InverseTransformPoint(world) : world;
    Vector3 ToLocalDir(Vector3 world)   => _body != null ? _body.InverseTransformDirection(world) : world;

    void Update()
    {
        if (_collected) return;
        float dt = Time.deltaTime;
        _age += dt;

        // --- pose: toss arc, then hover + bob ---
        _spin += kSpinSpeed * dt;
        _bobPhase += kBobSpeed * dt;

        Vector3 basePos;
        if (_age < kPopDuration)
        {
            float u = _age / kPopDuration;
            basePos = Vector3.Lerp(_popStartLocal, _restLocal, u)
                    + _upLocal * (Mathf.Sin(u * Mathf.PI) * kPopUp);
        }
        else
        {
            basePos = _restLocal + _upLocal * (Mathf.Sin(_bobPhase) * kBobAmplitude);
        }

        // --- pickup: walk over it ---
        var player = ResolvePlayer();
        if (player != null && _age >= kPickupDelay)
        {
            Vector3 selfWorld = _body != null ? _body.TransformPoint(basePos) : basePos;
            if (Vector3.Distance(selfWorld, player.transform.position) <= kCollectRadius)
            {
                TryCollect();
                if (_collected) return;
            }
        }

        // --- lifetime ---
        if (_age >= kLifetime) { Destroy(gameObject); return; }
        // Despawn tell: shrink away rather than fade. The cutout material has no
        // usable alpha channel to animate (that's the trade for correct depth
        // sorting on a solid mesh), and shrinking reads just as well.
        float fadeStart = kLifetime - kFadeDuration;
        float despawnScale = _age > fadeStart
            ? 1f - (_age - fadeStart) / kFadeDuration
            : 1f;

        transform.localPosition = basePos;
        transform.localRotation = Quaternion.AngleAxis(_spin, _upLocal) * _facingLocal;
        transform.localScale = _baseScale * despawnScale;
    }

    void TryCollect()
    {
        if (Hotbar.Instance == null) return;
        if (Time.time < _nextFullRetry) return;

        int leftover = Hotbar.Instance.AddResource(_id, _amount, _species);
        int taken = _amount - leftover;
        if (taken > 0) ShowPopup(_id, taken, transform.position, _species);

        if (leftover <= 0)
        {
            _collected = true;
            Destroy(gameObject);
            return;
        }

        // Inventory full — keep whatever didn't fit lying on the ground and
        // back off before trying again so the "full" popup can't spam.
        _amount = leftover;
        if (taken <= 0) InventoryFullPopup.Show();
        _nextFullRetry = Time.time + kFullRetryDelay;
    }

    static void ShowPopup(Hotbar.ItemId id, int amount, Vector3 worldPos, string species = null)
    {
        if (amount <= 0) return;
        if (id == Hotbar.ItemId.Wood) WoodPopup.Spawn(worldPos, amount);
        else if (id == Hotbar.ItemId.Crystal) CrystalPopup.Spawn(worldPos, amount);
        else if (Hotbar.IsMushroomItem(id))
            MushroomPopup.Spawn(worldPos, amount, species, id == Hotbar.ItemId.MushroomSapling);
    }

    // Cached player lookup, re-searched at most once a second while missing
    // (never per-frame FindObjectOfType — see CLAUDE.md).
    static PlayerController ResolvePlayer()
    {
        if (s_player != null) return s_player;
        if (Time.time < s_nextPlayerSearch) return null;
        s_nextPlayerSearch = Time.time + 1f;
        s_player = FindObjectOfType<PlayerController>();
        return s_player;
    }
}
