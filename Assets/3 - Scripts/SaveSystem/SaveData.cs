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
    // Tree/oxygen ecosystem (2026-07-21). JsonUtility defaults these to empty on
    // pre-feature saves, so old files load with no saplings/domes/reserve — safe.
    public List<SaplingSave> saplings = new List<SaplingSave>();
    public List<DomeSave> domes = new List<DomeSave>();
    public PlanetO2Save planetO2 = new PlanetO2Save();
    // Per-run stasis-pod slot ("stasis pod N") this run saves to — new games
    // claim the next free N so runs never overwrite each other. Empty on
    // pre-feature saves (falls back to saveName / next-free at save time).
    public string podSlotName;
    // Five-track progression (2026-08-02). JsonUtility gives pre-feature saves an
    // empty ProgressSave, which ApplyState reads as all-zero — an old save loads
    // at level 0 on every track, which is the correct fallback.
    public ProgressSave progress = new ProgressSave();
    // Mushroom economy (2026-08-04). Player-planted mushroom spores + the mushroom
    // they grow into. JsonUtility gives pre-feature saves an empty list, which is
    // the correct "nothing planted" default.
    public List<PlantedMushroomSave> plantedMushrooms = new List<PlantedMushroomSave>();
    // Messages app / repeat buyers (2026-08-07). JsonUtility gives pre-feature
    // saves an empty ledger — no regulars, no threads — the correct default.
    public BuyerLedgerSave buyerLedger = new BuyerLedgerSave();
    /// Tev's fronting loop, one row per character. Unlike buyerLedger (shared
    /// world state) this is PER PLAYER — both players can carry a front at once
    /// and their debts are independent. Parallel lists because JsonUtility can't
    /// do dictionaries.
    public TevFrontingSave tevFronting = new TevFrontingSave();
    public TraxLibrarySave traxLibrary = new TraxLibrarySave();
    public TapeMemorySave tapeMemory = new TapeMemorySave();
    // Galactic Standard Time (2026-08-08). Total in-game minutes since day 1
    // 00:00, ABSOLUTE — unlike the buyer deadlines above, which persist a
    // remaining duration. JsonUtility gives pre-feature saves 0, which
    // GalaxyTime.RestoreMinutes reads as "start a fresh day 1", so old saves
    // open on the same morning a new game does.
    public double galaxyTimeMinutes;
}

// Tev's fronting loop, one row per CHARACTER — see TevFronting. Parallel lists
// keyed by index (JsonUtility can't do dictionaries). No relative-time fields
// here: a debt doesn't expire, so there is no clock to re-anchor.
[Serializable]
public class TevFrontingSave
{
    public List<string> characterIds = new List<string>();
    public List<int> bond = new List<int>();
    public List<int> frontsCompleted = new List<int>();
    public List<string> activeStrain = new List<string>();
    public List<int> activeQty = new List<int>();
    public List<int> owed = new List<int>();
    public List<int> totalRepaid = new List<int>();
    public List<bool> isContact = new List<bool>();
    public List<bool> pitched = new List<bool>();
}

// Persistent per-buyer state for the Messages app (BuyerLedger). Parallel
// lists keyed by index (JsonUtility can't do dictionaries) — same shape as
// WorldPropConsumedSave. Events are flattened: buyer i owns the next
// eventCounts[i] entries of `events`, in order. All times are RELATIVE
// (seconds-ago / seconds-remaining), re-anchored to unscaledTime on load.
[Serializable]
public class BuyerLedgerSave
{
    public List<string> ids = new List<string>();
    public List<int> bond = new List<int>();
    public List<int> deals = new List<int>();
    public List<bool> regular = new List<bool>();
    public List<int> unread = new List<int>();
    public List<int> convo = new List<int>();
    public List<int> askTier = new List<int>();
    public List<int> askQty = new List<int>();
    public List<int> offerPerCap = new List<int>();
    public List<int> counterBack = new List<int>();
    public List<int> windowMinutes = new List<int>();
    public List<float> deadlineSecondsLeft = new List<float>();
    public List<int> eventCounts = new List<int>();
    public List<EvSave> events = new List<EvSave>();
    // 2026-08-16 contract terms (tier-aware orders). ABSENT (empty) on older
    // saves — BuyerLedger.ApplySave reads these two defensively by Count,
    // unlike the always-written-together lists above.
    public List<int> askTapeTier = new List<int>();
    public List<int> modulesBasis = new List<int>();
    // 2026-08-17 loop-feel: today's running totals for the day-wrap message
    // (reset at each day tick) and per-buyer craving. Scalars default to 0 and
    // the list is count-guarded, so old saves load clean.
    public int dayTapesSold;
    public int dayEarned;
    public List<string> dayBondUps = new List<string>();
    public List<int> craving = new List<int>();
    public List<int> lastPurchaseDay = new List<int>();
    public List<string> requestTrackId = new List<string>();
    // 2026-08-18 tape formats. Count-guarded like askTapeTier; absent on old saves.
    public List<int> askKind = new List<int>();
    public List<int> songsBought = new List<int>();

