using UnityEngine;

/// <summary>
/// MENU-ONLY: aims the sun's directional light at whichever planet DOMINATES
/// the camera's view each frame.
///
/// Why: a single directional light can only be correct for ONE planet at a
/// time. Gameplay's SunShadowCaster aims it at the camera — right for the
/// planet you're at, and you never scrutinize the others. The menu is the
/// opposite: distant planets ARE the backdrop, and lighting them along the
/// sun→camera direction lit their night sides gray (Sam: "in 1.6.7.7.7 when
/// you look at the dark side of a planet it's dark — that doesn't happen in
/// the menu"). Aiming at the most-viewed planet gives the on-screen planet a
/// correct terminator, exactly like being there in gameplay.
/// Added by MenuOrbitBootstrap (which also disables the original caster).
/// </summary>
public class MenuSunAim : MonoBehaviour
{
    Camera cam;

    public void Init(Camera menuCamera) { cam = menuCamera; }

    void LateUpdate()
    {
        if (cam == null) return;
        CelestialBody best = null;
        float bestScore = 0f;
        Vector3 camPos = cam.transform.position;
        Vector3 fwd = cam.transform.forward;
        foreach (var b in NBodySimulation.Bodies)
        {
            if (b == null || b.radius <= 0f) continue;
            if (b.bodyType == CelestialBody.BodyType.Sun || b.isStaticAttractor) continue;
            Vector3 to = b.Position - camPos;
            float dist = to.magnitude;
            if (dist < 1f) continue;
            float angularSize = b.radius / dist;                       // how big it looks
            float onAxis = Mathf.Max(0.05f, Vector3.Dot(fwd, to / dist)); // how centered it is
            float score = angularSize * onAxis * onAxis;
            if (score > bestScore) { bestScore = score; best = b; }
        }
        if (best != null) transform.LookAt(best.Position);
    }
}
