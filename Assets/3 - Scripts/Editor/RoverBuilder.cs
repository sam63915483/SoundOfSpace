using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the rover out of primitives (Tools ▸ Solar System ▸ Rover ▸ Build
/// Rover Prefab): one Rigidbody root, chassis + hood + dash + two seats +
/// roll bar + truck bed, four wheel hardpoints with visual wheel pivots, the
/// driver's head anchor, the exit spot and a single headlight. Saves
/// Assets/1 - samsPrefabs/Rover.prefab and drops two instances into the open
/// gameplay scene: one next to where the Player object stands, one near the
/// village pool table on Humble Abode. Re-running replaces both instances.
///
/// Every part is rover-local metres; RoverController's physics reads the
/// hardpoints, so moving them in the prefab retunes the stance.
/// </summary>
public static class RoverBuilder
{
    const string PrefabPath = "Assets/1 - samsPrefabs/Rover.prefab";
    const string MatFolder = "Assets/2 - Materials/Rover";

    [MenuItem("Tools/Solar System/Rover/Build Rover Prefab + Place In Scene")]
    public static void BuildMenu() { Debug.Log(Build()); }

    public static string Build()
    {
        var log = new StringBuilder();
        var mats = BuildMaterials();
        var root = BuildHierarchy(mats);

        // Save the prefab (replacing any previous one) and connect this instance.
        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
        log.AppendLine("Prefab saved: " + PrefabPath);

        // Place: replace previous instances by name.
        var scene = EditorSceneManager.GetActiveScene();
        int placed = 0;
        placed += Place(prefab, root, "Rover_Start", StartSpot(log), log) ? 1 : 0;
        placed += Place(prefab, null, "Rover_Village", VillageSpot(log), log) ? 1 : 0;
        if (placed == 0) Object.DestroyImmediate(root);
        EditorSceneManager.MarkSceneDirty(scene);
        log.AppendLine($"Placed {placed} rover(s) in {scene.name}");
        return log.ToString();
    }

    // ── placement ───────────────────────────────────────────────────────────

    struct Spot { public bool ok; public Vector3 pos; public Quaternion rot; }

    static CelestialBody NearestBody(Vector3 p)
    {
        CelestialBody best = null; float bd = float.MaxValue;
        foreach (var b in Object.FindObjectsOfType<CelestialBody>())
        {
            float d = (b.transform.position - p).magnitude - b.radius;
            if (d < bd) { bd = d; best = b; }
        }
        return best;
    }

    static Spot SpotNear(Vector3 origin, Vector3 forwardHint, float ahead, StringBuilder log, string label)
    {
        var s = new Spot();
        var body = NearestBody(origin);
        if (body == null) { log.AppendLine(label + ": no CelestialBody in scene"); return s; }
        Vector3 up = (origin - body.transform.position).normalized;
        Vector3 fwd = Vector3.ProjectOnPlane(forwardHint, up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
        fwd.Normalize();
        // Sit on the editor's sphere radius + 2 m; the runtime settle drops it onto the real terrain.
        Vector3 surface = body.transform.position + up * (body.radius + 2f);
        Vector3 tangentOrigin = surface + Vector3.ProjectOnPlane(origin - surface, up);
        s.pos = tangentOrigin + fwd * ahead;
        // Re-project so the distance from the planet centre stays radius + 2 on the curve.
        Vector3 dir = (s.pos - body.transform.position).normalized;
        s.pos = body.transform.position + dir * (body.radius + 2f);
        s.rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(fwd, dir).normalized, dir);
        s.ok = true;
        log.AppendLine($"{label}: {body.name} at {s.pos}");
        return s;
    }

    static Spot StartSpot(StringBuilder log)
    {
        var pc = Object.FindObjectOfType<PlayerController>(true);
        if (pc == null) { log.AppendLine("Rover_Start: no PlayerController in scene"); return new Spot(); }
        return SpotNear(pc.transform.position, pc.transform.forward, 7f, log, "Rover_Start");
    }

    static Spot VillageSpot(StringBuilder log)
    {
        var table = Object.FindObjectOfType<PoolTable>(true);
        if (table == null) { log.AppendLine("Rover_Village: no PoolTable in scene"); return new Spot(); }
        return SpotNear(table.transform.position, table.transform.right, 12f, log, "Rover_Village");
    }

    static bool Place(GameObject prefab, GameObject existingInstance, string name, Spot spot, StringBuilder log)
    {
        var old = GameObject.Find(name);
        if (old != null && old != existingInstance) Object.DestroyImmediate(old);
        if (!spot.ok) { if (existingInstance != null) Object.DestroyImmediate(existingInstance); return false; }
        GameObject go = existingInstance != null ? existingInstance : (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = name;
        go.transform.SetParent(null, true);
        go.transform.SetPositionAndRotation(spot.pos, spot.rot);
        return true;
    }

    // ── materials ───────────────────────────────────────────────────────────

    class Mats { public Material body, dark, tyre, hub, seat, lamp, chrome; }

    static Mats BuildMaterials()
    {
        if (!AssetDatabase.IsValidFolder("Assets/2 - Materials")) AssetDatabase.CreateFolder("Assets", "2 - Materials");
        if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder("Assets/2 - Materials", "Rover");
        var m = new Mats
        {
            body = Mat("Rover_Body", new Color(0.86f, 0.42f, 0.10f), 0.35f, 0.15f),
            dark = Mat("Rover_Dark", new Color(0.13f, 0.13f, 0.14f), 0.45f, 0.30f),
            tyre = Mat("Rover_Tyre", new Color(0.06f, 0.06f, 0.06f), 0.20f, 0.0f),
            hub = Mat("Rover_Hub", new Color(0.58f, 0.60f, 0.63f), 0.70f, 0.75f),
            seat = Mat("Rover_Seat", new Color(0.27f, 0.19f, 0.12f), 0.25f, 0.0f),
            chrome = Mat("Rover_Chrome", new Color(0.75f, 0.76f, 0.78f), 0.80f, 0.90f),
            lamp = Mat("Rover_Lamp", new Color(1f, 0.95f, 0.8f), 0.9f, 0.0f),
        };
        m.lamp.EnableKeyword("_EMISSION");
        m.lamp.SetColor("_EmissionColor", new Color(1f, 0.92f, 0.7f) * 2.5f);
        m.lamp.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(m.lamp);
        AssetDatabase.SaveAssets();
        return m;
    }

    static Material Mat(string name, Color c, float smooth, float metal)
    {
        string path = $"{MatFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.color = c;
        mat.SetFloat("_Glossiness", smooth);
        mat.SetFloat("_Metallic", metal);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ── hierarchy ───────────────────────────────────────────────────────────

    static GameObject BuildHierarchy(Mats m)
    {
        var root = new GameObject("Rover");
        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 800f;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        var ctrl = root.AddComponent<RoverController>();
        Transform t = root.transform;

        // Body
        Prim(t, "Chassis", PrimitiveType.Cube, new Vector3(0, 0.38f, 0), Vector3.zero, new Vector3(1.5f, 0.45f, 3.0f), m.body, true);
        Prim(t, "Hood", PrimitiveType.Cube, new Vector3(0, 0.70f, 1.05f), Vector3.zero, new Vector3(1.4f, 0.30f, 0.9f), m.body, true);
        Prim(t, "Grille", PrimitiveType.Cube, new Vector3(0, 0.50f, 1.52f), Vector3.zero, new Vector3(1.3f, 0.26f, 0.08f), m.dark, false);
        Prim(t, "Bumper", PrimitiveType.Cube, new Vector3(0, 0.30f, 1.58f), Vector3.zero, new Vector3(1.6f, 0.12f, 0.12f), m.chrome, true);
        Prim(t, "Dash", PrimitiveType.Cube, new Vector3(0, 0.86f, 0.70f), Vector3.zero, new Vector3(1.4f, 0.26f, 0.32f), m.dark, false);
        Prim(t, "Floor", PrimitiveType.Cube, new Vector3(0, 0.62f, 0.15f), Vector3.zero, new Vector3(1.4f, 0.04f, 1.1f), m.dark, false);
        // Fenders over each wheel
        foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f })
            Prim(t, "Fender", PrimitiveType.Cube, new Vector3(sx * 1.0f, 0.55f, sz * 1.15f), Vector3.zero, new Vector3(0.55f, 0.12f, 1.05f), m.dark, false);

        // Headlights: two lamp blocks + ONE real spot light (pixel-light budget).
        Prim(t, "LampL", PrimitiveType.Cube, new Vector3(-0.5f, 0.74f, 1.51f), Vector3.zero, new Vector3(0.24f, 0.14f, 0.06f), m.lamp, false);
        Prim(t, "LampR", PrimitiveType.Cube, new Vector3(0.5f, 0.74f, 1.51f), Vector3.zero, new Vector3(0.24f, 0.14f, 0.06f), m.lamp, false);
        var lightGo = new GameObject("Headlight");
        lightGo.transform.SetParent(t, false);
        lightGo.transform.localPosition = new Vector3(0, 0.78f, 1.45f);
        lightGo.transform.localRotation = Quaternion.Euler(8f, 0, 0);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Spot;
        light.spotAngle = 70f;
        light.range = 45f;
        light.intensity = 2.2f;
        light.color = new Color(1f, 0.95f, 0.82f);
        light.shadows = LightShadows.None;
        light.enabled = false;
        ctrl.headlights = new[] { light };

        // Seats (driver = left)
        var driverCushion = Prim(t, "DriverSeat", PrimitiveType.Cube, new Vector3(-0.42f, 0.72f, 0.12f), Vector3.zero, new Vector3(0.62f, 0.16f, 0.62f), m.seat, true);
        Prim(t, "DriverBack", PrimitiveType.Cube, new Vector3(-0.42f, 1.06f, -0.20f), new Vector3(-8f, 0, 0), new Vector3(0.62f, 0.62f, 0.12f), m.seat, true);
        Prim(t, "PassengerSeat", PrimitiveType.Cube, new Vector3(0.42f, 0.72f, 0.12f), Vector3.zero, new Vector3(0.62f, 0.16f, 0.62f), m.seat, true);
        Prim(t, "PassengerBack", PrimitiveType.Cube, new Vector3(0.42f, 1.06f, -0.20f), new Vector3(-8f, 0, 0), new Vector3(0.62f, 0.62f, 0.12f), m.seat, true);
        var seat = driverCushion.AddComponent<RoverSeat>();
        seat.rover = ctrl;
        seat.gazeTarget = driverCushion.transform;
        seat.interactMessage = "#set from script#";
        ctrl.seat = seat;

        // Steering wheel + column
        Prim(t, "SteeringWheel", PrimitiveType.Cylinder, new Vector3(-0.42f, 0.98f, 0.62f), new Vector3(-65f, 0, 0), new Vector3(0.36f, 0.015f, 0.36f), m.dark, false);
        Prim(t, "SteeringColumn", PrimitiveType.Cylinder, new Vector3(-0.42f, 0.93f, 0.70f), new Vector3(-65f, 0, 0), new Vector3(0.05f, 0.12f, 0.05f), m.chrome, false);

        // Roll bar behind the seats
        Prim(t, "RollBarL", PrimitiveType.Cylinder, new Vector3(-0.68f, 1.12f, -0.52f), Vector3.zero, new Vector3(0.07f, 0.55f, 0.07f), m.chrome, false);
        Prim(t, "RollBarR", PrimitiveType.Cylinder, new Vector3(0.68f, 1.12f, -0.52f), Vector3.zero, new Vector3(0.07f, 0.55f, 0.07f), m.chrome, false);
        Prim(t, "RollBarTop", PrimitiveType.Cylinder, new Vector3(0, 1.67f, -0.52f), new Vector3(0, 0, 90f), new Vector3(0.07f, 0.72f, 0.07f), m.chrome, false);

        // Truck bed
        Prim(t, "BedFloor", PrimitiveType.Cube, new Vector3(0, 0.64f, -0.98f), Vector3.zero, new Vector3(1.5f, 0.08f, 1.12f), m.dark, true);
        Prim(t, "BedWallL", PrimitiveType.Cube, new Vector3(-0.72f, 0.84f, -0.98f), Vector3.zero, new Vector3(0.06f, 0.36f, 1.12f), m.body, true);
        Prim(t, "BedWallR", PrimitiveType.Cube, new Vector3(0.72f, 0.84f, -0.98f), Vector3.zero, new Vector3(0.06f, 0.36f, 1.12f), m.body, true);
        Prim(t, "BedFront", PrimitiveType.Cube, new Vector3(0, 0.84f, -0.45f), Vector3.zero, new Vector3(1.5f, 0.36f, 0.06f), m.body, true);
        Prim(t, "Tailgate", PrimitiveType.Cube, new Vector3(0, 0.84f, -1.51f), Vector3.zero, new Vector3(1.5f, 0.36f, 0.06f), m.body, true);
        Prim(t, "TailLampL", PrimitiveType.Cube, new Vector3(-0.6f, 0.78f, -1.55f), Vector3.zero, new Vector3(0.18f, 0.10f, 0.04f), m.lamp, false);
        Prim(t, "TailLampR", PrimitiveType.Cube, new Vector3(0.6f, 0.78f, -1.55f), Vector3.zero, new Vector3(0.18f, 0.10f, 0.04f), m.lamp, false);

        // Wheels: hardpoint (physics) + visual pivot (steer/spin) + tyre + hub + spokes
        string[] names = { "FL", "FR", "RL", "RR" };
        Vector3[] hp = {
            new Vector3(-1.0f, 0.25f,  1.15f), new Vector3(1.0f, 0.25f,  1.15f),
            new Vector3(-1.0f, 0.25f, -1.15f), new Vector3(1.0f, 0.25f, -1.15f),
        };
        ctrl.wheelHardpoints = new Transform[4];
        ctrl.wheelVisuals = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            var h = new GameObject("HP_" + names[i]);
            h.transform.SetParent(t, false);
            h.transform.localPosition = hp[i];
            ctrl.wheelHardpoints[i] = h.transform;

            var pivot = new GameObject("Wheel_" + names[i]);
            pivot.transform.SetParent(t, false);
            pivot.transform.localPosition = hp[i] - Vector3.up * ctrl.restLength;
            ctrl.wheelVisuals[i] = pivot.transform;

            float r = ctrl.wheelRadius;
            Prim(pivot.transform, "Tyre", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0, 0, 90f), new Vector3(r * 2f, 0.21f, r * 2f), m.tyre, false);
            Prim(pivot.transform, "Hub", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0, 0, 90f), new Vector3(r * 1.1f, 0.215f, r * 1.1f), m.hub, false);
            Prim(pivot.transform, "SpokeA", PrimitiveType.Cube, Vector3.zero, Vector3.zero, new Vector3(0.44f, r * 1.7f, 0.10f), m.dark, false);
            Prim(pivot.transform, "SpokeB", PrimitiveType.Cube, Vector3.zero, Vector3.zero, new Vector3(0.44f, 0.10f, r * 1.7f), m.dark, false);
            // Tread blocks around the rim so the spin reads from any angle.
            for (int k = 0; k < 8; k++)
            {
                float a = k * 45f;
                var block = Prim(pivot.transform, "Tread", PrimitiveType.Cube, Vector3.zero, new Vector3(a, 0, 0), new Vector3(0.44f, 0.12f, 0.22f), m.tyre, false);
                block.transform.localPosition = Quaternion.Euler(a, 0, 0) * new Vector3(0, r * 0.98f, 0);
            }
        }

        // Driver eye + exit spot
        var head = new GameObject("DriverHead");
        head.transform.SetParent(t, false);
        head.transform.localPosition = new Vector3(-0.42f, 1.42f, 0.22f);
        ctrl.camViewPoint = head.transform;
        var exit = new GameObject("ExitPoint");
        exit.transform.SetParent(t, false);
        exit.transform.localPosition = new Vector3(-1.75f, 0.9f, 0.12f);
        ctrl.exitPoint = exit.transform;

        return root;
    }

    static GameObject Prim(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 euler, Vector3 scale, Material mat, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!keepCollider)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
        }
        return go;
    }
}
