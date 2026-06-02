using System.Collections;
using UnityEngine;

/// <summary>
/// Makes the NPC stand with arms at their sides, then every <waveInterval> seconds
/// raises the right arm and waves it back and forth before returning to rest.
/// Also smoothly rotates the head to follow the player when within headTrackDistance.
/// Uses LateUpdate so it runs after the Animator and is never overridden.
/// </summary>
public class NPCWaveAnimation : MonoBehaviour
{
    [Header("Wave Settings")]
    [SerializeField] private float waveInterval  = 3f;
    [SerializeField] private float waveDuration  = 2f;
    [SerializeField] private float armRaiseAngle = 121f;
    [SerializeField] private float waveSwingAngle = 40f;
    [SerializeField] private int   waveCount     = 3;

    // Defaults below match the values that all four pre-placed alien NPCs were
    // tuned with in the scene. Without these, runtime-spawned aliens (which
    // get their NPCWaveAnimation via AddComponent) head-track but don't actually
    // face the player — Quaternion.LookRotation needs the offset to convert
    // from world-forward to the alien rig's "head looking forward" axis.
    [Header("Head Look-At Settings")]
    [SerializeField] private float   headTrackDistance = 25f;
    [SerializeField] private float   headTurnSpeed     = 3f;
    [SerializeField] private float   headMaxAngle      = 160f;
    [SerializeField] private Vector3 headRotationOffset = new Vector3(-30f, 90f, -90f);

    // Arm bones
    private Transform _upperArmR;
    private Transform _lowerArmR;
    private Transform _upperArmL;
    private Transform _lowerArmL;

    // "Arms at sides" target rotations (computed from skeleton geometry)
    private Quaternion _upperArmRRest;
    private Quaternion _lowerArmRRest;
    private Quaternion _upperArmLRest;
    private Quaternion _lowerArmLRest;

    // World-space axis used to rotate the arm from T-pose down to sides.
    // Negating it and applying from the sides position raises the arm up for the wave.
    private Vector3 _armRaiseAxis;

    // Head look-at
    private Transform _headBone;
    private Quaternion _headRestLocal;
    private Quaternion _headSmoothed;   // smoothed local rotation of neck_01
    private Transform _player;

    private bool  _ready;
    private bool  _waving;
    private float _waveProgress;
    private float _timer;

    private void Start()
    {
        // FBX bone names (alien rig) tried first; Unity-humanoid fallbacks
        // (RightArm/LeftArm/Neck) let this component work on rigs that
        // weren't FBX-named — e.g. Toy1 in the ship-market vendor.
        _upperArmR = FindDeepChild("upperarm_r", "RightArm");
        _lowerArmR = FindDeepChild("lowerarm_r", "RightForeArm");
        _upperArmL = FindDeepChild("upperarm_l", "LeftArm");
        _lowerArmL = FindDeepChild("lowerarm_l", "LeftForeArm");
        _headBone  = FindDeepChild("neck_01",    "Neck");

        if (_upperArmR == null) Debug.LogWarning("[NPCWaveAnimation] right upper arm bone not found on " + name);
        if (_lowerArmR == null) Debug.LogWarning("[NPCWaveAnimation] right lower arm bone not found on " + name);
        if (_headBone  == null) Debug.LogWarning("[NPCWaveAnimation] neck bone not found on " + name);

        _timer = waveInterval;
        StartCoroutine(InitRestPose());
    }

    private IEnumerator InitRestPose()
    {
        // Wait one frame so the Animator has applied its initial pose
        yield return new WaitForEndOfFrame();

        // Derive the body "down" direction from the skeleton itself so we're
        // robust against any root rotation on this character.
        Transform pelvis = FindDeepChild("pelvis", "Hips");
        Transform spine  = FindDeepChild("spine_01", "Spine");
        Vector3 bodyDown = Vector3.down;
        if (pelvis != null && spine != null)
            bodyDown = (pelvis.position - spine.position).normalized;

        // Rotate each upper arm so its shaft points straight down.
        // Also store the axis of that rotation — negating it is the raise axis.
        if (_upperArmR != null && _lowerArmR != null)
        {
            Vector3 shaftR = (_lowerArmR.position - _upperArmR.position).normalized;
            // cross(T-pose shaft, bodyDown) gives the axis that swings the arm downward.
            // Its negation swings the arm upward from the sides position.
            _armRaiseAxis  = -Vector3.Cross(shaftR, bodyDown).normalized;
            _upperArmRRest = ArmAtSideLocalRot(_upperArmR, _lowerArmR, bodyDown);
            _upperArmR.localRotation = _upperArmRRest;
        }
        if (_upperArmL != null && _lowerArmL != null)
        {
            _upperArmLRest = ArmAtSideLocalRot(_upperArmL, _lowerArmL, bodyDown);
            _upperArmL.localRotation = _upperArmLRest;
        }

        // Let one more frame settle, then capture lower-arm rest rotations
        yield return null;
        if (_lowerArmR != null) _lowerArmRRest = _lowerArmR.localRotation;
        if (_lowerArmL != null) _lowerArmLRest = _lowerArmL.localRotation;

        if (_headBone != null)
        {
            _headRestLocal = _headBone.localRotation;
            _headSmoothed  = _headRestLocal;
        }

        _ready = true;
    }

