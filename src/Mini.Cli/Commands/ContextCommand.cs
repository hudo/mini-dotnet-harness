// Python: cli.py command (the /context branch)
namespace Mini.Cli.Commands;

/// <summary>/context: how full the context is, and what fills it.</summary>
public sealed class ContextCommand(ContextReport report) : ISlashCommand
{
    public string Name => "/context";

    public string? Help => "/context            how full the context is, and what fills it";

    public async Task ExecuteAsync(ReplContext context, string args, CancellationToken ct) =>
        context.Out.WriteLine(await report.BuildAsync(context.Agent, ct));
}
