using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a physics ragdoll on a humanoid Toy10-style rig at runtime, the
/// moment the enemy dies. Critically, the bones have NO physics components
/// during life — that was the previous attempt's mistake: kinematic
/// Rigidbody/CharacterJoint on bones being driven by an Animator each frame
/// causes the SkinnedMeshRenderer to render the bones at interpolated
/// physics positions one frame behind the animator, which the player sees as
/// the running mesh stretching and tearing.
///
/// Here every bone is bare during the walk/run/idle states. When BeginDeath
/// fires we tear off the Animator and inject the full RB + SphereCollider +
/// CharacterJoint stack in one frame, with each bone already non-kinematic
/// and carrying a starting velocity inherited from the moving enemy.
/// </summary>
public static class EnemyRagdollBuilder
{
    struct BoneSpec
    {
        public string suffix;       // bone name without the rig prefix (e.g. "Hips")
        public string parentSuffix; // null → ragdoll root (Hips)
        public string tipSuffix;    // child joint the limb capsule extends toward; null → SphereCollider (head)
        public float  mass;
        public float  radius;
    }

    // Eleven-bone ragdoll: hips → spine2/chest → head + 2 arms (upper+fore) + 2 legs (upper+lower).
    // Each limb bone gets a CAPSULE spanning toward its tip joint (the next bone
    // down the chain) so the segment between joints has real length + thickness —
    // limbs rest on the ground instead of clipping through, and shots between
    // joints register. tipSuffix bones (Hand/Foot) need not be ragdoll bones; they
    // only supply the capsule's far end. Head has no tip → a SphereCollider.
    // Suffix-only so one spec serves any Cursed_Toys_II rig (Toy10_*, Toy3_*, …).
    static readonly BoneSpec[] Bones =
    {
        // Radii measured from the actual skinned mesh (90th-percentile vertex
        // distance perpendicular to each bone) so the capsules wrap the visible
        // chunky-toy limbs — thin radii left the limb mesh hanging outside the
        // collider (ragdoll limbs clipped the ground, shots missed the visible
        // arm). Torso/head trimmed slightly under the measured value to limit
        // ragdoll self-overlap. Scales with the rig, so the 3x elite gets 3x.
        new BoneSpec{ suffix="Hips",         parentSuffix=null,        tipSuffix="Spine2",       mass=3.0f, radius=0.22f },
        new BoneSpec{ suffix="Spine2",       parentSuffix="Hips",      tipSuffix="Head",         mass=2.0f, radius=0.22f },
        new BoneSpec{ suffix="Head",         parentSuffix="Spine2",    tipSuffix=null,           mass=1.0f, radius=0.30f },
        new BoneSpec{ suffix="LeftArm",      parentSuffix="Spine2",    tipSuffix="LeftForeArm",  mass=0.8f, radius=0.15f },
        new BoneSpec{ suffix="LeftForeArm",  parentSuffix="LeftArm",   tipSuffix="LeftHand",     mass=0.6f, radius=0.18f },
        new BoneSpec{ suffix="RightArm",     parentSuffix="Spine2",    tipSuffix="RightForeArm", mass=0.8f, radius=0.15f },
        new BoneSpec{ suffix="RightForeArm", parentSuffix="RightArm",  tipSuffix="RightHand",    mass=0.6f, radius=0.18f },
        new BoneSpec{ suffix="LeftUpLeg",    parentSuffix="Hips",      tipSuffix="LeftLeg",      mass=1.0f, radius=0.17f },
        new BoneSpec{ suffix="LeftLeg",      parentSuffix="LeftUpLeg", tipSuffix="LeftFoot",     mass=0.8f, radius=0.14f },
        new BoneSpec{ suffix="RightUpLeg",   parentSuffix="Hips",      tipSuffix="RightLeg",     mass=1.0f, radius=0.17f },
        new BoneSpec{ suffix="RightLeg",     parentSuffix="RightUpLeg",tipSuffix="RightFoot",    mass=0.8f, radius=0.14f },
    };

    // Adds the right collider to a bone: a CapsuleCollider spanning bone→tip (the
    // limb segment) when a tip joint exists, else a SphereCollider (head). The
    // capsule's length axis is the dominant local-space axis of the bone→tip
    // vector, so it adapts to whatever bone-roll convention the rig uses.
    static Collider AddBoneCollider(Transform bone, Transform tip, float radius)
    {
        if (tip == null)
        {
            var s = bone.gameObject.AddComponent<SphereCollider>();
            s.radius = radius;
            return s;
        }
        var cap = bone.gameObject.AddComponent<CapsuleCollider>();
        cap.radius = radius;
        Vector3 local = bone.InverseTransformPoint(tip.position);
        cap.center = local * 0.5f;
        cap.height = Mathf.Max(local.magnitude, radius * 2f);
        Vector3 a = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
        cap.direction = (a.x >= a.y && a.x >= a.z) ? 0 : (a.y >= a.z ? 1 : 2);
        return cap;
    }

