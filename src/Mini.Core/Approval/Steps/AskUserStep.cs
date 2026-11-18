// Python: tools.py Approver.ok (the terminal prompt)
namespace Mini.Core.Approval;

/// <summary>The last word: the user, at the terminal. Always decides.</summary>
public sealed class AskUserStep(IUserInteraction interaction) : IApprovalStep
{
    public async ValueTask<ApprovalDecision?> TryDecideAsync(ApprovalRequest request, ApprovalTrail trail, CancellationToken ct)
    {
        var answer = await interaction.ConfirmAsync(new ApprovalPrompt(request.Kind, request.Detail), ct);
        if (answer.Allowed)
            return ApprovalDecision.Allow;
        // A reason from the prompt itself (too tall for the screen) replaces the judge's; a plain "no" keeps what the judge said.
        return answer.Reason.Length > 0 ? answer : ApprovalDecision.Deny(trail.Reason);
    }
}
