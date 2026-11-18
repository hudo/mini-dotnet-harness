using System.Diagnostics;

// Python: tools.py spawn (Windows without Git Bash)
namespace Mini.Core.Tools.Shell;

/// <summary>Windows without Git Bash: cmd by full path, since a bare name would search the project first.</summary>
public sealed class CmdLauncher(string? systemRoot) : IShellLauncher
{
    // Python: tools.py spawn (Windows branch)
    public ProcessStartInfo CreateStart(string command, string cwd, string log, bool job, bool killJobs)
    {
        var psi = new ProcessStartInfo { WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardInput = true };
        psi.Environment["PAGER"] = "cat";
        psi.Environment["PYTHONUNBUFFERED"] = "1";
        psi.FileName = Path.Combine(systemRoot ?? @"C:\Windows", @"System32\cmd.exe");
        psi.Arguments = $"/d /s /c \"({command}) >>\"{log}\" 2>&1\"";
        return psi;
    }
}
