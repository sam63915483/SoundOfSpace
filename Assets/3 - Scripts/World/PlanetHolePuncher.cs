using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Cuts openings in a procedurally generated celestial body's terrain so you can
// enter it — cave mouths, shafts, or a tunnel bored clean through a moon.
//
// WHY THIS EXISTS
// The planets are heightfields: CelestialBodyGenerator builds an icosphere and
// pushes each vertex out by a single height sampled from a compute shader
// (one radius per direction). That topology can't express an overhang, let
// alone a cave — so a hole can't come from the generator. It has to be cut out
// afterwards, which is what this does.
//
// HOW IT CUTS
// It does NOT boolean-subtract geometry. It rebuilds the triangle index list,
// dropping any triangle with a vertex inside a TerrainHole volume, and leaves
// the vertex array untouched. That keeps normals, tangents and the shading data
// the generator packs into UV0 exactly valid with no remapping — the surviving
// terrain shades identically to before. A few orphaned vertices stay in the
// buffer; they cost a little memory and nothing else.
//
// WHAT IT HAS TO CUT (all of it, or the hole doesn't work)
//   • All 3 LOD meshes. SetLOD swaps terrainMeshFilter.sharedMesh between them,
//     so punching only LOD0 means the hole seals itself when you back away.
//   • The collision mesh. Bodies with world radius >= 150 reuse LOD0 as their
//     collider; smaller ones get a separate low-res collisionMesh. Rather than
//     guess which branch a body took, this reads MeshCollider.sharedMesh
//     directly and punches whatever is actually there.
//   • The BodyPlaceholder collider, if present. That component leaves a smooth,
//     unperturbed sphere MeshCollider on "Mesh Holder/Mesh" with its renderer
//     disabled — an invisible wall that would block the hole even after the
//     real terrain is cut. Easy to forget because you can't see it.
//
// It reaches the generator's private lodMeshes/collisionMesh fields by
// reflection on purpose: CelestialBodyGenerator lives in the DO-NOT-TOUCH
// Celestial/ generation zone (CLAUDE.md trap #2), so it is not edited here.
// AtmosphereReloadFix.cs uses reflection against the same zone for the same
// reason.
//
// Gravity inside the body is handled separately by Universe.GravityAcceleration.
[RequireComponent (typeof (CelestialBody))]
public class PlanetHolePuncher : MonoBehaviour {
    // Diagnostic chatter switch. static readonly (never const) so the guarded
    // bodies don't become CS0162 unreachable code — see FeatureVault.cs.
    // Flip to true when investigating cave-hole cutting per mesh.
    static readonly bool Verbose = false;


	[Tooltip ("Also punch the BodyPlaceholder's collider (the invisible smooth sphere on 'Mesh Holder/Mesh'). Leave ON — with it off you'll hit an invisible wall where the hole should be.")]
	public bool punchPlaceholderCollider = true;

	[Tooltip ("Log a per-mesh breakdown of how many triangles each hole removed. Worth leaving on while you're tuning hole size.")]
	public bool verboseLogging = true;

	[Tooltip ("Pull the cut edge onto the hole's wall so the opening matches the marker exactly instead of being a ragged triangle-edge polygon. Turn off to see the raw cut. Cylinder holes only.")]
	public bool snapRimToHole = true;

	[Tooltip ("How many frames to wait for the terrain to appear before logging a warning. This is only a warning threshold — the puncher keeps watching indefinitely and cuts the hole whenever terrain shows up.")]
	public int maxWaitFrames = 300;

	const BindingFlags kPrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

	// A mesh plus the transform whose space its vertices live in. Vertices are
	// tested in WORLD space (transform each one up front) rather than converting
	// the hole into mesh space — that way rotation and scale are handled for
	// free and there's no uniform-scale assumption to get wrong.
	struct Target {
		public Mesh mesh;
		public Transform space;
		public string label;
	}

	bool _punched;

	void Start () {
		StartCoroutine (PunchWhenReady ());
	}

