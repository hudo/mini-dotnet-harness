namespace Mini.Core.Approval;

/// <summary>The words --permissions and /permissions use.</summary>
// Python: none (C#-only: the CLI spells modes as strings)
public static class PermissionModeExtensions
{
    /// <summary>"ask", "judge" or "yes": the exact text shown to the user and the model.</summary>
    public static string ToCliName(this PermissionMode mode) => mode switch
    {
        PermissionMode.Yes => "yes",
        PermissionMode.Judge => "judge",
        _ => "ask",
    };

    /// <summary>The mode for "ask", "judge" or "yes" (exact, lower case); null for anything else.</summary>
    public static PermissionMode? ParseCliName(string? name) => name switch
    {
        "ask" => PermissionMode.Ask,
        "judge" => PermissionMode.Judge,
        "yes" => PermissionMode.Yes,
        _ => null,
    };
}
