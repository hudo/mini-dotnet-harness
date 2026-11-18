using System.Text;

// Python: tools.py LocalTools.edit, SCHEMAS["edit"]
namespace Mini.Core.Tools.Files;

/// <summary>The arguments of edit.</summary>
// Python: loop.py Agent.dispatch (the "Bad arguments" text)
public sealed record EditArgs(string Path, string Old, string New, bool ReplaceAll) : IToolArgs<EditArgs>
{
    public static EditArgs Bind(ToolArgs args)
    {
        args.Only("path", "old", "new", "replace_all");
        return new(args.Req("path"), args.Req("old"), args.Req("new"), args.Bool("replace_all"));
    }
}

/// <summary>Replaces exact text in a file the model has read since it last changed.</summary>
public sealed class EditFileTool(WorkspacePolicy policy, IApprovalService approval, FileReadTracker seen) : ITool
{
    private static readonly UTF8Encoding Lossy = new(false), Strict = new(false, true); // edit must not rewrite bytes it couldn't decode

    // Python: tools.py SCHEMAS["edit"]
    public ToolDefinition Definition { get; } = ToolDefinition.Function("edit",
        """Replace exact text in a file you have read since it last changed. `old` must match the file """
        + """character for character, including indentation, and must not include the line-number """
        + """prefixes that read shows. It must occur once, unless replace_all is true; add surrounding """
        + """lines to make it unique.""",
        Json.Obj(("path", ToolDefinition.Property("string")), ("old", ToolDefinition.Property("string")), ("new", ToolDefinition.Property("string")),
                 ("replace_all", ToolDefinition.Property("boolean", """Replace every occurrence (default false)"""))),
        "path", "old", "new");

    // Python: loop.py Agent.dispatch (getattr(self.local, name)(**args))
    public Task<ToolOutput> InvokeAsync(ToolCall call, CancellationToken ct) => RunAsync(ToolArgs.Bind<EditArgs>(call.Input), ct);

    // Python: tools.py LocalTools.edit
    public async Task<ToolOutput> RunAsync(EditArgs args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (path, old, @new, replaceAll) = args;
        var p = policy.Resolve(path);
        if (!seen.TryGet(p, out var known))
            throw new ToolException("Read the file with the read tool before editing it (bash output doesn't count).");
        if (FileReadTracker.MtimeOf(p) != known)
            throw new ToolException("File changed since you last read it. Read it again.");
        if (policy.NeedsWriteApproval(p))
        {
            var detail = string.Join("\n", new[] { p }.Concat(old.Split('\n').Select(x => "- " + x)).Concat(@new.Split('\n').Select(x => "+ " + x)));
            var decision = await approval.RequestAsync(new ApprovalRequest(ApprovalKind.Write, detail), ct);
            if (!decision.Allowed)
                throw new ToolException("User denied this edit (outside the project, or a file that runs later)." + decision.Reason);
        }
        if (old.Length == 0)
            throw new ToolException("`old` must not be empty.");
        string text; // keep the file's own line endings
        try { text = Strict.GetString(File.ReadAllBytes(p)); }
        catch (DecoderFallbackException) { throw new ToolException("File is not valid UTF-8, so edit could not change it without damaging it. Use bash."); }
        if (!text.Contains(old, StringComparison.Ordinal) && text.Contains("\r\n", StringComparison.Ordinal)) // the model writes \n; the file uses \r\n
        {
            old = old.Replace("\r\n", "\n").Replace("\n", "\r\n");
            @new = @new.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }
        var n = PythonString.Count(text, old);
        if (n == 0)
        {
            var (done, hits) = LooseEditor.Edit(text, old, @new);
            if (done is not null)
            {
                File.WriteAllText(p, done, Lossy);
                seen.RecordCurrent(p);
                return ToolOutput.Of($"Edited {p} (1 replaced, ignoring line endings and trailing whitespace)");
            }
            throw new ToolException(hits > 0 ? $"`old` matches {hits} places once whitespace is ignored. Add context."
                : "`old` not found. It must match the file exactly, including whitespace." + FuzzyLineMatcher.Closest(text, old));
        }
        if (n > 1 && !replaceAll)
            throw new ToolException($"`old` appears {n} times. Add context to make it unique, or set replace_all.");
        File.WriteAllText(p, replaceAll ? text.Replace(old, @new, StringComparison.Ordinal) : PythonString.ReplaceFirst(text, old, @new), Lossy);
        seen.RecordCurrent(p);
        return ToolOutput.Of($"Edited {p} ({(replaceAll ? n : 1)} replaced)");
    }
}
