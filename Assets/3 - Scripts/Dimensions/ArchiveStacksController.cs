using UnityEngine;

/// <summary>
/// D21 "The Archive Stacks": the inside of a vast library tower, shelves rising out
/// of candlelight into the dark. A helix of ledge-slots spirals up the wall; only a
/// few ledges exist, hopping between slots when unseen (SliverTileSet — the D3 rule).
/// Whispering grows louder as you face the exit door at the top (gaze-audio).
/// Falling just puts you back on the floor. Climb with your eyes open.
/// </summary>
public class ArchiveStacksController : MonoBehaviour
{
    SliverTileSet _ledges;
    Transform _exitDoor;
    AudioSource _whisper;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    const float TowerRadius = 11f;
    const int SlotCount = 14;

    static Vector3 SlotPos(int i)
    {
        float ang = i * 24f * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(ang) * 8f, 1.6f + i * 1.35f, Mathf.Sin(ang) * 8f);
    }

    void Awake()
    {
        var root = transform;
        var floorMat = DimensionSceneUtil.Mat(new Color(0.20f, 0.15f, 0.10f), 0.2f);
        var shelfMat = DimensionSceneUtil.Mat(new Color(0.16f, 0.11f, 0.07f), 0.1f);
        var bookMat  = DimensionSceneUtil.Mat(new Color(0.35f, 0.22f, 0.14f), 0.15f);
        var ledgeMat = DimensionSceneUtil.FadeMat(new Color(0.55f, 0.40f, 0.24f, 0.95f));

        // Cylinder visual, but its capsule collider can't flatten (it becomes a huge
        // sphere bulging 12m up through the tower) — swap in a flat box collider.
        var floor = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Floor",
            new Vector3(0f, -0.5f, 0f), new Vector3(TowerRadius * 2.3f, 0.5f, TowerRadius * 2.3f), floorMat, root);
        Object.Destroy(floor.GetComponent<Collider>());
        floor.AddComponent<BoxCollider>();

        // Tower wall: a ring of shelf slabs with book-row strips, three storeys tall.
        int segs = 14;
        for (int s = 0; s < segs; s++)
        {
            float a = s * Mathf.PI * 2f / segs;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var slab = DimensionSceneUtil.Block(PrimitiveType.Cube, "Stack",
                dir * TowerRadius + Vector3.up * 13f, new Vector3(5.2f, 26f, 1.4f), shelfMat, root);
            slab.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            for (int row = 0; row < 8; row++)
            {
                var books = DimensionSceneUtil.Block(PrimitiveType.Cube, "BookRow",
                    Vector3.zero, new Vector3(4.6f, 0.9f, 0.35f), bookMat, slab.transform);
                books.transform.localPosition = new Vector3(0f, -0.44f + row * 0.125f, -0.55f);
                books.transform.localScale = new Vector3(4.6f / 5.2f, 0.9f / 26f, 0.35f / 1.4f);
                Object.Destroy(books.GetComponent<Collider>());
            }
        }

        // Candles around the floor.
        for (int k = 0; k < 6; k++)
        {
            float a = k * Mathf.PI / 3f;
            var lg = new GameObject("Candle");
            lg.transform.SetParent(root, false);
            lg.transform.position = new Vector3(Mathf.Cos(a) * 6.5f, 1.4f, Mathf.Sin(a) * 6.5f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point; l.range = 12f; l.intensity = 1.2f;
            l.color = new Color(1f, 0.75f, 0.45f);
            if (k % 2 == 0) lg.AddComponent<FlickerLight>();
        }

        // The helix ledges.
        var slots = new Vector3[SlotCount];
        for (int i = 0; i < SlotCount; i++) slots[i] = SlotPos(i);
        var tiles = new GameObject[ledgeCount];
        for (int i = 0; i < tiles.Length; i++)
            tiles[i] = DimensionSceneUtil.Block(PrimitiveType.Cube, "Ledge" + i,
                slots[i], new Vector3(2.6f, 0.25f, 2.6f), ledgeMat, root);
        _ledges = new SliverTileSet(slots, tiles, new Vector3(2.6f, 0.3f, 2.6f),
            Vector3.up, new Color(0.55f, 0.40f, 0.24f, 0.95f), colliderDropDelay);

        // Exit door high on the wall at the top of the helix.
        Vector3 top = SlotPos(SlotCount - 1);
        Vector3 doorDir = new Vector3(top.x, 0f, top.z).normalized;
        Vector3 doorPos = doorDir * (TowerRadius - 1.2f) + Vector3.up * (top.y + 0.4f);
        var frame = DimensionSceneUtil.Mat(new Color(0.30f, 0.20f, 0.10f));
        var pane  = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.85f, 0.55f), 2.4f);
        var door = new GameObject("ExitDoor");
        door.transform.SetParent(root, false);
        _exitDoor = door.transform;
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostL",  new Vector3(-0.8f, 1.5f, 0f), new Vector3(0.3f, 3f, 0.3f), frame, door.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostR",  new Vector3( 0.8f, 1.5f, 0f), new Vector3(0.3f, 3f, 0.3f), frame, door.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Lintel", new Vector3(0f, 3.05f, 0f),   new Vector3(1.9f, 0.3f, 0.3f), frame, door.transform);
        var glow = DimensionSceneUtil.Block(PrimitiveType.Cube, "Glow", new Vector3(0f, 1.5f, 0f), new Vector3(1.3f, 2.9f, 0.05f), pane, door.transform);
        Object.Destroy(glow.GetComponent<Collider>());
        // A landing ledge in front of the door (always solid).
        DimensionSceneUtil.Block(PrimitiveType.Cube, "DoorLanding",
            Vector3.zero, new Vector3(3f, 0.3f, 3f), DimensionSceneUtil.Mat(new Color(0.3f, 0.22f, 0.12f)), door.transform)
            .transform.localPosition = new Vector3(0f, -0.15f, -1.4f);
        DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1.5f, 0f),
            new Vector3(1.3f, 2.9f, 0.8f), LevelPortal.PortalAction.EnterInterior, nextScene, door.transform);
        door.transform.SetPositionAndRotation(doorPos, Quaternion.LookRotation(-doorDir, Vector3.up));

        // Whispers: hiss shaped by gaze toward the door.
        _whisper = DimensionSceneUtil.LoopingAudio(door, WhisperClip(), 200f, 1f);

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(82f, 3f, 0.04f), 300f, 1f);
    }

    // Filtered-ish noise: random walk instead of raw noise reads as breathy whispering.
    static AudioClip WhisperClip()
    {
        int rate = 44100;
        float seconds = 2.4f;
        int samples = (int)(rate * seconds);
        var data = new float[samples];
        var rng = new System.Random(2121);
        float v = 0f;
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            v = Mathf.Lerp(v, (float)(rng.NextDouble() * 2.0 - 1.0), 0.16f);
            float breath = 0.55f + 0.45f * Mathf.Sin(t * 2.6f + Mathf.Sin(t * 7.3f));
            data[i] = v * breath * 0.5f;
        }
        var clip = AudioClip.Create("whisper", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.14f, 0.11f, 0.08f),
                fog: new Color(0.05f, 0.04f, 0.03f), fogDensity: 0.020f,
                background: new Color(0.02f, 0.015f, 0.01f));
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
        _ledges.Tick(playerPos);

        var cam = ObserverState.Cam;
        if (_whisper != null && cam != null)
        {
            Vector3 to = _exitDoor.position - cam.transform.position;
            float align = Vector3.Dot(cam.transform.forward, to.normalized);
            _whisper.volume = Mathf.Lerp(0.05f, 1f, Mathf.InverseLerp(0.1f, 0.95f, align));
        }
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Ledges")]
    [Tooltip("How many ledges exist among the 14 helix slots.")]
    public int ledgeCount = 5;
    [Tooltip("Seconds a ledge stays solid after sight is lost (blink forgiveness).")]
    public float colliderDropDelay = 0.35f;

    [Header("Exit")]
    [Tooltip("Scene the top door leads to.")]
    public string nextScene = "D22_RustSea";
}
