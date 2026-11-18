using System.Text.Json.Serialization;

// Python: models.json models.claude-*.window, models.claude-*.compact_at, models.claude-*.search, models.claude-*.fallbacks
// Python: models.json models.claude-*.efforts, models.claude-*.prices, models.claude-haiku-5-5.prices_over_100k, models.claude-opus-5-5.max_output
// Python: models.json models.deepseek-flash.base_url, models.deepseek-flash.key, models.deepseek-flash.peak_utc, models.deepseek-flash.off_peak
namespace Mini.Core.Configuration;

/// <summary>The subset of models.json that the harness reads, snake_case in JSON.</summary>
// Python: models.json models (the shape of one entry; Python reads the dict keys directly)
public sealed record ModelConfig
{
    public int Window { get; init; }
    public int? CompactAt { get; init; }
    public double Search { get; init; }
    public bool Fallbacks { get; init; }
    public List<string> Efforts { get; init; } = new();
    public Dictionary<string, double> Prices { get; init; } = new();

    [JsonPropertyName("prices_over_100k")]
    public Dictionary<string, double>? PricesOver100k { get; init; }

    public int? MaxOutput { get; init; }
    public string? BaseUrl { get; init; }
    public string? Key { get; init; }
    public List<List<int>>? PeakUtc { get; init; }
    public double OffPeak { get; init; } = 0.5;
}
