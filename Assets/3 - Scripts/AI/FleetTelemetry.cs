using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Builds the FLEET STATE block injected into every phone-AI system prompt.
/// Enumerates the scene's original ship (always Ship 0) and every BoughtShip
/// in the scene, emits one line per ship covering location, motion,
/// attachments, and per-net space-dust buffers. Ships without a satellite
/// dish collapse to a single "OFFLINE" line — the model's signal to refuse
/// telemetry queries about that ship.
///
/// Pure read of scene state, no caching, no singleton. Called once per
/// chat turn from LLMService.BuildSystemPrompt — cheap at that cadence.
///
/// EnumerateAllShipsWithNumbers is shared with HALCommentator's
/// PollShipDust so both systems agree on which GameObjects are ships and
/// what number each carries.
/// </summary>
public static class FleetTelemetry
{
    /// Multiplier on body.radius below which a ship counts as "in orbit"
    /// (within the body's neighbourhood). Mirrors SpaceNet's own outer-edge
    /// gate so the model's idea of "in orbit" matches the dust-accumulation
    /// gate the player feels.
    const float OrbitProximityRadiusMultiplier   = 5f;
    /// Below this radius multiplier with low velocity, a ship is "on surface".
    const float SurfaceProximityRadiusMultiplier = 1.05f;
    /// Velocity threshold (m/s) below which a ship is "idle / at rest".
    const float IdleVelocityThreshold            = 5f;
    /// Per-net buffer at or above this value reports as "full".
    const int   FullDustThreshold                = 500;

