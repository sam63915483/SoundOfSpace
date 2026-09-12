using UnityEngine;

/// <summary>
/// Puts an always-animating HUD element on its own nested Canvas so its
/// per-frame change rebuilds only itself instead of the whole canvas it sits on.
///
/// Why (2026-09-12 dev-build probe, PerfTrace "[PerfTrace UI]"): uGUI re-batches
/// an ENTIRE canvas whenever any graphic on it changes. Every frame, these were
/// dirty: the galaxy panel's 8 twinkling stars + border on HUD_Canvas (~100
/// graphics rebuilt for a twinkle), the hotbar's brackets / index / sweep on
/// every slot, the HAL eye pulse, the galaxy clock's LED and border, the idle
/// sweep bars, the tutorial pill's accent bar and the helmet screen warps.
/// Together: 1.6 ms of PlayerUpdateCanvases plus 0.5 ms more per HUD-camera
/// render. A nested Canvas draws at the same place in the hierarchy (no sorting
/// override), so the look is unchanged; each one costs one extra draw call.
/// </summary>
public static class UiIsolate
{
    public static Canvas Nest(Transform t)
    {
        if (t == null) return null;
        var c = t.GetComponent<Canvas>();
        if (c == null) c = t.gameObject.AddComponent<Canvas>();
        c.overrideSorting = false;
        return c;
    }
}
