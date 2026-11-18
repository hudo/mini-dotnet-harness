// Python: loop.py Agent.price
namespace Mini.Core.Agents.Costs;

/// <summary>What one response cost, in dollars: each of its usage entries at its own model's list price (a fallback or compaction
/// iteration may have run on another one), scaled by the off-peak share of the moment, plus the web searches.</summary>
/// <param name="catalog">Finds the settings of a model a response names.</param>
/// <param name="schedule">The off-peak share of the list price.</param>
/// <param name="sessionModel">The settings of the session's own model: the price of an entry that names no known model, and of the searches.</param>
public sealed class PriceCalculator(ModelCatalog catalog, PriceSchedule schedule, ModelConfig sessionModel)
{
    // Python: loop.py Agent.price
    public double Price(ModelResponse response)
    {
        var usage = response.Usage;
        var cost = 0.0;
        // each iteration (compaction, declined attempt, fallback) has its own price
        foreach (var entry in usage.Entries)
        {
            var model = entry.Model is { Length: > 0 } m ? m : response.Model;
            var cfg = model is not null && catalog.Models.ContainsKey(model) ? catalog.For(model) : sessionModel; // a fallback target, or this session's model
            var p = entry.PromptTokens > 100_000 && cfg.PricesOver100k is { } over ? over : cfg.Prices; // $ per million
            cost += (entry.InputTokens * p["input"] + entry.OutputTokens * p["output"]
                     + entry.CacheReadInputTokens * p["cache_read"]
                     + entry.CacheCreationInputTokens * p["cache_write"]) / 1e6 * schedule.Rate(cfg);
        }
        cost += usage.WebSearchRequests * sessionModel.Search;
        return cost;
    }
}