	IEnumerator PunchWhenReady () {
		var holes = GetComponentsInChildren<TerrainHole> (true);
		if (holes.Length == 0) {
			Debug.LogWarning ($"[PlanetHolePuncher] '{name}' has no TerrainHole children — nothing to cut.", this);
			yield break;
		}

		var generator = GetComponentInChildren<CelestialBodyGenerator> (true);
		if (generator == null) {
			Debug.LogError ($"[PlanetHolePuncher] '{name}' has no CelestialBodyGenerator in its children.", this);
			yield break;
		}

		// Wait for the generator to finish building. It runs in Start(), and
		// Start order between components is undefined, so poll for the child it
		// creates instead of racing it.
		//
		// This polls indefinitely rather than giving up. Terrain can legitimately
		// appear late — or not at all on the first attempt: CelestialBodyGenerator
		// silently logs "Could not generate mesh" and gives up whenever
		// ComputeHelper.CanRunEditModeCompute is false at Start (which happens if
		// the Editor is mid-compile as you enter play mode). If something later
		// regenerates the body, we still want the hole cut, so keep watching.
		Transform terrain = null;
		int frames = 0;
		bool warned = false;
		while (true) {
			terrain = generator.transform.Find ("Terrain Mesh");
			if (terrain != null && terrain.GetComponent<MeshCollider> () != null) break;

			if (!warned && ++frames > maxWaitFrames) {
				warned = true;
				Debug.LogWarning (
					$"[PlanetHolePuncher] '{name}': no 'Terrain Mesh' after {maxWaitFrames} frames — the body hasn't generated. " +
					"Check the Console for \"Could not generate mesh\": CelestialBodyGenerator bails at Start when the Editor " +
					"is compiling, and never retries. Re-entering play mode with no compile pending usually fixes it. " +
					"Still watching, and will cut the hole the moment terrain appears.", this);
			}
			yield return null;
		}

		Punch (generator, terrain, holes);
	}

	[ContextMenu ("Re-punch now")]
	public void RePunch () {
		var generator = GetComponentInChildren<CelestialBodyGenerator> (true);
		var terrain = generator != null ? generator.transform.Find ("Terrain Mesh") : null;
		if (terrain == null) {
			Debug.LogWarning ("[PlanetHolePuncher] Nothing to re-punch — terrain not generated yet (play mode only).", this);
			return;
		}
		Punch (generator, terrain, GetComponentsInChildren<TerrainHole> (true));
	}

