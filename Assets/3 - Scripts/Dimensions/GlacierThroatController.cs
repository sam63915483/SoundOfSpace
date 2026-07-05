using UnityEngine;

/// <summary>
/// D14 "Glacier Throat": an ice canyon corridor split by a deep crevasse glowing blue
/// from below. Ghost-ice bridge tiles are solid only while a sliver is on screen
/// (D3's rule via SliverTileSet); look up or away mid-crossing and the ice under you
/// lets go. Falling drops you into the blue and back to the canyon mouth.
/// </summary>
public class GlacierThroatController : MonoBehaviour
{
    SliverTileSet _bridge;
    Transform _root;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        var iceMat  = DimensionSceneUtil.Mat(new Color(0.72f, 0.84f, 0.95f), 0.6f);
        var deepMat = DimensionSceneUtil.Mat(new Color(0.55f, 0.72f, 0.9f), 0.5f);
        var tileMat = DimensionSceneUtil.FadeMat(new Color(0.7f, 0.92f, 1f, 0.45f));

        // Two floor shelves with the crevasse between them (z 20..50).
        DimensionSceneUtil.Block(PrimitiveType.Cube, "ShelfStart",
            new Vector3(0f, -0.5f, 7.5f), new Vector3(12f, 1f, 25f), iceMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "ShelfEnd",
            new Vector3(0f, -0.5f, 62.5f), new Vector3(12f, 1f, 25f), iceMat, _root);

        // Canyon walls: stacked jittered ice blocks so the throat reads carved, not boxy.
        Random.State prev = Random.state;
        Random.InitState(1414);
        for (int side = -1; side <= 1; side += 2)
            for (float z = -5f; z <= 75f; z += 6f)
            {
                float w = Random.Range(3.5f, 6f);
                DimensionSceneUtil.Block(PrimitiveType.Cube, "WallIce",
                    new Vector3(side * (6f + w * 0.3f), Random.Range(5f, 7f), z),
                    new Vector3(w, Random.Range(12f, 16f), 6.2f), iceMat, _root)
                    .transform.rotation = Quaternion.Euler(Random.Range(-4f, 4f), Random.Range(-6f, 6f), Random.Range(-4f, 4f));
            }
        Random.state = prev;

        // The blue below: emissive floor far down + underlights in the crevasse.
        var glowMat = DimensionSceneUtil.EmissiveMat(new Color(0.15f, 0.6f, 1f), 1.8f);
        var glow = DimensionSceneUtil.Block(PrimitiveType.Cube, "DeepGlow",
            new Vector3(0f, -26f, 35f), new Vector3(12f, 0.5f, 34f), glowMat, _root);
        Destroy(glow.GetComponent<Collider>());
        for (float z = 24f; z <= 46f; z += 11f)
        {
            var lg = new GameObject("CrevasseLight");
            lg.transform.SetParent(_root, false);
            lg.transform.position = new Vector3(0f, -8f, z);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point; l.range = 26f; l.intensity = 2.4f;
            l.color = new Color(0.25f, 0.65f, 1f);
        }
        DimensionSceneUtil.Block(PrimitiveType.Cube, "CrevasseWallA",
            new Vector3(0f, -13f, 19.6f), new Vector3(12f, 26f, 1f), deepMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "CrevasseWallB",
            new Vector3(0f, -13f, 50.4f), new Vector3(12f, 26f, 1f), deepMat, _root);

        // The bridge: 5 fixed slots, 2 ghost-ice tiles.
        var slots = new Vector3[5];
        for (int i = 0; i < 5; i++) slots[i] = new Vector3(0f, -0.06f, 22f + i * 6.4f);
        var tiles = new GameObject[2];
        for (int i = 0; i < tiles.Length; i++)
            tiles[i] = DimensionSceneUtil.Block(PrimitiveType.Cube, "IceTile" + i,
                slots[i], new Vector3(2.6f, 0.12f, 2.6f), tileMat, _root);
        _bridge = new SliverTileSet(slots, tiles, new Vector3(2.6f, 0.2f, 2.6f),
            Vector3.forward, new Color(0.7f, 0.92f, 1f, 0.45f), colliderDropDelay);

        // Fall catcher → canyon mouth.
        var kill = new GameObject("FallVolume");
        kill.transform.SetParent(_root, false);
        kill.transform.position = new Vector3(0f, -20f, 35f);
        var kb = kill.AddComponent<BoxCollider>();
        kb.isTrigger = true; kb.size = new Vector3(60f, 8f, 60f);
        kill.AddComponent<DimensionRespawnVolume>().respawnPoint = new Vector3(0f, 1.5f, 4f);

        // Exit door on the far shelf.
        var frame = DimensionSceneUtil.Mat(new Color(0.1f, 0.12f, 0.16f));
        var pane  = DimensionSceneUtil.EmissiveMat(new Color(0.75f, 0.95f, 1f), 2.5f);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostL",  new Vector3(-0.8f, 1.5f, 73f), new Vector3(0.3f, 3f, 0.3f), frame, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostR",  new Vector3( 0.8f, 1.5f, 73f), new Vector3(0.3f, 3f, 0.3f), frame, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Lintel", new Vector3(0f, 3.05f, 73f),   new Vector3(1.9f, 0.3f, 0.3f), frame, _root);
        var glowPane = DimensionSceneUtil.Block(PrimitiveType.Cube, "Glow", new Vector3(0f, 1.5f, 73f), new Vector3(1.3f, 2.9f, 0.05f), pane, _root);
        Destroy(glowPane.GetComponent<Collider>());
        DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1.5f, 73f),
            new Vector3(1.3f, 2.9f, 0.6f), LevelPortal.PortalAction.EnterInterior, nextScene, _root);

        // Wind: two detuned low tones beat against each other.
        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(46f, 2f, 0.07f), 400f, 1f);
        var wind = new GameObject("Wind");
        wind.transform.SetParent(_root, false);
        wind.transform.position = new Vector3(0f, 8f, 35f);
        DimensionSceneUtil.LoopingAudio(wind, DimensionSceneUtil.ToneClip(49.3f, 2f, 0.05f), 300f, 1f);
    }

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.28f, 0.36f, 0.46f),
                fog: new Color(0.55f, 0.68f, 0.8f), fogDensity: 0.012f,
                background: new Color(0.62f, 0.74f, 0.85f));
            _atmosApplied = true;
        }

        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        Vector3 playerPos = _player != null && _player.Rigidbody != null
            ? _player.Rigidbody.position
            : (ObserverState.Cam != null ? ObserverState.Cam.transform.position : Vector3.zero);
        _bridge.Tick(playerPos);
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Bridge")]
    [Tooltip("Seconds a tile stays solid after sight is lost (blink forgiveness).")]
    public float colliderDropDelay = 0.3f;

    [Header("Exit")]
    [Tooltip("Scene the far door leads to.")]
    public string nextScene = "D15_Congregation";
}
