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

    void Update () {
        if ((Application.isEditor || !disableInBuild) && Application.isPlaying && cheatsEnabled) {
            if (Input.GetKeyDown (flyShip)) {
               // FindObjectOfType<Ship> ().StartFlying (FindObjectOfType<PlayerController> ());
            }
            if (Input.GetKeyDown(toggleORGReveal)) {
                EarlyGameProgress.ORG_Reveal = !EarlyGameProgress.ORG_Reveal;
                Debug.Log($"[CheatCodes] EarlyGameProgress.ORG_Reveal toggled → {EarlyGameProgress.ORG_Reveal}");
            }
        }
    }
}
