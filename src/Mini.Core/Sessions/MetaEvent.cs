using System.Text.Json.Nodes;

namespace Mini.Core.Sessions;

/// <summary>The first line of a session: what is frozen for its whole life, replayed on resume. Fields are null only in a file that lacks them.</summary>
// Python: session.py Session.append, Session.load (the "meta" line)
public sealed record MetaEvent(string? Model, string? Cwd, string? System, JsonArray? Tools) : SessionEvent
{
    public override SessionEventKind Kind => SessionEventKind.Meta;
}
