using UnityEngine;

/// <summary>
/// Primitive-composed furniture/prop builders for the dimension polish pass.
/// Every builder returns a root GameObject whose pivot sits at FLOOR level
/// (+Z = the prop's facing) unless noted; parts are positioned in LOCAL space
/// so callers build first, then place/rotate the root freely (the D7
/// world-space-Block bug can't happen here). All parts are on the walkable
/// Body layer. Small decorative parts drop their colliders — and no part ever
/// non-uniformly scales a sphere/cylinder that keeps its collider (the
/// capsule-balloon trap).
/// </summary>
public static class DimensionPropKit
{
    // ── plumbing ─────────────────────────────────────────────────────

    static GameObject Root(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    static GameObject Part(PrimitiveType type, string name, Transform parent,
        Vector3 localPos, Vector3 scale, Material mat, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.layer = DimensionSceneUtil.WalkableLayer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider) Object.Destroy(go.GetComponent<Collider>());
        return go;
    }

    /// <summary>Find a named part (e.g. the "Screen" of a CrtMonitor) for later mat swaps.</summary>
    public static Renderer FindPart(GameObject root, string partName)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (r.name == partName) return r;
        return null;
    }

    // ── seating / tables ─────────────────────────────────────────────

    public static GameObject Couch(Transform parent, Material fabric, Material wood)
    {
        var root = Root("Couch", parent);
        Part(PrimitiveType.Cube, "Base", root.transform, new Vector3(0f, 0.25f, 0f), new Vector3(1.9f, 0.5f, 0.85f), fabric);
        Part(PrimitiveType.Cube, "Back", root.transform, new Vector3(0f, 0.72f, -0.32f), new Vector3(1.9f, 0.6f, 0.22f), fabric);
        Part(PrimitiveType.Cube, "ArmL", root.transform, new Vector3(-0.97f, 0.42f, 0f), new Vector3(0.22f, 0.34f, 0.85f), fabric);
        Part(PrimitiveType.Cube, "ArmR", root.transform, new Vector3(0.97f, 0.42f, 0f), new Vector3(0.22f, 0.34f, 0.85f), fabric);
        Part(PrimitiveType.Cube, "CushionL", root.transform, new Vector3(-0.45f, 0.55f, 0.06f), new Vector3(0.82f, 0.14f, 0.66f), fabric, false);
        Part(PrimitiveType.Cube, "CushionR", root.transform, new Vector3(0.45f, 0.55f, 0.06f), new Vector3(0.82f, 0.14f, 0.66f), fabric, false);
        Part(PrimitiveType.Cube, "FeetL", root.transform, new Vector3(-0.85f, 0.04f, 0.3f), new Vector3(0.08f, 0.08f, 0.08f), wood, false);
        Part(PrimitiveType.Cube, "FeetR", root.transform, new Vector3(0.85f, 0.04f, 0.3f), new Vector3(0.08f, 0.08f, 0.08f), wood, false);
        return root;
    }

    public static GameObject Armchair(Transform parent, Material fabric, Material wood)
    {
        var root = Root("Armchair", parent);
        Part(PrimitiveType.Cube, "Base", root.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.95f, 0.5f, 0.85f), fabric);
        Part(PrimitiveType.Cube, "Back", root.transform, new Vector3(0f, 0.75f, -0.32f), new Vector3(0.95f, 0.66f, 0.22f), fabric);
        Part(PrimitiveType.Cube, "ArmL", root.transform, new Vector3(-0.5f, 0.45f, 0f), new Vector3(0.2f, 0.4f, 0.85f), fabric);
        Part(PrimitiveType.Cube, "ArmR", root.transform, new Vector3(0.5f, 0.45f, 0f), new Vector3(0.2f, 0.4f, 0.85f), fabric);
        Part(PrimitiveType.Cube, "Cushion", root.transform, new Vector3(0f, 0.55f, 0.06f), new Vector3(0.72f, 0.14f, 0.66f), fabric, false);
        Part(PrimitiveType.Cube, "Feet", root.transform, new Vector3(0f, 0.04f, 0f), new Vector3(0.7f, 0.08f, 0.6f), wood, false);
        return root;
    }

    public static GameObject ChairSimple(Transform parent, Material wood)
    {
        var root = Root("Chair", parent);
        Part(PrimitiveType.Cube, "Seat", root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.45f, 0.06f, 0.45f), wood);
        Part(PrimitiveType.Cube, "Back", root.transform, new Vector3(0f, 0.75f, -0.2f), new Vector3(0.45f, 0.55f, 0.05f), wood);
        for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                Part(PrimitiveType.Cube, "Leg", root.transform, new Vector3(x * 0.18f, 0.21f, z * 0.18f), new Vector3(0.05f, 0.42f, 0.05f), wood, false);
        return root;
    }

    public static GameObject CoffeeTable(Transform parent, Material wood)
    {
        var root = Root("CoffeeTable", parent);
        Part(PrimitiveType.Cube, "Top", root.transform, new Vector3(0f, 0.42f, 0f), new Vector3(1.1f, 0.06f, 0.6f), wood);
        for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                Part(PrimitiveType.Cube, "Leg", root.transform, new Vector3(x * 0.48f, 0.2f, z * 0.24f), new Vector3(0.06f, 0.4f, 0.06f), wood, false);
        return root;
    }

    public static GameObject DiningTable(Transform parent, Material wood)
    {
        var root = Root("DiningTable", parent);
        Part(PrimitiveType.Cube, "Top", root.transform, new Vector3(0f, 0.74f, 0f), new Vector3(1.6f, 0.06f, 0.9f), wood);
        for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                Part(PrimitiveType.Cube, "Leg", root.transform, new Vector3(x * 0.7f, 0.36f, z * 0.36f), new Vector3(0.08f, 0.72f, 0.08f), wood, false);
        return root;
    }

    public static GameObject Bench(Transform parent, Material wood)
    {
        var root = Root("Bench", parent);
        Part(PrimitiveType.Cube, "Seat", root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(1.7f, 0.07f, 0.45f), wood);
        Part(PrimitiveType.Cube, "Back", root.transform, new Vector3(0f, 0.78f, -0.2f), new Vector3(1.7f, 0.5f, 0.06f), wood);
        Part(PrimitiveType.Cube, "LegL", root.transform, new Vector3(-0.7f, 0.22f, 0f), new Vector3(0.08f, 0.44f, 0.4f), wood);
        Part(PrimitiveType.Cube, "LegR", root.transform, new Vector3(0.7f, 0.22f, 0f), new Vector3(0.08f, 0.44f, 0.4f), wood);
        return root;
    }

    public static GameObject Desk(Transform parent, Material wood)
    {
        var root = Root("Desk", parent);
        Part(PrimitiveType.Cube, "Top", root.transform, new Vector3(0f, 0.74f, 0f), new Vector3(1.4f, 0.05f, 0.7f), wood);
        Part(PrimitiveType.Cube, "SideL", root.transform, new Vector3(-0.66f, 0.37f, 0f), new Vector3(0.06f, 0.72f, 0.66f), wood);
        Part(PrimitiveType.Cube, "SideR", root.transform, new Vector3(0.66f, 0.37f, 0f), new Vector3(0.06f, 0.72f, 0.66f), wood);
        Part(PrimitiveType.Cube, "Drawers", root.transform, new Vector3(0.42f, 0.45f, -0.05f), new Vector3(0.42f, 0.5f, 0.56f), wood, false);
        return root;
    }

    // ── lights / wall dressing ───────────────────────────────────────

    public static GameObject FloorLamp(Transform parent, Material stem, Material shadeMat,
        bool withLight, Color lightColor, float intensity = 1.1f, float range = 9f)
    {
        var root = Root("FloorLamp", parent);
        // Uniform x/z on cylinders so their capsule colliders stay sane; tiny parts drop colliders.
        Part(PrimitiveType.Cylinder, "Base", root.transform, new Vector3(0f, 0.03f, 0f), new Vector3(0.34f, 0.03f, 0.34f), stem, false);
        Part(PrimitiveType.Cylinder, "Pole", root.transform, new Vector3(0f, 0.75f, 0f), new Vector3(0.05f, 0.72f, 0.05f), stem);
        Part(PrimitiveType.Cylinder, "Shade", root.transform, new Vector3(0f, 1.52f, 0f), new Vector3(0.4f, 0.18f, 0.4f), shadeMat, false);
        if (withLight)
        {
            var lgo = new GameObject("LampLight");
            lgo.transform.SetParent(root.transform, false);
            lgo.transform.localPosition = new Vector3(0f, 1.45f, 0f);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = lightColor;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }
        return root;
    }

    /// <summary>Root pivot at the PICTURE CENTER, +Z pointing out of the wall —
    /// place it ON the wall at hanging height, facing into the room.</summary>
    public static GameObject Painting(Transform parent, Material frame, Material canvas,
        float width = 0.9f, float height = 0.65f)
    {
        var root = Root("Painting", parent);
        Part(PrimitiveType.Cube, "Frame", root.transform, new Vector3(0f, 0f, 0.02f), new Vector3(width, height, 0.05f), frame, false);
        Part(PrimitiveType.Cube, "Canvas", root.transform, new Vector3(0f, 0f, 0.055f), new Vector3(width - 0.1f, height - 0.1f, 0.01f), canvas, false);
        return root;
    }

    /// <summary>Analog wall clock; same wall pivot convention as Painting. Hands are
    /// set from the given time — hang several with DIFFERENT wrong times.</summary>
    public static GameObject WallClock(Transform parent, Material faceMat, Material handMat,
        float hour, float minute)
    {
        var root = Root("WallClock", parent);
        var face = Part(PrimitiveType.Cylinder, "Face", root.transform, Vector3.zero,
            new Vector3(0.42f, 0.015f, 0.42f), faceMat, false);
        face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        float hourAng = (hour % 12f + minute / 60f) * 30f;
        float minAng = minute * 6f;
        var hh = Part(PrimitiveType.Cube, "HourHand", root.transform, new Vector3(0f, 0f, 0.03f),
            new Vector3(0.03f, 0.12f, 0.01f), handMat, false);
        hh.transform.localRotation = Quaternion.Euler(0f, 0f, -hourAng);
        hh.transform.localPosition += hh.transform.up * 0.05f;
        var mh = Part(PrimitiveType.Cube, "MinuteHand", root.transform, new Vector3(0f, 0f, 0.035f),
            new Vector3(0.02f, 0.17f, 0.01f), handMat, false);
        mh.transform.localRotation = Quaternion.Euler(0f, 0f, -minAng);
        mh.transform.localPosition += mh.transform.up * 0.07f;
        return root;
    }

    // ── storage / clutter ────────────────────────────────────────────

    public static GameObject Shelf(Transform parent, Material wood, Material booksMat,
        float width = 1.2f, float height = 2f, int shelves = 4)
    {
        var root = Root("Shelf", parent);
        Part(PrimitiveType.Cube, "SideL", root.transform, new Vector3(-width * 0.5f, height * 0.5f, 0f), new Vector3(0.05f, height, 0.32f), wood);
        Part(PrimitiveType.Cube, "SideR", root.transform, new Vector3(width * 0.5f, height * 0.5f, 0f), new Vector3(0.05f, height, 0.32f), wood);
        Part(PrimitiveType.Cube, "BackPanel", root.transform, new Vector3(0f, height * 0.5f, -0.15f), new Vector3(width, height, 0.03f), wood, false);
        for (int i = 0; i < shelves; i++)
        {
            float y = 0.25f + i * (height - 0.4f) / (shelves - 1);
            Part(PrimitiveType.Cube, "Plank", root.transform, new Vector3(0f, y, 0f), new Vector3(width, 0.04f, 0.3f), wood, false);
            if (booksMat != null && Random.value < 0.85f)
                Part(PrimitiveType.Cube, "Books", root.transform,
                    new Vector3(Random.Range(-0.12f, 0.12f), y + 0.16f, 0.01f),
                    new Vector3(width * Random.Range(0.55f, 0.95f), 0.28f, 0.24f), booksMat, false);
        }
        return root;
    }

    public static GameObject BookStack(Transform parent, Material booksMat, int count = 4)
    {
        var root = Root("BookStack", parent);
        float y = 0f;
        for (int i = 0; i < count; i++)
        {
            float h = Random.Range(0.035f, 0.06f);
            var b = Part(PrimitiveType.Cube, "Book", root.transform,
                new Vector3(Random.Range(-0.02f, 0.02f), y + h * 0.5f, Random.Range(-0.02f, 0.02f)),
                new Vector3(Random.Range(0.2f, 0.3f), h, Random.Range(0.15f, 0.22f)), booksMat, false);
            b.transform.localRotation = Quaternion.Euler(0f, Random.Range(-14f, 14f), 0f);
            y += h;
        }
        return root;
    }

    public static GameObject Crate(Transform parent, Material mat, float size = 0.8f)
    {
        var root = Root("Crate", parent);
        Part(PrimitiveType.Cube, "Box", root.transform, new Vector3(0f, size * 0.5f, 0f), new Vector3(size, size, size), mat);
        return root;
    }

    public static GameObject Mug(Transform parent, Material mat)
    {
        var root = Root("Mug", parent);
        Part(PrimitiveType.Cylinder, "Cup", root.transform, new Vector3(0f, 0.05f, 0f), new Vector3(0.09f, 0.05f, 0.09f), mat, false);
        return root;
    }

    public static GameObject PottedPlant(Transform parent, Material pot, Material plant)
    {
        var root = Root("PottedPlant", parent);
        Part(PrimitiveType.Cylinder, "Pot", root.transform, new Vector3(0f, 0.16f, 0f), new Vector3(0.34f, 0.16f, 0.34f), pot);
        // Foliage = uniformly scaled spheres (collider dropped anyway).
        for (int i = 0; i < 4; i++)
        {
            float s = Random.Range(0.25f, 0.42f);
            Part(PrimitiveType.Sphere, "Leaf", root.transform,
                new Vector3(Random.Range(-0.1f, 0.1f), 0.5f + i * 0.16f, Random.Range(-0.1f, 0.1f)),
                new Vector3(s, s, s), plant, false);
        }
        return root;
    }

    // ── office / tech ────────────────────────────────────────────────

    /// <summary>CRT with a swappable emissive "Screen" child (use FindPart(root, "Screen")).</summary>
    public static GameObject CrtMonitor(Transform parent, Material body, Material screen)
    {
        var root = Root("CrtMonitor", parent);
        Part(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, 0.21f, -0.05f), new Vector3(0.42f, 0.38f, 0.42f), body, false);
        Part(PrimitiveType.Cube, "Screen", root.transform, new Vector3(0f, 0.22f, 0.17f), new Vector3(0.34f, 0.28f, 0.02f), screen, false);
        return root;
    }

    public static GameObject WaterCooler(Transform parent, Material body, Material bottle)
    {
        var root = Root("WaterCooler", parent);
        Part(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.38f, 1.1f, 0.38f), body);
        Part(PrimitiveType.Cylinder, "Bottle", root.transform, new Vector3(0f, 1.32f, 0f), new Vector3(0.3f, 0.22f, 0.3f), bottle, false);
        return root;
    }

    public static GameObject Candlestick(Transform parent, Material metal, Material flameMat)
    {
        var root = Root("Candlestick", parent);
        Part(PrimitiveType.Cylinder, "Base", root.transform, new Vector3(0f, 0.02f, 0f), new Vector3(0.14f, 0.02f, 0.14f), metal, false);
        Part(PrimitiveType.Cylinder, "Stem", root.transform, new Vector3(0f, 0.16f, 0f), new Vector3(0.03f, 0.14f, 0.03f), metal, false);
        Part(PrimitiveType.Cylinder, "Candle", root.transform, new Vector3(0f, 0.36f, 0f), new Vector3(0.05f, 0.08f, 0.05f), metal, false);
        var flame = Part(PrimitiveType.Sphere, "Flame", root.transform, new Vector3(0f, 0.47f, 0f), new Vector3(0.05f, 0.05f, 0.05f), flameMat, false);
        flame.transform.localScale = new Vector3(0.04f, 0.07f, 0.04f); // no collider → free to stretch
        return root;
    }
}
