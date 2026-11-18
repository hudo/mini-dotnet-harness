using Microsoft.Extensions.DependencyInjection;

// Python: none (C#-only: the composition root of the command line; Python's cli.py builds what it needs in main)
namespace Mini.Cli.Hosting;

/// <summary>Registers what only the command line needs, on top of <c>AddMiniCore</c>.</summary>
public static class CliServiceCollectionExtensions
{
    /// <summary>The Ctrl-C state, the REPL, the headless runner, the root command's action and the slash commands. The order of the
    /// commands is the order of <c>/help</c>.</summary>
    public static IServiceCollection AddMiniCli(this IServiceCollection services)
    {
        services.AddSingleton<ConsoleCancellation>();
        services.AddSingleton<ContextReport>();
        services.AddSingleton<SlashCommandRouter>();
        services.AddSingleton<Repl>();
        services.AddSingleton<HeadlessRunner>();
        services.AddSingleton<MiniCommand>();
        services.AddSingleton<ISlashCommand, NewCommand>();
        services.AddSingleton<ISlashCommand, EffortCommand>();
        services.AddSingleton<ISlashCommand, PermissionsCommand>();
        services.AddSingleton<ISlashCommand, SessionsCommand>();
        services.AddSingleton<ISlashCommand, ResumeCommand>();
        services.AddSingleton<ISlashCommand, GoalCommand>();
        services.AddSingleton<ISlashCommand, DreamCommand>();
        services.AddSingleton<ISlashCommand, ContextCommand>();
        services.AddSingleton<ISlashCommand, CompactCommand>();
        services.AddSingleton<ISlashCommand, HelpCommand>();
        return services;
    }
}
