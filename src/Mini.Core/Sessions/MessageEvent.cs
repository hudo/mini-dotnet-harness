namespace Mini.Core.Sessions;

/// <summary>One message. <paramref name="By"/> is "harness" for a message the harness wrote, null for the user's own.</summary>
// Python: session.py Session.append, Session.load (the "msg" line)
public sealed record MessageEvent(Message Message, string? By = null) : SessionEvent
{
    public const string Harness = "harness";

    public override SessionEventKind Kind => SessionEventKind.Msg;

    /// <summary>True when the harness, not the user, wrote it.</summary>
    public bool ByHarness => !string.IsNullOrEmpty(By);
}
