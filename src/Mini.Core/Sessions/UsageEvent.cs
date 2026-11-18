using System.Text.Json.Nodes;

namespace Mini.Core.Sessions;

/// <summary>What something cost. One shape for the three sources: a model response (<see cref="Usage"/> and <see cref="Context"/>),
/// the judge (<see cref="Judge"/>, its verdict) and a subagent (<see cref="Subagent"/>, the name of its log).</summary>
// Python: session.py Session.append, Session.load (the "usage" line)
public sealed record UsageEvent(double Cost, Usage? Usage = null, long? Context = null, JsonObject? Judge = null, string? Subagent = null) : SessionEvent
{
    public override SessionEventKind Kind => SessionEventKind.Usage;
}
