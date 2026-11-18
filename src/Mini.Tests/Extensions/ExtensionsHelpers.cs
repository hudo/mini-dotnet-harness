using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Mini.Tests.Extensions;

static class ExtensionsHelpers
{
    static string RepoRoot([CallerFilePath] string here = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, "..", "..", "..", ".."));

    /// <summary>tests/echo_server.py at the repo root: the one-tool MCP server the Python tests use.</summary>
    public static string EchoServerPath => System.IO.Path.Combine(RepoRoot(), "tests", "echo_server.py");

    /// <summary>
    /// A command and arguments that start tests/echo_server.py with the <c>mcp</c> package available: the repo's .venv python,
    /// then python3, then <c>uv run --project &lt;repo root&gt;</c>. The first one that can import mcp wins.
    /// </summary>
    public static (string Command, string[] Args) EchoServer()
    {
        var root = RepoRoot();
        foreach (var python in new[] { System.IO.Path.Combine(root, ".venv", "bin", "python"), "python3" })
        {
            if (CanImportMcp(python))
                return (python, [EchoServerPath]);
        }

        return ("uv", ["run", "--project", root, "python", EchoServerPath]);
    }

    static bool CanImportMcp(string python)
    {
        try
        {
            var psi = new ProcessStartInfo(python, ["-c", "import mcp.server.mcpserver"]) { RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            p.WaitForExit(30_000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; } // not installed
    }
}
