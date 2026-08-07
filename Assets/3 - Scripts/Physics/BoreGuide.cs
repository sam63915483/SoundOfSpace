using UnityEngine;

// Keeps the player gliding down the middle of a tunnel instead of slamming into
// its walls.
//
// THE PROBLEM
// Falling through a bore under gravity, any sideways drift builds until you
// clip a wall. Even with zero friction, that contact is inelastic: the
// into-the-wall part of your velocity is absorbed, and the jolt costs speed and
// control. Rotating as you pass a planet's core makes it worse, because your
// body swings and catches the wall side-on.
//
// THE FIX
// Rather than making the impacts cheaper, avoid them. Every physics step this
// bleeds off only the RADIAL component of the player's velocity — motion across
// the bore — and leaves the AXIAL component (down the tunnel) completely
// untouched. You keep every bit of your fall speed; you just stop wandering into
// the walls. It's a soft assist, not a rail: you can still push sideways, it
// just settles again.
//
// PLACEMENT
// Put this on a GameObject sitting at the CENTRE of the bore with its local +Z
// running along the bore. Under a TunnelRig that means it inherits the rig's
// moves and rotations for free.
public class BoreGuide : MonoBehaviour {

	[Tooltip ("Inner radius of the bore. Used to decide whether the player is inside, and to scale the optional centring pull.")]
	public float boreRadius = 2.9f;

	[Tooltip ("Total length of the bore along local +Z.")]
	public float boreLength = 107f;

	[Tooltip ("Extra slack beyond the bore's radius and ends where the guide still applies, so it engages just before you enter rather than snapping on at the mouth.")]
	public float entryMargin = 2f;

	[Tooltip ("Seconds for sideways (across-the-bore) motion to decay to ~37%. LOWER = stronger correction. Around 0.5-0.8 removes wall-slamming while still feeling free; below ~0.2 starts to feel like a rail.")]
	public float radialDamping = 0.6f;

	[Tooltip ("Optional gentle pull toward the bore's centreline, in m/s². 0 = off (just damping, which is usually enough). A small value like 1-2 actively recentres you.")]
	public float centeringStrength = 0f;

	[Tooltip ("Log when the player enters and leaves the bore. Handy while tuning, noisy otherwise.")]
	public bool debugLogging = false;

	PlayerController player;
	Rigidbody playerRb;
	CelestialBody body;
	float nextSearchTime;
	bool wasInside;

	void Awake () {
		body = GetComponentInParent<CelestialBody> ();
	}

	// Cached, with a throttled retry — the player may not exist yet, and
	// FindObjectOfType must never run every physics step.
	void EnsurePlayer () {
		if (playerRb != null) return;
		if (Time.time < nextSearchTime) return;
		nextSearchTime = Time.time + 1f;
		player = FindObjectOfType<PlayerController> ();
		if (player != null) playerRb = player.GetComponent<Rigidbody> ();
	}

	void FixedUpdate () {
		EnsurePlayer ();
		if (playerRb == null) return;

		Vector3 axis = transform.forward;
		Vector3 offset = playerRb.position - transform.position;
		float along = Vector3.Dot (offset, axis);

		bool inside = Mathf.Abs (along) <= boreLength * 0.5f + entryMargin;
		Vector3 radialOffset = offset - axis * along;
		float dist = radialOffset.magnitude;
		inside &= dist <= boreRadius + entryMargin;

		if (inside != wasInside) {
			wasInside = inside;
			if (debugLogging) Debug.Log ($"[BoreGuide] player {(inside ? "entered" : "left")} '{name}'", this);
		}
		if (!inside) return;

		// Work relative to the body so its orbital velocity isn't treated as
		// drift — the moon is moving at ~51 m/s and damping that would drag the
		// player straight out of orbit.
		Vector3 refVel = body != null ? body.velocity : Vector3.zero;
		Vector3 rel = playerRb.velocity - refVel;

		// Split: along the bore (keep it all) vs across it (bleed it off).
		Vector3 axialVel = axis * Vector3.Dot (rel, axis);
		Vector3 radialVel = rel - axialVel;

		radialVel *= Mathf.Exp (-Time.fixedDeltaTime / Mathf.Max (0.0001f, radialDamping));

		if (centeringStrength > 0f && dist > 0.001f) {
			// Pull harder the closer you are to the wall.
			float pull = centeringStrength * Mathf.Clamp01 (dist / Mathf.Max (0.0001f, boreRadius));
			radialVel -= radialOffset / dist * (pull * Time.fixedDeltaTime);
		}

		playerRb.velocity = refVel + axialVel + radialVel;
	}

	void OnDrawGizmosSelected () {
		Gizmos.matrix = transform.localToWorldMatrix;
		float half = boreLength * 0.5f;

		Gizmos.color = new Color (0.3f, 1f, 0.5f, 0.9f);
		DrawRing (boreRadius, -half);
		DrawRing (boreRadius, half);
		Gizmos.DrawLine (new Vector3 (0f, 0f, -half), new Vector3 (0f, 0f, half));

		Gizmos.color = new Color (0.3f, 1f, 0.5f, 0.25f);
		DrawRing (boreRadius + entryMargin, -half - entryMargin);
		DrawRing (boreRadius + entryMargin, half + entryMargin);
		Gizmos.matrix = Matrix4x4.identity;
	}

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