    [Serializable]
    public class EvSave
    {
        public int type; public float secondsAgo; public int a; public int b; public int tier; public int c;
        // 2026-08-17: frozen text for snapshot-style events (the day wrap).
        // null/"" on every event from before the field existed.
        public string s;
        // 2026-08-18 tape formats: the tape FORMAT + 1 (0 = pre-feature event).
        public int k;
    }
}

// A planted mushroom sapling OR the mushroom it matured into (the MushroomGrowth
// component stays after Mature(), so one DTO covers both). The twin of
// SaplingSave — body-local so it survives orbital motion — except the species is
// stored by KEY (the source prefab's NAME) rather than by index, so reordering
// MushroomSpawner.mushroomPrefabs can't turn a saved red cap into a blue one.
[Serializable]
public class PlantedMushroomSave
{
    public string bodyName;
    public Vector3 localPos;
    public Quaternion localRot;
    public float growth;          // 0..1; >= 1 restores as a mature, choppable mushroom
    public string speciesKey;
    // Mature size as a multiple of the prefab scale, rolled from the same 1–5×
    // band wild mushrooms use. 0 on pre-feature saves, which restore at 1×.
    public float sizeMultiplier;
    // Stable identity for the multiplayer layer: a planted prop has no seed
    // cell to address, so harvest deltas travel keyed by this instead. Minted
    // (GUID) at plant time; empty on pre-feature saves, which mint on load.
    public string plantedId;
}

// PlayerProgress state. `scores` is indexed by the ProgressTrack enum, so that
// enum must never be reordered — append only (there's a matching warning on it).
// A short array from an older save just leaves the newer tracks at 0.
[Serializable]
public class ProgressSave
{
    public int[] scores;
    // Worlds already counted for Explorer, by CelestialBody.bodyName. Stored as
    // names because the bodies are procedurally rebuilt each load and carry no
    // stable id — the name is the only thing that survives.
    public List<string> visitedWorlds = new List<string>();
}

// A planted sapling OR a matured planted tree (growth >= 1 — the SaplingGrowth
// component stays on the object after maturing, so one DTO covers both).
// Positions are parent-body-local so they survive orbital motion, mirroring
// PlacedBuildingSave.
[Serializable]
public class SaplingSave
{
    public string bodyName;
    public Vector3 localPos;
    public Quaternion localRot;
    public float growth;        // 0..1; >= 1 restores as a mature planted tree
    public int prefabIndex;     // index into TreeSpawner.treePrefabs
    // Same wire identity PlantedMushroomSave carries — see the comment there.
    public string plantedId;
}

// A placed bubble dome. Captured separately from PlacedBuildingSave so fuel
// rides along and restore doesn't depend on the runtime-injected buildable
// entry (SaveCollector loads the prefab from Resources directly).
[Serializable]
public class DomeSave
{
    public string bodyName;
    public Vector3 localPos;
    public Quaternion localRot;
    public float fuel = 100f;   // 0..100 %
    // Sealed-greenhouse accumulated level. -1 = save predates the field →
    // dome re-pressurizes from its production floor on load.
    public float interior = -1f;
}

