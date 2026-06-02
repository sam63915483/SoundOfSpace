using System;
using System.Collections.Generic;

/// <summary>
/// A view that renders a DialogueRunner. The phone implements this; a future Tev view reuses
/// the same runner. The runner never knows which surface is drawing it.
/// </summary>
public interface DialoguePresenter
{
    /// <summary>Show a node's lines (in sequence); call onComplete when the last line finishes.</summary>
    void ShowLines(string speaker, string[] lines, Action onComplete);
    /// <summary>Show the (already flag-filtered) responses; call onPick when the player chooses one.</summary>
    void ShowResponses(List<PlayerResponse> responses, Action<PlayerResponse> onPick);
    /// <summary>The conversation reached "end" (or a node with no responses).</summary>
    void EndConversation();
}
