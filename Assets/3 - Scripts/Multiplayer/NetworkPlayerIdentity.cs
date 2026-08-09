using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Carries a player's character identity — name and suit colour — to everyone
/// else in the session, and applies it to the puppet.
///
/// ── Why a NetworkBehaviour and not a plain MonoBehaviour ─────────────────
/// NetworkPlayerSetup.OnNetworkSpawn destroys EVERY component on the puppet
/// that is not a NetworkObject or NetworkBehaviour, because half the codebase
/// finds "the player" via FindObjectOfType and must keep finding only the real
/// rig. A plain MonoBehaviour here would be deleted the instant it spawned.
///
/// ── Why NetworkVariables and not a connection payload ────────────────────
/// There is no ConnectionApprovalCallback anywhere in this project, so a payload
/// would mean standing up approval plumbing that does not exist. PlanetRelativeSync
/// already publishes owner-written NetworkVariables on this same object; this
/// follows that pattern exactly, and rides the same guaranteed delivery.
///
/// ── The late-join rule ───────────────────────────────────────────────────
/// A NetworkVariable delivers its CURRENT value in the spawn snapshot and only
/// then raises OnValueChanged. Subscribing to the callback alone means a player
/// who joined after you set your name never sees it — the classic bug here. So
/// Apply() is called once on spawn AND on every change.
/// </summary>
[RequireComponent(typeof(PlanetRelativeSync))]
public class NetworkPlayerIdentity : NetworkBehaviour
{
    // FixedString32Bytes because NGO cannot serialise `string` in a
    // NetworkVariable. 32 BYTES, not chars — CharacterProfile.MaxNameLength is
    // 16, which leaves room for multi-byte characters in a non-ASCII name.
    readonly NetworkVariable<FixedString32Bytes> netName = new NetworkVariable<FixedString32Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    readonly NetworkVariable<int> netSwatch = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    TextMesh _nametag;

    /// The name this player is showing, resolved for display. Read by anything
    /// that wants to talk about a remote player (join/leave messages, logs).
    public string DisplayName
    {
        get
        {
            string n = netName.Value.ToString();
            // A puppet whose owner has not published yet still needs a label.
            return string.IsNullOrWhiteSpace(n) ? $"Colonist {OwnerClientId + 1}" : n;
        }
    }

    public int SwatchIndex => SuitPalette.Clamp(netSwatch.Value);

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // Publish who we are. CharacterStore is not gated behind
            // FeatureVault.Multiplayer, so it exists here; but a session started
            // from a stripped test scene might have no character selected, and
            // the empty string falls back to "Colonist N" on the other side.
            var profile = CharacterStore.ActiveProfile;
            if (profile != null)
            {
                netName.Value   = new FixedString32Bytes(Truncate(profile.name));
                netSwatch.Value = SuitPalette.Clamp(profile.swatchIndex);
            }
        }
        else
        {
            // Only OTHER players get a floating label — you never see one over
            // your own head.
            CreateNametag();
        }

        netName.OnValueChanged   += OnNameChanged;
        netSwatch.OnValueChanged += OnSwatchChanged;

        // Read the spawn-snapshot values now; see the late-join rule above.
        Apply();
    }

    public override void OnNetworkDespawn()
    {
        netName.OnValueChanged   -= OnNameChanged;
        netSwatch.OnValueChanged -= OnSwatchChanged;
    }

    void OnNameChanged(FixedString32Bytes _, FixedString32Bytes __) => Apply();
    void OnSwatchChanged(int _, int __) => Apply();

    /// Paints the suit and updates the label. Idempotent — called on spawn and
    /// on every subsequent change, including a rename made between sessions.
    void Apply()
    {
        // The owner's puppet is invisible (the real rig stands inside it), but
        // tint it anyway: it costs nothing and keeps the two in agreement if the
        // puppet is ever made visible for debugging.
        SuitTinter.Apply(transform, SwatchIndex);

        if (_nametag != null)
        {
            _nametag.text  = DisplayName;
            _nametag.color = SuitPalette.ColorAt(SwatchIndex);
        }
    }

    /// World-space name above a remote player's avatar.
    ///
    /// Created disabled: PlanetRelativeSync.SetRemoteVisible re-collects child
    /// renderers on every visibility transition, so the tag is picked up and
    /// shown at the same moment the body is — no pop of a floating name over an
    /// invisible astronaut.
    void CreateNametag()
    {
        var go = new GameObject("Nametag");
        go.transform.SetParent(transform, false);
        // Tuning history, 2026-08-09 playtest: started at 2.3 / size 0.12
        // ("really big and high up"), then 1.95 / 0.06 ("still floats a bit too
        // high"). Now 1.72 / 0.04 — sits just off the top of the helmet.
        go.transform.localPosition = new Vector3(0f, 1.72f, 0f);

        _nametag = go.AddComponent<TextMesh>();
        _nametag.text          = DisplayName;
        _nametag.anchor        = TextAnchor.MiddleCenter;
        _nametag.alignment     = TextAlignment.Center;
        _nametag.characterSize = 0.04f;
        // fontSize stays at 64: it is the RASTER resolution, not the world size
        // (characterSize is). Dropping it with the scale would just make the
        // smaller tag blurry.
        _nametag.fontSize      = 64;
        _nametag.color         = SuitPalette.ColorAt(SwatchIndex);

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            _nametag.font = font;
            go.GetComponent<MeshRenderer>().material = font.material;
        }

        go.GetComponent<MeshRenderer>().enabled = false;
        go.AddComponent<NametagBillboard>();
    }

    /// Belt-and-braces against a longer name arriving from a future build with a
    /// bigger cap: FixedString32Bytes THROWS if the value does not fit, which
    /// would take the spawn down with it.
    ///
    /// Public so it can be exercised directly. This is the LAST line of defence
    /// before a value that overflows the buffer kills a player spawn, and the
    /// failure would surface as "the other player never appeared" — worth a test
    /// rather than an assumption. Note a 16-character all-emoji name is 32 UTF-8
    /// bytes, so the byte loop below is load-bearing, not theoretical.
    public static string Truncate(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // 32-byte buffer, and UTF-8 can spend 4 bytes on one character. Cap at
        // 16 characters (the profile cap) and trust the profile's own sanitise
        // for the common case; this only bites on hand-edited JSON.
        if (s.Length > CharacterProfile.MaxNameLength)
            s = CharacterProfile.TrimDanglingSurrogate(s.Substring(0, CharacterProfile.MaxNameLength));
        while (System.Text.Encoding.UTF8.GetByteCount(s) > 29 && s.Length > 0)
            s = CharacterProfile.TrimDanglingSurrogate(s.Substring(0, s.Length - 1));
        return s;
    }
}
