// Python: loop.py Agent.server_summary
namespace Mini.Core.Agents.Compaction;

/// <summary>Claude's compaction: the request carries the compaction edit and the server writes the summary, as a compaction block.</summary>
public sealed class ServerCompaction(IModelClient client, SessionManifest manifest, UsageTracker usage, IAgentOutput ui) : ICompactionStrategy
{
    // Python: loop.py Agent.server_summary
    public async Task<List<Message>?> SummarizeAsync(IReadOnlyList<Message> history, CancellationToken ct)
    {
        var resp = await client.CreateAsync(new MessageRequestBuilder()
            .Model(manifest.Model).MaxTokens(16000).System(manifest.System).Tools(manifest.Tools).Messages(history)
            .ServerCompaction().CacheControl().Betas(AnthropicProfile.Betas).Build(), ct);
        usage.Track(resp);
        var stop = resp.StopReason;
        if (stop != StopReason.Compaction)
        {
            ui.Warn($"no summary came back ({stop?.Value ?? "None"}); keeping the full history");
            return null;
        }
        return [Message.Assistant(resp.Content)];
    }
}
