using TMPro;
using UnityEngine;

public class CrystalPopup : MonoBehaviour
{
    public static void Spawn(Vector3 worldPos, int amount)
    {
        var go = new GameObject("CrystalPopup");
        go.transform.position = worldPos;
        var p = go.AddComponent<CrystalPopup>();
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
        tmp.text = $"+{amount} crystal";
        tmp.fontSize = 6f;
        tmp.color = new Color32(140, 230, 255, 255);
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.outlineWidth = 0.25f;
        tmp.outlineColor = Color.black;

        upDir = ComputeUpDirection();

        var planet = ClosestPlanet();
        if (planet != null)
            transform.SetParent(planet.transform, worldPositionStays: true);
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
