using Unity.Netcode;
using UnityEngine;

/// Strips a spawned network player down to a pure puppet on EVERY machine.
/// The real scene player rig is never touched — the puppet only renders the
/// remote pose (or, on the owner's machine, invisibly mirrors the real
/// player for publishing).
public class NetworkPlayerSetup : NetworkBehaviour
{
    // The old hard-coded "Player N" nametag and its four-colour ClientColors
    // array lived here. Both are gone: NetworkPlayerIdentity now owns the label
    // and the suit colour, driven by the player's chosen character, so the text
    // and the colour have a single owner instead of two. The four original
    // colours survive as SuitPalette entries 1–4, so the look is unchanged for
    // anyone who picks them.
    //
    // This class is back to doing only one thing: stripping a spawned player
    // down to a puppet.

    public override void OnNetworkSpawn()
    {
        // The puppet must never render or listen — the real player's camera
        // (with its runtime-attached post stack) stays the one and only.
        var cam = GetComponentInChildren<Camera>(true);
        if (cam != null)
        {
            cam.gameObject.SetActive(false); // instantly, Destroy is end-of-frame
            Destroy(cam.gameObject);
        }

        // Destroy (not disable) every behaviour that could drive the transform
        // or run input; half the codebase locates "the player" via
        // FindObjectOfType<PlayerController>(), which must keep finding ONLY
        // the real scene player. Keep the network stack.
        foreach (var mb in GetComponents<MonoBehaviour>())
        {
            if (mb == null) continue;
            if (mb is NetworkObject || mb is NetworkBehaviour) continue;
            Destroy(mb);
        }

        // PlanetRelativeSync places the puppet directly each frame; physics
        // must neither move it nor fight the placement.
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        // Puppets NEVER collide — on any machine. A solid kinematic capsule
        // swept around by network poses shoves the local player out of any
        // overlap (the "host randomly launched into space" bug). Players
        // simply pass through each other.
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;

        var indicator = transform.Find("RemoteBodyIndicator");
        if (indicator != null) Destroy(indicator.gameObject);

        if (IsOwner)
        {
            // Invisible too: the real player stands inside it.
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }
        // The remote player's nametag is built by NetworkPlayerIdentity, which
        // also owns its text and colour. Order between the two OnNetworkSpawn
        // calls does not matter: PlanetRelativeSync.SetRemoteVisible re-collects
        // child renderers on every visibility transition, so a tag created
        // either side of this sweep is still picked up when the body appears.
    }
}
