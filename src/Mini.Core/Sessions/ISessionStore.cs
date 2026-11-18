// Python: none (C#-only: Python's Session is the one concrete class; this is the seam the transcript and the tests write through)
namespace Mini.Core.Sessions;

/// <summary>An append-only log of one conversation's events. Nothing in it is ever rewritten.</summary>
public interface ISessionStore
{
    /// <summary>The session's id: its file name without the extension.</summary>
    string Id { get; }

    /// <summary>The file the events are in.</summary>
    string Path { get; }

    /// <summary>Appends one event, stamped with the time.</summary>
    void Append(SessionEvent e);

    /// <summary>Every event in the log, oldest first. A line a crash cut off is skipped, and all before it is intact.</summary>
    IReadOnlyList<SessionEvent> Read();
}
