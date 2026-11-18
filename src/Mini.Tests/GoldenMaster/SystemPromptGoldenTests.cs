using Mini.Tests.Fakes;

namespace Mini.Tests.GoldenMaster;

/// <summary>
/// REFACTOR-PLAN §2 parity contract item 1: the system prompt the model sees stays byte-identical. See
/// <see cref="GoldenHelpers.NormalizeSystem"/> for the three (and only) normalizations.
/// </summary>
public class SystemPromptGoldenTests
{
    const string Claude = "claude-opus-5-5";

    static void Check(string name, string system, Workspace ws) =>
        GoldenHelpers.Assert(name, GoldenHelpers.WithNewline(GoldenHelpers.NormalizeSystem(system, ws)));

    static string Build(Workspace ws, ApprovalSettings approver, bool sub = false, string model = Claude, bool @delegate = true,
        IReadOnlyList<Skill>? skills = null, IReadOnlyList<string>? mcpErrors = null) =>
        new SystemPromptBuilder(ws.Runtime.Locator, ws.Runtime.Time).Build(new SystemPromptRequest(ws.Root, skills ?? [], mcpErrors ?? [], sub, model, @delegate, ApprovalStatement.Describe(approver)));

    // C#-only: pins what a new main agent freezes into its session (the wiring from Agent to SystemPromptBuilder, not only the builder)
    [Fact]
    public void MainAgent()
    {
        using var ws = GoldenHelpers.NewWorkspace();
        var (agent, _) = ws.Make([], approver: new ApprovalSettings());
        Check("system-prompt.main.txt", agent.System, ws);
    }

    // C#-only: the subagent prompt: no delegation, memory or skills sections
    [Fact]
    public void SubAgent()
    {
        using var ws = GoldenHelpers.NewWorkspace();
        var (agent, _) = ws.Make([], approver: new ApprovalSettings(), sub: true);
        Check("system-prompt.sub.txt", agent.System, ws);
    }

    // C#-only: MINI_NO_TASK=1 drops the delegation paragraph
    [Fact]
    public void MainAgentWithoutTaskTool()
    {
        using var ws = GoldenHelpers.NewWorkspace(o => o with { NoTask = true });
        var (agent, _) = ws.Make([], approver: new ApprovalSettings());
        Check("system-prompt.main-no-task.txt", agent.System, ws);
    }

    // C#-only: another provider: the model name in the Environment line, the same sections
    [Fact]
    public void DeepSeekAgent()
    {
        using var ws = GoldenHelpers.NewWorkspace();
        var (agent, _) = ws.Make([], approver: new ApprovalSettings(), model: "deepseek-flash");
        Check("system-prompt.deepseek.txt", agent.System, ws);
    }

    // C#-only: SystemPromptBuilder directly, with no delegation paragraph (the builder's own switch)
    [Fact]
    public void BuilderWithoutDelegate()
    {
        using var ws = GoldenHelpers.NewWorkspace();
        Check("system-prompt.no-delegate.txt", Build(ws, new ApprovalSettings(), @delegate: false), ws);
    }

    // C#-only: a workspace with memory/INDEX.md
    [Fact]
    public void MemoryIndexIsEmbedded()
    {
        using var ws = GoldenHelpers.NewWorkspace();
        ws.Write("memory/INDEX.md", GoldenHelpers.MemoryIndex);
        Check("system-prompt.memory-index.txt", Build(ws, new ApprovalSettings()), ws);
    }

    // C#-only: a workspace without AGENTS.md
    [Fact]
    public void NoProjectInstructions()
    {
        using var ws = new Workspace();
        Check("system-prompt.no-agents-md.txt", Build(ws, new ApprovalSettings()), ws);
    }

    // C#-only: the skills section, with a SKILL.md under skills/ and one under .claude/skills/ (paths normalized to <CWD>)
    [Fact]
    public void WithSkills()
    {
        using var ws = GoldenHelpers.NewWorkspace();
        ws.Write("skills/release-notes/SKILL.md", "---\nname: release-notes\ndescription: Write release notes from git log\n---\nSteps...");
        ws.Write(".claude/skills/review/SKILL.md", "---\nname: review\ndescription: >-\n  Review a diff.\n  Use before merging.\n---\n# Steps\n");
        var skills = ws.FindSkills();
        Assert.Equal(2, skills.Count);
        Check("system-prompt.skills.txt", Build(ws, new ApprovalSettings(), skills: skills), ws);
    }

    // C#-only: the MCP failure line
    [Fact]
    public void WithMcpErrors()
    {
        using var ws = GoldenHelpers.NewWorkspace();
        var errors = new[] { "nope: Connection closed", "linear: only stdio servers (\"command\") are supported" };
        Check("system-prompt.mcp-errors.txt", Build(ws, new ApprovalSettings(), mcpErrors: errors), ws);
    }

    // C#-only: one full prompt per approval statement (the statement is the last line of the Environment paragraph)
    [Theory]
    [InlineData("ask")]
    [InlineData("ask-allow")]
    [InlineData("judge")]
    [InlineData("judge-headless")]
    [InlineData("yes")]
    [InlineData("headless")]
    [InlineData("headless-allow")]
    public void EachApprovalStatement(string mode)
    {
        using var ws = GoldenHelpers.NewWorkspace();
        Check($"system-prompt.approval-{mode}.txt", Build(ws, ApprovalGoldenTests.Make(mode)), ws);
    }
}
