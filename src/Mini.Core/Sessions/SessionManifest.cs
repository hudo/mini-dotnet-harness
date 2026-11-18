using System.Text.Json.Nodes;

// Python: loop.py Agent.__init__ (self.model, self.system, self.tools)
// Python: session.py Session.append, Session.load (the "meta" line)
namespace Mini.Core.Sessions;

/// <summary>
/// What is frozen for a session's whole life: the model, the system prompt and the tool list. Thinking blocks are bound to
/// exactly this prompt and tool set, so a resumed session replays it byte for byte from its meta line, and nothing changes it.
/// </summary>
public sealed record SessionManifest(string Model, string System, IReadOnlyList<ToolDefinition> Tools)
{
    /// <summary>The manifest a session's meta line froze.</summary>
    /// <exception cref="InvalidOperationException">The line lacks the model, the system prompt or the tools.</exception>
    public static SessionManifest FromMeta(MetaEvent meta) => new(
        meta.Model ?? throw new InvalidOperationException("the session's meta has no model"),
        meta.System ?? throw new InvalidOperationException("the session's meta has no system prompt"),
        (meta.Tools ?? throw new InvalidOperationException("the session's meta has no tools")).OfType<JsonObject>()
            .Select(t => new ToolDefinition(Json.Str(t["name"]) ?? "", (JsonObject)t.DeepClone())).ToList());

    /// <summary>The meta line that freezes this manifest for a new session in <paramref name="cwd"/>.</summary>
    public MetaEvent ToMeta(string cwd) => new(Model, cwd, System, new JsonArray(Tools.Select(t => (JsonNode?)t.ToJson()).ToArray()));

    public bool HasTool(string name) => Tools.Any(t => t.Name == name);
}
