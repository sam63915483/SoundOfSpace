using UnityEngine;

/// <summary>
/// D13 "The Orchard": fenced rows of identical white-blossom trees in soft pink fog,
/// petals drifting down everywhere. Exactly one tree bears red fruit, sheds RED
/// petals, and hums when faced (gaze-audio) — walk into its trunk to leave. Brushing
/// any other trunk scrambles you to a random row gap with a pink flash. Wind chimes
/// mark the hunt.
/// </summary>
public class OrchardController : MonoBehaviour
{
    Transform _root;
    Transform _trueTree;
    AudioSource _trueHum;
    AudioSource _chimer;
    AudioClip _chimeClip;
    float _nextChimeTime;
    UnityEngine.UI.Image _flash;
    float _scrambleDebounceUntil;
    ParticleSystem _petals;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        var groundMat  = DimensionSceneUtil.TexMat("grass_lush", Color.white, new Vector2(400f, 400f), 0.05f);
        var trunkMat   = DimensionSceneUtil.TexMat("d13_bark", Color.white, new Vector2(1f, 2f), 0.05f);
        var blossomMat = DimensionSceneUtil.Mat(new Color(0.97f, 0.93f, 0.94f), 0.1f);
        var blushMat   = DimensionSceneUtil.Mat(new Color(0.98f, 0.88f, 0.90f), 0.1f);
        var fenceMat   = DimensionSceneUtil.Mat(new Color(0.88f, 0.86f, 0.82f), 0.1f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Ground",
            new Vector3(0f, -0.5f, 0f), new Vector3(1200f, 1f, 1200f), groundMat, _root);
        DimensionSceneUtil.CreateDirectionalLight(new Color(1f, 0.85f, 0.8f), 0.85f, new Vector3(22f, -35f, 0f), true);

        int trueX = Random.Range(1, gridCount - 1), trueZ = Random.Range(gridCount / 2, gridCount - 1);
        for (int ix = 0; ix < gridCount; ix++)
            for (int iz = 0; iz < gridCount; iz++)
                BuildTree(ix, iz, ix == trueX && iz == trueZ, trunkMat,
                    (ix + iz) % 3 == 0 ? blushMat : blossomMat);

        BuildFence(fenceMat);
        BuildPetals();
        BuildFlash();
        BuildPicnicDressing();

