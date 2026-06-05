using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SaveData
{
    public int version = 1;
    public string saveName;
    public string isoTimestamp;

    public PlayerSave player = new PlayerSave();
    public ShipSave ship = new ShipSave();
    public ResourcesSave resources = new ResourcesSave();
    public O2Save oxygen = new O2Save();
    public WalletSave wallet = new WalletSave();
    public WoodSave wood = new WoodSave();
    public CrystalSave crystal = new CrystalSave();
    public FishInventorySave fishInventory = new FishInventorySave();
    public TutorialSave tutorial = new TutorialSave();
    public List<NPCSave> npcs = new List<NPCSave>();
    public List<PlacedBuildingSave> buildings = new List<PlacedBuildingSave>();
    public List<LoosePartSave> looseParts = new List<LoosePartSave>();
    public CassetteSave cassette = new CassetteSave();
    public EquipmentSave equipment = new EquipmentSave();
    public WorldFlagsSave worldFlags = new WorldFlagsSave();
    public BonusTutorialSave bonusTutorial = new BonusTutorialSave();
    public MapTutorialSave mapTutorial = new MapTutorialSave();
    public List<CelestialBodySave> celestialBodies = new List<CelestialBodySave>();
    public AlienKillsSave alienKills = new AlienKillsSave();
    public WorldPropConsumedSave treesMined = new WorldPropConsumedSave();
    public WorldPropConsumedSave mushroomsConsumed = new WorldPropConsumedSave();
    public WorldPropConsumedSave crystalsMined = new WorldPropConsumedSave();
    public EarlyGameProgressSave earlyGame = new EarlyGameProgressSave();
    public NoteSave notes = new NoteSave();
    public BuildMenuLockSave buildMenuLock = new BuildMenuLockSave();
    public CompassSave compass = new CompassSave();
    public List<EnemySave> enemies = new List<EnemySave>();
    // EnemySpawner cooldown state — round-tripped so save-cycling can't reset
    // the spawn interval. Defaults of 0 / 0 behave like a fresh spawner.
    public float enemySpawnTimer;
    public int enemyRegularsSinceElite;
    // Ships purchased from ShipMarketNPC (tagged with BoughtShip). The scene's
    // main ship is still saved separately in `ship` above; this list only
    // covers runtime-spawned extras so the player's fleet round-trips.
    public List<ExtraShipSave> extraShips = new List<ExtraShipSave>();
    public SpaceDustSave spaceDust = new SpaceDustSave();
    public HotbarSave hotbar = new HotbarSave();
    public List<StorageSave> storages = new List<StorageSave>();
    public AIStateSave aiState = new AIStateSave();
    public NameStoreSave nameStore = new NameStoreSave();
    public StoryDirectorSave storyDirector = new StoryDirectorSave();
}

[Serializable]
public class HotbarSlotSave
{
    public string itemId;  // Hotbar.ItemId enum.ToString(): "None", "Wood", "Pistol", ...
    public int count;
    // Populated only when itemId == "Fish". null otherwise. JsonUtility
    // serializes null-valued class fields as missing-from-JSON, so old
    // saves loading this schema get fishData = null automatically (the
    // correct default for non-fish slots in pre-Phase 1 saves).
    public FishEntrySave fishData;
    // Phase 3: 5-slot bag contents. null/empty when itemId != "FishBag".
    // JsonUtility serializes null lists as missing-from-JSON so old saves
    // load with bagContents = null. Recursive but only one level deep —
    // bags can't contain bags by current design.
    public List<HotbarSlotSave> bagContents;
}

// Flat DTO mirror of FishEntry for JsonUtility. Lives alongside
// HotbarSlotSave so any slot in any container (hotbar, storage, future
// fish bag) can carry per-fish data.
[Serializable]
public class FishEntrySave
{
    public string fishType;          // "Common" | "Uncommon" | "Rare"
    public int weightLbs;
    public Color fishColor;
}

[Serializable]
public class HotbarSave
{
    public List<HotbarSlotSave> slots = new List<HotbarSlotSave>();
}

[Serializable]
public class StorageSave
{
    public string boxId;
    public List<HotbarSlotSave> slots = new List<HotbarSlotSave>();
}

