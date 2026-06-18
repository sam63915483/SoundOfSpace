using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central, event-driven "hide the gameplay HUD" switch. Two independent reasons
/// can hide the HUD and it stays hidden while EITHER holds:
///   • a persistent user setting — the "HIDE HUD" toggle in the CAMERA tab, and
///   • a transient cinematic force — set while the pod-arrival sequence plays.
///
/// HUDs opt in by calling RegisterHideable(canvas) at build time. We drive a
/// CanvasGroup alpha on each registered canvas rather than canvas.enabled, because
/// several HUDs (e.g. CompassHUD) re-assert canvas.enabled every frame — a one-shot
/// enabled=false would not stick. Alpha is an independent multiplier: each HUD keeps
/// owning its own enabled state, and we fade it out underneath when hidden.
///
/// Only the canvases that register here are affected, so this hides exactly the
/// chosen elements (compass / vitals / flight status / wallet) and leaves the rest
/// (hotbar, interact prompts, the pod countdown, HAL subtitles) untouched.
/// </summary>
public static class HudVisibility
{
    static bool _userHidden;
    static bool _forceHidden;
    static readonly List<CanvasGroup> _groups = new List<CanvasGroup>();

    public static bool Hidden => _userHidden || _forceHidden;

    /// Persistent user preference (the "HIDE HUD" camera setting).
    public static void SetUserHidden(bool hidden)
    {
        if (_userHidden == hidden) return;
        _userHidden = hidden;
        Apply();
    }

    /// Transient cinematic force — true while a cutscene (the pod arrival) owns
    /// the screen, restored to false on teardown.
    public static void SetForceHidden(bool hidden)
    {
        if (_forceHidden == hidden) return;
        _forceHidden = hidden;
        Apply();
    }

    /// Called by a HUD at build time. Adds a CanvasGroup if absent, tracks it, and
    /// applies the current hidden state immediately (so a HUD built while hidden
    /// comes up hidden).
    public static void RegisterHideable(Canvas canvas)
    {
        if (canvas == null) return;
        var cg = canvas.GetComponent<CanvasGroup>();
        if (cg == null) cg = canvas.gameObject.AddComponent<CanvasGroup>();
        if (!_groups.Contains(cg)) _groups.Add(cg);
        ApplyTo(cg);
    }

    static void Apply()
    {
        for (int i = _groups.Count - 1; i >= 0; i--)
        {
            if (_groups[i] == null) { _groups.RemoveAt(i); continue; } // prune destroyed canvases
            ApplyTo(_groups[i]);
        }
    }

    static void ApplyTo(CanvasGroup cg)
    {
        cg.alpha = Hidden ? 0f : 1f;
        cg.blocksRaycasts = !Hidden;
    }
}
