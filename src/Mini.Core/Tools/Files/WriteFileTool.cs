using System.Text;

// Python: tools.py LocalTools.write, SCHEMAS["write"]
namespace Mini.Core.Tools.Files;

/// <summary>The arguments of write.</summary>
// Python: loop.py Agent.dispatch (the "Bad arguments" text)
public sealed record WriteArgs(string Path, string Content) : IToolArgs<WriteArgs>
{
    public static WriteArgs Bind(ToolArgs args)
    {
        args.Only("path", "content");
        return new(args.Req("path"), args.Req("content"));
    }
}

/// <summary>Creates a file, or replaces a whole file the model has read.</summary>
public sealed class WriteFileTool(WorkspacePolicy policy, IApprovalService approval, FileReadTracker seen) : ITool
{
    private static readonly UTF8Encoding Lossy = new(false);

    // Python: tools.py SCHEMAS["write"]
    public ToolDefinition Definition { get; } = ToolDefinition.Function("write",
        """Create a file, or replace a whole file. To overwrite an existing file, read it first; for """
        + """small changes use edit. Writing outside the working directory, into dot-folders, or to """
        + """files that run later (Makefile, conftest.py, mcp.json, executables) goes through approval.""",
        Json.Obj(("path", ToolDefinition.Property("string")), ("content", ToolDefinition.Property("string"))),
        "path", "content");

    // Python: loop.py Agent.dispatch (getattr(self.local, name)(**args))
    public Task<ToolOutput> InvokeAsync(ToolCall call, CancellationToken ct) => RunAsync(ToolArgs.Bind<WriteArgs>(call.Input), ct);

    // Python: tools.py LocalTools.write
    public async Task<ToolOutput> RunAsync(WriteArgs args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var p = policy.Resolve(args.Path);
        var content = args.Content;
        if ((File.Exists(p) || Directory.Exists(p)) && (!seen.TryGet(p, out var known) || FileReadTracker.MtimeOf(p) != known))
            throw new ToolException("File exists and you haven't read its current version. Read it first, or use edit.");
        if (policy.NeedsWriteApproval(p))
        {
            var decision = await approval.RequestAsync(new ApprovalRequest(ApprovalKind.Write, $"{p}\n{content}"), ct);
            if (!decision.Allowed)
                throw new ToolException("User denied this write (outside the project, or a file that runs later)." + decision.Reason);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        if (File.Exists(p) && File.ReadAllBytes(p).AsSpan().IndexOf("\r\n"u8) >= 0) // keep a Windows file's line endings
            content = content.Replace("\r\n", "\n").Replace("\n", "\r\n");
        File.WriteAllText(p, content, Lossy);
        seen.RecordCurrent(p);
        return ToolOutput.Of($"Wrote {LineSplitter.Split(content).Count} lines to {p}");
    }
}
