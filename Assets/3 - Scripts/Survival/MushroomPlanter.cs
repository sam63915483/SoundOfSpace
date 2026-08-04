using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lets the player plant mushroom spores straight from the hotbar: select a
/// MUSHROOM SAPLING slot and the placement ghost appears — exactly the flow
/// <see cref="SaplingPlanter"/> gives tree saplings, and reusing the same
/// GhostPlacement ground-snap path.
///
/// The BuildableEntry is synthesized per SPECIES at runtime from the species'
/// own source prefab, so "plant the spores you got off a red cap and a red cap
/// grows" needs zero inspector wiring — and adding a mushroom prefab to
/// MushroomSpawner is still the only step to add a species.
///
/// Auto-singleton with MainMenu skip — ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1 in CLAUDE.md).
/// </summary>
public class MushroomPlanter : MonoBehaviour
{
    public static MushroomPlanter Instance { get; private set; }

    BuildableEntry _entry;         // rebuilt whenever the selected species changes
    string _entrySpecies;
    bool _wasSporeSelected;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("MushroomPlanter");
        DontDestroyOnLoad(go);
        go.AddComponent<MushroomPlanter>();
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
        bool sporeSlot = slot.id == Hotbar.ItemId.MushroomSapling && slot.count > 0;
        bool free = !PlayerController.isInDialogue && !Ship.AnyShipPiloted;
        bool wantPlanting = sporeSlot && free;

        bool placingSpores = GhostPlacement.IsPlacing && GhostPlacement.Current != null
                             && GhostPlacement.Current.IsMushroomPlacement;

        if (placingSpores && !wantPlanting)
        {
            // Deselected, ran out, or entered dialogue/ship — drop the ghost.
            GhostPlacement.Current.CancelPlacement();
        }
        else if (wantPlanting && !GhostPlacement.IsPlacing && !_wasSporeSelected)
        {
            // Rising edge of selecting a spore slot: show the ghost right away.
            // Edge-gated so Esc/N to cancel stays cancelled until reselect.
            var menu = BuildMenuUI.Instance;
            var entry = menu != null ? ResolveEntry(slot.mushroomSpecies) : null;
            if (entry != null && entry.prefab != null) menu.StartPlacementFromPhone(entry);
            else if (menu != null)
                Debug.LogWarning($"[MushroomPlanter] No prefab for mushroom species '{slot.mushroomSpecies}'.");
        }

        _wasSporeSelected = sporeSlot;
    }

    BuildableEntry ResolveEntry(string species)
    {
        if (string.IsNullOrEmpty(species)) return null;
        if (_entry != null && _entrySpecies == species && _entry.prefab != null) return _entry;

        var prefab = MushroomRegistry.PrefabFor(species);
        if (prefab == null) return null;

        _entrySpecies = species;
        _entry = new BuildableEntry
        {
            displayName = MushroomRegistry.DisplayName(species),
            prefab = prefab,
            // isSapling rides the whole existing ground-snap placement flow;
            // isMushroomSapling is what branches the cost + the grower.
            isSapling = true,
            isMushroomSapling = true,
            mushroomSpecies = species,
            addBonfireInteractionOnPlace = false,
            woodCost = 0,
            crystalCost = 0,
            category = BuildableCategory.General,
        };
        return _entry;
    }
}
