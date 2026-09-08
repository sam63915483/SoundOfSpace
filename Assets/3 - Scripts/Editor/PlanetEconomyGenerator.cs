using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Drafts and checks <c>StreamingAssets/Economy/planet_economy.json</c> — which
/// fish live on which planet and what each market wants.
/// (docs/Handoff_PlanetEconomy_Fuel_Fishing_v2.md §6.4 + §7.4.)
///
/// <b>Tools ▸ Economy ▸ Draft Planet Tables</b> writes a first draft that obeys
/// every design rule, then STOPS — Sam hand-tunes from there. It never
/// overwrites a table that exists: with one on disk it writes
/// <c>planet_economy.draft.json</c> beside it instead, so a hand-edited table
/// can't be clobbered by a stray click.
///
/// <b>Tools ▸ Economy ▸ Validate Planet Tables</b> re-checks the live file
/// against the same rules and lists every violation, so a hand edit that
/// quietly breaks a route is caught before a playtest does.
///
/// The rules (all from the handoff, none invented here):
///   • every planet with a fish market has a table
///   • mains (radius ≥ 150) catch 2 species per tier, dwarfs 1 — 42 slots
///   • every species is catchable somewhere, on at most TWO planets
///   • two planets that share a species are never rail-neighbours, and the
///     co-orbital twins never share anything — overlap next door is pointless
///   • every species has at least one DELICACY buyer on a planet where it is
///     NOT catchable, so every fish has a guaranteed route
///   • delicacies prefer far planets (the big payday needs the big trip),
///     imports prefer near ones (early routes stay feasible)
///   • hint from the handoff: each twin is a delicacy buyer for the other's
///     fish — the shortest hop in the system becomes the tutorial trade route
///
/// Planets, sizes and neighbours are read from the OPEN SCENE — where Sam put
/// the stands, how big the bodies are and where their rails sit — so the draft
/// tracks the real system rather than a list typed in here.
/// </summary>
public static class PlanetEconomyGenerator
{
    const int   Seed          = 20260907;
    const float MainRadius    = 150f;     // bodies at least this big are "mains"
    const float SameRailTol   = 60f;      // orbit radii closer than this share a rail (the twins)
    // 24 species each need ONE delicacy buyer somewhere they are not caught.
    // The first draft used 4 mains x 3 + 6 dwarfs x 2 = 24 slots exactly (the
    // handoff said dwarfs 1, which covers only 18 species). 2026-09-07, Sam's
    // playtest: 7 fish flown to a neighbour and only 2 sold above base — the
    // lists were too narrow to find a buyer. Delicacies were widened to 5 / 4
    // and IMPORTS to 9 / 10, which fixed the symptom the wrong way round.
    //
    // 2026-09-08. The reason a foreign fish so rarely found a buyer was never
    // that the lists were short — it was that they were drawn round-robin from
    // the whole pool, so a planet's imports had nothing to do with what its
    // NEIGHBOURS actually catch. Widening was the only lever left, and it took
    // the lists to 9-10 of the 18-21 foreign species: 65% of every possible
    // (where you fished, where you flew, what you carried) combination paid a
    // premium, and the 0.5x dump price almost never fired. Route knowledge was
    // worth 88% next door versus 65% anywhere — barely a signal.
    //
    // AssignImports now draws from the neighbours' catch instead (see there),
    // so a SIX-species list beats a ten-species one at the job it exists to do:
    //     short hop  86% premium (was 88%)
    //     long hop   35% premium (was 59%)
    //     overall    47%         (was 65%)
    // Measured over the shipped catch/delicacy tables; every ordered pair of
    // neighbours still has at least one premium buyer. Delicacies are unchanged
    // — 5 per main across 24 species is about two delicacy planets per fish,
    // which is exactly the route knowledge worth learning.
    const int   DelicaciesMain = 5, DelicaciesDwarf = 4;
    const int   ImportsMain    = 6, ImportsDwarf    = 6;

