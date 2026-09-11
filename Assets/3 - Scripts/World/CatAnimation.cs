using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Plays the IndieCat clips without an AnimatorController asset.
///
/// The pack ships ~60 clips and eleven prefabs, and <b>none of the prefabs has
/// an Animator component or a usable controller</b> — the six .controller files
/// it includes hold one state each (Swim_F) and are demo-scene previews. Rather
/// than hand-author a .controller (fragile YAML, and one more asset to keep in
/// sync with this code), this drives the clips through a PlayableGraph: an
/// <see cref="AnimationMixerPlayable"/> with one input per pose, and weights
/// lerped toward whichever pose is current. That is the whole state machine.
///
/// Clips are injected by <see cref="CatSpawner"/> (see CatClipSet) because they
/// live inside FBX assets and cannot be found at runtime without a reference.
/// Anything left unassigned simply never plays — a missing Swim clip means cats
/// paddle in the Walk pose, not a null-ref.
///
/// Looping is enforced here rather than trusted to the FBX import settings: the
/// active clip's time is wrapped manually, so a clip whose Loop Time checkbox is
/// off still loops instead of freezing on its last frame.
/// </summary>
[RequireComponent(typeof(Animator))]
public class CatAnimation : MonoBehaviour
{
    public enum Pose
    {
        Idle = 0, Look, Stretch,
        Sit, Lie, Sleep,
        Walk, Trot,
        Swim, SwimIdle,
        Count
    }

    AnimationClip[] _loop;
    // Kept only so CatSpawner's call signature does not have to change. The
    // one-shot lead-ins they held are no longer played — see Build().
    AnimationClip[] _intro;

    PlayableGraph _graph;
    AnimationMixerPlayable _mixer;
    AnimationClipPlayable[] _players;   // one per Pose
    float[] _weight;
    float[] _target;
    Pose _pose = Pose.Idle;
    float _fadeRate = 4f;
    bool _built;

    public Pose Current => _pose;

    /// <summary>Build the graph. Safe to call again — rebuilds cleanly (pooling).</summary>
    public void Build(AnimationClip[] loopClips, AnimationClip[] introClips)
    {
        Teardown();
        _loop = loopClips;
        _intro = introClips;

        CacheRootBone();

        var animator = GetComponent<Animator>();
        if (animator == null) return;
        // The cats are generic-rig; applyRootMotion would fight CatWander, which
        // owns the body's planet-local position entirely.
        animator.applyRootMotion = false;

        int n = (int)Pose.Count;
        _players = new AnimationClipPlayable[n];
        _weight = new float[n];
        _target = new float[n];

        _graph = PlayableGraph.Create("CatAnimation:" + name);
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        // Exactly n inputs, fixed for the life of the graph. There is no longer
        // a spare slot for one-shot lead-ins: creating, connecting, disconnecting
        // and destroying a playable on a LIVE graph every time a cat settled —
        // with dozens of cats settling constantly — was the only part of this
        // that mutated itself at runtime, and stripping the animation out was
        // what made Sam's one-frame screen glitch stop. A crossfade into the
        // loaf reads nearly as well and the graph is now completely static.
        _mixer = AnimationMixerPlayable.Create(_graph, n);

        for (int i = 0; i < n; i++)
        {
            var clip = (_loop != null && i < _loop.Length) ? _loop[i] : null;
            if (clip == null) continue;
            _players[i] = AnimationClipPlayable.Create(_graph, clip);
            _players[i].SetApplyFootIK(false);
            _graph.Connect(_players[i], 0, _mixer, i);
            _mixer.SetInputWeight(i, 0f);
        }

        var output = AnimationPlayableOutput.Create(_graph, "CatOut", animator);
        output.SetSourcePlayable(_mixer);

        _built = true;
        _pose = Pose.Idle;
        _weight[(int)Pose.Idle] = 1f;
        _target[(int)Pose.Idle] = 1f;
        ApplyWeights();
        _graph.Play();
    }

