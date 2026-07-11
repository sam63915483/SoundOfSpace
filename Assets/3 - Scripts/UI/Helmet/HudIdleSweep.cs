using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Recurring "screen refresh" cycle for a HUD cluster: the accent scanline
/// sweeps down the screen, returns up and fades away — and the moment it
/// fires, the cluster snaps to full brightness, then slowly dims toward
/// DimFloor until the next sweep re-initializes it. Runs on unscaled time
/// with a random 3–5 s period per cluster so the three screens never pulse
/// in sync. Attach to a cluster's content root — for the warped corner
/// clusters that root lives inside the capture rig, so the sweep and the
/// dimming foreshorten with the screen. Dimming uses a CanvasGroup on the
/// target (plus optional linked rects, e.g. the compass heading badge);
/// nested CanvasGroups multiply, so HudVisibility / boot-FX groups on the
/// host canvas are unaffected.
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

    CanvasGroup _group;
    CanvasGroup[] _linked = new CanvasGroup[0];

    public static void Ensure(RectTransform target, params RectTransform[] alsoDim)
    {
        if (target == null) return;
        var s = target.GetComponent<HudIdleSweep>();
        if (s == null) s = target.gameObject.AddComponent<HudIdleSweep>();
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
        SetBrightness(1f);   // never leave the cluster stuck dim
    }

    void SetBrightness(float a)
    {
        if (_group != null) _group.alpha = a;
        for (int i = 0; i < _linked.Length; i++)
            if (_linked[i] != null) _linked[i].alpha = a;
    }

    IEnumerator Loop()
    {
        if (GetComponent<RectMask2D>() == null) gameObject.AddComponent<RectMask2D>();
        _group = GetGroup(gameObject);
        // Random initial phase so the clusters start desynced.
        yield return new WaitForSecondsRealtime(Random.Range(0f, MaxInterval));
        while (true)
        {
            yield return Sweep();
            // Slow decay toward DimFloor — "the screen is dying out" — until
            // the next sweep re-initializes it back to full brightness.
            float interval = Random.Range(MinInterval, MaxInterval);
            for (float t = 0f; t < interval; t += Time.unscaledDeltaTime)
            {
                SetBrightness(Mathf.Lerp(1f, DimFloor, t / interval));
                yield return null;
            }
        }
    }

    // Accent bar: down the screen, then back up, fading away on the rise.
    IEnumerator Sweep()
    {
        SetBrightness(1f);   // the scanline kicks the screen back to life

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
            yield return null;
        }
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
