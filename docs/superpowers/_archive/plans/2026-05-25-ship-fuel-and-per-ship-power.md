# Ship fuel + per-ship power — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-ship fuel resource (refilled with crystals at the in-ship Reactor) and refactor today's global ship power onto each ship instance. Both required for thrust; only power gates `LebronLight`. Vendor and debug-spawned ships start at 50% / 50%. The phone AI can query either resource per ship by number.

**Architecture:** Each `Ship` instance owns its own `powerCurrent` and `fuelCurrent` (with maxes, drain rates, and read/write methods). `ResourceManager` loses ship power and keeps only player vitals. Existing consumers (`LebronLight`, `SolarPanelCharger`, HUDs, HAL, AI router, FleetTelemetry, save system) migrate one task at a time to read from the piloted/owning `Ship`. The Reactor child GameObject inside each ship prefab gains a new `ShipReactor` script that consumes hotbar crystals on F press.

**Tech Stack:** Unity 2022.3 / Assembly-CSharp (no .asmdef), TextMeshPro UGUI for HUD and TextMeshPro (world-space) for popups, `JsonUtility` for save serialization. No automated tests — verification is in-Editor per task.

**Spec:** [`docs/superpowers/specs/2026-05-25-ship-fuel-and-per-ship-power-design.md`](../specs/2026-05-25-ship-fuel-and-per-ship-power-design.md)

**Testing model:** No automated tests in this project. Each task ends with **in-editor verification** in the gameplay scene (`Assets/1.6.7.7.7.unity`) — verify Unity Console shows no compile errors after save, then run the listed in-game checks before committing. Task 15 runs the full integration sweep including a build-from-MainMenu sanity check per the CLAUDE.md MainMenu trap.

**Task ordering rationale:** Task 1 adds per-ship state AND disables `ResourceManager`'s global ship-power drain block in the same task — otherwise the global counter would keep depleting and break the per-ship system during the migration window. Consumers (Tasks 4-13) then migrate one at a time to the per-ship API. Task 3 (final `ResourceManager` strip) runs last, after every reader has moved.

---

## Task 1: Per-ship power/fuel state on `Ship.cs` + disable global ship-power drain

Add the per-ship resource model and its drain/gating logic on `Ship`. Stop `ResourceManager.Update` from draining the global ship-power counter so it can't conflict with the per-ship state during the migration window.

**Files:**
- Modify: `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs`
- Modify: `Assets/3 - Scripts/Survival/ResourceManager.cs`

- [ ] **Step 1: Add the new serialized fields and private state on `Ship.cs`**

  Open `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs`. Find the existing `[Header("Boost (mirrors player jetpack)")]` block (~line 36). Immediately ABOVE it, insert:

  ```csharp
      [Header("Power")]
      [Tooltip("Maximum ship power. Drains while piloted (flying rate) or idle (idle rate). Restored by SolarPanelCharger. Reaching 0 disables thrust AND LebronLight.")]
      public float powerMax = 50f;
      public float powerIdleDrainPerSec   = 0.25f;
      public float powerFlyingDrainPerSec = 1.5f;
      float powerCurrent;
      public float PowerPercent => powerMax > 0f ? powerCurrent / powerMax : 0f;
      public bool HasPower      => powerCurrent > 0f;
      public bool CanRunLebronLight => HasPower;

      [Header("Fuel (1 crystal = 5 fuel units; 20 fills a 100-unit tank)")]
      [Tooltip("Maximum reactor fuel. Drains ONLY while piloted. Higher rate while thrusting; higher still while boost+thrust.")]
      public float fuelMax = 100f;
      public float fuelPilotedDrainPerSec = 2.0f;
      public float fuelThrustDrainPerSec  = 3.5f;
      public float fuelBoostDrainPerSec   = 6.0f;
      float fuelCurrent;
      public float FuelPercent => fuelMax > 0f ? fuelCurrent / fuelMax : 0f;
      public bool HasFuel      => fuelCurrent > 0f;
      public bool CanThrust    => HasPower && HasFuel;

      // Set true in FixedUpdate when boost is actively engaged on any axis,
      // so Update can pick the right fuel drain rate for this tick.
      bool _isBoostingThisTick;
  ```

- [ ] **Step 2: Add public mutation methods on `Ship.cs`**

  Immediately after the block from Step 1 (still above `[Header("Boost (mirrors player jetpack)")]`), insert:

  ```csharp
      public void DrainPower(float amount)
      {
          if (amount <= 0f) return;
          powerCurrent = Mathf.Clamp(powerCurrent - amount, 0f, powerMax);
      }

      public void RestorePower(float amount)
      {
          if (amount <= 0f) return;
          powerCurrent = Mathf.Clamp(powerCurrent + amount, 0f, powerMax);
      }

      public void DrainFuel(float amount)
      {
          if (amount <= 0f) return;
          fuelCurrent = Mathf.Clamp(fuelCurrent - amount, 0f, fuelMax);
      }

      public void RestoreFuel(float amount)
      {
          if (amount <= 0f) return;
          fuelCurrent = Mathf.Clamp(fuelCurrent + amount, 0f, fuelMax);
      }

      public void SetPower(float current)
      {
          powerCurrent = Mathf.Clamp(current, 0f, powerMax);
      }

      public void SetFuel(float current)
      {
          fuelCurrent = Mathf.Clamp(current, 0f, fuelMax);
      }
  ```

- [ ] **Step 3: Initialize power/fuel to full in `Ship.Awake()`**

  Find `Ship.Awake()` (~line 176). At the bottom of the method, just before its closing brace, add:

  ```csharp
          // Each ship starts at full power and full fuel by default. Vendor /
          // debug-spawned ships get overridden to 50% by ShipMarketNPC.SpawnShipInstance.
          // Saved ships get overridden by SaveCollector.ApplyExtraShips.
          powerCurrent = powerMax;
          fuelCurrent  = fuelMax;
  ```

- [ ] **Step 4: Replace the existing power drain call in `Ship.Update()` with per-ship drain**

  In `Ship.Update()` find the existing block (~line 408):

  ```csharp
          if (shipIsPiloted && canFly && !PlayerController.isMapOpen)
          {
              HandleMovement();
              ResourceManager.Instance?.DrainShipPower(ResourceManager.Instance.shipPowerFlyingDrainRate * Time.deltaTime);
          }
  ```

  Replace with:

  ```csharp
          if (shipIsPiloted && canFly && !PlayerController.isMapOpen)
          {
              HandleMovement();
          }

          // Per-ship drain — power drains while piloted (faster) OR idle (slower).
          // Fuel drains ONLY while piloted, faster under thrust, faster still with boost.
          // Damage-disabled ships (canFly=false) don't drain either resource.
          if (canFly)
          {
              float dt = Time.deltaTime;
              if (shipIsPiloted)
              {
                  powerCurrent = Mathf.Clamp(powerCurrent - powerFlyingDrainPerSec * dt, 0f, powerMax);
                  bool thrusting = thrusterInput.sqrMagnitude > 0.01f;
                  float fuelRate = _isBoostingThisTick ? fuelBoostDrainPerSec
                                  : thrusting         ? fuelThrustDrainPerSec
                                                       : fuelPilotedDrainPerSec;
                  fuelCurrent = Mathf.Clamp(fuelCurrent - fuelRate * dt, 0f, fuelMax);
              }
              else
              {
                  powerCurrent = Mathf.Clamp(powerCurrent - powerIdleDrainPerSec * dt, 0f, powerMax);
              }
          }
  ```

