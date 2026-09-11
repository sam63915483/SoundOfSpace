using TMPro;
using UnityEngine;

public class WoodPopup : MonoBehaviour
{
    public static void Spawn(Vector3 worldPos, int amount)
    {
        var go = new GameObject("WoodPopup");
        go.transform.position = worldPos;
        var p = go.AddComponent<WoodPopup>();
        p.Init(amount);
    }

    TextMeshPro tmp;
    float lifetime = 1.5f;
    float age;
    Vector3 upDir = Vector3.up;
    Camera _cam;          // cached Camera.main lookup
    const float FloatSpeed = 1.2f;

    void Init(int amount)
    {
        tmp = gameObject.AddComponent<TextMeshPro>();
        tmp.text = $"+{amount} wood";
        tmp.fontSize = 6f;
        tmp.color = new Color32(255, 200, 80, 255);
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.outlineWidth = 0.25f;
        tmp.outlineColor = Color.black;

        upDir = ComputeUpDirection();

        var planet = ClosestPlanet();
        if (planet != null)
            transform.SetParent(planet.transform, worldPositionStays: true);
        FaceCamera();
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

    // Face the camera NOW, not on the first Update. A freshly created label
    // renders its first frame with whatever rotation it was born with —
    // world-aligned, which on a planet surface is a random tilt — and only
    // Update straightens it, so the popup flashed diagonal for one frame
    // before snapping upright (Sam, 2026-09-11: "it glitches ... diagonal and
    // then corrects itself").
    void FaceCamera()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;
        Vector3 toCam = transform.position - _cam.transform.position;
        if (toCam.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(toCam.normalized, upDir);
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age >= lifetime || tmp == null) { Destroy(gameObject); return; }

        transform.position += upDir * FloatSpeed * Time.deltaTime;

        FaceCamera();

        float t = age / lifetime;
        var c = tmp.color;
        c.a = Mathf.Clamp01(1f - t * t);
        tmp.color = c;
    }
}
