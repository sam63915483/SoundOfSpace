using UnityEngine;

/// <summary>
/// D3 "Long Dark": a void crossing where the bridge is only solid while UNobserved.
/// Observed segments fade out and drop their collider after a short delay; unobserved
/// segments are instantly solid again. Falling respawns you at the start platform.
/// </summary>
public class LongDarkController : MonoBehaviour
{
    class Segment
    {
        public Transform tf;
        public Renderer rend;
        public Collider col;
        public MaterialPropertyBlock mpb;
        public ObservationTracker tracker = new ObservationTracker();
        public float alpha = 1f;
        public float observedSince = -1f;    // Time.time when it became observed; -1 while unobserved
    }

    Segment[] _segments;
    Transform _root;
    Vector3 _respawnPoint;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        _root = transform;
        var platMat   = DimensionSceneUtil.Mat(new Color(0.15f, 0.15f, 0.2f));
        var bridgeMat = DimensionSceneUtil.FadeMat(new Color(0.55f, 0.6f, 0.75f, 1f));
        var beaconMat = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.85f, 0.4f), 3f);

        // Start + end platforms (always solid).
        DimensionSceneUtil.Block(PrimitiveType.Cube, "StartPlatform",
            new Vector3(0f, -0.5f, 0f), new Vector3(14f, 1f, 14f), platMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "EndPlatform",
            new Vector3(0f, -0.5f, bridgeLength + 14f), new Vector3(14f, 1f, 14f), platMat, _root);
        _respawnPoint = new Vector3(0f, 1.5f, 0f);

        // Beacon tower on the far platform — the light you walk backwards toward.
        DimensionSceneUtil.Block(PrimitiveType.Cube, "BeaconTower",
            new Vector3(0f, 6f, bridgeLength + 17f), new Vector3(2f, 12f, 2f), platMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Sphere, "BeaconLightBall",
            new Vector3(0f, 13f, bridgeLength + 17f), Vector3.one * 2.5f, beaconMat, _root);
        var lightGo = new GameObject("BeaconLight");
        lightGo.transform.SetParent(_root, false);
        lightGo.transform.position = new Vector3(0f, 13f, bridgeLength + 17f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point; l.range = 60f; l.intensity = 2.5f;
        l.color = new Color(1f, 0.85f, 0.4f);

        // The bridge: segments from the start platform edge to the end platform.
        int count = Mathf.CeilToInt(bridgeLength / segmentLength);
        _segments = new Segment[count];
        for (int i = 0; i < count; i++)
        {
            float z = 7f + segmentLength * 0.5f + i * segmentLength;
            var go = DimensionSceneUtil.Block(PrimitiveType.Cube, "BridgeSeg" + i,
                new Vector3(0f, -0.15f, z), new Vector3(2.4f, 0.3f, segmentLength * 0.98f), bridgeMat, _root);
            _segments[i] = new Segment
            {
                tf = go.transform,
                rend = go.GetComponent<Renderer>(),
                col = go.GetComponent<Collider>(),
                mpb = new MaterialPropertyBlock(),
            };
        }

        // Kill volume — falling into the void respawns you at the start.
        var kill = new GameObject("KillVolume");
        kill.transform.SetParent(_root, false);
        kill.transform.position = new Vector3(0f, -30f, bridgeLength * 0.5f);
        var kb = kill.AddComponent<BoxCollider>();
        kb.isTrigger = true; kb.size = new Vector3(400f, 10f, 400f);
        kill.AddComponent<LongDarkKillVolume>().owner = this;

        // Teaching tablet at the start platform.
        BuildTablet(new Vector3(2.5f, 1.2f, 4f));

        // Exit portal on the far platform.
        DimensionSceneUtil.CreatePortal("ToD4", new Vector3(0f, 1.5f, bridgeLength + 13f),
            new Vector3(10f, 3f, 2f), LevelPortal.PortalAction.EnterInterior, nextScene, _root);

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(55f, 2f, 0.08f), 500f, 1f);
    }

    void BuildTablet(Vector3 pos)
    {
        var slab = DimensionSceneUtil.Block(PrimitiveType.Cube, "Tablet",
            pos, new Vector3(2.4f, 1.6f, 0.15f), DimensionSceneUtil.Mat(new Color(0.25f, 0.25f, 0.3f)), _root);
        slab.transform.rotation = Quaternion.Euler(0f, 180f, 0f);   // face the spawn
        var textGo = new GameObject("TabletText");
        textGo.transform.SetParent(slab.transform, false);
        textGo.transform.localPosition = new Vector3(0f, 0f, -0.51f);
        textGo.transform.localScale = Vector3.one * 0.05f;
        var tmp = textGo.AddComponent<TMPro.TextMeshPro>();
        tmp.text = "IT HOLDS ONLY\nWHAT YOU CANNOT SEE";
        tmp.fontSize = 60f;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = new Color(0.9f, 0.9f, 1f);
        var rt = tmp.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(44f, 28f);
    }

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.06f, 0.06f, 0.1f),
                fog: Color.black, fogDensity: 0.004f,
                background: new Color(0.01f, 0.01f, 0.03f));
            _atmosApplied = true;
        }

        // INVERTED rule: observed → dissolve + (after a beat) drop collider; unobserved → solid.
        foreach (var s in _segments)
        {
            var b = new Bounds(s.tf.position, new Vector3(3f, 1.5f, segmentLength + 0.5f));
            bool observed = s.tracker.Tick(b, out _, float.PositiveInfinity);

            if (observed && s.observedSince < 0f) s.observedSince = Time.time;
            if (!observed) s.observedSince = -1f;

            float targetAlpha = observed ? 0.08f : 1f;               // ghost hint, never fully invisible
            s.alpha = Mathf.MoveTowards(s.alpha, targetAlpha, Time.deltaTime / 0.25f);
            s.mpb.SetColor(ColorId, new Color(0.55f, 0.6f, 0.75f, s.alpha));
            s.rend.SetPropertyBlock(s.mpb);

            // Collider drops only after the delay — a quick glance isn't instant doom.
            s.col.enabled = !(observed && Time.time - s.observedSince > colliderDropDelay);
        }
    }

    /// <summary>Kill-volume respawn: back to the start platform, velocity zeroed. No damage.</summary>
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
    [Header("Bridge")]
    [Tooltip("Gap between the platforms (metres).")]
    public float bridgeLength = 150f;
    [Tooltip("Length of one solid-while-unseen segment.")]
    public float segmentLength = 3f;
    [Tooltip("Seconds a segment stays walkable after you start looking at it.")]
    public float colliderDropDelay = 0.2f;

    [Header("Exit")]
    [Tooltip("Scene the beacon platform leads to.")]
    public string nextScene = "D4_WaitingField";
}

/// <summary>Void-fall trigger that hands the player back to the start platform.</summary>
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
