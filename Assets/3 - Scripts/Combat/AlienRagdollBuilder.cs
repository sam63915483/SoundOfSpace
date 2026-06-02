using System.Collections.Generic;
using UnityEngine;

// Builds a full-skeleton ragdoll on a humanoid alien rig at death-time.
// The 10 alien prefabs share the same bone names so this works across all of
// them. Approach:
//   1. Resolve 14 named bones via deep find.
//   2. For each bone, add CapsuleCollider + Rigidbody + GravityObjectSimple.
//   3. Inherit the planet's orbital velocity at the moment of activation so
//      the ragdoll doesn't get left behind in space.
//   4. Register every bone Rigidbody with RagdollBoneRegistry so EndlessManager
//      can briefly kinematicize them during a floating-origin shift (avoids
//      depenetration impulses from the SyncTransforms teleport).
//   5. CharacterJoint between each bone and its parent in the chain.
//
// The alien wrapper STAYS parented to the planet — origin shifts move the
// planet via EndlessManager, and the entire alien hierarchy (wrapper, rig
// root, SMR host, every bone) follows in lockstep via Unity's transform
// hierarchy. No per-bone EndlessManager registration needed.
public static class AlienRagdollBuilder
{
    struct BoneSpec
    {
        public string name;
        public string parentName;     // null for the floating root (pelvis)
        public string primaryChild;   // used to size the capsule along bone-to-child
        public bool   isLimb;         // arms/legs use looser swing limits
        public float  mass;
    }

    // Hand bones (hand_l/r) and feet (foot_l/r) deliberately omitted —
    // their visual deformation comes "for free" via the SkinnedMeshRenderer
    // since the lower-arm / calf rigidbody drives the wrist/ankle bone, and
    // the SkinnedMeshRenderer skins the hand/foot from there.
    static readonly BoneSpec[] s_bones =
    {
        new BoneSpec { name = "pelvis",     parentName = null,         primaryChild = "spine_01",  isLimb = false, mass = 8f },
        new BoneSpec { name = "spine_01",   parentName = "pelvis",     primaryChild = "spine_02",  isLimb = false, mass = 3f },
        new BoneSpec { name = "spine_02",   parentName = "spine_01",   primaryChild = "spine_03",  isLimb = false, mass = 3f },
        new BoneSpec { name = "spine_03",   parentName = "spine_02",   primaryChild = "neck_01",   isLimb = false, mass = 3f },
        new BoneSpec { name = "neck_01",    parentName = "spine_03",   primaryChild = "head",      isLimb = false, mass = 1f },
        new BoneSpec { name = "head",       parentName = "neck_01",    primaryChild = null,        isLimb = false, mass = 2f },
        new BoneSpec { name = "upperarm_l", parentName = "spine_03",   primaryChild = "lowerarm_l",isLimb = true,  mass = 1.5f },
        new BoneSpec { name = "lowerarm_l", parentName = "upperarm_l", primaryChild = "hand_l",    isLimb = true,  mass = 1f },
        new BoneSpec { name = "upperarm_r", parentName = "spine_03",   primaryChild = "lowerarm_r",isLimb = true,  mass = 1.5f },
        new BoneSpec { name = "lowerarm_r", parentName = "upperarm_r", primaryChild = "hand_r",    isLimb = true,  mass = 1f },
        new BoneSpec { name = "thigh_l",    parentName = "pelvis",     primaryChild = "calf_l",    isLimb = true,  mass = 2.5f },
        new BoneSpec { name = "calf_l",     parentName = "thigh_l",    primaryChild = "foot_l",    isLimb = true,  mass = 2f   },
        new BoneSpec { name = "thigh_r",    parentName = "pelvis",     primaryChild = "calf_r",    isLimb = true,  mass = 2.5f },
        new BoneSpec { name = "calf_r",     parentName = "thigh_r",    primaryChild = "foot_r",    isLimb = true,  mass = 2f   },
    };

    public static void Build(Transform alienRoot, CelestialBody planet, Vector3 hitDirection)
    {
        if (alienRoot == null) return;

        // Resolve every bone we'll touch (including primary children for sizing,
        // even if they don't get their own physics).
        var bones = new Dictionary<string, Transform>();
        var allTransforms = alienRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < allTransforms.Length; i++)
            bones[allTransforms[i].name] = allTransforms[i];

        Vector3 planetVelocity = planet != null ? planet.velocity : Vector3.zero;

        // Make every SkinnedMeshRenderer never cull with huge static
        // localBounds. As bones ragdoll, the rendered mesh can sprawl
        // well outside the original bind-pose bounds; without this Unity
        // sometimes culls the corpse mid-tumble. updateWhenOffscreen is
        // OFF because we're hardcoding bounds — letting Unity recompute
        // would overwrite our value.
        var smrs = alienRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Bounds neverCull = new Bounds(Vector3.zero, Vector3.one * 1000000f);
        for (int i = 0; i < smrs.Length; i++)
        {
            if (smrs[i] == null) continue;
            smrs[i].updateWhenOffscreen = false;
            smrs[i].localBounds = neverCull;
        }

