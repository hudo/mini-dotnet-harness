// Python: tools.py Approver.ok (its arguments: kind, detail, private, secret)
namespace Mini.Core.Approval;

/// <summary>One action that may need the user's say.</summary>
/// <param name="Kind">What kind of action.</param>
/// <param name="Detail">The action as the user and the judge see it (a path, a command, the text of a write, an MCP call).</param>
/// <param name="IsPrivate">A bash command that names a path outside the project or a secret: it can't pass as a plain read.</param>
/// <param name="IsSecret">It names a file that may hold secrets: the judge doesn't decide, the user does.</param>
public sealed record ApprovalRequest(ApprovalKind Kind, string Detail, bool IsPrivate = false, bool IsSecret = false);
