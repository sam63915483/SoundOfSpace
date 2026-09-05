using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// ---- dialogue graph ----
//
// One schema serves two surfaces:
//   • conv_*.json — the phone / HAL conversations (PhoneDialoguePresenter,
//     WorldDialogueUI via DialogueRunner).
//   • npc_*.json  — a world NPC's whole conversation (NpcGraphWalker, driven
//     by the NPC's own typewriter + PostGreetingChoicePanel).
// The npc_ files use the extra fields below (routes / onEnter / conditions /
// pickRandomLine / nextNodeId). JsonUtility fills what a file lacks with the
// defaults, so every conv_*.json written before 2026-09-05 loads unchanged.
//
// Both files are authored with tools/dialogue-studio (localhost editor +
// player). Keep this schema and tools/dialogue-studio/app.js in lockstep —
// the browser player mirrors NpcGraphWalker's rules exactly.

[Serializable] public class Effect { public string kind; public string strArg = ""; public float numArg; public bool boolArg; }

/// <summary>
/// A gate on a response (must all pass to show the button) or on a route
/// (must all pass to take the jump). Kinds — keep DialogueConditions.Passes
/// and the studio's vocab.json in lockstep:
///   Flag arg            StoryDirector flag is true
///   MoneyAtLeast num    PlayerWallet.Money >= num
///   HasItem arg [num]   Hotbar total of ItemId arg >= max(1, num)
///   CounterAtLeast arg num
///   ObjectiveDone arg
///   Probe arg           the NPC's own runtime check (e.g. kidFollowing, traxOwned)
/// negate flips the result.
/// </summary>
[Serializable]
public class Condition
{
    public string kind = "Flag";
    public string arg = "";
    public float num;
    public bool negate;
}

/// <summary>Conditional jump evaluated before a node's lines. First match wins.</summary>
[Serializable]
public class Route
{
    public Condition[] conditions;
    public string nextNodeId = "end";
}

[Serializable]
public class PlayerResponse
{
    public string buttonText = "";
    public string nextNodeId = "end";   // node id, or "end"
    public Effect[] effects;
    public string startHintTrack = "";  // presentation-only; "" = none
    public string requiresFlag = "";    // only shown if this flag is true ("" = always)
    public string hiddenIfFlag = "";    // hidden if this flag is true ("" = never)
    // -- appended 2026-09-05 (npc graphs) --
    public Condition[] conditions;      // all must pass for the button to show
}

[Serializable]
public class DialogueNode
{
    public string id = "";
    public string speaker = "AI";       // "AI" | "Tev" | any display name
    public string[] lines;
    public PlayerResponse[] responses;
    // -- appended 2026-09-05 (npc graphs) --
    public Route[] routes;              // evaluated BEFORE lines; first match jumps (a node with routes and no lines is a pure switch)
    public Effect[] onEnter;            // fired as the lines start
    public bool pickRandomLine;         // speak ONE of lines at random instead of all of them
    public string nextNodeId = "";      // auto-continue after the lines when there are no (visible) responses; "" = end
}

/// <summary>A one-click state for the studio's player ("Kid following you"). Unity ignores it.</summary>
[Serializable]
public class TestPreset
{
    public string name = "";
    public string[] flags;              // "flag_name" or "flag_name=false"
    public int money = -1;              // -1 = leave alone
    public string[] items;              // "ItemId:count"
    public string[] probes;             // probe names that read true
}

[Serializable]
public class Conversation
{
    public string id = "";
    public DialogueNode[] nodes;
    // -- appended 2026-09-05 (npc graphs) --
    public string kind = "";            // "npc" | "phone" ("" = phone, the pre-studio default)
    public string displayName = "";     // speaker plate / roster card title
    public TestPreset[] testPresets;

    /// Start node: the one called "start", else the first in the file.
    public DialogueNode StartNode
    {
        get
        {
            if (nodes == null || nodes.Length == 0) return null;
            foreach (var n in nodes) if (n != null && n.id == "start") return n;
            return nodes[0];
        }
    }

    public DialogueNode FindNode(string nodeId)
    {
        if (nodes == null || string.IsNullOrEmpty(nodeId)) return null;
        foreach (var n in nodes) if (n != null && n.id == nodeId) return n;
        return null;
    }
}

