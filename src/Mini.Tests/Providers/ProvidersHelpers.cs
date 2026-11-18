using System.Net;
using System.Text.Json.Nodes;
using Anthropic.Exceptions;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;

namespace Mini.Tests.Providers;

/// <summary>Helpers private to the providers lane.</summary>
static class ProvidersHelpers
{
    /// <summary>A usage object with web searches billed: <c>server_tool_use</c> as the API sends it.</summary>
    // Python test helper: tests/test_mini.py (u = {**usage(...), "server_tool_use": {...}})
    public static JsonObject UsageWithSearch(int inp, int @out, int searches)
    {
        var u = Usage(inp, @out);
        u["server_tool_use"] = Json.Obj(("web_search_requests", searches), ("web_fetch_requests", 0));
        return u;
    }

    /// <summary>An agent on DeepSeek, with a scripted model.</summary>
    // Python test helper: tests/test_mini.py ds
    public static (Agent Agent, FakeModelClient Fake) Ds(this Workspace ws, IEnumerable<object> script, ISessionStore? session = null,
        string effort = "high", ConsoleAgentOutput? ui = null) =>
        ws.Make(script, session: session, model: "deepseek-flash", effort: effort, ui: ui);

    /// <summary>What the SDK raises mid-stream: HTTP 200 and the error's type (an <c>error</c> event).</summary>
    // Python test helper: tests/test_mini.py api_error (the mid-stream kind)
    public static AnthropicSseException StreamError(Anthropic.Models.ErrorType? type, string message = "stream error") =>
        new(message, new Exception(message)) { ErrorType = type };

    /// <summary>What the SDK raises before a stream: a class per HTTP status; the error body's type is set when given.</summary>
    // Python test helper: tests/test_mini.py api_error (the HTTP-status kind)
    public static AnthropicApiException StatusError(int status, Anthropic.Models.ErrorType? type = null) =>
        new($"HTTP {status}", new HttpRequestException($"HTTP {status}", null, (HttpStatusCode)status))
        {
            StatusCode = (HttpStatusCode)status,
            ResponseBody = "{}",
            ErrorType = type,
        };

    public static List<string?> ToolNames(Agent agent) => agent.ToolsJson().Select(t => Json.Str(t?["name"])).ToList();
}

/// <summary>
/// Adds entries to a runtime's <c>ModelCatalog.Models</c> for one test and removes them on dispose: the C# form of
/// Python's <c>monkeypatch.setitem(loop.MODELS, ...)</c>. Only entries without a base_url work this way
/// (<c>ModelCatalog.Other</c> is built once).
/// </summary>
sealed class ModelEntries : IDisposable
{
    readonly List<string> _names = [];

    readonly ModelCatalog _catalog;

    public ModelEntries(ModelCatalog catalog, params (string Name, JsonObject Entry)[] entries)
    {
        _catalog = catalog;
        foreach (var (name, entry) in entries)
        {
            catalog.Models[name] = entry;
            _names.Add(name);
        }
    }

    public void Dispose()
    {
        foreach (var name in _names)
            _catalog.Models.Remove(name);
    }
}
