using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameSetUp : MonoBehaviour {
	public enum StartCondition { InShip, OnBody, AtSpawnPoint }

	public StartCondition startCondition;
	public CelestialBody startBody;

	void Start () {
		// Never grab a mission-prop ship (e.g. Tevsship, a scene-authored second
		// Ship parked on Humble Abode): auto-piloting it at startup seats the
		// player in the wrong cockpit. Historically no scene ship existed at
		// Start, so FindObjectOfType returned null and InShip was skipped.
		Ship ship = null;
		foreach (Ship s in FindObjectsOfType<Ship> ()) {
			if (s.GetComponent<TevSmugglingMission> () != null) continue;
			ship = s;
			break;
		}
		PlayerController player = FindObjectOfType<PlayerController> (true);

		if (startCondition == StartCondition.InShip) {
			// Guard: the ship is spawned at runtime, so it may not exist yet on
			// some load paths (e.g. a level-portal return). The save system owns
			// the real piloting state via Ship.ForceExitPilot, so skipping the
			// auto-pilot here when there's no ship is safe.
			if (ship != null) {
				ship.PilotShip ();
				ship.flightControls.ForcePlayerInInteractionZone ();
			}
		} else if (startCondition == StartCondition.OnBody) {
			if (startBody && player != null && ship != null) {
				Vector3 pointAbovePlanet = startBody.transform.position + Vector3.right * startBody.radius * 1.1f;
				player.transform.position = pointAbovePlanet;
				player.SetVelocity (startBody.initialVelocity);
				ship.transform.position = pointAbovePlanet + Vector3.right * 20;
				ship.SetVelocity (startBody.initialVelocity);
				ship.ToggleHatch ();
			}
		} else if (startCondition == StartCondition.AtSpawnPoint) {
			if (player != null) {
				player.gameObject.SetActive (true);
				if (player.spawnPoint != null) {
					player.transform.position = player.spawnPoint.position;
					player.transform.rotation = player.spawnPoint.rotation;
					CelestialBody body = player.spawnPoint.GetComponentInParent<CelestialBody> ();
					player.SetVelocity (body != null ? body.velocity : Vector3.zero);
				}
			}
		}
	}
}