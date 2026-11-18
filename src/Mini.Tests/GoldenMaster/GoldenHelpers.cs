using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mini.Tests.Fakes;

namespace Mini.Tests.GoldenMaster;

/// <summary>Shared by the golden-master tests: a fixed workspace layout and the only normalizations allowed.</summary>
static class GoldenHelpers
{
    /// <summary>The known text of the workspace's AGENTS.md, so the prompt is stable.</summary>
    public const string AgentsMd = "# Project rules\n\nUse tabs.\nRun `make test` before you finish.\n";

    /// <summary>The known text of memory/INDEX.md for the variant that has one.</summary>
    public const string MemoryIndex = "- build.md: how to build\n- style.md: code style\n";

    /// <summary><see cref="GoldenFile.Assert"/>; regenerate with <c>MINI_UPDATE_GOLDEN=1</c>.</summary>
    public static void Assert(string name, string actual) => GoldenFile.Assert(name, actual);

    /// <summary>A workspace with the fixed AGENTS.md.</summary>
    public static Workspace NewWorkspace(Func<MiniOptions, MiniOptions>? configure = null)
    {
        var ws = new Workspace(configure);
        ws.Write("AGENTS.md", AgentsMd);
        return ws;
    }

    /// <summary>
    /// The ONLY normalizations applied to a system prompt before it is compared. Everything else must match byte for byte.
    /// <list type="number">
    /// <item>The workspace path (a fresh temp dir per run) becomes <c>&lt;CWD&gt;</c>. This also covers skill paths under it.</item>
    /// <item>The start time and zone in "session started ..." become <c>&lt;TIMESTAMP&gt;</c>.</item>
    /// <item>The OS name and shell in "Environment: ... (the bash tool runs ...)" become <c>&lt;OS&gt;</c> and <c>&lt;SHELL&gt;</c>:
    /// they differ between macOS, Linux and Windows and between machines with and without bash on PATH.</item>
    /// </list>
    /// </summary>
    public static string NormalizeSystem(string system, Workspace ws)
    {
        var text = system.Replace(ws.Root, "<CWD>").Replace(PathResolver.RealPath(ws.Root), "<CWD>");
        text = Regex.Replace(text, @"session started \d{4}-\d{2}-\d{2} \d{2}:\d{2} [^\n]*", "session started <TIMESTAMP>");
        text = Regex.Replace(text, @"^Environment: .*? \(the bash tool runs .*?\) · model", "Environment: <OS> (the bash tool runs <SHELL>) · model", RegexOptions.Multiline);
        return text;
    }

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Indented JSON, key order preserved, "\n" line ends and one trailing newline.</summary>
    public static string PrettyJson(JsonNode node) => node.ToJsonString(Pretty).Replace("\r\n", "\n") + "\n";

    /// <summary>Text files end with one newline, so diffs stay clean; the prompt itself is compared as is up to that.</summary>
    public static string WithNewline(string text) => text + "\n";
}
