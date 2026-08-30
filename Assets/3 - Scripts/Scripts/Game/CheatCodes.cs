using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CheatCodes : MonoBehaviour {

    public bool cheatsEnabled = true;
    public bool disableInBuild = true;

    public KeyCode flyShip = KeyCode.Plus;

    // Toggles EarlyGameProgress.ORG_Reveal — the AI knowledge-gating
    // placeholder. With this on, AIStoryController merges the gated
    // ORG knowledge file into GameKnowledgeBase, unlocking Phase 2/3
    // personas. Used for jailbreak testing the gate (see
    // docs/superpowers/plans/2026-05-23-ai-companion-knowledge-gating-and-org-placeholder.md).
    public KeyCode toggleORGReveal = KeyCode.F9;


    // Skips the entire opening loop (survive → village → Tev → vendors → report → fork) and
    // drops you straight into the Pilot branch, ready to take the flight test — so you don't
    // have to replay the start every time. Sets the story/mission flags, picks the Pilot fork,
    // grants tools + money for the test.
    public KeyCode skipToPilotSchool = KeyCode.F8;

    // Jumps the galactic clock forward one day, or a whole week with SHIFT.
    // Rent is charged per day and a day is 24 REAL MINUTES, so testing Tev's
    // five-day plugin lockout by waiting is a two-hour session. The jump goes
    // through the normal rollover, so rent accrues exactly as it would have.
    public KeyCode skipGameDay = KeyCode.F7;

    void Update () {
        if ((Application.isEditor || !disableInBuild) && Application.isPlaying && cheatsEnabled) {
            if (Input.GetKeyDown (flyShip)) {
               // FindObjectOfType<Ship> ().StartFlying (FindObjectOfType<PlayerController> ());
            }
            if (Input.GetKeyDown(toggleORGReveal)) {
                EarlyGameProgress.ORG_Reveal = !EarlyGameProgress.ORG_Reveal;
                Debug.Log($"[CheatCodes] EarlyGameProgress.ORG_Reveal toggled → {EarlyGameProgress.ORG_Reveal}");
            }
            if (Input.GetKeyDown(skipToPilotSchool)) {
                SkipToPilotSchool();
            }
            if (Input.GetKeyDown(skipGameDay)) {
                SkipGameDay();
            }
        }
    }

    void SkipGameDay() {
        var clock = GalaxyTime.Instance;
        if (clock == null) {
            Debug.LogWarning("[CheatCodes] No GalaxyTime in the scene — nothing to advance.");
            return;
        }

        bool week = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        int days = week ? GalaxyTime.DaysPerWeek : 1;
        clock.DevAdvanceDays(days);

        // Reported from the ledger rather than predicted, so this log is a
        // reading of what actually happened and not a second implementation of
        // the rent rules.
        string rent = FeatureVault.TevRent
            ? $"Rent bills on the rollover next frame; currently ${MushroomQuest.RentBalance} owed, " +
              $"{MushroomQuest.UnpaidDays} day(s) behind, plugins " +
              (MushroomQuest.PluginsLocked ? "LOCKED" : "open") + "."
            : "Rent is vaulted (FeatureVault.TevRent) — nothing bills.";
        Debug.Log($"[CheatCodes] Clock +{days} day(s) → DAY {clock.Day}. " + rent);
    }

    void SkipToPilotSchool() {
        // Shared-opening flags.
        var sd = StoryDirector.Instance;
        if (sd != null) {
            sd.SetFlag("hasWater", true);
            sd.SetFlag("hasFood", true);
            sd.SetFlag("villageReached", true);
            sd.SetStoryStep(StoryStep.PilotSchool);
            sd.StartObjective("obj_pilot_school");
        }
        // Legacy early-game flags (vendors visited, Tev intro done).
        EarlyGameProgress.TevReturnedDialogueDone = true;
        EarlyGameProgress.FishVendorVisited = true;
        EarlyGameProgress.GoodsVendorVisited = true;

        // Mission 1 state → Pilot branch, briefed, ready to take the test.
        Mission1.Set(Mission1.FlagMetTevVillage, true);
        Mission1.Set(Mission1.FlagReported, true);
        Mission1.Set(Mission1.FlagInstructorBriefed, true);
        Mission1.SetBranch(Mission1.Branch.Pilot);
        Mission1.Set(Mission1.FlagPilotStarted, true);

        // Tools + money so everything's testable (axe/pistol/jetpack + cash for the $20 test).
        var axe = FindObjectOfType<AxeController>();    if (axe != null)    axe.Unlock();
        var pistol = FindObjectOfType<PistolController>(); if (pistol != null) pistol.Unlock();
        if (PlayerWallet.Instance != null) PlayerWallet.Instance.AddMoney(100);

        Debug.Log("[CheatCodes] Skipped to Pilot School — Pilot branch active, briefed, $500 granted. Head to the ship school.");
    }
}