    /// Yields (ship GameObject, displayed ship number). Scene ship always
    /// gets number 0; BoughtShip instances use bs.shipNumber. Shared with
    /// HALCommentator so both see the same ship set with the same numbering.
    public static IEnumerable<(GameObject go, int number)> EnumerateAllShipsWithNumbers()
    {
        var bought = Object.FindObjectsOfType<BoughtShip>();
        if (bought != null && bought.Length > 1)
        {
            System.Array.Sort(bought, (a, b) =>
            {
                int ai = a != null ? a.shipNumber : int.MaxValue;
                int bi = b != null ? b.shipNumber : int.MaxValue;
                return ai.CompareTo(bi);
            });
        }

        // Scene ship: a Ship whose GameObject does NOT carry BoughtShip.
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
        foreach (var pair in EnumerateAllShipsWithNumbers())
        {
            rendered++;
            sb.Append("  ").Append(RenderShipRow(pair.go, pair.number)).Append('\n');
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

        // Dish attachment drives the OFFLINE gate. ThrusterDetachOnImpact
        // owns the full attachment matrix. If a ship is missing the
        // component entirely we default to "online" (defensive — a future
        // pure-debug ship spawn shouldn't be silently muted).
        var tdoi = ship.GetComponent<ThrusterDetachOnImpact>()
                   ?? ship.GetComponentInChildren<ThrusterDetachOnImpact>(true);
        bool dishOn = tdoi == null || tdoi.HasDishAttached;
        if (!dishOn)
            return $"Ship {shipNumber}: OFFLINE (no satellite dish — no telemetry available)";

        // Motion + planet proximity. Reported speed is WORLD-FRAME
        // rb.velocity magnitude — same value the in-cockpit GForceHUD speed
        // tape shows in m/s (GForceHUD.cs:278). The earlier body-relative
        // calculation was correct for orbital mechanics but mismatched the
        // HUD the player can see, so the AI looked wrong in side-by-side
        // comparison. We still compute relV for orbit/idle CLASSIFICATION
        // (an orbiting ship has high relative velocity even when its world
        // velocity is close to the planet's), then report worldV for the
        // speed number.
        var rb         = ship.GetComponent<Rigidbody>();
        Vector3 pos    = ship.transform.position;
        Vector3 worldV = rb != null ? rb.velocity : Vector3.zero;

        CelestialBody nearest = FindNearestBody(pos, out float distToCentre);
        Vector3 relV   = nearest != null ? worldV - nearest.velocity : worldV;
        float speedKms = worldV.magnitude / 1000f;
        int   speedMs  = Mathf.RoundToInt(worldV.magnitude);

        // Altitude above nearest body's surface, in metres. Negative would
        // mean "inside the planet" which shouldn't happen; clamp at 0 for
        // sane display. Only emitted when the ship is in body proximity.
        float altitudeMeters = nearest != null ? Mathf.Max(0f, distToCentre - nearest.radius) : 0f;

        string location;
        if (nearest != null
            && distToCentre <= nearest.radius * SurfaceProximityRadiusMultiplier
            && relV.magnitude < IdleVelocityThreshold)
        {
            location = $"{nearest.bodyName} surface, idle, altitude {altitudeMeters:0} m";
        }
        else if (nearest != null
            && distToCentre <= nearest.radius * OrbitProximityRadiusMultiplier)
        {
            // Render both km/s (for HUD parity at the player level) and
            // m/s (the actual GForceHUD reading). The model is told to use
            // whichever unit the Astronaut asked in — see the prompt rule.
            location = $"{nearest.bodyName} orbit, {speedMs} m/s ({speedKms:0.00} km/s), altitude {altitudeMeters:0} m";
        }
        else if (relV.magnitude < IdleVelocityThreshold)
        {
            location = "deep space, at rest";
        }
        else
        {
            location = $"deep space drifting, {speedMs} m/s ({speedKms:0.00} km/s)";
        }

        string dust      = RenderDustBuffers(ship);
        string solar     = tdoi == null || tdoi.HasSolarAttached ? "solar OK"     : "no solar";
        string thrusters = tdoi == null ? "thrusters L/R" : RenderThrusterState(tdoi);
        var s            = ship.GetComponent<Ship>();
        string hatch     = s != null && s.HatchOpen ? "hatch open" : "hatch closed";
        string powerStr  = s != null ? $"power {Mathf.RoundToInt(s.PowerPercent * 100f)}%" : "power ?";
        string fuelStr   = s != null ? $"fuel {Mathf.RoundToInt(s.FuelPercent * 100f)}%"   : "fuel ?";

        return $"Ship {shipNumber}: {location}, {dust}, {powerStr}, {fuelStr}, dish OK, {solar}, {thrusters}, {hatch}";
    }

    static string RenderDustBuffers(GameObject ship)
    {
        // Renders per-net buffers and total in a spelled-out sentence form
        // rather than `[net1=37, net2=8] total=45`. The bracketed-array
        // notation turned out to confuse the LLM — it tended to hallucinate
        // values like "12 and 12" instead of reading 37 and 8 directly.
        // Spelling each value out with the digit at the END of the phrase
        // ("net 1 holds 37 dust") makes each number stand on its own token
        // boundary, which the model parses more reliably.
        var nets = ship.GetComponentsInChildren<SpaceNet>(true);
        if (nets == null || nets.Length == 0) return "no space nets attached, 0 dust total";

        var attached = new System.Collections.Generic.List<SpaceNet>();
        int total = 0;
        for (int i = 0; i < nets.Length; i++)
        {
            var n = nets[i];
            if (n == null || !n.IsAttached) continue;
            attached.Add(n);
            total += n.BufferedDust;
        }
        if (attached.Count == 0) return "no attached nets, 0 dust total";

        var sb = new StringBuilder();
        for (int i = 0; i < attached.Count; i++)
        {
            int buf = attached[i].BufferedDust;
            sb.Append("net ").Append(i + 1).Append(" holds ").Append(buf).Append(" dust");
            if (i < attached.Count - 1) sb.Append(", ");
        }
        sb.Append(", total ").Append(total).Append(" dust");
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
        bool l = tdoi.HasLeftThrusterAttached;
        bool r = tdoi.HasRightThrusterAttached;
        if (l && r) return "thrusters L/R";
        if (l)      return "thrusters L only";
        if (r)      return "thrusters R only";
        return "thrusters detached";
    }
}
