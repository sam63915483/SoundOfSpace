using UnityEngine;

public abstract class TutorialStep
{
    public abstract string Tip { get; }
    public bool IsComplete { get; protected set; }

    // Used by save/load to restore the completion flag without re-running Tick().
    public void SetComplete(bool value) { IsComplete = value; }

    // Override to true on steps where the 3-second auto-skip would land in
    // a bad spot (e.g. TalkToNPCs satisfies on the third NPC's *greeting*,
    // so the auto-skip would fire while the player is still mid-dialogue and
    // the resulting performance-review modal would pop over the dialogue UI).
    // The advance hint also drops the "AutoSkip in N" countdown when this
    // is true.
    public virtual bool BlocksAutoSkip => false;

    // Override to true on steps whose NEXT step's instructions apply to a
    // modal UI the player is already inside (e.g. OpenCookPanelStep — the
    // step that follows tells the player to "Add fish, click Cook" inside
    // the cook panel that just opened). Default false: arming + advancing
    // wait until the player closes any modal, since most "next tips"
    // wouldn't make sense while a panel is occluding the world.
    public virtual bool AdvancesDuringModalUI => false;

    public virtual void OnEnter() { }
    public abstract void Tick();
    public virtual void OnExit() { }

    protected static bool AnyWASD()
    {
        // Keyboard WASD/arrows OR analog left stick beyond deadzone. Uses the
        // facade's move helpers (Input System stick, D-pad excluded) rather
        // than the legacy "Horizontal"/"Vertical" axes.
        float h = TutorialGate.MoveAxisHorizontal(TutorialAbility.Move);
        float v = TutorialGate.MoveAxisVertical(TutorialAbility.Move);
        return h * h + v * v > 0.04f;
    }
}
