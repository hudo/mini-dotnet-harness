using System.Text.Json.Nodes;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;
using static Mini.Tests.Providers.ProvidersHelpers;

namespace Mini.Tests.Providers;

/// <summary>Cost tracking and pricing, on Claude and DeepSeek.</summary>
public class CostTests
{
    // Python test: test_cost_tracking
    [Fact]
    public async Task CostTracking()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([ReplyWith([Text("x")], usage: Usage(inp: 1_000_000, @out: 100_000, read: 1_000_000))]);

        await agent.TurnAsync("hi", ct: TestContext.Current.CancellationToken);

        Assert.Equal(4 + 2 + 0.2, agent.Cost, 6);
    }

    // Python test: test_each_iteration_priced_at_its_own_model
    [Fact]
    public async Task EachIterationPricedAtItsOwnModel()
    {
        using var ws = new Workspace();
        var u = Usage(inp: 0, @out: 0);
        u["iterations"] = new JsonArray(
            Json.Obj(("type", "message"), ("model", "claude-opus-5-5"), ("input_tokens", 1_000_000), ("output_tokens", 0),
                ("cache_read_input_tokens", 0), ("cache_creation_input_tokens", 0)),
            Json.Obj(("type", "fallback_message"), ("model", "claude-opus-5"), ("input_tokens", 1_000_000), ("output_tokens", 0),
                ("cache_read_input_tokens", 0), ("cache_creation_input_tokens", 0)));
        var (agent, _) = ws.Make([ReplyWith([Text("x")], usage: u, model: "claude-opus-5")]);

        await agent.TurnAsync("hi", ct: TestContext.Current.CancellationToken);

        Assert.Equal(4 + 5, agent.Cost, 6); // opus-5-5 at $4 per million, opus-5 at $5
        Assert.Equal(1_000_000, agent.LastContext);
    }

    // Python test: test_unknown_model_does_not_break_tracking
    [Fact]
    public async Task UnknownModelDoesNotBreakTracking()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([ReplyWith([Text("x")], model: "claude-fable-5-1")]);

        Assert.Equal("end_turn", await agent.TurnAsync("hi", ct: TestContext.Current.CancellationToken));

        Assert.True(agent.Cost > 0);
    }

    // C#-only: a model entry that is never at peak (peak_utc: []) and one with no peak_utc at all, whatever the time:
    // the off-peak share scales tokens and never the flat search price.
    [Fact]
    public async Task OffPeakShareAppliesToTokensAndNeverToSearchWithAFixedRate()
    {
        using var ws = new Workspace();
        using var entries = new ModelEntries(ws.Runtime.Catalog,
            ("claude-test-offpeak", Json.Obj(("peak_utc", new JsonArray()), ("off_peak", 0.5))),
            ("claude-test-nopeak", Json.Obj(("off_peak", 0.5))));
        foreach (var (model, share) in new[] { ("claude-test-offpeak", 0.5), ("claude-test-nopeak", 1.0) })
        {
            var (agent, _) = ws.Make([ReplyWith([Text("x")], model: model, usage: UsageWithSearch(1_000_000, 1_000_000, 4))], model: model);

            await agent.TurnAsync("hi", ct: TestContext.Current.CancellationToken);

            Assert.Equal((4 + 20) * share + 4 * 0.01, agent.Cost, 6); // tokens scale with the rate; searches are a flat price
        }
    }

    // Python test: test_deepseek_prices_and_search
    // The clock stands at a peak instant, as the Python test pins it.
    [Fact]
    public async Task DeepseekPricesAndSearch()
    {
        using var ws = new Workspace();
        ws.Runtime.Time.SetUtcNow(TestRuntime.Peak);
        var (agent, _) = ws.Ds([ReplyWith([Text("x")], model: "deepseek-flash", usage: UsageWithSearch(1_000_000, 1_000_000, 4))]);

        await agent.TurnAsync("hi", ct: TestContext.Current.CancellationToken);

        Assert.Equal(0.30 + 1.20 + 4 * 0.00075, agent.Cost, 6);
    }

    // Python test: test_deepseek_off_peak_halves_tokens_not_search
    // Peak, three hours later (off-peak) and a weekend: the tokens' share changes, the search price does not.
    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(3 * 3600, 0.5)]
    [InlineData(86400, 0.5)] // TestRuntime.Weekend: Peak + one day
    public async Task DeepseekOffPeakHalvesTokensNotSearch(int secondsAfterPeak, double share)
    {
        using var ws = new Workspace();
        ws.Runtime.Time.SetUtcNow(TestRuntime.Peak.AddSeconds(secondsAfterPeak));
        var (agent, _) = ws.Ds([ReplyWith([Text("x")], model: "deepseek-flash", usage: UsageWithSearch(1_000_000, 1_000_000, 4))]);

        await agent.TurnAsync("hi", ct: TestContext.Current.CancellationToken);

        Assert.Equal((0.30 + 1.20) * share + 4 * 0.00075, agent.Cost, 6);
    }
}
