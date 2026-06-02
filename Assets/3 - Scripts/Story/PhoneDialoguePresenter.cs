using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders a DialogueRunner inside the phone: AI lines stream as chat bubbles, player responses
/// appear as buttons in the DialogueReplyColumn to the right of the chassis. One presenter per
/// open AI-chat session.
/// </summary>
public class PhoneDialoguePresenter : DialoguePresenter
{
    readonly AIChatScreen _chat;
    readonly DialogueReplyColumn _column;

    public PhoneDialoguePresenter(AIChatScreen chat, DialogueReplyColumn column)
    {
        _chat = chat; _column = column;
    }

    public void ShowLines(string speaker, string[] lines, Action onComplete)
    {
        _column.Clear();                       // hide replies while the AI is "typing"
        PostSequential(lines, 0, onComplete);
    }

    void PostSequential(string[] lines, int i, Action onComplete)
    {
        if (lines == null || i >= lines.Length) { onComplete?.Invoke(); return; }
        _chat.PostAILine(lines[i], () => PostSequential(lines, i + 1, onComplete));
    }

    public void ShowResponses(List<PlayerResponse> responses, Action<PlayerResponse> onPick)
    {
        var labels = new List<string>();
        foreach (var r in responses) labels.Add(r.buttonText);
        _column.Show(labels, idx =>
        {
            var chosen = responses[idx];
            _chat.PostUserLine(chosen.buttonText);   // echo the pick as a player bubble
            _column.Clear();
            onPick?.Invoke(chosen);
        });
    }

    public void EndConversation() => _column.Clear();
}
