using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
	Responsible for rendering oceans and atmospheres as post processing effect
*/

[CreateAssetMenu (menuName = "PostProcessing/PlanetEffects")]
public class PlanetEffects : PostProcessingEffect {

	public Shader oceanShader;
	public Shader atmosphereShader;
	public bool displayOceans = true;
	public bool displayAtmospheres = true;

	List<EffectHolder> effectHolders;
	List<float> sortDistances;

	List<Material> postProcessingMaterials;
	bool active = true;

	public override void Render (RenderTexture source, RenderTexture destination) {
		List<Material> materials = GetMaterials ();
		CustomPostProcessing.RenderMaterials (source, destination, materials);
	}

	bool HasStaleGenerators () {
		if (effectHolders == null) return true;
		foreach (var h in effectHolders)
			if (h.generator == null) return true;
		return false;
	}

	void Init () {
		if (effectHolders == null || effectHolders.Count == 0 || !Application.isPlaying || HasStaleGenerators ()) {
			var generators = FindObjectsOfType<CelestialBodyGenerator> ();
			effectHolders = new List<EffectHolder> (generators.Length);
			for (int i = 0; i < generators.Length; i++) {
				effectHolders.Add (new EffectHolder (generators[i]));
			}
		}
		if (postProcessingMaterials == null) {
			postProcessingMaterials = new List<Material> ();
		}
		if (sortDistances == null) {
			sortDistances = new List<float> ();
		}
		sortDistances.Clear ();
		postProcessingMaterials.Clear ();
	}

