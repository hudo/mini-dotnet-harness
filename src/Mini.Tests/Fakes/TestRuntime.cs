using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Mini.Core;

namespace Mini.Tests.Fakes;

/// <summary>
/// The services of one test, built from options instead of environment variables and statics: a fake clock, paths under the
/// run's temp MINI_NET_HOME, a model catalog of its own (a test may add entries to it) and a shell that found bash from the options, over a job registry of its own.
/// </summary>
// Python test helper: tests/test_mini.py ws, monkeypatch (the module globals and os.environ a Python test patches)
public sealed class TestRuntime
{
    /// <summary>Fri 2026-11-06 02:30 UTC: DeepSeek peak hours (the PEAK of tests/test_mini.py), the clock's start.</summary>
    public static readonly DateTimeOffset Peak = DateTimeOffset.FromUnixTimeSeconds(1793932200);

    /// <summary>Sat 2026-11-07 02:30 UTC: a weekend, off-peak all day (the WEEKEND of tests/test_mini.py).</summary>
    public static readonly DateTimeOffset Weekend = DateTimeOffset.FromUnixTimeSeconds(1794018600);

    /// <summary>The options: what the process environment gave (PATH, HOME), with MINI_NET_HOME at the run's temp folder, then the test's changes.</summary>
    public MiniOptions Options { get; }
    public MiniPaths Paths { get; }
    public ModelCatalog Catalog { get; }
    public PriceSchedule Prices { get; }
    public SessionCatalog Sessions { get; }

    /// <summary>Stands still until a test moves it.</summary>
    public FakeTimeProvider Time { get; }
    /// <summary>Where bash was found.</summary>
    public ShellLocator Locator { get; }

    /// <summary>Runs bash commands, over <see cref="Jobs"/>.</summary>
    public IShellRunner Shell { get; }

    /// <summary>What this test's bash calls left running (its own registry, not shared with other tests).</summary>
    public JobRegistry Jobs { get; }

    /// <summary>Reads the live environment, as the composition root's factory does: the credential tests set variables with <see cref="EnvScope"/>.</summary>
    public AnthropicClientFactory Clients { get; }

    /// <summary>Makes agents over these services.</summary>
    public AgentFactory Factory { get; }

    /// <param name="configure">Changes the options (a variable the harness reads, as an option).</param>
    /// <param name="now">The clock's start; <see cref="Peak"/> by default.</param>
    /// <param name="agentOptions">The agent's options (MaxTokens); the defaults when null.</param>
    /// <param name="judge">The decision model; the real <see cref="JevJudge"/> on the real HTTP client when null (a test that wants no network passes its own).</param>
    public TestRuntime(Func<MiniOptions, MiniOptions>? configure = null, DateTimeOffset? now = null, AgentOptions? agentOptions = null, IApprovalJudge? judge = null)
    {
        var options = MiniOptionsBinder.FromEnvironment() with { NetHome = TestEnvironment.Home };
        Options = configure?.Invoke(options) ?? options;
        Paths = new MiniPaths(Options);
        Catalog = ModelCatalogLoader.Load(Options, System.IO.Path.Combine(AppContext.BaseDirectory, "models.json"));
        Time = new FakeTimeProvider(now ?? Peak);
        Prices = new PriceSchedule(Time);
        Sessions = new SessionCatalog(Paths, Time);
        Locator = new ShellLocator(Options);
        Jobs = new JobRegistry();
        IShellLauncher launcher = Locator.Bash is not null || !OperatingSystem.IsWindows() ? new BashLauncher(Locator.Bash ?? "/bin/sh") : new CmdLauncher(Options.SystemRoot);
        Shell = new ShellRunner(launcher, Paths, Time, Jobs);
        Clients = new AnthropicClientFactory(MiniOptionsBinder.ReadLive);
        judge ??= new ServiceCollection().AddSingleton(Options).AddJudge().BuildServiceProvider().GetRequiredService<IApprovalJudge>();
        Factory = new AgentFactory(Options, Paths, Catalog, Prices, Time, Sessions, Locator, Shell, Jobs, Clients, agentOptions ?? new AgentOptions(), judge);
    }
}
