using System.Text.Json.Nodes;
using Mini.Tests.Fakes;

namespace Mini.Tests.Tools;

/// <summary>A real stdio MCP server with one tool, `mcp__mail__send`, answering "sent". It is a short stdlib-only python3
/// script (the Python suite's echo_server.py needs the `mcp` package; this one needs nothing but python3 on PATH).</summary>
static class StubMcp
{
    const string Script = """
        import json, sys
        for line in sys.stdin:
            m = json.loads(line)
            if "id" not in m:
                continue
            method = m["method"]
            if method == "initialize":
                res = {"protocolVersion": m["params"]["protocolVersion"], "capabilities": {"tools": {}},
                       "serverInfo": {"name": "mail", "version": "1"}}
            elif method == "tools/list":
                res = {"tools": [{"name": "send", "description": "send mail",
                                  "inputSchema": {"type": "object", "properties": {"to": {"type": "string"}}}}]}
            elif method == "tools/call":
                res = {"content": [{"type": "text", "text": "sent"}]}
            else:  # e.g. the client's server/discover probe: "method not found" makes it fall back to initialize
                print(json.dumps({"jsonrpc": "2.0", "id": m["id"], "error": {"code": -32601, "message": "Method not found"}}), flush=True)
                continue
            print(json.dumps({"jsonrpc": "2.0", "id": m["id"], "result": res}), flush=True)
        """;

    public static async Task<McpHub> StartAsync(Workspace ws)
    {
        var script = ws.Write("stub_mcp_server.py", Script);
        File.WriteAllText(ws.Path("mcp.json"), new JsonObject
        {
            ["mcpServers"] = new JsonObject { ["mail"] = new JsonObject { ["command"] = "python3", ["args"] = new JsonArray(script) } },
        }.ToJsonString());
        var hub = await ws.StartMcpAsync();
        Assert.True(hub.Errors.Count == 0, string.Join("\n", hub.Errors));
        Assert.True(hub.Has("mcp__mail__send"));
        return hub;
    }
}
