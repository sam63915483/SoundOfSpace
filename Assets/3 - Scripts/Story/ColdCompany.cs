using UnityEngine;

/// <summary>
/// "Cold Company" — Main Mission 1 (the moon run to Constant Companion). Begins once the
/// player holds the pilot licence (<see cref="Mission1.FlagLicensed"/>). This is the canonical
/// first real mission; see docs/GDD_VerticalSlice_Main1_ColdCompany.md and
/// docs/superpowers/specs/2026-07-08-cold-company-wiring-design.md.
///
/// Like <see cref="Mission1"/>, all state lives as named flags on <see cref="StoryDirector"/>
/// so it round-trips through the save system for free. This class is typed accessors over
/// those keys plus the mission's cross-system glue (the fish-bag reward, the compass guidance,
/// and the vendor completion hooks) so the vendor/dialogue edits stay one-liners.
/// </summary>
public static class ColdCompany
{
    // ── Progress flags (StoryDirector keys) ────────────────────────────────
    public const string FlagAssigned      = "cc_assigned";       // Tev gave the assignment + fish bag
    public const string FlagFishSold      = "cc_fish_sold";      // sold enough to afford a ship
    public const string FlagShipBought    = "cc_ship_bought";    // bought a flyable ship
    public const string FlagArrivedMoon   = "cc_arrived_moon";   // landed on Constant Companion
    public const string FlagEnteredBase   = "cc_entered_base";   // powered + opened the base door
    public const string FlagSawPhotoWall  = "cc_saw_photowall";  // read the black-hole photo wall
    public const string FlagSawReview     = "cc_saw_review";     // read the review station
    public const string FlagOpenedPodFile = "cc_opened_podfile"; // opened the pod / ORG??? file
    public const string FlagFirstLieDone  = "cc_first_lie_done"; // read the first-lie chat in the phone AI app
    public const string FlagGotRoute      = "cc_got_route";      // read the scrubbed nav route
    public const string FlagReported      = "cc_reported";       // gave Tev the report
    public const string FlagComplete      = "cc_complete";       // mission complete; Main 2 unlocked

    // Downstream: Tev's next ask (Cyclops) opens from here.
    public const string FlagMain2Available = "main2_cyclops_available";

    // ── Compass waypoint ids (owned by this mission) ───────────────────────
    public const string WpFishMarket = "cc_fishmarket";
    public const string WpShipVendor = "cc_shipvendor";
    public const string WpMoon       = "cc_moon";
    public const string WpBaseDoor   = "cc_basedoor";
    public const string WpTev        = "cc_tev";

    // ── Tuning ──────────────────────────────────────────────────────────────
    // The full ship (Ship44_Full.asset) costs exactly this. Tev's gift bag is stocked to
    // clear it with a little slack: 5 Rare trophy fish x 150 lb x $3/lb = $2,250.
    public const int ShipPrice           = 2000;
    public const string RewardFishTier   = "Rare";
    public const int RewardFishCount     = 5;
    public const int RewardFishWeightLbs = 150;

    const string MoonBodyName = "Constant Companion";

    // ── Flag helpers (null-safe; no-op if StoryDirector isn't up yet) ──────
    public static bool Get(string flag)
    {
        var sd = StoryDirector.Instance;
        return sd != null && sd.GetFlag(flag);
    }

    public static void Set(string flag, bool value = true)
    {
        var sd = StoryDirector.Instance;
        if (sd != null) sd.SetFlag(flag, value);
    }

    /// <summary>Mission is running: assigned but not yet complete.</summary>
    public static bool IsActive() => Get(FlagAssigned) && !Get(FlagComplete);

    /// <summary>Base done once the folder (pod file) has been opened → "return to Tev".</summary>
    public static bool ReadyToReport() => Get(FlagOpenedPodFile);

