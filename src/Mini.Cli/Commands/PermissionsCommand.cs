// Python: cli.py command (the /permissions branch)
namespace Mini.Cli.Commands;

/// <summary>/permissions [mode]: shows who approves risky calls, and changes it when a mode is given.</summary>
public sealed class PermissionsCommand : ISlashCommand
{
    public string Name => "/permissions";

    public string? Help => "/permissions [mode]  ask, judge or yes: who approves risky calls";

    public Task ExecuteAsync(ReplContext context, string args, CancellationToken ct)
    {
        var agent = context.Agent;
        if (PermissionModeExtensions.ParseCliName(args) is { } mode)
            agent.ChangePermissions(mode);
        context.Out.WriteLine($"permissions {agent.Approval.Mode.ToCliName()} · ask, judge or yes");
        return Task.CompletedTask;
    }
}
