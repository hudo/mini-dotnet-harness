using System.Text;
using System.Text.RegularExpressions;

// Python: tools.py LocalTools.find, SCHEMAS["find"]
namespace Mini.Core.Tools.Files;

/// <summary>The arguments of find.</summary>
// Python: loop.py Agent.dispatch (the "Bad arguments" text)
public sealed record FindArgs(string Name, string Contains, string Path) : IToolArgs<FindArgs>
{
    public static FindArgs Bind(ToolArgs args)
    {
        args.Only("name", "contains", "path");
        return new(args.Opt("name", "*"), args.Opt("contains", ""), args.Opt("path", "."));
    }
}

/// <summary>Search with no shell. Asks only outside the project; skips dot-folders, dependency folders and secrets.</summary>
public sealed class FindTool(WorkspacePolicy policy, IApprovalService approval) : ITool
{
    private static readonly UTF8Encoding Lossy = new(false);

    // Python: tools.py SCHEMAS["find"]
    public ToolDefinition Definition { get; } = ToolDefinition.Function("find",
        """Find files by name, or lines by content (path:line: text), in a folder or one file. Read-only; """
        + """searching outside the working directory goes through approval. Paths come back relative to the working directory. Skips dot-folders, """
        + """node_modules and __pycache__, symlinked folders, and the contents of files that may hold secrets. Stops at 500 results or 30 seconds.""",
        Json.Obj(("name", ToolDefinition.Property("string", """Glob matched against file names only (no /), such as *.py; default *""")),
                 ("contains", ToolDefinition.Property("string", """.NET regex to match lines; omit to list files""")),
                 ("path", ToolDefinition.Property("string", """Folder or file to search (default the working directory)"""))));

    // Python: loop.py Agent.dispatch (getattr(self.local, name)(**args))
    public Task<ToolOutput> InvokeAsync(ToolCall call, CancellationToken ct) => RunAsync(ToolArgs.Bind<FindArgs>(call.Input), ct);

    // Python: tools.py LocalTools.find
    public async Task<ToolOutput> RunAsync(FindArgs args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (name, contains, path) = args;
        var root = policy.Resolve(path);
        Regex? rx;
        // A pattern from the model, so it can't be a [GeneratedRegex]. The timeout is not in Python; the 30 s limit below is only checked between files, so one bad pattern would hang.
        try { rx = contains.Length > 0 ? new Regex(contains, RegexOptions.None, TimeSpan.FromSeconds(2)) : null; } // bad regex: re.error
        catch (ArgumentException e) { throw new ToolException($"error: {e.Message}"); }
        if (name.Contains('/') || !(File.Exists(root) || Directory.Exists(root)))
            throw new ToolException(name.Contains('/') ? "`name` matches file names only; put the folder in `path`." : "No such path.");
        if (policy.IsPrivate(root))
        {
            var decision = await approval.RequestAsync(new ApprovalRequest(ApprovalKind.Find,
                $"{root} · name {name} · contains {(contains.Length > 0 ? contains : "-")}", IsSecret: SecretClassifier.IsSecret(root)), ct);
            if (!decision.Allowed)
                throw new ToolException("User denied this search (outside the project, or a file that may hold secrets)." + decision.Reason);
        }
        var end = Environment.TickCount64 + 30_000;
        var hits = new List<string>();
        long size = 0;
        try
        {
            foreach (var (d, files) in DirectoryWalker.Walk(root, name))
            {
                foreach (var f in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var p = Path.Combine(d, f);
                    var shown = (policy.IsUnderCwd(p) ? Path.GetRelativePath(policy.Cwd, p) : p).Replace(Path.DirectorySeparatorChar, '/').Replace("\n", "\\n");
                    var real = p; // a link inside the project may lead to a secret: judge and read what it points at
                    if (rx is not null)
                    {
                        try { if (new FileInfo(p).LinkTarget is not null) real = PathResolver.RealPath(p); }
                        catch (IOException) { continue; } // a loop of links: unreadable, skip it
                        if (!WorkspacePolicy.IsSame(p, root) && policy.IsPrivate(real)) continue; // names are listed; secrets' contents aren't searched
                    }
                    string text;
                    try
                    {
                        var bytes = rx is not null && File.Exists(real) ? await Task.Run(() => RegularFileReader.ReadCapped(real, ImageBudget.MaxFile - 1)).WaitAsync(ct) : null;
                        text = bytes is null ? "" : Lossy.GetString(bytes);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or ToolException) // unreadable or not a regular file (p.is_file()): skip it, keep the rest
                    {
                        continue;
                    }
                    if (rx is null || text.IndexOf('\0', 0, Math.Min(8000, text.Length)) < 0) // skip binaries
                    {
                        var added = rx is null ? new List<string> { shown }
                            : LineSplitter.Split(text).Select((x, i) => (x, i)).Where(t => rx.IsMatch(t.x)).Select(t => $"{shown}:{t.i + 1}: {t.x.TakeCodePoints(300)}").ToList();
                        hits.AddRange(added);
                        size += added.Sum(x => x.Length + 1);
                    }
                    if (hits.Count >= 500 || size > OutputLimit.Max - 200 || Environment.TickCount64 > end)
                    {
                        return ToolOutput.Of(string.Join("\n", hits.Take(500)).TakeCodePoints(OutputLimit.Max - 200)
                            + "\n[stopped early (500 results, 30,000 characters or 30 s): narrow the search]");
                    }
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            throw new ToolException("error: the regex took over 2 s on one line: simplify it.");
        }
        return ToolOutput.Of(hits.Count > 0 ? string.Join("\n", hits) : "[no matches]");
    }
}
