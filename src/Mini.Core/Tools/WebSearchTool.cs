// Python: tools.py WEB_SEARCH
namespace Mini.Core.Tools;

/// <summary>Anthropic's server-side web search: only its definition is ours. The server runs it, so there is nothing to invoke.</summary>
public static class WebSearchTool
{
    // Direct calls only: from 20260209 on, the default runs searches from Anthropic's code-execution sandbox, which comes
    // along unasked, and the model then takes that sandbox for the user's machine.
    // Python: tools.py WEB_SEARCH
    public static ToolDefinition Definition { get; } = new("web_search", Json.Obj(("type", "web_search_20260209"), ("name", "web_search"),
        ("allowed_callers", new System.Text.Json.Nodes.JsonArray("direct"))));
}
