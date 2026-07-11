using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Recurring "screen refresh" cycle for a HUD cluster: the accent scanline
/// sweeps down the screen, returns up and fades away — and the screen only
/// re-brightens WHERE the line has passed, so the sweep visibly wipes the
/// dimmed display back to life. After the wipe, brightness slowly decays
/// toward DimFloor until the next sweep re-initializes it. Runs on unscaled
/// time with a random 3–5 s period per cluster so the three screens never
/// pulse in sync.
///
/// Two shading paths: warped corner clusters pass their HousingScreenWarp,
/// which shades per-vertex (spatial reveal above/below the line — a
/// CanvasGroup can't split a card in half); the flat compass falls back to
/// CanvasGroup alpha ramped across the down-pass (plus optional linked
/// rects, e.g. the heading badge). Nested CanvasGroups multiply, so the
/// HudVisibility / boot-FX groups on the host canvas are unaffected.
/// </summary>
public class HudIdleSweep : MonoBehaviour
{
    const float MinInterval = 3f;
    const float MaxInterval = 5f;
    const float SweepDownTime = 0.28f;
    const float SweepUpTime = 0.28f;
    const float BarHeight = 14f;
    const float BarAlpha = 0.65f;
    const float DimFloor = 0.55f;   // brightness the cluster decays to before the next sweep

    HousingScreenWarp _warp;        // spatial reveal path (corner clusters)
    CanvasGroup _group;             // temporal ramp path (compass)
    CanvasGroup[] _linked = new CanvasGroup[0];

    public static void Ensure(RectTransform target, HousingScreenWarp warp, params RectTransform[] alsoDim)
    {
        if (target == null) return;
        var s = target.GetComponent<HudIdleSweep>();
        if (s == null) s = target.gameObject.AddComponent<HudIdleSweep>();
        s._warp = warp;
        if (alsoDim != null && alsoDim.Length > 0)
        {
            var list = new List<CanvasGroup>();
            for (int i = 0; i < alsoDim.Length; i++)
                if (alsoDim[i] != null) list.Add(GetGroup(alsoDim[i].gameObject));
            s._linked = list.ToArray();
        }
    }

    static CanvasGroup GetGroup(GameObject go)
    {
        var g = go.GetComponent<CanvasGroup>();
        if (g == null) g = go.AddComponent<CanvasGroup>();
        return g;
    }

    void OnEnable()
    {
        StartCoroutine(Loop());
    }

    void OnDisable()
    {
        SetUniform(1f);   // never leave the cluster stuck dim
    }

    // Uniform brightness on whichever shading path this cluster uses.
    void SetUniform(float b)
    {
        if (_warp != null) { _warp.SetShade(b); return; }
        if (_group != null) _group.alpha = b;
        for (int i = 0; i < _linked.Length; i++)
            if (_linked[i] != null) _linked[i].alpha = b;
    }

    IEnumerator Loop()
    {
        if (GetComponent<RectMask2D>() == null) gameObject.AddComponent<RectMask2D>();
        if (_warp == null) _group = GetGroup(gameObject);
        // Random initial phase so the clusters start desynced.
        yield return new WaitForSecondsRealtime(Random.Range(0f, MaxInterval));
        while (true)
        {
            yield return Sweep();
            // Slow decay toward DimFloor — "the screen is dying out" — until
            // the next sweep re-initializes it.
            float interval = Random.Range(MinInterval, MaxInterval);
            for (float t = 0f; t < interval; t += Time.unscaledDeltaTime)
            {
                SetUniform(Mathf.Lerp(1f, DimFloor, t / interval));
                yield return null;
            }
        }
    }

    // Accent bar: down the screen (re-brightening only what it has passed),
    // then back up over the now-lit screen, fading away on the rise.
    IEnumerator Sweep()
    {
        float dimAtStart = _warp != null ? DimFloor : (_group != null ? _group.alpha : DimFloor);

        var rt = (RectTransform)transform;
        var barGo = new GameObject("IdleSweep", typeof(RectTransform));
        barGo.transform.SetParent(rt, false);
        var barRt = (RectTransform)barGo.transform;
        barRt.anchorMin = new Vector2(0f, 1f);
        barRt.anchorMax = new Vector2(1f, 1f);
        barRt.pivot = new Vector2(0.5f, 1f);
        barRt.sizeDelta = new Vector2(0f, BarHeight);
        var img = barGo.AddComponent<Image>();
        img.raycastTarget = false;
        barGo.AddComponent<LayoutElement>().ignoreLayout = true;
        Color c = HelmetHudPalette.Accent;

        float h = rt.rect.height;
        for (float s = 0f; s < SweepDownTime; s += Time.unscaledDeltaTime)
        {
            float p = s / SweepDownTime;
            barRt.anchoredPosition = new Vector2(0f, -p * (h + BarHeight));
            c.a = BarAlpha;
            img.color = c;
            // Spatial reveal: the warp brightens rows above the line only.
            // The line offset trails the bar slightly so the bar itself rides
            // in the lit zone. Compass fallback: ramp the whole strip up
            // across the pass instead of snapping.
            if (_warp != null) _warp.SetShade(dimAtStart, 1f - p - 0.04f);
            else SetUniform(Mathf.Lerp(dimAtStart, 1f, p));
            yield return null;
        }
        SetUniform(1f);   // wipe complete — fully re-initialized
        for (float s = 0f; s < SweepUpTime; s += Time.unscaledDeltaTime)
        {
            float p = 1f - s / SweepUpTime;   // 1 → 0: back up, fading away
            barRt.anchoredPosition = new Vector2(0f, -p * (h + BarHeight));
            c.a = BarAlpha * p;
            img.color = c;
            yield return null;
        }
        Destroy(barGo);
    }
}
