using Unity.Netcode;
using UnityEngine;

/// Strips a spawned network player down to a visible dummy on non-owner
/// machines. Owners keep the full stock rig — movement, camera, and gravity
/// run exactly as in single player.
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
        var indicator = transform.Find("RemoteBodyIndicator");

        if (IsOwner)
        {
            if (indicator != null) Destroy(indicator.gameObject);
            // Deterministic scene-player stand-down: the moment the owned
            // network player exists, every non-networked player rig goes away.
            // (Backs up the UI-side despawn — in playtest 1 the callback-based
            // despawn raced and the joiner ended up with two driven bodies.)
            foreach (var pc in FindObjectsOfType<PlayerController>(true))
            {
                if (pc.GetComponent<NetworkObject>() == null)
                {
                    Debug.Log($"[MP] Scene player '{pc.name}' stands down");
                    Destroy(pc.gameObject);
                }
            }
            return;
        }

        // Camera child carries the AudioListener and the whole post stack.
        var cam = GetComponentInChildren<Camera>(true);
        if (cam != null) Destroy(cam.gameObject);

        // Destroy (not disable) every behaviour that could drive the transform
        // or run input: half the codebase locates "the player" via
        // FindObjectOfType<PlayerController>(), which still returns disabled
        // components. Keep only the network stack.
        foreach (var mb in GetComponents<MonoBehaviour>())
        {
            if (mb == null) continue;
            if (mb is NetworkObject || mb is NetworkBehaviour) continue;
            Destroy(mb);
        }

        // PlanetRelativeSync places the avatar directly each frame; physics
        // must neither move it nor fight the placement.
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        // The astronaut model renders fine on remote avatars, so the capsule
        // placeholder is unnecessary visual noise (playtest 2 feedback).
        if (indicator != null) Destroy(indicator.gameObject);

        CreateNametag();
    }

    // World-space "Player N" tag above remote avatars (host = Player 1, first
    // joiner = Player 2, ...). Only on avatars of OTHER players — you can't
    // see your own head in first person anyway.
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
