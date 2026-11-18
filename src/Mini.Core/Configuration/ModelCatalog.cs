using System.Text.Json;
using System.Text.Json.Nodes;

// Python: loop.py MODELS, OTHER, MAIN_MODEL, SUB_MODEL, CHEAP_MODEL, COMPACT_AT, model_config
// Python: models.json main, subagent, subagent_cheap, models, models.claude-*.compact_at
namespace Mini.Core.Configuration;

/// <summary>The models mini knows: models.json merged with the user's, and what each model's settings are. Built once by <see cref="ModelCatalogLoader"/>.</summary>
// Python: loop.py MODELS, OTHER, MAIN_MODEL, SUB_MODEL, CHEAP_MODEL, COMPACT_AT, model_config (the module-level constants of loop.py, as one object)
public sealed class ModelCatalog
{
    private static readonly JsonSerializerOptions SnakeCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>The merged "models" object.</summary>
    // Python: loop.py MODELS
    public JsonObject Models { get; }

    // Python: loop.py MAIN_MODEL
    public string Main { get; }
    // Python: loop.py SUB_MODEL
    public string SubModel { get; }
    // Python: loop.py CHEAP_MODEL
    public string CheapModel { get; }
    /// <summary>Input tokens at which a Claude model without its own compact_at is compacted (MINI_COMPACT_AT over models.json).</summary>
    // Python: loop.py COMPACT_AT
    public int CompactAt { get; }

    /// <summary>Entries with a base_url: another provider's Anthropic-format endpoint.</summary>
    // Python: loop.py OTHER
    public Dictionary<string, ModelConfig> Other { get; }

    /// <summary>The catalog over already merged models; <see cref="ModelCatalogLoader"/> reads and merges the files.</summary>
    /// <exception cref="ModelConfigurationException">An entry that has a base_url is not a valid model entry, or there is no claude-* default.</exception>
    // Python: loop.py MODELS, OTHER, MAIN_MODEL, SUB_MODEL, CHEAP_MODEL, COMPACT_AT (the part after the merge)
    public ModelCatalog(JsonObject models, string main, string subModel, string cheapModel, int compactAt)
    {
        Models = models;
        Main = main;
        SubModel = subModel;
        CheapModel = cheapModel;
        CompactAt = compactAt;
        Other = Models
            .Where(kv => kv.Value is JsonObject model && model.ContainsKey("base_url"))
            .ToDictionary(kv => kv.Key, kv => For(kv.Key));
    }

    /// <summary>Settings for a model: its own entry over the Claude defaults; another provider's entry stands alone.</summary>
    // Python: loop.py model_config
    public ModelConfig For(string model)
    {
        if (Models[model] is JsonObject own && own.ContainsKey("base_url"))
            return Deserialize(own);
        var defaults = Models["claude-*"] as JsonObject
            ?? throw new ModelConfigurationException("models.json has no claude-* default");
        var merged = Models[model] is JsonObject settings
            ? Merge(defaults, settings)
            : (JsonObject)defaults.DeepClone();
        return Deserialize(merged);
    }

    internal static JsonObject Merge(JsonObject baseModel, JsonObject overrides)
    {
        var merged = (JsonObject)baseModel.DeepClone();
        foreach (var (key, value) in overrides)
            merged[key] = value?.DeepClone();
        return merged;
    }

    private static ModelConfig Deserialize(JsonObject settings) =>
        JsonSerializer.Deserialize<ModelConfig>(settings, SnakeCase)
        ?? throw new ModelConfigurationException("models.json has an invalid model entry");
}
