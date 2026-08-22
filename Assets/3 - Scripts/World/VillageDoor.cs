using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A swinging house door for the LowPolyFantasyVillage buildings in the Humble
/// Abode village. Look at it, press F, it swings open; press F again to shut it.
///
/// ── Why this is so short ─────────────────────────────────────────────────
/// The pack's door leaves (DoorPart_01 / DoorPart_02) are already rigged for
/// this and nobody noticed: the leaf mesh runs from local x = 0 to x ≈ 1.0 with
/// the handles at x ≈ 0.946, so the transform pivot sits ON THE HINGE EDGE. A
/// plain rotation about the leaf's LOCAL Y is a correct door swing — no pivot
/// object, no re-rigging, no animation clip.
///
/// Rotating about a LOCAL axis is also the only thing that works here: these
/// houses sit at arbitrary orientations on a sphere, so there is no world "up"
/// to hinge around. Whatever the house's rotation, the leaf's own Y is the
/// hinge, and the swing follows the planet for free.
///
/// ⚠️ THE GHOST-DOOR TRAP. MeshCombineTool bakes static village geometry into a
/// per-cluster __CombinedMeshes and disables the originals' MeshRenderers. It
/// used to eat the door leaves too, which meant a door could swing its COLLIDER
/// open while a welded copy of itself stayed rendered in the doorway. The tool
/// now skips anything carrying this component (and anything named DoorPart*),
/// but a scene combined BEFORE that rule was added still has the doors baked in.
/// Start() detects exactly that and says so by name — see the warning below.
///
/// Interaction, the prompt, the gaze gate and controller parity all come from
/// Interactable. This class only adds the swing.
/// </summary>
public class VillageDoor : Interactable
{
    /// <summary>Every live door, for the co-op sync layer to address by id.
    /// Maintained in OnEnable/OnDisable per the repo convention — never
    /// FindObjectsOfType in a loop.</summary>
    public static readonly List<VillageDoor> AllDoors = new List<VillageDoor>();

    public enum HingeAxis { LocalY, LocalX, LocalZ }

    public enum SwingRule
    {
        /// Swing away from whoever opened it. Right for a door with clearance
        /// on both sides, which is all of them in this village.
        AwayFromPlayer,
        /// Force one direction. The escape hatch for a door whose frame or a
        /// neighbouring prop blocks one side — set it per instance, no code.
        AlwaysPositive,
        AlwaysNegative,
    }

    [Header("Swing")]
    [Tooltip("Which LOCAL axis the leaf hinges around. The fantasy-village door parts hinge on Y; the field exists so an oddly-authored door can be fixed without code.")]
    public HingeAxis hinge = HingeAxis.LocalY;

    [Tooltip("How far the door swings when open, in degrees.")]
    public float openAngle = 90f;

    [Tooltip("Which way it swings. Leave on AwayFromPlayer unless a particular door clips its frame or a prop opening that way.")]
    public SwingRule swingRule = SwingRule.AwayFromPlayer;

    [Tooltip("Seconds for a full open or close.")]
    public float swingTime = 0.45f;

    [Header("Sound")]
    public AudioClip openClip;
    public AudioClip closeClip;

    [Range(0f, 1f)]
    [Tooltip("Explicit clip volume. PlayClipAtPoint's no-arg overload is 1.0 at 500 m rolloff, which is far too loud for a door.")]
    public float clipVolume = 0.6f;

    /// <summary>True once the player has asked for it open (immediately on
    /// press, not when the swing finishes).</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Stable across machines, so the co-op layer can name a door.</summary>
    public int DoorId { get; private set; }

    Quaternion _closedLocal;
    float _angle;         // current swing, degrees
    float _targetAngle;   // where the swing is heading
    float _openSign = 1f; // which way this door last opened

    void Awake()
    {
        // Authored rotation IS closed. Captured before anything can move it.
        _closedLocal = transform.localRotation;
        DoorId = StableId(transform);
    }

    void OnEnable()
    {
        if (!AllDoors.Contains(this)) AllDoors.Add(this);
    }

    void OnDisable()
    {
        AllDoors.Remove(this);
    }

    void Start()
    {
        WarnIfStillBaked();
    }

    // ── interaction ──────────────────────────────────────────────────────

    protected override string BuildInteractMessage()
    {
        return IsOpen
            ? $"Press {PromptGlyphs.Interact} to close door"
            : $"Press {PromptGlyphs.Interact} to open door";
    }

    protected override void Interact()
    {
        bool want = !IsOpen;
        _localPressAt = Time.unscaledTime;

        // Swing on this screen NOW — a door that waits for a network round trip
        // feels broken even when it is working. The host's absolute-state
        // broadcast corrects us if we guessed wrong.
        ApplyOpen(want, instant: false, fromNetwork: false);
        VillageDoorSync.RequestSetOpen(this, want);

        base.Interact();   // still fires any UnityEvent wired in the inspector
    }

    // ── state ────────────────────────────────────────────────────────────

    /// <summary>How long a local press outranks a periodic snapshot. Covers one
    /// round trip: the host cannot yet know about a door we opened a frame
    /// ago, and its next snapshot would otherwise snap it shut under us right
    /// before the confirmation arrived and swung it open again.</summary>
    const float LocalPressGrace = 1.5f;

