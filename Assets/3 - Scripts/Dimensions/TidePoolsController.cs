using UnityEngine;

/// <summary>
/// D17 "Tide Pools": moonlit shallows over pale sand. A deep black channel cuts the
/// flats in two; stepping rocks across it are solid only while a sliver is on screen
/// (SliverTileSet). Slipping in washes you back to the near shore. The far shore
/// glows bioluminescent blue-green — that's the way out.
/// </summary>
public class TidePoolsController : MonoBehaviour
{
    SliverTileSet _rocks;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    void Awake()
    {
        var root = transform;
        var sandMat  = DimensionSceneUtil.Mat(new Color(0.55f, 0.52f, 0.42f), 0.15f);
        var waterMat = DimensionSceneUtil.FadeMat(new Color(0.15f, 0.35f, 0.45f, 0.35f));
        var rockMat  = DimensionSceneUtil.Mat(new Color(0.30f, 0.30f, 0.32f), 0.2f);

        // Two sand shelves, deep channel between (z 18..48).
        DimensionSceneUtil.Block(PrimitiveType.Cube, "SandNear",
            new Vector3(0f, -0.5f, -41f), new Vector3(240f, 1f, 118f), sandMat, root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "SandFar",
            new Vector3(0f, -0.5f, 107f), new Vector3(240f, 1f, 118f), sandMat, root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "ChannelFloor",
            new Vector3(0f, -9f, 33f), new Vector3(240f, 1f, 30f), DimensionSceneUtil.Mat(new Color(0.03f, 0.05f, 0.07f)), root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "ChannelWallNear",
            new Vector3(0f, -4.5f, 17.5f), new Vector3(240f, 8f, 1f), sandMat, root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "ChannelWallFar",
            new Vector3(0f, -4.5f, 48.5f), new Vector3(240f, 8f, 1f), sandMat, root);

        // The shallow water sheet — visual only, ankle deep.
        var water = DimensionSceneUtil.Block(PrimitiveType.Cube, "Water",
            new Vector3(0f, 0.22f, 33f), new Vector3(240f, 0.05f, 260f), waterMat, root);
        Object.Destroy(water.GetComponent<Collider>());

        // Moon + cold light.
        var moonMat = DimensionSceneUtil.EmissiveMat(new Color(0.95f, 0.97f, 1f), 2.2f);
        var moon = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Moon",
            new Vector3(70f, 150f, 260f), Vector3.one * 22f, moonMat, root);
        Object.Destroy(moon.GetComponent<Collider>());
        DimensionSceneUtil.CreateDirectionalLight(new Color(0.6f, 0.7f, 0.9f), 0.6f, new Vector3(38f, -155f, 0f), true);

        // Stepping rocks: 5 zig-zag slots over the channel, 3 real rocks.
        var slots = new Vector3[5];
        for (int i = 0; i < 5; i++)
            slots[i] = new Vector3(i % 2 == 0 ? -2.2f : 2.2f, 0.3f, 20.5f + i * 6.2f);
        var tiles = new GameObject[3];
        for (int i = 0; i < tiles.Length; i++)
            tiles[i] = DimensionSceneUtil.Block(PrimitiveType.Cube, "StepRock" + i,
                slots[i], new Vector3(2.4f, 0.55f, 2.4f), rockMat, root);
        _rocks = new SliverTileSet(slots, tiles, new Vector3(2.4f, 0.3f, 2.4f),
            Vector3.forward, new Color(0.30f, 0.30f, 0.32f, 1f), colliderDropDelay);

        // Slip catcher in the channel → back to the near shore.
        var wash = new GameObject("WashVolume");
        wash.transform.SetParent(root, false);
        wash.transform.position = new Vector3(0f, -6f, 33f);
        var wb = wash.AddComponent<BoxCollider>();
        wb.isTrigger = true; wb.size = new Vector3(240f, 5f, 29f);
        wash.AddComponent<DimensionRespawnVolume>().respawnPoint = new Vector3(0f, 1.5f, 6f);

        // Bioluminescent far shore: glowing tide pools around the exit stones.
        var bioMat = DimensionSceneUtil.EmissiveMat(new Color(0.2f, 1f, 0.75f), 2.4f);
        Random.State prev = Random.state;
        Random.InitState(1717);
        for (int i = 0; i < 14; i++)
        {
            var blob = DimensionSceneUtil.Block(PrimitiveType.Sphere, "BioPool",
                new Vector3(Random.Range(-16f, 16f), 0.05f, Random.Range(56f, 76f)),
                new Vector3(Random.Range(1f, 2.6f), 0.15f, Random.Range(1f, 2.6f)), bioMat, root);
            Object.Destroy(blob.GetComponent<Collider>());
        }
        Random.state = prev;
        var lg = new GameObject("BioLight");
        lg.transform.SetParent(root, false);
        lg.transform.position = new Vector3(0f, 2f, 66f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = 30f; l.intensity = 2f;
        l.color = new Color(0.25f, 1f, 0.75f);

        // Exit: a ring of standing stones on the glowing shore.
        var stoneMat2 = DimensionSceneUtil.Mat(new Color(0.2f, 0.22f, 0.24f), 0.1f);
        for (int k = 0; k < 6; k++)
        {
            float a = k * Mathf.PI / 3f;
            DimensionSceneUtil.Block(PrimitiveType.Cube, "Standing",
                new Vector3(Mathf.Cos(a) * 3.4f, 1.4f, 66f + Mathf.Sin(a) * 3.4f),
                new Vector3(0.9f, 2.8f, 0.7f), stoneMat2, root);
        }
        DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1.2f, 66f),
            new Vector3(2.6f, 2.4f, 2.6f), LevelPortal.PortalAction.EnterInterior, nextScene, root);

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(66f, 3f, 0.05f), 500f, 1f);
    }

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.14f, 0.17f, 0.24f),
                fog: new Color(0.05f, 0.08f, 0.13f), fogDensity: 0.006f,
                background: new Color(0.015f, 0.03f, 0.06f));
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
        _rocks.Tick(playerPos);
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Rocks")]
    [Tooltip("Seconds a rock stays solid after sight is lost (blink forgiveness).")]
    public float colliderDropDelay = 0.3f;

    [Header("Exit")]
    [Tooltip("Scene the standing stones lead to.")]
    public string nextScene = "D18_StaticField";
}
