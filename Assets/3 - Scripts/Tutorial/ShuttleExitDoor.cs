using UnityEngine;

// The shuttle's exit door / boarding ramp. Lives on ExitDoor_Pivot — an empty
// at the door slab's BOTTOM edge — with Sam's "door" slab parented under it.
// Rotating the pivot around local X folds the door outward and down until it
// lies as a ramp. The player never controls it: it stays shut (a plain solid
// wall) until the orientation film ends, when ShuttleArrivalSequence calls
// Open(). Open Angle / Open Time are the hand-tuning knobs for the perfect
// door-to-ramp motion.
public class ShuttleExitDoor : MonoBehaviour
{
    [Tooltip("Degrees the door folds outward-down. 90 = flat horizontal; more lets the tip reach the ground.")]
    public float openAngle = 115f;

    [Tooltip("Seconds the fold takes.")]
    public float openTime = 2.5f;

    Quaternion _restRot;
    float _t;
    bool _opening;
    bool _open;

    public bool IsOpen => _open;

    /// Time.time at which the ramp first started deploying this session, or -1 if
    /// it hasn't. TevMushroomOnboarding counts its 120s hidden window from here
    /// (handoff §2.1) — the player gets that long to loot the locker and chop
    /// trees before an NPC turns up.
    public static float OpenedAtTime { get; private set; } = -1f;
    public static bool HasOpened => OpenedAtTime >= 0f;
    static void MarkOpened() { if (OpenedAtTime < 0f) OpenedAtTime = Time.time; }

    /// Statics survive a return to the main menu, so without this a run that
    /// opened the ramp leaves the stamp set for the NEXT run — and anything
    /// timing off it (Tev's hidden window) fires instantly on a fresh game.
    /// Called by NewGameReset and on every MainMenu load.
    public static void ResetOpenedStamp() { OpenedAtTime = -1f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void HookRunReset()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) =>
        {
            if (scene.name == "MainMenu") ResetOpenedStamp();   // a run ended
        };
    }

    void Awake()
    {
        _restRot = transform.localRotation;

        // Loaded games are always post-intro (saving requires the stasis pod),
        // and the film-end event that opens the door never replays on a load —
        // without this a save made after the ramp deployed reloads with the
        // door shut and the player sealed in the shuttle forever.
        // MUST be Awake: PendingLoad consumes+clears Data in the sceneLoaded
        // callback, which fires after Awake but BEFORE Start.
        if (PendingLoad.Data != null) OpenInstant();
    }

    void Start()
    {
        // Fallback for any load path where Data was already consumed by the
        // time we woke: the load process spawns a [SaveLoadRunner] that lives
        // through the first frames — its presence means this boot is a load.
        if (!_open && FindObjectOfType<SaveLoadRunner>() != null) OpenInstant();
    }

    public void Open()
    {
        if (_open || _opening) return;
        _opening = true;
        _t = 0f;
        MarkOpened();
        if (openSound != null) AudioSource.PlayClipAtPoint(openSound, transform.position, 0.9f);
    }

    public void OpenInstant()
    {
        _opening = false;
        _open = true;
        MarkOpened();
        transform.localRotation = _restRot * Quaternion.AngleAxis(openAngle, Vector3.right);
    }

    void Update()
    {
        // Shuttle-travel flight seal: fold the ramp back up into a solid wall.
        if (_closing)
        {
            _closeT += Time.deltaTime;
            float cu = Mathf.Clamp01(_closeT / Mathf.Max(0.01f, openTime));
            float ck = Mathf.Pow(1f - cu, 3f);   // fast lift, soft seat — hydraulics in reverse
            transform.localRotation = _restRot * Quaternion.AngleAxis(openAngle * ck, Vector3.right);
            if (cu >= 1f) _closing = false;
            return;
        }

        if (!_opening) return;
        _t += Time.deltaTime;
        float u = Mathf.Clamp01(_t / Mathf.Max(0.01f, openTime));
        // Ease-out with a soft settle at the end — reads as hydraulics, not a hinge flop.
        float k = 1f - Mathf.Pow(1f - u, 3f);
        transform.localRotation = _restRot * Quaternion.AngleAxis(openAngle * k, Vector3.right);
        if (u >= 1f) { _opening = false; _open = true; RiderReleaseBleed.Mark("door-open-complete"); }
    }

    /// Shuttle-travel (2026-08-25): seal the ramp for the flight. Reversible —
    /// ReopenAfterFlight (or a plain Open()) deploys it again on landing; the
    /// load-path auto-open in Awake/Start is untouched, so a PARKED save still
    /// reloads with the ramp down.
    public void CloseForFlight()
    {
        if (_closing) return;
        _opening = false;
        _open = false;
        _closing = true;
        _closeT = 0f;
        if (openSound != null) AudioSource.PlayClipAtPoint(openSound, transform.position, 0.9f);
    }

    public void ReopenAfterFlight()
    {
        _closing = false;
        Open();
    }

    // -- appended after initial release; keep order (serialization) --

    [Tooltip("Hydraulic ramp-deploy sound played when the door starts folding open.")]
    public AudioClip openSound;

    // Shuttle-travel flight seal (2026-08-25). Private runtime state — safe to
    // append; nothing serialized.
    bool _closing;
    float _closeT;
}
