using UnityEngine;

// Marks a volume where a celestial body's terrain mesh + collider should be
// removed, so you can actually walk/fly into a cave, shaft or through-tunnel.
//
// Place one as a child of the CelestialBody (which needs a PlanetHolePuncher)
// and size it in the Scene view. The puncher deletes every terrain triangle
// with a vertex inside this volume — see PlanetHolePuncher for how and when.
//
// THE EASY WAY: drop a plain Unity Cylinder into the mouth of your tunnel,
// scale/rotate it until it plugs the opening the way you want the hole cut,
// add this component, and set shape = Cylinder. What you see in the Scene view
// is exactly what gets removed. Leave hideAtRuntime on and the cylinder makes
// itself invisible and non-solid on play, so it's purely an authoring tool.
//
// Shapes:
//   Cylinder — uses THIS transform as the volume (Unity's Cylinder primitive:
//              radius 0.5, height 2 in local space, so it lines up exactly with
//              a scaled cylinder mesh). Position/rotation/scale all respected.
//   Sphere   — a round opening; radius in world units, ignores the transform's scale.
//   Capsule  — a segment with a radius, which cuts an opening wherever it
//              crosses the surface. One marker can punch BOTH ends of a
//              through-tunnel, but it's harder to aim than two cylinders.
public class TerrainHole : MonoBehaviour {

	public enum Shape { Cylinder, Sphere, Capsule }

	[Tooltip ("Cylinder = use this transform's own volume (drop in a Unity Cylinder, scale it to fit the tunnel mouth). Sphere/Capsule use the radius+length fields below instead.")]
	public Shape shape = Shape.Cylinder;

	[Tooltip ("Hide this marker on play: disables its renderers and colliders so it only ever exists as an authoring guide, never as visible or solid geometry.")]
	public bool hideAtRuntime = true;

	[Tooltip ("Sphere/Capsule only: world-space radius of the cut. Ignored by Cylinder, which takes its size from the transform's scale.")]
	public float radius = 6f;

	[Tooltip ("Capsule only: total length of the segment, centred on this transform and running along the axis below.")]
	public float length = 130f;

	[Tooltip ("Capsule only: which local axis the segment runs along.")]
	public Axis axis = Axis.Forward;

	public enum Axis { Right, Up, Forward }

	public Vector3 AxisDirection {
		get {
			switch (axis) {
				case Axis.Right: return transform.right;
				case Axis.Up: return transform.up;
				default: return transform.forward;
			}
		}
	}

	// Unit dimensions of Unity's built-in Cylinder mesh, in local space.
	public const float CylinderLocalRadius = 0.5f;
	public const float CylinderLocalHalfHeight = 1f;

	// Live markers, maintained the CLAUDE.md way rather than by scanning. Read by
	// NoGrassVolume: terrain that has been cut away can't hold grass up, so every
	// hole is implicitly a no-grass volume.
	static readonly System.Collections.Generic.List<TerrainHole> s_all =
		new System.Collections.Generic.List<TerrainHole> ();
	public static System.Collections.Generic.IReadOnlyList<TerrainHole> All => s_all;

	void OnEnable () { if (!s_all.Contains (this)) s_all.Add (this); }
	void OnDisable () { s_all.Remove (this); }

	void Awake () {
		if (!hideAtRuntime) return;
		// Authoring guide only — never render, never collide.
		foreach (var r in GetComponentsInChildren<Renderer> (true)) r.enabled = false;
		foreach (var c in GetComponentsInChildren<Collider> (true)) c.enabled = false;
	}

	// Segment endpoints in world space. For a sphere both ends collapse to the
	// centre, so one point-to-segment test covers both shapes.
	public void GetSegment (out Vector3 a, out Vector3 b) {
		if (shape != Shape.Capsule) {
			a = b = transform.position;
			return;
		}
		Vector3 half = AxisDirection.normalized * (length * 0.5f);
		a = transform.position - half;
		b = transform.position + half;
	}

	// True if `worldPoint` lies inside the cut volume. Convenience for one-off
	// checks — PlanetHolePuncher caches the maths rather than calling this per
	// vertex, because each call would re-read the transform.
	public bool Contains (Vector3 worldPoint) {
		if (shape == Shape.Cylinder) {
			return InsideUnitCylinder (transform.worldToLocalMatrix.MultiplyPoint3x4 (worldPoint));
		}
		GetSegment (out Vector3 a, out Vector3 b);
		return SqrDistanceToSegment (worldPoint, a, b) <= radius * radius;
	}

	public static bool InsideUnitCylinder (Vector3 local) {
		if (local.y < -CylinderLocalHalfHeight || local.y > CylinderLocalHalfHeight) return false;
		return local.x * local.x + local.z * local.z <= CylinderLocalRadius * CylinderLocalRadius;
	}

	public static float SqrDistanceToSegment (Vector3 p, Vector3 a, Vector3 b) {
		Vector3 ab = b - a;
		float abSqr = ab.sqrMagnitude;
		if (abSqr < 1e-10f) return (p - a).sqrMagnitude; // degenerate = sphere
		float t = Mathf.Clamp01 (Vector3.Dot (p - a, ab) / abSqr);
		return (p - (a + ab * t)).sqrMagnitude;
	}

	void OnDrawGizmosSelected () {
		Gizmos.color = new Color (1f, 0.35f, 0.1f, 0.9f);

		if (shape == Shape.Cylinder) {
			// Draw in local space so scale/rotation are reflected exactly.
			Gizmos.matrix = transform.localToWorldMatrix;
			DrawLocalCircle (CylinderLocalHalfHeight);
			DrawLocalCircle (-CylinderLocalHalfHeight);
			for (int i = 0; i < 4; i++) {
				float ang = i * Mathf.PI * 0.5f;
				var o = new Vector3 (Mathf.Cos (ang) * CylinderLocalRadius, 0f, Mathf.Sin (ang) * CylinderLocalRadius);
				Gizmos.DrawLine (o + Vector3.up * CylinderLocalHalfHeight, o - Vector3.up * CylinderLocalHalfHeight);
			}
			Gizmos.matrix = Matrix4x4.identity;
			return;
		}

		GetSegment (out Vector3 a, out Vector3 b);
		Gizmos.DrawWireSphere (a, radius);
		if (shape == Shape.Capsule) {
			Gizmos.DrawWireSphere (b, radius);
			Gizmos.DrawLine (a, b);
			Vector3 dir = (b - a).normalized;
			Vector3 side = Vector3.Cross (dir, Mathf.Abs (dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized * radius;
			Vector3 side2 = Vector3.Cross (dir, side).normalized * radius;
			Gizmos.DrawLine (a + side, b + side);
			Gizmos.DrawLine (a - side, b - side);
			Gizmos.DrawLine (a + side2, b + side2);
			Gizmos.DrawLine (a - side2, b - side2);
		}
	}

	static void DrawLocalCircle (float y) {
		const int steps = 24;
		Vector3 prev = new Vector3 (CylinderLocalRadius, y, 0f);
		for (int i = 1; i <= steps; i++) {
			float ang = i / (float) steps * Mathf.PI * 2f;
			var next = new Vector3 (Mathf.Cos (ang) * CylinderLocalRadius, y, Mathf.Sin (ang) * CylinderLocalRadius);
			Gizmos.DrawLine (prev, next);
			prev = next;
		}
	}
}
