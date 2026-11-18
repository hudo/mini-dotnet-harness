// Python: session.py Session.load, list_sessions (a line whose parts are missing: load raises, the listing skips it)
namespace Mini.Core.Sessions;

/// <summary>
/// A line of a kind this code knows whose parts it cannot read (a message that is not an object, a goal without one). Reading the log
/// does not fail for it: the session listing skips such a line, as it always did, and <see cref="SessionReplayer"/> raises
/// <paramref name="Error"/>, as the load always did, because a conversation rebuilt without a line would silently be a different one.
/// </summary>
public sealed record UnreadableEvent(SessionEventKind UnreadableKind, Exception Error) : SessionEvent
{
    public override SessionEventKind Kind => UnreadableKind;
}
