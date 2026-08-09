using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// "Where does this body's atmosphere end?" — one answer, shared.
///
/// ── Why reflection ───────────────────────────────────────────────────────
/// The atmosphere height lives inside the procedural-generation tree
/// (CelestialBodyGenerator → body → shading → atmosphereSettings), which is the
/// forbidden zone in CLAUDE.md: read-only inspection is fine, referencing or
/// modifying it is not. Reading it by reflection means this file compiles and
/// runs regardless of what happens in there, and can never drag a hard
/// dependency into gameplay code. SpaceDustField already established this exact
/// pattern ("reflection so we never touch/break the forbidden zone") — this is
/// the same computation, extracted so it isn't reinvented per caller.
///
/// ── The number itself ────────────────────────────────────────────────────
/// The generator's own formula: `atmosphereRadius = (1 + atmosphereScale) *
/// bodyRadius`. That is RELATIVE to the body, which is what makes it usable on
/// wildly different scales — Humble Abode's radius is 200, Cyclops' is 500.
///
/// Bodies with no atmosphere settings at all — moons, mostly — get
/// `radius * NoAtmosphereMultiplier`. They still need a sensible "you are in
/// space now" boundary, and a proportional one is the only kind that works
/// across sizes. A fixed altitude in metres is the thing to avoid: it is either
/// unreachable on a small moon or trivially cleared on a large planet.
/// </summary>
public static class AtmosphereBounds
{
    /// Stand-in atmosphere height for a body that has none, as a multiple of
    /// its radius. Sits between HALCommentator's 1.75 "near this body" ring and
    /// the ~1.5 a real atmosphere works out to, so an airless moon gets a
    /// boundary of the same character as a planet's.
    public const float NoAtmosphereMultiplier = 1.5f;

    // Per-body, computed once. Bodies are created at scene load and live for
    // the session, so this never needs invalidating within a scene — but it is
    // cleared on scene load anyway, because the CelestialBody instances are
    // replaced and holding the old keys would leak them.
    static readonly Dictionary<CelestialBody, float> _radius = new Dictionary<CelestialBody, float>();
    static readonly Dictionary<CelestialBody, bool>  _hasAtmo = new Dictionary<CelestialBody, bool>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void HookSceneLoad()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s,
                              UnityEngine.SceneManagement.LoadSceneMode m)
    {
        _radius.Clear();
        _hasAtmo.Clear();
    }

    /// <summary>
    /// Distance from the body's CENTRE at which you are clear of its
    /// atmosphere. Compare against `Vector3.Distance(point, body.Position)`.
    /// </summary>
    public static float Radius(CelestialBody body)
    {
        if (body == null) return 0f;
        if (_radius.TryGetValue(body, out float r)) return r;
        r = Compute(body);
        _radius[body] = r;
        return r;
    }

    /// True if this body has real atmosphere settings, as opposed to the
    /// proportional stand-in. Cached by the same pass as Radius.
    public static bool HasRealAtmosphere(CelestialBody body)
    {
        if (body == null) return false;
        if (!_hasAtmo.ContainsKey(body)) Radius(body);   // populates both
        return _hasAtmo.TryGetValue(body, out bool h) && h;
    }

    /// <summary>Is `point` outside this body's atmosphere?</summary>
    public static bool IsInSpace(Vector3 point, CelestialBody body)
    {
        if (body == null) return true;   // nothing to be inside of
        return Vector3.Distance(point, body.Position) > Radius(body);
    }

    static float Compute(CelestialBody b)
    {
        float fallback = b.radius * NoAtmosphereMultiplier;
        _hasAtmo[b] = false;

        try
        {
            MonoBehaviour gen = null;
            var comps = b.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var c in comps)
                if (c != null && c.GetType().Name == "CelestialBodyGenerator") { gen = c; break; }
            if (gen == null) return fallback;

            object settings = GetMember(gen, "body");
            object shading  = settings != null ? GetMember(settings, "shading") : null;
            object atmo     = shading  != null ? GetMember(shading,  "atmosphereSettings") : null;
            if (atmo == null) return fallback;      // body genuinely has none

            object scaleObj = GetMember(atmo, "atmosphereScale");
            if (scaleObj is float scale)
            {
                _hasAtmo[b] = true;
                return (1f + scale) * b.radius;
            }
        }
        catch { /* any shape change in the forbidden zone falls back safely */ }

        return fallback;
    }

    static object GetMember(object target, string name)
    {
        if (target == null) return null;
        var t = target.GetType();
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var f = t.GetField(name, Flags);
        if (f != null) return f.GetValue(target);

        var p = t.GetProperty(name, Flags);
        return p != null ? p.GetValue(target, null) : null;
    }
}
