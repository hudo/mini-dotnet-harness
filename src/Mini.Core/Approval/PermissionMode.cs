namespace Mini.Core.Approval;

/// <summary>Who approves risky calls: the user (Ask), a reviewer model with the user as fallback (Judge), or nobody (Yes).</summary>
// Python: none (C#-only: the Python Approver.mode is a plain string)
public enum PermissionMode { Ask, Judge, Yes }
