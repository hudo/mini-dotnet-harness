// Python: loop.py Agent.track
namespace Mini.Core.Agents.Costs;

/// <summary>What a session has spent and how big its conversation was at the last reply. Every response is tracked here (it goes to
/// the session log), and the money other parts spend (the approval judge, subagents) is added to it.</summary>
/// <param name="session">Where each response's usage is logged.</param>
/// <param name="prices">Prices a response.</param>
/// <param name="ui">Where the "API dropped thinking" warning goes.</param>
/// <param name="cost">What a resumed session has spent already.</param>
/// <param name="lastContext">The size at the last reply of a resumed session.</param>
public sealed class UsageTracker(ISessionStore session, PriceCalculator prices, IAgentOutput ui, double cost = 0, long lastContext = 0)
{
    /// <summary>Dollars spent in this session, across all its segments, its judge and its subagents.</summary>
    // Python: loop.py Agent.__init__ (self.cost)
    public double Cost { get; private set; } = cost;

    /// <summary>The conversation's size, in input tokens, at the last reply; zero before the first and right after a compaction.</summary>
    // Python: loop.py Agent.__init__ (self.last_context)
    public long LastContext { get; internal set; } = lastContext; // tests set it to stand in for a long conversation

    /// <summary>Money spent outside a response of this agent: the approval judge's call, a subagent's whole run.</summary>
    public void Add(double spent) => Cost += spent;

    /// <summary>The conversation is gone (a compaction): its size is unknown until the next reply.</summary>
    public void ResetContext() => LastContext = 0;

    /// <summary>Adds a response's price, logs its usage, and warns when the API says it dropped the thinking.</summary>
    // Python: loop.py Agent.track
    public void Track(ModelResponse response)
    {
        var usage = response.Usage;
        var spent = prices.Price(response);
        Cost += spent;
        // a compaction-only response says nothing about the conversation's size: it has no last message
        if (usage.LastMessage is { } last)
            LastContext = last.ContextTokens;
        session.Append(new UsageEvent(Math.Round(spent, 6), usage, LastContext));
        var drops = response.InputTransformations;
        if (drops.Count == 0) return;
        session.Append(new TransformEvent(drops));
        if (drops.Any(d => d.IsPrefixBindingMismatch)) // model switches are expected
            ui.Warn($"API dropped thinking: history was edited before {drops[0].Path}");
    }
}
