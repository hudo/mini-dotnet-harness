// Python: loop.py Agent.compact_at, Agent.should_compact (the threshold and the back-off)
namespace Mini.Core.Agents.Compaction;

/// <summary>When a conversation is compacted: past the model's threshold, and after a failed attempt only once the context has grown another 20%.</summary>
/// <param name="usage">Says how big the conversation was at the last reply.</param>
/// <param name="compactAt">The input tokens past which the context is compacted.</param>
public sealed class CompactionPolicy(UsageTracker usage, int compactAt)
{
    /// <summary>The input tokens past which the context is compacted.</summary>
    public int CompactAt => compactAt;

    /// <summary>The context size at the last failed compaction; zero when none failed (or the last one worked).</summary>
    // Python: loop.py Agent.__init__ (self.compact_failed_at)
    public long FailedAt { get; private set; }

    /// <summary>The threshold of a model: MINI_COMPACT_AT over the model's own compact_at (Claude's default is in its claude-* entry),
    /// and the catalog's default for a model that has none.</summary>
    // Python: loop.py Agent.compact_at
    public static int Threshold(ModelConfig model, ModelCatalog catalog, MiniOptions options) =>
        options.CompactAt ?? model.CompactAt ?? catalog.CompactAt;

    // after a failed compaction, wait until the context has grown another 20% before retrying
    // Python: loop.py Agent.should_compact
    public bool ShouldCompact => usage.LastContext > Math.Max(compactAt, FailedAt * 1.2);

    /// <summary>A compaction failed with the context at <paramref name="contextSize"/>.</summary>
    public void Failed(long contextSize) => FailedAt = contextSize;

    /// <summary>A compaction worked: the back-off is over.</summary>
    public void Succeeded() => FailedAt = 0;
}
