using System.Collections.Generic;
using UnityEngine;

public static class SaveCollector
{
    // Threshold (× body radius) within which an object is considered "near" a body
    // and saved planet-relative. Beyond this, save world-absolute.
    const float kBodyAttachThreshold = 5f;

    // ───────────────────────── Capture ─────────────────────────

    public static SaveData Capture(string saveName)
    {
        var data = new SaveData
        {
            saveName = saveName,
            isoTimestamp = System.DateTime.Now.ToString("o"),
        };

        CaptureCelestialBodies(data.celestialBodies);
        CapturePlayer(data.player);
        CaptureShip(data.ship);
        CaptureExtraShips(data.extraShips);
        CaptureResources(data.resources);
        CaptureOxygen(data.oxygen);
        CaptureWallet(data.wallet);
        CaptureWood(data.wood);
        CaptureCrystals(data.crystal);
        CaptureHotbar(data.hotbar);
        CaptureStorages(data.storages);
        CaptureFishInventory(data.fishInventory);
        CaptureTutorial(data.tutorial);
        CaptureNPCs(data.npcs);
        CaptureBuildings(data.buildings);
        CaptureLooseParts(data.looseParts);
        CaptureCassette(data.cassette);
        CaptureEquipment(data.equipment);
        CaptureWorldFlags(data.worldFlags);
        CaptureBonusTutorial(data.bonusTutorial);
        CaptureMapTutorial(data.mapTutorial);
        CaptureAIState(data.aiState);
        CaptureNameStore(data.nameStore);
        CaptureAlienKills(data.alienKills);
        CaptureTreesMined(data.treesMined);
        CaptureMushroomsConsumed(data.mushroomsConsumed);
        CaptureCrystalsMined(data.crystalsMined);
        CaptureEarlyGame(data.earlyGame);
        CaptureNotes(data.notes);
        CaptureBuildMenuLock(data.buildMenuLock);
        CaptureCompass(data.compass);
        CaptureStoryDirector(data.storyDirector);
        CaptureEnemies(data);
        CaptureSpaceDust(data);

        return data;
    }

    static void CaptureAlienKills(AlienKillsSave s)
    {
        s.killedSpawnedCells.Clear();
        s.killedSpawnedCellBodies.Clear();
        s.killedPrePlacedNames.Clear();
        var spawner = Object.FindObjectOfType<AlienNPCSpawner>();
        if (spawner == null) return;
        foreach (var kv in spawner.GetKilledCellsWithBody())
        {
            s.killedSpawnedCells.Add(kv.Value);
            s.killedSpawnedCellBodies.Add(kv.Key);
        }
        foreach (var name in spawner.GetKilledPrePlacedNames()) s.killedPrePlacedNames.Add(name);
    }

    static void CaptureTreesMined(WorldPropConsumedSave s)
    {
        s.cells.Clear();
        s.bodyNames.Clear();
        var spawner = Object.FindObjectOfType<TreeSpawner>();
        if (spawner == null) return;
        foreach (var kv in spawner.GetMinedCellsWithBody())
        {
            s.cells.Add(kv.Value);
            s.bodyNames.Add(kv.Key);
        }
    }

    static void CaptureMushroomsConsumed(WorldPropConsumedSave s)
    {
        s.cells.Clear();
        s.bodyNames.Clear();
        var spawner = Object.FindObjectOfType<MushroomSpawner>();
        if (spawner == null) return;
        foreach (var kv in spawner.GetConsumedCellsWithBody())
        {
            s.cells.Add(kv.Value);
            s.bodyNames.Add(kv.Key);
        }
    }

    static void CaptureCrystalsMined(WorldPropConsumedSave s)
    {
        s.cells.Clear();
        s.bodyNames.Clear();
        var spawner = Object.FindObjectOfType<CrystalSpawner>();
        if (spawner == null) return;
        foreach (var kv in spawner.GetConsumedCellsWithBody())
        {
            s.cells.Add(kv.Value);
            s.bodyNames.Add(kv.Key);
        }
    }

