// Python: loop.py Agent.summarize
namespace Mini.Core.Agents.Compaction;

/// <summary>Another provider's compaction: the model is asked, in an ordinary request, to write the summary.</summary>
public sealed class SummaryCompaction(IModelClient client, SessionManifest manifest, UsageTracker usage) : ICompactionStrategy
{
    // Python: loop.py Agent.summarize
    public async Task<List<Message>?> SummarizeAsync(IReadOnlyList<Message> history, CancellationToken ct)
    {
        var last = history[^1];
        var ask = TextBlock.Create(HarnessPrompts.Summarize);
        List<Message> msgs;
        if (last.Role == Role.User) // the request ends on tool results: add the ask to them, two user turns can't follow
        {
            var joined = new Message(last.Role, (last.Text is { } typed ? [TextBlock.Create(typed)] : last.Content).Append(ask).ToList());
            msgs = [.. history.Take(history.Count - 1), joined];
        }
        else
        {
            msgs = [.. history, Message.User([ask])];
        }

        var resp = await client.CreateAsync(new MessageRequestBuilder()
            .Model(manifest.Model).MaxTokens(16000).SystemAsString(manifest.System).Tools(manifest.Tools).Messages(msgs).Build(), ct);
        usage.Track(resp);
        var text = string.Join("\n", resp.Content.OfType<TextBlock>().Select(b => b.Text)).Trim();
        if (text.Length == 0) return null;
        return
        [
            Message.FromText(Role.User, $"Summary of the conversation so far, written by you:\n\n{text}"),
            Message.Assistant([TextBlock.Create("Understood, continuing from it.")]),
        ];
    }
}
