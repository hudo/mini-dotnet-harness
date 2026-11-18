using System.Text.Json.Nodes;

// Python: tools.py schema, SCHEMAS (the {"name": x, "description": …, **schema(…)} shape of each entry)
namespace Mini.Core.Tools;

/// <summary>What the model is told about a tool, as the JSON the API takes (key order matters: it is replayed byte for byte).</summary>
public sealed record ToolDefinition(string Name, JsonObject Schema)
{
    /// <summary>A strict function tool: the API guarantees inputs match the schema, so calls never fail on argument shape.
    /// The keys come in the order name, description, strict, input_schema, as in Python's {"description": …, **schema(…)} with the name put first.</summary>
    // Python: tools.py schema
    public static ToolDefinition Function(string name, string description, JsonObject properties, params string[] required) =>
        new(name, Json.Obj(("name", name), ("description", description), ("strict", true), ("input_schema", InputSchema(properties, required))));

    /// <summary>The {"type": "object", "properties", "required", "additionalProperties": false} part of a strict schema.</summary>
    // Python: tools.py schema (the input_schema)
    public static JsonObject InputSchema(JsonObject properties, string[] required) => Json.Obj(
        ("type", "object"), ("properties", properties),
        ("required", new JsonArray(required.Select(r => (JsonNode?)r).ToArray())),
        ("additionalProperties", false));

    /// <summary>One property of a schema: its type and, if it has one, its description.</summary>
    public static JsonObject Property(string type, string? description = null) =>
        description is null ? Json.Obj(("type", type)) : Json.Obj(("type", type), ("description", description));

    /// <summary>A copy to put in a request or a tool list: a JsonNode has one parent.</summary>
    public JsonObject ToJson() => (JsonObject)Schema.DeepClone();
}
