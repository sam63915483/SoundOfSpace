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
        public ObservationTracker tracker = new ObservationTracker();
        public float alpha = 1f;
        public float unobservedSince = -1f;  // Time.time when sight was lost; -1 while observed
    }

    Stone[] _stones;
    Transform _root;
    Vector3 _respawnPoint;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        _root = transform;
        var carpetMat = DimensionSceneUtil.Mat(new Color(0.34f, 0.10f, 0.10f), 0.05f);
        var wallMat   = DimensionSceneUtil.Mat(new Color(0.55f, 0.48f, 0.38f), 0.1f);
        var ceilMat   = DimensionSceneUtil.Mat(new Color(0.30f, 0.28f, 0.26f), 0.1f);
        var lampMat   = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.85f, 0.6f), 1.6f);
        var glassMat  = DimensionSceneUtil.FadeMat(new Color(0.7f, 0.85f, 1f, 0.35f));

        // Two corridor sections floating over the void, chasm between them.
        BuildCorridor(8f, carpetMat, wallMat, ceilMat, lampMat);    // start: z -2..18
        BuildCorridor(48f, carpetMat, wallMat, ceilMat, lampMat);   // end:   z 38..58
        _respawnPoint = new Vector3(0f, 1.5f, 4f);

        // Five glass stepping stones across the chasm (z 18..38), solid only while seen.
        _stones = new Stone[5];
        for (int i = 0; i < 5; i++)
        {
            float z = 20f + i * 4f;
            var go = DimensionSceneUtil.Block(PrimitiveType.Cube, "GlassStone" + i,
                new Vector3(0f, -0.06f, z), new Vector3(1.8f, 0.12f, 1.8f), glassMat, _root);
            _stones[i] = new Stone
            {
                tf = go.transform,
                rend = go.GetComponent<Renderer>(),
                col = go.GetComponent<Collider>(),
                mpb = new MaterialPropertyBlock(),
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
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostL",  new Vector3(-0.8f, 1.5f, 57f), new Vector3(0.3f, 3f, 0.3f), frame, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostR",  new Vector3( 0.8f, 1.5f, 57f), new Vector3(0.3f, 3f, 0.3f), frame, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Lintel", new Vector3(0f, 3.05f, 57f),   new Vector3(1.9f, 0.3f, 0.3f), frame, _root);
        var pane = DimensionSceneUtil.Block(PrimitiveType.Cube, "Glow", new Vector3(0f, 1.5f, 57f), new Vector3(1.3f, 2.9f, 0.05f), glow, _root);
        Destroy(pane.GetComponent<Collider>());
        DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1.5f, 57f),
            new Vector3(1.3f, 2.9f, 0.6f), LevelPortal.PortalAction.EnterInterior, nextScene, _root);

        // Kill volume far below — the drop is the punishment, respawn is the mercy.
        var kill = new GameObject("KillVolume");
        kill.transform.SetParent(_root, false);
        kill.transform.position = new Vector3(0f, -60f, 28f);
        var kb = kill.AddComponent<BoxCollider>();
        kb.isTrigger = true; kb.size = new Vector3(300f, 10f, 300f);
        kill.AddComponent<LongDarkKillVolume>().owner = this;

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(55f, 2f, 0.08f), 500f, 1f);
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
        foreach (var s in _stones)
        {
            var b = new Bounds(s.tf.position, new Vector3(2.2f, 1.2f, 2.2f));
            bool observed = s.tracker.Tick(b, out _, float.PositiveInfinity);

            if (!observed && s.unobservedSince < 0f) s.unobservedSince = Time.time;
            if (observed) s.unobservedSince = -1f;

            float targetAlpha = observed ? 0.35f : 0.02f;
            s.alpha = Mathf.MoveTowards(s.alpha, targetAlpha, Time.deltaTime / 0.2f);
            s.mpb.SetColor(ColorId, new Color(0.7f, 0.85f, 1f, s.alpha));
            s.rend.SetPropertyBlock(s.mpb);

            // Collider survives a brief blink, then drops — sustained looking-away kills it.
            s.col.enabled = observed || Time.time - s.unobservedSince <= colliderDropDelay;
        }
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
    public float colliderDropDelay = 0.25f;

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
