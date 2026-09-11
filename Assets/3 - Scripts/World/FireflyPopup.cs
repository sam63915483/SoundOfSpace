using TMPro;
using UnityEngine;

/// <summary>
/// "+1 firefly" floating over the spot you caught it, in the firefly's own
/// orange. Same shape as CrystalPopup / WoodPopup: a world-space TMP label,
/// parented to the nearest planet so it rides the orbit, drifting up and
/// fading over 1.5 s.
/// </summary>
public class FireflyPopup : MonoBehaviour
{
    public static void Spawn(Vector3 worldPos, int amount)
    {
        var go = new GameObject("FireflyPopup");
        go.transform.position = worldPos;
        var p = go.AddComponent<FireflyPopup>();
        p.Init(amount);
    }

    TextMeshPro tmp;
    float lifetime = 1.5f;
    float age;
    Vector3 upDir = Vector3.up;
    Camera _cam;
    const float FloatSpeed = 1.2f;

    void Init(int amount)
    {
        tmp = gameObject.AddComponent<TextMeshPro>();
        tmp.text = amount == 1 ? "+1 firefly" : $"+{amount} fireflies";
        tmp.fontSize = 6f;
        tmp.color = new Color32(255, 190, 80, 255);
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.outlineWidth = 0.25f;
        tmp.outlineColor = Color.black;

        var planet = ClosestPlanet();
        if (planet != null)
        {
            upDir = (transform.position - planet.Position).normalized;
            transform.SetParent(planet.transform, worldPositionStays: true);
        }
        FaceCamera();
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
