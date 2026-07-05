using UnityEngine;

/// <summary>
/// D20 "Cloud Shelf": cloud tops over an endless sunset sea. The rule is fully
/// INVERTED — cloud platforms are solid only while UNOBSERVED. Look at a cloud and
/// it's just vapor; turn your back and it will carry you. Cross the sky by walking
/// backwards. Falling drops you through gold light back to the start shelf.
/// </summary>
public class CloudShelfController : MonoBehaviour
{
    class Cloud
    {
        public Transform tf;
        public Renderer[] rends;
        public Collider col;
        public MaterialPropertyBlock mpb;
        public ObservationTracker tracker = new ObservationTracker(0.15f);
        public float alpha = 1f;
        public float observedSince = -1f;
    }

    Cloud[] _clouds;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        var root = transform;
        var solidCloudMat = DimensionSceneUtil.Mat(new Color(0.98f, 0.95f, 0.92f), 0.1f);
        var seaMat = DimensionSceneUtil.Mat(new Color(0.25f, 0.18f, 0.30f), 0.8f);

        // The sea of dusk far below + fall catcher.
        var sea = DimensionSceneUtil.Block(PrimitiveType.Cube, "DuskSea",
            new Vector3(0f, -45f, 60f), new Vector3(1200f, 1f, 1200f), seaMat, root);
        var wash = new GameObject("FallVolume");
        wash.transform.SetParent(root, false);
        wash.transform.position = new Vector3(0f, -38f, 60f);
        var wb = wash.AddComponent<BoxCollider>();
        wb.isTrigger = true; wb.size = new Vector3(1000f, 6f, 1000f);
        wash.AddComponent<DimensionRespawnVolume>().respawnPoint = new Vector3(0f, 1.5f, 0f);

        // Start and goal shelves: always solid.
        BuildPuffPlatform(root, new Vector3(0f, 0f, 0f), 7f, solidCloudMat, alwaysSolid: true);
        BuildPuffPlatform(root, new Vector3(0f, 0f, 62f), 7f, solidCloudMat, alwaysSolid: true);

