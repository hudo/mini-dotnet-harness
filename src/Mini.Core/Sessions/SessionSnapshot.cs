// Python: session.py Session.load (the tuple it returns)
namespace Mini.Core.Sessions;

/// <summary>What a session's log says now: the frozen meta, the transcript since the last compaction, the goal and the totals.</summary>
/// <param name="Meta">The latest meta event; null for a log that has none (a new session).</param>
/// <param name="Messages">The messages of the latest segment.</param>
/// <param name="Goal">The goal, unless it was cleared.</param>
/// <param name="Cost">Everything the session has spent, across all segments.</param>
/// <param name="Context">The conversation's size at the last response; zero after a compaction.</param>
public sealed record SessionSnapshot(MetaEvent? Meta, IReadOnlyList<Message> Messages, Goal? Goal, double Cost, long Context);