    // ── Beat 1: the fish-bag reward ────────────────────────────────────────
    /// <summary>
    /// Stock the player with Tev's gift bag: a fresh 5-slot FishBag filled with heavy rare
    /// catches worth ≥ the ship price. Mirrors the Bobber catch flow (log to the lifetime dex,
    /// then place into the bag). Returns false if there's no room to grant the bag — the caller
    /// should ask the player to free a slot and retry.
    /// </summary>
    public static bool GrantTevFishBag()
    {
        var hb = Hotbar.Instance;
        var fi = FishInventory.Instance;
        if (hb == null || fi == null) return false;

        // Ensure a bag exists to fill. If the player already carries one, fill that; otherwise
        // spawn a fresh bag (needs an empty hotbar slot).
        if (!hb.HasFishBagAnywhere())
        {
            if (!hb.HasEmptyHotbarSlot()) return false;
            if (!hb.TryAddBag()) return false;
        }

        int placed = 0;
        for (int i = 0; i < RewardFishCount; i++)
        {
            var entry = fi.AddFish(RewardFishTier, RewardFishWeightLbs);
            // Bag first, then loose hotbar slots as a fallback (same order as the catch flow).
            if (hb.TryAddFishToBag(entry) || hb.TryAddFish(entry)) placed++;
        }
        return placed > 0;
    }

    /// <summary>Called right after the assignment is accepted: HAL marks the fish market.</summary>
    public static void OnAssigned()
    {
        HALCommentator.Instance?.VolunteerExternal(
            "Marking the fish market on your compass — sell Tev's catch there for your ship money.", true);
        EnsureCompass();
    }

    // ── Vendor completion hooks (called from FishMarketNPC / ShipMarketNPC) ─
    /// <summary>
    /// Fish sold at the market. Only advances the mission once the wallet can actually afford a
    /// ship, so selling only part of the bag nudges rather than skipping ahead.
    /// </summary>
    public static void NotifyFishSold()
    {
        if (!IsActive() || Get(FlagFishSold)) return;

        int money = PlayerWallet.Instance != null ? PlayerWallet.Instance.Money : 0;
        if (money < ShipPrice)
        {
            HALCommentator.Instance?.VolunteerExternal(
                "Not quite enough yet — sell the rest of Tev's catch.", true);
            return;
        }

        Set(FlagFishSold, true);
        var sd = StoryDirector.Instance;
        if (sd != null) { sd.CompleteObjective("obj_cc_sellfish"); sd.StartObjective("obj_cc_buyship"); }

        HALCommentator.Instance?.VolunteerExternal(
            "That's more than enough for a ship. Marking the ship vendor — go and buy yourself one.", true);
        EnsureCompass();
    }

    /// <summary>A flyable ship was bought while the mission is active.</summary>
    public static void NotifyShipBought()
    {
        if (!IsActive() || Get(FlagShipBought)) return;

        Set(FlagShipBought, true);
        var sd = StoryDirector.Instance;
        if (sd != null) { sd.CompleteObjective("obj_cc_buyship"); sd.StartObjective("obj_cc_flymoon"); }

        HALCommentator.Instance?.VolunteerExternal(
            "She's yours. Constant Companion is marked — take her up when you're ready.", true);
        EnsureCompass();
    }

    // ── Beat 2: arrival at Constant Companion ──────────────────────────────
    static PlayerController _pc;

    /// <summary>
    /// Polled ~1/s by <see cref="ColdCompanyDirector"/> during the fly-to-moon step. Reuses the
    /// existing "which body am I on / am I grounded" primitives (no placed trigger, no forbidden
    /// atmosphere code) to detect touchdown on Constant Companion, on foot OR in the ship.
    /// </summary>
    public static void PollArrival()
    {
        if (!IsActive() || !Get(FlagShipBought) || Get(FlagArrivedMoon)) return;
        if (IsPlayerAtMoonSurface()) NotifyArrivedMoon();
    }

    static bool IsPlayerAtMoonSurface()
    {
        var moon = FindMoon();
        if (moon == null) return false;

        // In the ship: landed (touching ground) with the moon as the nearest body.
        var ship = Ship.PilotedInstance;
        if (ship != null)
            return ship.IsLanded && NearestBodyToSurface(ship.transform.position) == moon;

        // On foot: grounded with the moon as the reference body.
        if (_pc == null) _pc = Object.FindObjectOfType<PlayerController>();
        if (_pc == null) return false;
        return _pc.IsOnGround && _pc.ReferenceBody != null && _pc.ReferenceBody.bodyName == MoonBodyName;
    }