    /// <summary>
    /// Computes the local rotation for <armBone> that makes the arm shaft
    /// (direction from armBone to forearm) align with <bodyDown>.
    /// </summary>
    private Quaternion ArmAtSideLocalRot(Transform armBone, Transform forearm, Vector3 bodyDown)
    {
        Vector3 shaft     = (forearm.position - armBone.position).normalized;
        Quaternion world  = Quaternion.FromToRotation(shaft, bodyDown) * armBone.rotation;
        return Quaternion.Inverse(armBone.parent.rotation) * world;
    }

    private void Update()
    {
        if (!_ready) return;

        if (!_waving)
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _waving       = true;
                _waveProgress = 0f;
            }
        }
        else
        {
            _waveProgress += Time.deltaTime;
            if (_waveProgress >= waveDuration)
            {
                _waving = false;
                _timer  = waveInterval;
            }
        }
    }

    // LateUpdate runs after the Animator, so our bone writes win every frame.
    private void LateUpdate()
    {
        if (!_ready) return;

        // Always keep left arm at side
        if (_upperArmL != null) _upperArmL.localRotation = _upperArmLRest;
        if (_lowerArmL != null) _lowerArmL.localRotation = _lowerArmLRest;

        if (!_waving)
        {
            // Idle: both arms at sides
            if (_upperArmR != null) _upperArmR.localRotation = _upperArmRRest;
            if (_lowerArmR != null) _lowerArmR.localRotation = _lowerArmRRest;
            UpdateHeadLookAt();
            return;
        }

        float t = _waveProgress / waveDuration;

        // ── Phase layout ──────────────────────────────────────────
        //  0.0 – 0.2  raise upper arm
        //  0.2 – 0.8  wave lower arm back and forth
        //  0.8 – 1.0  lower upper arm back to rest
        // ─────────────────────────────────────────────────────────

        float raiseBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.2f));
        float lowerBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.8f) / 0.2f));
        float armBlend   = raiseBlend * (1f - lowerBlend);

        // Upper arm: blend from rest (arm at side) → raised.
        // Raise is computed in world space using the same axis that lowered the arm,
        // so the motion is always along the correct plane regardless of bone orientation.
        if (_upperArmR != null)
        {
            Quaternion worldRest   = _upperArmR.parent.rotation * _upperArmRRest;
            Quaternion worldRaised = Quaternion.AngleAxis(armRaiseAngle, _armRaiseAxis) * worldRest;
            Quaternion raisedRot   = Quaternion.Inverse(_upperArmR.parent.rotation) * worldRaised;
            _upperArmR.localRotation = Quaternion.Slerp(_upperArmRRest, raisedRot, armBlend);
        }

        // Lower arm: swing only while arm is raised
        if (_lowerArmR != null)
        {
            if (t >= 0.2f && t <= 0.8f)
            {
                float waveT     = (t - 0.2f) / 0.6f;
                float waveAngle = Mathf.Sin(waveT * Mathf.PI * waveCount * 2f) * waveSwingAngle;
                _lowerArmR.localRotation = _lowerArmRRest * Quaternion.Euler(0f, waveAngle, 0f);
            }
            else
            {
                _lowerArmR.localRotation = _lowerArmRRest;
            }
        }

        UpdateHeadLookAt();
    }

    private void UpdateHeadLookAt()
    {
        if (_headBone == null) return;

        // Retry every frame until the player is found (handles late spawns / tag timing).
        if (_player == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p != null) _player = p.transform;
            else return;
        }

        float dist = Vector3.Distance(transform.position, _player.position);
        Quaternion targetLocal = _headRestLocal;

        if (dist <= headTrackDistance)
        {
            Vector3 dir = (_player.position - _headBone.position).normalized;

            // Build the desired world-space rotation, then convert to neck_01 local space.
            // This is the same as setting the rotation numbers you see in the inspector.
            Quaternion worldRot = Quaternion.LookRotation(dir, transform.up);
            Quaternion localRot = Quaternion.Inverse(_headBone.parent.rotation) * worldRot
                                  * Quaternion.Euler(headRotationOffset);

            // Clamp how far from rest the neck can rotate.
            float angle = Quaternion.Angle(_headRestLocal, localRot);
            targetLocal = angle > headMaxAngle
                ? Quaternion.Slerp(_headRestLocal, localRot, headMaxAngle / angle)
                : localRot;
        }

        _headSmoothed = Quaternion.Slerp(_headSmoothed, targetLocal, headTurnSpeed * Time.deltaTime);
        _headBone.localRotation = _headSmoothed;
    }

    private Transform FindDeepChild(string childName)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return t;
        return null;
    }

    // Tries each name in order, returning the first hit. Lets us support both
    // FBX-style ("upperarm_r") and Unity-humanoid ("RightArm") naming.
    private Transform FindDeepChild(params string[] candidates)
    {
        foreach (var n in candidates)
        {
            var hit = FindDeepChild(n);
            if (hit != null) return hit;
        }
        return null;
    }
}
