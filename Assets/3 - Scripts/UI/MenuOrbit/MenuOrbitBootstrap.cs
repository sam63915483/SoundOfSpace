using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MENU-ONLY: the single scene-authored component in MenuOrbit (the stripped
/// copy of the gameplay scene used as the main-menu 3D background). At Awake it
/// demilitarizes the copy — the player becomes a kinematic passenger inside the
/// shuttle, every gameplay behaviour on the shuttle is switched off, the
/// shuttle detaches from Humble Abode and takes over floating-origin
/// registration from the player — then starts MenuShuttleTour + MenuShotDirector.
/// The gameplay scene 1.6.7.7.7 never contains this component.
/// </summary>
public class MenuOrbitBootstrap : MonoBehaviour
{
    void Awake()
    {
        var playerPC = FindObjectOfType<PlayerController>(true);
        var shuttleT = FindShuttle();
        var cam = Camera.main;
        var endless = FindObjectOfType<EndlessManager>();
        if (playerPC == null || shuttleT == null || cam == null)
        {
            Debug.LogError($"[MenuOrbitBootstrap] missing pieces: player={playerPC != null} shuttle={shuttleT != null} cam={cam != null}");
            return;
        }
        Transform player = playerPC.transform;

        // Player: kinematic passenger, no controller, no colliders.
        playerPC.enabled = false;
        var prb = player.GetComponent<Rigidbody>();
        if (prb != null) { prb.isKinematic = true; prb.interpolation = RigidbodyInterpolation.None; }
        foreach (var col in player.GetComponentsInChildren<Collider>(true)) col.enabled = false;

        // Shuttle: detach from its planet, silence every gameplay behaviour on
        // it (autopilot, rider frame, monitors, doors, TRAX...). The player
        // subtree is excluded — its camera carries the atmosphere/ocean post
        // stack, which must stay alive.
        shuttleT.SetParent(null, true);
        foreach (var mb in shuttleT.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            if (mb.transform.IsChildOf(player)) continue;
            if (mb is MenuShuttleTour || mb is MenuShotDirector || mb is MenuOrbitBootstrap) continue;
            mb.enabled = false;
        }
        // Monitor/interior cameras on the shuttle render to textures nobody
        // will see — turn them off (the main camera is under the player).
        foreach (var c in shuttleT.GetComponentsInChildren<Camera>(true))
            if (c != cam) c.enabled = false;

        // Seat the player inside the shuttle (pod-ish) and hand origin-shift
        // duty to the shuttle: the player must NOT also be shifted directly or
        // it would move twice (it now rides the shuttle's hierarchy).
        player.SetParent(shuttleT, true);
        if ((player.position - shuttleT.position).sqrMagnitude > 40f * 40f)
            player.localPosition = Vector3.up * 2f;
        if (endless != null)
        {
            endless.UnregisterPhysicsObject(player);
            endless.RegisterPhysicsObject(shuttleT);
        }

        // No stray UI in the background scene. OWN SCENE ONLY — FindObjectsOfType
        // sees across loaded scenes, and the first version of this line
        // deactivated the MainMenu's canvas too, taking the menu buttons (and
        // the controller's running coroutines) down with it.
        foreach (var canvas in FindObjectsOfType<Canvas>(true))
            if (canvas.gameObject.scene == gameObject.scene) canvas.gameObject.SetActive(false);

        var tour = shuttleT.gameObject.AddComponent<MenuShuttleTour>();
        var director = cam.gameObject.AddComponent<MenuShotDirector>();
        director.tour = tour;
        director.cam = cam;
        Debug.Log("[MenuOrbitBootstrap] menu background armed: shuttle tour + shot director running");
    }

    Transform FindShuttle()
    {
        foreach (var t in FindObjectsOfType<Transform>(true))
            if (t.name == "Shuttle_Lander") return t;
        return null;
    }
}
