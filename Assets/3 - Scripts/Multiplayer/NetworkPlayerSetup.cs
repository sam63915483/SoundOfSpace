using Unity.Netcode;
using UnityEngine;

/// Strips a spawned network player down to a pure puppet on EVERY machine.
/// The real scene player rig is never touched — the puppet only renders the
/// remote pose (or, on the owner's machine, invisibly mirrors the real
/// player for publishing).
public class NetworkPlayerSetup : NetworkBehaviour
{
    static readonly Color[] ClientColors =
    {
        new Color(0.95f, 0.45f, 0.20f), // orange
        new Color(0.30f, 0.70f, 1.00f), // blue
        new Color(0.40f, 0.90f, 0.40f), // green
        new Color(0.90f, 0.40f, 0.90f), // magenta
    };

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
        else
        {
            CreateNametag();
        }
    }

    // World-space "Player N" tag above remote avatars (host = Player 1, first
    // joiner = Player 2, ...). Only on avatars of OTHER players.
    void CreateNametag()
    {
        var go = new GameObject("Nametag");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 2.3f, 0f);

        var tm = go.AddComponent<TextMesh>();
        tm.text = $"Player {OwnerClientId + 1}";
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.characterSize = 0.12f;
        tm.fontSize = 64;
        tm.color = ClientColors[(int)(OwnerClientId % (ulong)ClientColors.Length)];
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            tm.font = font;
            go.GetComponent<MeshRenderer>().material = font.material;
        }

        // Hidden until PlanetRelativeSync shows the avatar (its visibility
        // pass re-collects child renderers, so the tag is included).
        go.GetComponent<MeshRenderer>().enabled = false;
        go.AddComponent<NametagBillboard>();
    }
}