	public List<Material> GetMaterials () {

		if (!active) {
			return null;
		}
		Init ();

		if (effectHolders.Count > 0) {
			Camera cam = Camera.current;
			Vector3 camPos = cam.transform.position;

			SortFarToNear (camPos);

			// Culling (2026-09-06, Sam's call — see the fields at the end of the
			// class). Every planet's sky and sea are FULL-SCREEN passes; with
			// twelve planets that was 21 screen-sized blits a frame, most of
			// them painting nothing. Skip a planet whose whole effect sphere is
			// off screen, too small to see, or hidden behind another planet's
			// ground. With cullInvisible off this loop is exactly what it was.
			int culled = 0;
			if (cullInvisible) {
				GeometryUtility.CalculateFrustumPlanes (cam, frustumPlanes);
				tanHalfFov = Mathf.Tan (cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
			}

			for (int i = 0; i < effectHolders.Count; i++) {
				EffectHolder effectHolder = effectHolders[i];
				if (cullInvisible && !IsWorthRendering (cam, camPos, i)) { culled++; continue; }
				Material underwaterMaterial = null;
				// Oceans
				if (displayOceans) {
					if (effectHolder.oceanEffect != null) {

						effectHolder.oceanEffect.UpdateSettings (effectHolder.generator, oceanShader);

						float camDstFromCentre = (camPos - effectHolder.generator.transform.position).magnitude;
						if (camDstFromCentre < effectHolder.generator.GetOceanRadius ()) {
							underwaterMaterial = effectHolder.oceanEffect.GetMaterial ();
						} else {
							postProcessingMaterials.Add (effectHolder.oceanEffect.GetMaterial ());
						}
					}
				}
				// Atmospheres
				if (displayAtmospheres) {
					if (effectHolder.atmosphereEffect != null) {
						effectHolder.atmosphereEffect.UpdateSettings (effectHolder.generator);
						postProcessingMaterials.Add (effectHolder.atmosphereEffect.GetMaterial ());
					}
				}

				if (underwaterMaterial != null) {
					postProcessingMaterials.Add (underwaterMaterial);
				}
			}
			LastCulled = culled;
			LastRendered = effectHolders.Count - culled;
		}

		return postProcessingMaterials;
	}

	// Radius of the largest thing this planet's passes can paint: its
	// atmosphere shell, else its ocean, else the body itself.
	float EffectRadius (EffectHolder h) {
		float r = h.generator.BodyScale;
		if (h.atmosphereEffect != null) {
			var atmo = h.generator.body.shading.atmosphereSettings;
			if (atmo != null) r = Mathf.Max (r, (1f + atmo.atmosphereScale) * h.generator.BodyScale);
		}
		if (h.oceanEffect != null) r = Mathf.Max (r, h.generator.GetOceanRadius ());
		return r;
	}

	// Conservative on purpose: any doubt → render (the old behaviour).
	bool IsWorthRendering (Camera cam, Vector3 camPos, int index) {
		var h = effectHolders[index];
		if (h.generator == null) return true;
		Vector3 centre = h.generator.transform.position;
		float fxR = EffectRadius (h);
		Vector3 toB = centre - camPos;
		float dst = toB.magnitude;
		if (dst <= fxR) return true;                         // inside its sky/sea: always draw

		// Too small to see: whole effect sphere shorter than minScreenPixels.
		float diameterPx = fxR * cam.pixelHeight / Mathf.Max (dst * tanHalfFov, 1e-3f);
		if (diameterPx < minScreenPixels) return false;

		// Off screen: sphere entirely outside any frustum plane.
		for (int p = 0; p < 6; p++) {
			if (frustumPlanes[p].GetDistanceToPoint (centre) < -fxR) return false;
		}

		// Behind another planet: B's whole disc inside a nearer body A's ground
		// disc (A's radius shrunk a little so valleys can't leak B through).
		if (cullBehindPlanets) {
			Vector3 dirB = toB / dst;
			float angB = Mathf.Asin (Mathf.Min (1f, fxR / dst));
			for (int j = 0; j < effectHolders.Count; j++) {
				if (j == index) continue;
				var g = effectHolders[j].generator;
				if (g == null) continue;
				Vector3 toA = g.transform.position - camPos;
				float dA = toA.magnitude;
				float rA = g.BodyScale * occluderRadiusFraction;
				if (dA <= rA || dA >= dst) continue;           // camera inside A, or A not nearer than B
				float angA = Mathf.Asin (rA / dA);
				float sep = Vector3.Angle (dirB, toA / dA) * Mathf.Deg2Rad;
				if (sep + angB <= angA) return false;
			}
		}
		return true;
	}

	float CalculateMaxClippingDst (Camera cam) {
		float halfHeight = cam.nearClipPlane * Mathf.Tan (cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
		float halfWidth = halfHeight * cam.aspect;
		float dstToNearClipPlaneCorner = new Vector3 (halfWidth, halfHeight, cam.nearClipPlane).magnitude;
		return dstToNearClipPlaneCorner;
	}

	public class EffectHolder {
		public CelestialBodyGenerator generator;
		public OceanEffect oceanEffect;
		public AtmosphereEffect atmosphereEffect;

		public EffectHolder (CelestialBodyGenerator generator) {
			this.generator = generator;
			if (generator.body.shading.hasOcean && generator.body.shading.oceanSettings) {
				oceanEffect = new OceanEffect ();
			}
			if (generator.body.shading.hasAtmosphere && generator.body.shading.atmosphereSettings) {
				atmosphereEffect = new AtmosphereEffect ();
			}
		}

		public float DstFromSurface (Vector3 viewPos) {
			return Mathf.Max (0, (generator.transform.position - viewPos).magnitude - generator.BodyScale);
		}
	}

	void SortFarToNear (Vector3 viewPos) {
		for (int i = 0; i < effectHolders.Count; i++) {
			float dstToSurface = effectHolders[i].DstFromSurface (viewPos);
			sortDistances.Add (dstToSurface);
		}

		for (int i = 0; i < effectHolders.Count - 1; i++) {
			for (int j = i + 1; j > 0; j--) {
				if (sortDistances[j - 1] < sortDistances[j]) {
					float tempDst = sortDistances[j - 1];
					var temp = effectHolders[j - 1];
					sortDistances[j - 1] = sortDistances[j];
					sortDistances[j] = tempDst;
					effectHolders[j - 1] = effectHolders[j];
					effectHolders[j] = temp;
				}
			}
		}
	}

	// ── Culling knobs (2026-09-06). Appended: serialized-field order. ──────
	// Tune on the "Planet Effects" asset. Off = the original always-draw loop.
	[Header ("Culling (2026-09-06)")]
	public bool cullInvisible = true;
	[Tooltip ("Skip a planet's sky/sea passes when its whole effect sphere would be shorter than this many pixels on screen.")]
	public float minScreenPixels = 8f;
	[Tooltip ("Skip a planet whose whole effect sphere is hidden behind a nearer planet's ground (below the horizon when you're standing on one).")]
	public bool cullBehindPlanets = true;
	[Range (0.8f, 1f), Tooltip ("Occluder radius as a fraction of the body radius — a little under 1 so terrain low points can't leak a hidden planet through.")]
	public float occluderRadiusFraction = 0.97f;

	// Read by FPSOverlay ("fx drawn/total").
	public static int LastRendered, LastCulled;
	Plane[] frustumPlanes = new Plane[6];
	float tanHalfFov;
}