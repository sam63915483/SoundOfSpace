using TMPro;
using UnityEngine;

/// <summary>
/// The "+3 red mushroom" float-up on pickup — the mushroom twin of WoodPopup /
/// CrystalPopup, so the core economy loop gets the same feedback wood already
/// has. Named by SPECIES rather than by resource, since species is what the
/// player is actually collecting.
/// </summary>
public class MushroomPopup : MonoBehaviour
{
    public static void Spawn(Vector3 worldPos, int amount, string speciesKey, bool isSapling)
    {
        var go = new GameObject("MushroomPopup");
        go.transform.position = worldPos;
        var p = go.AddComponent<MushroomPopup>();
        p.Init(amount, speciesKey, isSapling);
    }

    TextMeshPro tmp;
    float lifetime = 1.5f;
    float age;
    Vector3 upDir = Vector3.up;
    Camera _cam;
    const float FloatSpeed = 1.2f;

    void Init(int amount, string speciesKey, bool isSapling)
    {
        string name = MushroomRegistry.DisplayName(speciesKey);
        tmp = gameObject.AddComponent<TextMeshPro>();
        tmp.text = isSapling ? $"+{amount} {name} spores" : $"+{amount} {name}";
        tmp.fontSize = 6f;
        tmp.color = isSapling ? new Color32(200, 155, 230, 255) : new Color32(235, 120, 125, 255);
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
