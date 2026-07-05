using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// D27 "Inverted Rain": a cobblestone plaza where the rain falls UP. Doorways ring
/// the plaza edge and swap positions whenever they leave your view. Seven of them
/// shed rain upward like everything else here; over exactly one, the rain falls
/// DOWN. The particle tell is the puzzle — no sound, no glow. Wrong doors return
/// you to the plaza's center fountain.
/// </summary>
public class InvertedRainController : MonoBehaviour
{
    class Door
    {
        public Transform tf;
        public int slot;
        public readonly ObservationTracker tracker = new ObservationTracker();
    }

    readonly List<Door> _doors = new List<Door>();
    Transform _root;
    float _loopDebounceUntil;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    const int SlotCount = 12;               // perimeter slots; 8 doors shuffle among them

    Vector3 SlotPos(int i)
    {
        float a = i * Mathf.PI * 2f / SlotCount;
        return new Vector3(Mathf.Cos(a) * plazaRadius, 0f, Mathf.Sin(a) * plazaRadius);
    }

    void Awake()
    {
        _root = transform;
        var cobbleMat = DimensionSceneUtil.Mat(new Color(0.22f, 0.22f, 0.25f), 0.3f);
        var frameMat  = DimensionSceneUtil.Mat(new Color(0.12f, 0.10f, 0.10f), 0.2f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Plaza",
            new Vector3(0f, -0.5f, 0f), new Vector3(600f, 1f, 600f), cobbleMat, _root);
        // Cobble detail ring: staggered slabs inside the plaza.
        Random.State prev = Random.state;
        Random.InitState(2727);
        for (int i = 0; i < 40; i++)
        {
            float a = Random.value * Mathf.PI * 2f, d = Mathf.Sqrt(Random.value) * (plazaRadius - 2f);
            var slab = DimensionSceneUtil.Block(PrimitiveType.Cube, "Cobble",
                new Vector3(Mathf.Cos(a) * d, 0.03f, Mathf.Sin(a) * d),
                new Vector3(Random.Range(0.8f, 1.8f), 0.06f, Random.Range(0.8f, 1.8f)), cobbleMat, _root);
            slab.transform.rotation = Quaternion.Euler(0f, Random.value * 360f, 0f);
            Object.Destroy(slab.GetComponent<Collider>());
        }
        Random.state = prev;

        // Center fountain (also the wrong-door return point).
        var fountain = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "FountainRim",
            new Vector3(0f, 0.4f, 0f), new Vector3(3f, 0.4f, 3f), frameMat, _root);
        Object.Destroy(fountain.GetComponent<Collider>());
        var basin = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "FountainWater",
            new Vector3(0f, 0.62f, 0f), new Vector3(2.6f, 0.05f, 2.6f),
            DimensionSceneUtil.Mat(new Color(0.05f, 0.07f, 0.12f), 0.9f), _root);
        Object.Destroy(basin.GetComponent<Collider>());

        DimensionSceneUtil.CreateDirectionalLight(new Color(0.45f, 0.5f, 0.65f), 0.5f, new Vector3(40f, -30f, 0f), true);

        // The world's rain: a wide column of drops falling UP out of the plaza.
        BuildRain(new Vector3(0f, 0.2f, 0f), plazaRadius + 6f, upward: true, rate: 500f);

        // Doors: index 0 is the true one (downward rain), the rest shed rain upward.
        int[] startSlots = { 0, 2, 3, 5, 6, 8, 9, 11 };
        for (int i = 0; i < startSlots.Length; i++)
        {
            var door = BuildDoor(i == 0);
            var d = new Door { tf = door.transform, slot = startSlots[i] };
            _doors.Add(d);
            SeatDoor(d, d.slot);
        }

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(90f, 3f, 0.05f), 400f, 1f);
    }

    GameObject BuildDoor(bool isTrue)
    {
        var frameMat = DimensionSceneUtil.Mat(new Color(0.13f, 0.11f, 0.11f), 0.2f);
        var door = new GameObject(isTrue ? "Door_TRUE" : "Door");
        door.transform.SetParent(_root, false);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostL", new Vector3(-0.85f, 1.5f, 0f), new Vector3(0.35f, 3f, 0.35f), frameMat, door.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostR", new Vector3(0.85f, 1.5f, 0f), new Vector3(0.35f, 3f, 0.35f), frameMat, door.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Lintel", new Vector3(0f, 3.15f, 0f), new Vector3(2.05f, 0.35f, 0.35f), frameMat, door.transform);
        var dark = DimensionSceneUtil.Block(PrimitiveType.Cube, "Void", new Vector3(0f, 1.5f, 0f), new Vector3(1.35f, 3f, 0.08f), DimensionSceneUtil.Mat(new Color(0.02f, 0.02f, 0.03f)), door.transform);
        Object.Destroy(dark.GetComponent<Collider>());

        // The tell: every door has a local rain column; only the true door's falls DOWN.
        var rain = BuildRain(new Vector3(0f, isTrue ? 6f : 0.2f, 0f), 2.4f, upward: !isTrue, rate: 60f);
        rain.transform.SetParent(door.transform, false);

        if (isTrue)
            DimensionSceneUtil.CreatePortal("ToNext", new Vector3(0f, 1.5f, 0f),
                new Vector3(1.35f, 2.9f, 0.8f), LevelPortal.PortalAction.EnterInterior, nextScene, door.transform);
        else
        {
            var trig = new GameObject("LoopTrigger");
            trig.transform.SetParent(door.transform, false);
            trig.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            var box = trig.AddComponent<BoxCollider>();
            box.isTrigger = true; box.size = new Vector3(1.35f, 2.9f, 0.8f);
            trig.AddComponent<InvertedRainLoopTrigger>().owner = this;
        }
        return door;
    }

    GameObject BuildRain(Vector3 localPos, float radius, bool upward, float rate)
    {
        var go = new GameObject(upward ? "RainUp" : "RainDown");
        go.transform.SetParent(_root, false);
        go.transform.localPosition = localPos;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 3.2f;
        main.startSpeed = 0.5f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
        main.startColor = new Color(0.65f, 0.75f, 0.9f, 0.55f);
        main.maxParticles = 1200;
        main.gravityModifier = upward ? -1.4f : 1.4f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        var psr = go.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Stretch;
        psr.lengthScale = 6f;
        var mat = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
        mat.SetColor("_TintColor", new Color(0.4f, 0.5f, 0.65f, 0.4f));
        psr.material = mat;
        return go;
    }

    void SeatDoor(Door d, int slot)
    {
        d.slot = slot;
        Vector3 pos = SlotPos(slot);
        d.tf.SetPositionAndRotation(pos, Quaternion.LookRotation(-pos.normalized, Vector3.up));
        d.tracker.Reset();
    }

    void Update()
    {
        var cam = ObserverState.Cam;
        if (cam == null) return;
        if (!_atmosApplied)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.16f, 0.18f, 0.24f),
                fog: new Color(0.09f, 0.10f, 0.15f), fogDensity: 0.016f,
                background: new Color(0.05f, 0.06f, 0.10f));
            _atmosApplied = true;
        }

        // Doors that leave your view swap to a free perimeter slot (hidden ones preferred).
        foreach (var d in _doors)
        {
            var b = new Bounds(d.tf.position + Vector3.up * 1.7f, new Vector3(3f, 4f, 3f));
            d.tracker.Tick(b, out bool justLost, float.PositiveInfinity);
            if (!justLost) continue;

            var free = new List<int>();
            for (int s = 0; s < SlotCount; s++)
            {
                bool taken = false;
                foreach (var other in _doors) if (other.slot == s) taken = true;
                if (!taken) free.Add(s);
            }
            if (free.Count == 0) continue;
            var hidden = free.FindAll(s => !ObserverState.IsObserved(new Bounds(SlotPos(s) + Vector3.up * 1.7f, new Vector3(3f, 4f, 3f))));
            var pool = hidden.Count > 0 ? hidden : free;
            SeatDoor(d, pool[Random.Range(0, pool.Count)]);
        }
    }

    /// <summary>Wrong-door punishment: back to the fountain. No damage.</summary>
    public void LoopPlayerBack()
    {
        if (Time.time < _loopDebounceUntil) return;
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null) return;
        _loopDebounceUntil = Time.time + 1.5f;
        _player.Rigidbody.position = new Vector3(0f, 1.5f, -4f);
        _player.Rigidbody.velocity = Vector3.zero;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Plaza")]
    [Tooltip("Radius of the doorway ring.")]
    public float plazaRadius = 24f;

    [Header("Exit")]
    [Tooltip("Scene the down-rain door leads to.")]
    public string nextScene = "D28_LongTable";
}

/// <summary>Doorway trigger on every up-rain (wrong) door.</summary>
public class InvertedRainLoopTrigger : MonoBehaviour
{
    [HideInInspector] public InvertedRainController owner;

    void OnTriggerEnter(Collider other)
    {
        if (owner == null) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;
        owner.LoopPlayerBack();
    }
}
