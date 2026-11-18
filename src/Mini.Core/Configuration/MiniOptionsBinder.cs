using System.Globalization;

// This file is the one place that reads the process environment (BannedSymbols.txt bans it everywhere else in Mini.Core and Mini.Cli).
#pragma warning disable RS0030

// Python: loop.py MAIN_MODEL, COMPACT_AT
// Python: session.py HOME (MINI_NET_HOME instead of MINI_HOME)
// Python: tools.py find_bash (the variables it reads)
namespace Mini.Core.Configuration;

/// <summary>Binds <see cref="MiniOptions"/> from the process environment: the one place Mini.Core reads it.</summary>
// Python: none (C#-only: Python reads os.environ at each use; the environment is read once here and the options passed on)
public static class MiniOptionsBinder
{
    /// <summary>The real environment, read now.</summary>
    public static MiniOptions FromEnvironment() =>
        Bind(ReadLive, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>One variable of the live process environment, read at the moment of the call. For the few readers that cannot
    /// use a startup snapshot: the credential guard of <c>AnthropicClientFactory</c> (the SDK reads the real environment, so a
    /// copy taken at startup would be stale; the provider keys are named by models.json).</summary>
    public static string? ReadLive(string name) => Environment.GetEnvironmentVariable(name);

    /// <summary>The same binding over any source of variables, so a test needs no real ones.</summary>
    /// <param name="get">The value of a variable, or null when it is not set.</param>
    /// <param name="userProfileFolder">What the OS says the user's profile folder is.</param>
    /// <exception cref="FormatException">MINI_COMPACT_AT is not a whole number (Python's int() fails at import too).</exception>
    public static MiniOptions Bind(Func<string, string?> get, string userProfileFolder) => new()
    {
        NetHome = get("MINI_NET_HOME"),
        Model = get("MINI_MODEL"),
        CompactAt = get("MINI_COMPACT_AT") is { } compact ? int.Parse(compact, CultureInfo.InvariantCulture) : null,
        NoTask = get("MINI_NO_TASK") == "1",
        NoWeb = get("MINI_NO_WEB") == "1",
        Bash = get("MINI_BASH"),
        HasAnthropicApiKey = !string.IsNullOrEmpty(get("ANTHROPIC_API_KEY")),
        OpenRouterApiKey = get("OPENROUTER_API_KEY"),
        Home = get("HOME"),
        UserProfile = get("USERPROFILE"),
        UserProfileFolder = userProfileFolder,
        SearchPath = get("PATH"),
        ProgramFiles = get("ProgramFiles"),
        LocalAppData = get("LOCALAPPDATA"),
        SystemRoot = get("SystemRoot"),
    };
}
