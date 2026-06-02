using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Executes "tool calls" that the LLM emits inline in its response —
/// short tags like [waypoint:Tev] or [map:Cyclops] that the player never
/// sees (LLMService strips them from the visible text) but that translate
/// into real game actions.
///
/// This is the lever that turns the AI from a chat-box into a participant:
/// instead of saying "Tev is at the village" and the player having to find
/// him themselves, the AI says "Tev is at the village" AND drops a compass
/// waypoint to him in the same line.
///
/// Adding a new verb = one new case in Execute() plus a documented entry
/// in the TOOLS section of LLMService.BuildSystemPrompt so the model
/// knows it exists.
/// </summary>
public static class HALToolDispatcher
{
    /// Executes a parsed tool call. Safe to call from a streaming context —
    /// each call is fire-and-forget and isolated, so a thrown exception in
    /// one verb won't break the others.
    public static void Execute(string verb, string arg)
    {
        if (string.IsNullOrEmpty(verb)) return;
        try
        {
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
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    // ── Active-waypoint tracking ─────────────────────────────────────────
    // Every HAL-issued waypoint is recorded here so we can auto-remove it
    // when the player gets within 10 m of the target — that "target reached"
    // moment closes the loop on the AI actually helping the player navigate.
    // HALCommentator.Update calls TickProximity() once per Update.

    class TrackedWaypoint
    {
        public string Id;          // CompassHUD waypoint id, used for removal
        public Transform Target;   // followed each frame
        public string Label;       // shown in "Target reached: X" line
        public string Key;         // lower-cased name the AI used (for unwaypoint matching)
    }
    static readonly List<TrackedWaypoint> _activeWaypoints = new List<TrackedWaypoint>();

    const float WaypointReachedRadiusMeters = 35f;
    static float _reachedRadiusSq = WaypointReachedRadiusMeters * WaypointReachedRadiusMeters;

    static PlayerController _cachedPC;
    static PlayerController ResolvePlayer()
    {
        if (_cachedPC != null) return _cachedPC;
        _cachedPC = UnityEngine.Object.FindObjectOfType<PlayerController>();
        return _cachedPC;
    }

    /// Polled from HALCommentator.Update. Removes any tracked waypoint whose
    /// target is now within 10 m of the player and fires a "Target reached"
    /// HUD notification + log entry.
    public static void TickProximity()
    {
        if (_activeWaypoints.Count == 0) return;
        var pc = ResolvePlayer();
        if (pc == null) return;
        Vector3 playerPos = pc.transform.position;

        for (int i = _activeWaypoints.Count - 1; i >= 0; i--)
        {
            var wp = _activeWaypoints[i];
            if (wp == null || wp.Target == null)
            {
                _activeWaypoints.RemoveAt(i);
                continue;
            }
            float dSq = (wp.Target.position - playerPos).sqrMagnitude;
            if (dSq > _reachedRadiusSq) continue;

            // Reached! Remove from compass + active list, notify.
            if (CompassHUD.Instance != null) CompassHUD.Instance.RemoveWaypoint(wp.Id);
            string line = $"Target reached: {wp.Label}.";
            if (HALLineHUD.Instance != null)        HALLineHUD.Instance.Show(line);
            if (HALVolunteeredLog.Instance != null) HALVolunteeredLog.Instance.Append(line);
            _activeWaypoints.RemoveAt(i);
        }
    }

    // ── waypoint ─────────────────────────────────────────────────────────

    static void HandleWaypoint(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return;
        if (CompassHUD.Instance == null)
        {
            Debug.LogWarning($"[HALToolDispatcher] waypoint:'{arg}' — CompassHUD not available.");
            return;
        }

        Transform target = ResolveTarget(arg);
        if (target == null)
        {
            Debug.LogWarning($"[HALToolDispatcher] waypoint:'{arg}' — no matching Transform / CelestialBody in scene.");
            return;
        }

        // Capture the Transform in a closure. AddWaypoint uses a dynamic
        // position provider so the waypoint follows moving targets (NPCs
        // walking around, planets orbiting). NOT persisted through saves —
        // that's fine; the AI can re-drop on next chat.
        string key   = arg.Trim().ToLowerInvariant();
        string id    = "hal_" + key.Replace(' ', '_');
        string label = FormatLabel(arg);
        Transform t  = target;

        // If a HAL waypoint for the same key already exists, replace it
        // cleanly so we don't leak duplicate trackers/CompassHUD entries.
        RemoveTrackedWaypoint(key);

        CompassHUD.Instance.AddWaypoint(
            id,
            () => t != null ? t.position : Vector3.zero,
            label,
            null,                                 // default icon
            HALVisuals.EyeRed                     // red tint so HAL waypoints read as HAL's
        );
        _activeWaypoints.Add(new TrackedWaypoint
        {
            Id = id, Target = target, Label = label, Key = key
        });
        Debug.Log($"[HALToolDispatcher] waypoint dropped: id={id} target={target.name}");
    }

    static void HandleUnwaypoint(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return;
        string key = arg.Trim().ToLowerInvariant();
        bool removed = RemoveTrackedWaypoint(key);
        if (!removed)
            Debug.LogWarning($"[HALToolDispatcher] unwaypoint:'{arg}' — no active waypoint with that name.");
    }

    // Removes any tracked HAL waypoint whose normalised key matches `key`.
    // Returns true if one was found and removed.
    static bool RemoveTrackedWaypoint(string key)
    {
        bool removed = false;
        for (int i = _activeWaypoints.Count - 1; i >= 0; i--)
        {
            var wp = _activeWaypoints[i];
            if (wp == null) { _activeWaypoints.RemoveAt(i); continue; }
            if (wp.Key != key) continue;
            if (CompassHUD.Instance != null) CompassHUD.Instance.RemoveWaypoint(wp.Id);
            _activeWaypoints.RemoveAt(i);
            removed = true;
        }
        return removed;
    }

    // ── map ──────────────────────────────────────────────────────────────

    static void HandleMap(string arg)
    {
        if (SolarSystemMapController.Instance == null)
        {
            Debug.LogWarning("[HALToolDispatcher] map: SolarSystemMapController not available.");
            return;
        }

        SolarSystemMapController.Instance.OpenMap();

        // If an arg was provided, try to find a matching celestial body and
        // focus the map on it. Silent if no match (the map still opens).
        if (!string.IsNullOrEmpty(arg))
        {
            var body = ResolveCelestialBody(arg);
            if (body != null) SolarSystemMapController.Instance.FocusOn(body);
        }
    }

    // ── Target resolution ────────────────────────────────────────────────
    // The AI emits human-friendly names ("Tev", "village", "Cyclops") but
    // scene GameObjects use specific names ("TEV", "Alien10", etc.). We try
    // direct match first, then a small alias table, then celestial bodies.

    public static Transform ResolveTarget(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return null;
        string trimmed = arg.Trim();
        string lower   = trimmed.ToLowerInvariant();

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

        // 0a. Special-case "village" — the village GameObject has no fixed
        //     name (it's tagged by the VillageMarker component since it
        //     parents under the planet for orbital co-motion). Look it up
        //     by component, not by name.
        if (lower == "village" || lower == "the village" || lower == "town")
        {
            var marker = UnityEngine.Object.FindObjectOfType<VillageMarker>();
            return marker != null ? marker.transform : null;
        }

        // 1. Direct GameObject name match
        var go = GameObject.Find(trimmed);
        if (go != null) return go.transform;

        // 2. Common aliases — names the AI is likely to use mapped to the
        //    actual GameObject names in the scene.
        string alias = ResolveAlias(lower);
        if (!string.IsNullOrEmpty(alias))
        {
            go = GameObject.Find(alias);
            if (go != null) return go.transform;
        }

        // 3. Celestial body match (planets, moons, sun)
        var body = ResolveCelestialBody(trimmed);
        if (body != null) return body.transform;

        return null;
    }

    static string ResolveAlias(string lower)
    {
        // Already-lower-cased input. Maps natural names the AI is likely to
        // use to actual scene-GameObject names. When the player asks for
        // "the ship vendor" / "fish market" / etc., we want the waypoint
        // to land on the right NPC even though the GameObject name is
        // something else like "Toy1" or "Alien4".
        switch (lower)
        {
            case "tev":
            case "alien10":         return "TEV";

            case "fish market":
            case "fishmarket":
            case "fish vendor":
            case "alien4":          return "Alien4";

            case "goods vendor":
            case "goods market":
            case "alien7":
            case "alien7vendor":
            case "bakery":
            case "bakerymarket":    return "Alien7";

            case "ship vendor":
            case "ship market":
            case "shipmarket":
            case "ship marketplace":
            case "ship merchant":
            case "ship dealer":
            case "toy1":            return "ShipMarket";

            case "guitar shop":
            case "guitarshop":
            case "guitar vendor":   return "GuitarShopNPC";

            case "bonfire npc":
            case "bonfire":         return "BonfireNPC";

            case "alien3":
            case "cassette npc":    return "Alien3";

            case "moon base":
            case "moonbase":        return "MoonBase";

            case "start cabin":
            case "starting cabin":  return "StartCabin";

            default:                return null;
        }
    }

    /// True if `arg` names a CelestialBody in the active scene (a planet,
    /// moon, or the Sun). Used by LLMService to gate the "suppress reflexive
    /// [map] when also dropping a non-planet waypoint" safety net.
    public static bool IsPlanetName(string arg) => ResolveCelestialBody(arg) != null;

    static CelestialBody ResolveCelestialBody(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return null;
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            if (string.Equals(b.bodyName, arg, StringComparison.OrdinalIgnoreCase))
                return b;
        }
        return null;
    }

    // Title-case the arg for display ("tev" → "Tev", "cyclops" → "Cyclops")
    // so the compass label reads cleanly regardless of how the model
    // capitalised it.
    static string FormatLabel(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return arg;
        var s = arg.Trim();
        if (s.Length == 0) return s;
        // Cheap, allocation-free-ish title case — first char upper, rest as-is.
        // Avoids CultureInfo overhead and the multi-word ambiguity.
        return char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    // ── Ship resolution + handlers ───────────────────────────────────────
    // Used by [markship:N] and [showship:N]. N is BoughtShip.shipNumber for
    // bought ships, or 0 for the scene's original ship. Resolves
    // case-insensitive variants like "1", "ship 1", "Ship 1".

    static bool TryResolveShip(string arg, out GameObject shipGO, out int shipNumber)
    {
        shipGO = null;
        shipNumber = -1;
        if (string.IsNullOrWhiteSpace(arg)) return false;

        string s = arg.Trim();
        if (s.StartsWith("ship ", System.StringComparison.OrdinalIgnoreCase))
            s = s.Substring(5).TrimStart();

        if (!int.TryParse(s, out shipNumber))
        {
            Debug.LogWarning($"[HALToolDispatcher] markship/showship: could not parse ship number from '{arg}'.");
            return false;
        }

        // Use the FleetTelemetry enumerator so the dispatcher agrees with
        // the LLM's prompt-injected FLEET STATE about which GameObjects
        // are ships and what number each carries.
        foreach (var pair in FleetTelemetry.EnumerateAllShipsWithNumbers())
        {
            if (pair.number != shipNumber) continue;
            shipGO = pair.go;
            return true;
        }
        Debug.LogWarning($"[HALToolDispatcher] markship/showship: no ship with shipNumber={shipNumber} in scene.");
        return false;
    }

    // True iff the ship has its satellite dish attached. Defaults to
    // false if the ship lacks a ThrusterDetachOnImpact at all — fail-safe
    // treat-as-offline rather than risk leaking nav for unknown ships.
    static bool IsShipOnline(GameObject shipGO)
    {
        if (shipGO == null) return false;
        var tdoi = shipGO.GetComponent<ThrusterDetachOnImpact>()
                   ?? shipGO.GetComponentInChildren<ThrusterDetachOnImpact>(true);
        if (tdoi == null) return false;
        return tdoi.HasDishAttached;
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

        // Replace any existing HAL ship marker for this number so the
        // compass doesn't accumulate duplicates on repeated "mark Ship N"
        // requests.
        RemoveTrackedWaypoint(key);

        CompassHUD.Instance.AddWaypoint(
            id,
            () => t != null ? t.position : Vector3.zero,
            label,
            null,
            HALVisuals.EyeRed
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
}
