using Mini.Tests.Fakes;

namespace Mini.Tests.GoldenMaster;

/// <summary>Parity contract item 1: the <c>tools</c> array a new session freezes (key order preserved).</summary>
public class ToolsGoldenTests
{
    static void Check(string name, Agent agent) => GoldenHelpers.Assert(name, GoldenHelpers.PrettyJson(agent.ToolsJson()));

    // C#-only: the main agent's tools (interactive approver: ask_user is present)
    [Fact]
    public void Main()
    {
        using var ws = GoldenHelpers.NewWorkspace();
        var (agent, _) = ws.Make([], approver: new ApprovalSettings());
        Check("tools.main.json", agent);
    }

    // C#-only: a subagent has read, find, bash and web_search only
    [Fact]
    public void Sub()
    {
        using var ws = GoldenHelpers.NewWorkspace();
        var (agent, _) = ws.Make([], approver: new ApprovalSettings(), sub: true);
        Check("tools.sub.json", agent);
    }

    // C#-only: a headless approver leaves out ask_user
    [Fact]
    public void Headless()
    {
        using var ws = GoldenHelpers.NewWorkspace();
        var (agent, _) = ws.Make([], approver: new ApprovalSettings(interactive: false));
        Check("tools.headless.json", agent);
    }

    // C#-only: MINI_NO_TASK=1 leaves out task (goal_done stays: the harness starts the reviewer)
    [Fact]
    public void NoTask()
    {
        using var ws = GoldenHelpers.NewWorkspace(o => o with { NoTask = true });
        var (agent, _) = ws.Make([], approver: new ApprovalSettings());
        Check("tools.no-task.json", agent);
    }

    // C#-only: MINI_NO_WEB=1 leaves out web_search
    [Fact]
    public void NoWeb()
    {
        using var ws = GoldenHelpers.NewWorkspace(o => o with { NoWeb = true });
        var (agent, _) = ws.Make([], approver: new ApprovalSettings());
        Check("tools.no-web.json", agent);
    }

    // C#-only: another provider: the task tool loses its model enum (one model, so subagents use it too)
    [Fact]
    public void DeepSeek()
    {
        using var ws = GoldenHelpers.NewWorkspace();
        var (agent, _) = ws.Make([], approver: new ApprovalSettings(), model: "deepseek-flash");
        Check("tools.deepseek.json", agent);
    }

    // C#-only: MCP tools go after goal_done and before web_search. A hub with no config file starts with no servers; its
    // public Schemas list stands in for what a server would have registered (the Agent only reads Schemas and Errors).
    [Fact]
    public async Task WithMcpHub()
    {
        using var ws = GoldenHelpers.NewWorkspace();
        await using var hub = await ws.StartMcpAsync();
        hub.Schemas.Add(Json.Obj(("name", "mcp__echo__shout"), ("description", "Return the text in upper case."),
            ("input_schema", Json.Obj(("type", "object"), ("properties", Json.Obj(("text", Json.Obj(("type", "string"))))),
                ("required", new System.Text.Json.Nodes.JsonArray("text"))))));
        hub.Schemas.Add(Json.Obj(("name", "mcp__echo__ping"), ("description", ""), ("input_schema", Json.Obj(("type", "object")))));
        hub.Errors.Add("nope: Connection closed");
        var (agent, _) = ws.Make([], approver: new ApprovalSettings(), hub: hub);
        Check("tools.mcp.json", agent);
        GoldenHelpers.Assert("system-prompt.main-mcp-hub.txt",
            GoldenHelpers.WithNewline(GoldenHelpers.NormalizeSystem(agent.System, ws)));
    }
}
