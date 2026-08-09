using System.Collections;
using UnityEngine;

/// <summary>
/// The bullet streak drawn for someone ELSE'S shot.
///
/// PistolController.SpawnTracer is private and wired into that controller's own
/// muzzle, recoil and kill-cam state — none of which exists on a puppet. Rather
/// than widening its access and dragging that coupling across, this is a small
/// standalone copy of the same look: a tapered additive line that fades out.
///
/// Deliberately simpler than the first-person tracer. That one is tuned to read
/// at 30cm from the eye with an HDR core and a length cap to survive
/// foreshortening; this one is always seen from a distance, side-on, where a
/// plain bright streak reads better than a layered one.
///
/// ⚠ FLOATING ORIGIN: the streak is PARENTED and drawn in LOCAL space.
///
/// The first version pinned world-space coordinates with no parent, on the
/// reasoning that a rebase could only disturb one frame of a 0.12s effect.
/// That was wrong. The planet is orbiting, so the world slides out from under a
/// fixed world-space line continuously — the tracer appeared to sag and drift,
/// which reads exactly like it is falling under gravity.
///
/// PistolController.SpawnTracer already documents the answer for the
/// first-person streak: "camera-parented with local positions so
/// floating-origin shifts and player movement carry them cleanly during the
/// short life." Same rule here, parented to the shooter's puppet.
/// </summary>
public class RemoteTracer : MonoBehaviour
{
    const float Duration = 0.12f;
    const float Width    = 0.035f;

    static readonly Color TracerColor = new Color(1.4f, 1.1f, 0.4f, 1f);

    static Material _sharedMat;

    /// One additive unlit material for every tracer in the session.
    static Material SharedMaterial
    {
        get
        {
            if (_sharedMat != null) return _sharedMat;

            // ⚠️ Exactly the fallback chain PistolController.GetTracerShader
            // uses, and for a reason: a shader that no material in any scene or
            // Resources folder references gets STRIPPED FROM THE BUILD, so a
            // code-made material can render fine in the Editor and come out
            // magenta (or invisible) in the exe. This chain is already proven to
            // survive this project's builds — keep the two in step.
            var shader = Shader.Find("Particles/Additive")
                      ?? Shader.Find("Legacy Shaders/Particles/Additive")
                      ?? Shader.Find("Sprites/Default")
                      ?? Shader.Find("Unlit/Color");
            _sharedMat = new Material(shader) { name = "RemoteTracerMat" };
            _sharedMat.renderQueue = 3000;
            return _sharedMat;
        }
    }

    /// <param name="parent">
    /// The shooter's puppet. Everything is stored relative to it, so the streak
    /// rides origin rebases, the planet's orbit and the player's own movement
    /// for its whole life. Passing null falls back to world space and WILL
    /// visibly drift — only do it if there is genuinely nothing to attach to.
    /// </param>
    public static void Spawn(Vector3 start, Vector3 end, Transform parent)
    {
        if ((end - start).sqrMagnitude < 0.0001f) return;

        var go = new GameObject("RemoteTracer");

        Vector3 a = start, b = end;
        if (parent != null)
        {
            go.transform.SetParent(parent, worldPositionStays: false);
            a = parent.InverseTransformPoint(start);
            b = parent.InverseTransformPoint(end);
        }
        else
        {
            go.transform.position = start;
        }

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace   = parent == null;
        lr.positionCount   = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        // Tapered: bright and fat at the head, pinched to nothing at the tail.
        lr.widthCurve      = AnimationCurve.EaseInOut(0f, 0f, 1f, Width);
        lr.numCapVertices  = 2;
        lr.material        = SharedMaterial;
        lr.startColor      = TracerColor;
        lr.endColor        = TracerColor;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows  = false;
        lr.alignment       = LineAlignment.View;

        go.AddComponent<RemoteTracer>().Begin(lr);
    }

    void Begin(LineRenderer lr) => StartCoroutine(FadeAndDie(lr));

    IEnumerator FadeAndDie(LineRenderer lr)
    {
        float t = 0f;
        while (t < Duration && lr != null)
        {
            t += Time.deltaTime;
            float a = 1f - Mathf.Clamp01(t / Duration);
            var c = TracerColor * a;
            c.a = a;
            lr.startColor = c;
            lr.endColor   = c;
            yield return null;
        }
        if (gameObject != null) Destroy(gameObject);
    }
}
