using System.Text.Json.Nodes;

// Python: loop.py Agent.dispatch (the task branch)
// Python: loop.py EXTRA_SCHEMAS["task"]
namespace Mini.Core.Tools.Agentic;

/// <summary>The arguments of task. Its failures read "Bad arguments: prompt", and unknown keys are ignored.</summary>
// Python: loop.py Agent.dispatch (the "Bad arguments" text)
public sealed record TaskArgs(string Prompt, string? Model) : IToolArgs<TaskArgs>
{
    public static TaskArgs Bind(ToolArgs args) =>
        new(Json.Str(args["prompt"]) ?? throw ToolArgs.Bad("prompt"), Json.Str(args["model"]));
}

/// <summary>Delegates a self-contained task to a subagent.</summary>
/// <param name="runner">Runs the subagent.</param>
/// <param name="catalog">Names the two models the subagent may use.</param>
/// <param name="singleModel">Another provider has one model, so subagents run on it too and the tool takes no model.</param>
public sealed class TaskTool(ISubagentRunner runner, ModelCatalog catalog, bool singleModel) : ITool
{
    private const string Description =
        """Delegate a self-contained research or reading task to a subagent. It starts with an empty """
        + """context: it can't see this conversation, so the prompt must include every path, fact and """
        + """constraint it needs. It has read, find, bash and web_search, is told not to change files (its """
        + """bash calls need approval like yours), and returns only its final answer.""";

    // Python: loop.py EXTRA_SCHEMAS["task"]
    public ToolDefinition Definition { get; } = singleModel ? SingleModel() : WithModel(catalog);

    private static ToolDefinition WithModel(ModelCatalog catalog) => ToolDefinition.Function("task", Description,
        Json.Obj(("prompt", Json.Obj(("type", "string"))),
                 ("model", Json.Obj(("type", "string"), ("enum", new JsonArray(catalog.SubModel, catalog.CheapModel)),
                                    ("description", $$"""Default {{catalog.SubModel}}; {{catalog.CheapModel}} for plain reading or search""")))),
        "prompt");

    // One model: name, strict, input_schema, then the description (Python builds this one as schema(), inserts the name, then assigns the description).
    // Python: loop.py Agent.__init__ (the task tool of another provider)
    private static ToolDefinition SingleModel() => new("task", Json.Obj(("name", "task"), ("strict", true),
        ("input_schema", ToolDefinition.InputSchema(Json.Obj(("prompt", Json.Obj(("type", "string")))), ["prompt"])),
        ("description", Description)));

    // Python: loop.py Agent.dispatch (the task branch)
    public async Task<ToolOutput> InvokeAsync(ToolCall call, CancellationToken ct)
    {
        var args = ToolArgs.Bind<TaskArgs>(call.Input);
        return ToolOutput.Of(await runner.RunAsync(args.Prompt, args.Model, ct));
    }
}
