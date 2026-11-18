namespace Mini.Core.Sessions;

/// <summary>Several messages that only make sense together (a reply that paused on a server tool, and the one that completed it): one line, so a crash can't split them.</summary>
// Python: session.py Session.append, Session.load (the "msgs" line)
public sealed record MessagesEvent(IReadOnlyList<Message> Messages) : SessionEvent
{
    public override SessionEventKind Kind => SessionEventKind.Msgs;
}