    static void CaptureCelestialBodies(List<CelestialBodySave> list)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return;
        foreach (var body in bodies)
        {
            if (body == null) continue;
            list.Add(new CelestialBodySave
            {
                bodyName = body.bodyName,
                position = body.Position,
                rotation = body.transform.rotation,
                velocity = body.velocity,
            });
        }
    }

    static void ApplyCelestialBodies(List<CelestialBodySave> list)
    {
        if (list == null || list.Count == 0) return;
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return;
        foreach (var save in list)
        {
            CelestialBody match = null;
            foreach (var b in bodies)
            {
                if (b != null && b.bodyName == save.bodyName) { match = b; break; }
            }
            if (match == null) continue;
            match.ApplySavedState(save.position, save.rotation, save.velocity);
        }
    }

    static void CapturePlayer(PlayerSave s)
    {
        // Include inactive: player gameObject is SetActive(false) while piloting,
        // and we still need its last position so we can restore it on load.
        var player = Object.FindObjectOfType<PlayerController>(true);
        if (player == null) return;
        var rb = player.Rigidbody;
        if (rb != null) s.xform = CaptureBodyRelative(rb.position, rb.rotation, rb.velocity);
        s.jetpackFuel = player.JetpackFuelPercent;
        s.downThrustFuel = player.DownThrustFuelPercent;
        s.dirThrustFuel = player.DirectionalThrustFuelPercent;

        var pickup = Object.FindObjectOfType<PlayerPickup>();
        if (pickup != null && pickup.GetHeldObject() != null)
            s.heldKind = ResolvePickupKind(pickup.GetHeldObject());

        var fl = Object.FindObjectOfType<PlayerFlashlight>();
        if (fl != null && fl.flashlight != null)
        {
            s.flashlightEnabled = fl.flashlight.enabled;
            s.flashlightIntensity = fl.flashlight.intensity;
            s.flashlightMode = (int)fl.CurrentMode;
        }
    }

    // Walks every Ship in the scene and returns the one WITHOUT a BoughtShip
    // marker. In the current setup the scene has no untagged ship (every ship
    // is bought / debug-spawned), so this typically returns null. Kept for
    // legacy save compatibility where a main ship may have existed.
    static Ship FindMainShip()
    {
        var ships = Object.FindObjectsOfType<Ship>(true);
        if (ships == null || ships.Length == 0) return null;
        foreach (var s in ships)
            if (s != null && s.GetComponent<BoughtShip>() == null) return s;
        return ships[0];
    }

    static void CaptureShip(ShipSave s)
    {
        var ship = FindMainShip();
        if (ship == null) return;
        var rb = ship.Rigidbody;
        if (rb != null) s.xform = CaptureBodyRelative(rb.position, rb.rotation, rb.velocity);
        s.hatchOpen = ship.HatchOpen;
        s.canFly = ship.canFly;
        s.isPiloted = ship.IsPiloted;
        if (ship.headlight != null) s.headlightIntensity = ship.headlight.intensity;

        // damageState is a legacy field (ShipDamageManager removed). We still
        // write "Full" so existing save-load round-trip tests don't regress on
        // string compare; nothing reads it on load.
        s.damageState = "Full";

        // Main ship's parts: read the ThrusterDetachOnImpact on the main ship
        // itself, NOT FindObjectOfType (extras have their own copy).
        var detach = ship.GetComponent<ThrusterDetachOnImpact>();
        if (detach != null)
        {
            s.leftAttached = detach.leftThrusterChild == null || detach.leftThrusterChild.activeSelf;
            s.rightAttached = detach.rightThrusterChild == null || detach.rightThrusterChild.activeSelf;
            s.dishAttached = detach.dishChild == null || detach.dishChild.activeSelf;
            s.solarAttached = detach.solarPanelChild == null || detach.solarPanelChild.activeSelf;
        }
    }

    static void CaptureExtraShips(List<ExtraShipSave> list)
    {
        list.Clear();
        var marked = Object.FindObjectsOfType<BoughtShip>(true);
        if (marked == null) return;
        foreach (var m in marked)
        {
            if (m == null) continue;
            var ship = m.GetComponent<Ship>();
            if (ship == null) continue;
            var rb = ship.Rigidbody;
            var entry = new ExtraShipSave
            {
                name = ship.name,
                tier = m.tier.ToString(),
                shipNumber = m.shipNumber,
                hatchOpen = ship.HatchOpen,
                canFly = ship.canFly,
                isPiloted = ship.IsPiloted,
                headlightIntensity = ship.headlight != null ? ship.headlight.intensity : 0f,
            };
            if (rb != null) entry.xform = CaptureBodyRelative(rb.position, rb.rotation, rb.velocity);
            entry.power = ship.PowerPercent * ship.powerMax;
            entry.fuel  = ship.FuelPercent  * ship.fuelMax;
            var detach = m.GetComponent<ThrusterDetachOnImpact>();
            if (detach != null)
            {
                entry.leftAttached = detach.leftThrusterChild == null || detach.leftThrusterChild.activeSelf;
                entry.rightAttached = detach.rightThrusterChild == null || detach.rightThrusterChild.activeSelf;
                entry.dishAttached = detach.dishChild == null || detach.dishChild.activeSelf;
                entry.solarAttached = detach.solarPanelChild == null || detach.solarPanelChild.activeSelf;
            }
            list.Add(entry);
        }
    }

    static void CaptureResources(ResourcesSave s)
    {
        var rm = ResourceManager.Instance;
        if (rm == null) return;
        s.hunger = rm.HungerPercent * 100f;
        s.thirst = rm.ThirstPercent * 100f;
        s.health = rm.HealthPercent * 100f;
        s.totalDeaths = rm.TotalDeaths;
    }

    static void CaptureOxygen(O2Save s)
    {
        var om = OxygenManager.Instance;
        if (om == null) return;
        s.suitO2 = om.SuitO2;
        s.hullO2 = om.HullO2;
        s.cyclopsCheckpointReached = om.CyclopsCheckpointReached;
    }

    static void CaptureWallet(WalletSave s)
    {
        if (PlayerWallet.Instance != null) s.money = PlayerWallet.Instance.Money;
    }

    static void CaptureWood(WoodSave s)
    {
        if (WoodInventory.Instance != null) s.wood = WoodInventory.Instance.Wood;
    }

    static void CaptureCrystals(CrystalSave s)
    {
        if (CrystalInventory.Instance != null) s.count = CrystalInventory.Instance.Count;
    }

    static void CaptureHotbar(HotbarSave s)
    {
        if (s == null) return;
        s.slots.Clear();
        if (Hotbar.Instance == null) return;
        var live = Hotbar.Instance.GetSlotsForSave();
        for (int i = 0; i < live.Count; i++)
        {
            var slot = live[i];
            s.slots.Add(new HotbarSlotSave
            {
                itemId = slot.id.ToString(),
                count = slot.count,
                fishData = slot.id == Hotbar.ItemId.Fish && slot.fishData != null
                    ? new FishEntrySave
                      {
                          fishType  = slot.fishData.fishType,
                          weightLbs = slot.fishData.weightLbs,
                          fishColor = slot.fishData.fishColor,
                      }
                    : null,
                bagContents = slot.id == Hotbar.ItemId.FishBag && slot.bagContents != null
                    ? SerializeBagContents(slot.bagContents)
                    : null,
            });
        }
    }

    // Phase 3: serialize a bag's 5-slot internal array into a list of
    // HotbarSlotSave entries. Recursive but only one level deep — bags
    // can't contain bags by current design; nested bagContents on a saved
    // sub-entry is ignored on load.
    static List<HotbarSlotSave> SerializeBagContents(Hotbar.Slot[] bag)
    {
        var list = new List<HotbarSlotSave>(bag.Length);
        for (int k = 0; k < bag.Length; k++)
        {
            var s = bag[k];
            list.Add(new HotbarSlotSave
            {
                itemId = s.id.ToString(),
                count = s.count,
                fishData = s.id == Hotbar.ItemId.Fish && s.fishData != null
                    ? new FishEntrySave
                      {
                          fishType  = s.fishData.fishType,
                          weightLbs = s.fishData.weightLbs,
                          fishColor = s.fishData.fishColor,
                      }
                    : null,
                bagContents = null,   // no nested bags
            });
        }
        return list;
    }

    // Phase 3: rebuild a 5-element Slot[] from a saved bagContents list.
    // Defensive: pads/truncates to 5 if the saved list is the wrong length.
    // Public so Hotbar.ApplySlotsFromSave can call it across files.
    public static Hotbar.Slot[] DeserializeBagContentsPublic(List<HotbarSlotSave> saved)
    {
        var arr = new Hotbar.Slot[5];
        if (saved == null) return arr;
        int max = UnityEngine.Mathf.Min(saved.Count, 5);
        for (int k = 0; k < max; k++)
        {
            var e = saved[k];
            if (e == null) continue;
            if (!System.Enum.TryParse<Hotbar.ItemId>(e.itemId, out var id)) continue;
            int count = UnityEngine.Mathf.Clamp(e.count, 0, Hotbar.StackMax(id));
            if (id == Hotbar.ItemId.None || count <= 0) continue;

            FishEntry fish = null;
            if (id == Hotbar.ItemId.Fish)
            {
                if (e.fishData == null) continue;
                fish = new FishEntry(e.fishData.fishType, e.fishData.weightLbs);
                fish.fishColor = e.fishData.fishColor;
            }
            arr[k] = new Hotbar.Slot { id = id, count = count, fishData = fish };
        }
        return arr;
    }

    static void CaptureStorages(System.Collections.Generic.List<StorageSave> list)
    {
        if (list == null) return;
        list.Clear();
        var live = StorageRegistry.All;
        for (int i = 0; i < live.Count; i++)
        {
            var box = live[i];
            if (box == null) continue;
            var entry = new StorageSave { boxId = box.BoxId };
            var slots = box.Slots;
            for (int j = 0; j < slots.Length; j++)
            {
                var slot = slots[j];
                entry.slots.Add(new HotbarSlotSave
                {
                    itemId = slot.id.ToString(),
                    count = slot.count,
                    fishData = slot.id == Hotbar.ItemId.Fish && slot.fishData != null
                        ? new FishEntrySave
                          {
                              fishType  = slot.fishData.fishType,
                              weightLbs = slot.fishData.weightLbs,
                              fishColor = slot.fishData.fishColor,
                          }
                        : null,
                    bagContents = slot.id == Hotbar.ItemId.FishBag && slot.bagContents != null
                        ? SerializeBagContents(slot.bagContents)
                        : null,
                });
            }
            list.Add(entry);
        }
    }

    static void CaptureFishInventory(FishInventorySave s)
    {
        if (FishInventory.Instance == null) return;
        foreach (var f in FishInventory.Instance.AllFish)
        {
            s.fish.Add(new FishInventorySave.Entry
            {
                fishType = f.fishType,
                weightLbs = f.weightLbs,
                fishColor = f.fishColor,
            });
        }
    }

    static void CaptureTutorial(TutorialSave s)
    {
        var tm = TutorialManager.Instance;
        if (tm != null)
        {
            s.started = tm.IsStarted;
            s.finished = tm.IsFinished;
            s.currentStepIndex = tm.CurrentStepIndex;
            s.currentStepTypeName = tm.CurrentStepTypeName;
            s.stepsComplete.Clear();
            foreach (var c in tm.GetStepCompletionFlags())
                s.stepsComplete.Add(c);
        }
        s.gateEnabled = TutorialGate.IsGateEnabled;
        s.unlockedAbilities.Clear();
        foreach (var a in TutorialGate.GetUnlocked())
            s.unlockedAbilities.Add(a.ToString());
    }

    static void CaptureNPCs(List<NPCSave> list)
    {
        foreach (var npc in Object.FindObjectsOfType<NPCDialogue>(true))
        {
            list.Add(new NPCSave
            {
                npcId = "NPCDialogue:" + npc.gameObject.name,
                completed = npc.ConversationCompleted,
            });
        }
        foreach (var npc in Object.FindObjectsOfType<GuitarShopNPC>(true))
        {
            list.Add(new NPCSave
            {
                npcId = "GuitarShopNPC:" + npc.gameObject.name,
                stateString = npc.GetStateString(),
            });
        }
        foreach (var npc in Object.FindObjectsOfType<BonfireNPCDialogue>(true))
        {
            list.Add(new NPCSave
            {
                npcId = "BonfireNPCDialogue:" + npc.gameObject.name,
                completed = npc.FirstTimeDone,
            });
        }
    }

    static void CaptureBuildings(List<PlacedBuildingSave> list)
    {
        // Placed buildings have names "<prefab>_Placed" and are parented to a CelestialBody.
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return;
        foreach (var body in bodies)
        {
            if (body == null) continue;
            for (int i = 0; i < body.transform.childCount; i++)
            {
                var child = body.transform.GetChild(i);
                if (!child.name.EndsWith("_Placed")) continue;
                var prefabKey = child.name.Substring(0, child.name.Length - "_Placed".Length);
                list.Add(new PlacedBuildingSave
                {
                    prefabKey = prefabKey,
                    parentBodyName = body.bodyName,
                    localPos = child.localPosition,
                    localRot = child.localRotation,
                });
            }
        }
    }

    static void CaptureLooseParts(List<LoosePartSave> list)
    {
        // Loose ship parts not currently held by the player and not attached to the ship.
        var pickup = Object.FindObjectOfType<PlayerPickup>();
        var heldObject = pickup != null ? pickup.GetHeldObject() : null;

        foreach (var p in Object.FindObjectsOfType<ThrusterPickup>())
        {
            if (p == null) continue;
            if (p.gameObject == heldObject) continue;
            // Skip pickups that are children of the ship (i.e., still attached/mounted).
            if (p.GetComponentInParent<Ship>() != null) continue;
            var rb = p.GetComponent<Rigidbody>();
            // Use rb.position/rotation rather than transform — transform reflects the
            // interpolated visual pose, which can sit slightly inside terrain when at
            // rest. Saving that exact value caused the part to spawn overlapping the
            // collider on load and the solver depenetrated it violently.
            var pos = rb != null ? rb.position : p.transform.position;
            var rot = rb != null ? rb.rotation : p.transform.rotation;
            list.Add(new LoosePartSave
            {
                partKind = "thruster" + p.thrusterType,  // "thrusterLeft", "thrusterRight", "thrusterDish", "thrusterSolar"
                xform = CaptureBodyRelative(pos, rot, rb != null ? rb.velocity : Vector3.zero),
                angularVelocity = rb != null ? rb.angularVelocity : Vector3.zero,
            });
        }
        // Same pass for free-floating SpaceNet pickups (crash-detached, dropped).
        foreach (var n in Object.FindObjectsOfType<SpaceNetPickup>())
        {
            if (n == null) continue;
            if (n.gameObject == heldObject) continue;
            if (n.GetComponentInParent<Ship>() != null) continue;
            var rb = n.GetComponent<Rigidbody>();
            var pos = rb != null ? rb.position : n.transform.position;
            var rot = rb != null ? rb.rotation : n.transform.rotation;
            list.Add(new LoosePartSave
            {
                partKind = n.side == SpaceNetPickup.Side.Left ? "spacenetLeft" : "spacenetRight",
                xform = CaptureBodyRelative(pos, rot, rb != null ? rb.velocity : Vector3.zero),
                angularVelocity = rb != null ? rb.angularVelocity : Vector3.zero,
            });
        }
    }

    static void CaptureCassette(CassetteSave s)
    {
        var cp = Object.FindObjectOfType<CassettePlayer>();
        if (cp != null) s.insertedInPlayer = cp.HasCassette;
    }

    static void CaptureEquipment(EquipmentSave s)
    {
        var rod = Object.FindObjectOfType<FishingRodController>();
        if (rod != null)
        {
            s.fishingRodEquipped = rod.IsEquipped;
            s.fishingRodUnlocked = rod.IsUnlocked;
        }
        var guitar = Object.FindObjectOfType<GuitarController>();
        if (guitar != null)
        {
            s.guitarEquipped = guitar.IsEquipped;
            s.guitarUnlocked = guitar.IsUnlocked;
        }
        var bottle = Object.FindObjectOfType<WaterBottleController>();
        if (bottle != null)
        {
            s.waterBottleEquipped = bottle.IsEquipped;
            s.waterBottleUnlocked = bottle.IsUnlocked;
        }
        var axe = Object.FindObjectOfType<AxeController>();
        if (axe != null)
        {
            s.axeEquipped = axe.IsEquipped;
            s.axeUnlocked = axe.IsUnlocked;
        }
        var pistol = Object.FindObjectOfType<PistolController>();
        if (pistol != null)
        {
            s.pistolEquipped = pistol.IsEquipped;
            s.pistolUnlocked = pistol.IsUnlocked;
            s.pistolAmmo     = pistol.CurrentAmmo;
        }
        var pcForJetpack = Object.FindObjectOfType<PlayerController>(true);
        if (pcForJetpack != null) s.jetpackUnlocked = pcForJetpack.JetpackUnlocked;
    }

    static void CaptureBonusTutorial(BonusTutorialSave s)
    {
        var bt = BonusTutorial.Instance;
        if (bt == null) return;
        s.activeTutorial = bt.GetActiveTutorialKey();
        s.stepIndex = bt.GetStepIndex();
        s.stepsComplete = bt.GetStepsComplete();
        s.advanceArmed = bt.GetAdvanceArmed();
    }

    static void CaptureMapTutorial(MapTutorialSave s)
    {
        var t = MapTutorial.Instance;
        if (t == null) return;
        s.finished = t.GetFinished();
        s.stepIndex = t.GetStepIndex();
        s.stepsComplete = t.GetStepsComplete();
    }

    static void CaptureAIState(AIStateSave s)
    {
        if (s == null) return;
        var store = AIMemoryStore.Instance;
        if (store == null) return;

        var snap = store.Snapshot();
        s.memories            = snap.memories;
        s.standing            = snap.standing;
        s.recentUserTurns     = snap.recentUserTurns;
        s.recentAITurns       = snap.recentAITurns;
        s.dirtyForExtraction  = snap.dirtyForExtraction;
        s.totalTurns          = snap.totalTurns;

        // GameKnowledgeBase's current story phase. Old saves default to 0
        // (Phase1_Loyal) via JsonUtility — safe rollback for pre-feature saves.
        if (GameKnowledgeBase.Instance != null)
            s.storyPhase = (int)GameKnowledgeBase.Instance.CurrentPhase;
    }

    static void CaptureNameStore(NameStoreSave s)
    {
        if (s == null) return;
        s.playerName            = NameStore.PlayerName ?? "";
        s.aiName                = NameStore.AIName     ?? "";
        s.firstContactComplete  = NameStore.FirstContactComplete;
    }

    static void CaptureWorldFlags(WorldFlagsSave s)
    {
        if (LebronLight.Instance != null) s.lebronLightActive = LebronLight.Instance.IsActive;
    }

    static void CaptureStoryDirector(StoryDirectorSave s)
    {
        if (StoryDirector.Instance != null) StoryDirector.Instance.SaveTo(s);
    }

    static void CaptureEarlyGame(EarlyGameProgressSave s)
    {
        s.noteRead                = EarlyGameProgress.NoteRead;
        s.rodPickedUp             = EarlyGameProgress.RodPickedUp;
        s.firstFishCaught         = EarlyGameProgress.FirstFishCaught;
        s.oneOfEachCaught         = EarlyGameProgress.OneOfEachCaught;
        s.firstMealEaten          = EarlyGameProgress.FirstMealEaten;
        s.waterBottleDrunk        = EarlyGameProgress.WaterBottleDrunk;
        s.returnedHome            = EarlyGameProgress.ReturnedHome;
        s.tevReturnedDialogueDone = EarlyGameProgress.TevReturnedDialogueDone;
        s.cabinBuilt              = EarlyGameProgress.CabinBuilt;
        s.villageCoordsGiven      = EarlyGameProgress.VillageCoordsGiven;
        s.fishVendorVisited       = EarlyGameProgress.FishVendorVisited;
        s.goodsVendorVisited      = EarlyGameProgress.GoodsVendorVisited;
        s.orgReveal               = EarlyGameProgress.ORG_Reveal;
        s.hasEverOpenedPhone      = PlayerPhoneUI.HasEverOpened;   // §3
        s.introPlayed             = EarlyGameProgress.IntroPlayed;
    }

    static void CaptureNotes(NoteSave s)
    {
        s.readNoteIds.Clear();
        foreach (var id in NoteCollection.GetReadIds()) s.readNoteIds.Add(id);
    }

    static void CaptureBuildMenuLock(BuildMenuLockSave s)
    {
        s.isLockingActive = BuildMenuLock.IsLockingActive;
        s.unlockedNames.Clear();
        foreach (var n in BuildMenuLock.GetUnlockedNames()) s.unlockedNames.Add(n);
    }

    static void CaptureCompass(CompassSave s)
    {
        s.waypoints.Clear();
        if (CompassHUD.Instance == null) return;
        var entries = CompassHUD.Instance.GetSaveState();
        if (entries != null) s.waypoints.AddRange(entries);
    }

    static void CaptureEnemies(SaveData data)
    {
        data.enemies.Clear();
        foreach (var ec in EnemyController.ActiveEnemies)
        {
            if (ec == null || ec.IsDying) continue;
            var rb = ec.GetComponent<Rigidbody>();
            if (rb == null) continue;
            // Enemies are kinematic during life (see EnemyController.Awake), so
            // saving Vector3.zero for velocity is faithful — the body-relative
            // helper only stores the orbital reference frame anyway, which we
            // capture against the nearest planet.
            data.enemies.Add(new EnemySave
            {
                kind = ec.Kind == EnemyKind.Elite ? "elite" : "regular",
                xform = CaptureBodyRelative(rb.position, rb.rotation, Vector3.zero),
                health = ec.CurrentHealth,
            });
        }

        if (EnemySpawner.Instance != null)
        {
            data.enemySpawnTimer = EnemySpawner.Instance.TimerForSave;
            data.enemyRegularsSinceElite = EnemySpawner.Instance.RegularsSinceEliteForSave;
        }
    }

    static void CaptureSpaceDust(SaveData data)
    {
        var s = data.spaceDust;
        s.playerDust = SpaceDustInventory.Instance != null ? SpaceDustInventory.Instance.Count : 0;
        s.hasFilter  = SpaceDustInventory.Instance != null && SpaceDustInventory.Instance.HasFilter;
        s.nets.Clear();
        // Clear legacy fields so we don't double-write old + new format.
        s.netShipNumbers.Clear();
        s.netBuffers.Clear();
        s.sceneShipBuffer = 0;
        // Group nets by owning ship so we can emit a stable netIndex (the
        // sibling position within each ship's GetComponentsInChildren<SpaceNet>).
        // Two nets on the same ship would otherwise collide on a shipNumber-only key.
        var seenShips = new System.Collections.Generic.HashSet<Ship>();
        var allNets = Object.FindObjectsOfType<SpaceNet>(true);
        for (int i = 0; i < allNets.Length; i++)
        {
            var ship = allNets[i] != null ? allNets[i].OwningShip : null;
            if (ship == null) continue;
            if (!seenShips.Add(ship)) continue;
            var shipNets = ship.GetComponentsInChildren<SpaceNet>(true);
            var bought = ship.GetComponent<BoughtShip>();
            int shipKey = bought != null ? bought.shipNumber : 0;
            for (int j = 0; j < shipNets.Length; j++)
            {
                s.nets.Add(new SpaceNetSave
                {
                    shipNumber = shipKey,
                    netIndex   = j,
                    buffer     = shipNets[j].BufferedDust,
                    attached   = shipNets[j].gameObject.activeSelf,
                });
            }
        }
    }

    static void ApplySpaceDust(SaveData data)
    {
        if (data == null || data.spaceDust == null) return;
        var s = data.spaceDust;
        if (SpaceDustInventory.Instance == null)
        {
            var go = new GameObject("SpaceDustInventory");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<SpaceDustInventory>();
        }
        SpaceDustInventory.Instance.SetCount(s.playerDust);
        SpaceDustInventory.Instance.SetFilterUnlocked(s.hasFilter);

        // New format keys by (shipNumber, netIndex) so two nets per ship
        // round-trip independently. Old saves fall back to the legacy
        // parallel-arrays + sceneShipBuffer schema (lossy for multi-net ships
        // — known limitation of pre-fix saves).
        bool useNewFormat = s.nets != null && s.nets.Count > 0;

        var seenShips = new System.Collections.Generic.HashSet<Ship>();
        var allNets = Object.FindObjectsOfType<SpaceNet>(true);
        for (int i = 0; i < allNets.Length; i++)
        {
            var ship = allNets[i] != null ? allNets[i].OwningShip : null;
            if (ship == null) continue;
            if (!seenShips.Add(ship)) continue;
            var shipNets = ship.GetComponentsInChildren<SpaceNet>(true);
            var bought = ship.GetComponent<BoughtShip>();
            int shipKey = bought != null ? bought.shipNumber : 0;
            for (int j = 0; j < shipNets.Length; j++)
            {
                int buf = 0;
                bool attached = shipNets[j].gameObject.activeSelf; // fallback: leave as-is
                if (useNewFormat)
                {
                    for (int k = 0; k < s.nets.Count; k++)
                    {
                        var e = s.nets[k];
                        if (e != null && e.shipNumber == shipKey && e.netIndex == j)
                        {
                            buf = e.buffer;
                            attached = e.attached;
                            break;
                        }
                    }
                }
                else if (bought != null)
                {
                    int idx = s.netShipNumbers.IndexOf(shipKey);
                    if (idx >= 0 && idx < s.netBuffers.Count) buf = s.netBuffers[idx];
                }
                else
                {
                    buf = s.sceneShipBuffer;
                }
                if (shipNets[j].gameObject.activeSelf != attached)
                    shipNets[j].gameObject.SetActive(attached);
                shipNets[j].SetRawBuffer(buf);
            }
        }
    }

    // ───────────────────────── Apply ─────────────────────────

    public static void Apply(SaveData data)
    {
        if (data == null) return;

        // Apply order matters — this is the real call sequence:
        //   1. Celestial bodies — restore orbital state first; everything
        //      body-relative below resolves world position from these.
        //   2. Tutorial — suppresses the auto-start-on-collision.
        //   3. NPCs — marks dialogue completion so prompts don't reappear.
        //   4. EarlyGame — static-singleton progress flags; must run before
        //      any later apply that READS these flags.
        //   5. Notes / BuildMenuLock / Compass — UI/progress singletons.
        //   6. Resources / Wallet / Wood / FishInventory / Equipment /
        //      WorldFlags — singleton state.
        //   7. ShipDamage — synchronous prefab swap; may replace the ship.
        //   8. ShipTransform — after the damage swap (positions the new rb).
        //   9. ExtraShips — spawned before the player apply so a saved
        //      isPiloted=true extra exists when player placement reads it.
        //  10. PlayerTransform — after ship damage so the player isn't
        //      positioned relative to a ship about to be destroyed.
        //  11. Buildings / LooseParts — re-spawned body-relative content.
        //  12. Enemies — independent state; bodies already restored.
        //  13. HeldItem — last among gameplay; needs PlayerPickup intact.
        //  14. AI state — singleton restore, independent of world.
        //  15. Cassette / Flashlight / BonusTutorial / MapTutorial /
        //      AlienKills — final touch-ups.
        ApplyCelestialBodies(data.celestialBodies);

        ApplyTutorial(data.tutorial);
        ApplyNPCs(data.npcs);
        // Early-game progression: pure static-singleton state, must run before
        // any apply method that READS these flags (e.g. ApplyEquipment may
        // eventually check progress flags to decide whether to unlock items).
        ApplyEarlyGame(data.earlyGame);
        ApplyNotes(data.notes);
        ApplyBuildMenuLock(data.buildMenuLock);
        ApplyCompass(data.compass);
        ApplyResources(data.resources);
        ApplyOxygen(data.oxygen);
        ApplyWallet(data.wallet);
        ApplyWood(data.wood);
        ApplyCrystals(data.crystal);
        ApplyFishInventory(data.fishInventory);
        ApplyEquipment(data.equipment);
        ApplyWorldFlags(data.worldFlags);
        ApplyStoryDirector(data.storyDirector);

        ApplyShipDamage(data.ship);
        ApplyShipTransform(data.ship);
        // Spawn purchased extras before the player apply, so a saved
        // isPiloted=true extra exists when player placement reads pilot state.
        ApplyExtraShips(data.extraShips);
        // Space dust runs after extra ships so BoughtShip.shipNumber lookups
        // resolve against the freshly-spawned fleet.
        ApplySpaceDust(data);
        // Hotbar runs AFTER all legacy-total applies (wood, crystal, dust) because
        // those route through SetResourceTotal which redistributes stacks. The
        // saved slot layout is the source of truth; this overwrites any
        // redistribution the legacy paths just did and restores exact slot
        // positions. Falls back to the legacy totals if no layout was saved.
        ApplyHotbar(data);
        ApplyStorages(data.storages);
        ApplyPlayerTransform(data.player);

        ApplyBuildings(data.buildings);
        ApplyLooseParts(data.looseParts);

        // Enemies run after buildings/loose-parts (independent state) and
        // before held-item (which depends on the player only). ApplyCelestial-
        // Bodies has already run so body-relative positions resolve correctly.
        ApplyEnemies(data);

        ApplyHeldItem(data.player.heldKind);
        ApplyAIState(data.aiState);
        ApplyNameStore(data.nameStore);
        ApplyCassette(data.cassette);
        ApplyFlashlight(data.player);
        ApplyBonusTutorial(data.bonusTutorial);
        ApplyMapTutorial(data.mapTutorial);

        ApplyAlienKills(data.alienKills);
        ApplyTreesMined(data.treesMined);
        ApplyMushroomsConsumed(data.mushroomsConsumed);
        ApplyCrystalsMined(data.crystalsMined);
    }

    static void ApplyTreesMined(WorldPropConsumedSave s)
    {
        if (s == null) return;
        var spawner = Object.FindObjectOfType<TreeSpawner>();
        if (spawner != null) spawner.RestoreMinedCells(s.cells, s.bodyNames);
    }

    static void ApplyMushroomsConsumed(WorldPropConsumedSave s)
    {
        if (s == null) return;
        var spawner = Object.FindObjectOfType<MushroomSpawner>();
        if (spawner != null) spawner.RestoreConsumedCells(s.cells, s.bodyNames);
    }

    static void ApplyCrystalsMined(WorldPropConsumedSave s)
    {
        if (s == null) return;
        var spawner = Object.FindObjectOfType<CrystalSpawner>();
        if (spawner != null) spawner.RestoreConsumedCells(s.cells, s.bodyNames);
    }

    // Restore which alien NPCs (streamed + pre-placed) were killed in the
    // saved run. Streamed: re-seed the spawner's killedCells set so the
    // streaming loop skips those cells. Pre-placed: find any matching
    // hand-placed alien GameObjects in the scene and destroy them silently
    // (no ragdoll, no death banner — those fired during the original kill).
    static void ApplyAlienKills(AlienKillsSave s)
    {
        if (s == null) return;
        var spawner = Object.FindObjectOfType<AlienNPCSpawner>();
        if (spawner != null)
        {
            spawner.RestoreKilledCells(s.killedSpawnedCells, s.killedSpawnedCellBodies);
            spawner.RestoreKilledPrePlacedNames(s.killedPrePlacedNames);
            // The 1-frame defer before Apply may have let the spawner tick
            // once and produce live aliens in cells we just marked dead.
            // Wipe and let it repopulate against the restored killedCells.
            spawner.ClearAllActiveAliens();
        }

        // Destroy pre-placed scene aliens whose names were recorded as killed.
        // Filter by isStoryImpactful so a spawner-cloned prefab named the same
        // as a pre-placed alien isn't accidentally targeted.
        if (s.killedPrePlacedNames != null && s.killedPrePlacedNames.Count > 0)
        {
            // Single pass: build a name set, then destroy every story-impactful
            // pre-placed alien whose name is in it. The old nested loop was
            // O(N*M) and its inner `break` left duplicate-named aliens alive.
            var killedNames = new System.Collections.Generic.HashSet<string>(s.killedPrePlacedNames);
            killedNames.Remove("");
            var damageables = Object.FindObjectsOfType<AlienNPCDamageable>(true);
            for (int j = 0; j < damageables.Length; j++)
            {
                var d = damageables[j];
                if (d == null) continue;
                if (!d.isStoryImpactful) continue;
                if (!killedNames.Contains(d.gameObject.name)) continue;
                Object.Destroy(d.gameObject);
            }
        }
    }

    static void ApplyTutorial(TutorialSave s)
    {
        if (TutorialManager.Instance != null)
            TutorialManager.Instance.ApplyState(s.started, s.finished, s.currentStepIndex, s.stepsComplete, s.currentStepTypeName);

        var unlocked = new List<TutorialAbility>();
        foreach (var name in s.unlockedAbilities)
        {
            if (System.Enum.TryParse<TutorialAbility>(name, out var a)) unlocked.Add(a);
        }
        TutorialGate.ApplyState(s.gateEnabled, unlocked);
    }

    static void ApplyNPCs(List<NPCSave> list)
    {
        if (list == null) return;
        var dialogues = Object.FindObjectsOfType<NPCDialogue>(true);
        var guitarNpcs = Object.FindObjectsOfType<GuitarShopNPC>(true);
        var bonfires = Object.FindObjectsOfType<BonfireNPCDialogue>(true);
        foreach (var save in list)
        {
            if (save.npcId.StartsWith("NPCDialogue:"))
            {
                var name = save.npcId.Substring("NPCDialogue:".Length);
                foreach (var d in dialogues)
                    if (d.gameObject.name == name) d.ApplyCompleted(save.completed);
            }
            else if (save.npcId.StartsWith("GuitarShopNPC:"))
            {
                var name = save.npcId.Substring("GuitarShopNPC:".Length);
                foreach (var g in guitarNpcs)
                    if (g.gameObject.name == name) g.ApplyStateFromString(save.stateString);
            }
            else if (save.npcId.StartsWith("BonfireNPCDialogue:"))
            {
                var name = save.npcId.Substring("BonfireNPCDialogue:".Length);
                foreach (var b in bonfires)
                    if (b.gameObject.name == name) b.ApplyFirstTimeDone(save.completed);
            }
        }
    }

    static void ApplyResources(ResourcesSave s)
    {
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.ApplyState(s.hunger, s.thirst, s.health);
            ResourceManager.Instance.SetTotalDeaths(s.totalDeaths);
        }
    }

    static void ApplyOxygen(O2Save s)
    {
        if (OxygenManager.Instance != null)
            OxygenManager.Instance.ApplyState(s.suitO2, s.hullO2, s.cyclopsCheckpointReached);
    }

    static void ApplyWallet(WalletSave s)
    {
        if (PlayerWallet.Instance != null) PlayerWallet.Instance.SetMoney(s.money);
    }

    static void ApplyWood(WoodSave s)
    {
        if (WoodInventory.Instance != null) WoodInventory.Instance.SetWood(s.wood);
    }

    static void ApplyCrystals(CrystalSave s)
    {
        if (s == null) return;
        if (CrystalInventory.Instance != null) CrystalInventory.Instance.SetCount(s.count);
    }

    static void ApplyHotbar(SaveData data)
    {
        if (data == null || Hotbar.Instance == null) return;

        // Preferred path: new saves carry the full slot layout.
        bool hasLayout = data.hotbar != null && data.hotbar.slots != null && data.hotbar.slots.Count > 0;
        if (hasLayout)
        {
            Hotbar.Instance.ApplySlotsFromSave(data.hotbar.slots);
            return;
        }

        // Legacy fallback: redistribute totals from pre-refactor saves into fresh stacks.
        if (data.wood != null && data.wood.wood > 0)
            Hotbar.Instance.SetResourceTotal(Hotbar.ItemId.Wood, data.wood.wood);
        if (data.crystal != null && data.crystal.count > 0)
            Hotbar.Instance.SetResourceTotal(Hotbar.ItemId.Crystal, data.crystal.count);
        if (data.spaceDust != null && data.spaceDust.playerDust > 0)
            Hotbar.Instance.SetResourceTotal(Hotbar.ItemId.SpaceDust, data.spaceDust.playerDust);
    }

    static void ApplyStorages(System.Collections.Generic.List<StorageSave> list)
    {
        if (list == null) return;
        var live = StorageRegistry.All;
        for (int i = 0; i < list.Count; i++)
        {
            var saved = list[i];
            if (saved == null || string.IsNullOrEmpty(saved.boxId)) continue;

            LootBox match = null;
            for (int j = 0; j < live.Count; j++)
            {
                if (live[j] != null && live[j].BoxId == saved.boxId) { match = live[j]; break; }
            }
            if (match == null)
            {
                UnityEngine.Debug.LogWarning($"[Storage] no live LootBox for saved boxId '{saved.boxId}' — dropping");
                continue;
            }

            var slots = match.Slots;
            for (int k = 0; k < slots.Length; k++) slots[k] = default;
            int max = UnityEngine.Mathf.Min(saved.slots.Count, slots.Length);
            for (int k = 0; k < max; k++)
            {
                var e = saved.slots[k];
                if (e == null) continue;
                if (!System.Enum.TryParse<Hotbar.ItemId>(e.itemId, out var id)) continue;
                int count = UnityEngine.Mathf.Clamp(e.count, 0, Hotbar.StackMax(id));
                if (id == Hotbar.ItemId.None || count <= 0) { slots[k] = default; continue; }

                FishEntry fish = null;
                Hotbar.Slot[] bag = null;
                if (id == Hotbar.ItemId.Fish)
                {
                    if (e.fishData == null) { slots[k] = default; continue; }
                    fish = new FishEntry(e.fishData.fishType, e.fishData.weightLbs);
                    fish.fishColor = e.fishData.fishColor;
                }
                else if (id == Hotbar.ItemId.FishBag)
                {
                    bag = DeserializeBagContentsPublic(e.bagContents);
                }
                slots[k] = new Hotbar.Slot { id = id, count = count, fishData = fish, bagContents = bag };
            }
        }
    }

    static void ApplyFishInventory(FishInventorySave s)
    {
        if (FishInventory.Instance == null) return;
        var list = new List<FishEntry>();
        foreach (var e in s.fish)
        {
            var fe = new FishEntry(e.fishType, e.weightLbs);
            fe.fishColor = e.fishColor;
            list.Add(fe);
        }
        FishInventory.Instance.ReplaceAll(list);

        // Phase 2: one-shot migration of existing FishInventory entries
        // into hotbar / storage. Old saves load with migratedToHotbar=false
        // (JsonUtility default), which triggers the push. New saves (post
        // Phase 2) have it true after the first save so we skip the work.
        if (!s.migratedToHotbar)
        {
            MigrateFishInventoryToHotbar(list);
            s.migratedToHotbar = true;
        }
    }

    // Push existing FishInventory entries into the hotbar; spill to storage
    // when hotbar is full; destroy (still logged in dex) when storage is
    // also full. Called once per save's lifetime, gated by
    // FishInventorySave.migratedToHotbar.
    static void MigrateFishInventoryToHotbar(List<FishEntry> entries)
    {
        if (Hotbar.Instance == null) return;
        int placedHotbar = 0, placedStorage = 0, destroyed = 0;
        foreach (var entry in entries)
        {
            if (Hotbar.Instance.TryAddFish(entry)) { placedHotbar++; continue; }
            if (TrySpillToStorage(entry))         { placedStorage++; continue; }
            destroyed++;
        }
        UnityEngine.Debug.Log($"[FishMigration] hotbar={placedHotbar} storage={placedStorage} destroyed={destroyed}");
    }

    static bool TrySpillToStorage(FishEntry entry)
    {
        var live = StorageRegistry.All;
        for (int i = 0; i < live.Count; i++)
        {
            var box = live[i];
            if (box == null) continue;
            var slots = box.Slots;
            for (int j = 0; j < slots.Length; j++)
            {
                if (slots[j].id != Hotbar.ItemId.None) continue;
                slots[j] = new Hotbar.Slot { id = Hotbar.ItemId.Fish, count = 1, fishData = entry };
                return true;
            }
        }
        return false;
    }

    // Public wrappers used by the level-portal system (PortalManager) to carry the
    // player's jetpack + equippable state across scene transitions. The player
    // GameObject is recreated per scene, so this player-bound state must be captured
    // before the load and re-applied to the freshly-spawned player afterwards.
    public static EquipmentSave CaptureEquipmentState()
    {
        var s = new EquipmentSave();
        CaptureEquipment(s);
        return s;
    }

    public static void ApplyEquipmentState(EquipmentSave s)
    {
        if (s != null) ApplyEquipment(s);
    }

    static void ApplyEquipment(EquipmentSave s)
    {
        var rod = Object.FindObjectOfType<FishingRodController>();
        if (rod != null)
        {
            if (s.fishingRodUnlocked) rod.Unlock();
            if (s.fishingRodEquipped && !rod.IsEquipped) rod.ForceEquipRod();
            else if (!s.fishingRodEquipped && rod.IsEquipped) rod.ForceUnequipRod();
        }
        var guitar = Object.FindObjectOfType<GuitarController>();
        if (guitar != null)
        {
            guitar.SetUnlocked(s.guitarUnlocked);
            if (s.guitarEquipped && !guitar.IsEquipped) guitar.ForceEquipGuitar();
            else if (!s.guitarEquipped && guitar.IsEquipped) guitar.ForceUnequipGuitar();
        }
        var bottle = Object.FindObjectOfType<WaterBottleController>();
        if (bottle != null)
        {
            if (s.waterBottleUnlocked) bottle.Unlock();
            if (s.waterBottleEquipped && !bottle.IsEquipped) bottle.ForceEquipBottle();
            else if (!s.waterBottleEquipped && bottle.IsEquipped) bottle.ForceUnequipBottle();
        }
        var axe = Object.FindObjectOfType<AxeController>();
        if (axe != null)
        {
            if (s.axeUnlocked) axe.Unlock();
            if (s.axeEquipped && !axe.IsEquipped) axe.ForceEquipAxe();
            else if (!s.axeEquipped && axe.IsEquipped) axe.ForceUnequipAxe();
        }
        var pistol = Object.FindObjectOfType<PistolController>();
        if (pistol != null)
        {
            if (s.pistolUnlocked) pistol.Unlock();
            if (s.pistolEquipped && !pistol.IsEquipped) pistol.ForceEquipPistol();
            else if (!s.pistolEquipped && pistol.IsEquipped) pistol.ForceUnequipPistol();
            pistol.SetAmmo(s.pistolAmmo);
        }
        var pcApplyJetpack = Object.FindObjectOfType<PlayerController>(true);
        if (pcApplyJetpack != null && s.jetpackUnlocked) pcApplyJetpack.UnlockJetpack();
    }

    static void ApplyBonusTutorial(BonusTutorialSave s)
    {
        if (BonusTutorial.Instance == null) return;
        BonusTutorial.Instance.ApplySaveState(s.activeTutorial, s.stepIndex, s.stepsComplete, s.advanceArmed);
    }

    static void ApplyMapTutorial(MapTutorialSave s)
    {
        if (MapTutorial.Instance == null || s == null) return;
        MapTutorial.Instance.ApplySaveState(s.finished, s.stepIndex, s.stepsComplete);
    }

    static void ApplyAIState(AIStateSave s)
    {
        if (s == null) return;
        var store = AIMemoryStore.Instance;
        if (store != null) store.Restore(s);

        if (GameKnowledgeBase.Instance != null)
            GameKnowledgeBase.Instance.RestoreStoryPhase((StoryPhase)s.storyPhase);

        // LLMService is DontDestroyOnLoad — its _agent retains the previous
        // session's in-memory chat history even after AIMemoryStore is restored
        // from disk. Mark dirty so the next chat re-seeds _agent.chat from the
        // freshly-loaded turn buffer.
        if (LLMService.Instance != null)
            LLMService.Instance.MarkHistoryDirty();
    }

    static void ApplyNameStore(NameStoreSave s)
    {
        if (s == null) return;
        NameStore.PlayerName            = s.playerName ?? "";
        NameStore.AIName                = s.aiName     ?? "";
        NameStore.FirstContactComplete  = s.firstContactComplete;
    }

    static void ApplyWorldFlags(WorldFlagsSave s)
    {
        if (LebronLight.Instance != null && LebronLight.Instance.IsActive != s.lebronLightActive)
            LebronLight.Instance.SetActive(s.lebronLightActive);
    }

    static void ApplyStoryDirector(StoryDirectorSave s)
    {
        if (StoryDirector.Instance != null) StoryDirector.Instance.LoadFrom(s);
    }

    static void ApplyEarlyGame(EarlyGameProgressSave s)
    {
        if (s == null) return;
        EarlyGameProgress.NoteRead                = s.noteRead;
        EarlyGameProgress.RodPickedUp             = s.rodPickedUp;
        EarlyGameProgress.FirstFishCaught         = s.firstFishCaught;
        EarlyGameProgress.OneOfEachCaught         = s.oneOfEachCaught;
        EarlyGameProgress.FirstMealEaten          = s.firstMealEaten;
        EarlyGameProgress.WaterBottleDrunk        = s.waterBottleDrunk;
        EarlyGameProgress.ReturnedHome            = s.returnedHome;
        EarlyGameProgress.TevReturnedDialogueDone = s.tevReturnedDialogueDone;
        EarlyGameProgress.CabinBuilt              = s.cabinBuilt;
        EarlyGameProgress.VillageCoordsGiven      = s.villageCoordsGiven;
        EarlyGameProgress.FishVendorVisited       = s.fishVendorVisited;
        EarlyGameProgress.GoodsVendorVisited      = s.goodsVendorVisited;
        EarlyGameProgress.ORG_Reveal              = s.orgReveal;
        PlayerPhoneUI.HasEverOpened               = s.hasEverOpenedPhone;   // §3
        EarlyGameProgress.IntroPlayed             = s.introPlayed;
    }

    static void ApplyNotes(NoteSave s)
    {
        if (s == null) return;
        NoteCollection.ApplySaveState(s.readNoteIds);
    }

    static void ApplyBuildMenuLock(BuildMenuLockSave s)
    {
        if (s == null) return;
        BuildMenuLock.ApplySaveState(s.isLockingActive, s.unlockedNames);
    }

    static void ApplyCompass(CompassSave s)
    {
        if (s == null || CompassHUD.Instance == null) return;
        CompassHUD.Instance.ApplySaveState(s.waypoints);
    }

    static void ApplyShipDamage(ShipSave s)
    {
        // ShipDamageManager removed — the prefab-swap path was dead code. Only
        // the per-part attachment restoration still matters; ThrusterDetachOnImpact
        // handles that directly without needing a state-machine in between.
        var ship = FindMainShip();
        var detach = ship != null ? ship.GetComponent<ThrusterDetachOnImpact>() : null;
        if (detach != null)
            detach.ApplyAttachment(s.leftAttached, s.rightAttached, s.dishAttached, s.solarAttached);
    }

    static void ApplyShipTransform(ShipSave s)
    {
        var ship = FindMainShip();
        if (ship == null) return;

        // Restore piloting state. GameSetUp may have auto-piloted on Start
        // (StartCondition.InShip), which leaves the player stowed in the seat
        // and the camera parented to camViewPoint — undo that if the saved
        // state was not piloting.
        if (s.isPiloted && !ship.IsPiloted) ship.PilotShip();
        else if (!s.isPiloted && ship.IsPiloted) ship.ForceExitPilot();

        var rb = ship.Rigidbody;
        if (rb != null)
        {
            ApplyBodyRelative(rb, s.xform);
            ship.transform.position = rb.position;
            ship.transform.rotation = rb.rotation;
        }
        // Realign Ship's internal targetRot/smoothedRot to the saved rotation,
        // otherwise FixedUpdate snaps the rb back to the Awake-cached rotation.
        ship.SyncRotationToTransform();
        ship.SetHatchOpen(s.hatchOpen);
        ship.canFly = s.canFly;
        ship.MarkIntroComplete();
        if (ship.headlight != null && s.headlightIntensity > 0f)
            ship.headlight.intensity = s.headlightIntensity;
    }

    static void ApplyPlayerTransform(PlayerSave s)
    {
        // Include inactive: if loading into pilot mode, the player gameObject
        // is currently inactive (Ship.PilotShip disabled it).
        var player = Object.FindObjectOfType<PlayerController>(true);
        if (player == null) return;
        if (player.Rigidbody != null)
        {
            ApplyBodyRelative(player.Rigidbody, s.xform);
            player.transform.position = player.Rigidbody.position;
            player.transform.rotation = player.Rigidbody.rotation;
        }
        player.ApplyFuel(s.jetpackFuel, s.downThrustFuel, s.dirThrustFuel);
    }

    // Destroy any current extras, re-instantiate from save. Must run AFTER
    // ApplyShipTransform (main ship state settled) and BEFORE ApplyPlayerTransform
    // so a saved isPiloted=true extra exists when the player is being placed.
    static void ApplyExtraShips(List<ExtraShipSave> list)
    {
        // Tear down any extras currently in the scene (a fresh load may have
        // none; a save-over-save round-trip may have ones from the prior load).
        var em = Object.FindObjectOfType<EndlessManager>();
        var existing = Object.FindObjectsOfType<BoughtShip>(true);
        if (existing != null)
        {
            foreach (var m in existing)
            {
                if (m == null) continue;
                if (em != null) em.UnregisterPhysicsObject(m.transform);
                Object.Destroy(m.gameObject);
            }
        }
        if (list == null || list.Count == 0) return;

        foreach (var entry in list)
        {
            if (entry == null) continue;
            if (!System.Enum.TryParse<ShopItemKind>(entry.tier, out var tier))
            {
                Debug.LogWarning($"[SaveCollector] ApplyExtraShips: skipping ship with unknown tier '{entry.tier}'.");
                continue;
            }
            // Resolve body-relative xform → world pose/velocity by faking an
            // rb apply against a throwaway transform. The actual ship is then
            // spawned at that world pose.
            ResolveBodyRelativeWorldPose(entry.xform, out var worldPos, out var worldRot, out var worldVel);
            var go = ShipMarketNPC.RespawnFromSave(
                tier, worldPos, worldRot, worldVel,
                entry.leftAttached, entry.rightAttached, entry.dishAttached, entry.solarAttached);
            if (go == null) continue;

            // Restore the saved ship number so "Ship 1" stays "Ship 1"
            // even after a save → load cycle (RespawnFromSave's marker
            // initially gets a fresh max+1 number, but the save's number
            // is the authoritative one).
            var marker = go.GetComponent<BoughtShip>();
            if (marker != null && entry.shipNumber > 0) marker.shipNumber = entry.shipNumber;

            var ship = go.GetComponent<Ship>();
            if (ship != null)
            {
                ship.SyncRotationToTransform();
                ship.SetHatchOpen(entry.hatchOpen);
                ship.canFly = entry.canFly;
                if (ship.headlight != null && entry.headlightIntensity > 0f)
                    ship.headlight.intensity = entry.headlightIntensity;
                if (entry.power >= 0f) ship.SetPower(entry.power);
                if (entry.fuel  >= 0f) ship.SetFuel (entry.fuel);
                if (entry.isPiloted) ship.PilotShip();
            }
        }
    }

    static void ResolveBodyRelativeWorldPose(BodyRelativeTransform x, out Vector3 worldPos, out Quaternion worldRot, out Vector3 worldVel)
    {
        worldPos = x.localPos;
        worldRot = x.localRot;
        worldVel = x.relVelocity;
        if (string.IsNullOrEmpty(x.bodyName)) return;
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return;
        foreach (var b in bodies)
        {
            if (b == null) continue;
            if (b.bodyName != x.bodyName) continue;
            worldPos = b.Position + b.transform.rotation * x.localPos;
            worldRot = b.transform.rotation * x.localRot;
            worldVel = b.velocity + b.transform.rotation * x.relVelocity;
            return;
        }
    }

    static void ApplyBuildings(List<PlacedBuildingSave> list)
    {
        // Destroy existing placed buildings to avoid duplicates.
        var bodies = NBodySimulation.Bodies;
        if (bodies != null)
        {
            foreach (var body in bodies)
            {
                if (body == null) continue;
                for (int i = body.transform.childCount - 1; i >= 0; i--)
                {
                    var child = body.transform.GetChild(i);
                    if (child.name.EndsWith("_Placed")) Object.Destroy(child.gameObject);
                }
            }
        }

        if (list == null || list.Count == 0) return;

        var menu = Object.FindObjectOfType<BuildMenuUI>();
        if (menu == null || menu.buildables == null) return;

        foreach (var save in list)
        {
            BuildableEntry entry = null;
            foreach (var be in menu.buildables)
            {
                if (be != null && be.prefab != null && be.prefab.name == save.prefabKey)
                {
                    entry = be;
                    break;
                }
            }
            if (entry == null)
            {
                Debug.LogWarning($"[SaveCollector] ApplyBuildings: skipping building — no buildable matches prefab key '{save.prefabKey}'.");
                continue;
            }

            var body = FindBodyByName(save.parentBodyName);
            if (body == null)
            {
                Debug.LogWarning($"[SaveCollector] ApplyBuildings: skipping building '{save.prefabKey}' — parent body '{save.parentBodyName}' not found.");
                continue;
            }

            var go = Object.Instantiate(entry.prefab);
            go.name = entry.prefab.name + "_Placed";
            go.transform.SetParent(body.transform, worldPositionStays: false);
            go.transform.localPosition = save.localPos;
            go.transform.localRotation = save.localRot;

            if (entry.addBonfireInteractionOnPlace)
            {
                var bf = go.GetComponent<BonfireInteraction>() ?? go.AddComponent<BonfireInteraction>();
                if (BonfireUIRegistry.CookPanel != null)
                {
                    bf.cookPanel  = BonfireUIRegistry.CookPanel;
                    bf.promptText = BonfireUIRegistry.PromptText;
                }
                else
                {
                    var template = FindAnotherBonfire(bf);
                    if (template != null)
                    {
                        bf.cookPanel = template.cookPanel;
                        bf.promptText = template.promptText;
                    }
                    else
                    {
                        Debug.LogWarning($"[SaveCollector] ApplyBuildings: placed bonfire '{go.name}' has no cookPanel — neither registry nor scene template available.");
                    }
                }
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var sc = go.AddComponent<SphereCollider>();
                    sc.isTrigger = true;
                    sc.radius = 2f;
                }
            }
        }
    }

    static void ApplyLooseParts(List<LoosePartSave> list)
    {
        var em = Object.FindObjectOfType<EndlessManager>();

        // Destroy any loose pickups currently in the world (they'll come back from save).
        foreach (var p in Object.FindObjectsOfType<ThrusterPickup>())
        {
            if (p == null) continue;
            if (p.GetComponentInParent<Ship>() != null) continue;        // attached/mounted
            if (p.GetComponentInParent<PlayerPickup>() != null) continue; // held by player
            if (em != null) em.UnregisterPhysicsObject(p.transform);
            Object.Destroy(p.gameObject);
        }
        // Same for free-floating SpaceNet pickups.
        foreach (var n in Object.FindObjectsOfType<SpaceNetPickup>())
        {
            if (n == null) continue;
            if (n.GetComponentInParent<Ship>() != null) continue;
            if (n.GetComponentInParent<PlayerPickup>() != null) continue;
            if (em != null) em.UnregisterPhysicsObject(n.transform);
            Object.Destroy(n.gameObject);
        }

        if (list == null || list.Count == 0) return;

        var detach = Object.FindObjectOfType<ThrusterDetachOnImpact>();
        foreach (var save in list)
        {
            var prefab = ResolvePartPrefab(save.partKind, detach);
            if (prefab == null)
            {
                Debug.LogWarning($"[SaveCollector] ApplyLooseParts: skipping unknown part kind '{save.partKind}' — prefab not found.");
                continue;
            }
            var pos = ResolveBodyRelativePosition(save.xform);
            var rot = ResolveBodyRelativeRotation(save.xform);
            var go = Object.Instantiate(prefab, pos, rot);
            var pickup = go.GetComponent<ThrusterPickup>();
            if (pickup != null && save.partKind.StartsWith("thruster"))
                pickup.thrusterType = save.partKind.Substring("thruster".Length);
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // Match runtime SpawnPickup configuration exactly so physics behave
                // the same as freshly-detached parts (no Discrete tunneling, smooth
                // visuals, gravity comes from N-body via GravityObjectSimple).
                rb.isKinematic = false;
                rb.useGravity = false;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.position = pos;
                rb.rotation = rot;
                rb.velocity = ResolveBodyRelativeVelocity(save.xform);
                rb.angularVelocity = save.angularVelocity;
            }
            if (em != null) em.RegisterPhysicsObject(go.transform);

            var marker = go.GetComponent<PickupMarker>();
            if (marker != null && PickupUIManager.Instance != null)
                PickupUIManager.Instance.RegisterPickup(marker);
        }
    }

    static void ApplyHeldItem(string heldKind)
    {
        if (string.IsNullOrEmpty(heldKind)) return;
        var pickup = Object.FindObjectOfType<PlayerPickup>();
        if (pickup == null || pickup.holdPosition == null) return;

        GameObject prefab = null;
        if (heldKind == "cassette")
        {
            var cp = Object.FindObjectOfType<CassettePlayer>();
            if (cp != null) prefab = cp.cassettePickupPrefab;
        }
        else if (heldKind.StartsWith("thruster"))
        {
            var detach = Object.FindObjectOfType<ThrusterDetachOnImpact>();
            prefab = ResolvePartPrefab(heldKind, detach);
        }
        if (prefab == null) return;

        var go = Object.Instantiate(prefab, pickup.holdPosition.position, pickup.holdPosition.rotation);
        if (heldKind.StartsWith("thruster"))
        {
            var tp = go.GetComponent<ThrusterPickup>();
            if (tp != null) tp.thrusterType = heldKind.Substring("thruster".Length);
        }

        var rb = go.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        var col = go.GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // Mirror ApplyLooseParts (line 943-947): held items are physics objects
        // parented under the player, but the moment they're dropped they detach
        // and need EndlessManager to track them across floating-origin shifts.
        // PickupMarker (if present) needs to be visible on the marker HUD
        // again after a drop.
        var em = Object.FindObjectOfType<EndlessManager>();
        if (em != null) em.RegisterPhysicsObject(go.transform);

        var marker = go.GetComponent<PickupMarker>();
        if (marker != null && PickupUIManager.Instance != null)
            PickupUIManager.Instance.RegisterPickup(marker);

        pickup.ForcePickup(go);
    }

    static void ApplyCassette(CassetteSave s)
    {
        var cp = Object.FindObjectOfType<CassettePlayer>();
        if (cp != null) cp.SetHasCassette(s.insertedInPlayer);
    }

    static void ApplyEnemies(SaveData data)
    {
        // Snapshot first — Destroy() defers to end-of-frame and OnDisable
        // mutates ActiveEnemies, so we'd be iterating a list that's being
        // emptied beneath us. Snapshot, then destroy.
        var existing = new List<EnemyController>(EnemyController.ActiveEnemies);
        for (int i = 0; i < existing.Count; i++)
            if (existing[i] != null) Object.Destroy(existing[i].gameObject);

        if (EnemySpawner.Instance == null) return;

        if (data.enemies != null)
            for (int i = 0; i < data.enemies.Count; i++)
                EnemySpawner.Instance.SpawnFromSave(data.enemies[i]);

        EnemySpawner.Instance.RestoreTimerState(data.enemySpawnTimer, data.enemyRegularsSinceElite);
    }

    static void ApplyFlashlight(PlayerSave s)
    {
        var fl = Object.FindObjectOfType<PlayerFlashlight>();
        if (fl == null || fl.flashlight == null) return;
        // Mode-aware path: trust flashlightMode if present (>0), else fall
        // back to the legacy enabled bool (pre-3-mode saves) → Full when on.
        PlayerFlashlight.Mode mode;
        if (s.flashlightMode > 0)
            mode = (PlayerFlashlight.Mode)s.flashlightMode;
        else
            mode = s.flashlightEnabled ? PlayerFlashlight.Mode.Full : PlayerFlashlight.Mode.Off;
        fl.ApplyMode(mode);
        if (s.flashlightIntensity > 0f)
        {
            fl.flashlight.intensity = s.flashlightIntensity;
            fl.ApplyBaseIntensity(s.flashlightIntensity);
        }
    }

    // ───────────────────────── Helpers ─────────────────────────

    static BodyRelativeTransform CaptureBodyRelative(Vector3 worldPos, Quaternion worldRot, Vector3 worldVel)
    {
        var x = new BodyRelativeTransform();
        var body = FindClosestBody(worldPos);
        if (body != null)
        {
            float surfaceDst = (body.Position - worldPos).magnitude - body.radius;
            if (surfaceDst <= body.radius * kBodyAttachThreshold)
            {
                x.bodyName = body.bodyName;
                var inv = Quaternion.Inverse(body.transform.rotation);
                x.localPos = inv * (worldPos - body.Position);
                x.localRot = inv * worldRot;
                x.relVelocity = inv * (worldVel - body.velocity);
                return x;
            }
        }
        x.bodyName = "";
        x.localPos = worldPos;
        x.localRot = worldRot;
        x.relVelocity = worldVel;
        return x;
    }

    static void ApplyBodyRelative(Rigidbody rb, BodyRelativeTransform x)
    {
        rb.position = ResolveBodyRelativePosition(x);
        rb.rotation = ResolveBodyRelativeRotation(x);
        rb.velocity = ResolveBodyRelativeVelocity(x);
    }

    static Vector3 ResolveBodyRelativePosition(BodyRelativeTransform x)
    {
        var body = FindBodyByName(x.bodyName);
        if (body == null) return x.localPos;
        return body.Position + (body.transform.rotation * x.localPos);
    }

    static Quaternion ResolveBodyRelativeRotation(BodyRelativeTransform x)
    {
        var body = FindBodyByName(x.bodyName);
        if (body == null) return x.localRot;
        return body.transform.rotation * x.localRot;
    }

    static Vector3 ResolveBodyRelativeVelocity(BodyRelativeTransform x)
    {
        var body = FindBodyByName(x.bodyName);
        if (body == null) return x.relVelocity;
        return body.velocity + (body.transform.rotation * x.relVelocity);
    }

    static CelestialBody FindClosestBody(Vector3 worldPos)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        CelestialBody best = null;
        float bestDst = float.MaxValue;
        foreach (var b in bodies)
        {
            if (b == null) continue;
            float d = (b.Position - worldPos).magnitude - b.radius;
            if (d < bestDst) { bestDst = d; best = b; }
        }
        return best;
    }

    static CelestialBody FindBodyByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        foreach (var b in bodies)
        {
            if (b != null && b.bodyName == name) return b;
        }
        return null;
    }

    static string ResolvePickupKind(GameObject go)
    {
        if (go == null) return "";
        var t = go.GetComponent<ThrusterPickup>();
        if (t != null) return "thruster" + t.thrusterType;
        var c = go.GetComponent<CassettePickup>();
        if (c != null) return "cassette";
        var n = go.GetComponent<SpaceNetPickup>();
        if (n != null) return n.side == SpaceNetPickup.Side.Left ? "spacenetLeft" : "spacenetRight";
        return "";
    }

    static GameObject ResolvePartPrefab(string partKind, ThrusterDetachOnImpact detach)
    {
        if (string.IsNullOrEmpty(partKind) || detach == null) return null;
        if (partKind == "thrusterLeft")   return detach.thrusterLeftPickupPrefab;
        if (partKind == "thrusterRight")  return detach.thrusterRightPickupPrefab;
        if (partKind == "thrusterDish")   return detach.dishPickupPrefab;
        if (partKind == "thrusterSolar")  return detach.solarPanelPickupPrefab;
        if (partKind == "spacenetLeft")   return detach.spaceNetLeftPickupPrefab;
        if (partKind == "spacenetRight")  return detach.spaceNetRightPickupPrefab;
        return null;
    }

    static BonfireInteraction FindAnotherBonfire(BonfireInteraction self)
    {
        var all = Object.FindObjectsOfType<BonfireInteraction>(true);
        foreach (var b in all)
        {
            if (b == self) continue;
            if (b.cookPanel != null) return b;
        }
        return null;
    }

    public static bool IsGameplayScene()
    {
        var name = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        return name != "MainMenu";
    }
}
