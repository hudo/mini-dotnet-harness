using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mini.Tests.Fakes;
using static Mini.Tests.Providers.ProvidersHelpers;

namespace Mini.Tests.Providers;

/// <summary>/context: the report ContextReport builds (the C# of Python's cli.context_report).</summary>
public class ContextReportTests
{
    /// <summary>A model client that only counts tokens, through the given function; everything else is unused here.</summary>
    sealed class Counter(Func<MessageRequest, long> count) : IModelClient
    {
        public Task<long> CountTokensAsync(MessageRequest request, CancellationToken ct) => Task.FromResult(count(request));

        public Task<ModelResponse> StreamAsync(MessageRequest request, IStreamObserver observer, CancellationToken ct) => throw new NotSupportedException();

        public Task<ModelResponse> CreateAsync(MessageRequest request, CancellationToken ct) => throw new NotSupportedException();
    }

    static List<long> Split(string report) =>
        Regex.Matches(report, @"^\s+([\d,]+)\s", RegexOptions.Multiline).Select(m => long.Parse(m.Groups[1].Value.Replace(",", ""))).ToList();

    // Python test: test_context_report_uses_the_reported_size_and_splits_it
    // Python's fake client can't count tokens, so its report is "estimated". FakeModelClient answers every count with
    // TokenCount (0), so on Claude the C# report is "measured" with empty fixed parts; the estimated path is the next two tests.
    [Fact]
    public async Task ContextReportUsesTheReportedSizeAndSplitsIt()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([]);
        var ct = TestContext.Current.CancellationToken;

        Assert.Contains("No request since", await new ContextReport().BuildAsync(agent, ct)); // a fresh session, or just compacted: never "0 tokens"

        agent.Transcript.AddUser("hi");
        agent.LastContext = 12_000;
        var report = await new ContextReport().BuildAsync(agent, ct);

        Assert.StartsWith("12,000 of 1,000,000 tokens (1.2%)", report);
        Assert.Contains("compacts at 150,000", report);
        Assert.Equal(12_000, Split(report).Sum());
        Assert.Contains("conversation (1 messages)", report);
        Assert.Contains("measured", report);
        Assert.DoesNotContain("$", report);
    }

    // C#-only: the "measured" path with real differences: each part is what it adds to the count before it
    [Fact]
    public async Task MeasuredReportSplitsTheCountsByWhatEachPartAdds()
    {
        using var ws = new Workspace();
        // the calls come in order: bare messages, + system prompt, + local tools, + web search (no MCP tools: no call)
        var answers = new Queue<long>([10, 110, 310, 330]);
        var (agent, _) = ws.Make([], client: new Counter(_ => answers.Dequeue()));
        agent.Transcript.AddUser("hi");
        agent.LastContext = 12_000;

        var report = await new ContextReport().BuildAsync(agent, TestContext.Current.CancellationToken);

        Assert.Empty(answers);
        Assert.Equal([100, 200, 20, 11_680], Split(report)); // system, local tools, web search, the rest is the conversation
        Assert.Contains("system prompt (instructions, AGENTS.md, memory, skills)", report);
        Assert.Contains("local tools (", report);
        Assert.Contains("web search (1)", report);
        Assert.DoesNotContain("MCP tools", report);
        Assert.Contains("(measured; claude-opus-5-5 · session ", report);
    }

    // C#-only: counting tokens failed (no network, an error): the fixed parts are estimated from their size, as in Python
    [Fact]
    public async Task ReportIsEstimatedWhenCountingTokensFails()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([], client: new Counter(_ => throw new HttpRequestException("no network")));
        agent.Transcript.AddUser("hi");
        agent.LastContext = 12_000;

        var report = await new ContextReport().BuildAsync(agent, TestContext.Current.CancellationToken);

        Assert.StartsWith("12,000 of 1,000,000 tokens (1.2%)", report);
        Assert.Equal(12_000, Split(report).Sum());
        Assert.True(Split(report)[0] > 0); // the system prompt has a size now
        Assert.Contains("conversation (1 messages)", report);
        Assert.Contains("(estimated;", report);
    }

    // C#-only: another provider has no token counting: always estimated, and its own window and compaction line are shown
    [Fact]
    public async Task DeepseekReportIsEstimated()
    {
        using var ws = new Workspace();
        var (agent, fake) = ws.Ds([]);
        agent.Transcript.AddUser("hi");
        agent.LastContext = 12_000;

        var report = await new ContextReport().BuildAsync(agent, TestContext.Current.CancellationToken);

        Assert.StartsWith("12,000 of 1,048,576 tokens (1.1%)", report);
        Assert.Contains("compacts at 800,000", report);
        Assert.Equal(12_000, Split(report).Sum());
        Assert.Contains("(estimated; deepseek-flash", report);
        Assert.Empty(fake.Counts); // never asked
        Assert.DoesNotContain("$", report);
    }
}
