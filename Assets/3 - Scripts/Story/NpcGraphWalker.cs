using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Walks an npc_*.json Conversation using whatever typewriter and choice
/// panel the hosting NPC script already has. The script hands over five
/// delegates and yields <see cref="Run"/> from its own coroutine; nothing
/// about its range checks, prompt, cursor or audio changes.
///
///     var w = new NpcGraphWalker {
///         Speak      = SpeakOne,               // IEnumerator: type the line, wait for the click
///         Choose     = ChooseLabels,           // IEnumerator: show the panel, wait for a pick / walk-away
///         LastChoice = () => _choice,          // -1 = walked away
///         InRange    = () => _playerInRange,
///         Probe      = name => ...,            // optional: per-NPC conditions
///         Action     = name => ...,            // optional: per-NPC effects
///     };
///     yield return w.Run(graph);
///
/// Rules (the browser player in tools/dialogue-studio mirrors these exactly):
///   1. Start at the node called "start", else the first node.
///   2. On entering a node, try its routes in order; the first whose
///      conditions all pass jumps to its node (lines are NOT spoken).
///   3. Fire the node's onEnter effects.
///   4. Speak the lines (one at random if pickRandomLine), resolving
///      {TOKENS} through TokenResolver.
///   5. Show the responses whose gates pass (requiresFlag / hiddenIfFlag /
///      conditions). None visible → follow nextNodeId ("" = end).
///   6. Picking a response fires its effects and hint track, then goes to
///      its nextNodeId. Walking away ends the conversation with nothing
///      further fired.
/// A hop guard stops runaway route loops.
/// </summary>
public class NpcGraphWalker
{
    public Func<string, IEnumerator> Speak;
    public Func<IList<string>, IEnumerator> Choose;
    public Func<int> LastChoice;
    public Func<bool> InRange;
    public Func<string, bool> Probe;
    public Func<string, bool> Action;

    /// True when the graph reached an end on its own (not a walk-away).
    public bool Ended { get; private set; }
    public string LastNodeId { get; private set; }

    const int MaxHops = 200;

    /// Hosts without their own choice UI leave Choose/LastChoice null and get
    /// the shared PostGreetingChoicePanel (the vendors' Buy / Leave panel).
    readonly ChoiceBox _sharedBox = new ChoiceBox();
    public class ChoiceBox { public int Value = -1; }

    public static IEnumerator ChooseOnSharedPanel(IList<string> labels, ChoiceBox box, Func<bool> inRange)
    {
        box.Value = -1;
        var panel = PostGreetingChoicePanel.Instance;
        if (panel == null) { Debug.LogWarning("[Dialogue] No PostGreetingChoicePanel in scene."); yield break; }
        var rows = new List<PostGreetingChoicePanel.Row>(labels.Count);
        for (int i = 0; i < labels.Count; i++) rows.Add(new PostGreetingChoicePanel.Row(labels[i], true));
        panel.Show(rows, i => box.Value = i);
        yield return new WaitUntil(() => box.Value >= 0 || (inRange != null && !inRange()));
        if (panel.IsVisible) panel.Hide();
    }

    public IEnumerator Run(Conversation graph, string startNodeId = null)
    {
        Ended = false;
        if (graph == null) yield break;
        if (Choose == null)
        {
            Choose = labels => ChooseOnSharedPanel(labels, _sharedBox, InRange);
            LastChoice = () => _sharedBox.Value;
        }
        var node = string.IsNullOrEmpty(startNodeId) ? graph.StartNode : graph.FindNode(startNodeId);
        int hops = 0;

        while (node != null)
        {
            LastNodeId = node.id;
            if (++hops > MaxHops)
            {
                Debug.LogWarning($"[Dialogue:{graph.id}] more than {MaxHops} hops — route loop? Stopping at '{node.id}'.");
                break;
            }

            // 2. routes
            Route taken = null;
            if (node.routes != null)
                for (int i = 0; i < node.routes.Length && taken == null; i++)
                    if (node.routes[i] != null && DialogueConditions.AllPass(node.routes[i].conditions, Probe))
                        taken = node.routes[i];
            if (taken != null) { node = Next(graph, taken.nextNodeId); continue; }

            // 3. on-enter effects
            DialogueEffects.Apply(node.onEnter, Action);

            // 4. lines
            if (node.lines != null && node.lines.Length > 0 && Speak != null)
            {
                if (node.pickRandomLine)
                {
                    string pick = node.lines[UnityEngine.Random.Range(0, node.lines.Length)];
                    if (!StillHere()) yield break;
                    if (!string.IsNullOrEmpty(pick)) yield return Speak(TokenResolver.Resolve(pick));
                }
                else
                {
                    for (int i = 0; i < node.lines.Length; i++)
                    {
                        if (!StillHere()) yield break;
                        if (string.IsNullOrEmpty(node.lines[i])) continue;
                        yield return Speak(TokenResolver.Resolve(node.lines[i]));
                    }
                }
            }
            if (!StillHere()) yield break;

            // 5. responses
            var visible = VisibleResponses(node);
            if (visible.Count == 0) { node = Next(graph, node.nextNodeId); continue; }

            if (Choose == null || LastChoice == null) { Debug.LogWarning($"[Dialogue:{graph.id}] node '{node.id}' has responses but the host gave no Choose delegate."); break; }
            var labels = new List<string>(visible.Count);
            for (int i = 0; i < visible.Count; i++) labels.Add(TokenResolver.Resolve(visible[i].buttonText ?? ""));
            yield return Choose(labels);
            int pick2 = LastChoice();
            if (pick2 < 0 || pick2 >= visible.Count) yield break;   // walked away

            // 6. take it
            var r = visible[pick2];
            DialogueEffects.Apply(r.effects, Action);
            if (!string.IsNullOrEmpty(r.startHintTrack) && HintTrackRunner.Instance != null)
                HintTrackRunner.Instance.StartTrack(r.startHintTrack);
            node = Next(graph, r.nextNodeId);
        }
        Ended = true;
    }

    bool StillHere() => InRange == null || InRange();

    List<PlayerResponse> VisibleResponses(DialogueNode node)
    {
        var list = new List<PlayerResponse>();
        if (node.responses == null) return list;
        var sd = StoryDirector.Instance;
        foreach (var r in node.responses)
        {
            if (r == null) continue;
            if (!string.IsNullOrEmpty(r.requiresFlag) && (sd == null || !sd.GetFlag(r.requiresFlag))) continue;
            if (!string.IsNullOrEmpty(r.hiddenIfFlag) && sd != null && sd.GetFlag(r.hiddenIfFlag)) continue;
            if (!DialogueConditions.AllPass(r.conditions, Probe)) continue;
            list.Add(r);
        }
        return list;
    }

    static DialogueNode Next(Conversation graph, string nextId)
    {
        if (string.IsNullOrEmpty(nextId) || nextId == "end") return null;
        var n = graph.FindNode(nextId);
        if (n == null) Debug.LogWarning($"[Dialogue:{graph.id}] missing node '{nextId}' — ending.");
        return n;
    }
}
