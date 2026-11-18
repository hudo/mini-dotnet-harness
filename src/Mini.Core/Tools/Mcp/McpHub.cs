using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

// Python: ext.py safe_name, MCPHub, MCPHub.__init__, MCPHub._serve, MCPHub.call, MCPHub.close
namespace Mini.Core.Tools.Mcp;

/// <summary>Connects to every stdio server in mcp.json and exposes its tools as mcp__server__tool.</summary>
// Python: ext.py MCPHub
public sealed partial class McpHub : IAsyncDisposable
{
    // Python: ext.py MCPHub.__init__ (self.schemas)
    public List<JsonObject> Schemas { get; } = new();
    // Python: ext.py MCPHub.__init__ (self.errors)
    public List<string> Errors { get; } = new();

    // Python: ext.py MCPHub.__init__ (self.tools: api name -> (session, real name))
    private readonly Dictionary<string, (McpClient Client, string Real)> _tools = new();
    private readonly List<McpClient> _clients = new();

    private static readonly object LogLock = new();
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "mini-mcp.log");
    private static readonly UTF8Encoding NoBom = new(false);

    // Python's re works on code points, so a surrogate pair is one "_" there; match the pair first to keep it one here.
    [GeneratedRegex(@"[\uD800-\uDBFF][\uDC00-\uDFFF]|[^a-zA-Z0-9_-]")]
    private static partial Regex UnsafeChars();

    // Python: ext.py safe_name
    internal static string SafeName(string s) => UnsafeChars().Replace(s, "_");

    private McpHub() { }

    // Python: ext.py MCPHub.__init__ (the tools dict, which Agent.dispatch tests with `in`)
    public bool Has(string apiName) => _tools.ContainsKey(apiName);

    /// <summary>One tool per MCP tool, in the order the servers registered them, so dispatch needs no case for the hub.</summary>
    // Python: loop.py Agent.dispatch (the mcp__ branch)
    public IEnumerable<McpTool> CreateTools(IApprovalService approval) => Schemas.Select(s => new McpTool(this, s, approval));

    // Python: ext.py MCPHub.__init__ (finding mcp.json, reading it, and starting each server)
    public static async Task<McpHub> StartAsync(string cwd, MiniPaths paths)
    {
        var hub = new McpHub();
        var cfg = new[] { Path.Combine(cwd, "mcp.json"), Path.Combine(cwd, ".mcp.json"), Path.Combine(paths.Home, "mcp.json") }
            .FirstOrDefault(File.Exists);
        if (cfg is null)
            return hub;
        List<KeyValuePair<string, JsonNode?>> servers;
        try
        {
            // GetString keeps a BOM (File.ReadAllText strips it), so it's an error, like Python's utf-8 decode.
            var root = JsonNode.Parse(Encoding.UTF8.GetString(File.ReadAllBytes(cfg))) as JsonObject ?? throw new FormatException("top level must be an object");
            var configured = root["mcpServers"] is null && !root.ContainsKey("mcpServers")
                ? new JsonObject()
                : root["mcpServers"] as JsonObject ?? throw new FormatException("\"mcpServers\" must be an object");
            servers = configured.ToList(); // JsonObject materializes duplicate keys lazily; validate inside the config-error boundary.
        }
        // ArgumentException: JsonNode materialization rejects a duplicate key; keep it as a config error.
        catch (Exception e) when (e is JsonException or FormatException or ArgumentException)
        {
            hub.Errors.Add($"{cfg}: not a valid MCP config ({e.Message})");
            return hub;
        }
        // One after the other, as Python does, so a slow server shows up in the timing, and the tool order is stable.
        foreach (var (name, spec) in servers)
        {
            if (spec is not JsonObject o || !o.ContainsKey("command"))
            {
                hub.Errors.Add($"{name}: only stdio servers (\"command\") are supported; for a URL use "
                    + "\"command\": \"npx\", \"args\": [\"-y\", \"mcp-remote\", \"<url>\"]");
                continue;
            }
            await hub.ConnectAsync(name, o);
        }
        return hub;
    }

    // Python: ext.py MCPHub._serve
    private async Task ConnectAsync(string name, JsonObject spec)
    {
        McpClient? client = null;
        try
        {
            var command = spec["command"]!.GetValue<string>();
            var args = spec["args"] is JsonArray a ? a.Select(x => x!.GetValue<string>()).ToList() : new List<string>();
            // Python's stdio_client passes only a small default environment plus spec.env, never the whole
            // parent environment (which holds API keys). The C# default is to inherit everything, so opt out.
            var env = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
            if (spec["env"] is JsonObject envSpec)
            {
                foreach (var (k, v) in envSpec)
                    env[k] = v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : v?.ToJsonString();
            }

            var options = new StdioClientTransportOptions
            {
                Command = OperatingSystem.IsWindows() ? command : "/bin/sh",
                Name = name,
                InheritEnvironmentVariables = false,
                EnvironmentVariables = env,
                // A server's output never reaches the terminal.
                StandardErrorLines = line => { lock (LogLock) File.AppendAllText(LogPath, line + "\n", NoBom); },
            };
            // Windows: the SDK itself rewrites any command except cmd.exe to `cmd.exe /c <command> <args>`
            // (StdioClientTransport.ConnectAsync), so npx and .cmd shims work as is. POSIX: ignore SIGINT in
            // the server so Ctrl-C at the terminal doesn't kill it; ignored signals survive exec.
            if (OperatingSystem.IsWindows())
            {
                options.Arguments = args;
            }
            else
            {
                // shortcut: only SIGINT is ignored; the server stays in mini's process group, so SIGQUIT/SIGTSTP
                // from the terminal still reach it and its own children inherit the ignore only if they don't
                // reset it. Upgrade to a new session/process group (setsid) if servers die on terminal signals.
                options.Arguments = new List<string> { "-c", "trap '' INT; exec \"$0\" \"$@\"", command }.Concat(args).ToList();
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            client = await McpClient.CreateAsync(new StdioClientTransport(options), cancellationToken: cts.Token);
            var listed = await client.ListToolsAsync(cancellationToken: cts.Token);
            // Registered only after everything succeeded, so a timed-out server leaves no late tools behind.
            foreach (var tool in listed)
            {
                var t = tool.ProtocolTool;
                var api = $"mcp__{SafeName(name)}__{SafeName(t.Name)}".TakeCodePoints(128);
                if (_tools.ContainsKey(api)) // duplicate names would make every request fail
                {
                    Errors.Add($"{name}: skipped {t.Name} (name clash with {api})");
                    continue;
                }
                _tools[api] = (client, t.Name);
                // Python's `t.input_schema or {"type": "object"}`: an empty schema is replaced too.
                var schema = t.InputSchema.ValueKind == JsonValueKind.Object ? JsonNode.Parse(t.InputSchema.GetRawText()) as JsonObject : null;
                Schemas.Add(Json.Obj(("name", api), ("description", (t.Description ?? "").TakeCodePoints(1024)),
                    ("input_schema", schema is { Count: > 0 } ? schema : Json.Obj(("type", "object")))));
            }
            _clients.Add(client);
        }
        catch (Exception e) // one broken server must not stop the harness
        {
            if (client is not null)
                await CloseAsync(client);
            var msg = e.InnerException is { } i ? $"{e.Message} ({i.Message})" : e.Message;
            Errors.Add($"{name}: {msg}".TakeCodePoints(200));
        }
    }

    // Python: ext.py MCPHub.call
    public async Task<McpCallResult> CallAsync(string apiName, JsonObject args, CancellationToken ct)
    {
        var (client, real) = _tools[apiName];
        var arguments = args.ToDictionary(kv => kv.Key, kv => (object?)JsonSerializer.SerializeToElement(kv.Value));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(300));
        CallToolResult r;
        try
        {
            r = await client.CallToolAsync(real, arguments, cancellationToken: cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("MCP call timed out after 300 s");
        }
        var parts = r.Content.Select(c => c is TextContentBlock { Text.Length: > 0 } t ? t.Text : $"[{c.Type} content]").ToList();
        return new(parts.Count == 0 ? "[no output]" : string.Join("\n", parts), r.IsError ?? false);
    }

    // Python: ext.py MCPHub.close
    public async ValueTask DisposeAsync() => await Task.WhenAll(_clients.Select(CloseAsync));

    // Python: ext.py MCPHub.close (one server)
    private static async Task CloseAsync(McpClient client)
    {
        try { await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch { /* a server that won't close must not block exit */ }
    }
}