[Serializable]
public class ExtraShipSave
{
    public string name;        // for diagnostics only — re-spawn uses tier
    public string tier;        // ShopItemKind enum name: ShipFull / ShipNoDish / ShipHull
    public int shipNumber;     // legend label "Ship N" — preserves first-bought-stays-Ship-1 across save/load
    public BodyRelativeTransform xform = new BodyRelativeTransform();
    public bool leftAttached = true;
    public bool rightAttached = true;
    public bool dishAttached = true;
    public bool solarAttached = true;
    public bool hatchOpen;
    public bool canFly = true;
    public bool isPiloted;
    public float headlightIntensity;
    // Absolute units (not percent) so they survive future tuning of powerMax /
    // fuelMax. -1f means the save predates per-ship power/fuel; ApplyExtraShips
    // then keeps the 50% spawn defaults from ShipMarketNPC.SpawnShipInstance.
    public float power = -1f;
    public float fuel  = -1f;
}

[Serializable]
public class EnemySave
{
    public string kind;                                          // "regular" or "elite"
    public BodyRelativeTransform xform = new BodyRelativeTransform();
    public float health;
}

[Serializable]
public class AlienKillsSave
{
    // Cell IDs of streamed alien NPCs that were killed. The spawner's
    // streaming loop skips any cell in this set so the corpse doesn't
    // respawn as a new alien on load.
    public List<long> killedSpawnedCells = new List<long>();
    // Parallel array to killedSpawnedCells: bodyName for each cell.
    // Legacy saves (pre-multi-planet) leave this empty — load path
    // treats those as Humble Abode.
    public List<string> killedSpawnedCellBodies = new List<string>();
    // GameObject names of pre-placed scene aliens (Alien3/4/6/7) that
    // were killed. On load, those GameObjects are destroyed silently.
    public List<string> killedPrePlacedNames = new List<string>();
}

[Serializable]
public class WorldPropConsumedSave
{
    // Cell IDs of streamed world props (trees / mushrooms / crystals) that
    // have been chopped / eaten / mined. The owning spawner's streaming
    // loop skips any cell in this set so the prop doesn't respawn on load.
    public List<long> cells = new List<long>();
    // Parallel array of bodyName per cell. Empty on legacy saves (load path
    // treats those as Humble Abode).
    public List<string> bodyNames = new List<string>();
}

[Serializable]
public class CelestialBodySave
{
    public string bodyName;
    public Vector3 position;
    public Quaternion rotation = Quaternion.identity;
    public Vector3 velocity;
}

[Serializable]
public class BodyRelativeTransform
{
    public string bodyName = "";
    public Vector3 localPos;
    public Quaternion localRot = Quaternion.identity;
    public Vector3 relVelocity;
}

[Serializable]
public class PlayerSave
{
    public BodyRelativeTransform xform = new BodyRelativeTransform();
    public float jetpackFuel = 1f;
    public float downThrustFuel = 1f;
    public float dirThrustFuel = 1f;
    public string heldKind = "";
    public bool flashlightEnabled;
    public float flashlightIntensity;
    // 0 = Off, 1 = Half (50%), 2 = Full (100%). Saves predating the
    // 3-mode toggle leave this at 0; ApplyFlashlight falls back to
    // flashlightEnabled (any "on" intensity becomes Full).
    public int flashlightMode;
}

[Serializable]
public class ShipSave
{
    public BodyRelativeTransform xform = new BodyRelativeTransform();
    public bool hatchOpen;
    public bool canFly = true;
    public bool isPiloted;
    public float headlightIntensity;
    public string damageState = "Full";
    public bool leftAttached = true;
    public bool rightAttached = true;
    public bool dishAttached = true;
    public bool solarAttached = true;
}

[Serializable]
public class ResourcesSave
{
    public float hunger = 100f;
    public float thirst = 100f;
    public float health = 100f;
    public float shipPower = 100f;
    public int   totalDeaths = 0;
}

[Serializable]
public class O2Save
{
    // Defaults = full tanks so pre-feature saves (missing this object) load
    // breathing-safe rather than suffocating on load.
    public float suitO2 = 120f;
    public float hullO2 = 300f;
    public bool cyclopsCheckpointReached;
}

[Serializable]
public class WalletSave { public int money; }

[Serializable]
public class WoodSave { public int wood; }
[Serializable]
public class CrystalSave { public int count; }

