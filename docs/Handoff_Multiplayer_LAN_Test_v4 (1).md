# Handoff — Multiplayer LAN Test (Barebones Proof) v4

**Read first — workflow rule:** State your full build plan before implementing anything and wait for Sam's explicit go-ahead. (This is standing project protocol; it also lives in GDD_StoryBible_v2.md §0 rule 4 — you do not need to open that file.)

**Doc freshness warning:** /docs contains a mix of current and superseded md files — the story bible (GDD_StoryBible_v2.md, Jul 23) and most design docs predate the Aug 2026 Schedule 1-style pivot. Do NOT pull story or design direction from other docs for this task; this handoff is fully self-contained. If you believe you need broader project context, ask Sam which doc is current instead of guessing.

**v4 changes (supersedes v3):** workflow rule now stated inline (the story bible is cited only as where the rule lives, not as reading material) and doc-freshness warning added. All technical content unchanged from v3. Earlier: Sam confirmed the floating origin rebases are distance-triggered by TOTAL travel including planetary orbit — planets orbit the sun, so origin shifts fire even while standing still on a planet. World-space position sync is therefore off the table entirely; planet-relative sync is the required approach from the start.

## Goal
Prove two players can share a session. Sam's desktop runs the game, clicks **HOST**, and on-screen text confirms multiplayer is active. His laptop runs a build, enters the desktop's LAN IP, clicks **JOIN**, spawns a few meters above the host, falls to the ground beside them, and both players walk around near the landing shuttle and see each other move — including through origin rebases. That's the entire deliverable.