    /// <summary>Crossfade to a pose. Repeating the current pose is a no-op.</summary>
    public void Play(Pose pose, float fadeSeconds = 0.25f)
    {
        if (!_built || pose == _pose) return;
        int idx = (int)pose;
        if (idx < 0 || idx >= _players.Length || !_players[idx].IsValid()) return;

        _pose = pose;
        _fadeRate = 1f / Mathf.Max(0.02f, fadeSeconds);

        for (int i = 0; i < _target.Length; i++) _target[i] = 0f;
        _target[idx] = 1f;

        // Rewind the pose we are moving to so it starts from its first frame
        // rather than wherever it was left when it last faded out.
        _players[idx].SetTime(0d);

    }

    // ── the Root bone pin ────────────────────────────────────────────────
    //
    // The pack's directional clips (Walk_F, Trot_F, Run_F, Swim_F…) TRANSLATE.
    // They are not root motion: A_Cat_Move.fbx has `motionNodeName:` EMPTY, so
    // Unity never extracts it — the bone named `Root` is animated as an ordinary
    // transform curve. The clip therefore walks the whole skeleton forward and
    // then, because `loopTime: 1`, snaps it back to the start on every loop.
    //
    // That is the "walks forward, teleports back a few feet, repeats" Sam saw,
    // and `applyRootMotion = false` cannot fix it because this was never root
    // motion. AlienWander owns locomotion, so the honest fix is to strip the
    // baked translation and leave every clip in place: pin Root's localPosition
    // in LateUpdate, AFTER the Animator has written (the project's rule for all
    // bone manipulation), keeping rotation so turn clips still read.
    Transform _rootBone;
    Vector3 _rootRest;
    bool _rootRestCaptured;

    void CacheRootBone()
    {
        if (_rootBone == null)
        {
            var all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == "Root") { _rootBone = all[i]; break; }
        }
        // Capture the bind pose ONCE per instance. Re-capturing on a pooled
        // reuse would bake in wherever the previous life's clip happened to
        // stop, and the drift would compound every cycle.
        if (_rootBone != null && !_rootRestCaptured)
        {
            _rootRest = _rootBone.localPosition;
            _rootRestCaptured = true;
        }
    }

    void LateUpdate()
    {
        if (!_built) return;
        if (_rootBone != null && _rootRestCaptured) _rootBone.localPosition = _rootRest;
        HeadLook();
    }

    // -- head-look ---------------------------------------------------------
    //
    // Turn the head toward the player when they are close. It lives HERE, in the
    // same LateUpdate that pins the Root bone, for one specific reason: the
    // Animator writes the whole skeleton during the animation update, so a bone
    // we want to survive must be written after it. A separate component would
    // leave the order between the two undefined.
    //
    // WHY THE FIRST ATTEMPT DID NOTHING VISIBLE:
    // it slerped the head a little way toward the aim each frame --
    //     head.rotation = Slerp(head.rotation, aimed, dt * speed)
    // -- but the Animator RESETS the head every frame, so "a little way toward
    // the aim" was all that ever landed: roughly a tenth of the turn, forever.
    // With a 62 degree clamp that is about 6 degrees. Invisible.
    //
    // The smoothing has to live on a swing that PERSISTS between frames, and
    // that swing must then be applied IN FULL on top of whatever the clip did.
    // That is the whole difference between a head that tracks you and one that
    // does not appear to move.
    //
    // (The target lookup below is shared and throttled rather than per-cat, and
    // prefers PlayerController.Camera over Camera.main. Camera.main does work
    // here -- the tag lives on the player PREFAB, not in the scene file -- but
    // it is null until the player spawns, and one lookup beats 44.)

    [System.NonSerialized] public bool headLookEnabled = true;

    const float LookNear = 10f;          // full strength inside this
    const float LookFar  = 20f;          // fully off beyond this
    // Raised from 65: standing next to a cat that is now twice the size it was,
    // the upward angle to your visor is steep, and the clamp was cutting the
    // tilt short before the head ever got there.
    const float LookMaxAngle = 88f;      // a cat still cannot crank its neck round