- [ ] **Step 5: Gate thrust on `CanThrust` and track boost state in `FixedUpdate`**

  In `Ship.FixedUpdate()` find the thrust application block (~line 558):

  ```csharp
          if (shipIsPiloted && canFly && !PlayerController.isMapOpen)
          {
  ```

  Replace just this `if` condition with:

  ```csharp
          if (shipIsPiloted && canFly && CanThrust && !PlayerController.isMapOpen)
          {
  ```

  Then find the boost flags assignment inside this block (~line 574-576):

  ```csharp
              bool boostUp   = boostKey && thrusterInput.y > 0.01f  && _boostFuelUp   > 0f && !_boostUpDepleted;
              bool boostDown = boostKey && thrusterInput.y < -0.01f && _boostFuelDown > 0f && !_boostDownDepleted;
              bool boostDir  = boostKey && (Mathf.Abs(thrusterInput.x) > 0.01f || Mathf.Abs(thrusterInput.z) > 0.01f) && _boostFuelDir > 0f && !_boostDirDepleted;
  ```

  Immediately AFTER these three lines, add:

  ```csharp
              _isBoostingThisTick = boostUp || boostDown || boostDir;
  ```

  Then at the END of the same `if (shipIsPiloted && canFly && CanThrust ...)` block — find the closing `}` matching that `if` (which appears just before the `else { // Idle ship ...` branch around line 746). One line BEFORE that closing brace, add:

  ```csharp
              // Defensive: clear the boost flag when we exit the block via the
              // closing brace without hitting another assignment (e.g. the V/O
              // assist branches above run boost logic of their own).
              // No-op when assignment above already correct.
  ```

  Note: the `_isBoostingThisTick = false` baseline is handled by the next FixedUpdate's assignment block above. We do not need to clear it in the `else` (idle) branch since it's only READ from `Update` while `shipIsPiloted` is true, and the assignment runs before the read each piloted tick.

- [ ] **Step 6: Disable the global ship-power drain block in `ResourceManager.Update()`**

  Open `Assets/3 - Scripts/Survival/ResourceManager.cs`. Find `void Update()` (~line 81). Locate the lines:

  ```csharp
          shipPowerCurrent -= shipPowerIdleDrainRate * dt;
  ```

  and:

  ```csharp
          shipPowerCurrent = Mathf.Clamp(shipPowerCurrent, 0f, shipPowerMax);
  ```

  and the entire ship-power depletion block:

  ```csharp
          if (shipPowerCurrent <= 0f && !shipPowerDepleted)
          {
              shipPowerDepleted = true;
              if (shipRef != null) shipRef.canFly = false;
          }
          else if (shipPowerCurrent > shipPowerRestoreThreshold && shipPowerDepleted)
          {
              shipPowerDepleted = false;
              if (shipRef != null) shipRef.canFly = true;
          }
  ```

  **Delete all three** of those snippets from `Update()`. After this delete the only lines remaining in `Update()` will be hunger/thirst, the health derivation from them, and the death check. The `shipPowerCurrent`, `shipPowerDepleted`, `shipRef` FIELDS stay (zombie — removed in Task 3) so the rest of the file still compiles.

- [ ] **Step 7: Save and verify the project compiles cleanly**

  Save both files. In Unity Editor's Console, confirm no compile errors. Existing tool/equip flows should be unaffected.