## Hard rules
1. **Core systems are untouchable.** Do NOT modify, disable, or reconfigure the floating origin system, the n-body gravity simulation, the player movement scripts, or any other existing game system. Multiplayer integrates on top of them. If you believe a genuine dead end requires touching a core system, stop and report to Sam — do not proceed.
2. **No world-space position ever crosses the network.** Player poses, spawn offsets — everything transmitted between machines is expressed in planet-local coordinates (relative to the home planet's transform). World coordinates are machine-specific here: each machine's sim started at a different moment (planets in different orbital positions) and each machine rebases on its own schedule. A world-space number from one machine is meaningless on the other.

## Why planet-local coordinates work (context for implementation)
- A rebase shifts the planet and everything on it by the same offset → a player's position **relative to the planet transform** is unchanged through a rebase. Planet-local pose is rebase-invariant by construction.
- Using the planet's full transform (not just its center) also absorbs the planet's orbital motion and its spin, if it spins.
- The two machines' solar systems do NOT need to agree on where the planet is. Each machine places the remote player relative to its OWN copy of the planet. Both players correctly appear on the planet surface near the shuttle on both screens.
- (Out of scope, for later: full-game multiplayer with ships flying between planets would need the n-body sim state synced host→client. Not this test.)

## Non-goals — do not build
- No syncing of gameplay systems: chopping, mushrooms, inventory, economy, Tev dialogue, saving. They stay local and may misbehave in a session — fine.
- No n-body sim sync. No Steam, no Relay, no NAT punchthrough, no lobbies, no player names. Direct LAN only.
- No server authority or anti-cheat. Owner-authoritative everything.
- No ship/flight sync. Test happens on foot.

## Tech choice
- **Netcode for GameObjects (NGO) 1.x** — `com.unity.netcode.gameobjects`, latest 1.x via Package Manager. Do **NOT** install 2.x (requires Unity 6; project is 2022.3). Unity Transport installs as a dependency.
- No NetworkTransform / ClientNetworkTransform on players — replaced by the custom planet-relative sync below.

## Before writing code
- [OPEN] Check `Packages/manifest.json` and `Assets/` for leftovers from the previous multiplayer attempt (Mirror, Photon/PUN, FishNet, an older NGO, custom socket code). Report findings; remove conflicting packages before installing NGO.
- [EXISTS] Locate and report:
  - The player prefab/rig and every script that drives its transform (movement, input, custom gravity).
  - The home planet's transform/body reference (the thing to express poses relative to) and whether the planet spins.
  - The floating origin script: confirm Sam's description in code (trigger = accumulated travel distance including orbital motion), what it shifts when it fires, and the rough real-time cadence between rebases while standing still — needed for the rebase acceptance test.
  - How the scene-placed player currently gets into the world.

## Build spec

### [BUILD] 1. Package + NetworkManager
- Install NGO 1.x.
- Scene object `NetworkManager` with `NetworkManager` + `UnityTransport` components. Create it via Coplay, or flag it as the single placement item for Sam if convention requires.
- Port **7777 (UDP)**.

### [AUTHOR] 2. PlanetRelativeSync.cs — the core of this test
A NetworkBehaviour on the player prefab, replacing any NetworkTransform:
- NetworkVariables (write permission: **Owner**) for planet-local position and planet-local rotation, i.e. `planet.InverseTransformPoint(transform.position)` and `Quaternion.Inverse(planet.rotation) * transform.rotation`.
- **Owner:** each tick, write current planet-local pose. The owned player itself is otherwise untouched — normal movement, gravity, and the existing origin system act on it exactly as in single player.
- **Non-owner:** every frame (LateUpdate), convert the synced planet-local pose back through the LOCAL machine's planet transform (`planet.TransformPoint(...)`) and place the avatar. Because the conversion uses the planet's current post-rebase transform, rebases self-correct the same frame. Simple lerp/smoothing between updates; some jitter is acceptable for a proof.
- Resolve the planet reference at spawn on each machine independently (find the home planet object locally — never send a transform reference over the network).
- Mechanism alternative: parenting the non-owner avatar under the planet transform and syncing localPosition/localRotation is also acceptable if it doesn't fight NGO's parenting rules — your call; the invariant is what matters.

### [BUILD] 3. NetworkPlayer prefab
- Duplicate the existing first-person player rig into a new prefab `NetworkPlayer`. Do not modify the original.
- Add `NetworkObject` + `PlanetRelativeSync`.
- **Visible body:** the FP rig likely has no body mesh, which would make remote players invisible. Add a simple capsule or placeholder mesh so there is something to see. Per-client color (owner id → color) if trivial; skip if fiddly.
- [AUTHOR] `NetworkPlayerSetup.cs` — in `OnNetworkSpawn`: if `!IsOwner`, disable the Camera, AudioListener, all input/movement scripts, and anything else that writes to the transform — leave only the mesh, colliders, and PlanetRelativeSync. If `IsOwner`, leave everything enabled and untouched.
- Assign as **Player Prefab** on the NetworkManager so it auto-spawns per connection.

### [INTEGRATE] 4. Scene player handoff
When a session starts, the pre-placed scene player must be disabled/despawned so only network-spawned players exist. Hook whatever currently enables the player to stand down once `NetworkManager` is listening. With no session started, the game must run exactly as it does today.

### [INTEGRATE] 5. Spawn positions — joiner drops in beside the host (planet-local)
- **Host player** spawns at the normal player start near the landing shuttle.
- **Joining player** spawns a few meters **above** the host and falls in via gravity. Computed and transmitted in planet-local coordinates per Hard Rule 2:
  - Host side: take the host player's planet-local position; `up = normalize(hostLocalPos − planetLocalCenter)` (gravity is radial); spawn pose = `hostLocalPos + up * ~4m + tangentOffset(~1.5m)` — the sideways offset prevents landing inside the host.
  - Deliver that planet-local spawn pose to the joiner (initial NetworkVariable value, RPC, or connection-approval payload — your call); the joiner's machine converts it through its own planet transform and places its player there.
  - The joiner's own movement/gravity scripts handle the fall naturally; do not script the descent. (Doubles as an implicit test that gravity works on network-spawned players.)

### [BUILD] 6. Session UI — OnGUI overlay, zero scene wiring
[AUTHOR] `MultiplayerTestUI.cs`, attached to the NetworkManager object:
- Idle state: **HOST** button, IP text field (default `127.0.0.1`), **JOIN** button.
- HOST → set UnityTransport connection data with **listen address `0.0.0.0`**, port 7777 → `StartHost()` → display: `MULTIPLAYER ACTIVE — hosting on <LAN IP>:7777 — players: N`. Get the LAN IP by enumerating network interfaces; drive N from `OnClientConnectedCallback` / `OnClientDisconnectCallback`.
- JOIN → set connection data to the typed IP, port 7777 → `StartClient()` → display `CONNECTED to <ip>` on success or a visible failure/timeout message. Never fail silently.
- **SHUTDOWN** button while a session is running.
- On startup, this script also sets `Application.runInBackground = true` (see §7).

### [BUILD] 7. Same-machine testing support — Run In Background + windowed
Sam will run two instances on one desktop for rung 1, which means at most one window has focus. Unity builds **pause when unfocused by default**, which freezes the background instance and can stall or drop the connection. Required:
- Player Settings → Resolution and Presentation → **Run In Background: ON**, plus `Application.runInBackground = true` in code as belt-and-suspenders (covers the editor too).
- Test builds default to **windowed mode** at a modest resolution (e.g. 1280×720) so two instances sit side by side — no alt-tabbing needed.
- Fullscreen exclusive mode off for test builds.

### [TEST] 8. Testing ladder — in order, do not skip to LAN
1. **Same machine:** two windowed instances side by side (editor as host + a Windows build as client works too), joining `127.0.0.1`. Verify the joiner falls in beside the host and both can walk while the other window is unfocused.
2. **Rebase survival (same machine):** both players stand still next to each other long enough for at least one origin shift to fire on each instance (cadence known from the [EXISTS] report). **Pass = neither remote avatar teleports or drifts when a shift fires.** If one snaps across the map, world coordinates are leaking somewhere — fix before going to LAN.
3. **LAN:** copy the build to the laptop (also Windows — MSI Katana). Desktop hosts (editor or build). Laptop joins the desktop's IPv4 from `ipconfig` (usually `192.168.x.x`). Repeat the rebase-survival check once connected.
4. Pass = both machines show two players, both can walk, movement mirrors within about a second, and rebases are invisible.

## Known gotchas — check these before blaming the code
- **Host listen address:** if UnityTransport listens on `127.0.0.1` (a common default), LAN clients can never connect. It must be `0.0.0.0`.
- **Windows Firewall:** the first hosting launch prompts for access — tick **both** Private and Public. If the prompt was ever dismissed, add an inbound UDP 7777 rule (or allow the app). This is the #1 killer of LAN tests and a prime suspect for the last attempt. (Loopback testing bypasses it — exactly why rungs 1–2 come first.)
- **Unfocused pause:** if the background instance seems frozen or disconnects during same-machine testing, Run In Background isn't actually enabled — recheck §7 before debugging anything else.
- **Remote player teleporting periodically:** that's a rebase leaking through — something is syncing or caching a world-space value. Audit against Hard Rule 2.
- Both machines on the same network/subnet; some routers have wireless AP/client isolation that blocks device-to-device traffic entirely.
- A join that times out with no error usually means firewall or wrong IP — not code.

## Acceptance
- Desktop: click HOST → on-screen text confirms multiplayer is active.
- Laptop: enter IP, click JOIN → connects, spawns a few meters above the host, falls to the ground beside them.
- Both players visible to each other, walking around near the shuttle.
- **Both players stand still through at least one origin rebase on each machine — nobody teleports.**
- Two instances run side by side on one machine without freezing when unfocused.
- No existing game system was modified; with no session started, the game runs exactly as before.
