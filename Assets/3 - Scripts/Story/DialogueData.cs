using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// ---- dialogue graph ----
[Serializable] public class Effect { public string kind; public string strArg = ""; public float numArg; public bool boolArg; }

[Serializable]
public class PlayerResponse
{
    public string buttonText = "";
    public string nextNodeId = "end";   // node id, or "end"
    public Effect[] effects;
    public string startHintTrack = "";  // presentation-only; "" = none
    public string requiresFlag = "";    // only shown if this flag is true ("" = always)
    public string hiddenIfFlag = "";    // hidden if this flag is true ("" = never)
}

[Serializable]
public class DialogueNode
{
    public string id = "";
    public string speaker = "AI";       // "AI" | "Tev"
    public string[] lines;
    public PlayerResponse[] responses;
}

[Serializable] public class Conversation { public string id = ""; public DialogueNode[] nodes; }

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
/// File conventions: conv_*.json = one Conversation each; objectives.json = ObjectiveFile;
/// hinttracks.json = HintTrackFile. JsonUtility only (no dicts/polymorphism in the JSON).
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
                if (file.StartsWith("conv_"))
                {
                    var c = JsonUtility.FromJson<Conversation>(json);
                    if (c != null && !string.IsNullOrEmpty(c.id)) Conversations[c.id] = c;
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
        Debug.Log($"[Story] Loaded {Conversations.Count} conversations, {Objectives.Count} objectives, {HintTracks.Count} hint tracks.");
    }

    public static Conversation GetConversation(string id) => id != null && Conversations.TryGetValue(id, out var c) ? c : null;
    public static Objective GetObjective(string id) => id != null && Objectives.TryGetValue(id, out var o) ? o : null;
    public static HintTrack GetHintTrack(string id) => id != null && HintTracks.TryGetValue(id, out var t) ? t : null;
}
