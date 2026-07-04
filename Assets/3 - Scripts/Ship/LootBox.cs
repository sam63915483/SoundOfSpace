using UnityEngine;

// Per-loot-box component. User attaches it to the LootBox prefab in the
// Inspector. Holds the 20 storage slots and computes a stable boxId for
// the save system. Interaction (F-prompt + open) is added in a later task.
[DisallowMultipleComponent]
public class LootBox : MonoBehaviour
{
    public const int SlotCount = 20;

    // Data: same Slot type the Hotbar uses. Allocated once at Awake.
    [System.NonSerialized] Hotbar.Slot[] _slots = new Hotbar.Slot[SlotCount];
    public Hotbar.Slot[] Slots => _slots;

    // Stable identifier — derived from the hierarchy path. Format:
    //   "BoughtShip<N>/<relative-path>"  if under a BoughtShip
    //   "OriginalShip/<relative-path>"    if under a Ship that's not bought
    //   "<absolute-scene-path>"           otherwise (future world placement)
    // Computed once at Awake; not serialized because the value is purely
    // derived from scene hierarchy and BoughtShip.shipNumber.
    [System.NonSerialized] string _boxId;
    public string BoxId => _boxId;

    void Awake()
    {
        _boxId = ComputeBoxId();
        if (_slots == null || _slots.Length != SlotCount) _slots = new Hotbar.Slot[SlotCount];

        // The gameplay scene runs at ambientIntensity=0 (custom atmosphere
        // shaders own the look), so Standard-shader materials with no emission
        // render very dim. The loot box materials are set up to self-emit
        // their own albedo, but Unity strips the _EMISSION keyword on import
        // when m_LightmapFlags is EmissiveIsBlack — leaving the box black at
        // runtime. Force the keyword on here, mirroring ReactorGlow.Awake.
        var renderers = GetComponentsInChildren<MeshRenderer>(true);
        foreach (var mr in renderers)
        {
            var mats = mr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                if (!m.IsKeywordEnabled("_EMISSION")) m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }
    }

    void OnEnable()  { StorageRegistry.Register(this); }
    void OnDisable() { StorageRegistry.Unregister(this); }

    string ComputeBoxId()
    {
        // Walk up to find the nearest ship ancestor.
        Transform shipRoot = null;
        BoughtShip bought = null;
        for (var t = transform; t != null; t = t.parent)
        {
            var ship = t.GetComponent<Ship>();
            if (ship != null) { shipRoot = t; bought = t.GetComponent<BoughtShip>(); break; }
        }

        if (shipRoot != null)
        {
            string relative = RelativePath(transform, shipRoot);
            string prefix   = bought != null ? $"BoughtShip{bought.shipNumber}" : "OriginalShip";
            return $"{prefix}/{relative}";
        }
        // Non-ship loot box (future): use absolute scene path.
        return AbsolutePath(transform);
    }

    static string RelativePath(Transform leaf, Transform root)
    {
        if (leaf == root) return "";
        var stack = new System.Collections.Generic.Stack<string>();
        for (var t = leaf; t != null && t != root; t = t.parent) stack.Push(t.name);
        return string.Join("/", stack.ToArray());
    }

    static string AbsolutePath(Transform leaf)
    {
        var stack = new System.Collections.Generic.Stack<string>();
        for (var t = leaf; t != null; t = t.parent) stack.Push(t.name);
        return string.Join("/", stack.ToArray());
    }

    // ── Interaction ──────────────────────────────────────────────────

    // Property (not const) so the interact glyph tracks the active input
    // device — pad players see the X/Square icon, not "F".
    static string PromptText => $"Press {PromptGlyphs.Interact} to open storage";

    bool _playerInside;

    void OnTriggerEnter(Collider other)
    {
        if (other == null || !other.CompareTag("Player")) return;
        _playerInside = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (other == null || !other.CompareTag("Player")) return;
        _playerInside = false;
        InteractPromptUI.Clear(this);
        // If this box is the one currently open, close it when the player walks out.
        // RequestClose returns the cursor-held item to its source slot first.
        if (StorageUI.Instance != null && StorageUI.Instance.IsOpen)
            StorageUI.Instance.RequestClose();
    }

    void Update()
    {
        if (!_playerInside) return;
        if (!CanInteract()) { InteractPromptUI.Clear(this); return; }

        InteractPromptUI.Show(this, PromptText);

        if ((Input.GetKeyDown(KeyCode.F) || TutorialGate.PadPressed(TutorialGate.PadButton.X))
            && InteractGaze.IsLookingAt(this))
        {
            // Same-frame race guard: StorageUI may have just consumed this F
            // press to close the panel. CanInteract() above flipped from
            // false→true when StorageUI.IsOpen flipped to false, but the F
            // GetKeyDown is still true this frame. Without this check the
            // panel would close then immediately reopen.
            if (StorageUI.ConsumedFThisFrame) return;
            InteractPromptUI.Clear(this);
            StorageUI.Instance?.Open(this);
        }
    }

    bool CanInteract()
    {
        if (StorageUI.Instance != null && StorageUI.Instance.IsOpen) return false;
        if (PlayerController.isInDialogue) return false;
        if (PlayerController.isMapOpen)    return false;
        if (PlayerPhoneUI.IsOpen)          return false;
        if (Ship.FindPilotedShip() != null) return false;
        return true;
    }
}
