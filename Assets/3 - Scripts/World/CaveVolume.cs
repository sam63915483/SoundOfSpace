using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The inside of a cave, as a chain of capsules following the tunnel. Written by
/// CaveGenerator (local-space centres + radii, one per ring) so the test is the
/// actual bore, not a bounding box — a box around a bent tunnel would claim
/// large volumes of solid rock and open ground.
///
/// Two jobs, both about the planet's ocean:
///
///   • PlayerController asks IsInsideAnyCave() before treating the water trigger
///     as water. The ocean is a trigger sphere the size of the whole planet
///     (radius 200 on Humble Abode = sea level), so ANY cave that descends below
///     sea level puts the player inside it and they start swimming through solid
///     rock corridors.
///
///   • The ocean POST-PROCESS has the same problem for the same reason:
///     PlanetEffects switches to its full-screen underwater material whenever the
///     camera is closer to the body's centre than the ocean radius, regardless of
///     what's actually in front of it — so a cave below sea level fills with
///     water visually too. While the camera is inside a cave this disables
///     PlanetEffects.displayOceans (a public field — the effect itself lives in
///     the DO-NOT-TOUCH Planet Effects zone and is not edited).
///
/// The suppression is REFERENCE COUNTED across caves, so overlapping caves, or
/// walking from one straight into another, can't leave the oceans switched off.
/// </summary>
public class CaveVolume : MonoBehaviour
{
    // Live instances, maintained the CLAUDE.md way (OnEnable/OnDisable) so the
    // static test never calls FindObjectsOfType.
    static readonly List<CaveVolume> s_all = new List<CaveVolume>();
    public static IReadOnlyList<CaveVolume> All => s_all;

    // Every cavity as a capsule, in LOCAL space: tunnels AND rooms. Rooms are
    // capsules with A == B.
    //
    // The first version stored only the tunnel centre-lines, which meant a
    // 6.8 m chamber was covered to a radius of about 3 — so standing anywhere
    // off the middle of a room put you OUTSIDE the volume and the ocean came
    // straight back. Rooms have to be in here.
    [Tooltip("Cavity capsules, start points. Written by CaveGenerator — don't hand-edit.")]
    public Vector3[] capsuleA;
    [Tooltip("Cavity capsules, end points. Rooms have A == B.")]
    public Vector3[] capsuleB;
    [Tooltip("Cavity radius per capsule.")]
    public float[] capsuleR;

    [Tooltip("Multiplies every radius for the containment test. Over 1 so you still count as 'inside' while brushing a wall or standing in a corner.")]
    public float radiusPadding = 1.25f;

    [Tooltip("Extra padding applied ONLY to the ocean cutout sent to the shader, on top of radiusPadding. Slightly over 1 so the cutout reaches into the rock instead of leaving a rind of water against the walls.")]
    public float oceanCutoutPadding = 1.15f;

    // Kept so old scene data still deserialises; no longer used for anything.
    [HideInInspector] public float mouthBubbleRadius = 0f;
    [HideInInspector] public Vector3 mouthBubbleCentre = Vector3.zero;

    [Tooltip("LEGACY ESCAPE HATCH — leave OFF. Switches the planet's oceans off entirely while the camera is inside this cave. Superseded by the analytic cutout in OceanEffect.shader, which removes water only from the cave.")]
    public bool suppressOcean = false;

    // How many caves currently contain the camera. Oceans go back on at zero.
    static int s_oceanSuppressors;
    static PlanetEffects[] s_planetEffects;
    static bool s_oceansWereOn = true;

    bool _suppressing;
    Transform _cam;
    int _camRefindCooldown;

    void OnEnable()
    {
        if (!s_all.Contains(this)) s_all.Add(this);
        SelfHealOceans();
    }

    // The thing being toggled is a SHARED ASSET, so a session that ended while
    // the player was underground (editor stop, crash) leaves displayOceans
    // written false on disk — and then every planet loses its ocean, with
    // nothing to point at. If nobody is currently suppressing, it must be on.
    static void SelfHealOceans()
    {
        if (s_oceanSuppressors > 0) return;
        if (s_planetEffects == null || s_planetEffects.Length == 0)
            s_planetEffects = FindPlanetEffects();

        for (int i = 0; i < s_planetEffects.Length; i++)
        {
            if (s_planetEffects[i] == null || s_planetEffects[i].displayOceans) continue;
            s_planetEffects[i].displayOceans = true;
            Debug.LogWarning("[CaveVolume] PlanetEffects.displayOceans was left OFF by an " +
                             "earlier session — restored. (It's a shared asset, so the flag " +
                             "persists if play mode is stopped inside a cave.)");
        }
    }

