# Grapple gun (prototype) — design

🟢 ACTIVE — built 2026-09-10, playtest pending. Sam's calls: winch pull (not spring / not rigid tow), a miss resets the gun, locker seeding on New Game only.

## What it is

A hotbar equippable cloned from the pistol's viewmodel/equip rig with every gun
part removed. It fires a ball that sticks to what you aimed at, then a held
right click winches you along the rope to it.

| Input | State | Effect |
|---|---|---|
| Left click | idle | Fire. Ray along the crosshair up to `range` (40 m). Ball flies muzzle → hit point at `ballSpeed` (60 m/s) and latches, parented to the collider it hit (planets, shuttle, trees, anything solid). A miss flies to 40 m and the gun resets itself. |
| Left click | any other | Reset: ball destroyed, rope hidden. |
| Right click (held) | anchored | Winch. Velocity along the rope is set to `reelSpeed` (8 m/s) toward the anchor, easing to a stop inside `holdDistance` (1.5 m) and holding you there. Sideways velocity is kept (pendulum swing) with `swingDamping`; away-from-anchor velocity is cancelled, so the rope never stretches. |
| Right click released | anchored | Slack: you fall, the ball stays put, rope droops. Hold again to resume. |
| Unequip / ship piloted / component disabled | any | Reset. You can never be tethered invisibly. |

## Physics rule (the part worth remembering)

The winch is the fishing bobber's velocity-level line constraint turned around
(`Bobber.cs` ~line 976). It writes `rb.velocity` only, never a position, so PhysX
depenetration cannot compound into a launch. The anchor's own velocity is measured
by finite difference each physics step with the bobber's teleport reject (a jump
> 5 m in one step is an origin shift, keep the last estimate), so a moving planet
or shuttle is subtracted out before the constraint and added back after.

`GrappleGunController` carries `[DefaultExecutionOrder(50)]` so its `FixedUpdate`
runs after `PlayerController`'s. The grounded grip there rewrites `rb.velocity`;
running last means the winch wins the step, the player leaves the ground, and the
controller's airborne branch (jetpack included) takes over on the next one.

## Pieces

- `Assets/3 - Scripts/Pickups/GrappleGunController.cs` — the whole feature. Knobs:
  range, ballSpeed, ballPrefab/ballDiameter/ballColor, reelSpeed, holdDistance,
  holdSnap, swingDamping, heldSwingDamping, rope material/width/colour/segments,
  slackSag. Prototype borrows the pistol mesh (`Pistol_B`) and pistol icon;
  `gunPrefab` and `hotbarIcon` swap them.
- Rope = the fishing rod's LineRenderer quadratic Bézier, drooping along the
  player's `-up`; taut while reeling (`_sagBlend` → 0).
- `Hotbar.ItemId.GrappleGun` appended at the END of the enum (value 26) + one
  registry row ("GRAPPLE"). `LootBoxStarterItem`, `StorageUI`, `FishStagingUI`
  switch cases so the locker can seed and draw it.
- Save: `EquipmentSave.grappleEquipped/grappleUnlocked` (appended), captured and
  applied beside the pistol in `SaveCollector`. Live rope state is not saved.
- Scene: added component on the player prefab instance in `1.6.7.7.7.unity`
  (fileID 1348311899, same hold position as the pistol, `CameraHoldPos`).
- Locker: fourth `LootBoxStarterItem` on `Locker_2` in `Shuttle_Lander.prefab`
  (itemId 26, count 1, unlock on). Seeds on New Game only.

## Not covered

Co-op partners do not see the grapple, ball or rope (`HeldItemResolver` has no
case). Pulling a light object toward you instead of you toward it. No sounds
assigned (`fireClip` / `latchClip` slots exist).