// Per-planet O2 vented into the atmosphere by full domes (PlanetOxygen's
// ventedReserve dict, flattened to parallel lists — JsonUtility can't do dicts).
[Serializable]
public class PlanetO2Save
{
    public List<string> ventedBodies = new List<string>();
    public List<float> ventedValues = new List<float>();
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
    // Populated only when itemId is "Cassette": the PRINT id (see TraxPrints).
    // Like a species this IS part of the stack's identity — losing it would
    // merge two different songs into one stack on load.
    public string cassetteId;
    // Populated only when itemId is "Mushroom" / "MushroomSapling": the species
    // key (source prefab name). Stacks are species-pure, so this IS part of the
    // stack's identity — losing it would merge two species on load.
    public string mushroomSpecies;
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
    // Backup oxygen tanks (2026-07-15). -1 = "unknown" (save predates the
    // feature) → OxygenManager.ApplyState treats it as full tanks.
    public float reserveO2 = -1f;
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

// The TRAX project shelf on the shuttle computer — WORLD state, not per
// character, so both players in co-op see the same projects. Parallel lists
// indexed by module (JsonUtility can't do dictionaries), same shape as the
// other multi-field rows in this file.
//
// This is a NAME plus a whole TRACK, copied — not a reference to one. It has to
// survive its author deleting some other project, and a cassette printed from
// it has to keep sounding the same afterwards.
[Serializable]
public class TraxProjectSave
{
    public string id;
    public string name;
    public long savedAt;
    public int key;
    public List<float> dials = new List<float>();      // 6, dial order
    public List<int> preset = new List<int>();         // 6, module order
    public List<int> variation = new List<int>();      // 6, module order
    public List<bool> active = new List<bool>();       // 6, module order — which were PLAYING
    // The arrangement (2026-08-17): one row per section, in play order. The
    // legacy track fields above stay — they are the FIRST section, so every
    // old reader keeps working, and a record saved before songs existed
    // (empty list) loads as a one-section song of that track.
    public List<TraxSectionSave> sections = new List<TraxSectionSave>();
}

// One section of a song: a length in bars plus a whole track, copied.
[Serializable]
public class TraxSectionSave
{
    public int bars;
    public int key;
    public List<float> dials = new List<float>();      // 6, dial order
    public List<int> preset = new List<int>();         // 6, module order
    public List<int> variation = new List<int>();      // 6, module order
    public List<bool> active = new List<bool>();       // 6, module order
}

// One PRESSING. Frozen at print time and never edited, so a cassette in a
// pocket cannot change song when its project is edited or deleted. Same
// per-module parallel lists as a project row.
[Serializable]
public class TraxPrintSave
{
    public string id;
    public string name;
    public int tier;                                   // 1 or 2
    public int key;
    public List<float> dials = new List<float>();
    public List<int> preset = new List<int>();
    public List<int> variation = new List<int>();
    public List<bool> active = new List<bool>();
    // 2026-08-18 tape formats: the FORMAT (TraxKind: 0 demo / 1 half / 2 full)
    // and the arrangement — one row per section, exactly the TraxProjectSave
    // precedent. Empty list = a pre-format row: loads as a demo of the legacy
    // track fields above, under its old "t"-prefixed id.
    public int kind;
    public List<TraxSectionSave> sections = new List<TraxSectionSave>();
}

// What each alien remembers about you. WORLD state — an alien who has heard a
// song has heard it, whichever co-op partner played it.
//
// heardDials is FLATTENED: alien i owns the next heardCounts[i] * 6 floats, in
// order. Parallel lists because JsonUtility can't do dictionaries, and the flat
// list because it can't do jagged arrays either.
// Open genre requests from contacts. Times are RELATIVE (seconds-ago),
// re-anchored on load like every other timed thing in this file.
[Serializable]
public class TapeRequestSave
{
    public List<string> ids = new List<string>();
    public List<string> genres = new List<string>();
    public List<float> secondsAgo = new List<float>();
    public List<bool> seen = new List<bool>();
}

[Serializable]
public class TapeMemorySave
{
    public List<string> ids = new List<string>();
    public List<int> bond = new List<int>();
    public List<bool> contact = new List<bool>();
    public List<int> heardCounts = new List<int>();
    public List<float> heardDials = new List<float>();
    // 2026-08-17 loop-feel D: which tracks (TraxTrack.TrackId lineage, stored
    // as long — JsonUtility has no uint) each alien has BOUGHT. Count-guarded
    // on read; absent on older saves.
    public List<int> boughtCounts = new List<int>();
    public List<long> boughtTracks = new List<long>();
    // 2026-08-18 tape formats: Half/Full pressings are remembered by SongId
    // (see TapeMemory). Count-guarded; absent on older saves.
    public List<int> heardSongCounts = new List<int>();
    public List<long> heardSongs = new List<long>();
    public List<int> boughtSongCounts = new List<int>();
    public List<long> boughtSongs = new List<long>();
}

[Serializable]
public class TraxLibrarySave
{
    public List<TraxProjectSave> projects = new List<TraxProjectSave>();
    public List<string> installedPlugins = new List<string>();
    public List<TraxPrintSave> prints = new List<TraxPrintSave>();

    // The computer's cassette machine (CassetteDeck). World state, like the
    // shelf and the print table: one computer, one slot, one eject.
    //
    // 0 = the slot is empty, 1 or 2 = an UNPRINTED blank of that tier is seated.
    // A printed tape is never in the slot — printing moves it to the eject.
    public int deckInsertedTier;

    // Print id of a finished tape left unclaimed on the eject, or "" for none.
    public string deckEjectedPrintId = "";

    // 2026-08-18 tape formats: the seated blank's FORMAT (TraxKind). 0 on
    // older saves — with a tier seated that correctly reads as a Demo blank.
    public int deckInsertedKind;
}

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
    // Mission 1 cold open — mirrors EarlyGameProgress.IntroPlayed. Old saves
    // default to false (intro not yet played); harmless since the load path
    // already skips the intro (PendingLoad.Data != null).
    public bool introPlayed;
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
    // Named INT counters (2026-08-04) — flags can't count. Added for the Tev
    // mushroom onboarding (how many of his caps you sold, how many batches he's
    // fronted). Parallel lists, JsonUtility-safe; empty on pre-feature saves.
    public List<string> counterNames = new List<string>();
    public List<int> counterValues = new List<int>();
}
