using UnityEngine;

// TEMPORARY trailer-diagnostic helper. Drives the player's OWN look pipeline
// (PlayerController.ForceLookAt) to aim at the sun, so the lens flare renders
// exactly as it does in normal play — no frame-fighting. Safe to delete.
public class _TrailerSunAimer : MonoBehaviour
{
    PlayerController _pc;
    CelestialBody _sun;
    Camera _cam;
    public float lastDot = -2f;

    void LateUpdate()
    {
        if (_pc == null) _pc = FindObjectOfType<PlayerController>();
        if (_sun == null)
        {
            var bodies = NBodySimulation.Bodies;
            if (bodies != null)
                for (int i = 0; i < bodies.Length; i++)
                    if (bodies[i] != null && bodies[i].bodyType == CelestialBody.BodyType.Sun) { _sun = bodies[i]; break; }
        }
        if (_cam == null)
        {
            var mgr = CameraEffectsManager.Instance;
            if (mgr != null && mgr.PlayerCamera != null) _cam = mgr.PlayerCamera;
            if (_cam == null) _cam = Camera.main;
        }
        if (_pc == null || _sun == null) return;

        _pc.ForceLookAt(_sun.Position);

        if (_cam != null)
        {
            Vector3 toSun = (_sun.Position - _cam.transform.position).normalized;
            lastDot = Vector3.Dot(_cam.transform.forward, toSun);
        }
    }
}
