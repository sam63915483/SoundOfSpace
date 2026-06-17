using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
// Run our camera-follow LateUpdate AFTER EndlessManager's origin shift (default
// order 0) so on a shift frame we read the already-teleported camera position.
[DefaultExecutionOrder (1000)]
public class StarTest : MonoBehaviour {

	public int seed = 0;
	public int numStars;
	public int numVertsPerStar = 5;
	public Vector2 sizeMinMax;
	public float minBrightness;
	public float maxBrightness = 1;
	public float dst = 10;
	public float daytimeFade = 4; // higher value means it needs to be darker before stars will appear 
	public Material mat;
	Mesh mesh;
	Camera cam;

	public Gradient colourSpectrum;
	Texture2D spectrum;
	bool settingsUpdated;
	OceanMaskRenderer oceanMaskRenderer;

	void Start () {
		Init (true);
	}

	void OnValidate () {
		settingsUpdated = true;
	}

	void Update () {
		if (!Application.isPlaying) {
			Init (settingsUpdated);
			settingsUpdated = false;
		}
	}

	void Init (bool regenerateMesh) {
		if (regenerateMesh) {
			GenerateMesh ();
		}
		// Bind to the MAIN (player) camera's post-processing — NOT just any
		// CustomPostProcessing. FindObjectOfType returns an arbitrary instance and
		// was picking the MapCamera, so the starfield followed the stationary map
		// camera and appeared to jump as the player flew / the origin shifted.
		CustomPostProcessing customPostProcessing = null;
		foreach (var cpp in FindObjectsOfType<CustomPostProcessing> ()) {
			var c = cpp.GetComponent<Camera> ();
			if (c != null && c.CompareTag ("MainCamera")) { customPostProcessing = cpp; break; }
		}
		if (customPostProcessing == null) customPostProcessing = FindObjectOfType<CustomPostProcessing> ();
		if (customPostProcessing == null) return;

		customPostProcessing.onPostProcessingComplete -= Set;
		customPostProcessing.onPostProcessingComplete += Set;
		cam = customPostProcessing.GetComponent<Camera> ();
		TextureHelper.TextureFromGradient (colourSpectrum, 64, ref spectrum);
		mat.SetTexture ("_Spectrum", spectrum);
		mat.SetFloat ("daytimeFade", daytimeFade);

		if (!oceanMaskRenderer) {
			oceanMaskRenderer = FindObjectOfType<OceanMaskRenderer> ();
		}

	}

	public void Set (RenderTexture screen) {
		mat.SetTexture ("_MainTex", screen);
		if (oceanMaskRenderer) {
			mat.SetTexture ("_OceanMask", oceanMaskRenderer.oceanMaskTexture);
		}
		// Camera-follow is in LateUpdate (see below) — it always runs and runs
		// before rendering. (The previous Camera.onPreCull approach never fired for
		// this post-processing camera, so the starfield wasn't following at all.)
	}

	// Centre the starfield on the camera every frame so it reads as infinitely
	// distant (follow POSITION, not rotation). LateUpdate always runs and runs
	// before rendering; [DefaultExecutionOrder(1000)] makes it run AFTER the
	// EndlessManager origin shift, so on a shift frame the stars track the
	// already-teleported camera instead of jumping. Play-mode only so we don't
	// drag the authored transform around in the editor.
	void LateUpdate () {
		if (Application.isPlaying && cam != null) {
			transform.position = cam.transform.position;
		}
	}

	void GenerateMesh () {
		if (mesh) {
			mesh.Clear ();
		}

		mesh = new Mesh ();
		var tris = new List<int> ();
		var verts = new List<Vector3> ();
		var uvs = new List<Vector2> ();

		Random.InitState (seed);
		for (int starIndex = 0; starIndex < numStars; starIndex++) {
			var dir = Random.onUnitSphere;
			var (circleVerts, circleTris, circleUvs) = GenerateCircle (dir, verts.Count);
			verts.AddRange (circleVerts);
			tris.AddRange (circleTris);
			uvs.AddRange (circleUvs);
		}

		mesh.SetVertices (verts);
		mesh.SetTriangles (tris, 0, true);
		mesh.SetUVs (0, uvs);
		var meshRenderer = GetComponent<MeshRenderer> ();
		GetComponent<MeshFilter> ().sharedMesh = mesh;
		meshRenderer.sharedMaterial = mat;
		meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		meshRenderer.receiveShadows = false;
	}

	(Vector3[] verts, int[] tris, Vector2[] uvs) GenerateCircle (Vector3 dir, int indexOffset) {
		float size = Random.Range (sizeMinMax.x, sizeMinMax.y);
		float brightness = Random.Range (minBrightness, maxBrightness);
		float spectrumT = Random.value;

		var axisA = Vector3.Cross (dir, Vector3.up).normalized;
		if (axisA == Vector3.zero) {
			axisA = Vector3.Cross (dir, Vector3.forward).normalized;
		}
		var axisB = Vector3.Cross (dir, axisA);
		var centre = dir * dst;

		Vector3[] verts = new Vector3[numVertsPerStar + 1];
		Vector2[] uvs = new Vector2[numVertsPerStar + 1];
		int[] tris = new int[numVertsPerStar * 3];

		verts[0] = centre;
		uvs[0] = new Vector2 (brightness, spectrumT);

		for (int vertIndex = 0; vertIndex < numVertsPerStar; vertIndex++) {
			float currAngle = (vertIndex / (float) (numVertsPerStar)) * Mathf.PI * 2;
			var vert = centre + (axisA * Mathf.Sin (currAngle) + axisB * Mathf.Cos (currAngle)) * size;
			verts[vertIndex + 1] = vert;
			uvs[vertIndex + 1] = new Vector2 (0, spectrumT);

			if (vertIndex < numVertsPerStar) {
				tris[vertIndex * 3 + 0] = 0 + indexOffset;
				tris[vertIndex * 3 + 1] = (vertIndex + 1) + indexOffset;
				tris[vertIndex * 3 + 2] = ((vertIndex + 1) % (numVertsPerStar) + 1) + indexOffset;
			}
		}

		return (verts, tris, uvs);
	}
}