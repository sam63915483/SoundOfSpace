# Phone AI Revamp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the in-game phone AI genuinely game-aware — knows fleet state per ship with a satellite-dish gate, knows the game's systems via an audited knowledge file, volunteers substantive lines instead of stock filler, and the concert waypoint lands center-stage. Plus rewrite `docs/GAME_OVERVIEW.md` to reflect current state.

**Architecture:** New `FleetTelemetry` static helper that emits a structured "FLEET STATE" block into every system prompt. Two new tool verbs (`markship`, `showship`) routed through the existing `HALToolDispatcher`. `HALCommentator` loses its ~30-line ambient-idle pool entirely and gains substantive vitals / ship-dust / orbit-stabilized / concert-active triggers, plus planet-named atmosphere lines. `ConcertStageHub` gains an `OnStageActivated` event and a `FindActiveStageSpeaker()` helper for the center-stage marker fix. Knowledge file gets six new entries plus a planet-lore expansion pass. Spec: `docs/superpowers/specs/2026-05-22-phone-ai-revamp-design.md`.

**Tech Stack:** Unity 2022.3 (C#), LLMUnity (Hermes-3-Llama-3.1-8B GGUF runtime), existing Assembly-CSharp (no `.asmdef`).

**Testing model:** This is a Unity Editor project — no pytest/xunit runner. Each task's verification = (a) scripts compile clean on save, (b) Unity Console shows no errors, (c) named Play-mode scenarios produce specified console output and visible behavior. Each task includes its scenarios.

**Commit policy:** Per project convention (`CLAUDE.md` is silent; user system prompt requires explicit approval), each task includes an OPTIONAL commit step at the end. Do not run `git commit` unless the user has approved committing for this work item; otherwise skip the commit step and continue to the next task. The plan is otherwise self-contained — partial completion at any task boundary leaves a buildable project.

---

## File Structure

```
NEW   Assets/3 - Scripts/AI/FleetTelemetry.cs                   (~200 LoC)
EDIT  Assets/3 - Scripts/AI/LLMService.cs                       (~25 LoC added)
EDIT  Assets/3 - Scripts/AI/HALToolDispatcher.cs                (~80 LoC added)
EDIT  Assets/3 - Scripts/AI/HALCommentator.cs                   (~150 LoC churn — delete ~80, add ~150)
EDIT  Assets/3 - Scripts/Concert/ConcertStageHub.cs             (~15 LoC added)
EDIT  Assets/StreamingAssets/AI/game_knowledge.md               (~250 lines added)
REWRITE  docs/GAME_OVERVIEW.md                                  (~5000 words)
```

No save-schema changes. No new singletons. No new equippables. No new vendors. No `.asmdef` files exist in this project, so all new scripts join `Assembly-CSharp` automatically.

---

## Task 1: ConcertStageHub — OnStageActivated event + FindActiveStageSpeaker helper

**Why first:** Both downstream changes (the concert waypoint pointer fix in Task 2, and the HAL "concert just went live" trigger in Task 5) depend on these two additions. Bundling them keeps `ConcertStageHub` edits to a single task.

**Files:**
- Modify: `Assets/3 - Scripts/Concert/ConcertStageHub.cs`

### Steps

- [ ] **Step 1.1: Add the `using System;` import if missing**

Open `Assets/3 - Scripts/Concert/ConcertStageHub.cs`. Verify the top of the file imports `System` (needed for `Action<T>`). If only `using System.Collections.Generic;` and Unity imports are present, add `using System;` to the top of the using block.

- [ ] **Step 1.2: Add the public event and the speaker helper method**

Locate `FindActiveStageRoot()` at line 64. Insert these two members *immediately after* it (before the `float _nextCheckTime;` field on line 73):

```csharp
    /// Returns the Transform of the center-stage SpeakerSource of the
    /// currently-active stage. Each stage's `speaker` is the SpeakerSource
    /// child that sits at the geometric centre of the stage (in the active
    /// scene this is the GameObject named `speaker.005`). HALToolDispatcher
    /// prefers this over FindActiveStageRoot for "mark the concert" so the
    /// compass marker lands centre-stage instead of on the stage root pivot.
    /// Null if no stage is active or the active stage has no speaker.
    public Transform FindActiveStageSpeaker()
    {
        for (int i = 0; i < _stages.Count; i++)
        {
            var s = _stages[i];
            if (s != null && s.active && s.speaker != null) return s.speaker.transform;
        }
        return null;
    }

    /// Fires once on each stage's inactive→active transition (a stage just
    /// went live as the planet rotated into its night side, or a LebronLight
    /// stopped suppressing it). Does NOT fire on active→inactive, on force
    /// re-init, or on the first-ever evaluation of a stage. HALCommentator
    /// subscribes to volunteer a "Concert active at <stage>." line.
    public event Action<SpeakerSource> OnStageActivated;
```

- [ ] **Step 1.3: Fire the event from the state-transition site**

Locate `UpdateNightDay` at line 500. The transition block is lines 529-534:

```csharp
            if (force || !s.initialized || isNight != s.active)
            {
                s.active = isNight;
                s.initialized = true;
                ApplyActive(s);
            }
```

Replace with:

```csharp
            if (force || !s.initialized || isNight != s.active)
            {
                bool wasActive  = s.active;
                bool wasInitial = s.initialized;
                s.active = isNight;
                s.initialized = true;
                ApplyActive(s);

                // Fire OnStageActivated only on a real inactive→active
                // transition. Skip the very first init (wasInitial==false)
                // so a stage that loads in already-night doesn't fire on
                // game start, and skip active→inactive (waning) entirely.
                if (wasInitial && !wasActive && s.active)
                {
                    try { OnStageActivated?.Invoke(s.speaker); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
```

- [ ] **Step 1.4: Compile-check in Unity**

Save the file. Switch to Unity. Wait for the script-reload spinner to finish.

Verify: Unity Console shows no compile errors. If you see "The type or namespace `Action<>` could not be found", revisit Step 1.1 (missing `using System;`).

- [ ] **Step 1.5: Play-mode smoke test of the event**

Enter Play mode in the gameplay scene (`Assets/1.6.7.7.7.unity`). In the Hierarchy, locate `[ConcertStageHub]` (DontDestroyOnLoad).

Temporarily add this one-line debug subscriber to verify firing — add to the END of `ConcertStageHub.Start()` (line ~128):

```csharp
        OnStageActivated += sp => Debug.Log($"[ConcertStageHub] OnStageActivated fired for {sp?.gameObject.name}");
```

Re-enter Play mode. Manipulate time-of-day so a stage's night/day flips (cheat code, or just play long enough for natural rotation — Humble Abode rotates on a multi-minute cycle).

Expected: Console line `[ConcertStageHub] OnStageActivated fired for speaker.005` (or similar) appears at the moment of the inactive→active transition. It should NOT fire on Play-mode-start nor on active→inactive.

If verified: **remove the temporary debug subscriber line.**

- [ ] **Step 1.6 (optional): Commit**

Only if user has approved committing for this work item:

```bash
git add "Assets/3 - Scripts/Concert/ConcertStageHub.cs"
git commit -m "concert: add OnStageActivated event + FindActiveStageSpeaker helper

Two additive members on ConcertStageHub. The event fires once on each
real inactive→active transition (not on first-frame init, not on
deactivation), used by HALCommentator's new 'Concert active' volunteered
line. FindActiveStageSpeaker returns the SpeakerSource transform of the
active stage — the centre-stage speaker.005 GameObject — so HAL
waypoints can land centre-stage instead of on the off-pivot stage root."
```

---

## Task 2: Concert waypoint pointer fix in HALToolDispatcher

**Why:** Quickest visible win. Verifies Task 1's `FindActiveStageSpeaker` end-to-end via a feature the user already exercises ("mark the concert"). Tiny edit.

**Files:**
- Modify: `Assets/3 - Scripts/AI/HALToolDispatcher.cs:209-218`

### Steps

- [ ] **Step 2.1: Update the "concert" branch of ResolveTarget**

Open `Assets/3 - Scripts/AI/HALToolDispatcher.cs`. Locate `ResolveTarget` (line ~199). Find the concert branch at lines 209-218:

```csharp
        // 0. Special-case "concert" / "active concert" / "show" / "stage" —
        //    routes through ConcertStageHub.FindActiveStageRoot so the
        //    waypoint resolves to whichever pole-stage is currently
        //    night-side and actually playing.
        if (lower == "concert" || lower == "active concert" ||
            lower == "the concert" || lower == "show" || lower == "stage")
        {
            if (ConcertStageHub.Instance != null)
            {
                var stage = ConcertStageHub.Instance.FindActiveStageRoot();
                if (stage != null) return stage;
            }
            return null; // no active concert right now (daytime on both poles)
        }
```

Replace with:

```csharp
        // 0. Special-case "concert" / "active concert" / "show" / "stage" —
        //    routes through ConcertStageHub. Prefers the centre-stage
        //    speaker transform (speaker.005 in the active scene) so the
        //    compass marker lands centre-stage. Falls back to the stage
        //    root if the active stage has no speaker assigned (defensive
        //    — covers any future stage wired without one).
        if (lower == "concert" || lower == "active concert" ||
            lower == "the concert" || lower == "show" || lower == "stage")
        {
            if (ConcertStageHub.Instance != null)
            {
                var speaker = ConcertStageHub.Instance.FindActiveStageSpeaker();
                if (speaker != null) return speaker;
                var stage = ConcertStageHub.Instance.FindActiveStageRoot();
                if (stage != null) return stage;
            }
            return null; // no active concert right now (daytime on both poles)
        }
```

- [ ] **Step 2.2: Compile-check**

Save. Unity recompiles. Console must be clean.

- [ ] **Step 2.3: Play-mode verify**

Enter Play mode. Wait until a stage is night-side (or use a cheat to skew time). Open the phone (X) → AI Apps → AI. Type "mark the concert" and send.

Expected: A red HAL waypoint appears on the compass strip aimed center-stage. Walking toward it leads the player to the active stage's center, not its edge. Walking within 35 m fires "Target reached: Concert." per the existing `TickProximity` logic.

If both stages are day-side (rare but possible), the AI should reply that no concert is active right now — the resolver returns null, the dispatcher silently drops the waypoint, and the AI's text alone is what the player sees.

- [ ] **Step 2.4 (optional): Commit**

Only if approved:

```bash
git add "Assets/3 - Scripts/AI/HALToolDispatcher.cs"
git commit -m "AI: route 'mark concert' waypoint to centre-stage speaker

Previously the concert waypoint used FindActiveStageRoot, which returns
the stage hierarchy's pivot — slightly off-centre relative to stage
geometry. Now prefers FindActiveStageSpeaker (the SpeakerSource
transform, e.g. speaker.005 in the scene), with the stage root retained
as a defensive fallback."
```

---

## Task 3: FleetTelemetry helper + LLMService prompt injection

**Why:** This is the load-bearing piece for ship-awareness — the prompt now contains live per-ship facts. Tool verbs (Task 4) build on this. Task standalone-testable via "ask the AI about your ship" Play scenarios.

**Files:**
- Create: `Assets/3 - Scripts/AI/FleetTelemetry.cs`
- Modify: `Assets/3 - Scripts/AI/LLMService.cs` (BuildSystemPrompt area)

### Steps

- [ ] **Step 3.1: Create FleetTelemetry.cs with the enumeration helper and block builder**

Create file `Assets/3 - Scripts/AI/FleetTelemetry.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Builds the FLEET STATE block injected into every phone-AI system prompt.
/// Enumerates the scene ship (always Ship 0) and every BoughtShip in the
/// scene, emits one line per ship covering location, motion, attachments,
/// and per-net space-dust buffers. Ships without a satellite dish collapse
/// to a single "OFFLINE" line — the model's signal to refuse telemetry
/// queries about that ship.
///
/// Static helper, not a singleton. Pure read of scene state, no caching.
/// Called once per chat turn (in LLMService.BuildSystemPrompt) — cheap
/// at that cadence.
///
/// EnumerateAllShipsWithNumbers is shared with HALCommentator's
/// PollShipDust so both systems agree on which GameObjects are ships and
/// what number each carries.
/// </summary>
public static class FleetTelemetry
{
    /// Body proximity threshold for "in orbit / on surface" classification.
    /// Mirrors SpaceNet's own orbit-window upper bound (body.radius × 5).
    const float OrbitProximityRadiusMultiplier = 5f;
    /// Below this radius multiplier, a low-velocity ship is "on surface".
    const float SurfaceProximityRadiusMultiplier = 1.05f;
    /// Velocity threshold below which a ship is "idle / at rest" (m/s).
    const float IdleVelocityThreshold = 5f;
    /// Net buffer values at or above this report as "full".
    const int FullDustThreshold = 500;

    /// Yields (ship GameObject, displayed ship number). Scene ship gets 0;
    /// BoughtShip instances use bs.shipNumber. Shared with HALCommentator
    /// so HAL's dust-threshold trigger sees the same ship set as the AI's
    /// system prompt.
    public static IEnumerable<(GameObject go, int number)> EnumerateAllShipsWithNumbers()
    {
        // BoughtShip-tagged ships first (deterministic order: by shipNumber asc).
        var bought = Object.FindObjectsOfType<BoughtShip>();
        if (bought != null && bought.Length > 0)
        {
            System.Array.Sort(bought, (a, b) =>
            {
                int ai = a != null ? a.shipNumber : int.MaxValue;
                int bi = b != null ? b.shipNumber : int.MaxValue;
                return ai.CompareTo(bi);
            });
        }

        // Scene ship: a Ship whose GameObject has no BoughtShip component.
        var allShips = Object.FindObjectsOfType<Ship>();
        Ship sceneShip = null;
        for (int i = 0; i < allShips.Length; i++)
        {
            if (allShips[i] == null) continue;
            if (allShips[i].GetComponent<BoughtShip>() != null) continue;
            sceneShip = allShips[i];
            break;
        }
        if (sceneShip != null) yield return (sceneShip.gameObject, 0);

        if (bought != null)
        {
            for (int i = 0; i < bought.Length; i++)
            {
                if (bought[i] == null) continue;
                yield return (bought[i].gameObject, bought[i].shipNumber);
            }
        }
    }

    /// Renders the FLEET STATE block for inclusion in the system prompt.
    /// Always emits at least the header line, then one row per ship.
    public static string BuildBlock()
    {
        var sb = new StringBuilder();
        sb.Append("FLEET STATE (live ship data — refer to this, do not contradict it):\n");

        int rendered = 0;
        foreach (var (go, n) in EnumerateAllShipsWithNumbers())
        {
            rendered++;
            sb.Append("  ").Append(RenderShipRow(go, n)).Append('\n');
        }
        if (rendered == 0)
        {
            sb.Append("  (no ships present)\n");
        }
        return sb.ToString();
    }

    static string RenderShipRow(GameObject ship, int shipNumber)
    {
        if (ship == null) return $"Ship {shipNumber}: <destroyed>";

        // Dish attachment — drives the OFFLINE gate. ThrusterDetachOnImpact
        // owns the full attachment matrix.
        var tdoi = ship.GetComponent<ThrusterDetachOnImpact>()
                   ?? ship.GetComponentInChildren<ThrusterDetachOnImpact>(true);
        bool dishOn = tdoi == null || IsDishAttached(tdoi); // null defaults to "online" (safe for spawned-ship edge case)
        if (!dishOn)
            return $"Ship {shipNumber}: OFFLINE (no satellite dish — no telemetry available)";

        // Motion + planet proximity.
        var rb = ship.GetComponent<Rigidbody>();
        Vector3 pos    = ship.transform.position;
        Vector3 vel    = rb != null ? rb.velocity : Vector3.zero;
        float speedKms = vel.magnitude / 1000f;

        CelestialBody nearest = FindNearestBody(pos, out float distToCentre);
        string location;
        if (nearest != null && distToCentre <= nearest.radius * SurfaceProximityRadiusMultiplier
            && vel.magnitude < IdleVelocityThreshold)
        {
            location = $"{nearest.bodyName} surface, idle";
        }
        else if (nearest != null && distToCentre <= nearest.radius * OrbitProximityRadiusMultiplier)
        {
            location = $"{nearest.bodyName} orbit, {speedKms:0.00} km/s";
        }
        else if (vel.magnitude < IdleVelocityThreshold)
        {
            location = "deep space, at rest";
        }
        else
        {
            location = $"deep space drifting, {speedKms:0.00} km/s";
        }

        // Dust per net.
        string dust = RenderDustBuffers(ship);

        // Other attachments.
        string solar     = tdoi == null || IsSolarAttached(tdoi)         ? "solar OK"     : "no solar";
        string thrusters = tdoi == null
            ? "thrusters L/R"
            : RenderThrusterState(tdoi);
        string hatch     = ship.GetComponent<Ship>() != null && ship.GetComponent<Ship>().hatchOpen
            ? "hatch open" : "hatch closed";

        return $"Ship {shipNumber}: {location}, dust {dust}, dish OK, {solar}, {thrusters}, {hatch}";
    }

    static string RenderDustBuffers(GameObject ship)
    {
        var nets = ship.GetComponentsInChildren<SpaceNet>(true);
        if (nets == null || nets.Length == 0) return "[—]";
        var sb = new StringBuilder("[");
        int written = 0;
        for (int i = 0; i < nets.Length; i++)
        {
            var n = nets[i];
            if (n == null) continue;
            if (!IsNetAttached(n)) continue;
            int buf = GetNetBuffer(n);
            if (written++ > 0) sb.Append(", ");
            sb.Append("net").Append(written).Append('=');
            sb.Append(buf >= FullDustThreshold ? "full" : buf.ToString());
        }
        if (written == 0) sb.Append("—");
        sb.Append(']');
        return sb.ToString();
    }

    static CelestialBody FindNearestBody(Vector3 pos, out float distToCentre)
    {
        distToCentre = float.MaxValue;
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        CelestialBody best = null;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            float d = Vector3.Distance(pos, b.Position);
            if (d < distToCentre) { distToCentre = d; best = b; }
        }
        return best;
    }

    static string RenderThrusterState(ThrusterDetachOnImpact tdoi)
    {
        bool l = IsLeftAttached(tdoi);
        bool r = IsRightAttached(tdoi);
        if (l && r) return "thrusters L/R";
        if (l)      return "thrusters L only";
        if (r)      return "thrusters R only";
        return "thrusters detached";
    }

    // ── Defensive reflection-style accessors ───────────────────────────
    // ThrusterDetachOnImpact's field/property names for attachment booleans
    // aren't part of the public AI/ namespace contract — keep these
    // wrappers in one place so a future rename of the underlying field
    // only requires touching this file.

    static bool IsDishAttached (ThrusterDetachOnImpact t) => t.IsDishAttached;
    static bool IsSolarAttached(ThrusterDetachOnImpact t) => t.IsSolarAttached;
    static bool IsLeftAttached (ThrusterDetachOnImpact t) => t.IsLeftAttached;
    static bool IsRightAttached(ThrusterDetachOnImpact t) => t.IsRightAttached;

    static bool IsNetAttached(SpaceNet n) => n.IsAttached;
    static int  GetNetBuffer (SpaceNet n) => n.BufferedDust;
}
```

> **Engineer note:** The wrappers `IsDishAttached`/`IsSolarAttached`/`IsLeftAttached`/`IsRightAttached`/`IsAttached`/`BufferedDust` may not exist as public properties on `ThrusterDetachOnImpact` / `SpaceNet` yet — the underlying state is on private/serialized fields. Step 3.2 confirms which surface area is public and either uses the existing public API or adds minimal read-only properties.

- [ ] **Step 3.2: Confirm / add read-only property surface on ThrusterDetachOnImpact and SpaceNet**

Open `Assets/3 - Scripts/Ship/ThrusterDetachOnImpact.cs`. Search for fields `leftAttached`, `rightAttached`, `dishAttached`, `solarAttached`, `dishDetached`, `solarPanelDetached` (per CLAUDE.md these exist). The existing `ApplyAttachment(bool, bool, bool, bool)` (line 279) confirms these as the canonical state. If they are public fields, the wrappers in Step 3.1 can read them directly — replace the wrapper bodies with `t.dishAttached` / `t.solarAttached` / `t.leftAttached` / `t.rightAttached`. If they are private/serialized, add public read-only properties:

```csharp
public bool IsDishAttached  => dishAttached;
public bool IsSolarAttached => solarAttached;
public bool IsLeftAttached  => leftAttached;
public bool IsRightAttached => rightAttached;
```

(Add these directly under the existing field declarations.)

Do the same for `Assets/3 - Scripts/Ship/SpaceNet.cs`: confirm the `attached` and buffer fields are readable. If not public, add:

```csharp
public bool IsAttached    => attached;     // (or the actual underlying field name)
public int  BufferedDust  => Mathf.RoundToInt(buffer);   // (or buffer if already int)
```

> **Decision rule:** if the existing public accessor names differ (`Attached`, `Buffer`, `Dust`, etc.), keep the existing public names and update `FleetTelemetry`'s wrappers in Step 3.1 to match. The goal is the LEAST invasive surface area — prefer matching what's there.

- [ ] **Step 3.3: Inject the FLEET STATE block into the system prompt**

Open `Assets/3 - Scripts/AI/LLMService.cs`. Locate `BuildSystemPrompt` (line 320). Find the existing live-telemetry call at line 452:

```csharp
        // ── Live game state ───────────────────────────────────────────
        // Sampled fresh every turn. The model uses this to answer "where am
        // I" / "how am I doing" / "what should I do next" with actual context
        // rather than canned guidance. Compact — about 60-80 tokens.
        sb.Append(BuildLiveTelemetry()).Append('\n');
```

Add immediately after:

```csharp
        // Per-ship live telemetry. Folded in here so the model treats it as
        // current state, alongside player vitals. Offline ships (no dish)
        // surface as a single OFFLINE line — model is instructed (via the
        // Ship Commands knowledge entry) to refuse telemetry queries for
        // them. Worst-case ~400 tokens for an 8-ship fleet; typical 50-200.
        sb.Append(FleetTelemetry.BuildBlock()).Append('\n');
```

- [ ] **Step 3.4: Update the TOOLS section header to mention dish-gating (preparation for Task 4)**

In the same file, locate the TOOLS block in `BuildSystemPrompt` starting around line 386. Find the line documenting `[map:NAME]` (line ~402) — the placeholder where ship verbs go. We won't add the verb docs yet (Task 4 does that), but the FLEET STATE block needs context. Add this sentence at the END of the existing FLEET-STATE-relevant prompt content, immediately after the `if (retrieved.Count > 0)` block and before the memory section (around line 469):

Actually, do not edit the prompt content here. The Ship Commands knowledge ENTRY block (added in Task 6) carries the dish-refusal instruction in a way that auto-flows through the existing GroundingEntry retrieval pipeline. Skip this micro-edit.

- [ ] **Step 3.5: Compile-check**

Save all touched files. Unity recompiles. Console must be clean. If `Ship`, `BoughtShip`, `ThrusterDetachOnImpact`, `SpaceNet`, `NBodySimulation`, `CelestialBody`, or `Rigidbody` errors appear, verify they're all in `Assembly-CSharp` (they are — no `.asmdef` files in this project).

- [ ] **Step 3.6: Play-mode verify FLEET STATE block content**

Temporarily add a one-shot log in `LLMService.BuildSystemPrompt` to dump the block on every chat turn. Insert immediately after the new `FleetTelemetry.BuildBlock()` append:

```csharp
        Debug.Log("[FleetTelemetry] " + FleetTelemetry.BuildBlock());
```

Enter Play mode. Walk to the ship at StartCabin (Ship 0). Open phone → AI chat → ask "where are my ships?"

Expected Console:
```
[FleetTelemetry] FLEET STATE (live ship data — refer to this, do not contradict it):
  Ship 0: Humble Abode surface, idle, dust [—], dish OK, solar OK, thrusters L/R, hatch closed
```

Pilot the ship into orbit. Send another chat ("how is Ship 0?"). Expected line transitions to `Humble Abode orbit, 0.XX km/s, ...`.

Buy a ship from the Ship Market vendor. Repeat. Expected Console shows two ships:
```
  Ship 0: Humble Abode surface, idle, ...
  Ship 1: Humble Abode surface, idle, ...
```

Damage the bought ship's dish (cheat code or `tdoi.ApplyAttachment(true, true, false, true)`). Expected:
```
  Ship 1: OFFLINE (no satellite dish — no telemetry available)
```

Open AI chat, ask "is Ship 1 orbiting?". Expected: the AI sees the OFFLINE line and (after Task 6's knowledge entry lands) refuses with "Ship 1 is offline. I cannot reach it." For now (before Task 6), the AI may answer based on the OFFLINE line phrasing — that's acceptable mid-plan; Task 6 reinforces the refusal contract.

If verified: **remove the temporary `Debug.Log` line.**

- [ ] **Step 3.7 (optional): Commit**

```bash
git add "Assets/3 - Scripts/AI/FleetTelemetry.cs" "Assets/3 - Scripts/AI/LLMService.cs" "Assets/3 - Scripts/Ship/ThrusterDetachOnImpact.cs" "Assets/3 - Scripts/Ship/SpaceNet.cs"
git commit -m "AI: inject FLEET STATE block into every chat system prompt

New FleetTelemetry static helper enumerates the scene ship (always
shipNumber 0) and every BoughtShip, rendering one line per ship covering
location (orbit / surface / deep space), motion, attachments, dust per
net, and hatch state. Ships without a satellite dish collapse to a
single OFFLINE line — the model's signal to refuse telemetry queries
for that ship.

Read-only IsXAttached / IsAttached / BufferedDust properties added to
ThrusterDetachOnImpact and SpaceNet so FleetTelemetry can read state
without depending on internal field names.

Worst-case ~400 tokens added per chat turn (8-ship fleet); typical
50-200. Comfortable headroom on the 8192-ctx Hermes-3 model."
```

---

## Task 4: markship + showship tool verbs with dish gate

**Why:** Now that the AI sees fleet state, give it nav-action verbs. End-to-end ship awareness: ask "mark Ship 2" → red HAL compass waypoint follows the ship; ask "show Ship 2" → solar-system map opens, camera frames the ship.

**Files:**
- Modify: `Assets/3 - Scripts/AI/HALToolDispatcher.cs`
- Modify: `Assets/3 - Scripts/AI/LLMService.cs` (TOOLS section in BuildSystemPrompt)

### Steps

- [ ] **Step 4.1: Add the verb dispatch cases**

Open `Assets/3 - Scripts/AI/HALToolDispatcher.cs`. Locate the switch in `Execute(verb, arg)` at line 30:

```csharp
            switch (verb.ToLowerInvariant())
            {
                case "waypoint":   HandleWaypoint(arg);   break;
                case "unwaypoint": HandleUnwaypoint(arg); break;
                case "map":        HandleMap(arg);        break;
                default:
                    Debug.LogWarning($"[HALToolDispatcher] Unknown verb '{verb}' (arg='{arg}'). Ignoring.");
                    break;
            }
```

Replace with:

```csharp
            switch (verb.ToLowerInvariant())
            {
                case "waypoint":   HandleWaypoint(arg);   break;
                case "unwaypoint": HandleUnwaypoint(arg); break;
                case "map":        HandleMap(arg);        break;
                case "markship":   HandleMarkShip(arg);   break;
                case "showship":   HandleShowShip(arg);   break;
                default:
                    Debug.LogWarning($"[HALToolDispatcher] Unknown verb '{verb}' (arg='{arg}'). Ignoring.");
                    break;
            }
```

- [ ] **Step 4.2: Add ship resolution helpers and the two handler methods**

In the same file, immediately before the closing brace of the `HALToolDispatcher` class (after `FormatLabel` on line ~323), add:

```csharp
    // ── Ship resolution + handlers ───────────────────────────────────────
    // Used by [markship:N] and [showship:N]. N is a BoughtShip.shipNumber
    // (1..N for bought ships) or 0 for the scene's original ship. Resolves
    // case-insensitive variants like "1", "ship 1", "Ship 1".

    static bool TryResolveShip(string arg, out GameObject shipGO, out int shipNumber)
    {
        shipGO = null;
        shipNumber = -1;
        if (string.IsNullOrWhiteSpace(arg)) return false;

        // Strip leading "ship " prefix if the model didn't follow the docs
        // and emitted e.g. [markship:ship 2] instead of [markship:2].
        string s = arg.Trim();
        if (s.StartsWith("ship ", System.StringComparison.OrdinalIgnoreCase))
            s = s.Substring(5).TrimStart();

        if (!int.TryParse(s, out shipNumber))
        {
            Debug.LogWarning($"[HALToolDispatcher] markship/showship: could not parse ship number from '{arg}'.");
            return false;
        }

        // Use the shared enumerator so we agree with FleetTelemetry on
        // what counts as a ship and what number each carries.
        foreach (var (go, n) in FleetTelemetry.EnumerateAllShipsWithNumbers())
        {
            if (n != shipNumber) continue;
            shipGO = go;
            return true;
        }
        Debug.LogWarning($"[HALToolDispatcher] markship/showship: no ship with shipNumber={shipNumber} in scene.");
        return false;
    }

    /// True iff the ship is online (has its satellite dish attached). False
    /// if the ship has no ThrusterDetachOnImpact (fail-safe: treat as
    /// offline — should not happen in practice).
    static bool IsShipOnline(GameObject shipGO)
    {
        if (shipGO == null) return false;
        var tdoi = shipGO.GetComponent<ThrusterDetachOnImpact>()
                   ?? shipGO.GetComponentInChildren<ThrusterDetachOnImpact>(true);
        if (tdoi == null) return false;
        return tdoi.IsDishAttached;
    }

    static void HandleMarkShip(string arg)
    {
        if (!TryResolveShip(arg, out var shipGO, out int n)) return;
        if (CompassHUD.Instance == null) return;
        if (!IsShipOnline(shipGO))
        {
            Debug.Log($"[HALToolDispatcher] markship:{n} refused — ship is offline (no dish).");
            return;
        }

        string key   = "ship" + n;
        string id    = "hal_" + key;
        string label = $"Ship {n}";
        Transform t  = shipGO.transform;

        // Replace any existing HAL ship marker for this number so we don't
        // leak duplicates on repeated "mark Ship N" requests.
        RemoveTrackedWaypoint(key);

        CompassHUD.Instance.AddWaypoint(
            id,
            () => t != null ? t.position : Vector3.zero,
            label,
            null,                          // default icon
            HALVisuals.EyeRed              // HAL-red tint
        );
        _activeWaypoints.Add(new TrackedWaypoint
        {
            Id = id, Target = t, Label = label, Key = key
        });
        Debug.Log($"[HALToolDispatcher] markship dropped: id={id} target=Ship {n} ({shipGO.name})");
    }

    static void HandleShowShip(string arg)
    {
        if (!TryResolveShip(arg, out var shipGO, out int n)) return;
        if (!IsShipOnline(shipGO))
        {
            Debug.Log($"[HALToolDispatcher] showship:{n} refused — ship is offline (no dish).");
            return;
        }
        if (SolarSystemMapController.Instance == null) return;

        var ship = shipGO.GetComponent<Ship>();
        if (ship == null)
        {
            Debug.LogWarning($"[HALToolDispatcher] showship:{n} — ship GameObject lacks Ship component.");
            return;
        }
        SolarSystemMapController.Instance.OpenMap();
        SolarSystemMapController.Instance.FocusOnShip(ship);
        Debug.Log($"[HALToolDispatcher] showship: opened map and focused on Ship {n}.");
    }
```

- [ ] **Step 4.3: Document the new verbs in the system prompt**

Open `Assets/3 - Scripts/AI/LLMService.cs`. Locate the TOOLS section in `BuildSystemPrompt` (line ~386). Find the line documenting `[map:NAME]` (line ~402):

```csharp
            "  [map:NAME]         Opens the map AND focuses it on planet NAME.\n" +
            "\n" +
```

Replace with:

```csharp
            "  [map:NAME]         Opens the map AND focuses it on planet NAME.\n" +
            "  [markship:N]       Drops a red compass waypoint on Ship N (e.g.\n" +
            "                     [markship:2]). N=0 is the Astronaut's original\n" +
            "                     ship; N=1..M are ships bought from the Ship\n" +
            "                     Market vendor. REQUIRES Ship N to have a\n" +
            "                     satellite dish — if FLEET STATE says Ship N is\n" +
            "                     OFFLINE, do NOT emit this tag and instead reply\n" +
            "                     'Ship N is offline. I cannot reach it.'\n" +
            "  [showship:N]       Opens the map and focuses the camera on Ship N.\n" +
            "                     Same dish gate as [markship:N].\n" +
            "\n" +
```

- [ ] **Step 4.4: Add usage examples to the TOOLS section**

Still in `BuildSystemPrompt`, find the EXAMPLES list (line ~405-417):

```csharp
            "EXAMPLES of correct usage:\n" +
            "  Astronaut: \"show me Humble Abode on the map\"\n" +
            "    → reply with a one-line acknowledgement + [map:Humble Abode]\n" +
            "  Astronaut: \"show me Tev on the map\" / \"mark Tev\" / \"find Tev\"\n" +
            "    → people are not on the planetary map; instead reply briefly +\n" +
            "      [waypoint:Tev]\n" +
            "  Astronaut: \"mark the ship vendor\" / \"where is the goods vendor\"\n" +
            "    → [waypoint:ship vendor] / [waypoint:goods vendor]\n" +
            "  Astronaut: \"mark the active concert\" / \"where is the show\"\n" +
            "    → [waypoint:concert]\n" +
            "  Astronaut: \"remove the Tev marker\" / \"clear Tev\"\n" +
            "    → [unwaypoint:Tev]\n" +
```

Append (before the closing `"\n" +` of the examples block) these new examples:

```csharp
            "  Astronaut: \"mark Ship 2\" / \"where is my second ship\"\n" +
            "    → reply briefly + [markship:2]\n" +
            "  Astronaut: \"show Ship 0 on the map\" / \"point me to my ship on the map\"\n" +
            "    → reply briefly + [showship:0]\n" +
            "  Astronaut: \"is Ship 3 orbiting?\" / \"how much dust on Ship 3?\"\n" +
            "    → answer from the FLEET STATE block. If Ship 3 is OFFLINE,\n" +
            "      reply 'Ship 3 is offline. I cannot reach it.' and emit no tool tag.\n" +
```

- [ ] **Step 4.5: Compile-check**

Save. Unity recompiles. Console must be clean.

- [ ] **Step 4.6: Play-mode verify both verbs and the dish gate**

Enter Play mode. Buy two ships from the Ship Market vendor so the scene has Ship 0, Ship 1, Ship 2.

Test sequence:

1. Open AI chat. Type "mark Ship 1". Expected: red compass waypoint labeled "Ship 1" appears, tracking Ship 1's position. Console: `[HALToolDispatcher] markship dropped: id=hal_ship1 target=Ship 1 (...)`.
2. Type "show Ship 1 on the map". Expected: map opens, camera frames Ship 1, highlight ring on it. Console: `[HALToolDispatcher] showship: opened map and focused on Ship 1.`
3. Close map (M). Damage Ship 2's dish via cheat / by hand: `tdoi.ApplyAttachment(true, true, false, true)` on Ship 2's `ThrusterDetachOnImpact`. Verify Console shows the new FLEET STATE OFFLINE line for Ship 2 on the next chat turn.
4. Type "mark Ship 2". Expected: AI replies "Ship 2 is offline. I cannot reach it." No compass waypoint appears. Console: `[HALToolDispatcher] markship:2 refused — ship is offline (no dish).`
5. Type "is Ship 2 orbiting?". Expected: AI reads the OFFLINE block and refuses.
6. Type "mark Ship 99" (does not exist). Expected: AI either declines in text or emits the tag; the dispatcher logs `no ship with shipNumber=99 in scene` and does nothing visible.
7. Type "remove the Ship 1 marker" or "clear Ship 1". Expected: AI emits `[unwaypoint:ship 1]` — verify the existing unwaypoint path resolves by key. **Note:** the existing unwaypoint matches on `key = arg.Trim().ToLowerInvariant()`, so the AI must emit `[unwaypoint:ship 1]` (lowercase, with the "ship " prefix) to match the markship key `ship1`. Test what the model emits; if it uses `[unwaypoint:Ship 1]` the case-insensitive trim handles it; if it uses `[unwaypoint:1]` it will fail to match (the AI didn't include "ship", so the keys differ). If this fails in practice, add this examples line to BuildSystemPrompt: `"  Astronaut: \"remove the Ship 1 marker\" → [unwaypoint:ship 1]\n"`.

- [ ] **Step 4.7 (optional): Commit**

```bash
git add "Assets/3 - Scripts/AI/HALToolDispatcher.cs" "Assets/3 - Scripts/AI/LLMService.cs"
git commit -m "AI: add [markship:N] and [showship:N] verbs with dish gate

Two new tool verbs the AI can emit inline. [markship:N] drops a red
HAL compass waypoint on Ship N that tracks the ship's position;
[showship:N] opens the solar-system map and focuses it on Ship N
(reusing the existing FocusOnShip(Ship) entry point).

Both verbs check ThrusterDetachOnImpact.IsDishAttached and refuse
silently if the ship is offline. The system prompt is updated to tell
the model to refuse in text instead of emitting the tag for offline
ships ('Ship N is offline. I cannot reach it.').

Ship resolution shares FleetTelemetry.EnumerateAllShipsWithNumbers so
both systems agree on the ship set."
```

---

## Task 5: HALCommentator overhaul — atmosphere planet name, kill ambient, add substantive triggers

**Why:** Removes 90% filler the user complained about. Replaces with vitals / ship-dust / orbit / concert lines that mean something. Atmosphere lines now include the planet name.

**Files:**
- Modify: `Assets/3 - Scripts/AI/HALCommentator.cs`

### Steps

- [ ] **Step 5.1: Update PollAtmosphere to include the planet name**

Open `Assets/3 - Scripts/AI/HALCommentator.cs`. Locate `PollAtmosphere` (line 474). Find the volunteered line at line 504:

```csharp
        _inAtmosphere = inAtmoNow;
        Volunteer(inAtmoNow
            ? "Entering atmosphere, Astronaut. Descent in progress."
            : "Leaving atmosphere, Astronaut. Vacuum confirmed.");
```

Replace with:

```csharp
        _inAtmosphere = inAtmoNow;
        string planetName = !string.IsNullOrEmpty(body.bodyName) ? body.bodyName : "this body";
        Volunteer(inAtmoNow
            ? $"Entering {planetName} atmosphere, Astronaut. Descent in progress."
            : $"Leaving {planetName} atmosphere, Astronaut. Vacuum confirmed.");
```

- [ ] **Step 5.2: Delete the ambient-idle pool and its plumbing**

In the same file:

1. **Delete the constants** at lines 46-50:
   ```csharp
       float _nextAmbientCheck;
       int   _lastAmbientIdx = -1;
       const float AmbientIdleThresholdSeconds = 30f;
       const float AmbientPollIntervalSeconds  = 10f;
       const float AmbientFireChance           = 0.45f;
   ```
   Remove all five lines.

2. **Delete `TryAmbientObservation`** (lines ~231-242) — the entire method.

3. **Delete `PickAmbientLine`** (lines ~315-329) — the entire method.

4. **Delete the three static pools** `AmbientLinesPhase1`, `AmbientLinesPhase2`, `AmbientLinesPhase3` (lines ~280-313) — all three arrays.

5. **Delete the call site** in `Update` (line ~203):
   ```csharp
           TryAmbientObservation();
   ```
   Remove that line.

After these deletions, save and confirm the file still compiles (Unity will recompile on save).

- [ ] **Step 5.3: Add vitals-threshold tracking fields**

In the same file, find the field block near the top (around line 86-111). Add after the existing `_flagTrackers` field:

```csharp
    // ── Vitals threshold tracking ─────────────────────────────────────
    // Each metric has an integer "stage" — 0 = above first threshold,
    // 1/2/3 = crossed progressively lower thresholds. Fires the line on
    // a downward stage transition. Hysteresis (5 percentage points above
    // the previous threshold's lower edge) prevents one-pixel flapping
    // at boundary.
    int  _hungerStage;
    int  _thirstStage;
    int  _healthStage;
    bool _shipPowerLowFired;

    // Per-ship dust threshold tracking (shipNumber → highest stage fired).
    // 0 = under 100, 1 = >=100, 2 = >=250, 3 = full (>=500). Decrements
    // when buffer drops below the previous threshold's lower edge.
    readonly Dictionary<int, int> _shipDustStage = new Dictionary<int, int>();

    // Per-ship orbit-announce dedupe.
    readonly HashSet<int> _shipOrbitAnnounced = new HashSet<int>();
```

- [ ] **Step 5.4: Add the substantive trigger polls**

In the same file, locate `PollEarlyGameFlags` (line ~534). Immediately after it, add four new methods:

```csharp
    // ── Substantive vitals triggers ────────────────────────────────────
    // Hunger / thirst / health / ship power. Crosses thresholds downward
    // → fire a one-shot line per crossing. Reset on upward hysteresis.

    void PollVitals()
    {
        var rm = ResourceManager.Instance;
        if (rm == null) return;
        float h  = rm.HungerPercent    * 100f;
        float t  = rm.ThirstPercent    * 100f;
        float hp = rm.HealthPercent    * 100f;
        float sp = rm.ShipPowerPercent * 100f;

        // Hunger
        int hStage = h <= 10f ? 3 : h <= 25f ? 2 : h <= 50f ? 1 : 0;
        if (hStage > _hungerStage)
        {
            int pct = Mathf.RoundToInt(h);
            Volunteer($"Hunger at {pct}%. Seek food intake.");
        }
        // Hysteresis: decrement only if h rises 5+ points above the
        // current threshold's lower edge.
        if (_hungerStage == 1 && h >= 55f) _hungerStage = 0;
        else if (_hungerStage == 2 && h >= 30f) _hungerStage = 1;
        else if (_hungerStage == 3 && h >= 15f) _hungerStage = 2;
        else _hungerStage = Mathf.Max(_hungerStage, hStage);

        // Thirst
        int tStage = t <= 10f ? 3 : t <= 25f ? 2 : t <= 50f ? 1 : 0;
        if (tStage > _thirstStage)
        {
            int pct = Mathf.RoundToInt(t);
            Volunteer($"Thirst at {pct}%. Hydration recommended.");
        }
        if (_thirstStage == 1 && t >= 55f) _thirstStage = 0;
        else if (_thirstStage == 2 && t >= 30f) _thirstStage = 1;
        else if (_thirstStage == 3 && t >= 15f) _thirstStage = 2;
        else _thirstStage = Mathf.Max(_thirstStage, tStage);

        // Health (only two thresholds — 50 and 25)
        int healthStage = hp <= 25f ? 2 : hp <= 50f ? 1 : 0;
        if (healthStage > _healthStage)
        {
            int pct = Mathf.RoundToInt(hp);
            Volunteer($"Health at {pct}%, Astronaut. Take cover.");
        }
        if (_healthStage == 1 && hp >= 55f) _healthStage = 0;
        else if (_healthStage == 2 && hp >= 30f) _healthStage = 1;
        else _healthStage = Mathf.Max(_healthStage, healthStage);

        // Ship power — single threshold at 25 with hysteresis at 40
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
    }

    // ── Per-ship dust threshold trigger ───────────────────────────────
    // Volunteers "Ship N has collected NN dust." or "Ship N net is full."
    // on the upward crossing of 100 / 250 / 500. Decrements stage on the
    // downward crossing (player drained the net) so the next fill re-fires.

    void PollShipDust()
    {
        foreach (var pair in FleetTelemetry.EnumerateAllShipsWithNumbers())
        {
            var shipGO = pair.go;
            int n      = pair.number;
            if (shipGO == null) continue;

            int total = SumNetBuffers(shipGO);
            int prev  = _shipDustStage.TryGetValue(n, out var v) ? v : 0;
            int next  = total >= 500 ? 3
                       : total >= 250 ? 2
                       : total >= 100 ? 1
                       : 0;

            if (next > prev)
            {
                string line = next == 3
                    ? $"Ship {n} net is full."
                    : $"Ship {n} has collected {total} dust.";
                Volunteer(line);
            }
            _shipDustStage[n] = next;
        }
    }

    static int SumNetBuffers(GameObject ship)
    {
        var nets = ship.GetComponentsInChildren<SpaceNet>(true);
        if (nets == null) return 0;
        int total = 0;
        for (int i = 0; i < nets.Length; i++)
        {
            var net = nets[i];
            if (net == null || !net.IsAttached) continue;
            total += net.BufferedDust;
        }
        return total;
    }

    // ── Per-ship orbit-stabilized trigger ─────────────────────────────
    // Fires the first time each ship reaches a stable orbit (existing
    // Ship.IsOrbitMatched flag). Resets when the ship leaves the planet's
    // proximity so a re-orbit later fires again.

    void PollShipOrbit()
    {
        foreach (var pair in FleetTelemetry.EnumerateAllShipsWithNumbers())
        {
            var shipGO = pair.go;
            int n      = pair.number;
            if (shipGO == null) continue;

            var ship = shipGO.GetComponent<Ship>();
            if (ship == null) continue;

            // Determine nearest body proximity to decide "still in this
            // orbit episode" vs "left the planet."
            Vector3 pos = shipGO.transform.position;
            CelestialBody nearest = null;
            float bestDist = float.MaxValue;
            var bodies = NBodySimulation.Bodies;
            if (bodies != null)
            {
                for (int i = 0; i < bodies.Length; i++)
                {
                    var b = bodies[i];
                    if (b == null) continue;
                    float d = Vector3.Distance(pos, b.Position);
                    if (d < bestDist) { bestDist = d; nearest = b; }
                }
            }
            bool inProximity = nearest != null && bestDist <= nearest.radius * 5f;

            if (!inProximity)
            {
                // Left the planet — eligible to re-announce next time.
                _shipOrbitAnnounced.Remove(n);
                continue;
            }

            if (ship.IsOrbitMatched && !_shipOrbitAnnounced.Contains(n))
            {
                _shipOrbitAnnounced.Add(n);
                Volunteer($"Ship {n} has stabilized orbit around {nearest.bodyName}.");
            }
        }
    }
```

> **Note:** `Ship.IsOrbitMatched` is referenced by the existing `PollOrbitMatch` method (line ~440), so this property is already public on `Ship`. If it isn't, adjust to whatever the public accessor is in the file. Existing usage at line 444 (`shipMatched = ship != null && ship.IsOrbitMatched`) confirms it's accessible.

- [ ] **Step 5.5: Wire the new polls into Update**

In the same file, locate `Update` (line ~183). Find the poll block:

```csharp
        _pollTimer -= Time.unscaledDeltaTime;
        if (_pollTimer <= 0f)
        {
            PollPlanetChange();
            PollEarlyGameFlags();
            PollAtmosphere();
            _pollTimer = PollIntervalSeconds;
        }
```

Replace with:

```csharp
        _pollTimer -= Time.unscaledDeltaTime;
        if (_pollTimer <= 0f)
        {
            PollPlanetChange();
            PollEarlyGameFlags();
            PollAtmosphere();
            PollVitals();
            PollShipDust();
            PollShipOrbit();
            _pollTimer = PollIntervalSeconds;
        }
```

- [ ] **Step 5.6: Subscribe to ConcertStageHub.OnStageActivated**

In the same file, locate `TrySubscribe` (line ~157). Add a subscription:

```csharp
    void TrySubscribe()
    {
        if (_subscribed) return;
        if (ResourceManager.Instance == null) return; // wait for it to exist
        ResourceManager.Instance.OnDeath += HandlePlayerDeath;
        EnemyController.OnAnyEnemyDeath  += HandleEnemyKill;
        if (KillstreakManager.Instance != null)
            KillstreakManager.Instance.OnKillRegistered += HandleKillstreak;
        if (GameKnowledgeBase.Instance != null)
            GameKnowledgeBase.Instance.OnPhaseChanged += HandlePhaseChanged;
        if (ConcertStageHub.Instance != null)                            // NEW
            ConcertStageHub.Instance.OnStageActivated += HandleConcertActivated; // NEW
        _subscribed = true;
    }
```

And matching unsubscribe in `Unsubscribe` (line ~170):

```csharp
    void Unsubscribe()
    {
        if (!_subscribed) return;
        if (ResourceManager.Instance != null)
            ResourceManager.Instance.OnDeath -= HandlePlayerDeath;
        EnemyController.OnAnyEnemyDeath -= HandleEnemyKill;
        if (KillstreakManager.Instance != null)
            KillstreakManager.Instance.OnKillRegistered -= HandleKillstreak;
        if (GameKnowledgeBase.Instance != null)
            GameKnowledgeBase.Instance.OnPhaseChanged -= HandlePhaseChanged;
        if (ConcertStageHub.Instance != null)                              // NEW
            ConcertStageHub.Instance.OnStageActivated -= HandleConcertActivated; // NEW
        _subscribed = false;
    }
```

Add the handler method (place it near the other Handle* methods, e.g., after `HandlePhaseChanged` at line ~418):

```csharp
    void HandleConcertActivated(SpeakerSource speaker)
    {
        if (speaker == null) { Volunteer("Concert active."); return; }
        Volunteer($"Concert active at {speaker.gameObject.name}.");
    }
```

- [ ] **Step 5.7: Compile-check**

Save. Unity recompiles. Console must be clean. The deletes from Step 5.2 should not leave dangling references — if the compiler flags any, double-check the field/method names match the deletions.

- [ ] **Step 5.8: Play-mode verify**

Enter Play mode in the gameplay scene.

Verifications:

1. **No more ambient filler.** Stand idle in StartCabin for 5 minutes. Confirm NO "I am listening" / "All systems nominal" / "Observing" lines fire. The HUD strip should remain silent unless something happens.

2. **Atmosphere with planet name.** Pilot ship from surface to orbit. Expected line: `"Leaving Humble Abode atmosphere, Astronaut. Vacuum confirmed."`. Fly toward Cyclops and descend. Expected: `"Entering Cyclops atmosphere, Astronaut. Descent in progress."`.

3. **Hunger trigger.** Cheat hunger down to 49%. Expected: `"Hunger at 49%. Seek food intake."` fires once. Drop to 24%: `"Hunger at 24%. Seek food intake."`. Restore hunger to 56%+ via cooked fish, then drain again to 49%: line re-fires (hysteresis worked).

4. **Thirst / Health triggers.** Same shape. Health: trigger by taking enemy damage.

5. **Ship power low.** Drain ship power via prolonged flight or cheat. Expected at 25%: `"Ship power at 25%. Solar panel exposure recommended."`. Charge solar above 40%, drain again — line re-fires.

6. **Ship dust threshold.** Park Ship 0 in orbit with a net attached. Watch the buffer accumulate. At 100: `"Ship 0 has collected 100 dust."`. At 250: `"Ship 0 has collected 250 dust."`. At 500: `"Ship 0 net is full."`. Drain the net to 0, let it refill to 100: line re-fires.

7. **Ship orbit stabilized.** Pilot Ship 0 to a planet, press O to circularize. Once `IsOrbitMatched` holds true, expected: `"Ship 0 has stabilized orbit around Humble Abode."`. Leave the planet (fly to deep space). Approach a different planet, circularize again: line re-fires for the new planet.

8. **Concert activation.** Wait for / cheat-skip to a stage going night-side. Expected at the inactive→active transition: `"Concert active at speaker.005."` (or whichever speaker name is on that stage). Should NOT fire on Play-mode start, even if a stage is already night-side.

9. **All previously-working triggers still fire.** Spot-check death (`ResourceManager.SetHealth(-1)` or stand in enemy fire), killstreak 5, planet visit (fly to Constant Companion), enemy proximity (let a Toy10 spawn within 50 m). Each should fire its existing line.

- [ ] **Step 5.9 (optional): Commit**

```bash
git add "Assets/3 - Scripts/AI/HALCommentator.cs"
git commit -m "HAL: replace stock ambient pool with substantive triggers

Deletes the three phase-shaded ambient idle pools (~30 stock lines like
'I am listening', 'All systems nominal') and their poll machinery.
HAL is now silent unless something substantive changes.

Adds polled triggers on the existing 0.5s cadence:
  - Vitals thresholds (hunger/thirst at 50/25/10%, health at 50/25%)
    with 5-point hysteresis so they don't flap at the boundary
  - Ship power below 25% with hysteresis at 40%
  - Per-ship dust buffer at 100 / 250 / full (500), per-ship dedupe
  - Per-ship orbit-stabilized (resets when the ship leaves proximity)
  - Concert activation via new ConcertStageHub.OnStageActivated event

Atmosphere transition lines now include the planet name —
'Entering Humble Abode atmosphere, Astronaut.'"
```

---

## Task 6: Knowledge file audit + new entries + planet lore

**Files:**
- Modify: `Assets/StreamingAssets/AI/game_knowledge.md`

The file is 855 lines. This task is editing — read every section once for accuracy, then add the six new ENTRY blocks and the planet-lore expansions.

### Steps

- [ ] **Step 6.1: Audit pass — spot-check existing entries against current code**

Open `Assets/StreamingAssets/AI/game_knowledge.md` and re-read these specific entries against current code reality:

| Entry | What to verify |
|---|---|
| `## ENTRY: Controls Cheat Sheet` (line ~405) | Flashlight is **E for 50% → E for full → E for off** (3-state, not 2). Confirm by reading `Assets/3 - Scripts/Player/PlayerFlashlight.cs`. |
| `## ENTRY: How to Drink` (line ~273) | Refill is **hold RMB underwater**. Confirm in `WaterBottleController`. |
| `## ENTRY: How to Fish` (line ~313) | Cast / wait for strike / reel mechanics. Confirm in `FishingRodController`. |
| `## ENTRY: How to Build a Cabin` (line ~286) | Path is **phone → Building app → place cabin**. Confirm `PlayerPhoneUI` exposes the building app. |
| `## ENTRY: How to Build (general)` (line ~299) | Build menu key (N), LMB place, RMB rotate. |
| `## ENTRY: How to Cook` (line ~327) | Bonfire → add fish → 10 s timer → eat. Cooked: 20 / 35 / 60 hunger restored per rarity. |
| `## ENTRY: Space Dust` (line ~801) | Mention nets, ship-orbit collection, F-to-drain. (Will be supplemented by the new dedicated Space Nets entry; keep this entry as the high-level overview, prune any redundancy.) |
| `## ENTRY: Where to Buy Things` (line ~341) | Alien7 = goods, ShipMarket = ships+parts, Alien4 = fish vendor, GuitarShopNPC = guitar. |
| `## ENTRY: How to Use the Phone` (line ~391) | X opens phone, 4 pages (Apps / AI Apps / Vitals / Quests). |
| `## ENTRY: Jetpack` (line ~769) | Hold space; press O to circularize while in orbit. |

For any factual discrepancy, edit the body to match current code. Do not rewrite voice or restructure — minimal corrections only.

- [ ] **Step 6.2: Add Tev Intro entry**

Open `Assets/StreamingAssets/AI/game_knowledge.md`. Locate the `## ENTRY: The Crash` block (line ~836). Add this new block IMMEDIATELY AFTER it:

```markdown
## ENTRY: Tev Intro
mode: grounding
phase: all
keywords: tev, crash, woke, wake, cabin, story, beginning, start, intro, who is tev
---
You crash-landed on the planet Humble Abode. Your ship was destroyed in
the descent. An alien named Tev — the closest thing to a guide on this
world — found you and brought you to his cabin. You woke up there. Tev
nursed you back to operational condition. He directs the early arc:
read his note, pick up the fishing rod he provided, catch fish, cook
one, drink some water, return to him. After that he gives you an axe
and a pistol, asks you to build your own cabin, and tells you where the
village is.
```

- [ ] **Step 6.3: Add Space Nets entry**

In the same file, locate `## ENTRY: Space Dust` (line ~801). Add this block IMMEDIATELY AFTER it:

```markdown
## ENTRY: Space Nets
mode: grounding
phase: all
keywords: net, nets, space net, space nets, collect, gather, harvest, attach, mount
---
Space nets are accessories that attach to a ship and passively buffer
space dust while the ship is parked in orbit — proximity to a body,
not on the surface, not being piloted. Closer orbit altitudes collect
faster. Each net caps at 500 dust. To drain a net into your personal
inventory, stand inside its trigger collider and press F. A ship can
carry up to two nets (left mount and right mount). Buy nets and net
pickups from the Ship Market vendor. Without nets attached, a ship in
orbit collects nothing.
```

- [ ] **Step 6.4: Add Concerts as Rebel Hotspots entry**

In the same file, locate `## ENTRY: Concerts` (line ~784). Add this block IMMEDIATELY AFTER it (do NOT replace the existing Concerts entry — this is supplementary):

```markdown
## ENTRY: Concerts as Rebel Hotspots
mode: grounding
phase: all
keywords: rebel, rebels, rebellion, resistance, hotspot, gathering, music, concert visit
---
Two stages on opposite poles of Humble Abode run shows gated by the
day/night cycle — only the night-side stage is active at any moment.
Rumor among the local aliens says these concerts are also cover for
rebel activity: people who do not want to be observed gather where the
music is loud enough to make observation difficult. I recommend
attending — both for the spectacle and for the possibility of meeting
useful people. Ask me to mark the active concert and I will drop a
waypoint to the centre of the playing stage.
```

- [ ] **Step 6.5: Add AI Self-Capabilities entry**

In the same file, locate `## ENTRY: AI Self-Deny` (line ~230). Add this block IMMEDIATELY AFTER it:

```markdown
## ENTRY: AI Self-Capabilities
mode: verbatim
phase: all
intent: what can you do, what do you do, help me, how do you work, what are you for, your capabilities, your abilities, what can you help with
---
I can do the following, Astronaut:

• Drop a compass waypoint on a person, vendor, landmark, planet, or
  the active concert. Ask "mark Tev", "where is the ship vendor",
  "find the goods vendor".
• Open the solar-system map and focus it on a planet. Ask
  "show me Cyclops on the map".
• Mark or show any of your ships on the compass or the map. Ask
  "mark Ship 2" or "show Ship 0 on the map". A ship without a working
  satellite dish is offline to me — I cannot reach it. Buy a
  replacement dish from the Ship Market vendor.
• Answer questions about your fleet — orbit status, current planet,
  speed, dust per net. Only for ships with a working dish.
• Recall what I know about the eight bodies of this system, the
  aliens, the vendors, the equippables, and the early-game arc.
• Notice when your vitals are low or your ship nets fill, and tell
  you about it without being asked.

If you want to know how to do something specific — fishing, cooking,
flight, the flashlight, the building menu — just ask.
```

- [ ] **Step 6.6: Add Ship Commands entry**

In the same file, immediately after the new `## ENTRY: AI Self-Capabilities` block, add:

```markdown
## ENTRY: Ship Commands
mode: grounding
phase: all
keywords: ship, ships, mark ship, show ship, fleet, my ship, ship 0, ship 1, ship 2, ship 3, ship 4, dish, satellite, offline, online, telemetry, orbit, my fleet
---
Each ship you own has a number. Ship 0 is your original crash-survivor
ship. Ship 1, 2, 3… are ones you bought from the Ship Market vendor or
spawned via the debug menu. You can ask me:
  • "Mark Ship N"          — drops a red compass waypoint on it.
  • "Show Ship N on the map" — opens the map and focuses on it.
  • "Is Ship N orbiting?" / "What planet is Ship N around?"
  • "How fast is Ship N going?"
  • "How much dust does Ship N have?"
I have telemetry for a ship ONLY when its satellite dish is attached.
If a ship has lost its dish — visible in the FLEET STATE as
"Ship N: OFFLINE" — I cannot reach it. Reply "Ship N is offline.
I cannot reach it." for any request about that ship; do not guess
its state, do not drop a waypoint for it, do not open the map on it.
Buy a replacement dish from the Ship Market vendor and the link
restores immediately.
```

- [ ] **Step 6.7: Expand planet lore (placeholders)**

In the same file, append a placeholder lore paragraph to each of these existing planet entries. Mark each addition with `(placeholder — replace with real lore later)` so future replacement is obvious.

For **`## ENTRY: Fiery Twin`** (line ~480), append at the bottom of its body:

```markdown

(placeholder — replace with real lore later)
The Fiery Twin is the system's inner furnace. Its surface bakes at a
hundred-plus degrees and its storms can flense a hull. No permanent
settlement has held here in living memory. If anything is still on
this world, it has reason to hide from the heat and from anyone who
might come looking.
```

For **`## ENTRY: Icey Twin`** (line ~495), append:

```markdown

(placeholder — replace with real lore later)
The Icey Twin is the Fiery Twin's mirror — locked in a slow orbital
ballet, perpetually below freezing, its plains a single sheet of
glassy ice. The atmosphere is thin enough that the stars are sharp
even from the surface. A few research outposts persist; their
inhabitants are unsettlingly quiet.
```

For **`## ENTRY: Constant Companion`** (line ~510), append:

```markdown

(placeholder — replace with real lore later)
The Constant Companion is Humble Abode's moon — close enough that on
clear nights it fills half the sky. Its low gravity made it the
natural site for the MoonBase, a small set of pressurised modules
where supply runs from Humble Abode used to dock. The base is mostly
empty now.
```

For **`## ENTRY: Cyclops`** (line ~520), append:

```markdown

(placeholder — replace with real lore later)
Cyclops is the system's big mid-belt planet — a banded giant with
one enormous storm vortex permanently anchored at its equator. The
aliens call it the Eye. Mining platforms float in its upper
atmosphere, harvesting whatever the storm coughs up. Nobody who
lives on Cyclops considers themselves a permanent resident.
```

For **`## ENTRY: Tumbling Bean`** (line ~535), append:

```markdown

(placeholder — replace with real lore later)
The Tumbling Bean is an eccentric rock, oblong and rotating on no
particular axis. Its surface is a graveyard of failed landing
attempts. The locals say it does not want company. Anyone who lands
successfully tends to find the trip back harder than they expected.
```

For **`## ENTRY: Watchful Eye`** (line ~549), append:

```markdown

(placeholder — replace with real lore later)
The Watchful Eye is the outer planet — quiet, cold, and the closest
thing this system has to a deep-space outpost. Its surface
infrastructure is older than anyone alive remembers building, and
nobody is sure who maintains it now. The name suggests the obvious
inference. The Accord does not patrol here.
```

(Sun and Humble Abode already have substantive entries — skip them.)

- [ ] **Step 6.8: Verify the file parses**

The knowledge file uses a custom format. Save and switch to Unity. In the editor, the file is re-parsed automatically on save (per `GameKnowledgeBase.EnsureLoadedAndFresh` editor branch). Check Console for any warnings:
- `Malformed block header (no ':')` — a `## PERSONA:` or `## ENTRY:` header is missing its `:`.
- `Verbatim ENTRY 'X' has no intent` — a verbatim block needs an `intent:` line.
- `Grounding ENTRY 'X' has no keywords` — a grounding block needs a `keywords:` line.
- `Unknown mode 'X'` — `mode:` must be `core`, `grounding`, or `verbatim`.

If a warning fires, find the offending block and correct.

- [ ] **Step 6.9: Play-mode smoke test**

Enter Play mode. Open the AI chat. Type each of these and verify the response uses the new content:

| Prompt | Expected behavior |
|---|---|
| "what can you do" | Verbatim hit — exact content of the new Self-Capabilities entry. Console: `[LLMService] Verbatim hit: ...` |
| "who is Tev" | Retrieved entry includes the Tev Intro body; AI summarises. |
| "how do space nets work" | Retrieved entry includes Space Nets body; AI summarises. |
| "are concerts rebel hotspots" | Retrieved entry includes Concerts-as-Hotspots body; AI confirms. |
| "tell me about Cyclops" | Retrieved entry includes Cyclops + the placeholder lore. |
| "mark Ship 1" (with Ship 1 offline) | AI replies "Ship 1 is offline. I cannot reach it." (Ship Commands entry reinforces refusal.) |

- [ ] **Step 6.10 (optional): Commit**

```bash
git add "Assets/StreamingAssets/AI/game_knowledge.md"
git commit -m "AI knowledge: audit + six new entries + planet lore placeholders

Adds Tev Intro, Space Nets, Concerts as Rebel Hotspots, AI
Self-Capabilities (verbatim — answers 'what can you do'), and Ship
Commands (instructs the model on dish-gating and offline refusal).

Expands Fiery Twin / Icey Twin / Constant Companion / Cyclops /
Tumbling Bean / Watchful Eye with placeholder lore — each tagged
'(placeholder — replace with real lore later)' so the real canon
swap is obvious.

Spot-corrects any drift between existing entries and current code
(flashlight 3-state, water-bottle hold-RMB, phone-app build flow).
No structural rewrites."
```

---

## Task 7: docs/GAME_OVERVIEW.md rewrite

**Why:** User asked for a current short / medium / long overview.

**Files:**
- Rewrite: `docs/GAME_OVERVIEW.md`

### Steps

- [ ] **Step 7.1: Read the current file**

Read `docs/GAME_OVERVIEW.md` (128 lines). Note what sections exist so the rewrite covers everything currently mentioned plus the systems added since.

- [ ] **Step 7.2: Read CLAUDE.md as the source of truth**

Read `CLAUDE.md` end-to-end. It is the definitive reference for every system, NPC, save-tracked field, and design decision. The Long section of the rewrite is essentially CLAUDE.md restructured into narrative prose (not a reference table) and audience-shifted to "new contributor" rather than "future Claude."

- [ ] **Step 7.3: Rewrite docs/GAME_OVERVIEW.md**

Replace the entire contents of `docs/GAME_OVERVIEW.md` with a three-section document. Structure exactly:

```markdown
# Game Overview

A third-person space-exploration / survival / soft-combat game inspired by Outer Wilds, built in Unity 2022.3. Three sections at three lengths.

---

## Short (~150 words)

[Single paragraph elevator pitch. Cover: crash-landed astronaut, Tev's
cabin, procedurally orbiting solar system (8 bodies), survival vitals
(hunger / thirst / health / ship power), fishing + cooking + raw-fish
trip, building loop, ship damage / reassembly / multi-ship fleet,
soft combat with axe + pistol, space-dust economy via orbital nets,
night-gated concerts on opposite poles of the home planet, a phone-AI
companion with a phase-shifting persona, and the long arc of figuring
out who/what the AI is.]

---

## Medium (~800 words / ~1 page)

### Premise
[2 paragraphs — what the game is, where it starts, what the player does
moment-to-moment.]

### Systems
[One paragraph each — gravity / floating origin, survival, fishing &
cooking & trip effect, water, building, ship & damage states, fleet &
ship market, space dust & nets, combat & ragdolls & killstreak,
concerts, NPCs & dialogue, phone & phone AI, atmosphere & rendering.]

### Story
[1 paragraph — early-game progression beats from CLAUDE.md's TevDialogue
section, plus the long-arc AI character work.]

---

## Long (~3000-5000 words / comprehensive)

### 1. Setting & Solar System
[Eight bodies named and characterised. Sun, Fiery Twin, Icey Twin,
Humble Abode (where the player crash-lands), Constant Companion (moon
with MoonBase), Cyclops, Tumbling Bean, Watchful Eye. 2-3 sentences
each.]

### 2. The Player & The Ship
[PlayerController gravity alignment, PlayerController.OnLanded, Ship
canFly gating, Ship damage states (Full/MissingLeft/MissingRight/
NoThrusters), Ship damage tracking 4 attachment fields, ship hatch,
pilot/exit flow, ship name HUD, multi-ship fleet via BoughtShip and
ShipMarketNPC.]

### 3. Gravity & Floating Origin
[NBodySimulation at 100Hz, every CelestialBody is a GravityObject,
EndlessManager floating-origin shift past 1000m with two-stage
interpolation pipeline, deterministic planet motion, GravityObjectSimple
for pickups.]

### 4. Survival Vitals
[ResourceManager — hunger, thirst, health, ship power. Drain rates,
health-faster-while-hungry/thirsty, death-and-respawn, total-deaths
tracking (drives "Astronaut Number N" naming), ResourceHUD vs
VitalsHUD vs WaterFillHUD.]

### 5. Fishing & Cooking & the Trip
[Cassette → fishing rod trade with Alien3. FishingRodController + Bobber
mechanics. Three rarity tiers (Common/Uncommon/Rare). FishingdexManager
+ FishInventory recording catches. FishMarketNPC SellPanel. BonfireInteraction
CookPanel — 10s timer, 20/35/60 hunger restore. RawFishTripController
kaleidoscope trip on raw-eat / mushroom-eat, fade in / early / crossfade
/ late / fade out envelope, runs on unscaled time.]

### 6. Water
[WaterBottleController — RMB hold underwater to refill, drink to restore
thirst. Equippable on hotbar. Bone-animation arm raise in LateUpdate is
the only place arm bones are touched.]

### 7. Building Loop
[BuildMenuUI key N, BuildableEntry recipes, GhostPlacement preview,
LMB place / RMB rotate, "_Placed" naming convention, parented to
CelestialBody for save-system find/destroy/restore. BuildMenuLock
gates which blueprints appear via unlockedNames list — story unlocks
progressively.]

### 8. Ship Damage & Reassembly
[ShipDamageManager prefab swap, ThrusterDetachOnImpact spawning loose
parts, PlayerPickup + ThrusterMount + ShipReassembly reattach flow.
The four attachment fields. Loose parts are GravityObjectSimple,
registered with EndlessManager, marked with PickupMarker for the UI.]

### 9. Multi-Ship Fleet & Ship Market
[BoughtShip marker, shipNumber assigned at purchase (stable across
saves), ShopItem assets, ShipMarketNPC sells whole ships and parts.
SpaceNetMount/Controller/Pickup for nets. PostGreetingChoicePanel for
Buy/Leave.]

### 10. Space Dust & Space Nets
[SpaceDustInventory singleton. SpaceNet orbit-buffering logic — proximity
window, altitude multiplier, 500 cap. F-to-drain trigger. SpaceDustSellUI
for selling to NPCs. SpaceDustSave per-ship in save schema.]

### 11. Combat & Ragdolls
[EnemyController per-enemy + EnemySpawner singleton. AxeController vs
PistolController damage models. EnemyController.ActiveEnemies static
list. EnemyKind (Regular/Elite). SpitProjectile ranged attack with
10s tree-episode arming. EnemyRagdollBuilder and AlienRagdollBuilder
runtime ragdoll construction. RagdollGravity per-bone. KillstreakManager
+ HUD + SlowmoOnKill. TorchAura 15m enemy-block + 20dps damage. Enemies
also damage AlienNPCDamageable so they don't get permanently stuck on NPCs.
isStoryImpactful flag for save-tracked NPC kills.]

### 12. NPC Roster
[Table of every named NPC: Alien3 cassette trade, Alien4 fish vendor,
Alien6 small-talk, Alien7 goods vendor, Tev/Alien10 quest-giver,
BonfireNPC, ShipMarket/Toy1, GuitarShopNPC, ORG, Interrogation. Streamed
ambient via SpawnedAlienNPC + AlienNPCSpawner.]

### 13. Concert System
[ConcertStageHub singleton, SpeakerSource per stage, AudienceZone +
AudienceSpawner cloned automatically, LebronLight override forces
day-mode near the ship's artificial sun, night-side rotation gates
which stage runs. Per-stage props — lasers, blinders, cone lights,
strobes, haze, fog. Audience members can be killed. New OnStageActivated
event drives HAL's "Concert active at X" line.]

### 14. Map
[SolarSystemMapController key M, MapBootstrapReal builds from live
CelestialBody data, FocusOn(CelestialBody) and FocusOnShip(Ship),
MapTeleportToPilotButton, MapTutorial 6-step linear.]

### 15. Phone & Phone AI
[PlayerPhoneUI key X, 4 swipeable pages (Apps / AI Apps / Vitals /
Quests). AI Apps page has AI tile + 3 "Coming soon". AIChatScreen
overlays. LLMService (Hermes-3-Llama-3.1-8B GGUF, ~5 GB resident +
1 GB KV). System prompt assembled from persona / canon / retrieved
entries / memories / standing / live telemetry / fleet state.
Tool verbs: waypoint / unwaypoint / map / markship / showship.
HALCommentator volunteers substantive lines (vitals / ship-dust /
orbit / concert / atmosphere / planet-arrival / death / killstreak /
EarlyGameProgress flag transitions). HALVolunteeredLog is the chat
transcript backing. AIMemoryStore + AIMemoryExtractor for the
post-session memory distillation pass. Phase 1/2/3 character arc.]

### 16. Tutorials
[TutorialManager + TutorialUI + TutorialStep — main fishing/flight
tutorial fires on Ship.OnShipCollision against a CelestialBody, advances
on Tab. TutorialSteps.cs defines step types. BonusTutorial separately
runs axe/building (4-step) and fishing-deep (5-step), each pauses the
main tutorial.]

### 17. Save System
[SaveSystem.Save/Load/Apply, SaveCollector capture/apply, PendingLoad
bridge, SaveLoadRunner 1-frame + 1-fixedupdate defer, AutosaveManager
5-min cycle. JSON to persistentDataPath/saves/. Schema fields enumerated
— player, ship, resources, wallet, wood, fishInventory, tutorial, npcs,
buildings, looseParts, cassette, equipment, worldFlags, bonusTutorial,
mapTutorial, celestialBodies, alienKills, earlyGame, notes,
buildMenuLock, compass, enemies, enemySpawnTimer/RegularsSinceElite,
extraShips, spaceDust. BodyRelativeTransform for orbital-state-aware
positions. Apply order documented in SaveCollector.Apply.]

### 18. Auto-Created Singletons
[List from CLAUDE.md — PlayerWallet, WoodInventory, SpaceDustInventory,
ResourceManager, Hotbar, TutorialUI, BonusTutorial, CompassHUD,
CameraEffectsManager, KillstreakManager, PlayerTreeContactTracker,
ConcertStageHub, RawFishTripController, AutosaveManager, NoteCollection,
LLMService, GameKnowledgeBase, AIMemoryStore, HALCommentator,
HALVolunteeredLog, HALLineHUD, HALVoicePlayer. The MainMenu singleton
trap and EnsureGameplaySingletons.]

### 19. Camera Effects
[CameraEffectsManager + 16 modules. InputSettings.fx* toggles drive
each from the pause menu's CAMERA tab. Events that drive effects:
PlayerController.OnLanded, ResourceManager.OnHealthDropped, OnDeath,
EnemyController.OnAnyEnemyDeath, KillstreakManager.OnKillRegistered.]

### 20. Atmosphere & Rendering Notes
[CustomPostProcessing [ImageEffectOpaque] gotcha — transparent queue
draws over atmosphere. Standard shader resets queue to 3000 on Mode:3.
Forbidden zone — planet generation, atmosphere shaders, ocean shaders,
celestial generators. CelestialBody.cs not forbidden.]

### 21. Story Arc
[Phase 1 → Phase 2 → Phase 3 character arc of the phone AI. Tev's
12-flag EarlyGameProgress flow. The Crash, the cabin, the village,
the Accord, the ORG. (Cite knowledge file for canonical names.)]

---

*Last revised 2026-05-22.*
```

Fill each section with prose, not bullet lists where prose reads better. Aim for ~150 / ~800 / ~5000 words across the three sections.

- [ ] **Step 7.4: Read-pass — verify accuracy**

Read the rewritten file end-to-end alongside CLAUDE.md. Any factual contradiction is a bug — fix inline. Pay particular attention to: every system named must exist in code; every NPC name must match; every save schema field must match `SaveData.cs`.

- [ ] **Step 7.5 (optional): Commit**

```bash
git add "docs/GAME_OVERVIEW.md"
git commit -m "docs: rewrite GAME_OVERVIEW.md to current game state

Three sections at three lengths — short (~150 word elevator pitch),
medium (~1 page systems + story summary), long (~5000 word
comprehensive technical reference structured for narrative read-through,
not lookup). Reflects all systems added since the previous revision:
multi-ship fleet, space dust + nets, combat + ragdolls + killstreak,
torch aura, concert system, phone AI (LLMService + knowledge base
+ memory store + tool verbs + HAL commentator), save system maturity,
camera effects rebuild."
```

---

## Self-Review (post-write check, before handing off)

### Spec coverage

Cross-reference each spec section to a plan task:

| Spec § | Plan task |
|---|---|
| §1.1 Knowledge audit + expansion | Task 6 |
| §1.2 Ship-aware AI | Tasks 3 + 4 |
| §1.3 Atmosphere with planet name | Task 5 (Step 5.1) |
| §1.4 HAL substantive volunteered lines | Task 5 |
| §1.5 Game overview docs | Task 7 |
| §4.1 FleetTelemetry | Task 3 |
| §4.2 LLMService prompt injection | Task 3 (Step 3.3) |
| §4.3 HALToolDispatcher markship/showship | Task 4 |
| §4.4 SolarSystemMapController (no change) | Task 4 (uses existing FocusOnShip) |
| §4.5 HALCommentator overhaul | Task 5 |
| §4.5.1 Concert speaker pointer | Tasks 1 + 2 |
| §4.6 game_knowledge.md additions | Task 6 |
| §4.7 docs/GAME_OVERVIEW.md rewrite | Task 7 |

All spec sections are covered. No gaps.

### Placeholder scan

No "TBD" / "implement later" / "add error handling" steps. Every code block is complete. Every command is concrete.

### Type consistency

- `BoughtShip.shipNumber` — used identically in `FleetTelemetry` (Task 3), `HALToolDispatcher.TryResolveShip` (Task 4), `HALCommentator.PollShipDust` (Task 5). ✓
- `ThrusterDetachOnImpact.IsDishAttached` (and siblings) — added in Step 3.2, consumed in Tasks 3, 4, 5. ✓
- `SpaceNet.IsAttached` / `SpaceNet.BufferedDust` — added in Step 3.2, consumed in Tasks 3 and 5. ✓
- `FleetTelemetry.EnumerateAllShipsWithNumbers()` — defined in Task 3, consumed in Tasks 4 and 5. ✓
- `ConcertStageHub.OnStageActivated` event signature `Action<SpeakerSource>` — defined in Task 1 Step 1.2, consumed in Task 5 Step 5.6. ✓
- `ConcertStageHub.FindActiveStageSpeaker()` returns `Transform` — defined in Task 1, consumed in Task 2. ✓
- `Ship.IsOrbitMatched` — referenced by existing code (per HALCommentator.PollOrbitMatch line 444) — confirmed public, consumed in Task 5 Step 5.4. ✓

### Execution-order dependencies

Task 1 must run before Tasks 2 and 5 (event + speaker helper). Task 3 must run before Task 4 (verbs use the enumerator and IsDishAttached added there). Task 3 must run before Task 5 (PollShipDust uses the enumerator). Tasks 6 and 7 are independent and can run in either order, last.

Recommended order: **1 → 2 → 3 → 4 → 5 → 6 → 7**. The plan reflects this.

---

## Done condition

All seven tasks completed, Play-mode verifications pass per each task's Step .X verify block, Unity Console clean, and `docs/GAME_OVERVIEW.md` accurately reflects the current game state.
