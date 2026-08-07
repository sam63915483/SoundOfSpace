using UnityEngine;

// A single handle for a whole tunnel assembly: the tube mesh, its lights, and
// the TerrainHole markers that cut its mouths. Move or rotate THIS and
// everything travels together, so the openings in the planet stay lined up with
// the pipe.
//
// Why it exists: the tube mesh's own pivot sits ~215 units away from its
// geometry, so dragging or scaling the tube directly swings the mesh sideways
// and desynchronises it from the lights and the hole markers. This rig sits at
// the PLANET'S CENTRE with uniform scale 1, which makes rotation behave the way
// you'd want — swing it and the tunnel sweeps through the body with both mouths
// staying on the surface.
//
// Local axes:
//   +Z  runs along the bore (the direction the tunnel is drilled)
//   +Y  points at the wall strip the lights are mounted on
// So: rotate around X or Y to re-aim the tunnel through the planet, and around
// Z to roll the lights to a different side of the bore.
//
// RULES
//   • Keep localScale at 1,1,1. Scaling the rig shears the tube, which already
//     carries a non-uniform scale of its own.
//   • To change the bore's THICKNESS, scale the tube's local X/Y — never the
//     rig — and re-fit the hole markers to match.
//   • After moving it, just press Play: PlanetHolePuncher re-cuts the terrain
//     from the markers' new positions every time, so nothing needs redoing.
public class TunnelRig : MonoBehaviour {

	[Tooltip ("Length of the bore, drawn by the gizmo along local +Z. Display only — it doesn't move anything.")]
	public float boreLength = 106.5f;

	[Tooltip ("Inner radius of the bore, drawn by the gizmo. Display only — it doesn't resize anything.")]
	public float boreRadius = 2.975f;

	[Tooltip ("Radius of the body being drilled, drawn as a reference circle so you can see where the mouths land while aiming.")]
	public float bodyRadius = 50f;

	void OnDrawGizmosSelected () {
		Gizmos.matrix = transform.localToWorldMatrix;

		float half = boreLength * 0.5f;

		// The bore itself.
		Gizmos.color = new Color (0.2f, 0.9f, 1f, 0.9f);
		Gizmos.DrawLine (new Vector3 (0f, 0f, -half), new Vector3 (0f, 0f, half));
		DrawRing (boreRadius, -half);
		DrawRing (boreRadius, 0f);
		DrawRing (boreRadius, half);
		for (int i = 0; i < 4; i++) {
			float a = i * Mathf.PI * 0.5f;
			var o = new Vector3 (Mathf.Cos (a) * boreRadius, Mathf.Sin (a) * boreRadius, 0f);
			Gizmos.DrawLine (o + Vector3.forward * -half, o + Vector3.forward * half);
		}

		// Which way is "up" — the side the lights sit on.
		Gizmos.color = new Color (1f, 0.85f, 0.2f, 0.9f);
		Gizmos.DrawLine (Vector3.zero, new Vector3 (0f, boreRadius * 2f, 0f));

		// The body being drilled, so you can see where the mouths will land.
		Gizmos.color = new Color (1f, 1f, 1f, 0.25f);
		DrawRing (bodyRadius, 0f);
		Gizmos.matrix = Matrix4x4.identity;
		Gizmos.DrawWireSphere (transform.position, bodyRadius);
	}

	// Circle in the local XY plane (perpendicular to the bore) at distance z.
	void DrawRing (float radius, float z) {
		const int steps = 32;
		Vector3 prev = new Vector3 (radius, 0f, z);
		for (int i = 1; i <= steps; i++) {
			float a = i / (float) steps * Mathf.PI * 2f;
			var next = new Vector3 (Mathf.Cos (a) * radius, Mathf.Sin (a) * radius, z);
			Gizmos.DrawLine (prev, next);
			prev = next;
		}
	}
}