        DimensionSceneUtil.AmbienceLoop2D(gameObject, "amb_d13", 85f, 0.05f, 0.55f);
        _chimeClip = WindChimeClip();
        var chimeGo = new GameObject("Chimer");
        chimeGo.transform.SetParent(_root, false);
        _chimer = chimeGo.AddComponent<AudioSource>();
        _chimer.spatialBlend = 1f;
        _chimer.rolloffMode = AudioRolloffMode.Linear;
        _chimer.maxDistance = 120f;
        _nextChimeTime = Time.time + 5f;
    }

    Vector3 CellCenter(int ix, int iz) =>
        new Vector3((ix - (gridCount - 1) * 0.5f) * rowSpacing, 0f, (iz - (gridCount - 1) * 0.5f) * rowSpacing + 12f);

    float FieldHalf => (gridCount - 1) * 0.5f * rowSpacing + 8f;

    void BuildTree(int ix, int iz, bool isTrue, Material trunkMat, Material blossomMat)
    {
        var tree = new GameObject(isTrue ? "Tree_TRUE" : "Tree");
        tree.transform.SetParent(_root, false);

        var trunk = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Trunk",
            new Vector3(0f, 1.3f, 0f), new Vector3(0.42f, 1.3f, 0.42f), trunkMat, tree.transform);
        trunk.transform.localRotation = Quaternion.Euler(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
        var bough = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Bough",
            new Vector3(0.35f, 2.5f, 0.1f), new Vector3(0.2f, 0.5f, 0.2f), trunkMat, tree.transform);
        bough.transform.localRotation = Quaternion.Euler(0f, 0f, -38f);
        Destroy(bough.GetComponent<Collider>());

        Canopy(tree.transform, new Vector3(0f, 3.4f, 0f), new Vector3(2.8f, 2.1f, 2.8f), blossomMat);
        Canopy(tree.transform, new Vector3(0.9f, 3.9f, 0.3f), new Vector3(1.9f, 1.6f, 1.9f), blossomMat);
        Canopy(tree.transform, new Vector3(-0.8f, 3.8f, -0.5f), new Vector3(1.8f, 1.5f, 1.8f), blossomMat);
        Canopy(tree.transform, new Vector3(0.1f, 4.4f, 0.6f), new Vector3(1.4f, 1.2f, 1.4f), blossomMat);

        if (isTrue)
        {
            var fruitMat = DimensionSceneUtil.EmissiveMat(new Color(0.95f, 0.12f, 0.1f), 2.2f);
            for (int k = 0; k < 7; k++)
            {
                float a = k * Mathf.PI * 2f / 7f;
                var fruit = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Fruit",
                    new Vector3(Mathf.Cos(a) * 1.2f, 2.6f + (k % 2) * 0.35f, Mathf.Sin(a) * 1.2f),
                    Vector3.one * 0.24f, fruitMat, tree.transform);
                Destroy(fruit.GetComponent<Collider>());
            }
            _trueHum = DimensionSceneUtil.LoopingAudio(tree, DimensionSceneUtil.ToneClip(396f, 2f, 0.45f), 180f, 1f);
            DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1.2f, 0f),
                new Vector3(1.8f, 2.2f, 1.8f), LevelPortal.PortalAction.EnterInterior, nextScene, tree.transform);
            _trueTree = tree.transform;

            // The second tell: this tree sheds RED petals.
            var red = MakePetalSystem(tree.transform, new Vector3(0f, 4.6f, 0f), 3f, 9f,
                new Color(0.95f, 0.2f, 0.18f, 0.85f));
            red.transform.localPosition = new Vector3(0f, 4.6f, 0f);
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

    void Canopy(Transform tree, Vector3 pos, Vector3 scale, Material mat)
    {
        var c = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Blossom", Vector3.zero, scale, mat, tree);
        c.transform.localPosition = pos;
        Destroy(c.GetComponent<Collider>());
    }

    // Low white picket fence around the whole orchard, with a gap behind the spawn.
    void BuildFence(Material fenceMat)
    {
        float half = FieldHalf;
        for (int side = 0; side < 4; side++)
        {
            Vector3 dir = side == 0 ? Vector3.forward : side == 1 ? Vector3.back : side == 2 ? Vector3.left : Vector3.right;
            Vector3 along = side < 2 ? Vector3.right : Vector3.forward;
            Vector3 center = dir * half + Vector3.forward * 12f;
            var rail = DimensionSceneUtil.Block(PrimitiveType.Cube, "FenceRail",
                center + Vector3.up * 0.85f,
                side < 2 ? new Vector3(half * 2f, 0.12f, 0.1f) : new Vector3(0.1f, 0.12f, half * 2f), fenceMat, _root);
            var rail2 = DimensionSceneUtil.Block(PrimitiveType.Cube, "FenceRail2",
                center + Vector3.up * 0.45f,
                side < 2 ? new Vector3(half * 2f, 0.1f, 0.08f) : new Vector3(0.08f, 0.1f, half * 2f), fenceMat, _root);
            Destroy(rail2.GetComponent<Collider>());
            for (float t = -half; t <= half; t += 3.2f)
            {
                var post = DimensionSceneUtil.Block(PrimitiveType.Cube, "FencePost",
                    center + along * t + Vector3.up * 0.55f, new Vector3(0.14f, 1.1f, 0.14f), fenceMat, _root);
                Destroy(post.GetComponent<Collider>());
            }
        }
    }

    // Somebody was picnicking here. The blankets, baskets and ladders are never
    // where they were the last time you looked — each cluster re-deals itself onto
    // nearby gaps/trunks whenever its patch of orchard leaves your view.
    void BuildPicnicDressing()
    {
        var blanketMat = DimensionSceneUtil.Mat(new Color(0.78f, 0.24f, 0.22f), 0.05f);
        var patchMat   = DimensionSceneUtil.Mat(new Color(0.95f, 0.92f, 0.88f), 0.05f);
        var wickerMat  = DimensionSceneUtil.Mat(new Color(0.62f, 0.45f, 0.24f), 0.15f);
        var fruitMat   = DimensionSceneUtil.Mat(new Color(0.85f, 0.15f, 0.12f), 0.3f);
        var ladderMat  = DimensionSceneUtil.TexMat("wood_worn", new Color(0.75f, 0.66f, 0.55f), new Vector2(0.4f, 2f), 0.1f);

        // Picnic cluster: blankets + baskets share ground anchors in the row gaps
        // around one corner of the orchard.
        var picnic = PropShuffleSet.Create("PicnicShuffle", _root,
            new Bounds(new Vector3(-9f, 1f, 0f), new Vector3(24f, 4f, 24f)),
            "sfx_wood_creak", facePlayer: false, countJitter: true);
        picnic.AddAnchor(CellCenter(2, 2) + new Vector3(4.5f, 0f, 0f), 15f);
        picnic.AddAnchor(CellCenter(2, 2) + new Vector3(0f, 0f, 4.5f), 130f);
        picnic.AddAnchor(CellCenter(1, 2) + new Vector3(4.5f, 0f, 4.5f), 250f);
        picnic.AddAnchor(CellCenter(2, 3) + new Vector3(-4.5f, 0f, 0f), 80f);
        picnic.AddAnchor(CellCenter(3, 2) + new Vector3(0f, 0f, -4.5f), 200f);
        picnic.AddAnchor(CellCenter(3, 3) + new Vector3(-4.5f, 0f, -4.5f), 320f);
        picnic.AddProp(BuildBlanket(blanketMat, patchMat));
        picnic.AddProp(BuildBlanket(blanketMat, patchMat));
        picnic.AddProp(BuildBasket(wickerMat, fruitMat));
        picnic.AddProp(BuildBasket(wickerMat, fruitMat));

        // Two orchard ladders, each haunting its own little stand of trees — always
        // leaning against a DIFFERENT trunk when you come back around.
        BuildLadderSet("LadderShuffleA", ladderMat,
            new Bounds(new Vector3(9f, 2f, 3f), new Vector3(24f, 6f, 20f)),
            new[] { new Vector2Int(4, 2), new Vector2Int(5, 3), new Vector2Int(5, 2) });
        BuildLadderSet("LadderShuffleB", ladderMat,
            new Bounds(new Vector3(-9f, 2f, 21f), new Vector3(24f, 6f, 22f)),
            new[] { new Vector2Int(2, 5), new Vector2Int(3, 5), new Vector2Int(2, 4) });
    }

    void BuildLadderSet(string name, Material ladderMat, Bounds zone, Vector2Int[] cells)
    {
        var set = PropShuffleSet.Create(name, _root, zone, "sfx_wood_creak");
        set.posJitter = 0.05f;          // must stay against the trunk
        set.yawJitterDeg = 4f;
        foreach (var c in cells)
        {
            Vector3 trunk = CellCenter(c.x, c.y);
            float a = Random.value * Mathf.PI * 2f;
            Vector3 pos = trunk + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.75f;  // 14° lean over 2.7m rails lands the top on the trunk
            float yaw = Quaternion.LookRotation(trunk - pos).eulerAngles.y;
            set.AddAnchor(pos, yaw);
        }
        set.AddProp(BuildLadder(ladderMat));
    }

    GameObject BuildBlanket(Material blanketMat, Material patchMat)
    {
        var root = new GameObject("PicnicBlanket");
        root.transform.SetParent(_root, false);
        var cloth = DimensionSceneUtil.Block(PrimitiveType.Cube, "Cloth",
            Vector3.zero, new Vector3(1.7f, 0.02f, 1.7f), blanketMat, root.transform);
        cloth.transform.localPosition = new Vector3(0f, 0.012f, 0f);
        Destroy(cloth.GetComponent<Collider>());
        for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                var patch = DimensionSceneUtil.Block(PrimitiveType.Cube, "Patch",
                    Vector3.zero, new Vector3(0.42f, 0.012f, 0.42f), patchMat, root.transform);
                patch.transform.localPosition = new Vector3(x * 0.42f, 0.024f, z * 0.42f);
                Destroy(patch.GetComponent<Collider>());
            }
        return root;
    }

    GameObject BuildBasket(Material wickerMat, Material fruitMat)
    {
        var root = new GameObject("FruitBasket");
        root.transform.SetParent(_root, false);
        var body = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Basket",
            Vector3.zero, new Vector3(0.32f, 0.16f, 0.32f), wickerMat, root.transform);
        body.transform.localPosition = new Vector3(0f, 0.16f, 0f);
        for (int k = 0; k < 3; k++)
        {
            var fruit = DimensionSceneUtil.Block(PrimitiveType.Sphere, "Fruit",
                Vector3.zero, Vector3.one * 0.11f, fruitMat, root.transform);
            fruit.transform.localPosition = new Vector3(
                Mathf.Cos(k * 2.1f) * 0.09f, 0.36f, Mathf.Sin(k * 2.1f) * 0.09f);
            Destroy(fruit.GetComponent<Collider>());
        }
        return root;
    }

    // Wooden orchard ladder, lean baked into the prop (+Z tips toward the anchor's trunk).
    GameObject BuildLadder(Material mat)
    {
        var root = new GameObject("OrchardLadder");
        root.transform.SetParent(_root, false);
        var lean = new GameObject("Lean");
        lean.transform.SetParent(root.transform, false);
        lean.transform.localRotation = Quaternion.Euler(14f, 0f, 0f);
        for (int side = -1; side <= 1; side += 2)
        {
            var rail = DimensionSceneUtil.Block(PrimitiveType.Cube, "Rail",
                Vector3.zero, new Vector3(0.07f, 2.7f, 0.07f), mat, lean.transform);
            rail.transform.localPosition = new Vector3(side * 0.26f, 1.35f, 0f);
        }
        for (int r = 0; r < 5; r++)
        {
            var rung = DimensionSceneUtil.Block(PrimitiveType.Cube, "Rung",
                Vector3.zero, new Vector3(0.55f, 0.06f, 0.06f), mat, lean.transform);
            rung.transform.localPosition = new Vector3(0f, 0.4f + r * 0.5f, 0f);
            Destroy(rung.GetComponent<Collider>());
        }
        return root;
    }

    // Soft pink petal drift that follows the player — the whole orchard is shedding.
    void BuildPetals()
    {
        _petals = MakePetalSystem(_root, Vector3.zero, 22f, 90f, new Color(0.98f, 0.85f, 0.88f, 0.7f));
    }

    ParticleSystem MakePetalSystem(Transform parent, Vector3 localPos, float radius, float rate, Color color)
    {
        var go = new GameObject("Petals");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 7f;
        main.startSpeed = 0.25f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
        main.startColor = color;
        main.maxParticles = 500;
        main.gravityModifier = 0.045f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.5f);
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-2.4f, 2.4f);
        var psr = go.GetComponent<ParticleSystemRenderer>();
        var mat = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
        mat.SetColor("_TintColor", color);
        psr.material = mat;
        return ps;
    }

    void BuildFlash()
    {
        var canvasGo = new GameObject("ScrambleFlash");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9998;
        var imgGo = new GameObject("Pink");
        imgGo.transform.SetParent(canvasGo.transform, false);
        _flash = imgGo.AddComponent<UnityEngine.UI.Image>();
        _flash.color = new Color(1f, 0.75f, 0.8f, 0f);
        _flash.raycastTarget = false;
        var rt = _flash.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    // Three-note descending glass chime.
    static AudioClip WindChimeClip()
    {
        int rate = 44100;
        float seconds = 1.8f;
        int samples = (int)(rate * seconds);
        var data = new float[samples];
        float[] notes = { 1567f, 1244f, 1046f };
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            float v = 0f;
            for (int n = 0; n < notes.Length; n++)
            {
                float start = n * 0.28f;
                if (t >= start) v += Mathf.Sin(2f * Mathf.PI * notes[n] * (t - start)) * Mathf.Exp(-(t - start) * 4f) * 0.28f;
            }
            data[i] = v;
        }
        var clip = AudioClip.Create("windchime", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
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

        // Petal cloud rides above the player.
        if (_petals != null)
        {
            Vector3 p = cam.transform.position;
            _petals.transform.position = new Vector3(p.x, 6.5f, p.z);
        }

        if (_trueHum != null && _trueTree != null)
        {
            Vector3 to = _trueTree.position - cam.transform.position;
            float align = Vector3.Dot(cam.transform.forward, to.normalized);
            _trueHum.volume = Mathf.Lerp(0.06f, 1f, Mathf.InverseLerp(0.2f, 0.95f, align));
        }

        if (_flash != null && _flash.color.a > 0f)
            _flash.color = new Color(1f, 0.75f, 0.8f, Mathf.MoveTowards(_flash.color.a, 0f, Time.deltaTime * 1.6f));

        if (Time.time >= _nextChimeTime)
        {
            _nextChimeTime = Time.time + Random.Range(7f, 16f);
            float a = Random.value * Mathf.PI * 2f;
            _chimer.transform.position = cam.transform.position + new Vector3(Mathf.Cos(a), 0.5f, Mathf.Sin(a)) * Random.Range(15f, 40f);
            _chimer.pitch = Random.Range(0.9f, 1.1f);
            _chimer.PlayOneShot(_chimeClip, 0.45f);
        }
    }

    /// <summary>Wrong-trunk punishment: pink flash, dropped in a random row gap.</summary>
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
        if (_flash != null) _flash.color = new Color(1f, 0.75f, 0.8f, 0.85f);
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
    public string nextScene = "D15_Congregation";
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
