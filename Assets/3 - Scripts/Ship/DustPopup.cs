using TMPro;
using UnityEngine;

public class DustPopup : MonoBehaviour
{
    /// <summary>
    /// Spawn at a world position. If a `followTarget` is provided, the popup
    /// parents to it so it inherits the ship's orbital motion — without
    /// this, an orbiting ship leaves the popup behind in a frame because
    /// the popup is body-relative. Pass the SpaceNet's transform (or any
    /// ship-attached transform) for the collect-dust UX.
    /// </summary>
    public static void Spawn(Vector3 worldPos, int amount, Transform followTarget = null)
    {
        var go = new GameObject("DustPopup");
        go.transform.position = worldPos;
        var p = go.AddComponent<DustPopup>();
        p.Init(amount, followTarget);
    }

    TextMeshPro tmp;
    float lifetime = 1.5f;
    float age;
    Vector3 upDir = Vector3.up;
    Camera _cam;
    const float FloatSpeed = 1.2f;

    void Init(int amount, Transform followTarget)
    {
        tmp = gameObject.AddComponent<TextMeshPro>();
        tmp.text = $"+{amount} dust";
        tmp.fontSize = 6f;
        tmp.color = new Color32(184, 140, 255, 255);
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.outlineWidth = 0.25f;
        tmp.outlineColor = Color.black;

        upDir = ComputeUpDirection();
        // Prefer following the caller's transform (e.g. the SpaceNet on a
        // ship). That keeps the popup glued to the ship as it moves — an
        // orbiting ship moves so fast that a planet-parented popup would
        // streak across the screen and out of view in well under a second.
        if (followTarget != null)
        {
            transform.SetParent(followTarget, worldPositionStays: true);
        }
        else
        {
            var planet = ClosestPlanet();
            if (planet != null)
                transform.SetParent(planet.transform, worldPositionStays: true);
        }
    }

    Vector3 ComputeUpDirection()
    {
        var planet = ClosestPlanet();
        if (planet == null) return Vector3.up;
        return (transform.position - planet.Position).normalized;
    }

    CelestialBody ClosestPlanet()
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        CelestialBody closest = null;
        float bestSq = float.MaxValue;
        foreach (var b in bodies)
        {
            if (b == null) continue;
            float d = (b.Position - transform.position).sqrMagnitude;
            if (d < bestSq) { bestSq = d; closest = b; }
        }
        return closest;
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age >= lifetime || tmp == null) { Destroy(gameObject); return; }

        transform.position += upDir * FloatSpeed * Time.deltaTime;

        if (_cam == null) _cam = Camera.main;
        if (_cam != null)
        {
            Vector3 toCam = transform.position - _cam.transform.position;
            if (toCam.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(toCam.normalized, upDir);
        }

        float t = age / lifetime;
        var c = tmp.color;
        c.a = Mathf.Clamp01(1f - t * t);
        tmp.color = c;
    }
}
