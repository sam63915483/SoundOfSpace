using UnityEditor;
using UnityEngine;

/// <summary>
/// Installs the fuel system into the shuttle
/// (docs/Handoff_PlanetEconomy_Fuel_Fishing_v2.md, Phase 2): the tank itself, and
/// a reactor you can walk up to and feed crystals into, exactly like the manual
/// ship's.
///
/// ⚠ <b>Shuttle_Lander.prefab is hand-maintained and must never be regenerated.</b>
/// This patches it in place through <c>LoadPrefabContents</c> / <c>SaveAsPrefabAsset</c>,
/// touching only the two things it owns, and is safe to re-run — a second run
/// finds both already there and changes nothing.
///
/// The reactor's settings are COPIED from the manual ship's reactor rather than
/// authored here, so both vehicles take crystals at the same rate, with the same
/// prompt and the same sound. One crystal = 5 fuel; a 100-unit tank is 20
/// crystals; 20 crystals is one 15 km jump.
///
/// Menu: <b>Tools ▸ Shuttle ▸ Install Fuel System</b>.
/// </summary>
public static class ShuttleFuelInstaller
{
    const string ShuttlePrefab = "Assets/1 - samsPrefabs/Shuttle_Lander.prefab";
    const string ShipPrefab    = "Assets/1 - samsPrefabs/SHIP44.prefab";
    const string ReactorName   = "Reactor";
    const string CabinAnchor   = "LightProbe (cabin centre)";

    [MenuItem("Tools/Shuttle/Install Fuel System")]
    public static void Install()
    {
        var root = PrefabUtility.LoadPrefabContents(ShuttlePrefab);
        if (root == null) { Debug.LogError("[ShuttleFuel] could not open " + ShuttlePrefab); return; }

        try
        {
            bool changed = false;

            // 1. The tank.
            var tank = root.GetComponent<ShuttleFuel>();
            if (tank == null) { tank = root.AddComponent<ShuttleFuel>(); changed = true; }

            // 2. The reactor you insert crystals into.
            var existing = FindDeep(root.transform, ReactorName);
            GameObject reactorGo;
            if (existing != null)
            {
                reactorGo = existing.gameObject;
            }
            else
            {
                reactorGo = new GameObject(ReactorName);
                reactorGo.transform.SetParent(root.transform, false);
                reactorGo.transform.localPosition = ProposeSpot(root);
                changed = true;
            }

            var box = reactorGo.GetComponent<BoxCollider>();
            if (box == null) { box = reactorGo.AddComponent<BoxCollider>(); changed = true; }
            box.isTrigger = true;
            if (box.size == Vector3.one) box.size = new Vector3(1.4f, 2f, 1.4f);

            var reactor = reactorGo.GetComponent<ShipReactor>();
            if (reactor == null) { reactor = reactorGo.AddComponent<ShipReactor>(); changed = true; }

            // Match the manual ship's reactor exactly — rate, prompt, sound.
            var shipSrc = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefab);
            if (shipSrc != null)
            {
                var srcReactor = shipSrc.GetComponentInChildren<ShipReactor>(true);
                if (srcReactor != null) EditorUtility.CopySerialized(srcReactor, reactor);
            }
            // ...but the shuttle has no Ship. Cleared so ShipReactor falls through
            // to the ShuttleFuel tank on this same prefab.
            reactor.ship = null;
            reactor.glow = null;

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, ShuttlePrefab);
                AssetDatabase.SaveAssets();
            }

            float perJump = tank.fuelMax;
            int crystalsForFull = Mathf.CeilToInt(tank.fuelMax / Mathf.Max(0.01f, reactor.fuelPerCrystal));
            Debug.Log(
                $"[ShuttleFuel] {(changed ? "INSTALLED" : "already installed — nothing changed")}.\n" +
                $"  Tank      : ShuttleFuel on '{root.name}' — {tank.fuelMax:F0} units, " +
                $"max jump {tank.maxJumpKm:F0} km, launch+land {tank.launchLandCost:F0}.\n" +
                $"  Reactor   : '{ReactorName}' at local {reactorGo.transform.localPosition} " +
                $"(trigger {box.size}) — MOVE THIS wherever it looks right, Sam.\n" +
                $"  Economy   : {reactor.fuelPerCrystal:F0} fuel per crystal → " +
                $"{crystalsForFull} crystals fills the tank = one {tank.maxJumpKm:F0} km jump ({perJump:F0} units).\n" +
                $"  New game  : starts at {tank.newGameFuel:F0}, intro burns {tank.introApproachBurn:F0} → " +
                $"lands with about {tank.newGameFuel - tank.introApproachBurn:F0} " +
                $"(range ~{Mathf.Max(0f, (tank.newGameFuel - tank.introApproachBurn - tank.launchLandCost) / ((tank.fuelMax - tank.launchLandCost) / tank.maxJumpKm)):F1} km).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>A sane first home for the reactor: beside the cabin-centre marker,
    /// down at floor height. Sam moves it; this only has to not be inside a wall.</summary>
    static Vector3 ProposeSpot(GameObject root)
    {
        var anchor = FindDeep(root.transform, CabinAnchor);
        if (anchor == null) return new Vector3(0f, 1f, 0f);
        var p = root.transform.InverseTransformPoint(anchor.position);
        return new Vector3(p.x + 1.2f, p.y - 0.9f, p.z);
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }
}
