using UnityEngine;

/// <summary>
/// Hybrid motion blur. Two contributions, summed per-pixel in the shader:
///   1. <b>Real motion vector</b> sampled from
///      <c>_CameraMotionVectorsTexture</c> — accurate per-pixel screen-space
///      motion driven by Unity's built-in motion-vector pass. Close geometry
///      smears correctly with parallax; sky pixels (zero motion) stay sharp.
///   2. <b>Synthetic radial</b> outward from the perspective-projected
///      velocity vector. Adds the "warp speed" feel on sky / atmosphere
///      pixels that real motion vectors don't touch. Without this the
///      effect is too subtle in space (most of the screen is sky).
///
/// Attached to the player camera at runtime by
/// <see cref="CameraEffectsManager"/>. Not <c>[ImageEffectOpaque]</c> — runs
/// after the atmosphere/planet/ocean composite, so we never touch that
/// pipeline.
/// </summary>
[RequireComponent(typeof(Camera))]
public class RadialMotionBlurEffect : MonoBehaviour
{
    const float ShipThreshold = 12f;
    const float ShipFullAt = 100f;
    const float JetpackThreshold = 8f;
    const float JetpackFullAt = 100f;

    // Real-motion-vector multiplier. Raw vectors are tiny per-frame deltas;
    // 4× amplifies them into a very subtle smear on close geometry.
    // (Was 60 → 12 → 4 over successive tuning passes — pilots want a
    // hint of motion, not an obstructive blur.)
    const float RealMotionMaxStrength = 4f;
    // Synthetic radial strength — peak UV smear at corner pixels at full
    // intensity. 0.008 ≈ 0.8% screen smear, just enough to register at
    // the periphery. (Was 0.12 → 0.024 → 0.008.)
    const float SyntheticMaxStrength = 0.008f;

    Camera _cam;
    Material _material;
    Ship _ship;
    PlayerController _player;
    float _strength;
    Vector2 _centerUv = new Vector2(0.5f, 0.5f);

    void Awake()
    {
        _cam = GetComponent<Camera>();
        _cam.depthTextureMode |= DepthTextureMode.MotionVectors;

        var shader = Shader.Find("Hidden/RadialMotionBlur");
        if (shader == null)
        {
            Debug.LogWarning("[RadialMotionBlurEffect] Shader 'Hidden/RadialMotionBlur' not found. Effect will pass-through.");
            return;
        }
        _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    void OnDestroy()
    {
        if (_material != null) DestroyImmediate(_material);
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (_material == null) { Graphics.Blit(source, destination); return; }
        if (!ShouldBeActive())  { Graphics.Blit(source, destination); return; }

        float targetStrength = ComputeTargetStrength();
        Vector2 targetCenter = ComputeTargetCenter();
        _strength = Mathf.MoveTowards(_strength, targetStrength, Time.unscaledDeltaTime * 1.2f);
        _centerUv = Vector2.Lerp(_centerUv, targetCenter, Time.unscaledDeltaTime * 4f);

        if (_strength <= 0.001f) { Graphics.Blit(source, destination); return; }

        _material.SetVector("_Center", new Vector4(_centerUv.x, _centerUv.y, 0f, 0f));
        _material.SetFloat("_Strength", _strength * RealMotionMaxStrength);
        _material.SetFloat("_SyntheticStrength", _strength * SyntheticMaxStrength);
        Graphics.Blit(source, destination, _material);
    }

    bool ShouldBeActive()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled || mgr.Input == null) return false;
        return mgr.Input.fxRadialMotionBlur;
    }

    float ComputeTargetStrength()
    {
        // Only the currently piloted ship contributes — abandoned ships
        // still orbiting at high speed would otherwise keep motion blur
        // active after the player exits. Use the cached static rather than
        // FindPilotedShip — this runs inside OnRenderImage every frame.
        if (_ship == null || !_ship.IsPiloted) _ship = Ship.PilotedInstance;
        // Planet-relative ship velocity (see Ship.RelativeVelocity) so a
        // newly-spawned ship sitting on Humble Abode doesn't max out the
        // motion blur from inheriting the planet's orbital velocity.
        float shipSpeed = _ship != null ? _ship.RelativeVelocity.magnitude : 0f;
        float shipT = Mathf.Clamp01((shipSpeed - ShipThreshold) / (ShipFullAt - ShipThreshold));

        if (_player == null) _player = FindObjectOfType<PlayerController>(true);
        float playerT = 0f;
        if (_player != null && _player.isActiveAndEnabled && !_player.IsOnGround)
        {
            float relSpeed = _player.RelativeVelocity.magnitude;
            playerT = Mathf.Clamp01((relSpeed - JetpackThreshold) / (JetpackFullAt - JetpackThreshold));
        }

        // Quadratic curve — low speeds barely register, high speeds ramp.
        float t = Mathf.Max(shipT, playerT);
        return t * t;
    }

    // Perspective-project the velocity vector onto the camera's image plane
    // in normalized UV space [0, 1]. Used as the synthetic-radial center —
    // the "vanishing point" the player is heading toward.
    Vector2 ComputeTargetCenter()
    {
        Vector3 worldVel = Vector3.zero;
        if (_player != null && _player.isActiveAndEnabled && !_player.IsOnGround)
            worldVel = _player.RelativeVelocity;
        else if (_ship != null)
        {
            var rb = _ship.GetComponent<Rigidbody>();
            if (rb != null) worldVel = rb.velocity;
        }
        if (worldVel.sqrMagnitude < 1f) return new Vector2(0.5f, 0.5f);

        Vector3 vCam = _cam.transform.InverseTransformDirection(worldVel);
        float fovRad = _cam.fieldOfView * Mathf.Deg2Rad;
        float focalLen = 0.5f / Mathf.Tan(fovRad * 0.5f);
        float aspect = (float)_cam.pixelWidth / Mathf.Max(1, _cam.pixelHeight);

        Vector2 vp;
        if (vCam.z > 0.5f)
        {
            vp = new Vector2(
                (vCam.x / vCam.z) * focalLen / aspect,
                (vCam.y / vCam.z) * focalLen);
        }
        else
        {
            Vector2 lateral = new Vector2(vCam.x, vCam.y);
            vp = lateral.sqrMagnitude < 0.001f
                ? Vector2.zero
                : lateral.normalized * 1.5f;
        }

        Vector2 centerUv = new Vector2(0.5f, 0.5f) + vp * 0.5f;
        centerUv.x = Mathf.Clamp(centerUv.x, -0.5f, 1.5f);
        centerUv.y = Mathf.Clamp(centerUv.y, -0.5f, 1.5f);
        return centerUv;
    }
}
