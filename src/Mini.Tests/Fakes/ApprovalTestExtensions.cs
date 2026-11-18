namespace Mini.Tests.Fakes;

/// <summary>What the tests that ask the approval directly (Python: Approver.ok) call: the settings, asked through the real pipeline with the console prompt and no judge.</summary>
// Python test helper: tests/test_mini.py (Approver(...).ok(kind, detail))
static class ApprovalTestExtensions
{
    /// <summary>Is the action approved? <paramref name="kind"/> is "bash", "read", "write", "find" or "mcp".</summary>
    public static async Task<bool> OkAsync(this ApprovalSettings settings, string kind, string detail, bool isPrivate = false, bool secret = false,
        CancellationToken ct = default) =>
        (await settings.AskAsync(kind, detail, isPrivate, secret, ct)).Allowed;

    /// <summary>The full decision, with the reason a denial adds to the tool's error.</summary>
    public static Task<ApprovalDecision> AskAsync(this ApprovalSettings settings, string kind, string detail, bool isPrivate = false, bool secret = false,
        CancellationToken ct = default)
    {
        var kinds = Enum.GetValues<ApprovalKind>().ToDictionary(k => k.Wire());
        var pipeline = ApprovalPipeline.Create(settings, new ConsoleUserInteraction(new ConsoleAgentOutput(silent: true)),
            new NoJudge(), new NoJudgeContext());
        return pipeline.RequestAsync(new ApprovalRequest(kinds[kind], detail, isPrivate, secret), ct);
    }

    sealed class NoJudge : IApprovalJudge
    {
        public Task<JudgeVerdict> JudgeAsync(ApprovalRequest request, IJudgeContext context, CancellationToken ct) =>
            throw new InvalidOperationException("the judge was asked, but this test gave none");
    }

    sealed class NoJudgeContext : IJudgeContext
    {
        public string WorkingDirectory => "";
        public IEnumerable<JudgeUtterance> Utterances() => [];
        public string? ActiveGoalObjective => null;
        public void RecordSpend(double cost, System.Text.Json.Nodes.JsonObject verdict) { }
    }
}
