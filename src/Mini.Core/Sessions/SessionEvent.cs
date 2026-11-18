// Python: session.py Session.append, Session.load (the events: the kwargs of append, what load reads back)
namespace Mini.Core.Sessions;

/// <summary>
/// One line of a session file. The subclasses are the shapes the harness writes; the store adds the timestamp and the kind, so an
/// event is only its data. <see cref="Timestamp"/> is set on events that were read back, never on ones about to be written.
/// </summary>
public abstract record SessionEvent
{
    public abstract SessionEventKind Kind { get; }

    /// <summary>Seconds since the Unix epoch, as the line says; null for an event not read from a file.</summary>
    public double? Timestamp { get; init; }
}
