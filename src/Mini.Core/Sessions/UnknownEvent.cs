namespace Mini.Core.Sessions;

/// <summary>A line of a kind this code does not know (a newer version wrote it). Replay ignores it.</summary>
// Python: session.py Session.load (a line of a kind it does not know)
public sealed record UnknownEvent(SessionEventKind UnknownKind) : SessionEvent
{
    public override SessionEventKind Kind => UnknownKind;
}