    float _localPressAt = -99f;

    /// <summary>Drive the door from the network. Absolute, never a toggle.</summary>
    /// <param name="instant">True for a periodic snapshot — a silent correction,
    /// not an event, so it snaps rather than swinging and creaking a door nobody
    /// touched. Snapshots also yield to a very recent local press.</param>
    public void NetSetOpen(bool open, bool instant)
    {
        if (instant && Time.unscaledTime - _localPressAt < LocalPressGrace) return;
        if (open == IsOpen) return;
        ApplyOpen(open, instant, fromNetwork: true);
    }

    void ApplyOpen(bool open, bool instant, bool fromNetwork)
    {
        // Pick the swing direction the moment we OPEN, from whoever is standing
        // there, so the leaf always moves away from them and cannot shove them
        // through a wall. A remote-driven open has no local presser to measure,
        // so it keeps whichever direction this door last used.
        if (swingRule == SwingRule.AlwaysPositive) _openSign = 1f;
        else if (swingRule == SwingRule.AlwaysNegative) _openSign = -1f;
        else if (open && !fromNetwork) _openSign = SideOfPlayer();

        IsOpen = open;
        _targetAngle = open ? openAngle * _openSign : 0f;

        if (instant)
        {
            _angle = _targetAngle;
            transform.localRotation = _closedLocal * Quaternion.AngleAxis(_angle, AxisVector());
        }
        else
        {
            var clip = open ? openClip : closeClip;
            if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position, clipVolume);
        }
    }

    /// <summary>+1 or -1: which way this leaf should travel to get out of the
    /// player's way.
    ///
    /// The sign convention is fixed by the pack's geometry, which was measured
    /// rather than guessed. Both leaf meshes (Door1_02, Door2_02) run from local
    /// x ≈ 0 — the hinge — out to x ≈ +1.10, and are thin in local Z, so +X is
    /// the leaf and ±Z are its two faces.
    ///
    /// Unity's yaw maps right → back: Quaternion.AngleAxis(+90, up) * (1,0,0)
    /// = (0,0,-1). So a POSITIVE angle carries the leaf toward -Z, and a player
    /// standing on the +Z face needs exactly that. Getting this backwards swings
    /// the door into their face, which is how it read on the first pass.</summary>
    float SideOfPlayer()
    {
        var player = PlayerTransform();
        if (player == null) return _openSign;
        float z = transform.InverseTransformPoint(player.position).z;
        if (Mathf.Abs(z) < 1e-4f) return _openSign;   // dead-on: don't flip on noise
        return z > 0f ? 1f : -1f;
    }

    static Transform _player;
    static Transform PlayerTransform()
    {
        if (_player == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            _player = go != null ? go.transform : null;
        }
        return _player;
    }

    // ── motion ───────────────────────────────────────────────────────────

    protected override void Update()
    {
        base.Update();   // Interactable: F handling, prompt ownership, gaze gate

        if (Mathf.Approximately(_angle, _targetAngle)) return;

        float step = (swingTime > 0.001f ? openAngle / swingTime : 100000f) * Time.deltaTime;
        _angle = Mathf.MoveTowards(_angle, _targetAngle, step);
        transform.localRotation = _closedLocal * Quaternion.AngleAxis(_angle, AxisVector());
    }

    Vector3 AxisVector()
    {
        switch (hinge)
        {
            case HingeAxis.LocalX: return Vector3.right;
            case HingeAxis.LocalZ: return Vector3.forward;
            default:               return Vector3.up;
        }
    }

    // ── diagnostics ──────────────────────────────────────────────────────

    static bool _warnedBaked;

    /// A door whose own MeshRenderer is disabled is one MeshCombineTool ate.
    /// It will swing perfectly and look like nothing happened, because the copy
    /// you can see belongs to __CombinedMeshes. Say so once, by name.
    void WarnIfStillBaked()
    {
        if (_warnedBaked) return;
        var mr = GetComponent<MeshRenderer>();
        if (mr == null || mr.enabled) return;
        _warnedBaked = true;
        Debug.LogWarning(
            $"[VillageDoor] '{name}' has its MeshRenderer disabled — it is still baked into a " +
            "__CombinedMeshes cluster, so it will swing invisibly behind a welded copy of itself. " +
            "Run Tools ▸ Optimize ▸ Un-bake Village Doors once in the Editor.", this);
    }

    // ── identity ─────────────────────────────────────────────────────────

    /// FNV-1a over the hierarchy path.
    ///
    /// ⚠️ NOT string.GetHashCode: .NET Core randomises string hashing per
    /// process, so the host and the guest would compute different ids for the
    /// same door and every message would address a door that isn't there. This
    /// has to be an algorithm, not a runtime service.
    static int StableId(Transform t)
    {
        uint h = 2166136261u;
        Feed(ref h, Path(t));
        return unchecked((int)h);
    }

    static void Feed(ref uint h, string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            h ^= s[i];
            h *= 16777619u;
        }
    }

    static string Path(Transform t)
    {
        var sb = new System.Text.StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent)
            sb.Insert(0, p.name + "/");
        return sb.ToString();
    }
}
