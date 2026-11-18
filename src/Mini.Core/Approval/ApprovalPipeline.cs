// Python: tools.py Approver.ok (the order of its checks)
namespace Mini.Core.Approval;

/// <summary>Asks the steps in order until one decides.</summary>
public sealed class ApprovalPipeline(IReadOnlyList<IApprovalStep> steps) : IApprovalService
{
    /// <summary>The steps in the order <c>Approver.ok</c> checks them: auto-approve, safe command, MCP allow-list, too long to review, judge, headless, ask the user.</summary>
    /// <param name="settings">What /permissions changes while the session runs.</param>
    /// <param name="interaction">Asks the user; the last step.</param>
    /// <param name="judge">The reviewer model.</param>
    /// <param name="context">What the judge may see of the session.</param>
    // Python: tools.py Approver.ok
    public static ApprovalPipeline Create(ApprovalSettings settings, IUserInteraction interaction, IApprovalJudge judge, IJudgeContext context) =>
        new([
            new AutoApproveStep(settings),
            new SafeCommandStep(settings),
            new McpAllowListStep(settings),
            new TooLongToReviewStep(),
            new JudgeStep(settings, judge, context),
            new HeadlessStep(settings),
            new AskUserStep(interaction),
        ]);

    // ct is checked right after ReadLine returns (see the shortcut comment there).
    // Python: tools.py Approver.ok
    public async Task<ApprovalDecision> RequestAsync(ApprovalRequest request, CancellationToken ct)
    {
        var trail = new ApprovalTrail();
        foreach (var step in steps)
        {
            if (await step.TryDecideAsync(request, trail, ct) is { } decision)
                return decision;
        }

        throw new InvalidOperationException("no approval step decided: the last one must always decide");
    }
}
