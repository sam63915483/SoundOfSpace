using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// D3 "Long Dark" (v2): an abandoned hotel corridor split by a sheer drop into blackness.
/// Five GLASS stepping stones bridge the gap — solid only while OBSERVED. Look up or
/// away and the stone under you dissolves. Cross by keeping your eyes down the whole way.
/// Falling respawns you at the corridor start.
/// </summary>
public class LongDarkController : MonoBehaviour
{
    class Stone
    {
        public Transform tf;
        public Renderer rend;
        public Collider col;
        public MaterialPropertyBlock mpb;
        public ObservationTracker tracker = new ObservationTracker(0.25f);  // grace — brief peripheral flicks forgiven
        public float alpha = 1f;
        public float unobservedSince = -1f;  // Time.time when sight was lost; -1 while observed
        public bool rearrangedSinceSeen;     // one rearrange per look-away, but never lost (no stranding)
        public int slotIndex;                // which of the 5 fixed bridge slots this tile occupies
    }

    // The bridge is 5 FIXED slots in a straight line — only 2 tiles exist to fill them.
    const int SlotCount = 5;
    static float SlotZ(int i) => 20.4f + i * 5.5f;
    static Vector3 SlotPos(int i) => new Vector3(0f, -0.06f, SlotZ(i));
    static Bounds SlotBounds(int i) => new Bounds(SlotPos(i), new Vector3(3f, 1f, 3f));

    Stone[] _stones;
    Transform _root;
    Vector3 _respawnPoint;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    // Hotel dressing (polish pass): doors the muffled TV can hide behind, and
    // room-service trays that slide around while the corridor is unobserved.
    readonly List<Vector3> _doorSpots = new List<Vector3>();
    GameObject _tvGo;
    int _tvSpot;
    readonly ObservationTracker _tvTracker = new ObservationTracker();
    // Rearrange scratch (hoisted — was a per-call alloc, flagged by the sweep).
    readonly List<int> _candScratch = new List<int>();
    readonly List<int> _hiddenScratch = new List<int>();

    /// <summary>Library texture material, or the pre-polish flat color if missing.</summary>
    static Material TexOr(string key, Color fallback, Vector2 tiling, float smooth = 0.1f, Color? tint = null)
        => DimensionAssetLibrary.Tex(key) != null
            ? DimensionSceneUtil.TexMat(key, tint ?? Color.white, tiling, smooth)
            : DimensionSceneUtil.Mat(fallback, smooth);

