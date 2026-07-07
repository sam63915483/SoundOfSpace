using UnityEditor;
using UnityEngine;

/// <summary>
/// Story/Mission 2 debug panel (INTEGRATION_HANDOFF.md, Chunk 0). Play-mode
/// tool: set the HAL story phase, flip any Mission2 flag, queue a phone
/// conversation, or open one on the WorldDialogueUI — so mission beats can be
/// tested without playing the game up to them.
///
/// Menu: Window ▸ Story ▸ Mission 2 Debug
/// </summary>
public class Mission2DebugWindow : EditorWindow
{
    static readonly string[] Flags =
    {
        Mission2.FlagFaceDownOffered, Mission2.FlagFaceDownAccepted, Mission2.FlagFaceDownRefused,
        Mission2.FlagFaceDownWaitDone, Mission2.FlagFaceDownDone,
        Mission2.FlagMoonOfferTaken, Mission2.FlagMoonDelivered,
        Mission2.FlagClaimsOfferTaken, Mission2.FlagFieryClaims,
        Mission2.FlagTevLetterGiven, Mission2.FlagIceyReached, Mission2.FlagIceyVisited,
        Mission2.FlagLedgerHeld, Mission2.FlagLedgerToORG, Mission2.FlagLedgerKept, Mission2.FlagLedgerDelivered,
        Mission2.FlagBeanOfferTaken, Mission2.FlagBeanSalvage,
        Mission2.FlagPupilReading, Mission2.FlagLightsOnTaken, Mission2.FlagShadowRescue,
        Mission2.FlagRebelMet, Mission2.FlagRebelContact, Mission2.FlagCoverSetDone,
        Mission2.FlagInterviewDeniedName, Mission2.FlagInterviewLied, Mission2.FlagInterviewDone,
        Mission2.FlagOrgReveal,
        Mission2.FlagTradeBackHasFish, Mission2.FlagTradeBackHasGuitar,
        Mission2.FlagCassetteSixOwned, Mission2.FlagCassetteSixHeard,
        Mission2.FlagPaleOneSeen, Mission2.FlagOwnersAllFound, Mission2.FlagDimensionReturned,
        Mission2.FlagTalkQueued, Mission2.FlagTalkHasKills, Mission2.FlagTalkCleanHands,
        Mission2.FlagTalkManyDeaths, Mission2.FlagTalkAgreed, Mission2.FlagAtTheDoor,
        Mission2.FlagEndingRelease, Mission2.FlagEndingStay, Mission2.FlagEndingHandover,
    };

    Vector2 _scroll;
    string _convId = "conv_menu";

    [MenuItem("Window/Story/Mission 2 Debug")]
    static void Open() => GetWindow<Mission2DebugWindow>("Mission 2 Debug");

    void OnGUI()
    {
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to use this panel.", MessageType.Info);
            return;
        }
        var sd = StoryDirector.Instance;
        var kb = GameKnowledgeBase.Instance;
        if (sd == null) { EditorGUILayout.HelpBox("No StoryDirector yet.", MessageType.Warning); return; }

        // ── Phase ──
        EditorGUILayout.LabelField("HAL Phase", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Current: " + (kb != null ? kb.CurrentPhase.ToString() : "no GameKnowledgeBase"));
        EditorGUILayout.HelpBox("Phase only advances FORWARD. Going back = New Game / reload.", MessageType.None);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = kb != null;
            if (GUILayout.Button("→ Phase 2 Uneasy"))    kb.SetStoryPhase(StoryPhase.Phase2_Uneasy);
            if (GUILayout.Button("→ Phase 3 Resistant")) kb.SetStoryPhase(StoryPhase.Phase3_Resistant);
            GUI.enabled = true;
        }

        EditorGUILayout.Space();

        // ── Conversations ──
        EditorGUILayout.LabelField("Conversations", EditorStyles.boldLabel);
        _convId = EditorGUILayout.TextField("Conversation id", _convId);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Open on WorldDialogueUI")) WorldDialogueUI.Begin(_convId);
            if (GUILayout.Button("Queue on phone")) sd.QueueConversation(_convId);
        }
        EditorGUILayout.LabelField("Pending on phone: " + (sd.HasPendingConversation ? sd.PendingConversationId : "(none)"));
        if (GUILayout.Button("Precompute Talk_* flags now")) Mission2.PrecomputeTalkFlags();

        EditorGUILayout.Space();

        // ── Flags ──
        EditorGUILayout.LabelField("Mission 2 Flags  (Act 2 done: " + Mission2.Act2MissionCount() + ")", EditorStyles.boldLabel);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var f in Flags)
        {
            bool cur = sd.GetFlag(f);
            bool next = EditorGUILayout.ToggleLeft(f, cur);
            if (next != cur) sd.SetFlag(f, next);
        }
        EditorGUILayout.EndScrollView();
    }
}
