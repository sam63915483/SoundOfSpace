using System.Collections.Generic;
using UnityEngine;

// Marks colliders the player gets NO traction on — slide tubes, chutes, ice.
//
// You still land on a slick surface and can still walk and jump off it; what's
// removed is the friction. PlayerController normally does two things on contact
// that a slide tube must not do:
//   • cancels along-surface velocity outright below slideStopSpeed, so a slow
//     drift stops dead the instant you brush a wall;
//   • hands landing momentum to the walk pipeline, where SmoothDamp bleeds it to
//     zero as soon as you stop holding a direction.
// Both are skipped on a surface tagged with this, so momentum carries through.
//
// Attach to the root of the geometry (a pipe, a chute) and leave includeChildren
// on — every collider underneath is registered.
//
// Registration follows the project's static-instance-list convention: colliders
// are added in OnEnable and removed in OnDisable, so the lookup PlayerController
// does every physics step is a hash-set hit rather than a GetComponentInParent
// walk (which would be a per-FixedUpdate allocation-free but cache-hostile climb
// up the hierarchy).
public class SlickSurface : MonoBehaviour {

	[Tooltip ("Register every collider on this object's children too, not just the ones on this GameObject. Normally what you want — a pipe's geometry usually sits on child meshes.")]
	public bool includeChildren = true;

	[Tooltip ("Also give these colliders a zero-friction physics material at runtime. Without this the PLAYER CODE stops braking you but PhysX itself still scrubs speed off every graze — Unity's default material has 0.6 friction. Leave on for slide tubes.")]
	public bool frictionlessPhysics = true;

	static readonly HashSet<Collider> Slick = new HashSet<Collider> ();

	Collider[] registered;

	// One shared zero-friction material for every slick surface in the game.
	// Combine = Minimum means the LOWER of the two materials wins, so contact
	// with the player is frictionless no matter what the player's own collider
	// is set to — otherwise Unity would average the two and still brake.
	static PhysicMaterial frictionless;
	static PhysicMaterial Frictionless {
		get {
			if (frictionless == null) {
				frictionless = new PhysicMaterial ("SlickSurface (runtime)") {
					dynamicFriction = 0f,
					staticFriction = 0f,
					frictionCombine = PhysicMaterialCombine.Minimum,
					bounciness = 0f,
					bounceCombine = PhysicMaterialCombine.Minimum,
					hideFlags = HideFlags.HideAndDontSave
				};
			}
			return frictionless;
		}
	}

	void OnEnable () {
		registered = includeChildren ? GetComponentsInChildren<Collider> (true) : GetComponents<Collider> ();
		for (int i = 0; i < registered.Length; i++) {
			if (registered[i] == null) continue;
			Slick.Add (registered[i]);
			// Runtime-only: assigns the collider's material reference, never
			// touches an asset on disk, and reverts when play mode ends.
			if (frictionlessPhysics) registered[i].sharedMaterial = Frictionless;
		}
	}

	void OnDisable () {
		if (registered == null) return;
		for (int i = 0; i < registered.Length; i++) {
			if (registered[i] != null) Slick.Remove (registered[i]);
		}
		registered = null;
	}

	/// <summary>True if this collider belongs to a surface the player can't grip.</summary>
	public static bool IsSlick (Collider c) {
		return c != null && Slick.Count > 0 && Slick.Contains (c);
	}

	/// <summary>Re-scan children. Call after adding or removing colliders at runtime.</summary>
	public void Refresh () {
		OnDisable ();
		OnEnable ();
	}
}
