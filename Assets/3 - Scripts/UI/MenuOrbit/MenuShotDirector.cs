using UnityEngine;

/// <summary>
/// MENU-ONLY (MenuOrbit scene): drives the main camera like a lazy documentary
/// director — hard cuts every few seconds between shuttle-relative shots
/// (chase, side flyby, wide planet frame, close orbit, front reveal), each with
/// its own FOV and a slow drift/zoom inside the shot. Everything is computed
/// relative to the shuttle each LateUpdate, so floating-origin shifts are
/// invisible. Added at runtime by MenuOrbitBootstrap.
/// </summary>
public class MenuShotDirector : MonoBehaviour
{
    public MenuShuttleTour tour;
    public Camera cam;

    enum Shot { Chase, SideFlyby, WidePlanet, CloseOrbit, FrontReveal }
    Shot shot;
    Shot lastShot;
    float shotClock, shotDuration;
    float fovFrom, fovTo;
    float side = 1f;
    float seed;

    System.Random rng = new System.Random();

    void Start()
    {
        if (cam == null) cam = Camera.main;
        if (tour == null) tour = FindObjectOfType<MenuShuttleTour>();
        if (cam == null || tour == null) { enabled = false; return; }
        NextShot();
        // First frame: snap straight to the shot pose (no lerp from wherever
        // the camera happened to be).
        Apply(1f, true);
    }

    void NextShot()
    {
        Shot pick;
        do { pick = (Shot)rng.Next(0, 5); } while (pick == lastShot);
        lastShot = shot = pick;
        shotClock = 0f;
        shotDuration = Mathf.Lerp(6f, 12f, (float)rng.NextDouble());
        side = rng.Next(0, 2) == 0 ? -1f : 1f;
        seed = (float)rng.NextDouble() * 10f;

        switch (shot)
        {
            case Shot.Chase:       fovFrom = 62f; fovTo = 55f; break;
            case Shot.SideFlyby:   fovFrom = 42f; fovTo = 36f; break;
            case Shot.WidePlanet:  fovFrom = 48f; fovTo = 56f; break;
            case Shot.CloseOrbit:  fovFrom = 34f; fovTo = 30f; break;
            case Shot.FrontReveal: fovFrom = 50f; fovTo = 44f; break;
        }
    }

    void LateUpdate()
    {
        shotClock += Time.deltaTime;
        if (shotClock >= shotDuration) { NextShot(); Apply(1f, true); return; }
        Apply(Time.deltaTime * 3f, false);
        cam.fieldOfView = Mathf.Lerp(fovFrom, fovTo, shotClock / shotDuration);
    }

    void Apply(float lerp, bool snap)
    {
        Transform sh = tour.transform;
        var body = tour.FocusBody;
        if (body == null) return;

        Vector3 up = (sh.position - body.Position).normalized;      // radial out
        Vector3 fwd = sh.forward;
        Vector3 right = Vector3.Cross(up, fwd).normalized;
        float drift = Mathf.Sin(Time.time * 0.35f + seed);          // slow sway

        Vector3 pos;
        Vector3 lookTarget = sh.position;

        switch (shot)
        {
            case Shot.Chase:
                pos = sh.position - fwd * 26f + up * 9f + right * (4f * drift);
                break;
            case Shot.SideFlyby:
                pos = sh.position + right * side * 55f + up * (6f + 3f * drift) + fwd * (shotClock - shotDuration * 0.5f) * -2.5f;
                break;
            case Shot.WidePlanet:
                // Stand off so the planet fills the background behind the shuttle.
                pos = sh.position + (sh.position - body.Position).normalized * (body.radius * 0.9f) + right * side * 30f;
                lookTarget = Vector3.Lerp(sh.position, body.Position, 0.15f);
                break;
            case Shot.CloseOrbit:
                float ang = Time.time * 0.25f + seed;
                pos = sh.position + (right * Mathf.Cos(ang) + up * Mathf.Sin(ang) * 0.4f) * 14f + fwd * 6f * drift;
                break;
            default: // FrontReveal
                pos = sh.position + fwd * 30f + up * (5f + 2f * drift) + right * side * 8f;
                break;
        }

        var rot = Quaternion.LookRotation((lookTarget - pos).normalized, up);
        if (snap)
        {
            transform.SetPositionAndRotation(pos, rot);
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, pos, lerp);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, lerp);
        }
    }
}
