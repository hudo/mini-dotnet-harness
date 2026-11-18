// Python: loop.py Agent.compact (the choice between server_summary and summarize)
namespace Mini.Core.Agents.Compaction;

/// <summary>Who writes the summary a compaction swaps the history for. Claude's server does; another provider's model is asked.</summary>
public interface ICompactionStrategy
{
    /// <summary>The messages that replace the history, or null when no summary came back (the history is kept). A failure is thrown.</summary>
    Task<List<Message>?> SummarizeAsync(IReadOnlyList<Message> history, CancellationToken ct);
}
