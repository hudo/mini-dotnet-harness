using System.Globalization;
using System.Text.Json.Nodes;

// Python: loop.py Agent.dispatch (the ask_user branch)
// Python: loop.py EXTRA_SCHEMAS["ask_user"]
namespace Mini.Core.Tools.Agentic;

/// <summary>The arguments of ask_user. Its failures read "Bad arguments: question", and unknown keys are ignored.</summary>
// Python: loop.py Agent.dispatch (the "Bad arguments" text)
public sealed record AskUserArgs(string Question, IReadOnlyList<string> Options) : IToolArgs<AskUserArgs>
{
    public static AskUserArgs Bind(ToolArgs args) => new(
        Json.Str(args["question"]) ?? throw ToolArgs.Bad("question"),
        args["options"] is JsonArray list ? list.Select(o => Json.Str(o) ?? o?.ToJsonString() ?? "None").ToList() : []);
}

/// <summary>Asks the user a question and waits for the answer. Left out of headless runs, where nobody answers.</summary>
public sealed class AskUserTool(ApprovalSettings approval, IUserInteraction interaction, IAnswerLog answers) : ITool
{
    // Python: loop.py EXTRA_SCHEMAS["ask_user"]
    public ToolDefinition Definition { get; } = ToolDefinition.Function("ask_user",
        """Ask the user a question and wait for the answer. Use it only when you can't go on without """
        + """their decision. When the answer is a choice, list the options: the user picks a number or """
        + """types their own answer.""",
        Json.Obj(("question", Json.Obj(("type", "string"))),
                 ("options", Json.Obj(("type", "array"), ("items", Json.Obj(("type", "string"))), ("description", """Choices, if any""")))),
        "question");

    // Python: loop.py Agent.dispatch (the ask_user branch)
    public async Task<ToolOutput> InvokeAsync(ToolCall call, CancellationToken ct)
    {
        if (!approval.Interactive)
            return ToolOutput.Of("Nobody can answer: this is a headless run. Assume what's most reasonable, say so, go on.");
        interaction.BeginQuestion();
        var args = ToolArgs.Bind<AskUserArgs>(call.Input);
        var answer = await interaction.AskAsync(new UserQuestion(args.Question, args.Options), ct);
        if (answer.Length == 0) answer = "(no answer)";
        // the model wrote the option: not the user's words
        if (answer.All(char.IsDigit) && int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out var pick) && pick > 0 && pick <= args.Options.Count)
            return ToolOutput.Of($"The user picked option {answer}: {args.Options[pick - 1]}");
        answers.Record(args.Question, answer); // typed by the user: the judge counts it
        return ToolOutput.Of(answer);
    }
}
