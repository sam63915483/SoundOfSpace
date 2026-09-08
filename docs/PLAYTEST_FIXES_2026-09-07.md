# Playtest — the seven fixes from your 2026-09-07 notes

All compile clean (0 warnings). None played yet. Nothing here needs a scene save;
the shuttle change is on the prefab, the economy is in the JSON.

## 1. Jumping on a dwarf planet (2 min)
- [ ] Land on **Puddle** or **Hearth** (the worst two). Stand still, jump straight up,
      land. You should come down on the same spot — no sideways drift, no ground
      sliding past under you. Try it near the day/night line, where it used to be worst.
- [ ] Sprint-jump: you should land where your momentum says, nothing extra.
- [ ] Humble Abode and the twins should feel exactly as before (HA was already
      99% right; Icey Twin's old sideways "slide" should be gone too).
- [ ] Walk into a cave, ride the shuttle, come back out — gravity should never
      kick or flicker at the handover.

## 2. Grass under the torch, late in a session (5 min)
- [ ] Boot, walk at night with the torch: grass should match the ground. Now **fly
      the shuttle somewhere and come back**. Torch on grass again — it must look
      the same as before the flight. That flight was the trigger.
- [ ] If you're testing on a save from an old build: the fix also removes the bad
      marker within 3 s of the torch existing, so it heals itself.

## 3. Fuel crystals (1 min)
- [ ] Shine the torch across a crystal slowly. It should light up progressively as
      the beam crosses it, with a bright side and a dark side, like any other prop.
      (They're a touch glossier now — the old material was a mobile shader with
      no shine. Say if you'd rather they were matte.)

## 4. The 8th slot (2 min)
- [ ] Fill 7 slots, catch a fish: it lands in the 8th cell. Catch another: NOW
      "inventory full".
- [ ] Put a fish in the 8th cell, sleep in the pod (save), reload: it's still there.
- [ ] Money still shows up in slot 8 when that slot is free, and moves elsewhere
      if you fill slot 8 with something else first.

## 5. Fuel (1 min)
- [ ] Reactor screen at full: **RANGE 22.5 KM** (was 15). A 12 km hop should
      burn about 56 of 100 units instead of 81.

## 6. Selling fish elsewhere (5 min)
- [ ] Catch 6–7 fish on one planet, fly to the nearest other market, open the
      panel's FISH PRICE INDEX. Most of what you carry should now be IMPORTED or
      DELICACY; expect roughly one unwanted species per bag, not five.
- [ ] Phone MARKETS page: the longer lists fit without spilling over the hint line.

## 7. Reactor screen (10 s)
- [ ] Reads `BATTERY 62%` / `RANGE 8.6 KM` / bar, left-aligned, no smaller than before.
