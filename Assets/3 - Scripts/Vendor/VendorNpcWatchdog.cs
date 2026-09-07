using UnityEngine;

/// <summary>
/// Makes sure the vendor standing in a market stand is actually THERE at
/// runtime, and says exactly what is wrong with it if not.
///
/// <b>Why this exists.</b> Sam's markets show the stand but no alien in play,
/// while in the Editor all ten are present, active, renderers enabled, on the
/// Default layer the camera renders, with the same mesh, material, bones, scale
/// and components as the Humble Abode vendor that works. Every static
/// difference has been checked and there is none — so whatever removes it
/// happens at runtime, and no amount of reading the scene will show it.
///
/// So this does two jobs:
///
///  1. <b>Repairs what can be repaired.</b> If the vendor has been deactivated,
///     had its renderer switched off, or been pushed to a layer the camera does
///     not draw, it is put back — every frame it needs to be, not once at Start,
///     because whatever is doing it may do it again after we run.
///
///  2. <b>Reports what cannot.</b> The first time the player gets close it logs
///     one line per stand with the vendor's real state — alive, active, renderer
///     enabled, actually visible to the camera, world position, distance, bounds.
///     That line is the difference between fixing this and guessing at it again;
///     it lands in Player.log.
///
/// Cheap: one distance check a second per stand, and the repair pass only runs
/// while the player is near enough to care.
/// </summary>
[DisallowMultipleComponent]
public class VendorNpcWatchdog : MonoBehaviour
{
    [Tooltip("Only watch while the player is this close (metres). Beyond it the " +
             "component does nothing but a distance check once a second.")]
    public float watchRange = 150f;

    [Tooltip("Log one detailed state line the first time the player comes this close.")]
    public float reportRange = 60f;

    [Tooltip("Put the vendor back if something deactivates or hides it.")]
    public bool repair = true;

    VendorSite _site;
    Transform  _player;
    GameObject _npc;          // the vendor GameObject we are guarding
    Renderer[] _rends;
    bool       _reported;
    float      _nextScan;
    int        _repairs;

    void Awake()
    {
        _site = GetComponent<VendorSite>();
        CacheNpc();
    }

    void CacheNpc()
    {
        // Look for either vendor kind; the marker component is what identifies
        // the NPC, and it survives even if the renderer does not.
        var fish = GetComponentInChildren<FishMarketNPC>(true);
        if (fish != null) { _npc = fish.gameObject; }
        else
        {
            var goods = GetComponentInChildren<Alien7Vendor>(true);
            if (goods != null) _npc = goods.gameObject;
        }
        _rends = _npc != null ? _npc.GetComponentsInChildren<Renderer>(true) : null;
    }

    void Update()
    {
        if (Time.time < _nextScan) return;
        _nextScan = Time.time + 1f;

        if (_player == null)
        {
            var pc = FindObjectOfType<PlayerController>();
            if (pc == null) return;
            _player = pc.transform;
        }

        float dist = Vector3.Distance(_player.position, transform.position);
        if (dist > watchRange) return;

        if (_npc == null) CacheNpc();

        string planet = _site != null ? _site.BodyName : "?";

        // ── The one case nothing can repair: it is gone ──────────────────────
        if (_npc == null)
        {
            if (!_reported)
            {
                _reported = true;
                Debug.LogError($"[VendorWatchdog] {planet}: the vendor NPC has been DESTROYED — " +
                               "the stand has no FishMarketNPC/Alien7Vendor child at all. " +
                               "Something removed it at runtime.");
            }
            return;
        }

        if (repair) RepairIfNeeded(planet);

        // ── One detailed report per stand, once the player is close ──────────
        if (!_reported && dist <= reportRange)
        {
            _reported = true;
            var smr = _npc.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Debug.Log(
                $"[VendorWatchdog] {planet}: dist={dist:F1}m  " +
                $"npcActiveSelf={_npc.activeSelf} activeInHierarchy={_npc.activeInHierarchy}  " +
                $"layer={_npc.layer}({LayerMask.LayerToName(_npc.layer)})  " +
                $"scale={_npc.transform.lossyScale}  " +
                $"pos={_npc.transform.position}  standPos={transform.position}  " +
                $"renderers={(_rends != null ? _rends.Length : 0)}  " +
                (smr != null
                    ? $"smrEnabled={smr.enabled} smrVisible={smr.isVisible} " +
                      $"updateOffscreen={smr.updateWhenOffscreen} " +
                      $"boundsCentre={smr.bounds.center} boundsSize={smr.bounds.size} " +
                      $"mesh={(smr.sharedMesh != null ? smr.sharedMesh.name : "NULL")} " +
                      $"mat={(smr.sharedMaterial != null ? smr.sharedMaterial.name : "NULL")} " +
                      $"rootBone={(smr.rootBone != null ? smr.rootBone.name + "@" + smr.rootBone.position : "NULL")}"
                    : "NO SkinnedMeshRenderer"));
        }
    }

    /// <summary>Put back anything that has been switched off. Runs every scan, not
    /// once, because whatever hides the vendor may do it again after we fix it —
    /// and if that happens the repair counter in the log makes it obvious.</summary>
    void RepairIfNeeded(string planet)
    {
        bool fixedSomething = false;

        if (!_npc.activeSelf)
        {
            _npc.SetActive(true);
            fixedSomething = true;
        }

        // A layer the main camera does not draw would make it invisible in play
        // while the Scene view (which ignores culling masks) still shows it.
        var cam = Camera.main;
        if (cam != null && (cam.cullingMask & (1 << _npc.layer)) == 0)
        {
            SetLayerTree(_npc.transform, 0);
            fixedSomething = true;
        }

        if (_rends == null || _rends.Length == 0) _rends = _npc.GetComponentsInChildren<Renderer>(true);
        if (_rends != null)
        {
            for (int i = 0; i < _rends.Length; i++)
            {
                var r = _rends[i];
                if (r == null) continue;
                if (!r.enabled)          { r.enabled = true;          fixedSomething = true; }
                if (r.forceRenderingOff) { r.forceRenderingOff = false; fixedSomething = true; }
                if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    fixedSomething = true;
                }
                // Skinned meshes cull on cached bounds; measuring the live pose
                // instead is the difference between a vendor that appears and
                // one that does not.
                if (r is SkinnedMeshRenderer skinned && !skinned.updateWhenOffscreen)
                {
                    skinned.updateWhenOffscreen = true;
                    fixedSomething = true;
                }
            }
        }

        if (fixedSomething)
        {
            _repairs++;
            Debug.LogWarning($"[VendorWatchdog] {planet}: put the vendor back " +
                             $"(repair #{_repairs}) — something had switched it off. " +
                             "If this number keeps climbing, something is fighting us every frame.");
        }
    }

    static void SetLayerTree(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayerTree(t.GetChild(i), layer);
    }
}
