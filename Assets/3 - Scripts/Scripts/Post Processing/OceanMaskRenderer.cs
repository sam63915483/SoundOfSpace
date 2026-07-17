using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class OceanMaskRenderer : MonoBehaviour {

	public Shader oceanMaskShader;

	[HideInInspector]
	public RenderTexture oceanMaskTexture;
	CelestialBodyGenerator[] oceanBodies;
	RenderTexture prev;

	// Reused across frames — the original allocated (and never destroyed) a fresh
	// Material every post-process frame, leaking one material per frame. Cached
	// here and rebuilt only when the ocean-body count changes (Material.SetVectorArray
	// locks its length on the first set, so the array size can't grow on a reuse).
	Material _maskMat;
	Vector4[] _oceanSpheres;

	void DestroyMaskMat () {
		if (_maskMat == null) return;
		if (Application.isPlaying) Destroy (_maskMat); else DestroyImmediate (_maskMat);
		_maskMat = null;
	}

	void Update () {
		Init ();
	}

	void Init () {
		if (!Application.isPlaying || oceanBodies == null) {
			var allBodies = FindObjectsOfType<CelestialBodyGenerator> ();
			var oceanBodiesList = new List<CelestialBodyGenerator> ();
			for (int i = 0; i < allBodies.Length; i++) {
				if (allBodies[i].body.shading.hasOcean && allBodies[i].body.shading.oceanSettings != null) {
					oceanBodiesList.Add (allBodies[i]);
				}
			}
			oceanBodies = oceanBodiesList.ToArray ();
			FindObjectOfType<CustomPostProcessing> ().onPostProcessingBegin -= RenderOceanMask;
			FindObjectOfType<CustomPostProcessing> ().onPostProcessingBegin += RenderOceanMask;
		}

	}

	void RenderOceanMask (RenderTexture screenTex) {

		Init ();

		if (prev != null) {
			prev.Release ();
			prev = null;
		}

		if (oceanMaskTexture == null || oceanMaskTexture.width != screenTex.width || oceanMaskTexture.height != screenTex.height) {
			if (oceanMaskTexture != null) {
				prev = oceanMaskTexture;
			}
			oceanMaskTexture = new RenderTexture (screenTex);
		}

		oceanMaskTexture.Create ();
		if (oceanBodies != null && oceanBodies.Length > 0) {
			// Rebuild the cached array + material only when the body count changes.
			if (_oceanSpheres == null || _oceanSpheres.Length != oceanBodies.Length) {
				_oceanSpheres = new Vector4[oceanBodies.Length];
				DestroyMaskMat ();
			}
			for (int i = 0; i < oceanBodies.Length; i++) {
				Vector3 pos = oceanBodies[i].transform.position;
				float oceanRadius = oceanBodies[i].GetOceanRadius ();
				_oceanSpheres[i] = new Vector4 (pos.x, pos.y, pos.z, oceanRadius);
			}
			if (_maskMat == null) _maskMat = new Material (oceanMaskShader);
			_maskMat.SetInt ("numSpheres", _oceanSpheres.Length);
			_maskMat.SetVectorArray ("spheres", _oceanSpheres);
			//ComputeHelper.Run (oceanMaskCompute, width, height);

			Graphics.Blit (screenTex, oceanMaskTexture, _maskMat);
		}

	}

	void OnDestroy () {
		if (oceanMaskTexture != null) {
			oceanMaskTexture.Release ();
		}
		DestroyMaskMat ();
	}
}