    /// <summary>
    /// Attach RB + collider + joint to every bone listed in Bones[], starting
    /// already physics-active with the supplied velocity (+ a small random
    /// scatter so limbs don't launch identical). Returns the list of created
    /// Rigidbodies for caller bookkeeping (RagdollGravity).
    /// </summary>
    public static List<Rigidbody> BuildAndActivate(Transform rigRoot, Vector3 initialVelocity, float radiusScale = 1f)
    {
        // Auto-detect the rig's bone-name prefix from its Hips bone — e.g.
        // a "Toy10_Hips" yields "Toy10_", a "Toy3_Hips" yields "Toy3_".
        // The shared bone layout (suffixes Hips/Spine2/Head/...) lets one
        // builder serve every Cursed_Toys_II humanoid rig.
        string prefix = DetectRigPrefix(rigRoot);
        if (prefix == null)
        {
            Debug.LogWarning($"[EnemyRagdollBuilder] No '*_Hips' bone under '{rigRoot.name}'. Ragdoll skipped.");
            return new List<Rigidbody>();
        }

        var byName = new Dictionary<string, Transform>();
        foreach (var spec in Bones)
        {
            var t = FindDeep(rigRoot, prefix + spec.suffix);
            if (t != null) byName[spec.suffix] = t;
        }

        // Make every SkinnedMeshRenderer never cull with huge static
        // localBounds. As bones ragdoll, the rendered mesh can sprawl
        // well outside the original bind-pose bounds; without this Unity
        // sometimes culls the corpse mid-tumble. updateWhenOffscreen is
        // OFF because we're hardcoding bounds — letting Unity recompute
        // would overwrite our value.
        var smrs = rigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Bounds neverCull = new Bounds(Vector3.zero, Vector3.one * 1000000f);
        for (int i = 0; i < smrs.Length; i++)
        {
            if (smrs[i] == null) continue;
            smrs[i].updateWhenOffscreen = false;
            smrs[i].localBounds = neverCull;
        }

        // Pass 1: Rigidbody + SphereCollider on each bone, immediately non-kinematic.
        var rbs = new Dictionary<string, Rigidbody>();
        foreach (var spec in Bones)
        {
            if (!byName.TryGetValue(spec.suffix, out var bone)) continue;

            var rb = bone.gameObject.AddComponent<Rigidbody>();
            rb.mass        = spec.mass;
            rb.useGravity  = false;             // RagdollGravity applies n-body pull
            rb.isKinematic = false;
            rb.interpolation         = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.velocity        = initialVelocity + Random.insideUnitSphere * 0.5f;
            rb.angularVelocity = Random.onUnitSphere * 6f;
            // Register with RagdollBoneRegistry so EndlessManager kinematicizes
            // this bone briefly during a floating-origin shift — the rb.position
            // teleport that SyncTransforms pushes (after the planet's hierarchy
            // shift propagates down to this bone's transform) would otherwise
            // fire a depenetration impulse against the also-shifted terrain.
            RagdollBoneRegistry.Register(rb);
            rbs[spec.suffix] = rb;

            Transform tip = spec.tipSuffix != null ? FindDeep(rigRoot, prefix + spec.tipSuffix) : null;
            AddBoneCollider(bone, tip, spec.radius * radiusScale);
        }

        // Pass 2: CharacterJoint chains. Connected bodies must already exist
        // from Pass 1, so the two-pass split is required.
        foreach (var spec in Bones)
        {
            if (spec.parentSuffix == null) continue;
            if (!rbs.TryGetValue(spec.suffix,       out var childRB))  continue;
            if (!rbs.TryGetValue(spec.parentSuffix, out var parentRB)) continue;

            var joint = childRB.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody       = parentRB;
            joint.enablePreprocessing = false;
            joint.enableCollision     = false;   // joined bones don't shove each other apart
            joint.enableProjection    = true;    // snap back if a joint's limits are violated
        }

        var list = new List<Rigidbody>(rbs.Count);
        foreach (var kv in rbs) list.Add(kv.Value);
        return list;
    }

    /// <summary>
    /// Adds bare SphereColliders (NO Rigidbody / joint) to the rig bones so the
    /// enemy has an animation-following hitbox WHILE ALIVE — shots register on
    /// the actual limbs, not just the static torso capsule. Bones stay
    /// Rigidbody-free during life (a Rigidbody there causes the mesh tearing
    /// described in the class summary), so these colliders attach to the enemy
    /// ROOT's kinematic Rigidbody as a moving compound collider: they follow the
    /// animation, are hit by weapon raycasts, and add no static-collider rebuild
    /// cost. Reuses the exact bones + radii the ragdoll uses. Returns the created
    /// colliders so the caller can destroy them on death, just before
    /// BuildAndActivate injects the ragdoll's own colliders.
    /// </summary>
    public static List<Collider> BuildHitColliders(Transform rigRoot, float radiusScale = 1f)
    {
        var result = new List<Collider>();
        if (rigRoot == null) return result;
        string prefix = DetectRigPrefix(rigRoot);
        if (prefix == null) return result; // capsule placeholder / non-humanoid rig
        foreach (var spec in Bones)
        {
            var bone = FindDeep(rigRoot, prefix + spec.suffix);
            if (bone == null) continue;
            Transform tip = spec.tipSuffix != null ? FindDeep(rigRoot, prefix + spec.tipSuffix) : null;
            result.Add(AddBoneCollider(bone, tip, spec.radius * radiusScale));
        }
        return result;
    }

    static string DetectRigPrefix(Transform root)
    {
        const string HipsSuffix = "_Hips";
        var hips = FindBySuffix(root, HipsSuffix);
        if (hips == null) return null;
        return hips.name.Substring(0, hips.name.Length - "Hips".Length);
    }

    static Transform FindBySuffix(Transform root, string suffix)
    {
        if (root.name.EndsWith(suffix)) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindBySuffix(root.GetChild(i), suffix);
            if (found != null) return found;
        }
        return null;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
