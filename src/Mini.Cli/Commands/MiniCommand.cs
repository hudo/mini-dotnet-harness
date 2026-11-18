// Python: cli.py main (after argparse)
namespace Mini.Cli.Commands;

/// <summary>What the root command does: list the models, or start an agent and hand it to the REPL or to a headless run.</summary>
public sealed class MiniCommand(IAgentFactory factory, MiniOptions environment, ModelCatalog catalog, MiniPaths paths, SessionCatalog sessions,
    AnthropicClientFactory clients, Repl repl, HeadlessRunner headless, ConsoleCancellation cancellation)
{
    /// <summary>The exit code.</summary>
    public async Task<int> ExecuteAsync(CliOptions options)
    {
        if (options.ListModels) // listing Claude models needs an Anthropic key; the others are always listed
        {
            var claude = environment.HasAnthropicApiKey ? await clients.ListModelsAsync() : [];
            Console.Out.WriteLine(string.Join("\n", claude.Concat(catalog.Other.Keys)));
            return 0;
        }

        var cwd = Directory.GetCurrentDirectory();
        var isHeadless = options.Headless;
        var ui = new ConsoleAgentOutput(silent: isHeadless && options.Json);
        // Jev by default when its key is there, but a headless run only uses it when asked: nobody watches it.
        var mode = options.Permissions ?? (environment.HasOpenRouterApiKey && !isHeadless ? "judge" : "ask");
        var approval = new ApprovalSettings(auto: mode == "yes", interactive: !isHeadless, allow: options.Allow, useJudge: mode == "judge");
        await using var hub = await McpHub.StartAsync(cwd, paths);
        cancellation.UseHub(hub);
        var skills = SkillCatalog.Find(cwd, paths);
        foreach (var e in hub.Errors) ui.Warn($"MCP {e}");

        ISessionStore? session = null;
        if (options.Continue || !string.IsNullOrEmpty(options.Resume))
        {
            session = sessions.Find(cwd, options.Resume);
            if (session is null)
            {
                Console.Error.WriteLine("No session found.");
                return 1;
            }
        }

        var launcher = new AgentLauncher(factory, cwd, ui, new ConsoleUserInteraction(ui), approval, hub, skills, options);
        try
        {
            var agent = launcher.Start(session ?? sessions.Create(cwd));
            if (isHeadless)
                return await headless.RunAsync(agent, options);
            await repl.RunAsync(agent, sessions, launcher.Start);
            return 0;
        }
        catch (Exception e) when (e is MiniException or InvalidOperationException) // e.g. a missing API key (a RuntimeError in Python)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
        catch (Exception e) // Python prints the traceback and exits 1; without this the runtime aborts the process (exit 134)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }
}
