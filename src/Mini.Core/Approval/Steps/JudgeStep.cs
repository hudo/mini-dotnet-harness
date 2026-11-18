// Python: tools.py Approver.ok (the `self.use_judge and self.judge` check)
namespace Mini.Core.Approval;

/// <summary>Lets the reviewer model rule, unless it is a gated write or names a secret: then the user decides. An "ask" verdict passes the request on with the judge's reason noted.</summary>
public sealed class JudgeStep(ApprovalSettings settings, IApprovalJudge judge, IJudgeContext context) : IApprovalStep
{
    public async ValueTask<ApprovalDecision?> TryDecideAsync(ApprovalRequest request, ApprovalTrail trail, CancellationToken ct)
    {
        if (!settings.UseJudge || request.Kind == ApprovalKind.Write || request.IsSecret) // gated writes, secrets: the user decides
            return null;
        var (choice, why) = await judge.JudgeAsync(request, context, ct);
        ct.ThrowIfCancellationRequested(); // an "allow" that came back after Ctrl-C must not start the command
        trail.Add($" Judge: {why}");
        return choice == JudgeChoice.Ask ? null : new ApprovalDecision(choice == JudgeChoice.Allow, trail.Reason);
    }
}
