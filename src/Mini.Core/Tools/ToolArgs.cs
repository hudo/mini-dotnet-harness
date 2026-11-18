using System.Text.Json.Nodes;

// Python: loop.py Agent.dispatch (the "Bad arguments: …" text)
namespace Mini.Core.Tools;

/// <summary>A tool's argument record: built from the model's JSON by <see cref="ToolArgs.Bind{T}"/>.</summary>
public interface IToolArgs<TSelf> where TSelf : IToolArgs<TSelf>
{
    /// <summary>Checks the arguments (unknown keys, missing or mistyped values) and builds the record, in the order the tool reads them.</summary>
    static abstract TSelf Bind(ToolArgs args);
}

/// <summary>The model's arguments for one call, with the checks a tool makes: each failure is a <see cref="ToolException"/> reading "Bad arguments: …".</summary>
public sealed class ToolArgs(JsonObject input)
{
    /// <summary>Builds a tool's argument record from the model's JSON.</summary>
    public static T Bind<T>(JsonObject input) where T : IToolArgs<T> => T.Bind(new ToolArgs(input));

    // Python: loop.py Agent.dispatch (the "Bad arguments: …" text)
    public static ToolException Bad(string why) => new($"Bad arguments: {why}");

    /// <summary>A key as the model wrote it, or null.</summary>
    public JsonNode? this[string key] => input[key];

    /// <summary>Fails on the first key that is not allowed.</summary>
    public void Only(params string[] allowed)
    {
        foreach (var (k, _) in input)
            if (!allowed.Contains(k)) throw Bad($"unexpected `{k}`");
    }

    public string Req(string k) => Json.Str(input[k]) ?? throw Bad($"`{k}` is required and must be a string");

    public string Opt(string k, string d) =>
        input[k] is null ? d : Json.Str(input[k]) ?? throw Bad($"`{k}` must be a string");

    public int Int(string k, int d) => input[k] switch
    {
        null => d,
        JsonValue v when v.TryGetValue<int>(out var i) => i,
        _ => throw Bad($"`{k}` must be an integer"),
    };

    public bool Bool(string k) => input[k] switch
    {
        null => false,
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        _ => throw Bad($"`{k}` must be a boolean"),
    };
}
