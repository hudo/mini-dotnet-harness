namespace Mini.Core.Sessions;

/// <summary>The goal as it stands. A cleared goal is recorded too, so replay knows there is none.</summary>
// Python: session.py Session.append, Session.load (the "goal" line)
public sealed record GoalEvent(Goal Goal) : SessionEvent
{
    public override SessionEventKind Kind => SessionEventKind.Goal;
}
