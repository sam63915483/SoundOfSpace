using System.Collections.Generic;
using UnityEngine;

// Static registry of every active ragdoll bone Rigidbody in the scene.
// EndlessManager temporarily kinematicizes everything in this list during a
// floating-origin shift so the rb.position teleport pushed by SyncTransforms
// (after the planet's hierarchy shift propagates to each bone's transform)
// doesn't fire a depenetration impulse against the also-shifted terrain.
// Kinematic state is restored one frame later, after PhysX has settled the
// new contact pairs at the post-shift coordinates.
//
// Bones auto-cleanup via RagdollBoneMarker.OnDestroy when the parent corpse
// is destroyed, so callers only need to call Register() at ragdoll build time.
public static class RagdollBoneRegistry
{
    static readonly List<Rigidbody> s_bones = new List<Rigidbody>();
    public static IReadOnlyList<Rigidbody> Bones => s_bones;

    public static void Register(Rigidbody rb)
    {
        if (rb == null) return;
        if (s_bones.Contains(rb)) return;
        s_bones.Add(rb);
        // Marker MonoBehaviour on the same GameObject auto-unregisters on
        // destroy. Callers don't have to track cleanup manually.
        var marker = rb.gameObject.GetComponent<RagdollBoneMarker>();
        if (marker == null) marker = rb.gameObject.AddComponent<RagdollBoneMarker>();
        marker.bone = rb;
    }

    public static void Unregister(Rigidbody rb)
    {
        if (rb == null)
        {
            for (int i = s_bones.Count - 1; i >= 0; i--)
                if (s_bones[i] == null) s_bones.RemoveAt(i);
            return;
        }
        s_bones.Remove(rb);
    }
}

// Internal marker — re-registers its bone with RagdollBoneRegistry on every
// OnEnable (covers Unity's domain-reload-on-script-edit, which wipes the
// static s_bones list since it lives in a non-MonoBehaviour class), and
// auto-unregisters on OnDestroy. The serialized `bone` field survives domain
// reload so OnEnable can find its Rigidbody without caller intervention.
public class RagdollBoneMarker : MonoBehaviour
{
    [HideInInspector] public Rigidbody bone;

    void OnEnable()
    {
        if (bone == null) bone = GetComponent<Rigidbody>();
        if (bone == null) return;
        RagdollBoneRegistry.Register(bone);
    }

    void OnDestroy() { RagdollBoneRegistry.Unregister(bone); }
}