    void Awake()
    {
        _root = transform;
        var carpetMat = TexOr("d3_carpet", new Color(0.34f, 0.10f, 0.10f), new Vector2(2f, 10f), 0.05f);
        var wallMat   = TexOr("d3_wall", new Color(0.55f, 0.48f, 0.38f), new Vector2(10f, 2f), 0.1f);
        var ceilMat   = DimensionSceneUtil.Mat(new Color(0.30f, 0.28f, 0.26f), 0.1f);
        var lampMat   = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.85f, 0.6f), 1.6f);
        var glassMat  = DimensionSceneUtil.FadeMat(new Color(0.7f, 0.85f, 1f, 0.35f));

        // Two corridor sections floating over the void, chasm between them.
        BuildCorridor(8f, carpetMat, wallMat, ceilMat, lampMat);    // start: z -2..18
        BuildCorridor(55f, carpetMat, wallMat, ceilMat, lampMat);   // end:   z 45..65
        BuildHotelDressing(8f);
        BuildHotelDressing(55f);
        _respawnPoint = new Vector3(0f, 1.5f, 4f);

        // One door hides a muffled TV set — it moves to another door while the
        // corridor is unobserved. Skipped entirely if the clip isn't generated yet.
        var tvClip = DimensionAssetLibrary.Clip("sfx_tv_muffled");
        if (tvClip != null && _doorSpots.Count > 1)
        {
            _tvGo = new GameObject("MuffledTV");
            _tvGo.transform.SetParent(_root, false);
            _tvSpot = Random.Range(0, _doorSpots.Count);
            _tvGo.transform.position = _doorSpots[_tvSpot] + Vector3.up * 1.2f;
            DimensionSceneUtil.LoopingAudio(_tvGo, tvClip, 12f, 0.5f);
        }

        // The classic 5-slot straight bridge — but only TWO tiles exist to fill the
        // slots. Tiles are solid while seen; a tile that leaves your view (and isn't
        // underfoot) jumps to a different empty slot. Look away and back until the
        // occupied pair gives you a jump, cross carefully, repeat.
        _stones = new Stone[stoneCount];
        for (int i = 0; i < stoneCount; i++)
        {
            var go = DimensionSceneUtil.Block(PrimitiveType.Cube, "GlassStone" + i,
                SlotPos(i), new Vector3(2.6f, 0.12f, 2.6f), glassMat, _root);
            _stones[i] = new Stone
            {
                tf = go.transform,
                rend = go.GetComponent<Renderer>(),
                col = go.GetComponent<Collider>(),
                mpb = new MaterialPropertyBlock(),
                slotIndex = i,
            };
        }

        // Warning sign on the wall at the chasm edge.
        var sign = DimensionSceneUtil.Block(PrimitiveType.Cube, "Sign",
            new Vector3(1.9f, 2f, 17f), new Vector3(0.1f, 0.9f, 1.4f),
            DimensionSceneUtil.Mat(new Color(0.15f, 0.15f, 0.18f)), _root);
        var textGo = new GameObject("SignText");
        textGo.transform.SetParent(sign.transform, false);
        textGo.transform.localPosition = new Vector3(-0.51f, 0f, 0f);
        textGo.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        textGo.transform.localScale = new Vector3(0.035f, 0.055f, 0.5f);
        var tmp = textGo.AddComponent<TMPro.TextMeshPro>();
        tmp.text = "STEP ONLY\nWHERE YOU LOOK";
        tmp.fontSize = 48f;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = new Color(0.95f, 0.9f, 0.8f);
        tmp.GetComponent<RectTransform>().sizeDelta = new Vector2(30f, 16f);

        // Exit door at the far end of the second corridor.
        var frame = DimensionSceneUtil.Mat(new Color(0.1f, 0.1f, 0.12f));
        var glow  = DimensionSceneUtil.EmissiveMat(new Color(0.75f, 0.95f, 1f), 2.5f);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostL",  new Vector3(-0.8f, 1.5f, 64f), new Vector3(0.3f, 3f, 0.3f), frame, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostR",  new Vector3( 0.8f, 1.5f, 64f), new Vector3(0.3f, 3f, 0.3f), frame, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Lintel", new Vector3(0f, 3.05f, 64f),   new Vector3(1.9f, 0.3f, 0.3f), frame, _root);
        var pane = DimensionSceneUtil.Block(PrimitiveType.Cube, "Glow", new Vector3(0f, 1.5f, 64f), new Vector3(1.3f, 2.9f, 0.05f), glow, _root);
        Destroy(pane.GetComponent<Collider>());
        DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1.5f, 64f),
            new Vector3(1.3f, 2.9f, 0.6f), LevelPortal.PortalAction.EnterInterior, nextScene, _root);

        // Kill volume far below — the drop is the punishment, respawn is the mercy.
        var kill = new GameObject("KillVolume");
        kill.transform.SetParent(_root, false);
        kill.transform.position = new Vector3(0f, -60f, 31f);
        var kb = kill.AddComponent<BoxCollider>();
        kb.isTrigger = true; kb.size = new Vector3(300f, 10f, 300f);
        kill.AddComponent<LongDarkKillVolume>().owner = this;

        DimensionSceneUtil.AmbienceLoop2D(gameObject, "amb_d3", 55f, 0.08f, 0.55f);
    }

    // Hotel dressing for one corridor section: numbered guest doors, unlit brass
    // sconces (pure emissive — the lamp lights already carry the corridor), and a
    // PropShuffleSet of room-service trays that slide around while unobserved.
    void BuildHotelDressing(float zCenter)
    {
        var doorMat   = TexOr("d3_door", new Color(0.30f, 0.20f, 0.12f), Vector2.one, 0.25f);
        var frameMat  = DimensionSceneUtil.Mat(new Color(0.16f, 0.11f, 0.07f), 0.2f);
        var plateMat  = DimensionSceneUtil.EmissiveMat(new Color(0.85f, 0.78f, 0.55f), 0.35f);
        var sconceMat = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.8f, 0.5f), 1.1f);
        var trayMat   = DimensionSceneUtil.Mat(new Color(0.75f, 0.72f, 0.68f), 0.5f);
        var chinaMat  = DimensionSceneUtil.Mat(new Color(0.9f, 0.88f, 0.84f), 0.6f);

        var set = PropShuffleSet.Create("TraySet_" + (int)zCenter, _root,
            new Bounds(new Vector3(0f, 1.8f, zCenter), new Vector3(4.6f, 4f, 20f)),
            "sfx_wood_creak");

        for (int side = -1; side <= 1; side += 2)
            for (int d = 0; d < 3; d++)
            {
                float z = zCenter + (-7f + d * 5.5f) + (side > 0 ? 2.5f : 0f);
                BuildDoor(side, z, doorMat, frameMat, plateMat, sconceMat);
                _doorSpots.Add(new Vector3(side * 1.9f, 0f, z));
                set.AddAnchor(new Vector3(side * 1.3f, 0f, z + Random.Range(-0.6f, 0.6f)),
                    Random.Range(0f, 360f));
            }
        set.AddAnchor(new Vector3(0.9f, 0f, zCenter - 4f), 20f);
        set.AddAnchor(new Vector3(-0.9f, 0f, zCenter + 6f), 200f);
        for (int i = 0; i < 3; i++) set.AddProp(BuildTray(set.transform, trayMat, chinaMat));
    }

    // One guest door flush against a corridor wall (side -1 = left/-x, +1 = right/+x).
    void BuildDoor(int side, float z, Material door, Material frame, Material plate, Material sconce)
    {
        float x = side * 1.95f;
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Door", new Vector3(x, 1.05f, z),
            new Vector3(0.1f, 2.1f, 1.0f), door, _root);
        foreach (float dz in new[] { -0.56f, 0.56f })
        {
            var post = DimensionSceneUtil.Block(PrimitiveType.Cube, "DoorPost",
                new Vector3(side * 1.97f, 1.15f, z + dz), new Vector3(0.12f, 2.3f, 0.12f), frame, _root);
            Destroy(post.GetComponent<Collider>());
        }
        var lintel = DimensionSceneUtil.Block(PrimitiveType.Cube, "DoorLintel",
            new Vector3(side * 1.97f, 2.24f, z), new Vector3(0.12f, 0.12f, 1.24f), frame, _root);
        Destroy(lintel.GetComponent<Collider>());
        var numPlate = DimensionSceneUtil.Block(PrimitiveType.Cube, "RoomPlate",
            new Vector3(side * 1.88f, 1.7f, z), new Vector3(0.04f, 0.12f, 0.18f), plate, _root);
        Destroy(numPlate.GetComponent<Collider>());
        var sc = DimensionSceneUtil.Block(PrimitiveType.Cube, "Sconce",
            new Vector3(side * 1.95f, 2.55f, z + 1.5f), new Vector3(0.09f, 0.26f, 0.13f), sconce, _root);
        Destroy(sc.GetComponent<Collider>());
    }

    // Abandoned room-service tray: base + plate + cup, parts LOCAL so the shuffle
    // set can place/rotate the root freely.
    static GameObject BuildTray(Transform parent, Material trayMat, Material chinaMat)
    {
        var root = new GameObject("ServiceTray");
        root.transform.SetParent(parent, false);
        LocalPart(PrimitiveType.Cube, "Tray", root.transform, new Vector3(0f, 0.025f, 0f),
            new Vector3(0.5f, 0.045f, 0.38f), trayMat, true);
        LocalPart(PrimitiveType.Cylinder, "Plate", root.transform, new Vector3(-0.08f, 0.055f, 0f),
            new Vector3(0.17f, 0.008f, 0.17f), chinaMat, false);
        LocalPart(PrimitiveType.Cylinder, "Cup", root.transform, new Vector3(0.15f, 0.08f, 0.09f),
            new Vector3(0.07f, 0.045f, 0.07f), chinaMat, false);
        // Dome cover — uniform sphere, collider dropped, so no capsule ballooning.
        LocalPart(PrimitiveType.Sphere, "Dome", root.transform, new Vector3(-0.08f, 0.06f, 0f),
            new Vector3(0.16f, 0.16f, 0.16f), chinaMat, false);
        return root;
    }

    static GameObject LocalPart(PrimitiveType type, string name, Transform parent,
        Vector3 localPos, Vector3 scale, Material mat, bool collider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.layer = DimensionSceneUtil.WalkableLayer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider) Destroy(go.GetComponent<Collider>());
        return go;
    }

    // One 20m hotel corridor section centred at (0, ·, zCenter): carpet, walls, ceiling, lamps.
    void BuildCorridor(float zCenter, Material carpet, Material wall, Material ceil, Material lamp)
    {
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Carpet", new Vector3(0f, -0.15f, zCenter), new Vector3(4f, 0.3f, 20f), carpet, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "WallL", new Vector3(-2.15f, 1.75f, zCenter), new Vector3(0.3f, 3.8f, 20f), wall, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "WallR", new Vector3( 2.15f, 1.75f, zCenter), new Vector3(0.3f, 3.8f, 20f), wall, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Ceil",  new Vector3(0f, 3.8f, zCenter), new Vector3(4.6f, 0.3f, 20f), ceil, _root);
        for (int i = -1; i <= 1; i++)
        {
            var l = DimensionSceneUtil.Block(PrimitiveType.Cube, "Lamp",
                new Vector3(0f, 3.6f, zCenter + i * 7f), new Vector3(0.8f, 0.1f, 0.8f), lamp, _root);
            Destroy(l.GetComponent<Collider>());
            var lightGo = new GameObject("LampLight");
            lightGo.transform.SetParent(l.transform, false);
            lightGo.transform.localPosition = Vector3.down * 2f;
            var pl = lightGo.AddComponent<Light>();
            pl.type = LightType.Point; pl.range = 9f; pl.intensity = 1.1f;
            pl.color = new Color(1f, 0.88f, 0.65f);
            if (i == 0) lightGo.AddComponent<FlickerLight>();
        }
    }

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.16f, 0.14f, 0.12f),
                fog: new Color(0.015f, 0.015f, 0.025f), fogDensity: 0.014f,
                background: new Color(0.008f, 0.008f, 0.015f));
            _atmosApplied = true;
        }

        // Stones exist only while OBSERVED: keep your eyes on them or fall.
        // Deliberately test only a SMALL core at the stone's centre, not its full box —
        // the box of the stone you're standing on clips the bottom of the frustum even
        // when you look up, which re-enabled the collider mid-fall and bounced you
        // (fall-two-inches-get-pushed-up oscillation). Small core = you must actually
        // be looking AT the stone.
        Vector3 playerPos = _player != null && _player.Rigidbody != null
            ? _player.Rigidbody.position
            : (ObserverState.Cam != null ? ObserverState.Cam.transform.position : Vector3.zero);

        foreach (var s in _stones)
        {
            // FULL FOOTPRINT, kept flat: any sliver of the tile on screen counts as
            // observed (a visible corner keeps it solid and un-rearranged). Flat is
            // what matters — a TALL test box pokes up toward the camera and reads
            // "observed" while looking up (the old bounce bug); a thin one can't.
            var b = new Bounds(s.tf.position, new Vector3(2.6f, 0.2f, 2.6f));
            bool observed = s.tracker.Tick(b, out _, float.PositiveInfinity);

            if (!observed && s.unobservedSince < 0f) s.unobservedSince = Time.time;
            if (observed) { s.unobservedSince = -1f; s.rearrangedSinceSeen = false; }

            // Off-screen stones rearrange (once per look-away) — except the one you're
            // standing on/next to, which keeps the drop-out-from-under-you rule.
            // STATE-driven, not edge-driven: a stone whose look-away moment happened
            // while you stood next to it still rearranges once you move on (the old
            // one-shot signal left stones stranded behind you, starving the path).
            Vector3 flat = s.tf.position - playerPos; flat.y = 0f;
            if (!observed && !s.rearrangedSinceSeen && s.unobservedSince > 0f
                && Time.time - s.unobservedSince > 0.35f && flat.magnitude > 1.6f)
            {
                Rearrange(s, playerPos);
                continue;
            }

            float targetAlpha = observed ? 0.35f : 0.02f;
            s.alpha = Mathf.MoveTowards(s.alpha, targetAlpha, Time.deltaTime / 0.2f);
            s.mpb.SetColor(ColorId, new Color(0.7f, 0.85f, 1f, s.alpha));
            s.rend.SetPropertyBlock(s.mpb);

            // Collider survives a brief blink, then drops — sustained looking-away kills it.
            s.col.enabled = observed || Time.time - s.unobservedSince <= colliderDropDelay;
        }

        // The muffled TV moves to a different door while its current door is unobserved.
        if (_tvGo != null)
        {
            var tb = new Bounds(_tvGo.transform.position, new Vector3(2.5f, 3f, 2.5f));
            _tvTracker.Tick(tb, out bool tvLost, 60f);
            if (tvLost && Random.value < 0.4f)
            {
                int next = Random.Range(0, _doorSpots.Count);
                if (next == _tvSpot) next = (next + 1) % _doorSpots.Count;
                _tvSpot = next;
                _tvGo.transform.position = _doorSpots[next] + Vector3.up * 1.2f;
            }
        }
    }

    // Jump to a different EMPTY slot. Slot uniqueness means tiles can never overlap;
    // a 60% bias toward the nearest empty slot ahead of the player keeps the path
    // buildable within a couple of look-aways; hidden slots preferred (no pop-in).
    void Rearrange(Stone s, Vector3 playerPos)
    {
        _candScratch.Clear();
        for (int i = 0; i < SlotCount; i++)
        {
            if (i == s.slotIndex) continue;
            bool occupied = false;
            foreach (var other in _stones)
                if (other != s && other.slotIndex == i) occupied = true;
            if (!occupied) _candScratch.Add(i);
        }
        if (_candScratch.Count == 0) return;

        _hiddenScratch.Clear();
        foreach (int i in _candScratch)
            if (!ObserverState.IsObserved(SlotBounds(i))) _hiddenScratch.Add(i);
        var pool = _hiddenScratch.Count > 0 ? _hiddenScratch : _candScratch;

        int pick = -1;
        if (Random.value < 0.6f)
        {
            float bestZ = float.MaxValue;
            foreach (int i in pool)
                if (SlotZ(i) > playerPos.z + 1f && SlotZ(i) < bestZ) { bestZ = SlotZ(i); pick = i; }
        }
        if (pick < 0) pick = pool[Random.Range(0, pool.Count)];

        s.slotIndex = pick;
        s.tf.position = SlotPos(pick);
        s.tracker.Reset();
        s.unobservedSince = -1f;
        s.rearrangedSinceSeen = true;
        s.alpha = 1f;
        s.col.enabled = true;
    }

    /// <summary>Kill-volume respawn: back to the corridor start, velocity zeroed. No damage.</summary>
    public void RespawnPlayer()
    {
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null) return;
        _player.Rigidbody.position = _respawnPoint;
        _player.Rigidbody.velocity = Vector3.zero;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Stones")]
    [Tooltip("Seconds a stone stays solid after sight is lost (blink forgiveness).")]
    public float colliderDropDelay = 0.3f;
    [Tooltip("How many stones exist at once (they rearrange off-screen).")]
    public int stoneCount = 2;

    [Header("Exit")]
    [Tooltip("Scene the far corridor's door leads to.")]
    public string nextScene = "D4_WaitingField";
}

/// <summary>Void-fall trigger that hands the player back to the corridor start.</summary>
public class LongDarkKillVolume : MonoBehaviour
{
    [HideInInspector] public LongDarkController owner;

    void OnTriggerEnter(Collider other)
    {
        if (owner == null) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;
        owner.RespawnPlayer();
    }
}