[Serializable]
public class FishInventorySave
{
    [Serializable]
    public class Entry
    {
        public string fishType;
        public int weightLbs;
        public Color fishColor;
    }
    public List<Entry> fish = new List<Entry>();
    // Phase 2: true once existing FishInventory entries have been pushed
    // into hotbar/storage on load. JsonUtility defaults to false on old
    // saves missing this field — exactly the right trigger for the
    // one-shot migration in SaveCollector.MigrateFishInventoryToHotbar.
    public bool migratedToHotbar;
}

[Serializable]
public class TutorialSave
{
    public bool started;
    public bool finished;
    // Type name of the step the player was on when saving (e.g. "FlashlightStep").
    // Resolved by name on load so that adding/removing/reordering steps in
    // TutorialSteps.BuildDefault() doesn't break old saves. Falls back to
    // currentStepIndex when empty (legacy saves predating this field).
    public string currentStepTypeName = "";
    public int currentStepIndex;
    public List<bool> stepsComplete = new List<bool>();
    public bool gateEnabled;
    public List<string> unlockedAbilities = new List<string>();
}

[Serializable]
public class NPCSave
{
    public string npcId;
    public string stateString = "";
    public bool completed;
}

[Serializable]
public class PlacedBuildingSave
{
    public string prefabKey;
    public string parentBodyName = "";
    public Vector3 localPos;
    public Quaternion localRot = Quaternion.identity;
}

[Serializable]
public class LoosePartSave
{
    public string partKind;
    public BodyRelativeTransform xform = new BodyRelativeTransform();
    public Vector3 angularVelocity;
}

[Serializable]
public class CassetteSave { public bool insertedInPlayer = true; }

[Serializable]
public class EquipmentSave
{
    public bool fishingRodEquipped;
    public bool guitarEquipped;
    public bool waterBottleEquipped;
    public bool axeEquipped;
    public bool axeUnlocked;
    public bool guitarUnlocked;
    public bool pistolEquipped;
    public bool pistolUnlocked;
    public int pistolAmmo = 10; // default keeps older saves at full mag when this field is missing from JSON
    public bool jetpackUnlocked;
    // New for the early-game revamp. Default true so older saves predating
    // the unlock refactor see the rod + bottle as already unlocked, matching
    // the pre-revamp behavior where they were always available once you got
    // close enough to the relevant NPC / pickup.
    public bool fishingRodUnlocked = true;
    public bool waterBottleUnlocked = true;
}

[Serializable]
public class WorldFlagsSave
{
    public bool lebronLightActive;
}

[Serializable]
public class BonusTutorialSave
{
    public string activeTutorial = "";   // "" | "axe-building" | "fishing"
    public int stepIndex = -1;
    public List<bool> stepsComplete = new List<bool>();
    public bool advanceArmed;
}

// MapTutorial state: a single linear 6-step tutorial bound to the map mode.
// `finished=true` once all six steps are done — the tutorial never appears
// again on that save. While in-flight, `stepIndex` is the active step and
// `stepsComplete` is the per-step completion bitvector.
[Serializable]
public class MapTutorialSave
{
    public bool finished;
    public int stepIndex = -1;
    public List<bool> stepsComplete = new List<bool>();
}

// ── Early-game tutorial progression flags ────────────────────────────────
// Mirrors the static fields in EarlyGameProgress.cs. Adding a new flag = one
// new field here + matching field there + capture/apply in SaveCollector.
[Serializable]
public class EarlyGameProgressSave
{
    public bool noteRead;
    public bool rodPickedUp;
    public bool firstFishCaught;
    public bool oneOfEachCaught;
    public bool firstMealEaten;
    public bool waterBottleDrunk;
    public bool returnedHome;
    public bool tevReturnedDialogueDone;
    public bool cabinBuilt;
    public bool villageCoordsGiven;
    public bool fishVendorVisited;
    public bool goodsVendorVisited;
    // AI knowledge-gating flag. JsonUtility defaults bool to false on old
    // saves missing this field — pre-feature saves will load with the flag
    // unset, which is the correct "story not yet revealed" state.
    public bool orgReveal;
    // §3: true once the player has opened their phone at least once. Gates the
    // persistent "Press X to open your phone." first-message nag.
    public bool hasEverOpenedPhone;
}

