using System.Text.Json.Nodes;

// Python: cli.py context_report
namespace Mini.Cli.Commands;

/// <summary>What /context prints. Whether the fixed parts can be measured is the provider's to say (<see cref="IProviderProfile.CanCountTokens"/>).</summary>
public sealed class ContextReport
{
    static readonly string[] SystemPromptRow = ["system prompt (instructions, AGENTS.md, memory, skills)"];

    /// <summary>The total is what the API reported for the last request. For Claude the fixed parts are measured with the
    /// free token-counting endpoint (nothing goes to the model); elsewhere they're estimated at ~4 characters a token.</summary>
    // Python: cli.py context_report
    public async Task<string> BuildAsync(Agent agent, CancellationToken ct)
    {
        var tools = agent.Tools;
        var groups = new (string Name, List<ToolDefinition> Tools)[]
        {
            ("local tools", tools.Where(t => !t.Schema.ContainsKey("type") && !t.Name.StartsWith("mcp__", StringComparison.Ordinal)).ToList()),
            ("MCP tools", tools.Where(t => t.Name.StartsWith("mcp__", StringComparison.Ordinal)).ToList()),
            ("web search", tools.Where(t => t.Schema.ContainsKey("type")).ToList()),
        };
        long[] sizes;
        string how;
        try
        {
            if (!agent.Profile.CanCountTokens) throw new InvalidOperationException();
            // Each call builds a fresh request, which copies what it needs: a JsonNode can only have one parent.
            Task<long> Count(bool system, List<ToolDefinition>? extra = null)
            {
                var request = new MessageRequestBuilder().Model(agent.Model).Messages([Message.FromText(Role.User, "x")]);
                if (system)
                    request.System(agent.System);
                if (extra is { Count: > 0 })
                    request.Tools(extra);
                return agent.Client.CountTokensAsync(request.Build(), ct);
            }
            var before = await Count(false);
            var totals = new List<long> { await Count(true) };
            var all = new List<ToolDefinition>();
            foreach (var (_, group) in groups) // each part is measured as what it adds
            {
                all.AddRange(group);
                totals.Add(group.Count > 0 ? await Count(true, all) : totals[^1]);
            }
            sizes = totals.Select((t, i) => t - (i == 0 ? before : totals[i - 1])).ToArray();
            how = "measured";
        }
        catch (Exception) when (!ct.IsCancellationRequested) // another provider, or no network: estimate
        {
            // Dumps is ASCII-only (non-ASCII escaped), so counting code points here equals Python's len(json.dumps(g)).
            sizes = new[] { (long)agent.System.CodePointLength() / 4 }
                .Concat(groups.Select(g => g.Tools.Count > 0 ? PythonJson.Dumps(new JsonArray(g.Tools.Select(t => (JsonNode)t.ToJson()).ToArray())).CodePointLength() / 4L : 0L))
                .ToArray();
            how = "estimated";
        }
        var names = SystemPromptRow
            .Concat(groups.Select(g => $"{g.Name} ({g.Tools.Count})")).ToArray();
        var total = agent.LastContext; // 0 before the first reply and right after compaction
        var rows = sizes.Zip(names).Where(r => r.First != 0).ToList();
        if (total != 0) rows.Add((total - sizes.Sum(), $"conversation ({agent.Messages.Count} messages)"));
        var head = total != 0
            ? $"{total:N0} of {agent.Window:N0} tokens ({(double)total / agent.Window * 100:F1}%) at the last request"
            : "No request since the session started or was compacted. Fixed parts:";
        return $"{head} · compacts at {agent.CompactAt:N0}\n"
            + string.Join("\n", rows.Select(r => $"  {r.First,9:N0}  {r.Second}"))
            + $"\n  ({how}; {agent.Model} · session {agent.Session.Id})";
    }
}
