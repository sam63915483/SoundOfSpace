using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lets the player plant saplings straight from the hotbar: select the SAPLINGS
/// slot and press the primary action to enter placement (reusing the build
/// system's GhostPlacement). No build-menu entry or tutorial unlock required —
/// each placed sapling spends one Sapling from the stack.
///
/// Uses an authored isSapling BuildableEntry if one exists in BuildMenuUI;
/// otherwise synthesizes one from the first tree prefab, so planting works with
/// zero inspector wiring (SaplingGrowth scales it down while young).
///
/// Auto-singleton with MainMenu skip — ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1 in CLAUDE.md).
/// </summary>
public class SaplingPlanter : MonoBehaviour
{
    public static SaplingPlanter Instance { get; private set; }

    BuildableEntry _synthEntry;
    bool _wasSaplingSelected;   // rising-edge tracking so Esc/N stays cancelled

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("SaplingPlanter");
        DontDestroyOnLoad(go);
        go.AddComponent<SaplingPlanter>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        var hb = Hotbar.Instance;
        Hotbar.ItemId sel = hb != null ? hb.GetEquippedSlotId() : Hotbar.ItemId.None;
        bool saplingSlot = sel == Hotbar.ItemId.Sapling;
        int count = hb != null ? hb.GetResourceTotal(Hotbar.ItemId.Sapling) : 0;
        bool free = !PlayerController.isInDialogue && !Ship.AnyShipPiloted;
        bool wantPlanting = saplingSlot && count > 0 && free;

        bool placingSapling = GhostPlacement.IsPlacing && GhostPlacement.Current != null
                              && GhostPlacement.Current.IsSaplingPlacement;

        if (placingSapling && !wantPlanting)
        {
            // Deselected saplings, ran out, or entered dialogue/ship — drop the ghost.
            GhostPlacement.Current.CancelPlacement();
        }
        else if (wantPlanting && !GhostPlacement.IsPlacing && !_wasSaplingSelected)
        {
            // Rising edge of selecting the sapling slot: show the ghost right away
            // (no click needed). Edge-gated so pressing Esc/N to cancel stays
            // cancelled until the player reselects the slot.
            var menu = BuildMenuUI.Instance;
            var entry = menu != null ? ResolveSaplingEntry(menu) : null;
            if (entry != null && entry.prefab != null) menu.StartPlacementFromPhone(entry);
            else if (menu != null) Debug.LogWarning("[SaplingPlanter] No sapling/tree prefab available to plant.");
        }

        _wasSaplingSelected = saplingSlot;
    }

    BuildableEntry ResolveSaplingEntry(BuildMenuUI menu)
    {
        // Prefer an authored isSapling entry (lets Sam swap in a dedicated model).
        if (menu.buildables != null)
        {
            foreach (var be in menu.buildables)
                if (be != null && be.isSapling && be.prefab != null) return be;
        }

        // Fall back to a synthetic entry built off the first tree prefab.
        if (_synthEntry != null && _synthEntry.prefab != null) return _synthEntry;
        var ts = TreeSpawner.Instance;
        if (ts == null || ts.treePrefabs == null || ts.treePrefabs.Length == 0 || ts.treePrefabs[0] == null)
            return null;
        _synthEntry = new BuildableEntry
        {
            displayName = "Sapling",
            prefab = ts.treePrefabs[0],
            isSapling = true,
            addBonfireInteractionOnPlace = false,
            woodCost = 0,
            category = BuildableCategory.General,
        };
        return _synthEntry;
    }
}