    static CelestialBody NearestBodyToSurface(Vector3 pos)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        CelestialBody best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            float d = Vector3.Distance(pos, b.Position) - b.radius;
            if (d < bestDist) { bestDist = d; best = b; }
        }
        return best;
    }

    static void NotifyArrivedMoon()
    {
        Set(FlagArrivedMoon, true);
        var sd = StoryDirector.Instance;
        if (sd != null) { sd.CompleteObjective("obj_cc_flymoon"); sd.StartObjective("obj_cc_investigate"); }

        // On landing, HAL only acknowledges + points to the base (the "left in a hurry" reveal
        // waits until the door is open — see NotifyEnteredBase).
        HALCommentator.Instance?.VolunteerExternal(
            "Touchdown. There's the base — marking it on your compass. Go take a look.", true);
        EnsureCompass();   // swaps the moon marker for the base-door marker
    }

    // ── Beat 2b: the base door opened (power puzzle solved) ─────────────────
    /// <summary>Called by <see cref="MoonBaseDoor"/> when the powered door opens.</summary>
    public static void NotifyEnteredBase()
    {
        if (!IsActive() || Get(FlagEnteredBase)) return;
        Set(FlagEnteredBase, true);
        var sd = StoryDirector.Instance;
        if (sd != null) { sd.CompleteObjective("obj_cc_investigate"); sd.StartObjective("obj_cc_search"); }

        // The reveal moment, now that they're actually inside.
        HALCommentator.Instance?.VolunteerExternal(
            "Looks like they left in a hurry. Whole place, just... dropped. See what you can find.", true);
        EnsureCompass();   // clears the base-door marker
    }

    // ── Beat 3: the base clues ──────────────────────────────────────────────
    public static void NotifyPhotoWall()     { if (IsActive()) Set(FlagSawPhotoWall); }
    public static void NotifyReviewStation() { if (IsActive()) Set(FlagSawReview); }
    public static void NotifyScrubbedRoute() { if (IsActive()) Set(FlagGotRoute); }

    /// <summary>Pod file is soft-gated: only opens after the photo wall has been seen.</summary>
    public static bool CanOpenPodFile() => Get(FlagSawPhotoWall);

    /// <summary>
    /// Called after the player views the pod-crash photo. The handler's first lie is delivered
    /// through the PHONE AI chat (not a world pop-up): it's queued for the AI app, and a red HAL
    /// line nudges the player to open their phone. The "head back to Tev" beat waits until the
    /// chat has actually been read (cc_first_lie_done, set by the conversation's closing reply).
    /// </summary>
    public static void NotifyPodFileOpened()
    {
        if (!IsActive() || Get(FlagOpenedPodFile)) return;
        Set(FlagOpenedPodFile);
        StoryDirector.Instance?.QueueConversation("conv_cc_first_lie");
        HALCommentator.Instance?.VolunteerExternal(
            "That photo... there's something you should understand. Open your phone — let's talk.", true);
    }

    /// <summary>Once the first-lie chat has been read, send the player back to Tev. Polled by the director.</summary>
    public static void AnnounceReturnIfReady()
    {
        if (!IsActive() || !ReadyToReport() || !Get(FlagFirstLieDone) || Get(FlagReported)) return;
        var sd = StoryDirector.Instance;
        if (sd == null) return;
        // Fire exactly once — the saved objective state is the guard (survives save/load).
        if (sd.IsObjectiveActive("obj_cc_report") || sd.IsObjectiveComplete("obj_cc_report")) return;
        sd.CompleteObjective("obj_cc_search");
        sd.StartObjective("obj_cc_report");
        HALCommentator.Instance?.VolunteerExternal(
            "That's everything here. Head back to Tev — marking him on your compass.", true);
        EnsureCompass();
    }

    /// <summary>Mission complete — clear our compass markers.</summary>
    public static void OnComplete()
    {
        var c = CompassHUD.Instance;
        if (c == null) return;
        c.RemoveWaypoint(WpFishMarket);
        c.RemoveWaypoint(WpShipVendor);
        c.RemoveWaypoint(WpMoon);
        c.RemoveWaypoint(WpBaseDoor);
        c.RemoveWaypoint(WpTev);
    }

    // ── Compass guidance ────────────────────────────────────────────────────
    /// <summary>
    /// Keep exactly one mission marker on the compass, matching the current step. Idempotent and
    /// churn-free (only touches the strip when the wanted marker is missing/wrong), so the
    /// director can call it every second to survive save/load and scene transitions — closure
    /// waypoints aren't serialized, so this re-establishes them.
    /// </summary>
    public static void EnsureCompass()
    {
        var c = CompassHUD.Instance;
        if (c == null) return;

        string want = null;
        if (IsActive())
        {
            if (!Get(FlagFishSold))         want = WpFishMarket;
            else if (!Get(FlagShipBought))  want = WpShipVendor;
            else if (!Get(FlagArrivedMoon)) want = WpMoon;
            else if (!Get(FlagEnteredBase)) want = WpBaseDoor;
            else if (ReadyToReport() && Get(FlagFirstLieDone) && !Get(FlagReported)) want = WpTev;
        }

        // Drop any of our markers that aren't the one we want right now.
        if (want != WpFishMarket) c.RemoveWaypoint(WpFishMarket);
        if (want != WpShipVendor) c.RemoveWaypoint(WpShipVendor);
        if (want != WpMoon)       c.RemoveWaypoint(WpMoon);
        if (want != WpBaseDoor)   c.RemoveWaypoint(WpBaseDoor);
        if (want != WpTev)        c.RemoveWaypoint(WpTev);

        if (want == null || c.HasWaypoint(want)) return;

        if (want == WpFishMarket) MarkFishMarket();
        else if (want == WpShipVendor) MarkShipVendor();
        else if (want == WpMoon) MarkMoon();
        else if (want == WpBaseDoor) MarkBaseDoor();
        else if (want == WpTev) MarkTev();
    }

    static void MarkFishMarket()
    {
        var v = Object.FindObjectOfType<FishMarketNPC>();
        if (v == null || CompassHUD.Instance == null) return;
        var t = v.transform;
        CompassHUD.Instance.AddWaypoint(WpFishMarket, () => t != null ? t.position : Vector3.zero, "Fish Market");
    }

    static void MarkShipVendor()
    {
        var v = Object.FindObjectOfType<ShipMarketNPC>();
        if (v == null || CompassHUD.Instance == null) return;
        var t = v.transform;
        CompassHUD.Instance.AddWaypoint(WpShipVendor, () => t != null ? t.position : Vector3.zero, "Ship Vendor");
    }

    static void MarkMoon()
    {
        var moon = FindMoon();
        if (moon == null || CompassHUD.Instance == null) return;
        var t = moon.transform;
        CompassHUD.Instance.AddWaypoint(WpMoon, () => t != null ? t.position : Vector3.zero, MoonBodyName);
    }

    static void MarkBaseDoor()
    {
        var ac = AirlockController.Instance;
        if (ac == null || CompassHUD.Instance == null) return;
        var t = ac.transform;
        CompassHUD.Instance.AddWaypoint(WpBaseDoor, () => t != null ? t.position : Vector3.zero, "Moon Base");
    }

    static void MarkTev()
    {
        var tev = Object.FindObjectOfType<TevDialogue>();
        if (tev == null || CompassHUD.Instance == null) return;
        var t = tev.transform;
        CompassHUD.Instance.AddWaypoint(WpTev, () => t != null ? t.position : Vector3.zero, "Tev");
    }

    /// <summary>The Constant Companion moon body, or null if not in the solar-system scene.</summary>
    public static CelestialBody FindMoon()
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        for (int i = 0; i < bodies.Length; i++)
            if (bodies[i] != null && bodies[i].bodyName == MoonBodyName) return bodies[i];
        return null;
    }
}
