// Python: tools.py LocalTools.bash
// Python: tools.py SCHEMAS["bash"]
// Python: tools.py WINDOWS (OperatingSystem.IsWindows())
namespace Mini.Core.Tools.Shell;

/// <summary>The arguments of bash.</summary>
// Python: loop.py Agent.dispatch (the "Bad arguments" text)
public sealed record BashArgs(string Command, int Timeout, bool Background) : IToolArgs<BashArgs>
{
    public static BashArgs Bind(ToolArgs args)
    {
        args.Only("command", "timeout", "background");
        return new(args.Req("command"), args.Int("timeout", 120), args.Bool("background"));
    }
}

/// <summary>Runs a shell command in the working directory.</summary>
public sealed class BashTool(WorkspacePolicy policy, CommandPathInspector inspector, IApprovalService approval, IShellRunner shell, JobRegistry jobs) : ITool
{
    // The description depends on the platform: Windows kills a command at its timeout, elsewhere it goes on in the background.
    // Python: tools.py SCHEMAS["bash"]
    public ToolDefinition Definition { get; } = ToolDefinition.Function("bash",
        """Run a shell command in the working directory and get its output (stdout and stderr together) """
        + """and the exit code. Use it for git, tests and builds; search files with the find tool instead. """
        + """There is no terminal: commands get no input, so interactive programs end at once. Output over """
        + """30,000 characters is cut in the middle. """ + (OperatingSystem.IsWindows() ? """A command still running at the timeout is killed. """
        : """A command still running at the timeout isn't killed: it goes on in the background """
        + """until it ends or mini exits, and you get its pid, the log file where its output continues, and """
        + """its output so far. """) + """For servers, long builds and other long jobs, set background: the """
        + """command starts detached, writes to a log file, and the call returns at once with the pid and """
        + """the log's path. Output piped into tail, grep or less appears only when the command ends, so """
        + """to watch a long one, send its output to a file and read that. Commands other than plain reads """
        + """(ls, cat or head of project files, read-only git; no pipes or redirects) go through approval.""",
        Json.Obj(("command", ToolDefinition.Property("string")),
                 ("timeout", ToolDefinition.Property("integer", """Seconds before """ + (OperatingSystem.IsWindows() ? """it is killed""" : """it goes on in the background""")
                                              + """ (default 120)""")),
                 ("background", ToolDefinition.Property("boolean", """Start it detached and return at once (default false). """
                                                 + """It runs detached already, so don't add & or nohup"""))),
        "command");

    // Python: loop.py Agent.dispatch (getattr(self.local, name)(**args))
    public Task<ToolOutput> InvokeAsync(ToolCall call, CancellationToken ct) => RunAsync(ToolArgs.Bind<BashArgs>(call.Input), ct);

    // Python: tools.py LocalTools.bash
    public async Task<ToolOutput> RunAsync(BashArgs args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (command, timeout, background) = args;
        var secret = (inspector.Words(command) ?? new List<string>()).Any(SecretClassifier.IsSecret); // names a secret file: you decide
        // Its own stop, and only that: exactly `kill PID` for a command left running at its timeout, which the result
        // tells the model to run last. That signals the shell alone, so the tree isn't claimed stopped, and the children
        // (`pgrep -P PID` lists them) are ordinary commands that go through approval like any other.
        var stop = command.Trim(' ', '\t', '\n');
        var told = jobs.Running.Where(j => !j.HasExited && stop == $"kill {j.Pid}").ToList();
        if (told.Count == 0)
        {
            var decision = await approval.RequestAsync(new ApprovalRequest(ApprovalKind.Bash, command, inspector.NamesPrivate(command), secret), ct);
            if (!decision.Allowed)
                throw new ToolException("User denied this command." + decision.Reason);
        }
        var earlier = jobs.All();
        if (background)
            return ToolOutput.Of(shell.StartJob(command, policy.Cwd) + jobs.StillRunning(earlier));
        ShellResult r;
        try { r = await shell.RunAsync(command, policy.Cwd, Math.Max(timeout, 1), keep: !OperatingSystem.IsWindows(), ct); }
        catch (TimeoutException) { throw new ToolException($"Timed out after {timeout}s, so it was stopped."); } // Windows only (see ShellRunner.RunAsync)
        var o = r.Output.Trim(); // .NET already reports a signal death as 128+N
        return ToolOutput.Of((r.Code is { } c && c != 0 ? $"{o}\n[exit {c}]" : o.Length > 0 ? o : "[no output]") + jobs.StillRunning(earlier));
    }
}