    class Planet
    {
        public string name;
        public bool   main;
        public float  orbitRadius;
        public int    rank;            // rail order from the sun; co-orbitals share a rank
        public Vector2 pos;
        /// <summary>A goods vendor stands on this body — crystals can be BOUGHT here.</summary>
        public bool   goodsVendor;
        /// <summary>The crystal spawner runs on this body — crystals can be MINED here.</summary>
        public bool   naturalCrystals;
        public List<string> catchable = new List<string>();
        public List<string> imports = new List<string>();
        public List<string> delicacies = new List<string>();
        public int SlotsPerTier => main ? 2 : 1;
        public int DelicacyCap  => main ? DelicaciesMain : DelicaciesDwarf;
        public int ImportCap    => main ? ImportsMain : ImportsDwarf;
    }

    // ── Menu ─────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Economy/Draft Planet Tables")]
    public static void Draft()
    {
        var planets = ReadPlanetsFromScene(out string err);
        if (planets == null) { Debug.LogError("[Economy] " + err); return; }

        var file = BuildDraft(planets, out string log);
        Directory.CreateDirectory(PlanetEconomy.Dir);

        string target = File.Exists(PlanetEconomy.File)
            ? Path.Combine(PlanetEconomy.Dir, "planet_economy.draft.json")
            : PlanetEconomy.File;
        File.WriteAllText(target, JsonUtility.ToJson(file, true));
        AssetDatabase.Refresh();

        var problems = Validate(file, planets);
        Debug.Log($"[Economy] wrote {target}\n{log}\n" +
                  (problems.Count == 0 ? "Validation: CLEAN."
                                       : "Validation: " + problems.Count + " problem(s):\n  " + string.Join("\n  ", problems)) +
                  (target != PlanetEconomy.File
                      ? "\n⚠ planet_economy.json already exists, so this went to planet_economy.draft.json — rename it to adopt it."
                      : ""));
        PlanetEconomy.Load();
    }

    [MenuItem("Tools/Economy/Validate Planet Tables")]
    public static void ValidateMenu()
    {
        var planets = ReadPlanetsFromScene(out string err);
        if (planets == null) { Debug.LogError("[Economy] " + err); return; }
        if (!File.Exists(PlanetEconomy.File)) { Debug.LogError("[Economy] no " + PlanetEconomy.File + " — run Draft first."); return; }

        var file = JsonUtility.FromJson<PlanetEconomy.EconomyFile>(File.ReadAllText(PlanetEconomy.File));
        var problems = Validate(file, planets);
        if (problems.Count == 0) Debug.Log("[Economy] planet_economy.json is CLEAN — every rule holds.");
        else Debug.LogError("[Economy] planet_economy.json has " + problems.Count + " problem(s):\n  " + string.Join("\n  ", problems));
    }

    // ── Scene → planets ──────────────────────────────────────────────────────

