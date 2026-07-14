using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShipHUD : MonoBehaviour {

	[Header ("Aim")]
	public float dotSize = 1;
	public float minAimAngle = 30;
	public Image centreDot;
	public TMPro.TMP_Text planetName;
	public TMPro.TMP_Text planetInfo;
	public Vector2 surfaceDstFadeOutRange = new Vector2 (300, 150);

	[Header ("Velocity indicators")]
	public VelocityIndicator velocityHorizontal;
	public VelocityIndicator velocityVertical;
	public Vector2 velocityIndicatorSizeMinMax;
	public Vector2 velocityIndicatorThicknessMinMax;
	public float maxVisDst;
	public float velocityDisplayScale = 1;
	public Material velocityIndicatorMat;
	public Material arrowHeadMat;

	/// Immovable mission marker. While set, the lock-on stays glued to this
	/// body: clicking it doesn't unmark it, clicking elsewhere doesn't move
	/// it, and the map is force-synced back to it. Missions set it (B-1 pins
	/// Fiery Twin from first pilot until touchdown) and clear it when done.
	/// A destroyed body (scene reload) reads as null, which is the same as
	/// cleared — no reset hook needed.
	public static CelestialBody MissionPin;

	CelestialBody lockedBody;
	Camera cam;
	Transform camT;
	LockOnUI lockOnUI;
	Ship ship;
	CelestialBody aimedBody;

	// Change-detection state for the planet HUD strings — see DrawPlanetHUD.
	string _lastBodyName;
	int _lastDistShown = int.MinValue;
	int _lastVelShown  = int.MinValue;
	int _lastAlphaQ    = -1;

	void Start () {
		velocityHorizontal.line.material = new Material (velocityIndicatorMat);
		velocityHorizontal.head.material = new Material (arrowHeadMat);
		velocityVertical.line.material = new Material (velocityIndicatorMat);
		velocityVertical.head.material = new Material (arrowHeadMat);
	}

	// Direct LateUpdate instead of subscribing to EndlessManager's
	// PostFloatingOriginUpdate event. The event approach silently breaks if
	// the manager isn't found in Awake order — and a missed UpdateUI means
	// the lock-on never draws, even when the player is piloting and a body
	// is marked. LateUpdate also runs AFTER the floating origin shift in
	// EndlessManager.LateUpdate, so the flicker-during-shift concern that
	// motivated the original event subscription is still avoided.
	void LateUpdate () => UpdateUI ();

	void Init () {
		if (cam == null) {
			cam = Camera.main;
		}
		camT = cam.transform;

		if (lockOnUI == null) {
			lockOnUI = GetComponent<LockOnUI> ();
		}

		// Bind to the CURRENTLY piloted ship via Ship.PilotedInstance — the
		// cached static is set/cleared on pilot enter/exit, so it tracks
		// cockpit swaps without scanning every Ship in the scene every frame.
		ship = Ship.PilotedInstance;

		EnsureDepthCanvas ();
	}

	// Camera-space canvas that hosts the world-anchored marker elements (the
	// planet label + the velocity arrows). Unlike the overlay canvas they
	// were authored on, this one renders inside the 3D pass with real scene
	// depth, so the cockpit hull occludes the marker per-pixel while the
	// canopy glass (transparent, no depth write) stays see-through. The plane
	// sits at 40 units — beyond the hull, closer than anything the marker
	// should hide behind.
	Canvas depthCanvas;
	Transform overlayLabelParent;   // original parent — the edge pointer moves back here

	void EnsureDepthCanvas () {
		if (depthCanvas != null || cam == null) return;

		var go = new GameObject ("ShipHUD_DepthCanvas");
		depthCanvas = go.AddComponent<Canvas> ();
		depthCanvas.renderMode = RenderMode.ScreenSpaceCamera;
		depthCanvas.worldCamera = cam;
		depthCanvas.planeDistance = 40f;
		var scaler = go.AddComponent<CanvasScaler> ();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2 (1920f, 1080f);

		overlayLabelParent = planetName.transform.parent;
		planetName.transform.SetParent (go.transform, false);
		velocityHorizontal.line.transform.SetParent (go.transform, false);
		velocityHorizontal.head.transform.SetParent (go.transform, false);
		velocityVertical.line.transform.SetParent (go.transform, false);
		velocityVertical.head.transform.SetParent (go.transform, false);
	}

	/// The label lives on the depth canvas while anchored to the planet
	/// (hull occludes it) and hops back to the overlay canvas while acting
	/// as the mission pin's screen-edge pointer (must never be hidden).
	void SetLabelDepthTested (bool depthTested) {
		Transform want = depthTested ? depthCanvas.transform : overlayLabelParent;
		if (planetName.transform.parent != want)
			planetName.transform.SetParent (want, false);
	}

	void UpdateUI () {
		Init ();

		centreDot.rectTransform.localScale = Vector3.one * dotSize;

		// Suppress ShipHUD entirely while the map is open. The map draws
		// its own lock-on brackets via SolarSystemMapController.LateUpdate,
		// and ShipHUD's screen-space labels / arrows are anchored against
		// the SHIP camera — which is still positioned inside the cockpit
		// behind the map view, putting random label hitboxes at the top of
		// the screen and silently eating clicks on the teleport-to-pilot
		// button (raycastTarget=true on default Text/Image).
		if (ship != null && ship.ShowHUD && !PlayerController.isMapOpen) {

			// Sync from shared "marked body" state. If the player clicked a
			// body in the map (or via the legend) while not in the ship,
			// pendingHighlight holds it. Read here so the lock-on persists
			// across map ↔ ship transitions.
			var mapCtl = SolarSystemMapController.Instance;
			if (mapCtl != null) lockedBody = mapCtl.PendingHighlight;

			// Mission pin overrides everything: whatever the player clicked
			// (here or in the map), the marker snaps back to the pinned body.
			if (MissionPin != null && lockedBody != MissionPin) {
				lockedBody = MissionPin;
				if (mapCtl != null) mapCtl.SetMarkedBody (MissionPin);
			}

			if (Time.timeScale != 0) {
				aimedBody = FindAimedBody ();

				// LMB (kbm) or RT-trigger pull (controller). FirePressed wraps
				// both inputs; while piloting, neither is used by anything else
				// on the ship so this binding is conflict-free. Swallow it while
				// a conversation is open — clicking through dialogue while aimed
				// at a planet was toggling its mark. Swallowed entirely while a
				// mission pin is active — that marker can't be moved or cleared.
				if (!WorldDialogueUI.IsOpen && MissionPin == null && TutorialGate.FirePressed ()) {
					var newLock = (lockedBody == aimedBody) ? null : aimedBody;
					lockedBody = newLock;
					if (mapCtl != null) mapCtl.SetMarkedBody (newLock);
				}
			}

			if (aimedBody && aimedBody != lockedBody) {
				lockOnUI.DrawLockOnUI (aimedBody, false);
			}

			// Occlusion is per-pixel: the brackets' LockOnRing shader and the
			// label/arrows (moved onto the camera-space canvas in Init) all
			// ZTest against the scene, so the cabin hull covers them and the
			// canopy glass (no depth write) doesn't.
			if (lockedBody) {
				lockOnUI.DrawLockOnUI (lockedBody, true);
				DrawPlanetHUD (lockedBody);
			} else {
				SetHudActive (false);
			}
		} else {
			SetHudActive (false);
		}
	}

	void SetHudActive (bool active) {
		planetName.gameObject.SetActive (active);
		velocityHorizontal.SetActive (active);
		velocityVertical.SetActive (active);
	}

	void DrawPlanetHUD (CelestialBody planet) {
		SetHudActive (true);
		Vector3 dirToPlanet = (planet.transform.position - camT.position).normalized;
		float dstToPlanetCentre = (planet.transform.position - camT.position).magnitude;
		float dstToPlanetSurface = dstToPlanetCentre - planet.radius;

		// Calculate horizontal/vertical axes relative to direction toward planet
		Vector3 horizontal = Vector3.Cross (dirToPlanet, camT.up).normalized;
		horizontal *= Mathf.Sign (Vector3.Dot (horizontal, camT.right)); // make sure roughly same direction as right vector of cam
		Vector3 vertical = Vector3.Cross (dirToPlanet, horizontal).normalized;
		vertical *= Mathf.Sign (Vector3.Dot (vertical, camT.up));

		// Calculate relative velocity
		Vector3 relativeVelocityWorldSpace = ship.Rigidbody.velocity - planet.velocity;
		//Debug.Log(relativeVelocityWorldSpace +"   player: " + player.velocity + "  planet: " + planet.Velocity);
		float vx = -Vector3.Dot (relativeVelocityWorldSpace, horizontal);
		float vy = -Vector3.Dot (relativeVelocityWorldSpace, vertical);
		float vz = Vector3.Dot (relativeVelocityWorldSpace, dirToPlanet);
		Vector3 relativeVelocity = new Vector3 (vx, vy, vz);

		// Planet info — change-detected so the TMP text strings only rebuild
		// when the integer-rounded values actually change. Was allocating
		// ~3.7 KB / frame from $"..." interpolation + TMP text setter +
		// color struct creation — ~370 KB/sec of GC churn while piloting.
		Vector3 planetInfoWorldPos = planet.transform.position + horizontal * planet.radius * lockOnUI.lockedRadiusMultiplier + vertical * planet.radius * 0.35f;
		bool labelOnScreen = PointIsOnScreen (planetInfoWorldPos);
		// A mission-pinned body must stay findable even when it's off-screen
		// or hidden behind the cabin (mid-chase it can end up anywhere): hug
		// the label to the screen edge in the body's direction — on the
		// overlay canvas, where nothing occludes it — so it doubles as a
		// pointer. The world-anchored label depth-tests behind the hull.
		bool edgeMode = planet == MissionPin &&
			(!labelOnScreen || OccludedByOwnShip (planet.transform.position));
		planetName.gameObject.SetActive (labelOnScreen || edgeMode);
		SetLabelDepthTested (!edgeMode);
		planetName.rectTransform.localPosition = edgeMode
			? CalculateEdgeUIPos (planet.transform.position)
			: CalculateUIPos (planetInfoWorldPos);
		if (planet.bodyName != _lastBodyName) {
			_lastBodyName = planet.bodyName;
			planetName.text = planet.bodyName;
		}
		int distRounded = Mathf.RoundToInt (dstToPlanetSurface);
		int velRounded  = Mathf.RoundToInt (relativeVelocity.z);
		if (distRounded != _lastDistShown || velRounded != _lastVelShown) {
			_lastDistShown = distRounded;
			_lastVelShown  = velRounded;
			planetInfo.text = FormatDistance (dstToPlanetSurface) + " \n" + velRounded + "m/s";
		}

		float alpha = Mathf.InverseLerp (surfaceDstFadeOutRange.y, surfaceDstFadeOutRange.x, dstToPlanetSurface);
		// Color assignment to TMP rebuilds vertex colors — only do it when
		// the alpha actually changed (rounded to a perceptible step).
		int alphaQ = Mathf.RoundToInt (alpha * 255f);
		if (alphaQ != _lastAlphaQ) {
			_lastAlphaQ = alphaQ;
			float a = alphaQ / 255f;
			var c1 = planetName.color; c1.a = a; planetName.color = c1;
			var c2 = planetInfo.color; c2.a = a; planetInfo.color = c2;
		}

		// Relative velocity lines
		if (PointIsOnScreen (planet.transform.position)) {
			float arrowHeadSizePercent = dstToPlanetSurface / maxVisDst;
			//Debug.Log (arrowHeadSizePercent);
			float arrowHeadSize = Mathf.Lerp (velocityIndicatorSizeMinMax.y, velocityIndicatorSizeMinMax.x, arrowHeadSizePercent);
			float indicatorThickness = Mathf.Lerp (velocityIndicatorThicknessMinMax.y, velocityIndicatorThicknessMinMax.x, dstToPlanetSurface / maxVisDst);
			float indicatorAngle = (relativeVelocity.x < 0) ? 180 : 0;
			var indicatorPos = CalculateUIPos (planet.transform.position + horizontal * planet.radius * lockOnUI.lockedRadiusMultiplier * Mathf.Sign (relativeVelocity.x));
			float indicatorMagnitude = Mathf.Abs (relativeVelocity.x) * velocityDisplayScale;
			velocityHorizontal.Update (indicatorAngle, indicatorPos, indicatorMagnitude, arrowHeadSize, indicatorThickness);

			indicatorAngle = (relativeVelocity.y < 0) ? 270 : 90;
			indicatorPos = CalculateUIPos (planet.transform.position + camT.up * planet.radius * lockOnUI.lockedRadiusMultiplier * Mathf.Sign (relativeVelocity.y));
			indicatorMagnitude = Mathf.Abs (relativeVelocity.y) * velocityDisplayScale;
			velocityVertical.Update (indicatorAngle, indicatorPos, indicatorMagnitude, arrowHeadSize, indicatorThickness);

		} else {
			velocityHorizontal.SetActive (false);
			velocityVertical.SetActive (false);
		}

	}

	// Line-of-sight buffer — NonAlloc so the per-frame occlusion test doesn't
	// churn GC (this runs every LateUpdate while piloting).
	static readonly RaycastHit[] s_losHits = new RaycastHit[16];

	/// True when the sight line from the cockpit camera toward worldPos is
	/// blocked by the piloted ship's OWN hull — the marker should only be
	/// visible through the canopy. The canopy's solid collider is named
	/// "Window", so Window/Glass colliders count as see-through. Other
	/// ships/objects never occlude (the marker is a HUD aid, not a physical
	/// sight).
	bool OccludedByOwnShip (Vector3 worldPos) {
		if (ship == null || camT == null) return false;
		Vector3 offset = worldPos - camT.position;
		// The cabin hull is metres away — a short sweep is enough and keeps
		// the ray from wading through the whole scene.
		float dst = Mathf.Min (offset.magnitude, 60f);
		int n = Physics.RaycastNonAlloc (camT.position, offset.normalized, s_losHits, dst, ~0, QueryTriggerInteraction.Ignore);
		for (int i = 0; i < n; i++) {
			var col = s_losHits[i].collider;
			if (col == null) continue;
			if (col.GetComponentInParent<Ship> () != ship) continue;
			string cn = col.name;
			if (cn.IndexOf ("Window", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
				cn.IndexOf ("Glass", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
			return true;
		}
		return false;
	}

	CelestialBody FindAimedBody () {
		// Use the cached body array from NBodySimulation rather than
		// FindObjectsOfType<CelestialBody>() every frame (this method runs
		// every LateUpdate via the floating-origin event).
		CelestialBody[] bodies = NBodySimulation.Bodies;
		if (bodies == null) return null;
		CelestialBody aimedBody = null;

		Vector3 viewForward = cam.transform.forward;
		Vector3 viewOrigin = cam.transform.position;

		float nearestSqrDst = float.PositiveInfinity;

		// If aimed directly at any body, return the closest one
		foreach (var body in bodies) {
			if (body == null) continue;
			Vector3 intersection;
			if (MathUtility.RaySphere (body.transform.position, body.radius, viewOrigin, viewForward, out intersection)) {
				float sqrDst = (viewOrigin - intersection).sqrMagnitude;
				if (sqrDst < nearestSqrDst) {
					nearestSqrDst = sqrDst;
					aimedBody = body;
				}
			}
		}

		if (aimedBody) {
			return aimedBody;
		}

		// Return body with min angle to view direction
		float minAngle = minAimAngle * Mathf.Deg2Rad;

		foreach (var body in bodies) {
			if (body == null) continue;
			Vector3 offsetToBody = body.transform.position - cam.transform.position;
			float dstToBody = offsetToBody.magnitude;
			/*
			Vector3 viewPointNearPlanet = viewOrigin + viewForward * dstToBody;
			Vector3 closestSurfacePoint = body.transform.position + (viewPointNearPlanet - body.transform.position).normalized * body.radius;
			Vector3 dirToClosestSurfacePoint = (closestSurfacePoint - viewOrigin).normalized;
			float cosAngleToSurface = Vector3.Dot (dirToClosestSurfacePoint, viewForward);
			float aimAngle = Mathf.Acos (cosAngleToSurface);
			*/
			float aimAngle = Mathf.Acos (Vector3.Dot (viewForward, offsetToBody.normalized));

			if (aimAngle < minAngle) {
				minAngle = aimAngle;
				aimedBody = body;
			}
		}

		return aimedBody;
	}

	bool PointIsOnScreen (Vector3 worldPoint) {
		Vector3 p = cam.WorldToViewportPoint (worldPoint);
		return p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1 && p.z > 0;
	}

	static string FormatDistance (float distance) {
		const int maxMetreDst = 1000;
		string dstString = (distance < maxMetreDst) ? (int) distance + "m" : $"{distance/1000:0}km";
		return dstString;
	}

	Vector3 CalculateRelativeVelocity (CelestialBody body) {
		Vector3 dirToBody = (body.transform.position - camT.position).normalized;
		Vector3 relativeVelocityWorldSpace = ship.Rigidbody.velocity - body.velocity;

		// Calculate horizontal/vertical axes relative to direction toward planet
		Vector3 horizontal = Vector3.Cross (dirToBody, camT.up).normalized;
		horizontal *= Mathf.Sign (Vector3.Dot (horizontal, camT.right)); // make sure roughly same direction as right vector of cam
		Vector3 vertical = Vector3.Cross (dirToBody, horizontal).normalized;
		vertical *= Mathf.Sign (Vector3.Dot (vertical, camT.up));

		float vx = -Vector3.Dot (relativeVelocityWorldSpace, horizontal);
		float vy = -Vector3.Dot (relativeVelocityWorldSpace, vertical);
		float vz = Vector3.Dot (relativeVelocityWorldSpace, dirToBody);
		Vector3 relativeV = new Vector3 (vx, vy, vz);

		// Debug.Log ($"Rel world: {relativeVelocityWorldSpace} rel: {relativeV} speed world: {relativeVelocityWorldSpace.magnitude} speed rel: {relativeV.magnitude}");

		return relativeV;
	}

	/// Screen position clamped to the viewport edge, in the direction of the
	/// world point from screen centre (points behind the camera flip so they
	/// still land on a sensible edge). The mission pin's label uses this to
	/// double as an off-screen direction pointer.
	Vector2 CalculateEdgeUIPos (Vector3 worldPos) {
		const int referenceWidth = 1920;
		const int referenceHeight = 1080;
		const float margin = 0.07f;

		Vector3 vp = cam.WorldToViewportPoint (worldPos);
		Vector2 dir = new Vector2 (vp.x - 0.5f, vp.y - 0.5f);
		if (vp.z < 0) dir = -dir;
		if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up;

		// Scale so the longer axis lands on the edge box.
		float scale = (0.5f - margin) / Mathf.Max (Mathf.Abs (dir.x), Mathf.Abs (dir.y));
		dir *= scale;
		return new Vector2 (dir.x * referenceWidth, dir.y * referenceHeight);
	}

	Vector2 CalculateUIPos (Vector3 worldPos) {
		const int referenceWidth = 1920;
		const int referenceHeight = 1080;

		Vector3 viewportCentre = cam.WorldToViewportPoint (worldPos);
		if (viewportCentre.z <= 0) {
			viewportCentre.x = (viewportCentre.x <= 0.5f) ? 1 : 0;
			viewportCentre.y = (viewportCentre.y <= 0.5f) ? 1 : 0;
		}
		//screenCentre = new Vector2 (screenCentre.x / Screen.width, screenCentre.y / Screen.height);

		return new Vector2 ((viewportCentre.x - 0.5f) * referenceWidth, (viewportCentre.y - 0.5f) * referenceHeight);
	}

	public CelestialBody LockedBody {
		get {
			return lockedBody;
		}
	}

	[System.Serializable]
	public struct VelocityIndicator {
		public Image line;
		public Image head;

		public void Update (float angle, Vector2 pos, float magnitude, float arrowHeadSize, float thickness) {
			line.rectTransform.pivot = new Vector2 (0, 0.5f);
			line.rectTransform.eulerAngles = Vector3.forward * angle;
			line.rectTransform.localPosition = pos;
			line.rectTransform.sizeDelta = new Vector2 (magnitude, thickness);
			line.material.SetVector ("_Size", line.rectTransform.sizeDelta);
			head.material.SetVector ("_Size", line.rectTransform.sizeDelta);

			head.rectTransform.localPosition = pos + (Vector2) line.rectTransform.right * magnitude;
			head.rectTransform.eulerAngles = Vector3.forward * angle;

			head.rectTransform.localScale = Vector3.one * arrowHeadSize;
		}

		public void SetActive (bool active) {
			line.gameObject.SetActive (active);
			head.gameObject.SetActive (active);
		}
	}
}