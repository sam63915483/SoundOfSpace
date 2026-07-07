using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Mission 2 "The Quiet System" wiring hub. Auto-singleton (StoryDirector's
/// pattern — MUST also be seeded in MainMenuController.EnsureGameplaySingletons,
/// CLAUDE.md trap #1). Watches StoryDirector flags and drives the story-phase
/// machine + queued phone beats:
///
///   1. Phase 1 → 2: first dimension return OR 3+ Act 2 missions complete.
///   2. "Face Down" offer (conv_face_down) queues once, any time in Phase 2+.
///   3. ORG_Reveal bridge: dialogue effects can only set StoryDirector flags,
///      so when conv_interview sets "ORG_Reveal" this mirrors it onto
///      EarlyGameProgress.ORG_Reveal (AIStoryController then merges the gated
///      knowledge file on its own poll) and advances the phase to Resistant.
///   4. Queues conv_we_need_to_talk once Phase 3 + Interview_Done, after
///      snapshotting live game data into Talk_* flags (Mission2.PrecomputeTalkFlags).
///
/// Every queued conversation is guarded on StoryContent actually containing it,
/// so this component is INERT until the draft JSONs are copied from
/// docs/story-drafts/ into StreamingAssets/Story/ — safe to ship ahead of the
/// content. All decisions re-derive from flags, so save/load needs nothing new.
/// </summary>
public class Mission2Director : MonoBehaviour
{
    public static Mission2Director Instance { get; private set; }

    const float PollInterval = 1f;   // catch-up cadence; also covers late singletons
    float _pollTimer;
    bool _inHandle;                  // re-entrancy guard: handling sets flags → OnStoryStateChanged

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return; // trap #1: also seeded in MainMenuController
        var go = new GameObject("Mission2Director");
        DontDestroyOnLoad(go);
        go.AddComponent<Mission2Director>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (StoryDirector.Instance != null)
            StoryDirector.Instance.OnStoryStateChanged += HandleChange;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            if (StoryDirector.Instance != null)
                StoryDirector.Instance.OnStoryStateChanged -= HandleChange;
            Instance = null;
        }
    }

    void Update()
    {
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        _pollTimer += Time.deltaTime;
        if (_pollTimer < PollInterval) return;
        _pollTimer = 0f;
        // Late-subscribe if StoryDirector spawned after us (seeding order varies
        // between editor play and the MainMenu boot path). Idempotent: -= then +=.
        var sd = StoryDirector.Instance;
        if (sd != null) { sd.OnStoryStateChanged -= HandleChange; sd.OnStoryStateChanged += HandleChange; }
        HandleChange();
    }

    void HandleChange()
    {
        if (_inHandle) return;
        _inHandle = true;
        try
        {
            var sd = StoryDirector.Instance;
            var kb = GameKnowledgeBase.Instance;
            if (sd == null || kb == null) return;

            // 1. Phase 1 → 2 gate.
            if (kb.CurrentPhase == StoryPhase.Phase1_Loyal &&
                (Mission2.Get(Mission2.FlagDimensionReturned) || Mission2.Act2MissionCount() >= 3))
            {
                kb.SetStoryPhase(StoryPhase.Phase2_Uneasy);
            }

            // 2. "Face Down" — HAL's private request, offered once in Phase 2+.
            if (kb.CurrentPhase >= StoryPhase.Phase2_Uneasy &&
                !Mission2.Get(Mission2.FlagFaceDownOffered) &&
                !sd.HasPendingConversation &&
                StoryContent.GetConversation("conv_face_down") != null)
            {
                Mission2.Set(Mission2.FlagFaceDownOffered);
                sd.QueueConversation("conv_face_down");
                if (PlayerPhoneUI.Instance != null)
                    PlayerPhoneUI.Instance.FlashNotification("Incoming transmission");
            }

            // 3. ORG_Reveal bridge (the one hand-off dialogue effects can't do).
            if (Mission2.Get(Mission2.FlagOrgReveal) && !EarlyGameProgress.ORG_Reveal)
            {
                EarlyGameProgress.ORG_Reveal = true;   // AIStoryController merges the gated knowledge file
                kb.SetStoryPhase(StoryPhase.Phase3_Resistant);
                Debug.Log("[Mission2Director] ORG_Reveal bridged: EarlyGameProgress set + phase → Resistant.");
            }

            // 4. Queue the Phase 3 confrontation (once), with live data snapshotted first.
            if (kb.CurrentPhase == StoryPhase.Phase3_Resistant &&
                Mission2.Get(Mission2.FlagInterviewDone) &&
                !Mission2.Get(Mission2.FlagTalkQueued) &&
                !sd.HasPendingConversation &&
                StoryContent.GetConversation("conv_we_need_to_talk") != null)
            {
                Mission2.PrecomputeTalkFlags();
                Mission2.Set(Mission2.FlagTalkQueued);
                sd.QueueConversation("conv_we_need_to_talk");
                if (PlayerPhoneUI.Instance != null)
                    PlayerPhoneUI.Instance.FlashNotification("Incoming transmission");
            }
        }
        finally { _inHandle = false; }
    }
}
