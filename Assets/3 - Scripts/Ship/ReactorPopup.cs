using TMPro;
using UnityEngine;

// World-space "+N FUEL" floater spawned by ShipReactor on a successful refuel.
// Mirrors CrystalPopup's float-up + face-camera + alpha-fade pattern.
public class ReactorPopup : MonoBehaviour
{
    public static void Spawn(Vector3 worldPos, float fuelAdded)
    {
        var go = new GameObject("ReactorPopup");
        go.transform.position = worldPos;
        var p = go.AddComponent<ReactorPopup>();
        p.Init(fuelAdded);
    }

    TextMeshPro tmp;
    float lifetime = 1.5f;
    float age;
    Vector3 upDir = Vector3.up;
    Camera _cam;
    const float FloatSpeed = 1.2f;

    void Init(float fuelAdded)
    {
        tmp = gameObject.AddComponent<TextMeshPro>();
        tmp.text = $"+{Mathf.RoundToInt(fuelAdded)} FUEL";
        tmp.fontSize = 6f;
        tmp.color = new Color32(140, 230, 255, 255); // crystal cyan
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.outlineWidth = 0.25f;
        tmp.outlineColor = Color.black;
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
