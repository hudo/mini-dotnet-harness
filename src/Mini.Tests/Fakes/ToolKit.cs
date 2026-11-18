using System.Text.Json.Nodes;

namespace Mini.Tests.Fakes;

/// <summary>
/// The five local tools of one workspace, wired as the Agent wires them (one policy, one read tracker, one image budget, one approval
/// pipeline), with the typed calls the tests make. It takes the place of the old LocalTools in the tests: the tests'
/// assertions are what they were, only the way they reach the tools changed.
/// </summary>
// Python test helper: tests/test_mini.py (LocalTools(str(ws), Approver(...)))
public sealed class ToolKit
{
    readonly ReadFileTool _read;
    readonly WriteFileTool _write;
    readonly EditFileTool _edit;
    readonly FindTool _find;
    readonly BashTool _bash;
    readonly ToolExecutor _executor;

    /// <param name="root">The working directory.</param>
    /// <param name="settings">Who approves; the tests flip it as /permissions does.</param>
    /// <param name="runtime">The services of the test.</param>
    /// <param name="judge">The reviewer; none: a judge that fails the test if it is asked.</param>
    public ToolKit(string root, ApprovalSettings settings, TestRuntime runtime, IApprovalJudge? judge = null)
    {
        Settings = settings;
        Policy = new WorkspacePolicy(root, runtime.Paths);
        Images = new ImageBudget();
        var inspector = new CommandPathInspector(Policy);
        var interaction = new ConsoleUserInteraction(new ConsoleAgentOutput(silent: true));
        Approval = ApprovalPipeline.Create(settings, interaction, judge ?? new UnexpectedJudge(), new FakeJudgeContext(root));
        var seen = new FileReadTracker();
        _read = new ReadFileTool(Policy, Approval, seen, Images);
        _write = new WriteFileTool(Policy, Approval, seen);
        _edit = new EditFileTool(Policy, Approval, seen);
        _find = new FindTool(Policy, Approval);
        _bash = new BashTool(Policy, inspector, Approval, runtime.Shell, runtime.Jobs);
        _executor = new ToolExecutor(new ToolRegistry([_read, _write, _edit, _bash, _find]), new ConsoleAgentOutput(silent: true));
    }

    public ApprovalSettings Settings { get; }
    public WorkspacePolicy Policy { get; }
    public IApprovalService Approval { get; }
    public ImageBudget Images { get; }

    /// <summary>The working directory, as the OS reports it.</summary>
    public string Cwd => Policy.Cwd;

    /// <summary>Base64 bytes of images the transcript holds.</summary>
    public long Held
    {
        get => Images.Held;
        set => Images.Reset(value);
    }

    /// <summary>A JsonValue string, or a JsonArray of text and image blocks.</summary>
    public async Task<JsonNode> ReadAsync(string path, int offset = 1, int limit = 2000, CancellationToken ct = default) =>
        (await _read.RunAsync(new ReadArgs(path, offset, limit), ct)).Content;

    public async Task<string> WriteAsync(string path, string content, CancellationToken ct = default) =>
        (await _write.RunAsync(new WriteArgs(path, content), ct)).Text!;

    public async Task<string> EditAsync(string path, string old, string @new, bool replaceAll = false, CancellationToken ct = default) =>
        (await _edit.RunAsync(new EditArgs(path, old, @new, replaceAll), ct)).Text!;

    public async Task<string> FindAsync(string name = "*", string contains = "", string path = ".", CancellationToken ct = default) =>
        (await _find.RunAsync(new FindArgs(name, contains, path), ct)).Text!;

    public async Task<string> BashAsync(string command, int timeout = 120, bool background = false, CancellationToken ct = default) =>
        (await _bash.RunAsync(new BashArgs(command, timeout, background), ct)).Text!;

    /// <summary>Dispatch by name, as the executor does it: bad arguments and unknown tools are ToolExceptions.</summary>
    public async Task<JsonNode> CallAsync(string name, JsonObject args, CancellationToken ct) =>
        (await _executor.InvokeAsync(new ToolCall(null, name, args), ct)).Content;

    /// <summary>A judge nobody expects to hear from: the tests that set useJudge give their own.</summary>
    sealed class UnexpectedJudge : IApprovalJudge
    {
        public Task<JudgeVerdict> JudgeAsync(ApprovalRequest request, IJudgeContext context, CancellationToken ct) =>
            throw new InvalidOperationException("the judge was asked, but this test gave none");
    }

    /// <summary>A session with nothing in it.</summary>
    sealed class FakeJudgeContext(string cwd) : IJudgeContext
    {
        public string WorkingDirectory => cwd;
        public IEnumerable<JudgeUtterance> Utterances() => [];
        public string? ActiveGoalObjective => null;
        public void RecordSpend(double cost, JsonObject verdict) { }
    }
}
