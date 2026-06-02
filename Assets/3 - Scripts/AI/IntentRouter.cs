using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Deterministic intent router for the phone AI. First piece of Phase 1 of
/// the AI Companion Revamp Plan (docs/AI_Companion_Revamp_Plan.md §2):
///
///   Player input → [INTENT ROUTER] → [HANDLERS] → AIResult → [LLM voice]
///
/// Principle: questions with an objectively correct answer pulled from live
/// game state (ship dust, ship speed, player vitals, mission progress, etc.)
/// must NEVER be synthesised by the LLM. The model is for voice and
/// flavour; deterministic code owns the facts. Without this gate, a small
/// CPU-friendly model like Qwen-3B will happily copy example numbers from
/// the system prompt as if they were the real answer.
///
/// Each handler returns null if the message isn't a match → fall through to
/// the next handler → eventually the LLM. Returning a non-null string
/// short-circuits the LLM entirely; the bubble shows that string verbatim,
/// LLMService records the turn in AIMemoryStore, and the chat history is
/// kept consistent.
///
/// Adding a new intent = one new private static method + one call from
/// TryAnswer's chain. No subclassing, no registration ceremony.
/// </summary>
public static class IntentRouter
{
    /// Tries every registered handler in order. Returns the first non-null
    /// deterministic answer, or null if nothing matched (LLM should handle).
    ///
    /// Ordering: ship-specific intents first (they require an explicit "ship N"
    /// in the message, so they're surgical), then player-scoped intents
    /// (vitals, mission, inventory) which are looser keyword matches.
    public static string TryAnswer(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) return null;
        string lower = userMessage.ToLowerInvariant();

