// Story phase advances forward only, driven by quest/story events.
// Used by the phone AI to gate persona blocks + knowledge entries.
// Lives in its own file so SaveData can reference it without dragging
// in MonoBehaviour deps.
public enum StoryPhase
{
    Phase1_Loyal     = 0,
    Phase2_Uneasy    = 1,
    Phase3_Resistant = 2,
}