// ---- objectives ----
[Serializable]
public class Objective
{
    public string id = "";
    public string description = "";
    public string completionEvent = "";  // OnCookedFoodEaten | OnCleanWaterDrunk | OnShelterBuilt | OnVillageReached
    public Effect[] onComplete;
    public string hintTrackId = "";
}
[Serializable] public class ObjectiveFile { public Objective[] objectives; }

// ---- hint tracks ----
// advanceEvent advances the entry on a named gameplay event; gatherWoodTarget (>0) instead
// makes the entry a wood-gather gate that advances once WoodInventory.Wood reaches it (and is
// skipped on sight if the player already holds that much). Leave one of the two empty/0.
[Serializable] public class HintEntry { public string tipText = ""; public string advanceEvent = ""; public int gatherWoodTarget = 0; }
[Serializable] public class HintTrack { public string id = ""; public string objectiveId = ""; public HintEntry[] entries; }
[Serializable] public class HintTrackFile { public HintTrack[] tracks; }

/// <summary>
/// Loads all authored content from StreamingAssets/Story at runtime.
/// File conventions: conv_*.json = one phone Conversation each; npc_*.json = one
/// world-NPC Conversation each (same class, extra fields); objectives.json =
/// ObjectiveFile; hinttracks.json = HintTrackFile. JsonUtility only (no
/// dicts/polymorphism in the JSON).
/// </summary>
public static class StoryContent
{
    public static readonly Dictionary<string, Conversation> Conversations = new Dictionary<string, Conversation>();
    public static readonly Dictionary<string, Objective>    Objectives    = new Dictionary<string, Objective>();
    public static readonly Dictionary<string, HintTrack>    HintTracks    = new Dictionary<string, HintTrack>();
    public static bool Loaded { get; private set; }

    public static string StoryDir => Path.Combine(Application.streamingAssetsPath, "Story");

    public static void LoadAll(bool force = false)
    {
        if (Loaded && !force) return;
        Conversations.Clear(); Objectives.Clear(); HintTracks.Clear();
        if (!Directory.Exists(StoryDir)) { Debug.LogWarning("[Story] No Story dir at " + StoryDir); Loaded = true; return; }

        foreach (var path in Directory.GetFiles(StoryDir, "*.json"))
        {
            string file = Path.GetFileName(path).ToLowerInvariant();
            string json = File.ReadAllText(path);
            try
            {
                if (file.StartsWith("conv_") || file.StartsWith("npc_"))
                {
                    var c = JsonUtility.FromJson<Conversation>(json);
                    if (c != null && !string.IsNullOrEmpty(c.id))
                    {
                        if (string.IsNullOrEmpty(c.kind)) c.kind = file.StartsWith("npc_") ? "npc" : "phone";
                        Conversations[c.id] = c;
                    }
                }
                else if (file == "objectives.json")
                {
                    var f = JsonUtility.FromJson<ObjectiveFile>(json);
                    if (f?.objectives != null) foreach (var o in f.objectives) if (!string.IsNullOrEmpty(o.id)) Objectives[o.id] = o;
                }
                else if (file == "hinttracks.json")
                {
                    var f = JsonUtility.FromJson<HintTrackFile>(json);
                    if (f?.tracks != null) foreach (var t in f.tracks) if (!string.IsNullOrEmpty(t.id)) HintTracks[t.id] = t;
                }
            }
            catch (Exception e) { Debug.LogError($"[Story] Failed to parse {file}: {e.Message}"); }
        }
        Loaded = true;
    }

    public static Conversation GetConversation(string id) => id != null && Conversations.TryGetValue(id, out var c) ? c : null;
    public static Objective GetObjective(string id) => id != null && Objectives.TryGetValue(id, out var o) ? o : null;
    public static HintTrack GetHintTrack(string id) => id != null && HintTracks.TryGetValue(id, out var t) ? t : null;

    /// <summary>
    /// An NPC's graph, or null when there is no npc_&lt;id&gt;.json (the caller
    /// then runs its legacy C# conversation — that fallback is the safety net
    /// for the whole data-driven layer). In the Editor this re-reads the Story
    /// folder every call, so a Save in Dialogue Studio is live on the next
    /// talk without leaving Play mode. Builds read the files once.
    /// </summary>
    public static Conversation GetNpcGraph(string graphId)
    {
        if (string.IsNullOrEmpty(graphId)) return null;
#if UNITY_EDITOR
        LoadAll(force: true);
#else
        LoadAll();
#endif
        var c = GetConversation(graphId);
        return c != null && c.nodes != null && c.nodes.Length > 0 ? c : null;
    }
}