// ── Notes the player has picked up and read ──────────────────────────────
[Serializable]
public class NoteSave
{
    public List<string> readNoteIds = new List<string>();
}

// ── Per-blueprint build menu lock state ──────────────────────────────────
[Serializable]
public class BuildMenuLockSave
{
    // false = no restrictions (every blueprint allowed). When true, only
    // entries whose displayName is in unlockedNames are shown in the menu.
    public bool isLockingActive;
    public List<string> unlockedNames = new List<string>();
}

// ── Space dust inventory + per-ship net buffers ──────────────────────────
[Serializable]
public class SpaceNetSave
{
    // BoughtShip.shipNumber for vendor-bought ships; 0 for the scene's
    // original (non-bought) ship. Multiple nets per ship are disambiguated
    // by netIndex.
    public int shipNumber;
    // Index within the owning ship's GetComponentsInChildren<SpaceNet> order.
    public int netIndex;
    public int buffer;
    // Whether the net is currently bolted onto the ship. False means it
    // detached (crash or never installed on this tier) and is either lying
    // around as a loose pickup or simply absent from the ship.
    public bool attached = true;
}

[Serializable]
public class SpaceDustSave
{
    public int playerDust;
    public bool hasFilter;
    public List<SpaceNetSave> nets = new List<SpaceNetSave>();

    // ── Legacy fields (pre-multi-net schema) — kept for backward compat
    // when loading older saves. New captures always write to `nets` only.
    public List<int> netShipNumbers = new List<int>();
    public List<int> netBuffers     = new List<int>();
    public int sceneShipBuffer;
}

// ── Compass HUD waypoints (Phase 1+) ─────────────────────────────────────
// Each waypoint carries an id, a label, and a sourceTag. The sourceTag is a
// scene-tagged Transform name (e.g. "FishingBank", "Cabin") that resolves to
// a world position at runtime. Dynamic-only waypoints (added via a Func<Vector3>)
// are not persisted — only tag-based ones round-trip through saves.
[Serializable]
public class CompassSave
{
    [Serializable]
    public class WaypointEntry
    {
        public string id;
        public string label;
        public string sourceTag;
        public bool active = true;
    }
    public List<WaypointEntry> waypoints = new List<WaypointEntry>();
}

[Serializable]
public class AIMemory
{
    public string text;
    public int importance;            // 0..100
    public AIMemoryKind kind;
    public bool pinned;               // floor — never evicted
    public string isoTimestamp;       // when extracted
    public int formedFromTurn;        // which conversation turn produced this
}

// Note: JsonUtility serializes enums as ints. Adding new values must only
// be done by APPENDING to the end so older saves still deserialize.
[Serializable]
public enum AIMemoryKind
{
    Commitment = 0,
    Fact = 1,
    Preference = 2,
    Event = 3,
    Relationship = 4,
}

[Serializable]
public class AIStateSave
{
    public List<AIMemory> memories = new List<AIMemory>();
    public int standing;                        // -100..+100
    public List<string> recentUserTurns = new List<string>();
    public List<string> recentAITurns = new List<string>();
    public bool dirtyForExtraction;
    public int totalTurns;                       // monotonic — feeds AIMemory.formedFromTurn
    public int storyPhase;                       // (int)StoryPhase — gates persona + lore in GameKnowledgeBase
}

// Player-chosen player name + player-chosen AI name + first-contact flag.
// Mirrors EarlyGameProgressSave pattern: parallel to a static class
// (NameStore.cs). JsonUtility defaults old saves to empty strings + false →
// the next AIChatScreen open reruns the first-contact scripted flow, which
// is the correct fallback behaviour for a pre-feature save.
[Serializable]
public class NameStoreSave
{
    public string playerName = "";
    public string aiName     = "";
    public bool firstContactComplete = false;
}

[Serializable]
public class StoryDirectorSave
{
    public int currentStoryStep = 0;
    public float tevTrust = 0f;
    public List<string> flagNames = new List<string>();
    public List<bool>   flagValues = new List<bool>();
    public List<string> activeObjectives = new List<string>();
    public List<string> completedObjectives = new List<string>();
    public List<string> unlockedQuestions = new List<string>();
    public string pendingConversationId = "";
    public string pendingNodeId = "";
}