/// Horizontal cone the cat will look inside, measured from its own forward.
    /// 160 degrees total (Sam, 2026-09-11): full strength within 70 either side,
    /// faded out by 80. Outside it the cat simply ignores you, which is both
    /// what a cat does and what stops the broken-neck look.
    ///
    /// Note this sits just inside LookMaxAngle (88): a player at the very edge
    /// of the cone needs an ~80 degree swing, so the clamp never cuts in before
    /// the fade does. Raise the cone past the clamp and cats at the edge would
    /// stop short instead of fading out, which looks like a bug rather than a
    /// choice.
    const float LookFovInner = 70f;
    const float LookFovOuter = 80f;
    const float LookDegPerSec = 220f;    // how fast the aim catches up

    Transform _headBone, _neckBone;
    bool _headSearched;
    bool _headAxisCaptured;
    Vector3 _headFwdLocal = Vector3.forward;
    float _lookBlend;
    Quaternion _swing = Quaternion.identity;   // PERSISTS across frames

    // One shared target for every cat: a throttled lookup, not 44 of them.
    static Transform s_target;
    static float s_retargetAt;

    static Transform LookTarget()
    {
        if (s_target != null) return s_target;
        if (Time.time < s_retargetAt) return null;
        s_retargetAt = Time.time + 1f;

        // The project's own accessor first -- this scene has no MainCamera tag.
        var pc = FindObjectOfType<PlayerController>(true);
        if (pc != null && pc.Camera != null) { s_target = pc.Camera.transform; return s_target; }

        var cam = Camera.main;
        if (cam == null) cam = FindObjectOfType<Camera>();
        if (cam != null) s_target = cam.transform;
        return s_target;
    }

    void HeadLook()
    {
        if (!headLookEnabled) return;

        if (!_headSearched)
        {
            _headSearched = true;
            var all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (_headBone == null && all[i].name == "Head") _headBone = all[i];
                if (_neckBone == null && all[i].name == "Neck") _neckBone = all[i];
            }
            if (_headBone == null)
                Debug.LogWarning("[CatAnimation] head-look off on " + name +
                                 ": no Head bone found.");
        }
        if (_headBone == null) return;

        // Calibrate the head's REAL forward axis, once, from the rest pose.
        //
        // The previous version used the neck->head vector as "forward". That
        // axis points slightly up on this rig, so aiming it at the camera left
        // the nose below you -- and no fixed up-bias could fix it, because a
        // fixed bias adds a big angle when the needed angle is small (far away,
        // where it worked) and a negligible one when the needed angle is large
        // (up close, where Sam still saw it staring at his torso). It was
        // helping in exactly the place it was not needed.
        //
        // A cat at rest looks straight ahead, so in the rest pose the head's
        // forward IS the body's forward. Storing that direction in the head's
        // own local space gives a true forward that follows the animation for
        // free, with no bias needed at any distance.
        if (!_headAxisCaptured)
        {
            _headAxisCaptured = true;
            _headFwdLocal = Quaternion.Inverse(_headBone.rotation) * transform.forward;
            if (_headFwdLocal.sqrMagnitude < 1e-6f) _headFwdLocal = Vector3.forward;
        }

        var target = LookTarget();

        float want = 0f;
        Vector3 toTarget = Vector3.zero;
        if (target != null)
        {
            Vector3 delta = target.position - _headBone.position;
            float dist = delta.magnitude;
            if (dist > 0.01f)
            {
                toTarget = delta / dist;

                // Distance fade.
                want = 1f - Mathf.Clamp01((dist - LookNear) / Mathf.Max(0.01f, LookFar - LookNear));

                // FIELD OF VIEW GATE. A cat does not crane round at someone
                // standing behind it -- that read as a broken neck (Sam,
                // 2026-09-11). Only the HORIZONTAL bearing counts, measured
                // against the cat's own up, so standing above a cat still works.
                // ~100 degrees total: full inside 45 either side, off past 55.
                Vector3 up = transform.up;
                Vector3 flatTo = Vector3.ProjectOnPlane(toTarget, up);
                Vector3 flatFwd = Vector3.ProjectOnPlane(transform.forward, up);
                if (flatTo.sqrMagnitude > 1e-6f && flatFwd.sqrMagnitude > 1e-6f)
                {
                    float bearing = Vector3.Angle(flatFwd, flatTo);
                    want *= 1f - Mathf.Clamp01((bearing - LookFovInner) /
                                   Mathf.Max(0.01f, LookFovOuter - LookFovInner));
                }
            }
        }
        _lookBlend = Mathf.MoveTowards(_lookBlend, want, Time.deltaTime * 2.2f);

        Quaternion desired = Quaternion.identity;
        if (_lookBlend > 0.001f && toTarget != Vector3.zero)
        {
            // The head's true forward, following whatever the clip is doing.
            Vector3 facing = _headBone.rotation * _headFwdLocal;
            if (facing.sqrMagnitude > 1e-6f)
            {
                facing.Normalize();
                desired = Quaternion.FromToRotation(facing, toTarget);
                // Clamp BEFORE blending, so someone just outside the cone turns
                // the head as far as it goes and no further.
                desired = Quaternion.RotateTowards(Quaternion.identity, desired, LookMaxAngle);
                desired = Quaternion.Slerp(Quaternion.identity, desired, _lookBlend);
            }
        }

        // Smooth the SWING (which survives the Animator), then apply it whole.
        _swing = Quaternion.RotateTowards(_swing, desired, LookDegPerSec * Time.deltaTime);
        if (Quaternion.Angle(_swing, Quaternion.identity) < 0.05f) return;
        _headBone.rotation = _swing * _headBone.rotation;
    }

    void Update()
    {
        if (!_built) return;

        float step = _fadeRate * Time.deltaTime;
        for (int i = 0; i < _weight.Length; i++)
            _weight[i] = Mathf.MoveTowards(_weight[i], _target[i], step);

        // Loop by hand so a clip with Loop Time unchecked still loops.
        for (int i = 0; i < _players.Length; i++)
        {
            if (_weight[i] <= 0.001f || !_players[i].IsValid()) continue;
            var clip = _loop[i];
            if (clip == null || clip.length <= 0.01f) continue;
            double t = _players[i].GetTime();
            if (t >= clip.length) _players[i].SetTime(t % clip.length);
        }

        ApplyWeights();
    }

    void ApplyWeights()
    {
        // The applied weights MUST total exactly 1. An animation mixer writes
        // weight x pose, so a total below 1 drags every bone toward zero —
        // position, and scale — and a total of 0 yields a zero quaternion, which
        // normalises to NaN. NaN vertices rasterise as huge triangles across the
        // screen, which is precisely what a one-frame full-screen obstruction
        // looks like. Normalising here is the guard against that.
        float sum = 0f;
        for (int i = 0; i < _weight.Length; i++)
            if (_players[i].IsValid()) sum += _weight[i];

        if (sum <= 0.0001f)
        {
            // Nothing has any weight (a pose whose clip is missing, or a frame
            // caught mid-rebuild). Fall back to a whole-weight Idle rather than
            // publishing a degenerate pose.
            for (int i = 0; i < _weight.Length; i++)
                if (_players[i].IsValid()) _mixer.SetInputWeight(i, 0f);
            if (_players[(int)Pose.Idle].IsValid())
                _mixer.SetInputWeight((int)Pose.Idle, 1f);
            return;
        }

        for (int i = 0; i < _weight.Length; i++)
            if (_players[i].IsValid()) _mixer.SetInputWeight(i, _weight[i] / sum);
    }

    void Teardown()
    {
        _built = false;
        if (_graph.IsValid()) _graph.Destroy();
    }

    void OnDisable() { Teardown(); }
    void OnDestroy() { Teardown(); }
}
