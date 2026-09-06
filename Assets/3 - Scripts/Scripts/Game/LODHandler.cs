using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class LODHandler : MonoBehaviour {
	[Header ("LOD screen heights")]
	// LOD level is determined by body's screen height (1 = taking up entire screen, 0 = teeny weeny speck) 
	public float lod1Threshold = .5f;
	public float lod2Threshold = .2f;

	// Multiplier on the measured screen height before the LOD thresholds are
	// applied. 1 = normal. SolarMap raises it while the map is open so a planet
	// you fly up to gets its detailed mesh from further out (2026-09-06).
	public static float ScreenHeightBias = 1f;

	// Camera to measure from. Null = resolve automatically. SolarMap sets this
	// to the camera it borrowed while the map is open (2026-09-06): the cached
	// Camera.main here can be a camera that never leaves the player, which froze
	// every planet at the LOD it had when the map opened.
	public static Camera CameraOverride;
	// While the map is open the camera transform can be parked elsewhere by
	// other scripts between frames; the map publishes where the camera is
	// ACTUALLY rendered from and the measurement uses that position.
	public static bool HasViewPosOverride;
	public static Vector3 ViewPosOverride;

	[Header ("Debug")]
	public bool debug;
	public CelestialBody debugBody;

	Camera cam;
	Transform camT;
	CelestialBody[] bodies;
	CelestialBodyGenerator[] generators;

	void Start () {
		if (Application.isPlaying) {
			bodies = FindObjectsOfType<CelestialBody> ();
			generators = new CelestialBodyGenerator[bodies.Length];
			for (int i = 0; i < generators.Length; i++) {
				generators[i] = bodies[i].GetComponentInChildren<CelestialBodyGenerator> ();
			}
		}
	}

	int _lodFrameCounter;
	int[] _lastLod;
	void Update () {
		DebugLODInfo ();

		if (Application.isPlaying) {
			// LOD bands change slowly; recomputing every frame forced a camera
			// LookAt + two viewport projections per body every frame (view-matrix
			// thrash even while standing still). Throttle to ~6 Hz — imperceptible
			// for planet-scale LOD switching.
			if (++_lodFrameCounter >= 10) {
				_lodFrameCounter = 0;
				HandleLODs ();
			}
		}

	}

	void HandleLODs () {
		for (int i = 0; i < bodies.Length; i++) {
			if (generators[i] != null) {
				float screenHeight = CalculateScreenHeight (bodies[i]) * ScreenHeightBias;
				int lodIndex = CalculateLODIndex (screenHeight);
				generators[i].SetLOD (lodIndex);
				// Diagnostic for the map (Sam, 2026-09-06: "planets don't change LOD
				// when I fly close"): log LOD changes while the map is open. Rare.
				if (_lastLod == null || _lastLod.Length != bodies.Length) _lastLod = new int[bodies.Length];
				if (_lastLod[i] != lodIndex + 1) {
					_lastLod[i] = lodIndex + 1;
					if (SolarMap.IsOpen) Debug.Log ($"[LODHandler] {bodies[i].bodyName} -> LOD{lodIndex} (screenHeight {screenHeight:0.000}, cam {(cam != null ? cam.name : "none")})");
				}
			}

		}
	}

	int CalculateLODIndex (float screenHeight) {
		if (screenHeight > lod1Threshold) {
			return 0;
		} else if (screenHeight > lod2Threshold) {
			return 1;
		}
		return 2;
	}

	void DebugLODInfo () {
		if (debugBody && debug) {
			float h = CalculateScreenHeight (debugBody);
			int index = CalculateLODIndex (h);
			Debug.Log ($"Screen height of {debugBody.name}: {h} (lod = {index})");
		}
	}

	// The measuring camera: the override if set, else the player camera
	// (CameraEffectsManager knows it), else Camera.main. Re-resolved whenever
	// the cached one is gone OR disabled — a disabled camera is not null, so the
	// old `cam == null` check could keep a dead loading/menu camera forever.
	Camera ResolveCamera () {
		if (CameraOverride != null) return CameraOverride;
		if (cam == null || !cam.isActiveAndEnabled) {
			var mgr = CameraEffectsManager.Instance;
			Camera pc = mgr != null ? mgr.PlayerCamera : null;
			cam = (pc != null && pc.isActiveAndEnabled) ? pc : Camera.main;
		}
		return cam;
	}

	float CalculateScreenHeight (CelestialBody body) {
		Camera c = ResolveCamera ();
		if (c == null) return 0f;
		cam = c;
		camT = c.transform;
		Quaternion originalRot = camT.rotation;
		Vector3 originalPos = camT.position;
		if (HasViewPosOverride) camT.position = ViewPosOverride;
		Vector3 bodyCentre = body.transform.position;
		camT.LookAt (bodyCentre);

		Vector3 viewA = cam.WorldToViewportPoint (bodyCentre - camT.up * body.radius);
		Vector3 viewB = cam.WorldToViewportPoint (bodyCentre + camT.up * body.radius);
		float screenHeight = Mathf.Abs (viewA.y - viewB.y);
		camT.rotation = originalRot;
		camT.position = originalPos;

		return screenHeight;
	}
}