namespace Mini.Core.Sessions;

/// <summary>A compaction: the summary that replaces everything before it. Replay skips all earlier lines.</summary>
// Python: session.py Session.append, Session.load (the "compact" line)
public sealed record CompactEvent(IReadOnlyList<Message> Messages) : SessionEvent
{
    public override SessionEventKind Kind => SessionEventKind.Compact;
}
