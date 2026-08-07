using UnityEngine;

/// Faces a world-space nametag toward the local camera. Camera is cached and
/// lazily re-found (throttled) — the local player's camera can be created
/// after this tag when spawn order varies.
public class NametagBillboard : MonoBehaviour
{
    Camera cam;
    float nextFindTime;

    void LateUpdate()
    {
        if (cam == null || !cam.isActiveAndEnabled)
        {
            if (Time.unscaledTime < nextFindTime) return;
            nextFindTime = Time.unscaledTime + 0.5f;
            cam = Camera.main;
            if (cam == null) return;
        }
        transform.rotation = Quaternion.LookRotation(
            transform.position - cam.transform.position, cam.transform.up);
    }
}
