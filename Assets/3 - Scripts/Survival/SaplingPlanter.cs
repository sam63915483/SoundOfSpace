using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lets the player plant saplings straight from the hotbar: select the SAPLINGS
/// slot and press the primary action to enter placement (reusing the build
/// system's GhostPlacement). No build-menu entry or tutorial unlock required —
/// each placed sapling spends one Sapling from the stack.
///
/// The BuildableEntry is synthesized per SPECIES at runtime from the selected
/// slot's own species key, exactly the way MushroomPlanter does it — so "plant
/// the sapling you got off a birch and a birch grows" needs zero inspector
/// wiring, and adding a tree prefab to TreeSpawner is still the only step to
/// add a species.
///
/// ⚠️ It deliberately does NOT prefer an authored isSapling entry any more.
/// That is what caused Sam's 2026-09-16 bug: an authored entry pins ONE prefab,
/// so every sapling grew into the same tree no matter what you chopped. An
/// authored entry is now only the fallback, for a catalogue with no TreeSpawner
/// to read.
///
/// Auto-singleton with MainMenu skip — ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1 in CLAUDE.md).
/// </summary>
public class SaplingPlanter : MonoBehaviour
{
    public static SaplingPlanter Instance { get; private set; }

    BuildableEntry _synthEntry;
    string _entrySpecies;       // which species _synthEntry was built for
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
        Hotbar.Slot slot = hb != null ? hb.GetEquippedSlot() : default;
        Hotbar.ItemId sel = slot.id;
        bool saplingSlot = sel == Hotbar.ItemId.Sapling;
        // The SELECTED stack's count, not the total across every species — with
        // species-pure stacks those are different numbers, and planting spends
        // from the stack in your hand.
        int count = saplingSlot ? slot.count : 0;
        bool free = !PlayerController.isInDialogue && !Ship.AnyShipPiloted;
        bool wantPlanting = saplingSlot && count > 0 && free;

        // Mushroom spores also set isSapling (they reuse the ground-snap flow),
        // so exclude them here or this would cancel MushroomPlanter's ghost the
        // frame after it opens.
        bool placingSapling = GhostPlacement.IsPlacing && GhostPlacement.Current != null
                              && GhostPlacement.Current.IsSaplingPlacement
                              && !GhostPlacement.Current.IsMushroomPlacement;

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
            var entry = menu != null ? ResolveSaplingEntry(menu, slot.mushroomSpecies) : null;
            if (entry != null && entry.prefab != null) menu.StartPlacementFromPhone(entry);
            else if (menu != null)
                Debug.LogWarning($"[SaplingPlanter] No prefab for tree species '{slot.mushroomSpecies}'.");
        }

        _wasSaplingSelected = saplingSlot;
    }

    /// <summary>
    /// The entry for ONE species, rebuilt whenever the selected species changes
    /// (mirrors MushroomPlanter.ResolveEntry). The entry carries the species key
    /// so GhostPlacement can spend from the right stack and tell SaplingGrowth
    /// which tree to grow back.
    /// </summary>
    BuildableEntry ResolveSaplingEntry(BuildMenuUI menu, string species)
    {
        var prefab = TreeRegistry.PrefabFor(species);

        // No TreeSpawner yet, or a species key from a save written before this
        // feature: fall back to an authored isSapling entry, then to the first
        // tree prefab, so planting never becomes impossible.
        if (prefab == null)
        {
            if (menu.buildables != null)
                foreach (var be in menu.buildables)
                    if (be != null && be.isSapling && be.prefab != null) return be;

            var ts = TreeSpawner.Instance;
            if (ts == null || ts.treePrefabs == null || ts.treePrefabs.Length == 0 || ts.treePrefabs[0] == null)
                return null;
            prefab = ts.treePrefabs[0];
            species = prefab.name;
        }

        if (_synthEntry != null && _entrySpecies == species && _synthEntry.prefab == prefab)
            return _synthEntry;

        _entrySpecies = species;
        _synthEntry = new BuildableEntry
        {
            displayName = TreeRegistry.DisplayName(species),
            prefab = prefab,
            isSapling = true,
            addBonfireInteractionOnPlace = false,
            woodCost = 0,
            category = BuildableCategory.General,
            // Reuses the mushroom species field on BuildableEntry, the same way
            // the hotbar slot reuses its own. GhostPlacement reads it for BOTH
            // the cost (spend a sapling OF THIS SPECIES) and the grown tree.
            mushroomSpecies = species,
        };
        return _synthEntry;
    }
}
