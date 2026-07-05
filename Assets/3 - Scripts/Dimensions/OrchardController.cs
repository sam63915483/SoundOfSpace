using UnityEngine;

/// <summary>
/// D13 "The Orchard": rows of identical white-blossom trees in soft pink fog. Exactly
/// one tree bears red fruit and hums (gaze-reactive) — walk into its trunk to leave.
/// Brushing any other trunk scrambles you to a random spot between the rows. Hunt by
/// ear, commit carefully.
/// </summary>
public class OrchardController : MonoBehaviour
{
    Transform _root;
    Transform _trueTree;
    AudioSource _trueHum;
    float _scrambleDebounceUntil;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        var groundMat  = DimensionSceneUtil.Mat(new Color(0.35f, 0.24f, 0.22f), 0.05f);
        var trunkMat   = DimensionSceneUtil.Mat(new Color(0.38f, 0.28f, 0.22f), 0.05f);
        var blossomMat = DimensionSceneUtil.Mat(new Color(0.97f, 0.93f, 0.94f), 0.1f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Ground",
            new Vector3(0f, -0.5f, 0f), new Vector3(1200f, 1f, 1200f), groundMat, _root);
        DimensionSceneUtil.CreateDirectionalLight(new Color(1f, 0.85f, 0.8f), 0.85f, new Vector3(22f, -35f, 0f), true);

        // Pick the true tree away from the spawn row so the hunt has some distance.
        int trueX = Random.Range(0, gridCount), trueZ = Random.Range(gridCount / 2, gridCount);
        for (int ix = 0; ix < gridCount; ix++)
            for (int iz = 0; iz < gridCount; iz++)
                BuildTree(ix, iz, ix == trueX && iz == trueZ, trunkMat, blossomMat);

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(85f, 3f, 0.05f), 500f, 1f);
    }

    Vector3 CellCenter(int ix, int iz) =>
        new Vector3((ix - (gridCount - 1) * 0.5f) * rowSpacing, 0f, (iz - (gridCount - 1) * 0.5f) * rowSpacing + 12f);

    void BuildTree(int ix, int iz, bool isTrue, Material trunkMat, Material blossomMat)
    {
        var tree = new GameObject(isTrue ? "Tree_TRUE" : "Tree");
        tree.transform.SetParent(_root, false);

        DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Trunk",
            new Vector3(0f, 1.3f, 0f), new Vector3(0.45f, 1.3f, 0.45f), trunkMat, tree.transform);
        Canopy(tree.transform, new Vector3(0f, 3.3f, 0f), new Vector3(2.7f, 2.1f, 2.7f), blossomMat);
        Canopy(tree.transform, new Vector3(0.8f, 3.9f, 0.3f), new Vector3(1.9f, 1.6f, 1.9f), blossomMat);
        Canopy(tree.transform, new Vector3(-0.7f, 3.7f, -0.5f), new Vector3(1.8f, 1.5f, 1.8f), blossomMat);

        if (isTrue)
        {
            var fruitMat = DimensionSceneUtil.EmissiveMat(new Color(0.95f, 0.12f, 0.1f), 2.2f);
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f;
                var fruit = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Fruit",
                    new Vector3(Mathf.Cos(a) * 1.15f, 2.55f + (k % 2) * 0.3f, Mathf.Sin(a) * 1.15f),
                    Vector3.one * 0.24f, fruitMat, tree.transform);
                Destroy(fruit.GetComponent<Collider>());
            }
            _trueHum = DimensionSceneUtil.LoopingAudio(tree, DimensionSceneUtil.ToneClip(396f, 2f, 0.45f), 180f, 1f);
            DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1.2f, 0f),
                new Vector3(1.8f, 2.2f, 1.8f), LevelPortal.PortalAction.EnterInterior, nextScene, tree.transform);
            _trueTree = tree.transform;
        }
        else
        {
            var trig = new GameObject("ScrambleTrigger");
            trig.transform.SetParent(tree.transform, false);
            trig.transform.localPosition = new Vector3(0f, 1f, 0f);
            var sc = trig.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 1.05f;
            trig.AddComponent<OrchardScrambleTrigger>().owner = this;
        }

        tree.transform.SetPositionAndRotation(CellCenter(ix, iz),
            Quaternion.Euler(0f, Random.value * 360f, 0f));
    }

    static void Canopy(Transform tree, Vector3 pos, Vector3 scale, Material mat)
    {
        var c = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Blossom", Vector3.zero, scale, mat, tree);
        c.transform.localPosition = pos;
        Destroy(c.GetComponent<Collider>());
    }

    void Update()
    {
        var cam = ObserverState.Cam;
        if (cam == null) return;
        if (!_atmosApplied)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.52f, 0.42f, 0.44f),
                fog: new Color(0.92f, 0.72f, 0.76f), fogDensity: 0.020f,
                background: new Color(0.88f, 0.66f, 0.70f));
            _atmosApplied = true;
        }

        if (_trueHum != null && _trueTree != null)
        {
            Vector3 to = _trueTree.position - cam.transform.position;
            float align = Vector3.Dot(cam.transform.forward, to.normalized);
            _trueHum.volume = Mathf.Lerp(0.06f, 1f, Mathf.InverseLerp(0.2f, 0.95f, align));
        }
    }

    /// <summary>Wrong-trunk punishment: dropped in a random row gap, velocity zeroed.</summary>
    public void ScramblePlayer()
    {
        if (Time.time < _scrambleDebounceUntil) return;
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null) return;
        _scrambleDebounceUntil = Time.time + 1.5f;
        // Diagonal midpoint of a random cell = equidistant from four trunks: never
        // scramble INTO another trigger.
        int ix = Random.Range(0, gridCount - 1), iz = Random.Range(0, gridCount - 1);
        Vector3 p = CellCenter(ix, iz) + new Vector3(rowSpacing * 0.5f, 0f, rowSpacing * 0.5f);
        _player.Rigidbody.position = p + Vector3.up * 1.5f;
        _player.Rigidbody.velocity = Vector3.zero;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Orchard")]
    [Tooltip("Trees per side (grid is gridCount × gridCount).")]
    public int gridCount = 8;
    [Tooltip("Spacing between rows (metres).")]
    public float rowSpacing = 9f;

    [Header("Exit")]
    [Tooltip("Scene the fruit tree leads to.")]
    public string nextScene = "D14_GlacierThroat";
}

/// <summary>Trunk-brush trigger on every wrong tree.</summary>
public class OrchardScrambleTrigger : MonoBehaviour
{
    [HideInInspector] public OrchardController owner;

    void OnTriggerEnter(Collider other)
    {
        if (owner == null) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;
        owner.ScramblePlayer();
    }
}