    void OnDisable()
    {
        s_all.Remove(this);
        // Never leave the world's oceans switched off because a cave was
        // disabled or a scene unloaded while the player stood inside it.
        ReleaseOcean();
    }

    /// True if `worldPoint` is inside this cave — any tunnel, any room, or the
    /// bubble at the mouth.
    public bool Contains(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        if (capsuleA == null || capsuleB == null || capsuleR == null) return false;
        int n = Mathf.Min(capsuleA.Length, Mathf.Min(capsuleB.Length, capsuleR.Length));
        for (int i = 0; i < n; i++)
        {
            float r = capsuleR[i] * radiusPadding;
            if (SqrDistanceToSegment(local, capsuleA[i], capsuleB[i]) <= r * r) return true;
        }
        return false;
    }

    public static bool IsInsideAnyCave(Vector3 worldPoint)
    {
        for (int i = 0; i < s_all.Count; i++)
            if (s_all[i] != null && s_all[i].Contains(worldPoint)) return true;
        return false;
    }

    static float SqrDistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float abSqr = ab.sqrMagnitude;
        if (abSqr < 1e-10f) return (p - a).sqrMagnitude;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / abSqr);
        return (p - (a + ab * t)).sqrMagnitude;
    }

    void Update()
    {
        // OFF BY DEFAULT and it should stay that way. OceanEffect.shader now
        // cuts the water out of the cave analytically (see CaveOceanCutout), so
        // there is no reason to touch the global flag — and switching it killed
        // the sea across the entire planet whenever the player came near a cave.
        // Left in place only as an escape hatch if the shader path ever fails.
        if (!suppressOcean) { ReleaseOcean(); return; }

        // Cached, lazily refound — never Camera.main per frame (CLAUDE.md).
        if (_cam == null)
        {
            if (--_camRefindCooldown > 0) return;
            var cam = Camera.main;
            _cam = cam != null ? cam.transform : null;
            _camRefindCooldown = 30;
            if (_cam == null) return;
        }

        bool inside = Contains(_cam.position);
        if (inside && !_suppressing) AcquireOcean();
        else if (!inside && _suppressing) ReleaseOcean();
    }

    // PlanetEffects is a ScriptableObject ASSET ([CreateAssetMenu], referenced by
    // CustomPostProcessing) — NOT a component on anything in the scene. So
    // FindObjectsOfType returns zero and the first version of this silently did
    // nothing at all: the cave stayed full of water and there was no error to
    // notice. Resources.FindObjectsOfTypeAll is what reaches a loaded asset.
    //
    // (This is not Resources.Load — nothing is loaded from a Resources folder,
    // so it doesn't breach the project's no-Resources.Load rule. It only
    // enumerates objects already in memory, which this asset is, because the
    // post-processing stack holds a reference to it.)
    static PlanetEffects[] FindPlanetEffects()
    {
        var found = Resources.FindObjectsOfTypeAll<PlanetEffects>();
        if (found == null || found.Length == 0)
            Debug.LogWarning("[CaveVolume] No PlanetEffects asset found — the ocean " +
                             "post-process can't be suppressed, so caves below sea level " +
                             "will still look flooded.");
        return found ?? new PlanetEffects[0];
    }

    void AcquireOcean()
    {
        _suppressing = true;
        if (s_oceanSuppressors++ > 0) return;      // another cave already has it off

        if (s_planetEffects == null || s_planetEffects.Length == 0)
            s_planetEffects = FindPlanetEffects();

        s_oceansWereOn = true;
        for (int i = 0; i < s_planetEffects.Length; i++)
        {
            if (s_planetEffects[i] == null) continue;
            s_oceansWereOn = s_planetEffects[i].displayOceans;
            s_planetEffects[i].displayOceans = false;
        }
    }

    void ReleaseOcean()
    {
        if (!_suppressing) return;
        _suppressing = false;
        if (--s_oceanSuppressors > 0) return;      // someone else is still inside
        s_oceanSuppressors = 0;

        if (s_planetEffects == null) return;
        for (int i = 0; i < s_planetEffects.Length; i++)
            if (s_planetEffects[i] != null) s_planetEffects[i].displayOceans = s_oceansWereOn;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.5f);
        if (capsuleA != null && capsuleB != null && capsuleR != null)
        {
            int n = Mathf.Min(capsuleA.Length, Mathf.Min(capsuleB.Length, capsuleR.Length));
            for (int i = 0; i < n; i++)
            {
                Gizmos.DrawWireSphere(capsuleA[i], capsuleR[i] * radiusPadding);
                Gizmos.DrawWireSphere(capsuleB[i], capsuleR[i] * radiusPadding);
                Gizmos.DrawLine(capsuleA[i], capsuleB[i]);
            }
        }
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(mouthBubbleCentre, mouthBubbleRadius);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
