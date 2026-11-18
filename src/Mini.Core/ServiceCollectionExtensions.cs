using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

// Python: none (C#-only: the composition root; Python's modules build their globals at import)
namespace Mini.Core;

/// <summary>Registers everything Mini.Core needs. The only place that wires the services together; nothing else resolves from the provider.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the options, clock, paths, model catalog, price schedule, session catalog, job registry, shell, client factory, agent options, the judge and the <see cref="IAgentFactory"/>.</summary>
    /// <param name="services">The collection to add to.</param>
    /// <param name="options">The options bound from the environment (or built by a test).</param>
    /// <param name="modelsJsonPath">The models.json that ships with the program. Null: next to the executable.</param>
    /// <remarks>A <see cref="TimeProvider"/> registered before this call (a test's fake) is kept; otherwise the system clock is used.</remarks>
    public static IServiceCollection AddMiniCore(this IServiceCollection services, MiniOptions options, string? modelsJsonPath = null)
    {
        services.AddSingleton(Options.Create(options));
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<MiniPaths>();
        services.AddSingleton(_ => ModelCatalogLoader.Load(options, modelsJsonPath ?? Path.Combine(AppContext.BaseDirectory, "models.json")));
        services.AddSingleton<PriceSchedule>();
        services.AddSingleton<SessionCatalog>();
        services.AddSingleton<JobRegistry>();
        services.AddSingleton<ShellLocator>();
        services.AddSingleton<IShellLauncher>(sp => ShellLauncher(sp.GetRequiredService<ShellLocator>(), options));
        services.AddSingleton<IShellRunner, ShellRunner>();
        services.AddSingleton(_ => new AnthropicClientFactory(MiniOptionsBinder.ReadLive));
        services.AddOptions<AgentOptions>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AgentOptions>>().Value);
        services.AddJudge();
        services.AddSingleton<IAgentFactory, AgentFactory>();
        return services;
    }

    /// <summary>The judge: a typed client with the 20 s timeout of a call to the decision model. Also used by the test runtime, which builds its services by hand
    /// (it needs a <see cref="MiniOptions"/> registered, for the OpenRouter key).</summary>
    internal static IServiceCollection AddJudge(this IServiceCollection services)
    {
        services.AddHttpClient<JevJudge>(c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddSingleton<IApprovalJudge>(sp => sp.GetRequiredService<JevJudge>());
        return services;
    }

    /// <summary>Bash (or /bin/sh) wherever there is a POSIX shell, and Windows' cmd when Git Bash isn't there.</summary>
    // Python: tools.py spawn (the choice between the shell and cmd)
    private static IShellLauncher ShellLauncher(ShellLocator locator, MiniOptions options) =>
        locator.Bash is not null || !OperatingSystem.IsWindows() ? new BashLauncher(locator.Bash ?? "/bin/sh") : new CmdLauncher(options.SystemRoot);
}
