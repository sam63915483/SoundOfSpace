using UnityEngine;

public static class NPCConversationTracker
{
    public static event System.Action<MonoBehaviour> OnConversationStarted;

    public static void NotifyStart(MonoBehaviour npc)
    {
        OnConversationStarted?.Invoke(npc);
    }
}