	void Punch (CelestialBodyGenerator generator, Transform terrain, TerrainHole[] holes) {
		var targets = new List<Target> ();
		var seen = new HashSet<int> ();
		var collidersToRecook = new List<MeshCollider> ();

		void AddTarget (Mesh m, Transform space, string label) {
			if (m == null || space == null) return;
			if (!seen.Add (m.GetInstanceID ())) return; // same Mesh reached two ways
			if (!m.isReadable) {
				Debug.LogWarning ($"[PlanetHolePuncher] '{label}' mesh is not readable — skipping.", this);
				return;
			}
			targets.Add (new Target { mesh = m, space = space, label = label });
		}

		// The visible mesh and the collider actually in use right now.
		var terrainFilter = terrain.GetComponent<MeshFilter> ();
		var terrainCollider = terrain.GetComponent<MeshCollider> ();
		if (terrainFilter != null) AddTarget (terrainFilter.sharedMesh, terrain, "Terrain Mesh (visible)");
		if (terrainCollider != null) {
			AddTarget (terrainCollider.sharedMesh, terrain, "Terrain Mesh (collider)");
			collidersToRecook.Add (terrainCollider);
		}

		// Every LOD, so the hole survives SetLOD swapping the mesh out.
		var lodField = typeof (CelestialBodyGenerator).GetField ("lodMeshes", kPrivateInstance);
		if (lodField != null && lodField.GetValue (generator) is Mesh[] lods) {
			for (int i = 0; i < lods.Length; i++) AddTarget (lods[i], terrain, $"LOD{i}");
		} else {
			Debug.LogWarning ("[PlanetHolePuncher] Couldn't read 'lodMeshes' by reflection — the hole may reseal at distance.", this);
		}

		// The separate low-res collision mesh used by bodies under radius 150.
		var colField = typeof (CelestialBodyGenerator).GetField ("collisionMesh", kPrivateInstance);
		if (colField != null && colField.GetValue (generator) is Mesh cm) AddTarget (cm, terrain, "collisionMesh");

		// The invisible BodyPlaceholder sphere collider.
		if (punchPlaceholderCollider) {
			var placeholder = GetComponentInChildren<BodyPlaceholder> (true);
			if (placeholder != null) {
				var pmT = placeholder.transform.Find ("Mesh");
				if (pmT != null) {
					var pmc = pmT.GetComponent<MeshCollider> ();
					var pmf = pmT.GetComponent<MeshFilter> ();
					if (pmf != null) AddTarget (pmf.sharedMesh, pmT, "BodyPlaceholder (visible)");
					if (pmc != null) {
						AddTarget (pmc.sharedMesh, pmT, "BodyPlaceholder (collider)");
						if (pmc.enabled) collidersToRecook.Add (pmc);
					}
				}
			}
		}

		int totalRemoved = 0;
		foreach (var t in targets) {
			int removed = PunchMesh (t, holes);
			totalRemoved += removed;
			if (verboseLogging) {
				if (Verbose) Debug.Log ($"[PlanetHolePuncher] '{name}' / {t.label}: removed {removed} triangles.", this);
			}
		}

		// PhysX caches cooked collision data; mutating the Mesh doesn't
		// invalidate it. Nulling and reassigning forces a re-cook so the hole
		// exists for physics and not just for the eye.
		foreach (var mc in collidersToRecook) {
			var m = mc.sharedMesh;
			mc.sharedMesh = null;
			mc.sharedMesh = m;
		}

		_punched = true;
		Debug.Log ($"[PlanetHolePuncher] '{name}': cut {holes.Length} hole(s) across {targets.Count} mesh(es), {totalRemoved} triangles removed, {collidersToRecook.Count} collider(s) re-cooked.", this);

		if (totalRemoved == 0) {
			Debug.LogWarning ($"[PlanetHolePuncher] '{name}': nothing was removed. The markers are probably not crossing the surface, or their radius is smaller than one terrain triangle.", this);
		}
	}

