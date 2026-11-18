using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

// Python: loop.py CONFIG, MODELS, MAIN_MODEL, SUB_MODEL, CHEAP_MODEL, COMPACT_AT (CONFIG is models.json merged with ~/.mini-net/models.json)
// Python: models.json main, subagent, subagent_cheap, models, models.claude-*.compact_at
namespace Mini.Core.Configuration;

/// <summary>Reads models.json and the user's models.json under MINI_NET_HOME, merges them, and applies MINI_MODEL and MINI_COMPACT_AT.</summary>
// Python: loop.py CONFIG, MODELS, MAIN_MODEL, SUB_MODEL, CHEAP_MODEL, COMPACT_AT (the reading and merging at import)
public static class ModelCatalogLoader
{
    /// <summary>The catalog for these options.</summary>
    /// <param name="options">MINI_MODEL, MINI_COMPACT_AT and where the user's models.json is.</param>
    /// <param name="modelsJsonPath">The models.json that ships with the program.</param>
    /// <exception cref="ModelConfigurationException">A file is not a JSON object, or something the harness needs is missing.</exception>
    public static ModelCatalog Load(MiniOptions options, string modelsJsonPath)
    {
        var config = Read(modelsJsonPath);
        var home = Path.Combine(new MiniPaths(options).Home, "models.json");
        if (File.Exists(home))
        {
            var mine = Read(home);
            foreach (var (key, value) in mine)
            {
                if (key != "models")
                    config[key] = value?.DeepClone();
            }
            if (mine["models"] is JsonObject mineModels)
            {
                var models = (JsonObject)(config["models"]?.DeepClone() ?? new JsonObject());
                foreach (var (name, entry) in mineModels)
                {
                    if (entry is not JsonObject overrides)
                        continue;
                    models[name] = models[name] is JsonObject baseModel
                        ? ModelCatalog.Merge(baseModel, overrides)
                        : (JsonObject)overrides.DeepClone();
                }
                config["models"] = models;
            }
        }

        var merged = (JsonObject)(config["models"]?.DeepClone()
            ?? throw new ModelConfigurationException("models.json has no models object"));
        var main = options.Model ?? GetString(config, "main");
        var sub = GetString(config, "subagent");
        var cheap = GetString(config, "subagent_cheap");
        var compactAt = options.CompactAt
            ?? GetInt(merged["claude-*"] as JsonObject
                ?? throw new ModelConfigurationException("models.json has no claude-* default"), "compact_at");
        return new ModelCatalog(merged, main, sub, cheap, compactAt);
    }

    private static JsonObject Read(string path) =>
        JsonNode.Parse(Encoding.UTF8.GetString(File.ReadAllBytes(path))) as JsonObject
        ?? throw new ModelConfigurationException($"{path}: expected a JSON object");

    private static string GetString(JsonObject obj, string key) =>
        obj[key]?.GetValue<string>() ?? throw new ModelConfigurationException($"models.json: missing {key}");

    private static int GetInt(JsonObject obj, string key) =>
        obj[key]?.GetValue<int>() ?? throw new ModelConfigurationException($"models.json: missing {key}");
}
