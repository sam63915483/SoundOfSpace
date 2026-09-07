using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sits on the ROOT of a vendor stand (fish market or goods vendor) and answers
/// the one question every planet-economy system needs: "which planet am I on?"
///
/// The answer is read from the hierarchy — a vendor is parented under its
/// <see cref="CelestialBody"/> the same way buildings, spawn markers and the
/// authored NPCs are — so a prefab dropped under a planet configures itself and
/// Sam never touches a per-instance field. That is the whole point: 10 fish
/// markets with zero settings between them.
///
/// The body is resolved lazily rather than in Awake because a planet's
/// GameObject exists long before <c>SolarSystemSpawner</c> has built its
/// terrain, and because a vendor may be re-parented by the placement tool in
/// the Editor. <see cref="BodyName"/> is safe to call every frame.
/// </summary>
[DisallowMultipleComponent]
public class VendorSite : MonoBehaviour
{
    public enum VendorKind { FishMarket = 0, GoodsVendor = 1 }

    /// <summary>Live vendors, maintained in OnEnable/OnDisable (repo convention —
    /// never FindObjectsOfType in a loop). Economy tables and the phone MARKETS
    /// page walk this.</summary>
    public static readonly List<VendorSite> AllInstances = new List<VendorSite>();

    [Tooltip("What this stand sells. Fish markets buy fish; goods vendors sell crystals and supplies.")]
    public VendorKind kind = VendorKind.FishMarket;

    [Tooltip("Leave EMPTY. Only set this to override the planet a vendor belongs to — " +
             "normally the body is read from the parent hierarchy, which is what makes " +
             "the prefab drag-and-drop with no configuration.")]
    public string bodyNameOverride = "";

    CelestialBody _body;
    string        _cachedName;

    /// <summary>The planet this vendor trades on, or null while the hierarchy is
    /// still being built. Re-resolves itself if the reference is lost.</summary>
    public CelestialBody Body
    {
        get
        {
            if (_body == null) _body = GetComponentInParent<CelestialBody>();
            return _body;
        }
    }

    /// <summary>Planet name as the save system and the economy tables know it, or
    /// an empty string if this vendor is not parented under a body yet.</summary>
    public string BodyName
    {
        get
        {
            if (!string.IsNullOrEmpty(bodyNameOverride)) return bodyNameOverride;
            var b = Body;
            if (b == null) return string.Empty;
            if (string.IsNullOrEmpty(_cachedName) || _body != b) _cachedName = b.bodyName;
            return _cachedName;
        }
    }

    /// <summary>True once this vendor knows its planet. Callers that build a buy
    /// list should wait for this rather than assuming Awake order.</summary>
    public bool IsResolved => !string.IsNullOrEmpty(BodyName);

    void OnEnable()
    {
        if (!AllInstances.Contains(this)) AllInstances.Add(this);
    }

    void OnDisable()
    {
        AllInstances.Remove(this);
    }

    /// <summary>First vendor of a given kind on a named planet, or null.</summary>
    public static VendorSite Find(string bodyName, VendorKind kind)
    {
        if (string.IsNullOrEmpty(bodyName)) return null;
        for (int i = 0; i < AllInstances.Count; i++)
        {
            var v = AllInstances[i];
            if (v == null || v.kind != kind) continue;
            if (v.BodyName == bodyName) return v;
        }
        return null;
    }

    /// <summary>The planet a component is standing on, walking up from its own
    /// transform. Used by vendor scripts that live on a child of the stand
    /// (the NPC) rather than on the site root.</summary>
    public static string BodyNameFor(Component c)
    {
        if (c == null) return string.Empty;
        var site = c.GetComponentInParent<VendorSite>();
        if (site != null) return site.BodyName;
        var body = c.GetComponentInParent<CelestialBody>();
        return body != null ? body.bodyName : string.Empty;
    }
}