        string r;
        if ((r = TryShipDust(lower))            != null) return r;
        if ((r = TryShipSpeed(lower))           != null) return r;
        if ((r = TryShipAltitude(lower))        != null) return r;
        if ((r = TryShipPower(lower))           != null) return r;
        if ((r = TryShipFuel(lower))            != null) return r;
        if ((r = TryAmbiguousShipResource(lower)) != null) return r;
        if ((r = TryPlayerVitals(lower))        != null) return r;
        if ((r = TryMissionProgress(lower))     != null) return r;
        if ((r = TryInventory(lower))           != null) return r;
        if ((r = TryMarkTarget(userMessage, lower)) != null) return r; // pass raw msg too — we need original case for the alias
        return null;
    }

    static readonly Regex ShipPowerIntent = new Regex(
        @"\bship\s+(\d+)\b[^.?!]*\bpower\b|\bpower\b[^.?!]*\bship\s+(\d+)\b",
        RegexOptions.IgnoreCase);

    static readonly Regex ShipFuelIntent = new Regex(
        @"\bship\s+(\d+)\b[^.?!]*\bfuel\b|\bfuel\b[^.?!]*\bship\s+(\d+)\b",
        RegexOptions.IgnoreCase);

    // Matches "ship power" / "ship fuel" / "the power" / "the fuel" when no
    // ship number follows. Combined with the per-ship regexes above, this
    // catches under-specified queries so the AI can ask which ship.
    static readonly Regex AmbiguousShipResourceIntent = new Regex(
        @"\b(?:ship\s+power|ship\s+fuel|the\s+(?:power|fuel))\b",
        RegexOptions.IgnoreCase);

    static string TryShipPower(string msgLower)
    {
        var m = ShipPowerIntent.Match(msgLower);
        if (!m.Success) return null;
        string num = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        if (!int.TryParse(num, out int shipNumber)) return null;
        var ship = ResolveShipByNumber(shipNumber);
        if (ship == null) return $"Ship {shipNumber} does not exist.";
        int pct = Mathf.RoundToInt(ship.PowerPercent * 100f);
        return $"Ship {shipNumber} power is at {pct}%.";
    }

    static string TryShipFuel(string msgLower)
    {
        var m = ShipFuelIntent.Match(msgLower);
        if (!m.Success) return null;
        string num = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        if (!int.TryParse(num, out int shipNumber)) return null;
        var ship = ResolveShipByNumber(shipNumber);
        if (ship == null) return $"Ship {shipNumber} does not exist.";
        int pct = Mathf.RoundToInt(ship.FuelPercent * 100f);
        return $"Ship {shipNumber} fuel is at {pct}%.";
    }

    static string TryAmbiguousShipResource(string msgLower)
    {
        if (!AmbiguousShipResourceIntent.IsMatch(msgLower)) return null;
        // Don't refuse if the per-ship regexes matched (they ran first in
        // TryAnswer, but defensive check in case ordering changes).
        if (ShipPowerIntent.IsMatch(msgLower)) return null;
        if (ShipFuelIntent.IsMatch(msgLower))  return null;
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

    // ── Ship-scoped helpers ─────────────────────────────────────────

    /// Common front-end for ship intents: parse the ship number out of the
    /// match's first capturing group (or second if the player put the metric
    /// before "ship N"), find the ship in the scene, and emit the standard
    /// "does not exist" / "offline" answers if the ship isn't queryable.
    /// On success, `ship` is set to the GameObject and the return is null
    /// so the caller continues. On failure, the return is the player-facing
    /// answer and `ship` is null.
    static string ResolveShip(Match m, out GameObject ship, out int shipNumber)
    {
        ship = null;
        shipNumber = -1;
        string shipNumStr = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        if (!int.TryParse(shipNumStr, out shipNumber)) return null; // unparseable → fall through to LLM

        foreach (var pair in FleetTelemetry.EnumerateAllShipsWithNumbers())
        {
            if (pair.number == shipNumber) { ship = pair.go; break; }
        }
        if (ship == null) return $"Ship {shipNumber} does not exist.";

        var tdoi = ship.GetComponent<ThrusterDetachOnImpact>()
                   ?? ship.GetComponentInChildren<ThrusterDetachOnImpact>(true);
        if (tdoi != null && !tdoi.HasDishAttached)
        {
            ship = null;
            return $"Ship {shipNumber} is offline. I cannot reach it.";
        }
        return null; // ship resolved + online
    }

    // ── Ship dust intent ────────────────────────────────────────────
    //
    // Matches: "how much dust on ship 2", "ship 0 dust", "dust ship 1",
    //          "what's the spacedust on ship 3", "ship2 dust".
    static readonly Regex DustRegex = new Regex(
        @"\b(?:space[\s-]?)?dust\b.*?\bship\s*(\d+)\b" +
        @"|" +
        @"\bship\s*(\d+)\b.*?\b(?:space[\s-]?)?dust\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static string TryShipDust(string msgLower)
    {
        var m = DustRegex.Match(msgLower);
        if (!m.Success) return null;
        var fail = ResolveShip(m, out var ship, out int shipNumber);
        if (fail != null) return fail;

        var nets = ship.GetComponentsInChildren<SpaceNet>(true);
        var dustPerNet = new List<int>();
        int total = 0;
        if (nets != null)
        {
            for (int i = 0; i < nets.Length; i++)
            {
                var net = nets[i];
                if (net == null || !net.IsAttached) continue;
                int buf = net.BufferedDust;
                dustPerNet.Add(buf);
                total += buf;
            }
        }
        if (dustPerNet.Count == 0)
            return $"Ship {shipNumber} has no space nets attached. 0 dust total.";

        var sb = new StringBuilder();
        sb.Append("Ship ").Append(shipNumber).Append(" has ").Append(total).Append(" dust total");
        sb.Append(" — ");
        for (int i = 0; i < dustPerNet.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(dustPerNet[i]).Append(" in net ").Append(i + 1);
        }
        sb.Append('.');
        return sb.ToString();
    }

    // ── Ship speed intent ────────────────────────────────────────────
    //
    // Matches: "how fast is ship 0", "ship 1 speed", "ship 2 velocity",
    //          "how fast ship 0 moving", "ship 0 how fast".
    static readonly Regex SpeedRegex = new Regex(
        @"\b(?:speed|velocity|fast|moving)\b.*?\bship\s*(\d+)\b" +
        @"|" +
        @"\bship\s*(\d+)\b.*?\b(?:speed|velocity|fast|moving)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static string TryShipSpeed(string msgLower)
    {
        var m = SpeedRegex.Match(msgLower);
        if (!m.Success) return null;
        var fail = ResolveShip(m, out var ship, out int shipNumber);
        if (fail != null) return fail;

        var rb = ship.GetComponent<Rigidbody>();
        float speed = rb != null ? rb.velocity.magnitude : 0f;
        int   ms    = Mathf.RoundToInt(speed);
        float kms   = speed / 1000f;
        return $"Ship {shipNumber} is moving at {ms} m/s ({kms:0.00} km/s).";
    }

    // ── Ship altitude intent ─────────────────────────────────────────
    //
    // Matches: "how high is ship 0", "ship 1 altitude", "ship 2 height",
    //          "altitude of ship 3", "ship 0 how high orbiting".
    static readonly Regex AltitudeRegex = new Regex(
        @"\b(?:altitude|how high|height|orbiting?)\b.*?\bship\s*(\d+)\b" +
        @"|" +
        @"\bship\s*(\d+)\b.*?\b(?:altitude|how high|height|orbiting?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static string TryShipAltitude(string msgLower)
    {
        var m = AltitudeRegex.Match(msgLower);
        if (!m.Success) return null;
        var fail = ResolveShip(m, out var ship, out int shipNumber);
        if (fail != null) return fail;

        // Find nearest body. CLAUDE.md is explicit: "don't add another list —
        // use NBodySimulation.Bodies" — so we read directly from there
        // instead of duplicating FleetTelemetry's private FindNearestBody.
        var bodies = NBodySimulation.Bodies;
        if (bodies == null || bodies.Length == 0)
            return $"Ship {shipNumber} is in deep space. No body nearby.";

        Vector3 pos = ship.transform.position;
        CelestialBody nearest = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            float d = Vector3.Distance(pos, b.Position);
            if (d < bestDist) { bestDist = d; nearest = b; }
        }
        if (nearest == null)
            return $"Ship {shipNumber} is in deep space. No body nearby.";

        // Same OrbitProximityRadiusMultiplier (5×) FleetTelemetry uses to
        // decide "in orbit" vs "deep space". Keeps the IntentRouter and the
        // FLEET STATE prose consistent.
        const float OrbitProximity = 5f;
        if (bestDist > nearest.radius * OrbitProximity)
            return $"Ship {shipNumber} is in deep space, not near any body.";

        int altitude = Mathf.RoundToInt(Mathf.Max(0f, bestDist - nearest.radius));
        return $"Ship {shipNumber} is {altitude} metres above the surface of {nearest.bodyName}.";
    }

    // ── Player vitals intent ─────────────────────────────────────────
    //
    // Handles both general queries ("how am i doing", "vitals", "am i ok")
    // and per-stat queries ("hunger", "thirst", "health", "ship power").
    // Pulls from ResourceManager.Instance.
    static string TryPlayerVitals(string msgLower)
    {
        var rm = ResourceManager.Instance;
        if (rm == null) return null;

        bool wantsHunger = ContainsWord(msgLower, "hunger") || ContainsWord(msgLower, "hungry");
        bool wantsThirst = ContainsWord(msgLower, "thirst") || ContainsWord(msgLower, "thirsty");
        bool wantsHealth = ContainsWord(msgLower, "health") || ContainsWord(msgLower, "hp");
        bool wantsAll    = ContainsWord(msgLower, "vitals")
                           || msgLower.Contains("how am i")
                           || msgLower.Contains("am i ok")
                           || msgLower.Contains("am i okay")
                           || msgLower.Contains("am i alright");

        int h  = Mathf.RoundToInt(rm.HungerPercent * 100);
        int t  = Mathf.RoundToInt(rm.ThirstPercent * 100);
        int hp = Mathf.RoundToInt(rm.HealthPercent * 100);

        if (wantsHealth) return $"Your health is at {hp}%.";
        if (wantsHunger) return $"Hunger is at {h}%.";
        if (wantsThirst) return $"Thirst is at {t}%.";
        if (wantsAll)
        {
            var piloted = Ship.PilotedInstance;
            if (piloted != null)
            {
                int spi = Mathf.RoundToInt(piloted.PowerPercent * 100f);
                int sfi = Mathf.RoundToInt(piloted.FuelPercent  * 100f);
                return $"Hunger {h}%. Thirst {t}%. Health {hp}%. Ship power {spi}%. Ship fuel {sfi}%.";
            }
            return $"Hunger {h}%. Thirst {t}%. Health {hp}%.";
        }
        return null;
    }

    // ── Mission / progress intent ────────────────────────────────────
    //
    // "what is my mission" / "what should i do" / "what now" / "next step".
    // Walks the EarlyGameProgress flags in canonical order and reports the
    // first one that's still false. Maps each flag to a player-facing
    // hint about the next concrete action.
    static string TryMissionProgress(string msgLower)
    {
        // Only fire on phrasings that clearly mean "tell me what to do NEXT" —
        // i.e. tutorial-step requests. Lore questions that happen to contain
        // the word "mission" or "objective" go to the LLM, which has the
        // canonical Mission Core entry from game_knowledge.md to draw on.
        //
        // Was previously matching bare "mission" / "objective" — caught lore
        // questions like "what is my mission why have they sent me here"
        // and returned a tutorial step. Now requires an intent phrase.
        bool match = msgLower.Contains("what should i do")
                     || msgLower.Contains("what do i do")
                     || msgLower.Contains("what now")
                     || msgLower.Contains("what next")
                     || msgLower.Contains("next step")
                     || msgLower.Contains("my next")
                     || msgLower.Contains("whats next")
                     || msgLower.Contains("what's next");
        if (!match) return null;

        // Negative gate — if the message ALSO contains words that signal
        // a lore/narrative question (who hired me, why am I here, what is
        // the Office, etc.), prefer the LLM over a tutorial step.
        if (msgLower.Contains("why") || msgLower.Contains("who")
            || msgLower.Contains("org") || msgLower.Contains("office")
            || msgLower.Contains("hired") || msgLower.Contains("sent me")
            || msgLower.Contains("tell me about") || msgLower.Contains("what is"))
            return null;

        // Canonical order from EarlyGameProgress + CLAUDE.md Phase 1-7.
        if (!EarlyGameProgress.NoteRead)
            return "Read the note inside the start cabin.";
        if (!EarlyGameProgress.RodPickedUp)
            return "Trade with the cassette alien for a fishing rod.";
        if (!EarlyGameProgress.FirstFishCaught)
            return "Catch your first fish.";
        if (!EarlyGameProgress.OneOfEachCaught)
            return "Catch one of each fish species: Common, Uncommon, and Rare.";
        if (!EarlyGameProgress.FirstMealEaten)
            return "Cook and eat a fish at a bonfire.";
        if (!EarlyGameProgress.WaterBottleDrunk)
            return "Drink from the water bottle.";
        if (!EarlyGameProgress.ReturnedHome)
            return "Return to the start cabin.";
        if (!EarlyGameProgress.TevReturnedDialogueDone)
            return "Speak with Tev.";
        if (!EarlyGameProgress.CabinBuilt)
            return "Build a cabin using the build menu.";
        if (!EarlyGameProgress.VillageCoordsGiven)
            return "Speak with Tev to obtain the village coordinates.";
        if (!EarlyGameProgress.FishVendorVisited)
            return "Visit the fish vendor in the village.";
        if (!EarlyGameProgress.GoodsVendorVisited)
            return "Visit the goods vendor in the village.";

        return "All early-game objectives are complete.";
    }

    // ── Inventory intent (money / wood / personal space dust) ────────
    //
    // Player-scoped resource queries. Skips entirely if the message also
    // mentions a ship — those are handled by the ship-scoped intents above.
    static string TryInventory(string msgLower)
    {
        // Defensive: if "ship N" is in the message, this is a ship query
        // that the ship handlers didn't match (e.g. unusual phrasing). Don't
        // shadow it with personal-inventory data.
        if (Regex.IsMatch(msgLower, @"\bship\s*\d+\b")) return null;

        bool wantsMoney = ContainsWord(msgLower, "money")
                          || ContainsWord(msgLower, "cash")
                          || ContainsWord(msgLower, "currency")
                          || ContainsWord(msgLower, "credits")
                          || ContainsWord(msgLower, "coin")
                          || ContainsWord(msgLower, "coins");
        bool wantsWood = ContainsWord(msgLower, "wood")
                         || ContainsWord(msgLower, "lumber")
                         || ContainsWord(msgLower, "logs");
        // Personal dust (player inventory, not a ship's net buffer).
        bool wantsDust = (msgLower.Contains("space dust")
                          || msgLower.Contains("spacedust")
                          || ContainsWord(msgLower, "dust"));

        if (wantsMoney && PlayerWallet.Instance != null)
            return $"You have {PlayerWallet.Instance.Money} currency.";
        if (wantsWood && WoodInventory.Instance != null)
            return $"You have {WoodInventory.Instance.Wood} wood.";
        if (wantsDust && SpaceDustInventory.Instance != null)
            return $"You have {SpaceDustInventory.Instance.Count} dust in your inventory.";
        return null;
    }

    // ── Mark / waypoint intent ──────────────────────────────────────
    //
    // "mark X" / "mark the X" / "where is X" / "find X" / "put X on my
    // compass" / "show me X". The LLM was supposed to handle these via
    // inline [waypoint:X] tags, but Hermes-8B kept saying "I've marked it"
    // WITHOUT emitting the tag — playtests showed ZERO HALToolDispatcher
    // lines across many mark requests. The architectural fix (per the
    // user's framing: "the AI is just for flavour, not to synthesize its
    // own answers") is to route marking through deterministic code.
    //
    // Pipeline: pattern-match the verb + extract the target → validate via
    // HALToolDispatcher.ResolveTarget → if resolvable, dispatch + return
    // templated acknowledgement; if not, return null (fall through to LLM
    // which can explain or deflect).

    // ── Open-the-map intent ─────────────────────────────────────────
    // "open the map" / "open my map" / "show me the map" / "bring up the
    // map" — no specific target, just toggle the system map open.
    static readonly Regex OpenMapRegex = new Regex(
        @"^\s*(?:open|bring up|show me|show)\s+(?:the\s+|my\s+)?(?:solar\s+)?(?:system\s+)?map\s*[?.!]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Mark-ship intent (markship / showship) ──────────────────────
    // "mark ship N" / "find ship N" / "show me ship N (on the map)" /
    // "where is my ship N". Ships use a dedicated dispatcher verb with
    // a number argument — separate from the general waypoint flow.
    static readonly Regex MarkShipRegex = new Regex(
        @"\b(?:mark|find|locate|show me|where(?:'s| is)?)\s+(?:my\s+)?ship\s*(\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Unmark intent ────────────────────────────────────────────────
    // "unmark X" / "remove X" / "remove the X marker" / "clear X" /
    // "delete the X waypoint" — remove a previously-placed waypoint.
    static readonly Regex UnmarkRegex = new Regex(
        @"\b(?:unmark|remove|clear|delete|drop)\s+(?:the\s+|my\s+)?(.+?)(?:\s+(?:marker|waypoint|compass marker|pin))?\s*[?.!]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Generic mark intent ─────────────────────────────────────────
    // "mark X" / "find X" / "where is X" / "show me X" / "put X on
    // compass" / "guide me to X". Captures the target after the verb.
    // Trailing "on my compass" / "on the map" is optionally consumed so
    // it doesn't get baked into the target.
    static readonly Regex MarkRegex = new Regex(
        @"\b(?:mark|find|locate|show me|point me to|guide me to|take me to|lead me to|put|drop|where(?:'s| is)?)\s+(?:the\s+|my\s+)?(.+?)(?:\s+on\s+(?:my|the)\s+(?:compass|map))?\s*[?.!]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static string TryMarkTarget(string rawMessage, string msgLower)
    {
        // 1. Pure "open map" intent — no target argument.
        if (OpenMapRegex.IsMatch(rawMessage))
        {
            HALToolDispatcher.Execute("map", "");
            return "Opening the solar-system map for you.";
        }

        // 2. Ship N marking — must check BEFORE the generic mark regex,
        //    because the generic regex would otherwise capture "ship N" as
        //    a target and ResolveTarget would fail (ships aren't aliased).
        var shipMatch = MarkShipRegex.Match(rawMessage);
        if (shipMatch.Success)
        {
            int shipN = int.Parse(shipMatch.Groups[1].Value);
            // Validate the ship actually exists before promising anything.
            bool exists = false;
            foreach (var pair in FleetTelemetry.EnumerateAllShipsWithNumbers())
            {
                if (pair.number == shipN) { exists = true; break; }
            }
            if (!exists) return $"Ship {shipN} does not exist.";

            bool wantsMap = Regex.IsMatch(rawMessage, @"\bon\s+(?:my|the)\s+map\b", RegexOptions.IgnoreCase);
            if (wantsMap)
            {
                HALToolDispatcher.Execute("showship", shipN.ToString());
                return $"Opening the map on Ship {shipN}.";
            }
            HALToolDispatcher.Execute("markship", shipN.ToString());
            return $"Marker placed on Ship {shipN}.";
        }

        // 3. Unmark intent.
        var unmarkMatch = UnmarkRegex.Match(rawMessage);
        if (unmarkMatch.Success)
        {
            string utarget = unmarkMatch.Groups[1].Value.Trim();
            utarget = Regex.Replace(utarget, @"\s+(please|now)\.?$", "", RegexOptions.IgnoreCase).Trim();
            if (!string.IsNullOrEmpty(utarget))
            {
                HALToolDispatcher.Execute("unwaypoint", utarget);
                return $"Marker cleared: {utarget}.";
            }
        }

        // 4. Generic mark intent (people, vendors, landmarks, concerts, planets).
        var m = MarkRegex.Match(rawMessage);
        if (!m.Success) return null;

        string target = m.Groups[1].Value.Trim();
        if (string.IsNullOrEmpty(target)) return null;

        // Strip a few common trailing fillers the regex didn't catch.
        target = Regex.Replace(target, @"\s+(please|now|asap)\.?$", "", RegexOptions.IgnoreCase).Trim();
        if (string.IsNullOrEmpty(target)) return null;

        // Don't match unmark verbs as marks — the unmark regex above runs
        // first, so this is just defensive.
        if (Regex.IsMatch(rawMessage, @"^\s*(?:unmark|remove|clear|delete)\b", RegexOptions.IgnoreCase))
            return null;

        // Map intent: explicit "on the map" phrasing routes to map verb.
        // Otherwise default to compass waypoint.
        bool wantsMapTarget = Regex.IsMatch(rawMessage, @"\bon\s+(?:my|the)\s+map\b", RegexOptions.IgnoreCase);

        // Validate the target resolves to something in the scene before we
        // promise the player we marked it. If ResolveTarget returns null,
        // the alias is unknown — let the LLM handle the deflection.
        var resolved = HALToolDispatcher.ResolveTarget(target);
        if (resolved == null) return null;

        if (wantsMapTarget && HALToolDispatcher.IsPlanetName(target))
        {
            HALToolDispatcher.Execute("map", target);
            return $"Opening the map for you, focused on {target}.";
        }

        // Concert gets a special acknowledgement noting the after-dark window.
        if (target.ToLowerInvariant().Contains("concert") || target.ToLowerInvariant() == "show" || target.ToLowerInvariant() == "stage")
        {
            HALToolDispatcher.Execute("waypoint", "concert");
            return "Done — it's on your compass. Heads up, the concert only runs after dark.";
        }

        HALToolDispatcher.Execute("waypoint", target);
        return $"Marker placed: {target}.";
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// Word-boundary contains. Avoids matching "hp" inside "shrimp" or
    /// "health" inside "stealth" by anchoring on \b on both sides.
    static bool ContainsWord(string haystack, string word)
    {
        // Lightweight: precompiling these regexes is overkill since the
        // word set is small and call rate is per-chat, not per-frame.
        return Regex.IsMatch(haystack, $@"\b{Regex.Escape(word)}\b");
    }
}
