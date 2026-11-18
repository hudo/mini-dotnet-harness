namespace Mini.Core.Models;

/// <summary>How a provider shrinks a conversation that no longer fits.</summary>
// Python: loop.py Agent.compact (the choice between server_summary and summarize)
public enum CompactionMode
{
    /// <summary>The server writes the summary (Claude's compaction).</summary>
    Server,

    /// <summary>The model is asked for a summary in an ordinary request.</summary>
    Summary,
}
