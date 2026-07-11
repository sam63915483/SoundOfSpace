using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Screen power-on" for a HUD cluster: a short alpha flicker on the cluster's
/// CanvasGroup followed by a bright accent scanline sweeping down the card.
/// Generic — GForceHUD uses it when the boost cluster appears; any housing can
/// call Play. Unscaled time (pause/cinematic safe). The sweep bar is created
/// per play and destroyed after; a RectMask2D on the card clips it.
/// </summary>
public class HudBootFX : MonoBehaviour
{
    Coroutine _running;

    public static void Play(CanvasGroup group, RectTransform card)
    {
        if (group == null || card == null) return;
        var fx = group.GetComponent<HudBootFX>();
        if (fx == null) fx = group.gameObject.AddComponent<HudBootFX>();
        if (fx._running != null) fx.StopCoroutine(fx._running);
        fx._running = fx.StartCoroutine(fx.Run(group, card));
    }

    IEnumerator Run(CanvasGroup group, RectTransform card)
    {
        if (card.GetComponent<RectMask2D>() == null) card.gameObject.AddComponent<RectMask2D>();

        // Flicker: (time, alpha) keyframes over ~0.28s.
        float[] t = { 0f, 0.05f, 0.09f, 0.14f, 0.18f, 0.24f, 0.28f };
        float[] a = { 0f, 1f,    0.05f, 1f,    0.25f, 1f,    1f    };
        float elapsed = 0f;
        int k = 0;
        while (elapsed < t[t.Length - 1])
        {
            elapsed += Time.unscaledDeltaTime;
            while (k < t.Length - 2 && elapsed > t[k + 1]) k++;
            float seg = Mathf.InverseLerp(t[k], t[k + 1], elapsed);
            group.alpha = Mathf.Lerp(a[k], a[k + 1], seg);
            yield return null;
        }
        group.alpha = 1f;

        // Scanline sweep: accent bar, top → bottom over 0.3s, fading out.
        var barGo = new GameObject("BootSweep", typeof(RectTransform));
        barGo.transform.SetParent(card, false);
        var rt = (RectTransform)barGo.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, 14f);
        var img = barGo.AddComponent<Image>();
        img.raycastTarget = false;
        barGo.AddComponent<LayoutElement>().ignoreLayout = true;
        Color c = HelmetHudPalette.Accent;

        float h = card.rect.height;
        const float dur = 0.3f;
        for (float s = 0f; s < dur; s += Time.unscaledDeltaTime)
        {
            float p = s / dur;
            rt.anchoredPosition = new Vector2(0f, -p * (h + 14f));
            c.a = 0.65f * (1f - p * p);
            img.color = c;
            yield return null;
        }
        Destroy(barGo);
        _running = null;
    }
}
