using System.Globalization;
using System.Text;

// Python: none (C#-only: Python reads os.environ wherever it needs a value; here the process environment is read once, in MiniOptionsBinder)
namespace Mini.Core.Configuration;

/// <summary>
/// Everything mini reads from the process environment, bound once at startup by <see cref="MiniOptionsBinder"/>.
/// No other Mini.Core code asks the environment for these, so a test builds the options it needs instead of setting variables.
/// Provider keys named in models.json (DEEPSEEK_API_KEY and the like) and the Anthropic header guard are still read by
/// <see cref="AnthropicClientFactory"/>, not here.
/// </summary>
public sealed record MiniOptions
{
    /// <summary>MINI_NET_HOME: where sessions, job logs and the user's models.json, mcp.json and skills live. Null: ~/.mini-net.</summary>
    public string? NetHome { get; init; }

    /// <summary>MINI_MODEL: the main model, over models.json's "main".</summary>
    public string? Model { get; init; }

    /// <summary>MINI_COMPACT_AT: input tokens at which the context is compacted, over every model's compact_at. Read once at startup.</summary>
    public int? CompactAt { get; init; }

    /// <summary>MINI_NO_TASK=1: no `task` tool, so one loop does it all.</summary>
    public bool NoTask { get; init; }

    /// <summary>MINI_NO_WEB=1: no web_search tool (benchmarks that forbid looking things up).</summary>
    public bool NoWeb { get; init; }

    /// <summary>MINI_BASH: the bash to use on Windows, before the Git for Windows locations.</summary>
    public string? Bash { get; init; }

    /// <summary>ANTHROPIC_API_KEY is set and not empty (`mini --models` then lists Claude's models).</summary>
    public bool HasAnthropicApiKey { get; init; }

    /// <summary>OPENROUTER_API_KEY, the key Jev (the approval judge) is called with. Null: not set.</summary>
    public string? OpenRouterApiKey { get; init; }

    /// <summary>OPENROUTER_API_KEY is set and not empty: Jev is the default approver of an interactive session.</summary>
    public bool HasOpenRouterApiKey => !string.IsNullOrEmpty(OpenRouterApiKey);

    /// <summary>HOME.</summary>
    public string? Home { get; init; }

    /// <summary>USERPROFILE.</summary>
    public string? UserProfile { get; init; }

    /// <summary>The OS's own answer for the user's profile folder (Environment.SpecialFolder.UserProfile); empty when it has none.</summary>
    public string UserProfileFolder { get; init; } = "";

    /// <summary>PATH: where bash is looked for on POSIX.</summary>
    public string? SearchPath { get; init; }

    /// <summary>ProgramFiles: Git for Windows' usual place.</summary>
    public string? ProgramFiles { get; init; }

    /// <summary>LOCALAPPDATA: Git for Windows' per-user place.</summary>
    public string? LocalAppData { get; init; }

    /// <summary>SystemRoot: where cmd.exe is on Windows.</summary>
    public string? SystemRoot { get; init; }

    // The default ToString would print the OpenRouter key into any log or exception message that shows the options.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"NetHome = {NetHome}, Model = {Model}, CompactAt = {CompactAt}, NoTask = {NoTask}, NoWeb = {NoWeb}, OpenRouterApiKey = {(HasOpenRouterApiKey ? "<set>" : "<none>")}");
        return true;
    }
}
