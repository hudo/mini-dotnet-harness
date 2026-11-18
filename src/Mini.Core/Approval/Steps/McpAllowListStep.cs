// Python: tools.py Approver.ok (the `kind == "mcp" and any(...)` check)
namespace Mini.Core.Approval;

/// <summary>An MCP call whose tool name starts with the first word of an allow rule.</summary>
public sealed class McpAllowListStep(ApprovalSettings settings) : IApprovalStep
{
    public ValueTask<ApprovalDecision?> TryDecideAsync(ApprovalRequest request, ApprovalTrail trail, CancellationToken ct) =>
        ValueTask.FromResult<ApprovalDecision?>(request.Kind == ApprovalKind.Mcp && settings.Allow.Any(a =>
            request.Detail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].StartsWith(a[0], StringComparison.Ordinal))
            ? ApprovalDecision.Allow : null);
}
