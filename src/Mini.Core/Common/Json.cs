using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

// Python: loop.py n
namespace Mini.Core.Common;

/// <summary>Json helpers: the equivalents of Python's dict literals and exclude_none=True.</summary>
public static class Json
{
    public static JsonObject Obj(params (string Key, JsonNode? Value)[] kv)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in kv)
            obj[key] = value;
        return obj;
    }

    /// <summary>Python's n(): null is 0.</summary>
    // Python: loop.py n
    // Python's n() is `x or 0` and passes floats through; Agent.judge in loop.py sums the judge's cost through it,
    // so Track J must read that value as a double, not through N, or small judge costs truncate to 0.
    public static long N(JsonNode? x)
    {
        if (x is not JsonValue value)
            return 0;
        if (value.TryGetValue<long>(out var n))
            return n;
        if (value.TryGetValue<int>(out var i))
            return i;
        if (value.TryGetValue<JsonElement>(out var element) && element.ValueKind == JsonValueKind.Number)
            return element.TryGetInt64(out var l) ? l : (long)element.GetDouble();
        return value.TryGetValue<double>(out var d) ? (long)d : 0;
    }

    public static string? Str(JsonNode? x)
    {
        if (x is not JsonValue value)
            return null;
        if (value.TryGetValue<string>(out var s))
            return s;
        return value.TryGetValue<JsonElement>(out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }

    /// <summary>to_dict(exclude_none=True): drop null object properties, recursively.</summary>
    public static JsonNode StripNulls(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .Where(kv => kv.Value is not null)
            .Select(kv => new KeyValuePair<string, JsonNode?>(kv.Key, StripNulls(kv.Value!)))),
        JsonArray array => new JsonArray(array.Select(item => item is null ? null : StripNulls(item)).ToArray()),
        _ => node.DeepClone(),
    };

    /// <summary>Turn a request body into the dictionary FromRawUnchecked expects.</summary>
    public static Dictionary<string, JsonElement> ToElements(JsonObject body)
    {
        var elements = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in body)
            elements[key] = JsonSerializer.SerializeToElement(value);
        return elements;
    }

    /// <summary>Session lines: keep Unicode readable instead of escaping it.</summary>
    public static readonly JsonSerializerOptions Relaxed = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
