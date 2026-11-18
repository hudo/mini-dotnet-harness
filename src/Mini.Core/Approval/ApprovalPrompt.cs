// Python: tools.py Approver.ok (what the prompt shows: kind and detail)
namespace Mini.Core.Approval;

/// <summary>What the user is asked to allow.</summary>
public sealed record ApprovalPrompt(ApprovalKind Kind, string Detail);