        // Pass 1: add Rigidbody + Collider + GravityObjectSimple to every bone
        // we have a spec for. Joints come second so connectedBody is non-null.
        var addedRbs = new Dictionary<string, Rigidbody>();
        for (int i = 0; i < s_bones.Length; i++)
        {
            var spec = s_bones[i];
            if (!bones.TryGetValue(spec.name, out Transform t) || t == null) continue;

            ConfigureBoneCollider(t, bones, spec.primaryChild);

            var rb = t.gameObject.AddComponent<Rigidbody>();
            rb.useGravity = false;
            // ContinuousSpeculative (not ContinuousDynamic) — even with the
            // EndlessManager temp-kinematic guard, ContinuousDynamic bodies
            // tracked a "previous physics position" that PhysX used for
            // post-restore swept collision, which made the bone get caught
            // on terrain along the 1000m+ shift path. Speculative skips the
            // sweep entirely; it's plenty for resting / settling ragdolls
            // that don't move fast enough to need anti-tunnelling.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.mass = spec.mass;
            rb.velocity = planetVelocity;
            // Register with RagdollBoneRegistry so EndlessManager kinematicizes
            // this bone briefly during a floating-origin shift — the rb.position
            // teleport that SyncTransforms pushes (after the planet's hierarchy
            // shift propagates down to this bone's transform) would otherwise
            // fire a depenetration impulse against the also-shifted terrain.
            RagdollBoneRegistry.Register(rb);

            t.gameObject.AddComponent<GravityObjectSimple>();

            addedRbs[spec.name] = rb;
        }

        // Pass 2: chain each bone to its parent via CharacterJoint.
        for (int i = 0; i < s_bones.Length; i++)
        {
            var spec = s_bones[i];
            if (spec.parentName == null) continue;
            if (!addedRbs.TryGetValue(spec.name, out Rigidbody childRb)) continue;
            if (!addedRbs.TryGetValue(spec.parentName, out Rigidbody parentRb)) continue;

            var joint = childRb.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parentRb;
            joint.enableProjection = true;

            float swing = spec.isLimb ? 80f : 35f;
            float twist = spec.isLimb ? 30f : 15f;
            var s1 = joint.swing1Limit; s1.limit =  swing; joint.swing1Limit = s1;
            var s2 = joint.swing2Limit; s2.limit =  swing; joint.swing2Limit = s2;
            var hi = joint.highTwistLimit; hi.limit =  twist; joint.highTwistLimit = hi;
            var lo = joint.lowTwistLimit;  lo.limit = -twist; joint.lowTwistLimit  = lo;
        }

        // Tiny pop in the hit direction — enough to see the kill registered
        // without yeeting the body across the planet. Earlier values (6 m/s
        // lateral, 2 m/s up) were sustained because nothing damps lateral
        // motion in NBody gravity, so a single shot sent the corpse 30-60m
        // before the lifetime expired.
        if (addedRbs.TryGetValue("pelvis", out Rigidbody pelvisRb) && pelvisRb != null)
        {
            Vector3 up = planet != null
                ? (pelvisRb.position - planet.Position).normalized
                : Vector3.up;
            pelvisRb.AddForce(hitDirection.normalized * 1.5f + up * 0.5f, ForceMode.VelocityChange);
        }
        if (addedRbs.TryGetValue("spine_03", out Rigidbody chestRb) && chestRb != null)
        {
            chestRb.AddForce(hitDirection.normalized * 0.5f, ForceMode.VelocityChange);
        }
    }

    static void ConfigureBoneCollider(Transform bone, Dictionary<string, Transform> bones, string primaryChildName)
    {
        // Compute bone length from bone-to-primary-child distance, or fall back
        // to a small constant for terminal bones (head, no specified child).
        float length = 0.2f;
        Vector3 toChildLocal = Vector3.up * length;
        if (!string.IsNullOrEmpty(primaryChildName)
            && bones.TryGetValue(primaryChildName, out Transform child)
            && child != null)
        {
            Vector3 worldDelta = child.position - bone.position;
            // Convert to bone-local so we can pick the dominant axis.
            toChildLocal = bone.InverseTransformVector(worldDelta);
            length = toChildLocal.magnitude;
            if (length < 0.05f) { length = 0.2f; toChildLocal = Vector3.up * length; }
        }

        // Pick the local axis most aligned with the bone-to-child vector.
        // CapsuleCollider.direction: 0=X, 1=Y, 2=Z.
        Vector3 abs = new Vector3(Mathf.Abs(toChildLocal.x), Mathf.Abs(toChildLocal.y), Mathf.Abs(toChildLocal.z));
        int dir;
        if (abs.x >= abs.y && abs.x >= abs.z) dir = 0;
        else if (abs.y >= abs.z) dir = 1;
        else dir = 2;

        var cap = bone.gameObject.AddComponent<CapsuleCollider>();
        cap.isTrigger = false;
        cap.direction = dir;
        cap.height = length;
        cap.radius = Mathf.Clamp(length * 0.25f, 0.04f, 0.18f);
        // Centre the capsule halfway along the bone-to-child axis.
        Vector3 centre = Vector3.zero;
        if (dir == 0) centre.x = toChildLocal.x * 0.5f;
        else if (dir == 1) centre.y = toChildLocal.y * 0.5f;
        else centre.z = toChildLocal.z * 0.5f;
        cap.center = centre;
    }
}
