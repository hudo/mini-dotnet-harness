using System.Runtime.CompilerServices;
using Xunit;

// Statics (Shell jobs, the client factory's header guard), env vars and Console are process-wide: one collection per assembly
// runs every test serially (xunit v3.0.3 marks DisableTestParallelization obsolete and has no replacement attribute yet).
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Mini.Tests;

/// <summary>Runs before any test: clears the variables that change behavior and points MINI_NET_HOME at a temp folder, which <see cref="TestRuntime"/> and the real-binary tests use.</summary>
public static class TestEnvironment
{
    /// <summary>The throwaway MINI_NET_HOME for this test run (sessions, models.json overrides).</summary>
    public static string Home { get; private set; } = "";

    /// <summary>Every variable the harness reads that could change behavior on a developer's machine.</summary>
    static readonly string[] Cleared =
    [
        "MINI_MODEL", "MINI_NO_TASK", "MINI_NO_WEB", "MINI_COMPACT_AT", "MINI_BASH",
        "ANTHROPIC_CUSTOM_HEADERS", "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "OPENROUTER_API_KEY", "DEEPSEEK_API_KEY",
    ];

#pragma warning disable CA2255 // a module initializer is the only hook that runs before the first static in Mini is read
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Init()
    {
        foreach (var name in Cleared)
            Environment.SetEnvironmentVariable(name, null);
        Home = Directory.CreateTempSubdirectory("mini-tests-home-").FullName;
        Environment.SetEnvironmentVariable("MINI_NET_HOME", Home);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Home, recursive: true); }
            catch (IOException) { } // best effort: a test left a handle open
        };
    }
}

/// <summary>Sets environment variables for one test and restores the previous values on dispose; a null value unsets.</summary>
public sealed class EnvScope : IDisposable
{
    readonly List<(string Name, string? Old)> _saved = [];

    /// <summary>Sets each (name, value) pair now.</summary>
    public EnvScope(params (string Name, string? Value)[] vars)
    {
        foreach (var (name, value) in vars)
        {
            _saved.Add((name, Environment.GetEnvironmentVariable(name)));
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    /// <summary>Restores the values from before the scope, last set first.</summary>
    public void Dispose()
    {
        for (var i = _saved.Count - 1; i >= 0; i--)
            Environment.SetEnvironmentVariable(_saved[i].Name, _saved[i].Old);
    }
}
