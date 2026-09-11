using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hands out unused (unnamed) layers to render-texture "stages" — the helmet
/// HUD rig, the thrust gauge widget — so their cameras cull ONLY their own few
/// objects, and the main camera never draws them.
///
/// Why this exists (2026-09-11 profiler): both HUD cameras were created with the
/// default culling mask (Everything). Each one therefore culled and rendered
/// the whole scene — planet chunks, grass, cats, the player — into its little
/// HUD texture every frame: 3.4 ms + 2.5 ms of main-thread time, more than the
/// game's own camera (1.9 ms). Same trick ShuttleComputerWorldScreen already
/// uses for its screen (it takes layer 30 by scanning down from 30; this scans
/// down from 29 and remembers what it handed out so two stages never share).
/// </summary>
public static class PrivateLayer
{
    static readonly HashSet<int> _claimed = new HashSet<int>();

    /// <summary>An unnamed layer nobody else has claimed (29 downwards). Falls back
    /// to the UI layer if the project has no spare layer left.</summary>
    public static int Claim(string owner)
    {
        for (int i = 29; i >= 8; i--)
        {
            if (!string.IsNullOrEmpty(LayerMask.LayerToName(i))) continue;
            if (_claimed.Contains(i)) continue;
            if (IsSomeCamerasWholeMask(i)) continue;          // another private stage (e.g. the shuttle screen)
            _claimed.Add(i);
            Exclude(i);
            return i;
        }
        Debug.LogWarning("[PrivateLayer] no spare layer for " + owner + " — using UI");
        return LayerMask.NameToLayer("UI");
    }

    static bool IsSomeCamerasWholeMask(int layer)
    {
        int bit = 1 << layer;
        var cams = Camera.allCameras;
        for (int i = 0; i < cams.Length; i++)
            if (cams[i] != null && cams[i].cullingMask == bit) return true;
        return false;
    }

    /// <summary>Strip the layer from every camera that is not itself a private
    /// stage. Cheap; call again occasionally so cameras created later comply.</summary>
    public static void Exclude(int layer)
    {
        if (layer < 0) return;
        int bit = 1 << layer;
        var cams = Camera.allCameras;
        for (int i = 0; i < cams.Length; i++)
        {
            var cam = cams[i];
            if (cam == null) continue;
            int mask = cam.cullingMask;
            if (mask == bit) continue;                                   // its own stage
            if (mask != 0 && (mask & (mask - 1)) == 0) continue;         // some other single-layer stage
            if ((mask & bit) != 0) cam.cullingMask = mask & ~bit;
        }
    }

    public static void SetLayerRecursive(Transform root, int layer)
    {
        if (root == null || layer < 0) return;
        if (root.gameObject.layer != layer) root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++) SetLayerRecursive(root.GetChild(i), layer);
    }
}
