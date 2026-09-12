using UnityEngine;

/// <summary>
/// The drunk sway: a slow Perlin roll / pitch / yaw laid over the camera's
/// final pose. Runs at order 120 — AFTER <see cref="CameraTransformFX"/> (100)
/// rewrites the camera every LateUpdate and BEFORE <see cref="ViewmodelMotor"/>
/// (150) reads the hold transform, so the held item sways with the view.
///
/// Purely additive on a pose something else sets fresh each frame (the FX
/// component in LateUpdate, or PlayerController's pitch write in Update when
/// the FX are off), so it never accumulates. Skipped while the solar map owns
/// the camera. Driven by <see cref="BeerBuzz"/>; <see cref="Amount"/> 0 = off.
/// </summary>
[DefaultExecutionOrder(120)]
public class BeerCameraWobble : MonoBehaviour
{
    [System.NonSerialized] public float Amount;        // 0-1
    [System.NonSerialized] public float MaxRollDeg = 5f;
    [System.NonSerialized] public float MaxPitchDeg = 2.5f;
    [System.NonSerialized] public float MaxYawDeg = 2.5f;
    [System.NonSerialized] public float Speed = 0.35f;  // cycles per second of the noise walk

    float _t;

    void LateUpdate()
    {
        if (Amount <= 0.0001f) return;
        if (SolarMap.IsOpen) return;

        _t += Time.deltaTime * Speed;
        float roll  = (Mathf.PerlinNoise(_t, 0.37f) - 0.5f) * 2f * MaxRollDeg  * Amount;
        float pitch = (Mathf.PerlinNoise(0.71f, _t * 0.8f) - 0.5f) * 2f * MaxPitchDeg * Amount;
        float yaw   = (Mathf.PerlinNoise(_t * 0.6f, 5.3f) - 0.5f) * 2f * MaxYawDeg   * Amount;
        transform.rotation = transform.rotation * Quaternion.Euler(pitch, yaw, roll);
    }
}