	// Returns the number of triangles removed from this mesh.
	int PunchMesh (Target target, TerrainHole[] holes) {
		Mesh mesh = target.mesh;
		Vector3[] verts = mesh.vertices;

		// Transform every vertex to world space once, then flag the ones inside
		// any hole. Flagging vertices (rather than re-testing per triangle) means
		// each vertex is tested once instead of up to six times.
		var localToWorld = target.space.localToWorldMatrix;
		var inside = new bool[verts.Length];
		// Which hole swallowed each vertex, so the rim can later be snapped to
		// that specific hole's wall. -1 = not inside any hole.
		var holeOf = new int[verts.Length];
		for (int i = 0; i < holeOf.Length; i++) holeOf[i] = -1;
		int insideCount = 0;

		// Pre-resolve each hole's maths ONCE. Calling TerrainHole.Contains per
		// vertex would re-read transform.position / worldToLocalMatrix hundreds
		// of thousands of times, which dominates the cost.
		int holeCount = holes.Length;
		var isCylinder = new bool[holeCount];
		var worldToLocal = new Matrix4x4[holeCount];
		var holeLocalToWorld = new Matrix4x4[holeCount];
		var segA = new Vector3[holeCount];
		var segB = new Vector3[holeCount];
		var sqrR = new float[holeCount];
		for (int h = 0; h < holeCount; h++) {
			isCylinder[h] = holes[h].shape == TerrainHole.Shape.Cylinder;
			if (isCylinder[h]) {
				worldToLocal[h] = holes[h].transform.worldToLocalMatrix;
				holeLocalToWorld[h] = holes[h].transform.localToWorldMatrix;
			} else {
				holes[h].GetSegment (out segA[h], out segB[h]);
				sqrR[h] = holes[h].radius * holes[h].radius;
			}
		}

		for (int v = 0; v < verts.Length; v++) {
			Vector3 world = localToWorld.MultiplyPoint3x4 (verts[v]);
			for (int h = 0; h < holeCount; h++) {
				bool hit = isCylinder[h]
					? TerrainHole.InsideUnitCylinder (worldToLocal[h].MultiplyPoint3x4 (world))
					: TerrainHole.SqrDistanceToSegment (world, segA[h], segB[h]) <= sqrR[h];
				if (hit) {
					inside[v] = true;
					holeOf[v] = h;
					insideCount++;
					break;
				}
			}
		}

		if (insideCount == 0) return 0;

		// Drop a triangle if ANY of its corners is inside, so no triangle is left
		// straddling the boundary. That deliberately over-cuts by up to one
		// triangle — the rim snap below pulls the edge back to the exact shape.
		//
		// While cutting, record the RIM: vertices that survive but belong to a
		// deleted triangle. Those are exactly the ring of vertices bordering the
		// opening, and they're never more than one triangle away from it.
		int removed = 0;
		int subMeshes = mesh.subMeshCount;
		var rimHole = new int[verts.Length];
		for (int i = 0; i < rimHole.Length; i++) rimHole[i] = -1;

		var kept = new List<int> ();
		for (int s = 0; s < subMeshes; s++) {
			int[] tris = mesh.GetTriangles (s);
			kept.Clear ();
			if (kept.Capacity < tris.Length) kept.Capacity = tris.Length;
			for (int i = 0; i < tris.Length; i += 3) {
				int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
				if (inside[i0] || inside[i1] || inside[i2]) {
					removed++;
					// Attribute the rim to whichever hole actually cut here, so
					// two nearby holes never pull each other's edges.
					int h = inside[i0] ? holeOf[i0] : (inside[i1] ? holeOf[i1] : holeOf[i2]);
					if (!inside[i0]) rimHole[i0] = h;
					if (!inside[i1]) rimHole[i1] = h;
					if (!inside[i2]) rimHole[i2] = h;
					continue;
				}
				kept.Add (i0);
				kept.Add (i1);
				kept.Add (i2);
			}
			// Bounds are recalculated once at the end, after vertices move.
			mesh.SetTriangles (kept, s, false);
		}

		// Snap the rim onto the hole's wall.
		//
		// Without this the opening can only follow triangle edges, so it comes
		// out as a ragged polygon noticeably larger than the marker — the gap you
		// see around a tunnel. Sliding each rim vertex sideways onto the cylinder
		// (keeping its position ALONG the axis, so it stays at its own terrain
		// height) turns that polygon into the exact curve where the cylinder
		// meets the surface. If the tunnel enters at an angle that curve is an
		// ellipse, and this produces it correctly — it's the true intersection,
		// not an assumed circle.
		int snapped = 0;
		if (snapRimToHole) {
			var worldToMeshSpace = target.space.worldToLocalMatrix;
			for (int v = 0; v < verts.Length; v++) {
				int h = rimHole[v];
				if (h < 0 || !isCylinder[h]) continue;

				Vector3 local = worldToLocal[h].MultiplyPoint3x4 (localToWorld.MultiplyPoint3x4 (verts[v]));
				// Past the cylinder's ends there's no wall to snap to.
				if (local.y < -TerrainHole.CylinderLocalHalfHeight || local.y > TerrainHole.CylinderLocalHalfHeight) continue;

				float rXZ = Mathf.Sqrt (local.x * local.x + local.z * local.z);
				if (rXZ < 1e-6f) continue; // dead on the axis: no direction to push
				float k = TerrainHole.CylinderLocalRadius / rXZ;
				local.x *= k;
				local.z *= k;

				verts[v] = worldToMeshSpace.MultiplyPoint3x4 (holeLocalToWorld[h].MultiplyPoint3x4 (local));
				snapped++;
			}
			if (snapped > 0) mesh.vertices = verts;
		}

		mesh.RecalculateBounds ();
		// Normals are deliberately NOT recalculated: the moves are sub-triangle,
		// and recalculating would reshade the whole body and break the seam with
		// the generator's own normals.

		if (verboseLogging && snapped > 0) {
			if (Verbose) Debug.Log ($"[PlanetHolePuncher] '{name}' / {target.label}: snapped {snapped} rim vertices to the hole wall.", this);
		}

		return removed;
	}

	public bool HasPunched { get { return _punched; } }
}