        // The crossing: clouds solid only while UNOBSERVED.
        var ghostMat = DimensionSceneUtil.FadeMat(new Color(0.98f, 0.95f, 0.92f, 0.9f));
        Vector3[] spots =
        {
            new Vector3(-2.5f, 0f, 9f),  new Vector3(2.2f, 0f, 16.5f), new Vector3(-1.8f, 0f, 24f),
            new Vector3(2.6f, 0f, 31.5f), new Vector3(-2.2f, 0f, 39f), new Vector3(1.9f, 0f, 46.5f),
            new Vector3(-1.5f, 0f, 54f),
        };
        _clouds = new Cloud[spots.Length];
        for (int i = 0; i < spots.Length; i++)
        {
            var deck = DimensionSceneUtil.Block(PrimitiveType.Cube, "CloudDeck" + i,
                spots[i], new Vector3(3.6f, 0.6f, 3.6f), ghostMat, root);
            // Puffs around the deck (visual only).
            var puffs = new Renderer[3];
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI * 2f / 3f + i;
                var puff = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Puff",
                    Vector3.zero, new Vector3(2.2f, 1.1f, 2.2f), ghostMat, deck.transform);
                puff.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.42f, -0.1f, Mathf.Sin(a) * 0.42f);
                Object.Destroy(puff.GetComponent<Collider>());
                puffs[k] = puff.GetComponent<Renderer>();
            }
            var rends = new Renderer[4];
            rends[0] = deck.GetComponent<Renderer>();
            puffs.CopyTo(rends, 1);
            _clouds[i] = new Cloud
            {
                tf = deck.transform,
                rends = rends,
                col = deck.GetComponent<Collider>(),
                mpb = new MaterialPropertyBlock(),
            };
        }

        // Goal: a golden door on the far shelf.
        var frame = DimensionSceneUtil.Mat(new Color(0.35f, 0.22f, 0.10f));
        var pane  = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.8f, 0.45f), 2.6f);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostL",  new Vector3(-0.8f, 1.5f, 64f), new Vector3(0.3f, 3f, 0.3f), frame, root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostR",  new Vector3( 0.8f, 1.5f, 64f), new Vector3(0.3f, 3f, 0.3f), frame, root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Lintel", new Vector3(0f, 3.05f, 64f),   new Vector3(1.9f, 0.3f, 0.3f), frame, root);
        var glow = DimensionSceneUtil.Block(PrimitiveType.Cube, "Glow", new Vector3(0f, 1.5f, 64f), new Vector3(1.3f, 2.9f, 0.05f), pane, root);
        Object.Destroy(glow.GetComponent<Collider>());
        DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1.5f, 64f),
            new Vector3(1.3f, 2.9f, 0.6f), LevelPortal.PortalAction.EnterInterior, nextScene, root);

        // A low sun on the horizon.
        var sunMat = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.55f, 0.25f), 3f);
        var sun = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Sun",
            new Vector3(-180f, -6f, 320f), Vector3.one * 46f, sunMat, root);
        Object.Destroy(sun.GetComponent<Collider>());
        DimensionSceneUtil.CreateDirectionalLight(new Color(1f, 0.6f, 0.35f), 1.0f, new Vector3(8f, 150f, 0f), false);

        // Warning sign at the start shelf edge.
        var sign = DimensionSceneUtil.Block(PrimitiveType.Cube, "Sign",
            new Vector3(2.4f, 1.6f, 4.5f), new Vector3(0.1f, 0.9f, 1.4f),
            DimensionSceneUtil.Mat(new Color(0.25f, 0.16f, 0.08f)), root);
        var textGo = new GameObject("SignText");
        textGo.transform.SetParent(sign.transform, false);
        textGo.transform.localPosition = new Vector3(-0.51f, 0f, 0f);
        textGo.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        textGo.transform.localScale = new Vector3(0.035f, 0.055f, 0.5f);
        var tmp = textGo.AddComponent<TMPro.TextMeshPro>();
        tmp.text = "CLOUDS HOLD\nONLY THE UNSEEING";
        tmp.fontSize = 44f;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 0.9f, 0.75f);
        tmp.GetComponent<RectTransform>().sizeDelta = new Vector2(30f, 16f);

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(72f, 3f, 0.05f), 500f, 1f);
    }

    static void BuildPuffPlatform(Transform root, Vector3 pos, float size, Material mat, bool alwaysSolid)
    {
        var deck = DimensionSceneUtil.Block(PrimitiveType.Cube, "Shelf",
            pos, new Vector3(size, 0.8f, size), mat, root);
        for (int k = 0; k < 5; k++)
        {
            float a = k * Mathf.PI * 2f / 5f;
            var puff = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Puff",
                Vector3.zero, new Vector3(3.4f, 1.6f, 3.4f), mat, deck.transform);
            puff.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.45f, -0.15f, Mathf.Sin(a) * 0.45f);
            Object.Destroy(puff.GetComponent<Collider>());
        }
    }

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.45f, 0.30f, 0.28f),
                fog: new Color(0.85f, 0.48f, 0.32f), fogDensity: 0.006f,
                background: new Color(0.72f, 0.36f, 0.30f));
            _atmosApplied = true;
        }

        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }

        foreach (var c in _clouds)
        {
            // FLAT footprint test (bounce-bug rule). INVERSE: observed = vapor.
            var b = new Bounds(c.tf.position, new Vector3(3.6f, 0.7f, 3.6f));
            bool observed = c.tracker.Tick(b, out _, float.PositiveInfinity);

            if (observed && c.observedSince < 0f) c.observedSince = Time.time;
            if (!observed) c.observedSince = -1f;

            // Solid while unseen; a glance is forgiven for vaporDelay seconds.
            c.col.enabled = !observed || Time.time - c.observedSince <= vaporDelay;

            float targetAlpha = observed ? 0.25f : 0.9f;
            c.alpha = Mathf.MoveTowards(c.alpha, targetAlpha, Time.deltaTime / 0.25f);
            c.mpb.SetColor(ColorId, new Color(0.98f, 0.95f, 0.92f, c.alpha));
            foreach (var r in c.rends) r.SetPropertyBlock(c.mpb);
        }
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Clouds")]
    [Tooltip("Seconds a cloud stays solid after you glance at it (forgiveness window).")]
    public float vaporDelay = 0.45f;

    [Header("Exit")]
    [Tooltip("Scene the golden door leads to.")]
    public string nextScene = "D21_ArchiveStacks";
}