- [ ] **Step 8: Editor sanity check**

  Press Play. Buy a ship from the vendor (or use debug menu's "+Ship" button). Pilot it. Hold WASD for 10 seconds. Open the AI chat (phone) and ask "what's ship 1's fuel" — for now it'll still say what `IntentRouter` says today (we wire fuel queries in Task 11). The IMPORTANT check: per-ship power and fuel are now ticking down in memory — verify no NRE in Console while piloting.

- [ ] **Step 9: Commit**

  ```bash
  git add "Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs" "Assets/3 - Scripts/Survival/ResourceManager.cs"
  git commit -m "feat(ship): per-ship power + fuel state, drain logic, gating"
  ```

---

## Task 2: 50% spawn defaults in `ShipMarketNPC.SpawnShipInstance`

Vendor purchases AND debug-menu spawns both route through this method, so a single edit covers both.

**Files:**
- Modify: `Assets/3 - Scripts/Vendor/ShipMarketNPC.cs`

- [ ] **Step 1: Set 50% on spawn**

  Open `Assets/3 - Scripts/Vendor/ShipMarketNPC.cs`. Find the existing block right after the BoughtShip marker is added (~line 371):

  ```csharp
          marker.shipNumber = NextShipNumber();
  ```

  Immediately AFTER that line, insert:

  ```csharp
          // Half tanks on a fresh spawn — vendor purchase or debug spawn alike.
          // Save-load overrides this in SaveCollector.ApplyExtraShips when a
          // saved ship is re-spawned from disk.
          var freshShip = instance.GetComponent<Ship>();
          if (freshShip != null)
          {
              freshShip.SetPower(freshShip.powerMax * 0.5f);
              freshShip.SetFuel (freshShip.fuelMax  * 0.5f);
          }
  ```

- [ ] **Step 2: Save and verify compile**

  Save. Console: no errors.

- [ ] **Step 3: Editor verification**

  Open the debug menu (backtick `) → click +Ship. Pilot it. (UI doesn't reflect fuel yet — wired in Task 8). To verify the values, attach a temporary `Debug.Log($"power={powerCurrent} fuel={fuelCurrent}")` in `Ship.Awake` AFTER your Task 1 init, or just check via inspector. Confirm `powerCurrent ≈ 25` (half of default 50) and `fuelCurrent = 50` (half of default 100). Remove any temporary logs before committing.

- [ ] **Step 4: Commit**

  ```bash
  git add "Assets/3 - Scripts/Vendor/ShipMarketNPC.cs"
  git commit -m "feat(ship): vendor and debug spawns start at 50% power and fuel"
  ```

---

## Task 3: Add `Hotbar.GetEquippedSlotId()` getter

The reactor needs to know what resource the player has selected. The previous feature added `_equippedSlot` as private state; expose a getter.

**Files:**
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs`

- [ ] **Step 1: Add the public getter**

  Open `Assets/3 - Scripts/UI/Hotbar.cs`. Find the existing `GetEquipped()` method (~line 396 — the one that returns an `ItemId` based on `_equippedSlot` and the registry fallback).

  Immediately ABOVE the `GetEquipped()` method declaration, insert:

  ```csharp
      // Returns the ItemId of the slot the player has currently selected via 1-5
      // or D-pad cycling, regardless of whether it's a tool or a resource stack.
      // ItemId.None when no slot is selected or the selected slot is empty.
      public ItemId GetEquippedSlotId()
      {
          if (_equippedSlot < 0 || _equippedSlot >= NumSlots) return ItemId.None;
          return slots[_equippedSlot].id;
      }
  ```

- [ ] **Step 2: Save and verify compile**

  Save. Console: no errors.

- [ ] **Step 3: Commit**

  ```bash
  git add "Assets/3 - Scripts/UI/Hotbar.cs"
  git commit -m "feat(hotbar): expose GetEquippedSlotId() for reactor / future consumers"
  ```

---

## Task 4: `LebronLight` reads owning ship's power

The artificial-sun singleton stops touching `ResourceManager` and drains/checks the piloted ship's `Ship.powerCurrent` instead.

**Files:**
- Modify: `Assets/3 - Scripts/Ship/LebronLight.cs`

- [ ] **Step 1: Read the file to locate the existing power drain + ignition path**

  Read `Assets/3 - Scripts/Ship/LebronLight.cs` end-to-end. Identify:
  - The `Update()` method (drains `ResourceManager.shipPower`)
  - The "toggle on" path (sets `light.enabled = true`, plays audio)
  - Any spot that checks `ResourceManager.Instance.ShipPowerPercent` to refuse activation

- [ ] **Step 2: Add an owning-ship reference + helper**

  At a stable location near the top of the class (just after the existing `public static LebronLight Instance` declaration around line 5), add:

  ```csharp
      // The ship whose power this artificial sun drains. Resolved when the
      // light is toggled on; cleared when the light is toggled off. Defaults
      // to the currently piloted ship if any.
      Ship _owningShip;

      static Ship ResolveOwningShip()
      {
          // Prefer the piloted ship; otherwise the closest ship to the light.
          var piloted = Ship.PilotedInstance;
          if (piloted != null) return piloted;
          var allShips = Object.FindObjectsOfType<Ship>(true);
          if (allShips == null || allShips.Length == 0) return null;
          if (allShips.Length == 1) return allShips[0];
          // Closest by linear distance.
          Ship best = null;
          float bestSqr = float.MaxValue;
          var instTransform = Instance != null ? Instance.transform : null;
          for (int i = 0; i < allShips.Length; i++)
          {
              if (allShips[i] == null) continue;
              float dsq = instTransform != null
                  ? (allShips[i].transform.position - instTransform.position).sqrMagnitude
                  : 0f;
              if (dsq < bestSqr) { bestSqr = dsq; best = allShips[i]; }
          }
          return best;
      }
  ```

- [ ] **Step 3: Replace power-drain in `Update()`**

  Find the line in `Update()` that drains the global power:

  ```csharp
          if (ResourceManager.Instance != null)
              ResourceManager.Instance.DrainShipPower(usageRate * Time.deltaTime);
  ```

  Replace with:

  ```csharp
          if (_owningShip == null) _owningShip = ResolveOwningShip();
          if (_owningShip != null)
              _owningShip.DrainPower(usageRate * Time.deltaTime);
          // Auto-extinguish when the owning ship has no power left. The
          // light cannot run on zero power per the spec.
          if (_owningShip != null && !_owningShip.CanRunLebronLight)
              ToggleOff();
  ```

  Note: `ToggleOff()` is the existing method that disables the light. If the actual method name is `Deactivate`, `TurnOff`, `Disable`, or similar — use whatever is defined. If you cannot locate a corresponding method, **STOP and report BLOCKED** with what you found instead.

- [ ] **Step 4: Refuse ignition when owning ship has no power**

  Find the ignition path (the method that activates the light — likely `ToggleOn`, `Activate`, `Ignite`, or similar). At the very top of that method, immediately after any early-return-if-already-active check, insert:

  ```csharp
          // Refuse to ignite if no owning ship has power.
          _owningShip = ResolveOwningShip();
          if (_owningShip == null || !_owningShip.CanRunLebronLight) return;
  ```

  If the method name differs, find the path responsible for first turning the light on and use the same pattern. If you cannot locate it, **STOP and report BLOCKED**.

- [ ] **Step 5: Clear the owning ship on toggle-off**

  In whichever method turns the light off, at the end add:

  ```csharp
          _owningShip = null;
  ```

- [ ] **Step 6: Save and verify compile**

  Save. Console: no errors.

- [ ] **Step 7: Editor verification**

  Pilot a ship. Toggle on the LebronLight (whatever the existing keybind is — look in `LebronLight.Update` for the input check). Confirm:
  - Light turns on
  - Ship's `powerCurrent` decreases at `usageRate` per second while light is on (inspect via Hierarchy)
  - When power hits 0, light auto-extinguishes
  - Re-pressing the activate key while power=0 → light refuses to ignite

- [ ] **Step 8: Commit**

  ```bash
  git add "Assets/3 - Scripts/Ship/LebronLight.cs"
  git commit -m "refactor(lebron): drain owning ship's power instead of global"
  ```

---

## Task 5: `SolarPanelCharger` restores owning ship's power

The per-ship solar panel restores its parent ship's power instead of the global counter.

**Files:**
- Modify: `Assets/3 - Scripts/Ship/SolarPanelCharger.cs`

- [ ] **Step 1: Add an owning-ship cache + resolve it in `Awake()`**

  Open `Assets/3 - Scripts/Ship/SolarPanelCharger.cs`. Find the existing `Awake()` (~line 22). Add a field above it and a resolve line inside it:

  ```csharp
      Ship _ship;

      void Awake()
      {
          damage = GetComponent<ThrusterDetachOnImpact>();
          _ship = GetComponentInParent<Ship>();
      }
  ```

  (If `Awake` already has other lines, keep them — only ADD the `_ship = ...` line at the end.)

- [ ] **Step 2: Replace the global power restore with per-ship**

  Find the last lines of `Update()`:

  ```csharp
          if (isCharging && ResourceManager.Instance != null)
              ResourceManager.Instance.RestoreShipPower(chargeRate * Time.deltaTime);
  ```

  Replace with:

  ```csharp
          if (isCharging && _ship != null)
              _ship.RestorePower(chargeRate * Time.deltaTime);
  ```

- [ ] **Step 3: Save and verify compile**

  Save. Console: no errors.

- [ ] **Step 4: Editor verification**

  Spawn a ship via debug, drain its power (Lebron drain or just pilot for a while). Park it on a planet's day side under sunlight with the solar panel attached. Verify `powerCurrent` ticks UP at `chargeRate` per second.

- [ ] **Step 5: Commit**

  ```bash
  git add "Assets/3 - Scripts/Ship/SolarPanelCharger.cs"
  git commit -m "refactor(solar): charge owning ship's power instead of global"
  ```

---

## Task 6: `ResourceHUD` reads piloted ship's power AND adds fuel row

The legacy 4-row vitals stack moves its ship-power binding to the piloted ship and gains a new fuel row immediately below it. If the scene doesn't have the fuel row wired, build it procedurally by cloning the ship-power row at runtime.

**Files:**
- Modify: `Assets/3 - Scripts/Survival/ResourceHUD.cs`

- [ ] **Step 1: Add new serialized fields for the fuel row**

  Open `Assets/3 - Scripts/Survival/ResourceHUD.cs`. Find the existing fields (~line 7-24). Immediately after `public RectTransform shipPowerBarFill;` and analogous fields, add:

  ```csharp
      [Header("Ship Fuel (optional — auto-cloned from ship-power row if null)")]
      public RectTransform shipFuelBarFill;
      public Image shipFuelBarImage;
      public TMP_Text shipFuelLabel;
      public GameObject shipFuelRow;
  ```

- [ ] **Step 2: Read the file's `Update` and `LateUpdate` to find where ship-power values are read**

  Read `Assets/3 - Scripts/Survival/ResourceHUD.cs` entirely (it's < 300 lines). Identify the line(s) that read `ResourceManager.Instance.ShipPowerPercent` and drive `shipPowerBarFill`. There will be one or two — the fill scale and the label text.

- [ ] **Step 3: Replace the global ship-power read with the piloted ship's value**

  Find this read pattern:

  ```csharp
      float shipPowerPct = rm != null ? rm.ShipPowerPercent : 0f;
  ```

  (or whatever variable name is used). Replace with:

  ```csharp
      Ship piloted = Ship.PilotedInstance;
      float shipPowerPct = piloted != null ? piloted.PowerPercent : 0f;
      float shipFuelPct  = piloted != null ? piloted.FuelPercent  : 0f;
  ```

  Then, wherever `shipPowerBarFill` is scaled and the label written, add the analogous fuel block immediately below. Example: if the existing power code is:

  ```csharp
      if (shipPowerBarFill != null)
      {
          var s = shipPowerBarFill.localScale;
          s.x = Mathf.Clamp01(shipPowerPct);
          shipPowerBarFill.localScale = s;
      }
      if (shipPowerLabel != null) shipPowerLabel.text = $"{Mathf.RoundToInt(shipPowerPct * 100f)}%";
  ```

  Add immediately AFTER:

  ```csharp
      if (shipFuelBarFill != null)
      {
          var s2 = shipFuelBarFill.localScale;
          s2.x = Mathf.Clamp01(shipFuelPct);
          shipFuelBarFill.localScale = s2;
      }
      if (shipFuelLabel != null) shipFuelLabel.text = $"{Mathf.RoundToInt(shipFuelPct * 100f)}%";
  ```

- [ ] **Step 4: Auto-clone the fuel row at startup if not assigned**

  At the bottom of the existing `Awake()` (~line 55), add:

  ```csharp
          if (shipFuelRow == null) AutoCreateFuelRow();
  ```

  Then add the new method anywhere inside the class (e.g. just above `ConfigureCanvasScaling`):

  ```csharp
      void AutoCreateFuelRow()
      {
          if (shipPowerRow == null) return;
          var srcRow = shipPowerRow;
          var clone = Instantiate(srcRow, srcRow.transform.parent);
          clone.name = "ShipFuelRow";
          // Rename child labels so we don't accidentally hit the same name lookup.
          // Resolve the cloned children for our new fields by structural match —
          // we mirror the ship-power row's hierarchy exactly so the path lookups
          // below find their counterparts.
          shipFuelRow = clone;
          // Bar fill = the child object whose RectTransform matches shipPowerBarFill's name.
          if (shipPowerBarFill != null)
          {
              var t = clone.transform.Find(shipPowerBarFill.name);
              if (t != null) shipFuelBarFill = t as RectTransform;
          }
          if (shipPowerBarImage != null)
          {
              var t = clone.transform.Find(shipPowerBarImage.name);
              if (t != null) shipFuelBarImage = t.GetComponent<Image>();
          }
          if (shipPowerLabel != null)
          {
              var t = clone.transform.Find(shipPowerLabel.name);
              if (t != null) shipFuelLabel = t.GetComponent<TMP_Text>();
          }
          // Position the clone immediately below the source row using a small Y offset.
          var srcRT = srcRow.transform as RectTransform;
          var dstRT = clone.transform as RectTransform;
          if (srcRT != null && dstRT != null)
          {
              dstRT.anchoredPosition = srcRT.anchoredPosition + new Vector2(0f, -60f);
          }
          // Recolor the new label / icon to a distinct fuel hue so the player tells them apart.
          if (shipFuelLabel != null) shipFuelLabel.text = "FUEL 0%";
          if (shipFuelBarImage != null) shipFuelBarImage.color = new Color32(0x8C, 0xE6, 0xFF, 0xFF); // crystal cyan
      }
  ```

  Note: this auto-clone uses a structural-name path lookup. If the actual scene hierarchy differs (e.g. cyclical names, deep children), the lookup may miss — the worst case is `shipFuelBarFill` stays null and the row is silent (no NRE in Step 3 because each write is null-guarded). Verify in-Editor that the cloned row shows up correctly.

- [ ] **Step 5: Hide the fuel row when not piloting**

  Find the existing block that toggles `shipPowerRow.SetActive(...)` based on `Ship.PilotedInstance` (or `_shipRowVisible`). Immediately after that toggle, mirror it for the fuel row:

  ```csharp
      if (shipFuelRow != null && shipFuelRow.activeSelf != shouldShowShipRow)
          shipFuelRow.SetActive(shouldShowShipRow);
  ```

  Use whatever boolean the existing code computes — most likely `shouldShowShipRow` or `Ship.PilotedInstance != null`. If you're unsure of the variable name, match the existing line exactly.

- [ ] **Step 6: Save and verify compile**

  Save. Console: no errors.

- [ ] **Step 7: Editor verification**

  Pilot a ship. Confirm a second bar appears below the existing ship-power bar, labeled FUEL, in cyan. Holding WASD: fuel bar drains faster than power. Exit pilot mode: both rows hide. Re-enter: both reappear.

- [ ] **Step 8: Commit**

  ```bash
  git add "Assets/3 - Scripts/Survival/ResourceHUD.cs"
  git commit -m "feat(hud): ship-fuel row alongside power (piloted-ship-driven)"
  ```

---

## Task 7: `VitalsHUD` reads piloted ship's power AND adds fuel row

The newer compact vitals card mirrors the same change.

**Files:**
- Modify: `Assets/3 - Scripts/Survival/VitalsHUD.cs`

- [ ] **Step 1: Read `VitalsHUD.cs` end-to-end** to understand its structure (procedural Build vs. serialized refs).

- [ ] **Step 2: Add the fuel row**

  If `VitalsHUD` is procedurally built (a `Build()` method instantiates rows in code), find the row-build calls for power and add a new row immediately after. The new row's drive function should read `Ship.PilotedInstance?.FuelPercent ?? 0f`.

  If `VitalsHUD` uses serialized refs, mirror Task 6 Step 1-5 with VitalsHUD's field names.

  **If after reading the file the right path is genuinely unclear**, STOP and report BLOCKED with the specific structure you found and what's confusing.

- [ ] **Step 3: Retarget the ship-power read to `Ship.PilotedInstance.PowerPercent`**

  Wherever the file reads `rm.ShipPowerPercent`, replace with:

  ```csharp
      Ship piloted = Ship.PilotedInstance;
      float shipPowerPct = piloted != null ? piloted.PowerPercent : 0f;
  ```

- [ ] **Step 4: Save and verify compile**

  Save. Console: no errors.

- [ ] **Step 5: Editor verification**

  Pilot a ship. Confirm `VitalsHUD` shows both power and fuel. Both reflect the current piloted ship's values; both hide when on foot.

- [ ] **Step 6: Commit**

  ```bash
  git add "Assets/3 - Scripts/Survival/VitalsHUD.cs"
  git commit -m "feat(vitals): per-piloted-ship power + fuel rows"
  ```

---

## Task 8: `HALCommentator` retargets low-power voice line + adds low/dry-fuel lines

The existing 25%/40% hysteresis low-power voice line is repointed to the piloted ship's power. Two new symmetric lines fire on low fuel (25%) and dry fuel (0%).

**Files:**
- Modify: `Assets/3 - Scripts/AI/HALCommentator.cs`

- [ ] **Step 1: Add two new tracking fields**

  Open `Assets/3 - Scripts/AI/HALCommentator.cs`. Find the existing `bool _shipPowerLowFired;` field (~line 49). Immediately AFTER it, insert:

  ```csharp
      bool _shipFuelLowFired;
      bool _shipFuelEmptyFired;
  ```

- [ ] **Step 2: Locate the existing low-power volunteer block**

  Find the existing block (~line 558-568):

  ```csharp
          // Ship power: single threshold at 25% with hysteresis at 40%.
          if (sp <= 25f && !_shipPowerLowFired)
          {
              int pct = Mathf.RoundToInt(sp);
              Volunteer($"Ship power at {pct}%. Solar panel exposure recommended.");
              _shipPowerLowFired = true;
          }
          else if (sp >= 40f && _shipPowerLowFired)
          {
              _shipPowerLowFired = false;
          }
  ```

- [ ] **Step 3: Replace with per-piloted-ship variant + add fuel lines**

  Replace the block from Step 2 with:

  ```csharp
          // Ship power + fuel: voice lines target the piloted ship's per-ship
          // resources. When no ship is piloted we hold the fired flags (don't
          // flicker when entering / exiting cockpits).
          Ship pi = Ship.PilotedInstance;
          if (pi != null)
          {
              int shipN = HALShipNumber(pi);
              float pwr = pi.PowerPercent * 100f;
              float ful = pi.FuelPercent  * 100f;

              if (pwr <= 25f && !_shipPowerLowFired)
              {
                  Volunteer($"Ship {shipN} power at {Mathf.RoundToInt(pwr)}%. Solar panel exposure recommended.");
                  _shipPowerLowFired = true;
              }
              else if (pwr >= 40f && _shipPowerLowFired)
              {
                  _shipPowerLowFired = false;
              }

              if (ful <= 25f && ful > 0f && !_shipFuelLowFired)
              {
                  Volunteer($"Ship {shipN} fuel at {Mathf.RoundToInt(ful)}%. Insert crystals into the reactor.");
                  _shipFuelLowFired = true;
              }
              else if (ful >= 40f && _shipFuelLowFired)
              {
                  _shipFuelLowFired = false;
              }

              if (ful <= 0f && !_shipFuelEmptyFired)
              {
                  Volunteer($"Ship {shipN} reactor is dry. Thrust disabled.");
                  _shipFuelEmptyFired = true;
              }
              else if (ful > 0f && _shipFuelEmptyFired)
              {
                  _shipFuelEmptyFired = false;
              }
          }
  ```

  Also remove the now-unused `float sp = rm.ShipPowerPercent * 100f;` line at the top of this method (~line 521). If `rm` is otherwise still used elsewhere in the method, leave that variable; if `rm` is now unused, remove its declaration too.

- [ ] **Step 4: Add the `HALShipNumber` helper at the bottom of the class**

  At the bottom of the `HALCommentator` class (just above its closing `}`), insert:

  ```csharp
      // Returns the BoughtShip.shipNumber of a given ship, or 0 if the ship
      // has no BoughtShip marker (the scene shouldn't have one, but defend).
      static int HALShipNumber(Ship ship)
      {
          if (ship == null) return 0;
          var b = ship.GetComponent<BoughtShip>();
          return b != null ? b.shipNumber : 0;
      }
  ```

- [ ] **Step 5: Save and verify compile**

  Save. Console: no errors.

- [ ] **Step 6: Editor verification**

  Pilot a ship. Drain its fuel by holding WASD until below 25%. HAL volunteers "Ship N fuel at X%. Insert crystals into the reactor." Continue draining to 0%. HAL volunteers "Ship N reactor is dry. Thrust disabled." Refuel via the reactor (Task 12 will wire that; for now use Inspector to set fuelCurrent>0). Re-drain → low line should fire again.

- [ ] **Step 7: Commit**

  ```bash
  git add "Assets/3 - Scripts/AI/HALCommentator.cs"
  git commit -m "feat(hal): per-piloted-ship low/dry voice lines for power and fuel"
  ```

---

## Task 9: `IntentRouter` adds per-ship power + fuel queries (and refuses ambiguous)

Replace today's global `wantsShipPower` intent with per-ship intents, and add a fuel intent. Ambiguous queries (no ship number) get a refusal asking for one.

**Files:**
- Modify: `Assets/3 - Scripts/AI/IntentRouter.cs`

- [ ] **Step 1: Read the file's top and find the existing routing structure**

  Open `Assets/3 - Scripts/AI/IntentRouter.cs`. Identify:
  - The existing `wantsShipPower` detection (~line 215)
  - The existing `ResolveShip` helper (~line 61)
  - Where the response strings are built (~line 228)

- [ ] **Step 2: Remove the legacy `wantsShipPower` global detection**

  Find these lines (~line 215, 226, 228):

  ```csharp
          bool wantsShipPower = msgLower.Contains("ship power") || ContainsWord(msgLower, "battery");
  ```

  and:

  ```csharp
          int sp = Mathf.RoundToInt(rm.ShipPowerPercent * 100);
  ```

  and:

  ```csharp
          if (wantsShipPower) return $"Ship power is at {sp}%.";
  ```

  **Delete all three.**

- [ ] **Step 3: Add the per-ship intents**

  Near the existing per-ship intent regexes (search for `Regex.*ship`/`ShipSpeed`/`ShipAltitude` patterns in the file), add:

  ```csharp
      static readonly System.Text.RegularExpressions.Regex ShipPowerIntent =
          new System.Text.RegularExpressions.Regex(
              @"(?:what(?:'s| is)|how much|how's|how is)\s+(?:.*?\b)?ship\s+(\d+)(?:'s)?\s+power|ship\s+(\d+)(?:'s)?\s+power|power\s+(?:in|of)\s+ship\s+(\d+)",
              System.Text.RegularExpressions.RegexOptions.IgnoreCase);

      static readonly System.Text.RegularExpressions.Regex ShipFuelIntent =
          new System.Text.RegularExpressions.Regex(
              @"(?:what(?:'s| is)|how much|how's|how is)\s+(?:.*?\b)?ship\s+(\d+)(?:'s)?\s+fuel|ship\s+(\d+)(?:'s)?\s+fuel|fuel\s+(?:in|of)\s+ship\s+(\d+)",
              System.Text.RegularExpressions.RegexOptions.IgnoreCase);

      // Ambiguous query: "ship power" / "ship fuel" / "what's the fuel" with no number.
      static readonly System.Text.RegularExpressions.Regex AmbiguousShipResourceIntent =
          new System.Text.RegularExpressions.Regex(
              @"\b(?:ship\s+power|ship\s+fuel|the\s+(?:power|fuel))\b(?!\s*\d)",
              System.Text.RegularExpressions.RegexOptions.IgnoreCase);
  ```

- [ ] **Step 4: Add handler methods near the other per-ship handlers**

  Find the existing `TryHandle*` per-ship methods (the ones that read `ResolveShip(m, out var ship, out int shipNumber)`). Below them, add two new handlers:

  ```csharp
      static string TryHandleShipPower(string msg)
      {
          var m = ShipPowerIntent.Match(msg);
          if (!m.Success) return null;
          // The shipNumber may be in any of capture groups 1/2/3 depending on
          // which alternative branch matched. Take the first non-empty.
          string num = m.Groups[1].Value;
          if (string.IsNullOrEmpty(num)) num = m.Groups[2].Value;
          if (string.IsNullOrEmpty(num)) num = m.Groups[3].Value;
          if (!int.TryParse(num, out int shipNumber)) return null;
          var ship = ResolveShipByNumber(shipNumber);
          if (ship == null) return $"Ship {shipNumber} does not exist.";
          float pct = ship.PowerPercent * 100f;
          return $"Ship {shipNumber} power is at {Mathf.RoundToInt(pct)}%.";
      }

      static string TryHandleShipFuel(string msg)
      {
          var m = ShipFuelIntent.Match(msg);
          if (!m.Success) return null;
          string num = m.Groups[1].Value;
          if (string.IsNullOrEmpty(num)) num = m.Groups[2].Value;
          if (string.IsNullOrEmpty(num)) num = m.Groups[3].Value;
          if (!int.TryParse(num, out int shipNumber)) return null;
          var ship = ResolveShipByNumber(shipNumber);
          if (ship == null) return $"Ship {shipNumber} does not exist.";
          float pct = ship.FuelPercent * 100f;
          return $"Ship {shipNumber} fuel is at {Mathf.RoundToInt(pct)}%.";
      }

      static string TryHandleAmbiguousShipResource(string msg)
      {
          if (!AmbiguousShipResourceIntent.IsMatch(msg)) return null;
          // Don't refuse if a per-ship version of the intent already matched.
          if (ShipPowerIntent.IsMatch(msg)) return null;
          if (ShipFuelIntent.IsMatch(msg))  return null;
          return "Which ship? Try 'ship 1', 'ship 2', and so on.";
      }

      static Ship ResolveShipByNumber(int shipNumber)
      {
          foreach (var pair in FleetTelemetry.EnumerateAllShipsWithNumbers())
          {
              if (pair.number != shipNumber) continue;
              return pair.go != null ? pair.go.GetComponent<Ship>() : null;
          }
          return null;
      }
  ```

- [ ] **Step 5: Wire the new handlers into the main routing entry point**

  In the file's main public entry method (likely `Route(string msg)` or `Handle(string msg)` — find the one that calls all the existing `TryHandleX` methods in sequence and returns the first non-null result), add three calls:

  ```csharp
          var rPower = TryHandleShipPower(msg); if (rPower != null) return rPower;
          var rFuel  = TryHandleShipFuel(msg);  if (rFuel  != null) return rFuel;
          var rAmb   = TryHandleAmbiguousShipResource(msg); if (rAmb != null) return rAmb;
  ```

  Place them ABOVE the vitals fallback (the block that handles "how am I" / "vitals" / hunger / thirst / health). Per-ship checks must run first because their queries are more specific.

  **If the file uses a different routing convention (switch statement, table-driven, etc.)** — match that convention. If the structure is unclear after reading the file, STOP and report BLOCKED.

- [ ] **Step 6: Save and verify compile**

  Save. Console: no errors.

- [ ] **Step 7: Editor verification**

  Open the phone AI chat. Ask:
  - "What's ship 1's power?" → "Ship 1 power is at X%."
  - "What's ship 1's fuel?" → "Ship 1 fuel is at X%."
  - "What's ship power?" → "Which ship? Try 'ship 1', 'ship 2', and so on."
  - "What's ship 99's fuel?" → "Ship 99 does not exist."

- [ ] **Step 8: Commit**

  ```bash
  git add "Assets/3 - Scripts/AI/IntentRouter.cs"
  git commit -m "feat(ai): per-ship power + fuel intents; refuse ambiguous queries"
  ```

---

## Task 10: `AIChatScreen` updates ship-power refs to piloted ship

The vitals tour line that mentions ship power needs to read the piloted ship's value (or omit when not piloted) rather than the now-zombie `ResourceManager.ShipPowerPercent`.

**Files:**
- Modify: `Assets/3 - Scripts/AI/AIChatScreen.cs`

- [ ] **Step 1: Find the reference**

  Open `Assets/3 - Scripts/AI/AIChatScreen.cs`. Find line ~831:

  ```csharp
              float sp = rm.ShipPowerPercent * 100f;
  ```

  And the message-construction lines below it that mention `sp`.

- [ ] **Step 2: Replace with piloted-ship reads**

  Replace:

  ```csharp
              float sp = rm.ShipPowerPercent * 100f;
  ```

  With:

  ```csharp
              var pi = Ship.PilotedInstance;
              float sp = pi != null ? pi.PowerPercent * 100f : -1f; // -1 = "not piloted"
              float sf = pi != null ? pi.FuelPercent  * 100f : -1f;
  ```

  Then find any message construction that uses `sp` and add a fuel sibling. For example, if there's a line like:

  ```csharp
              if (sp < 25f) return $"Ship power is at {Mathf.RoundToInt(sp)} percent.";
  ```

  Add immediately after:

  ```csharp
              if (sf >= 0f && sf < 25f) return $"Ship fuel is at {Mathf.RoundToInt(sf)} percent. The reactor needs crystals.";
  ```

  Guard each `sp`-using line with `sp >= 0f` so when not piloted the line skips. Example: change

  ```csharp
              if (sp < 25f) return ...
  ```

  to

  ```csharp
              if (sp >= 0f && sp < 25f) return ...
  ```

- [ ] **Step 3: Save and verify compile**

  Save. Console: no errors.

- [ ] **Step 4: Editor verification**

  In the phone AI chat, ask "how am I doing" or whatever triggers the existing vitals tour. While not piloting, the response should NOT mention ship power. While piloting with low fuel, it should mention fuel. With low power and piloting, it should mention power.

- [ ] **Step 5: Commit**

  ```bash
  git add "Assets/3 - Scripts/AI/AIChatScreen.cs"
  git commit -m "feat(ai-chat): vitals tour reads piloted-ship power and fuel"
  ```

---

## Task 11: `FleetTelemetry` includes power + fuel per ship row

The fleet roster the AI receives in the system prompt now includes each ship's power and fuel so generic fleet queries get accurate data.

**Files:**
- Modify: `Assets/3 - Scripts/AI/FleetTelemetry.cs`

- [ ] **Step 1: Add power/fuel to `RenderShipRow`**

  Open `Assets/3 - Scripts/AI/FleetTelemetry.cs`. Find the existing `RenderShipRow` method (line ~92). At the bottom (just before the `return` statement at line 159), add:

  ```csharp
          string powerStr = s != null ? $"power {Mathf.RoundToInt(s.PowerPercent * 100f)}%" : "power ?";
          string fuelStr  = s != null ? $"fuel {Mathf.RoundToInt(s.FuelPercent * 100f)}%"   : "fuel ?";
  ```

  Then change the existing return line from:

  ```csharp
          return $"Ship {shipNumber}: {location}, {dust}, dish OK, {solar}, {thrusters}, {hatch}";
  ```

  To:

  ```csharp
          return $"Ship {shipNumber}: {location}, {dust}, {powerStr}, {fuelStr}, dish OK, {solar}, {thrusters}, {hatch}";
  ```

- [ ] **Step 2: Save and verify compile**

  Save. Console: no errors.

- [ ] **Step 3: Editor verification**

  Open the phone AI. Ask "give me a fleet status" or whatever triggers a multi-ship roster. Each row should now include power and fuel percentages.

- [ ] **Step 4: Commit**

  ```bash
  git add "Assets/3 - Scripts/AI/FleetTelemetry.cs"
  git commit -m "feat(ai-telemetry): include per-ship power and fuel in fleet rows"
  ```

---

## Task 12: `ShipReactor` + `ReactorPopup` new files

The Reactor child GameObject inside each ship prefab gets a behavior script. F press inside the trigger AND with a crystal stack equipped → partial-fill the ship's fuel.

**Files:**
- Create: `Assets/3 - Scripts/Ship/ShipReactor.cs`
- Create: `Assets/3 - Scripts/Ship/ReactorPopup.cs`

- [ ] **Step 1: Create `ReactorPopup.cs` (world-space floating text)**

  Create the file at `Assets/3 - Scripts/Ship/ReactorPopup.cs` with:

  ```csharp
  using TMPro;
  using UnityEngine;

  // World-space "+N FUEL" floater spawned by ShipReactor on a successful refuel.
  // Mirrors CrystalPopup's float-up + face-camera + alpha-fade pattern.
  public class ReactorPopup : MonoBehaviour
  {
      public static void Spawn(Vector3 worldPos, float fuelAdded)
      {
          var go = new GameObject("ReactorPopup");
          go.transform.position = worldPos;
          var p = go.AddComponent<ReactorPopup>();
          p.Init(fuelAdded);
      }

      TextMeshPro tmp;
      float lifetime = 1.5f;
      float age;
      Vector3 upDir = Vector3.up;
      Camera _cam;
      const float FloatSpeed = 1.2f;

      void Init(float fuelAdded)
      {
          tmp = gameObject.AddComponent<TextMeshPro>();
          tmp.text = $"+{Mathf.RoundToInt(fuelAdded)} FUEL";
          tmp.fontSize = 6f;
          tmp.color = new Color32(140, 230, 255, 255); // crystal cyan
          tmp.fontStyle = FontStyles.Bold;
          tmp.alignment = TextAlignmentOptions.Center;
          tmp.outlineWidth = 0.25f;
          tmp.outlineColor = Color.black;
      }

      void Update()
      {
          age += Time.deltaTime;
          if (age >= lifetime || tmp == null) { Destroy(gameObject); return; }

          transform.position += upDir * FloatSpeed * Time.deltaTime;

          if (_cam == null) _cam = Camera.main;
          if (_cam != null)
          {
              Vector3 toCam = transform.position - _cam.transform.position;
              if (toCam.sqrMagnitude > 0.0001f)
                  transform.rotation = Quaternion.LookRotation(toCam.normalized, upDir);
          }

          float t = age / lifetime;
          var c = tmp.color;
          c.a = Mathf.Clamp01(1f - t * t);
          tmp.color = c;
      }
  }
  ```

- [ ] **Step 2: Create `ShipReactor.cs`**

  Create the file at `Assets/3 - Scripts/Ship/ShipReactor.cs` with:

  ```csharp
  using UnityEngine;

  // Lives on the Reactor child GameObject inside each ship prefab. The Reactor
  // already has a BoxCollider with isTrigger=true. When the player walks into
  // the trigger AND has a crystal stack equipped in the hotbar, an F-to-insert
  // prompt appears. Pressing F drains crystals (partial fill: take all the
  // player has up to the topup amount) and restores the ship's fuel.
  public class ShipReactor : MonoBehaviour
  {
      [Tooltip("Owning ship. Auto-resolved via GetComponentInParent<Ship>() if null.")]
      public Ship ship;

      [Tooltip("Fuel units added per crystal. With Ship.fuelMax=100 and this=5, 20 crystals fill a full tank, 10 fill half.")]
      public float fuelPerCrystal = 5f;

      bool _playerInZone;
      bool _promptShown;

      void Awake()
      {
          if (ship == null) ship = GetComponentInParent<Ship>();
      }

      void OnTriggerEnter(Collider other)
      {
          if (other == null) return;
          if (!other.CompareTag("Player")) return;
          _playerInZone = true;
      }

      void OnTriggerExit(Collider other)
      {
          if (other == null) return;
          if (!other.CompareTag("Player")) return;
          _playerInZone = false;
          HidePrompt();
      }

      void Update()
      {
          if (ship == null || !_playerInZone) { HidePrompt(); return; }

          bool eligible = ship.FuelPercent < 1f && IsPlayerHoldingCrystals();
          if (eligible)
          {
              ShowPrompt();
              if (TutorialGate.InteractPressed()) Refuel();
          }
          else
          {
              HidePrompt();
          }
      }

      static bool IsPlayerHoldingCrystals()
      {
          var hb = Hotbar.Instance;
          if (hb == null) return false;
          if (hb.GetEquippedSlotId() != Hotbar.ItemId.Crystal) return false;
          return hb.GetResourceTotal(Hotbar.ItemId.Crystal) > 0;
      }

      void Refuel()
      {
          if (ship == null) return;
          float deficit = ship.fuelMax - ship.FuelPercent * ship.fuelMax;
          if (deficit <= 0f) return;
          int crystalsNeeded = Mathf.CeilToInt(deficit / fuelPerCrystal);
          if (crystalsNeeded <= 0) return;
          var hb = Hotbar.Instance;
          if (hb == null) return;
          int available = hb.GetResourceTotal(Hotbar.ItemId.Crystal);
          int take = Mathf.Min(crystalsNeeded, available);
          if (take <= 0) return;
          if (!hb.SpendResource(Hotbar.ItemId.Crystal, take)) return;
          float fuelAdded = take * fuelPerCrystal;
          ship.RestoreFuel(fuelAdded);
          ReactorPopup.Spawn(transform.position + transform.up * 0.5f, fuelAdded);
      }

      void ShowPrompt()
      {
          if (_promptShown) return;
          GameUI.SetInteractionPrompt(this, "F  INSERT CRYSTALS");
          _promptShown = true;
      }

      void HidePrompt()
      {
          if (!_promptShown) return;
          GameUI.ClearInteractionPrompt(this);
          _promptShown = false;
      }
  }
  ```

  Note on `GameUI.SetInteractionPrompt`: this is the existing project pattern (used by `FishingRodPickup.cs:52` etc.). If the actual signature differs (e.g. expects `Interactable` rather than `Object`), check `Assets/3 - Scripts/Scripts/Game/UI/GameUI.cs:22-35` — adapt to whichever signature it exposes. The intent is: show a "F to interact" prompt with the given text while the script is in scope; clear it when leaving scope. If the project uses a different mechanism (e.g. a direct `InteractPromptUI` static call), use that instead. If unclear, STOP and report BLOCKED.

- [ ] **Step 3: Save and verify both files compile**

  Save. Console: no errors. If `GameUI.SetInteractionPrompt` signature errors, swap the implementation to match the actual signature found in `GameUI.cs`. If `TutorialGate.InteractPressed` doesn't exist, search `TutorialGate.cs` for the equivalent (likely `InteractPressedThisFrame` or `Input.GetKeyDown(KeyCode.F)`).

- [ ] **Step 4: Add the `ShipReactor` component to the Reactor child in each ship prefab**

  This is a scene/prefab edit. For each ship prefab that has a `Reactor` child:
  - `Assets/1 - samsPrefabs/SHIP44.prefab`
  - `Assets/1 - samsPrefabs/Ship_MissingLeft.prefab`
  - `Assets/1 - samsPrefabs/Ship_MissingRight.prefab`
  - `Assets/1 - samsPrefabs/Ship_NoThrusters.prefab`
  - `Assets/1 - sam3d/Ship_Full.prefab`

  Open each prefab in the Unity Editor (Project view → double-click). In the hierarchy panel, locate the `Reactor` child. With it selected:
  1. Add Component → search "ShipReactor" → Add.
  2. The `ship` field can be left null — it auto-resolves via `GetComponentInParent<Ship>()` at Awake.
  3. The `Fuel Per Crystal` field stays at the default 5.

  Save the prefab (`Ctrl+S` after exiting Prefab Mode, or click the back arrow at the top of the hierarchy).

  **If you cannot find the `Reactor` child in a prefab** (e.g. variant without a reactor mesh), skip that prefab — adding the script to others is sufficient. SHIP44 is the most important; the damage-state variants share the same hierarchy.

- [ ] **Step 5: Editor verification**

  Equip a crystal stack via hotbar (mine some, press 1-5 to highlight the crystal slot). Approach a parked ship's reactor — walk through the door, into the Reactor BoxCollider. Confirm:
  - "F INSERT CRYSTALS" prompt appears
  - Press F → ship's fuelCurrent increases, crystals deducted, "+25 FUEL" (or similar) popup
  - With 50% fuel + 5 crystals: all 5 consumed, +25 fuel, fuelCurrent=75
  - With 0% fuel + 30 crystals: 20 consumed (full tank), +100 fuel, fuelCurrent=100, 10 crystals remaining
  - Without crystal slot equipped (e.g. axe equipped): NO prompt

- [ ] **Step 6: Commit**

  ```bash
  git add "Assets/3 - Scripts/Ship/ShipReactor.cs" "Assets/3 - Scripts/Ship/ReactorPopup.cs" "Assets/1 - samsPrefabs/SHIP44.prefab" "Assets/1 - samsPrefabs/Ship_MissingLeft.prefab" "Assets/1 - samsPrefabs/Ship_MissingRight.prefab" "Assets/1 - samsPrefabs/Ship_NoThrusters.prefab" "Assets/1 - sam3d/Ship_Full.prefab"
  git commit -m "feat(ship): ShipReactor — F to insert crystals refills fuel (partial fills supported)"
  ```

  If any of the prefab paths listed don't exist in `git status`, omit them from the `git add`.

---

## Task 13: Save / load — per-ship power + fuel

Add `power` and `fuel` fields to `ExtraShipSave`. Capture from each `Ship.PowerPercent * powerMax` (absolute units). Apply on load, with `-1f` legacy sentinel.

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveData.cs`
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`

- [ ] **Step 1: Add the two new fields on `ExtraShipSave`**

  Open `Assets/3 - Scripts/SaveSystem/SaveData.cs`. Find the existing `ExtraShipSave` class (~line 52). At the end of the field list (just before the closing `}`), add:

  ```csharp
      // Absolute power/fuel units (not percentages — survive future tuning of
      // powerMax/fuelMax). -1f sentinel means the save predates this field;
      // SaveCollector.ApplyExtraShips then keeps the spawn-default 50% values.
      public float power = -1f;
      public float fuel  = -1f;
  ```

- [ ] **Step 2: Capture in `SaveCollector.CaptureExtraShips`**

  Open `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`. Find `CaptureExtraShips` (~line 207). Inside the loop, just before `list.Add(entry);` (~line 237), add:

  ```csharp
              entry.power = ship.PowerPercent * ship.powerMax;
              entry.fuel  = ship.FuelPercent  * ship.fuelMax;
  ```

- [ ] **Step 3: Apply in `SaveCollector.ApplyExtraShips`**

  In the same file, find `ApplyExtraShips` (~line 1101). Locate the existing block that sets `ship.canFly`, `ship.SetHatchOpen`, etc. (~line 1142-1148). Immediately after `ship.canFly = entry.canFly;`, add:

  ```csharp
                  if (entry.power >= 0f) ship.SetPower(entry.power);
                  if (entry.fuel  >= 0f) ship.SetFuel (entry.fuel);
  ```

- [ ] **Step 4: Save and verify compile**

  Save both files. Console: no errors.

- [ ] **Step 5: Editor verification — save/load round-trip**

  Spawn a ship, pilot it, drain to ~30% fuel and ~60% power. Save the game (pause → save). Reload from main menu. Verify the ship's power and fuel match the saved values within ±1%.

- [ ] **Step 6: Editor verification — legacy save load**

  If you have a pre-refactor save in `%AppData%\..\LocalLow\DefaultCompany\Solar System 2\saves\`, load it. Verify each bought ship spawns at 50% / 50% (the spawn defaults, since the save's `power` and `fuel` fields are absent and JsonUtility leaves them as the field default `-1f`).

- [ ] **Step 7: Commit**

  ```bash
  git add "Assets/3 - Scripts/SaveSystem/SaveData.cs" "Assets/3 - Scripts/SaveSystem/SaveCollector.cs"
  git commit -m "feat(save): per-ship power and fuel (with -1 legacy sentinel)"
  ```

---

## Task 14: Strip `ResourceManager` ship-power state

Final cleanup. All consumers have migrated. Remove the zombie fields and methods so future readers can't accidentally reach for them.

**Files:**
- Modify: `Assets/3 - Scripts/Survival/ResourceManager.cs`
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`

- [ ] **Step 1: Remove ship-power fields from `ResourceManager.cs`**

  Open `Assets/3 - Scripts/Survival/ResourceManager.cs`. Delete:
  - The `[Header("Ship Power")]` block and the four fields below it: `shipPowerMax`, `shipPowerIdleDrainRate`, `shipPowerFlyingDrainRate`, `shipPowerRestoreThreshold` (~line 19-24).
  - The private field `float shipPowerCurrent;` (~line 38).
  - The private fields `bool shipPowerDepleted;` and `Ship shipRef;` (~line 53-56). **Keep** `bool isDead;` and `PlayerController playerRef;`.
  - In `Awake()`, remove the line `shipRef = FindObjectOfType<Ship>();`.
  - In `Start()`, remove `shipPowerCurrent = shipPowerMax;`.

- [ ] **Step 2: Remove the public ship-power methods**

  Delete:
  - `public void DrainShipPower(float amount)` and its body (~line 166-169).
  - `public void RestoreShipPower(float amount)` and its body (~line 171-174).
  - `public float ShipPowerPercent` property (~line 179).

- [ ] **Step 3: Trim the `ApplyState` signature**

  Replace:

  ```csharp
      public void ApplyState(float hunger, float thirst, float health, float shipPower)
      {
          hungerCurrent    = Mathf.Clamp(hunger,    0f, 100f);
          thirstCurrent    = Mathf.Clamp(thirst,    0f, 100f);
          healthCurrent    = Mathf.Clamp(health,    0f, 100f);
          shipPowerCurrent = Mathf.Clamp(shipPower, 0f, shipPowerMax);
          isDead = false;
          shipPowerDepleted = shipPowerCurrent <= 0f;
          if (shipRef != null) shipRef.canFly = !shipPowerDepleted;
      }
  ```

  With:

  ```csharp
      public void ApplyState(float hunger, float thirst, float health)
      {
          hungerCurrent = Mathf.Clamp(hunger, 0f, 100f);
          thirstCurrent = Mathf.Clamp(thirst, 0f, 100f);
          healthCurrent = Mathf.Clamp(health, 0f, 100f);
          isDead = false;
      }
  ```

- [ ] **Step 4: Update the call site in `SaveCollector.cs`**

  Open `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`. Find line ~863:

  ```csharp
              ResourceManager.Instance.ApplyState(s.hunger, s.thirst, s.health, s.shipPower);
  ```

  Replace with:

  ```csharp
              ResourceManager.Instance.ApplyState(s.hunger, s.thirst, s.health);
  ```

- [ ] **Step 5: Update `CaptureResources` to stop writing `s.shipPower`**

  In `SaveCollector.cs` find `CaptureResources` (~line 241). Delete the line:

  ```csharp
          s.shipPower = rm.ShipPowerPercent * 100f;
  ```

  Leave the `ResourcesSave.shipPower` FIELD in `SaveData.cs` untouched (legacy compat — old saves still have it; new saves write 0 since we never set it, which is harmless).

- [ ] **Step 6: Save and verify compile**

  Save all touched files. Console: no errors anywhere. If a stale reference to one of the removed members surfaces (e.g. in code we didn't enumerate), fix it on the spot by retargeting to the per-ship API.

- [ ] **Step 7: Editor sanity check — full happy path still works**

  Play. Pilot a ship. Verify: power and fuel drain correctly, HAL still fires its voice lines, HUD bars work, AI queries answer correctly, refuel via reactor works, save/load round-trips correctly.

- [ ] **Step 8: Commit**

  ```bash
  git add "Assets/3 - Scripts/Survival/ResourceManager.cs" "Assets/3 - Scripts/SaveSystem/SaveCollector.cs"
  git commit -m "refactor(resource-manager): strip ship-power state (now per-ship)"
  ```

---

## Task 15: Full integration verification (no code changes)

Walk every test from the spec's test plan. Surface results to the user.

**Files:** None (verification only).

- [ ] **Step 1: Vendor + debug spawn defaults**

  Spawn a ship via vendor; another via debug menu. Pilot each. Confirm power and fuel bars show 50%.

- [ ] **Step 2: Drain rates**

  Pilot a ship for 30 seconds doing nothing. Both bars deplete; fuel slightly faster than power. Hold WASD for 30 s — fuel depletes faster. Hold Shift+WASD — fuel depletes faster still.

- [ ] **Step 3: Fuel = 0 behavior**

  Drain fuel to 0%. Verify: thrust no longer fires. Headlight still works. LebronLight (if power > 0) still works.

- [ ] **Step 4: Power = 0 behavior**

  Drain power to 0%. Verify: thrust no longer fires. Headlight still works. LebronLight refuses to ignite (or auto-extinguishes if was on).

- [ ] **Step 5: Refuel flow**

  Equip a crystal stack in hotbar. Walk into the reactor trigger. Press F. Crystals consumed correctly per the partial-fill rules.

- [ ] **Step 6: Solar recharge**

  Park a ship on a planet's day side, power < 100%. Confirm power restores at chargeRate per second. Fuel unchanged.

- [ ] **Step 7: AI queries**

  Phone AI answers:
  - "What's ship 1's power?" → `Ship 1 power is at X%.`
  - "What's ship 1's fuel?" → `Ship 1 fuel is at X%.`
  - "What's ship power?" → `Which ship? Try 'ship 1', 'ship 2', and so on.`
  - "What's ship 99's fuel?" → `Ship 99 does not exist.`

- [ ] **Step 8: HAL voice lines**

  Drain fuel to 25% → HAL volunteers low-fuel line. To 0% → HAL volunteers dry line.

- [ ] **Step 9: Save round-trip**

  Save with custom power/fuel values. Reload. Values match exactly (within rounding).

- [ ] **Step 10: Legacy save load**

  Load a pre-refactor save. Ships spawn at 50% / 50% (the spawn defaults via `-1f` legacy sentinel).

- [ ] **Step 11: Build sanity check (MainMenu trap)**

  Build the game. Launch from MainMenu → PLAY. Buy a ship. Verify no NRE in `%AppData%\..\LocalLow\DefaultCompany\Solar System 2\Player.log` for any of: `Ship`, `ShipReactor`, `ReactorPopup`, `LebronLight`, `SolarPanelCharger`, `ResourceManager`.

- [ ] **Step 12: First-ship numbering check (the Task 0 fix)**

  Buy your first ship. Verify the HUD / AI / save data labels it "Ship 1" — NOT "Ship 2". This is independent of the fuel feature but is a primary user complaint we resolved in commit `5a23439`; confirm the fix is still in effect after all the subsequent changes.

---

## Out of scope (per design spec)

- **Reactor visuals** — no glow/animation changes; just add the script.
- **LebronLight per-ship attachment refactor** — singleton model stays; we just point it at the piloted/closest ship for power drain.
- **Non-solar recharge sources** — only solar charges power.
- **Non-crystal fuel sources** — only crystals refuel.
- **Hot-swappable reactor part** — fuelMax is fixed at the spawn value for now.
