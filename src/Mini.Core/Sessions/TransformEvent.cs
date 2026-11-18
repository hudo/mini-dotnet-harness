namespace Mini.Core.Sessions;

/// <summary>What the API changed in the history before answering (a dropped thinking block).</summary>
// Python: session.py Session.append, Session.load (the "transform" line)
public sealed record TransformEvent(IReadOnlyList<InputTransformation> Items) : SessionEvent
{
    public override SessionEventKind Kind => SessionEventKind.Transform;
}
