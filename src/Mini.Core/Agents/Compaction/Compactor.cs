using System.Globalization;

// Python: loop.py Agent.compact, Agent.maybe_compact
namespace Mini.Core.Agents.Compaction;

/// <summary>Swaps a conversation that has grown too long for a summary, written by the provider's <see cref="ICompactionStrategy"/>.</summary>
/// <param name="strategy">Who writes the summary.</param>
/// <param name="transcript">The conversation; a summary starts a new segment of it.</param>
/// <param name="usage">Forgets the conversation's size once it is replaced.</param>
/// <param name="policy">Says when to compact, and remembers a failure.</param>
/// <param name="images">What the transcript holds of images; the summary holds none.</param>
/// <param name="ui">Where "compacted" and the warning of a failure go.</param>
/// <param name="activeGoalRules">The goal's rules when a goal is running, else null: they are restated after the swap, so the summary does not lose them.</param>
public sealed class Compactor(ICompactionStrategy strategy, Transcript transcript, UsageTracker usage, CompactionPolicy policy, ImageBudget images,
    IAgentOutput ui, Func<string?> activeGoalRules)
{
    public bool ShouldCompact => policy.ShouldCompact;

    /// <summary>Swap the whole history for a summary. Returns false when there was nothing to compact or no summary came back.</summary>
    // Python: loop.py Agent.compact
    public async Task<bool> CompactAsync(CancellationToken ct = default)
    {
        if (transcript.Messages.Count == 0) return false;
        var before = usage.LastContext;
        List<Message>? summary;
        try
        {
            summary = await strategy.SummarizeAsync(transcript.Messages, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested) // a cancelled compaction is the user's interrupt, not a failure
        {
            ui.Warn($"compaction failed: {e.Message}");
            summary = null;
        }
        if (summary is not { Count: > 0 })
        {
            policy.Failed(before);
            return false;
        }
        transcript.ReplaceWithSummary(summary); // a new segment in the session file; older turns stay on disk
        usage.ResetContext();
        policy.Succeeded();
        images.Reset(0); // the summary holds no images
        ui.Note("compacted");
        if (activeGoalRules() is { } rules)
            transcript.PendingNotes.Add(rules);
        return true;
    }

    /// <summary>Compacts when the policy says the context is too big.</summary>
    // Python: loop.py Agent.maybe_compact
    public async Task MaybeCompactAsync(CancellationToken ct = default)
    {
        if (!policy.ShouldCompact) return;
        ui.Note($"context at {usage.LastContext.ToString("N0", CultureInfo.InvariantCulture)} tokens, compacting");
        await CompactAsync(ct);
    }
}
