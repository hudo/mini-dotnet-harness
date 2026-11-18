using System.Text.Json.Nodes;
using Mini.Tests.Fakes;

namespace Mini.Tests.Tools;

/// <summary>task, ask_user, goal_done and the MCP adapter, over fake ports: no agent, no terminal.</summary>
// Python test helper: tests/test_mini.py (test_ask_user*, the goal_done and task dispatch cases)
public class AgenticToolsTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static ToolCall Call(string name, JsonObject input) => new("id", name, input);

    sealed class Answers : IAnswerLog
    {
        public List<(string Question, string Answer)> Recorded { get; } = [];
        public void Record(string question, string answer) => Recorded.Add((question, answer));
    }

    sealed class FakeRunner(Func<string, string?, string> run) : ISubagentRunner
    {
        public List<(string Prompt, string? Model)> Calls { get; } = [];

        public Task<string> RunAsync(string prompt, string? model, CancellationToken ct)
        {
            Calls.Add((prompt, model));
            return Task.FromResult(run(prompt, model));
        }
    }

    // --- ask_user ---

    [Fact]
    public async Task AskUserInAHeadlessRunNeverAsksAndNeverClosesTheLine()
    {
        var user = new ScriptedUserInteraction(["x"]);
        var tool = new AskUserTool(new ApprovalSettings(interactive: false), user, new Answers());

        var output = await tool.InvokeAsync(Call("ask_user", Json.Obj()), Ct); // even bad arguments: nobody can answer

        Assert.Equal("Nobody can answer: this is a headless run. Assume what's most reasonable, say so, go on.", output.Text);
        Assert.Empty(user.Questions);
        Assert.Equal(0, user.Begun);
    }

    [Fact]
    public async Task AskUserReturnsTheTypedAnswerAndRecordsItForTheJudge()
    {
        var user = new ScriptedUserInteraction(["postgres"]);
        var answers = new Answers();
        var tool = new AskUserTool(new ApprovalSettings(), user, answers);

        var output = await tool.InvokeAsync(Call("ask_user", Json.Obj(("question", "Which db?"))), Ct);

        Assert.Equal("postgres", output.Text);
        Assert.Equal([("Which db?", "postgres")], answers.Recorded);
        Assert.Equal(1, user.Begun);
        Assert.Equal("Which db?", user.Questions.Single().Text);
        Assert.Empty(user.Questions.Single().Options);
    }

    [Fact]
    public async Task APickedNumberIsTheModelsOptionNotTheUsersWords()
    {
        var user = new ScriptedUserInteraction(["2"]);
        var answers = new Answers();
        var tool = new AskUserTool(new ApprovalSettings(), user, answers);

        var output = await tool.InvokeAsync(Call("ask_user", Json.Obj(("question", "Which db?"), ("options", new JsonArray("postgres", "sqlite")))), Ct);

        Assert.Equal("The user picked option 2: sqlite", output.Text);
        Assert.Empty(answers.Recorded);
        Assert.Equal(["postgres", "sqlite"], user.Questions.Single().Options);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("-1")]
    [InlineData("99999999999999999999")]
    public async Task ANumberOutsideTheOptionsIsAnAnswerLikeAnyOther(string typed)
    {
        var answers = new Answers();
        var tool = new AskUserTool(new ApprovalSettings(), new ScriptedUserInteraction([typed]), answers);

        var output = await tool.InvokeAsync(Call("ask_user", Json.Obj(("question", "q"), ("options", new JsonArray("a", "b")))), Ct);

        Assert.Equal(typed, output.Text);
        Assert.Equal([("q", typed)], answers.Recorded);
    }

    [Fact]
    public async Task AnEmptyAnswerIsNoAnswer()
    {
        var answers = new Answers();
        var tool = new AskUserTool(new ApprovalSettings(), new ScriptedUserInteraction([""]), answers);

        var output = await tool.InvokeAsync(Call("ask_user", Json.Obj(("question", "q"))), Ct);

        Assert.Equal("(no answer)", output.Text);
        Assert.Equal([("q", "(no answer)")], answers.Recorded);
    }

    [Fact]
    public async Task AskUserWithoutAQuestionIsABadArgumentAfterTheLineWasClosed()
    {
        var user = new ScriptedUserInteraction(["x"]);
        var tool = new AskUserTool(new ApprovalSettings(), user, new Answers());

        var e = await Assert.ThrowsAsync<ToolException>(() => tool.InvokeAsync(Call("ask_user", Json.Obj()), Ct));

        Assert.Equal("Bad arguments: question", e.Message);
        Assert.Equal(1, user.Begun);
        Assert.Empty(user.Questions);
    }

    [Fact]
    public async Task EndOfInputOnAQuestionPropagates()
    {
        var tool = new AskUserTool(new ApprovalSettings(), new ScriptedUserInteraction(), new Answers());

        await Assert.ThrowsAsync<EndOfStreamException>(() => tool.InvokeAsync(Call("ask_user", Json.Obj(("question", "q"))), Ct));
    }

    [Fact]
    public async Task AskUserFollowsTheSettingsAtEachCall()
    {
        var settings = new ApprovalSettings(interactive: false);
        var tool = new AskUserTool(settings, new ScriptedUserInteraction(["yes"]), new Answers());

        Assert.StartsWith("Nobody can answer", (await tool.InvokeAsync(Call("ask_user", Json.Obj(("question", "q"))), Ct)).Text);
        settings.Interactive = true;
        Assert.Equal("yes", (await tool.InvokeAsync(Call("ask_user", Json.Obj(("question", "q"))), Ct)).Text);
    }

    // --- goal_done ---

    [Fact]
    public async Task GoalDoneWithNoActiveGoalSaysSoAndClaimsNothing()
    {
        var slot = new GoalClaimSlot();
        var tool = new GoalDoneTool(slot, () => false);

        var output = await tool.InvokeAsync(Call("goal_done", Json.Obj(("evidence", "ran tests"))), Ct);

        Assert.Equal("No goal is active.", output.Text);
        Assert.Null(slot.Value);
    }

    [Fact]
    public async Task GoalDoneWithAnActiveGoalWritesTheClaimToTheSlot()
    {
        var slot = new GoalClaimSlot();
        var tool = new GoalDoneTool(slot, () => true);

        var output = await tool.InvokeAsync(Call("goal_done", Json.Obj(("evidence", "ran tests"))), Ct);

        Assert.Equal("Recorded. The harness will check it after this turn.", output.Text);
        Assert.Equal("ran tests", slot.Value);
        slot.Clear();
        Assert.Null(slot.Value);
        await tool.InvokeAsync(Call("goal_done", Json.Obj()), Ct);
        Assert.Equal("", slot.Value); // a claim without evidence is still a claim
    }

    // --- task ---

    [Fact]
    public async Task TaskPassesThePromptAndTheModelToTheRunnerAndReturnsItsAnswer()
    {
        var runner = new FakeRunner((p, _) => $"done: {p}");
        using var ws = new Workspace();
        var tool = new TaskTool(runner, ws.Runtime.Catalog, singleModel: false);

        var output = await tool.InvokeAsync(Call("task", Json.Obj(("prompt", "read it"), ("model", "claude-haiku-5-5"))), Ct);

        Assert.Equal("done: read it", output.Text);
        Assert.Equal([("read it", "claude-haiku-5-5")], runner.Calls);
    }

    [Fact]
    public async Task TaskWithoutAPromptIsABadArgumentAndRunsNothing()
    {
        var runner = new FakeRunner((_, _) => "x");
        using var ws = new Workspace();
        var tool = new TaskTool(runner, ws.Runtime.Catalog, singleModel: false);

        var e = await Assert.ThrowsAsync<ToolException>(() => tool.InvokeAsync(Call("task", Json.Obj()), Ct));

        Assert.Equal("Bad arguments: prompt", e.Message);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void TheTaskDefinitionHasAModelEnumUnlessThereIsOneModel()
    {
        using var ws = new Workspace();
        var catalog = ws.Runtime.Catalog;
        var many = new TaskTool(new FakeRunner((_, _) => ""), catalog, singleModel: false).Definition.ToJson();
        var one = new TaskTool(new FakeRunner((_, _) => ""), catalog, singleModel: true).Definition.ToJson();

        Assert.Equal(["name", "description", "strict", "input_schema"], many.Select(p => p.Key));
        Assert.Equal(["prompt", "model"], many["input_schema"]!["properties"]!.AsObject().Select(p => p.Key));
        Assert.Equal([catalog.SubModel, catalog.CheapModel], many["input_schema"]!["properties"]!["model"]!["enum"]!.AsArray().Select(m => Json.Str(m)));
        // one model: the description comes last (Python builds it by assigning after schema())
        Assert.Equal(["name", "strict", "input_schema", "description"], one.Select(p => p.Key));
        Assert.Equal(["prompt"], one["input_schema"]!["properties"]!.AsObject().Select(p => p.Key));
        Assert.Equal(Json.Str(many["description"]), Json.Str(one["description"]));
    }

    // --- definitions ---

    [Fact]
    public void EveryFunctionToolKeepsTheKeyOrderNameDescriptionStrictInputSchema()
    {
        var tool = ToolDefinition.Function("t", "d", Json.Obj(("a", ToolDefinition.Property("string", "the a"))), "a");

        var json = tool.ToJson();

        Assert.Equal("""{"name":"t","description":"d","strict":true,"input_schema":{"type":"object","properties":{"a":{"type":"string","description":"the a"}},"required":["a"],"additionalProperties":false}}""", json.ToJsonString());
        Assert.NotSame(tool.Schema, json); // a JsonNode has one parent: every request gets a copy
        Assert.Equal("""{"type":"string"}""", ToolDefinition.Property("string").ToJsonString());
    }

    [Fact]
    public void TheWebSearchDefinitionIsTheServerToolsShape()
    {
        Assert.Equal("""{"type":"web_search_20260209","name":"web_search","allowed_callers":["direct"]}""", WebSearchTool.Definition.ToJson().ToJsonString());
        Assert.Equal("web_search", WebSearchTool.Definition.Name);
    }

    // --- MCP ---

    [Fact]
    public async Task AnMcpToolIsDeniedWithTheApprovalsReasonAndNeverCallsTheServer()
    {
        using var ws = new Workspace();
        await using var hub = await ws.StartMcpAsync(); // no mcp.json: no servers
        var schema = Json.Obj(("name", "mcp__echo__shout"), ("description", "d"), ("input_schema", Json.Obj(("type", "object"))));
        var tool = new McpTool(hub, schema, new FixedApproval(ApprovalDecision.Deny(" Nobody can approve it in this run.")));

        var e = await Assert.ThrowsAsync<ToolException>(() => tool.InvokeAsync(Call("mcp__echo__shout", Json.Obj(("text", "hi"))), Ct));

        Assert.Equal("User denied this tool call. Nobody can approve it in this run.", e.Message);
        Assert.Equal("mcp__echo__shout", tool.Definition.Name);
        Assert.Equal(schema.ToJsonString(), tool.Definition.ToJson().ToJsonString());
    }

    [Fact]
    public async Task TheApprovalOfAnMcpCallIsTheNameAndTheArgumentsAsPythonDumpsThem()
    {
        using var ws = new Workspace();
        await using var hub = await ws.StartMcpAsync();
        var seen = new List<ApprovalRequest>();
        var tool = new McpTool(hub, Json.Obj(("name", "mcp__x__y"), ("input_schema", Json.Obj())), new FixedApproval(ApprovalDecision.Deny(""), seen));

        await Assert.ThrowsAsync<ToolException>(() => tool.InvokeAsync(Call("mcp__x__y", Json.Obj(("a", 1), ("b", "é"))), Ct));

        Assert.Equal(new ApprovalRequest(ApprovalKind.Mcp, "mcp__x__y {\"a\": 1, \"b\": \"\\u00e9\"}"), seen.Single());
    }

    sealed class FixedApproval(ApprovalDecision decision, List<ApprovalRequest>? seen = null) : IApprovalService
    {
        public Task<ApprovalDecision> RequestAsync(ApprovalRequest request, CancellationToken ct)
        {
            seen?.Add(request);
            return Task.FromResult(decision);
        }
    }
}