    static List<Planet> ReadPlanetsFromScene(out string err)
    {
        err = null;
        var bodies = UnityEngine.Object.FindObjectsOfType<CelestialBody>(true);
        CelestialBody sun = null;
        foreach (var b in bodies) if (b.bodyName == "Sun") sun = b;
        if (sun == null) { err = "No Sun in the open scene — open Assets/1.6.7.7.7.unity."; return null; }

        // Fuel sources, for the hard rule in §4.5 of the handoff: a fishable body
        // you can land on with an empty tank must offer SOME way to fill it, or
        // the run is over — the pod in the shuttle is the only save point, and
        // dying respawns you on the same rock.
        var sites = UnityEngine.Object.FindObjectsOfType<VendorSite>(true);
        var goodsBodies = new HashSet<string>();
        foreach (var s in sites)
        {
            if (s.kind != VendorSite.VendorKind.GoodsVendor) continue;
            var b = s.GetComponentInParent<CelestialBody>();
            if (b != null) goodsBodies.Add(b.bodyName);
        }
        // The crystal spawner grows crystals on EVERY body in the simulation
        // except the ones it is told to skip, so "has natural crystals" is its
        // exclusion list read backwards. A scene with no spawner at all has
        // crystals nowhere, which the rule correctly flags on every planet.
        var spawner = UnityEngine.Object.FindObjectOfType<CrystalSpawner>(true);
        HashSet<string> noCrystals = spawner == null ? null : new HashSet<string>();
        if (spawner != null && spawner.excludeBodyNames != null)
            foreach (var n in spawner.excludeBodyNames) if (!string.IsNullOrEmpty(n)) noCrystals.Add(n);

        var list = new List<Planet>();
        foreach (var site in sites)
        {
            if (site.kind != VendorSite.VendorKind.FishMarket) continue;
            var body = site.GetComponentInParent<CelestialBody>();
            if (body == null) continue;
            if (list.Exists(p => p.name == body.bodyName)) continue;
            Vector3 rel = body.transform.position - sun.transform.position;
            list.Add(new Planet
            {
                name = body.bodyName,
                main = body.radius >= MainRadius,
                orbitRadius = new Vector2(rel.x, rel.y).magnitude,
                pos = new Vector2(rel.x, rel.y),
                goodsVendor     = goodsBodies.Contains(body.bodyName),
                naturalCrystals = noCrystals != null && !noCrystals.Contains(body.bodyName),
            });
        }
        if (list.Count == 0) { err = "No FishMarket stands in the scene — place them first."; return null; }

        // Rail ranks: sort by orbit radius, collapse co-orbitals onto one rank.
        list.Sort((a, b) => a.orbitRadius.CompareTo(b.orbitRadius));
        int rank = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0 && list[i].orbitRadius - list[i - 1].orbitRadius > SameRailTol) rank++;
            list[i].rank = rank;
        }
        return list;
    }

    static bool Neighbours(Planet a, Planet b) => a != b && Math.Abs(a.rank - b.rank) <= 1;

    /// <summary>Closest the two ever get: co-orbitals hold a fixed gap, everyone
    /// else reaches |r1 − r2|. Same maths as OrbitRange / the distance table.</summary>
    static float ClosestApproach(Planet a, Planet b)
        => a.rank == b.rank ? Vector2.Distance(a.pos, b.pos) : Mathf.Abs(a.orbitRadius - b.orbitRadius);

    // ── The draft ────────────────────────────────────────────────────────────

    static PlanetEconomy.EconomyFile BuildDraft(List<Planet> planets, out string log)
    {
        var rng = new System.Random(Seed);
        var sb  = new StringBuilder();

        foreach (FishTier tier in new[] { FishTier.Common, FishTier.Uncommon, FishTier.Rare })
        {
            var pool = new List<string>();
            for (int i = 0; i < FishingRules.Species.Length; i++)
                if (FishingRules.Species[i].tier == tier && !FishingRules.Species[i].bounty)
                    pool.Add(FishingRules.Species[i].id);

            bool ok = AssignTier(planets, pool, tier, rng, out string why);
            sb.Append(tier).Append(": ").Append(ok ? "assigned" : "FAILED — " + why).Append('\n');
        }

        AssignDelicacies(planets, rng);
        AssignImports(planets, rng);

        var file = new PlanetEconomy.EconomyFile
        {
            version = 1,
            note = "Drafted by Tools > Economy > Draft Planet Tables. Hand-tune freely; run Validate afterwards. " +
                   "Words on the boards come from the bucket a species lands in, never from these lists' order.",
        };
        foreach (var p in planets)
            file.planets.Add(new PlanetEconomy.PlanetEntry
            {
                body = p.name, catchable = p.catchable, imports = p.imports, delicacies = p.delicacies,
            });

        log = sb.ToString().TrimEnd();
        return file;
    }

    /// <summary>Hand a tier's species out across the planets: each planet takes
    /// its slot count, no species on more than two planets, never on two
    /// neighbours, every species used. Greedy with retries on a seeded RNG, so
    /// the draft is reproducible.</summary>
    static bool AssignTier(List<Planet> planets, List<string> pool, FishTier tier, System.Random rng, out string why)
    {
        why = "";
        int slots = 0; foreach (var p in planets) slots += p.SlotsPerTier;
        if (pool.Count == 0) { why = "no species in tier"; return false; }
        if (slots > pool.Count * 2) { why = $"{slots} slots but only {pool.Count} species (max {pool.Count * 2} placements)"; return false; }

        for (int attempt = 0; attempt < 4000; attempt++)
        {
            var count = new Dictionary<string, int>();
            foreach (var s in pool) count[s] = 0;
            var where = new Dictionary<string, List<Planet>>();
            foreach (var s in pool) where[s] = new List<Planet>();
            var picks = new Dictionary<Planet, List<string>>();
            foreach (var p in planets) picks[p] = new List<string>();

            // Visit planets in a random order so exclusives don't always fall on
            // the same worlds.
            var order = new List<Planet>(planets);
            Shuffle(order, rng);

            bool failed = false;
            foreach (var p in order)
            {
                for (int k = 0; k < p.SlotsPerTier && !failed; k++)
                {
                    // Candidates: under the two-planet cap, not already here, and
                    // not on a neighbour. Prefer the least-used species so every
                    // species gets a home; break ties randomly.
                    var cands = new List<string>();
                    foreach (var s in pool)
                    {
                        if (count[s] >= 2 || picks[p].Contains(s)) continue;
                        bool clash = false;
                        foreach (var q in where[s]) if (Neighbours(p, q)) { clash = true; break; }
                        if (!clash) cands.Add(s);
                    }
                    if (cands.Count == 0) { failed = true; break; }
                    int minUse = int.MaxValue;
                    foreach (var s in cands) minUse = Math.Min(minUse, count[s]);
                    var best = cands.FindAll(s => count[s] == minUse);
                    string pick = best[rng.Next(best.Count)];
                    picks[p].Add(pick); count[pick]++; where[pick].Add(p);
                }
                if (failed) break;
            }
            if (failed) continue;

            bool allUsed = true;
            foreach (var s in pool) if (count[s] == 0) { allUsed = false; break; }
            if (!allUsed) continue;

            foreach (var p in planets) p.catchable.AddRange(picks[p]);
            return true;
        }
        why = "could not satisfy the neighbour rule after 4000 tries — move a stand or an orbit";
        return false;
    }

    /// <summary>Every species gets at least one delicacy buyer on a planet where
    /// it is NOT catchable, preferring the farthest such planet (the big payday
    /// needs the big trip). Twins first: each wants the other's fish.</summary>
    static void AssignDelicacies(List<Planet> planets, System.Random rng)
    {
        var all = AllSpecies();
        Shuffle(all, rng);

        // GUARANTEE FIRST, while every slot is still free: each species gets
        // one buyer on a planet where it is not caught. Scored by distance from
        // the fish's home waters (the big payday needs the big trip), with a
        // large bonus for the co-orbital twin of a home planet — the handoff's
        // hint that each twin should crave the other's catch, so the shortest
        // hop in the system is also the first trade route a player finds.
        foreach (var s in all)
        {
            Planet best = null; float bestScore = float.MinValue;
            foreach (var p in planets)
            {
                if (p.catchable.Contains(s) || p.delicacies.Count >= p.DelicacyCap) continue;
                float score = DistanceFromHomes(planets, p, s);
                foreach (var q in planets)
                    if (q != p && q.catchable.Contains(s) && q.rank == p.rank) { score += 1e6f; break; }
                if (score > bestScore) { bestScore = score; best = p; }
            }
            if (best != null) best.delicacies.Add(s);
        }

        // Any capacity left over goes to far fish, so mains reach their full list.
        foreach (var p in planets)
        {
            var cands = new List<string>();
            foreach (var s in all)
                if (!p.catchable.Contains(s) && !p.delicacies.Contains(s)) cands.Add(s);
            cands.Sort((x, y) => DistanceFromHomes(planets, p, y).CompareTo(DistanceFromHomes(planets, p, x)));
            for (int i = 0; i < cands.Count && p.delicacies.Count < p.DelicacyCap; i++)
                p.delicacies.Add(cands[i]);
        }
    }

    /// <summary>Imports: off-world species the market pays extra for, drawn from
    /// what this planet's ORBITAL NEIGHBOURS actually catch. Never overlaps the
    /// delicacy list.
    ///
    /// This is the whole point of the pass. A player who fishes at home and
    /// takes the short hop next door should find a buyer paying over the odds
    /// nearly every time — that is the loop, and it has to be reliable. A
    /// player who picks a planet at random should mostly find a market with no
    /// use for what they are carrying. Drawing the list from the neighbours'
    /// catch gets both at once; drawing it from the whole pool (what this used
    /// to do, sorted by distance) could only get the first by making the lists
    /// so long that the second became impossible.
    ///
    /// <b>Round-robin across the neighbours, not one at a time.</b> Puddle sits
    /// between the two co-orbital mains and Hearth, so its neighbours catch 15
    /// species between them and it can only list six. Taking them in order
    /// would give Hearth's three fish no buyer at all next door and quietly
    /// break the shortest route in that part of the system. Dealing one card
    /// per neighbour per pass gives every neighbour a share of a list that can
    /// never hold all of them.
    ///
    /// The neighbour relation is the same <see cref="Neighbours"/> the
    /// validator uses for the shared-species rule: adjacent rails, co-orbitals
    /// counted as one rank. Falls back to the old nearest-first fill only if a
    /// planet's neighbours cannot supply enough species — which no current
    /// layout does, but a hand-moved stand could.</summary>
    static void AssignImports(List<Planet> planets, System.Random rng)
    {
        foreach (var p in planets)
        {
            // One queue per neighbour, shuffled so the draft does not always
            // hand out the same species (still reproducible — seeded rng).
            var queues = new List<List<string>>();
            foreach (var q in planets)
            {
                if (!Neighbours(p, q)) continue;
                var take = new List<string>();
                foreach (var s in q.catchable)
                    if (!p.catchable.Contains(s) && !p.delicacies.Contains(s) && !take.Contains(s))
                        take.Add(s);
                Shuffle(take, rng);
                if (take.Count > 0) queues.Add(take);
            }

            int cursor = 0;
            while (p.imports.Count < p.ImportCap && queues.Count > 0)
            {
                var q = queues[cursor % queues.Count];
                string pick = q[q.Count - 1];
                q.RemoveAt(q.Count - 1);
                if (!p.imports.Contains(pick)) p.imports.Add(pick);
                if (q.Count == 0) { queues.Remove(q); if (queues.Count == 0) break; }
                else cursor++;
            }

            // Isolated planet (no neighbour with spare species): fall back to
            // the old behaviour so a table is never left empty.
            if (p.imports.Count < p.ImportCap)
            {
                var cands = new List<string>();
                foreach (var s in AllSpecies())
                    if (!p.catchable.Contains(s) && !p.delicacies.Contains(s) && !p.imports.Contains(s))
                        cands.Add(s);
                cands.Sort((x, y) => DistanceFromHomes(planets, p, x).CompareTo(DistanceFromHomes(planets, p, y)));
                for (int i = 0; i < cands.Count && p.imports.Count < p.ImportCap; i++)
                    p.imports.Add(cands[i]);
            }
        }
    }

    static float DistanceFromHomes(List<Planet> planets, Planet buyer, string species)
    {
        float best = float.MaxValue;
        foreach (var q in planets)
            if (q != buyer && q.catchable.Contains(species))
                best = Mathf.Min(best, ClosestApproach(buyer, q));
        return best == float.MaxValue ? 0f : best;
    }

    static bool HasDelicacyBuyer(List<Planet> planets, string s)
    {
        foreach (var p in planets)
            if (p.delicacies.Contains(s) && !p.catchable.Contains(s)) return true;
        return false;
    }

    static List<string> AllSpecies()
    {
        var l = new List<string>();
        for (int i = 0; i < FishingRules.Species.Length; i++)
            if (!FishingRules.Species[i].bounty) l.Add(FishingRules.Species[i].id);
        return l;
    }

    static void Shuffle<T>(List<T> l, System.Random rng)
    {
        for (int i = l.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); }
    }

    // ── Validation ───────────────────────────────────────────────────────────

    static List<string> Validate(PlanetEconomy.EconomyFile file, List<Planet> scenePlanets)
    {
        var problems = new List<string>();
        if (file == null) { problems.Add("file failed to parse"); return problems; }

        var byName = new Dictionary<string, PlanetEconomy.PlanetEntry>();
        foreach (var e in file.planets) if (e != null && !string.IsNullOrEmpty(e.body)) byName[e.body] = e;

        var sceneByName = new Dictionary<string, Planet>();
        foreach (var p in scenePlanets) sceneByName[p.name] = p;

        foreach (var p in scenePlanets)
            if (!byName.ContainsKey(p.name)) problems.Add($"{p.name} has a fish market but no table");

        // ── The one rule whose violation BRICKS A RUN ────────────────────────
        // Handoff §4.5: every fishable body has natural crystals OR a goods
        // vendor (or both). Land on one with neither and an empty tank and
        // there is no way to leave, no way to refuel, and no earlier save to go
        // back to — the stasis pod in the shuttle is the only save point, and
        // dying puts you back on the same rock. Every other rule in this file
        // costs the player a wasted trip; this one costs them the run. It was
        // written down in the handoff and never checked until 2026-09-08.
        foreach (var p in scenePlanets)
            if (!p.naturalCrystals && !p.goodsVendor)
                problems.Add($"⛔ {p.name} is fishable but has NO fuel source — no natural crystals " +
                             "(it is in CrystalSpawner.excludeBodyNames, or the scene has no spawner) " +
                             "and no goods vendor. Landing there with an empty tank is a permanent " +
                             "softlock. Add a goods vendor stand, or take it off the exclusion list.");

        var tierOf = new Dictionary<string, FishTier>();
        for (int i = 0; i < FishingRules.Species.Length; i++)
            if (!FishingRules.Species[i].bounty) tierOf[FishingRules.Species[i].id] = FishingRules.Species[i].tier;

        var homes = new Dictionary<string, List<string>>();
        foreach (var e in byName.Values)
        {
            foreach (var s in e.catchable)
            {
                if (!tierOf.ContainsKey(s)) { problems.Add($"{e.body}: unknown species '{s}'"); continue; }
                if (!homes.ContainsKey(s)) homes[s] = new List<string>();
                homes[s].Add(e.body);
            }
            // shape
            if (sceneByName.TryGetValue(e.body, out var sp))
            {
                var perTier = new Dictionary<FishTier, int>();
                foreach (var s in e.catchable) if (tierOf.TryGetValue(s, out var t)) perTier[t] = perTier.TryGetValue(t, out var c) ? c + 1 : 1;
                foreach (FishTier t in Enum.GetValues(typeof(FishTier)))
                {
                    int have = perTier.TryGetValue(t, out var c) ? c : 0;
                    if (have != sp.SlotsPerTier) problems.Add($"{e.body}: {have} {t} species, expected {sp.SlotsPerTier}");
                }
            }
            foreach (var s in e.delicacies) if (e.catchable.Contains(s)) problems.Add($"{e.body}: '{s}' is both catchable and a delicacy");
            foreach (var s in e.imports)    if (e.catchable.Contains(s)) problems.Add($"{e.body}: '{s}' is both catchable and an import");
            foreach (var s in e.imports)    if (e.delicacies.Contains(s)) problems.Add($"{e.body}: '{s}' is both an import and a delicacy");
        }

        foreach (var s in tierOf.Keys)
        {
            if (!homes.ContainsKey(s)) { problems.Add($"'{s}' is catchable nowhere"); continue; }
            if (homes[s].Count > 2) problems.Add($"'{s}' is catchable on {homes[s].Count} planets (max 2)");
            if (homes[s].Count == 2 && sceneByName.TryGetValue(homes[s][0], out var a) && sceneByName.TryGetValue(homes[s][1], out var b) && Neighbours(a, b))
                problems.Add($"'{s}' is shared by neighbours {a.name} and {b.name}");

            bool buyer = false;
            foreach (var e in byName.Values) if (e.delicacies.Contains(s) && !e.catchable.Contains(s)) { buyer = true; break; }
            if (!buyer) problems.Add($"'{s}' has no delicacy buyer on a planet where it is not catchable");
        }
        return problems;
    }
}
