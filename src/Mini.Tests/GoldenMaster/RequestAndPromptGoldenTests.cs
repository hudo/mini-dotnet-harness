using System.Text.Json.Nodes;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;

namespace Mini.Tests.GoldenMaster;

/// <summary>
/// Parity contract item 1: the shape of the first request (system and tools are pinned elsewhere, so they are replaced by a
/// marker here) and the prompt constants the harness sends.
/// </summary>
public class RequestAndPromptGoldenTests
{
    static async Task CheckRequest(string name, string? model, string effort = "high")
    {
        using var ws = GoldenHelpers.NewWorkspace(); // default AgentOptions: MaxTokens 0, the model's own max_output
        var (agent, fake) = ws.Make([Reply(Text("hi"))], approver: new ApprovalSettings(), model: model, effort: effort);
        await agent.TurnAsync("hi", ct: TestContext.Current.CancellationToken);
        var request = fake.Requests[0];
        // Same keys in the same places; only the two big values, which have their own golden files, are replaced.
        request["system"] = "<system: see system-prompt.*.txt>";
        request["tools"] = "<tools: see tools.*.json>";
        GoldenHelpers.Assert(name, GoldenHelpers.PrettyJson(request));
    }

    // C#-only: Claude: thinking binding, betas, output_config, fallbacks, cache_control, max_tokens
    [Fact]
    public Task RequestClaude() => CheckRequest("request.claude.json", null);

    // C#-only: a Claude model with fallbacks off (haiku): no fallbacks key, no fallback beta
    [Fact]
    public Task RequestClaudeHaikuWithoutFallbacks() => CheckRequest("request.claude-haiku.json", "claude-haiku-5-5");

    // C#-only: another effort level is passed through in output_config
    [Fact]
    public Task RequestClaudeMaxEffort() => CheckRequest("request.claude-max-effort.json", null, "max");

    // C#-only: DeepSeek: no thinking, betas or fallbacks
    [Fact]
    public Task RequestDeepSeek() => CheckRequest("request.deepseek.json", "deepseek-flash");

    // C#-only: every prompt constant the harness sends the model, in one file with headers; each is wrapped in ⟦ ⟧ so
    // that leading and trailing whitespace shows
    [Fact]
    public void HarnessPromptConstants()
    {
        var parts = new List<(string Name, string Text)>
        {
            ("Context.Base", HarnessPrompts.Base),
            ("Context.Delegate", HarnessPrompts.Delegate),
            ("Context.Memory", HarnessPrompts.Memory),
            ("Context.Sub", HarnessPrompts.Sub),
            ("Approver.Gated", ApprovalStatement.Gated),
            ("Agent.GoalOn (GOAL_RULES)", HarnessPrompts.GoalOn.Text),
            ("Agent.Review", HarnessPrompts.Review.Text),
            ("Agent.GoalOff", HarnessPrompts.GoalOff.Text),
            ("Agent.GoalNext", HarnessPrompts.GoalNext),
            ("Agent.CutOff[thinking]", HarnessPrompts.CutOff["thinking"]),
            ("Agent.CutOff[text]", HarnessPrompts.CutOff["text"]),
            ("Agent.CutOff[tool]", HarnessPrompts.CutOff["tool"]),
            ("Agent.SummarizePrompt", HarnessPrompts.Summarize),
            ("Agent.JudgePrompt", JevJudge.Prompt),
            ("Cli.Dream", DreamCommand.Prompt),
            ("Agent.FallbackBeta", AnthropicProfile.FallbackBeta),
            ("Agent.Betas", string.Join(", ", AnthropicProfile.Betas)),
        };
        var text = string.Concat(parts.Select(p => $"=== {p.Name} ===\n⟦{p.Text}⟧\n\n"));
        Assert.Equal(["thinking", "text", "tool"], HarnessPrompts.CutOff.Keys); // a new key must get a header above
        GoldenHelpers.Assert("harness-prompts.txt", text);
    }

    // C#-only: the filled templates, as the model sees them for a goal with and without a verify command
    [Fact]
    public void FilledGoalTemplates()
    {
        var text = string.Join("\n=====\n",
            HarnessPrompts.GoalOn.Render(("objective", "ship it"), ("verify", "")),
            HarnessPrompts.GoalOn.Render(("objective", "ship it"),
                ("verify", "After you call it, the harness runs `make test` and only accepts the goal if it passes. ")),
            HarnessPrompts.Review.Render(("objective", "ship it"), ("claim", "ran make test: ok"), ("verified", "")),
            HarnessPrompts.Review.Render(("objective", "ship it"), ("claim", "ran make test: ok"),
                ("verified", " The harness already ran `make test` and it passed.")),
            HarnessPrompts.GoalOff.Render(("status", "done")));
        GoldenHelpers.Assert("goal-templates.filled.txt", text + "\n");
    }
}
