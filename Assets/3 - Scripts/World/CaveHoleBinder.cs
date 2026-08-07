using UnityEngine;

/// <summary>
/// Makes a placed cave self-sufficient: on wake it walks up to the CelestialBody
/// it was dropped on and makes sure that body has a PlanetHolePuncher, so the
/// TerrainHole marker inside the cave prefab actually gets cut.
///
/// Without this you'd have to remember to add the puncher to every planet you
/// ever put a cave on — and the failure is silent and confusing: the cave is
/// there, correctly positioned, and completely sealed behind un-punched terrain.
///
/// The puncher does all its work from Start (it scans the body for TerrainHole
/// children and waits for the terrain mesh to appear), so adding the component
/// at runtime is enough — Unity runs Start on it the moment it's added.
/// </summary>
[DefaultExecutionOrder(-50)]   // before PlanetHolePuncher's own Start would run
public class CaveHoleBinder : MonoBehaviour
{
    [Tooltip("Log what was found/added. Leave on while you're placing caves — it's the fastest way to see why a hole didn't cut.")]
    public bool verbose = true;

    void Awake()
    {
        var body = GetComponentInParent<CelestialBody>();
        if (body == null)
        {
            Debug.LogWarning($"[CaveHoleBinder] '{name}' is not parented under a CelestialBody — " +
                             "no terrain will be cut. Drag the cave onto the planet's transform.", this);
            return;
        }

        var puncher = body.GetComponent<PlanetHolePuncher>();
        if (puncher == null)
        {
            puncher = body.gameObject.AddComponent<PlanetHolePuncher>();
            if (verbose)
                Debug.Log($"[CaveHoleBinder] Added a PlanetHolePuncher to '{body.bodyName}' for cave '{name}'.", body);
        }
        else if (verbose)
        {
            Debug.Log($"[CaveHoleBinder] '{body.bodyName}' already has a PlanetHolePuncher — cave '{name}' will be cut by it.", body);
        }

        if (GetComponentInChildren<TerrainHole>(true) == null)
            Debug.LogWarning($"[CaveHoleBinder] Cave '{name}' has no TerrainHole child — " +
                             "nothing will be cut and the entrance will stay sealed.", this);
    }
}
