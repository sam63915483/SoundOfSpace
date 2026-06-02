using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Speaker-agnostic engine that walks a Conversation through a DialoguePresenter.
/// Filters responses by StoryDirector flags, applies a chosen response's effects + hint track,
/// then navigates to the next node. Pure logic; no UnityEngine UI here.
/// </summary>
public class DialogueRunner
{
    readonly Conversation _conv;
    readonly DialoguePresenter _view;

    public DialogueRunner(Conversation conv, DialoguePresenter view) { _conv = conv; _view = view; }

    public void Start() => GoToNode(_conv?.nodes != null && _conv.nodes.Length > 0 ? _conv.nodes[0].id : "end");

    public void StartAt(string nodeId) => GoToNode(nodeId);

    void GoToNode(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || nodeId == "end") { _view.EndConversation(); return; }
        var node = FindNode(nodeId);
        if (node == null) { Debug.LogWarning("[Dialogue] Missing node: " + nodeId); _view.EndConversation(); return; }

        _view.ShowLines(node.speaker, node.lines ?? System.Array.Empty<string>(), () =>
        {
            var visible = FilterResponses(node.responses);
            if (visible.Count == 0) { _view.EndConversation(); return; }
            _view.ShowResponses(visible, OnPick);
        });
    }

    void OnPick(PlayerResponse r)
    {
        if (r == null) { _view.EndConversation(); return; }
        DialogueEffects.Apply(r.effects);
        if (!string.IsNullOrEmpty(r.startHintTrack) && HintTrackRunner.Instance != null)
            HintTrackRunner.Instance.StartTrack(r.startHintTrack);
        GoToNode(r.nextNodeId);
    }

    DialogueNode FindNode(string id)
    {
        if (_conv?.nodes == null) return null;
        foreach (var n in _conv.nodes) if (n.id == id) return n;
        return null;
    }

    static List<PlayerResponse> FilterResponses(PlayerResponse[] all)
    {
        var outList = new List<PlayerResponse>();
        if (all == null) return outList;
        var sd = StoryDirector.Instance;
        foreach (var r in all)
        {
            if (!string.IsNullOrEmpty(r.requiresFlag) && (sd == null || !sd.GetFlag(r.requiresFlag))) continue;
            if (!string.IsNullOrEmpty(r.hiddenIfFlag) && sd != null && sd.GetFlag(r.hiddenIfFlag)) continue;
            outList.Add(r);
        }
        return outList;
    }
}
