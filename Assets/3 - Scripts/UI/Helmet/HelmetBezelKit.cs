using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared "recessed display" dressing for the three helmet housings: a dark
/// metal bezel ring, a glass backplate with a top sheen line, and a soft
/// accent halo behind the content ("backlit screen"). REAL camera bloom can't
/// reach ScreenSpaceOverlay UI (KinoBloom runs in OnRenderImage, before UI
/// composites) — the halo sprite IS the bloom, faked the same way the existing
/// HUDs fake glow with Shadow components. All tinted images register for live
/// re-tint via HelmetHudPalette.OnAccentChanged.
/// </summary>
public static class HelmetBezelKit
{
    class TintTarget { public Image img; public System.Func<Color> color; }
    static readonly List<TintTarget> _tints = new List<TintTarget>();
    static bool _subscribed;

    /// Builds bezel layers as the FIRST children of `card` (behind its content),
    /// expanded `pad` units beyond the card rect, ignoring any layout group.
    public static void BuildBezel(RectTransform card, float pad)
    {
        EnsureSubscribed();
        // Halo (outermost, softest) → backplate → ring → sheen (top edge).
        var halo = NewLayer(card, "BezelHalo", pad + 18f);
        halo.sprite = GetRadialHaloSprite();
        halo.type = Image.Type.Sliced;
        Track(halo, () => HelmetHudPalette.AccentGlow);

        var plate = NewLayer(card, "BezelGlass", pad);
        plate.sprite = UIPanelSprites.GetBeveledPanel();
        plate.type = Image.Type.Sliced;
        Track(plate, () => HelmetHudPalette.GlassBackplate);

        var ring = NewLayer(card, "BezelRing", pad);
        ring.sprite = UIPanelSprites.GetBeveledOutline();
        ring.type = Image.Type.Sliced;
        Track(ring, () => HelmetHudPalette.BezelRing);

        // 1-unit sheen line across the top of the expanded plate.
        var sheenRt = NewRT(card, "BezelSheen");
        sheenRt.anchorMin = new Vector2(0f, 1f);
        sheenRt.anchorMax = new Vector2(1f, 1f);
        sheenRt.pivot = new Vector2(0.5f, 1f);
        sheenRt.offsetMin = new Vector2(-pad + 6f, pad - 2f);
        sheenRt.offsetMax = new Vector2(pad - 6f, pad - 1f);
        sheenRt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var sheen = sheenRt.gameObject.AddComponent<Image>();
        sheen.raycastTarget = false;
        Track(sheen, () => HelmetHudPalette.GlassSheen);

        // Draw order: push all four behind the card's existing content.
        sheenRt.SetAsFirstSibling();
        ((RectTransform)ring.transform).SetAsFirstSibling();
        ((RectTransform)plate.transform).SetAsFirstSibling();
        ((RectTransform)halo.transform).SetAsFirstSibling();
    }

    static Image NewLayer(RectTransform card, string name, float pad)
    {
        var rt = NewRT(card, name);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-pad, -pad);
        rt.offsetMax = new Vector2(pad, pad);
        rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    static RectTransform NewRT(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Track(Image img, System.Func<Color> color)
    {
        img.color = color();
        _tints.Add(new TintTarget { img = img, color = color });
    }

    static void EnsureSubscribed()
    {
        if (_subscribed) return;
        _subscribed = true;
        HelmetHudPalette.OnAccentChanged += () =>
        {
            for (int i = _tints.Count - 1; i >= 0; i--)
            {
                if (_tints[i].img == null) { _tints.RemoveAt(i); continue; }
                _tints[i].img.color = _tints[i].color();
            }
        };
    }

    // Soft radial halo sprite (procedural, cached) — the faked bloom.
    static Sprite _halo;
    static Sprite GetRadialHaloSprite()
    {
        if (_halo != null) return _halo;
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S - 0.5f, dy = (y + 0.5f) / S - 0.5f;
                float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) * 2f);
                float a = Mathf.Pow(1f - d, 2.2f);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        _halo = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f,
                              0u, SpriteMeshType.FullRect, new Vector4(40, 40, 40, 40));
        _halo.name = "HelmetBezelHalo";
        return _halo;
    }
}
