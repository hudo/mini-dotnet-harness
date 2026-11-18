using System.Text;
using System.Text.Json.Nodes;

// Python: tools.py LocalTools.read, SCHEMAS["read"], IMAGE_PX, IMAGES_HELD
namespace Mini.Core.Tools.Files;

/// <summary>The arguments of read.</summary>
// Python: loop.py Agent.dispatch (the "Bad arguments" text)
public sealed record ReadArgs(string Path, int Offset, int Limit) : IToolArgs<ReadArgs>
{
    public static ReadArgs Bind(ToolArgs args)
    {
        args.Only("path", "offset", "limit");
        return new(args.Req("path"), args.Int("offset", 1), args.Int("limit", 2000));
    }
}

/// <summary>Reads a text file or an image, or lists a directory.</summary>
public sealed class ReadFileTool(WorkspacePolicy policy, IApprovalService approval, FileReadTracker seen, ImageBudget held) : ITool
{
    private static readonly UTF8Encoding Lossy = new(false);

    // Each prose piece is a one-line raw literal: the Python strings have no newlines, so a multi-line literal would add some.
    // Python: tools.py SCHEMAS["read"]
    public ToolDefinition Definition { get; } = ToolDefinition.Function("read",
        """Read a text file or an image (PNG, JPEG, GIF or WebP, up to 3000 px on a side: you see the """
        + """picture), or list a directory. Paths are relative to the working directory. Output lines start """
        + """with a line number and a tab; that prefix is not part of the file. Long files come in pages of """
        + """about 30,000 characters; the last line says which offset to pass next. Reading outside the """
        + """working directory, or a file that may hold secrets (.env, keys, credentials), goes through """
        + """approval (see the system prompt); don't read secrets unless the task needs them.""",
        Json.Obj(("path", ToolDefinition.Property("string")),
                 ("offset", ToolDefinition.Property("integer", """First line to show, 1-based (default 1)""")),
                 ("limit", ToolDefinition.Property("integer", """Lines to show (default 2000)"""))),
        "path");

    // Python: loop.py Agent.dispatch (getattr(self.local, name)(**args))
    public Task<ToolOutput> InvokeAsync(ToolCall call, CancellationToken ct) => RunAsync(ToolArgs.Bind<ReadArgs>(call.Input), ct);

    /// <summary>A string, or a list of text and image blocks.</summary>
    // Python: tools.py LocalTools.read
    public async Task<ToolOutput> RunAsync(ReadArgs args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var p = policy.Resolve(args.Path);
        if (policy.IsPrivate(p))
        {
            var decision = await approval.RequestAsync(new ApprovalRequest(ApprovalKind.Read, p, IsSecret: SecretClassifier.IsSecret(p)), ct);
            if (!decision.Allowed)
                throw new ToolException("User denied this read (outside the project, or a file that may hold secrets)." + decision.Reason);
        }
        if (Directory.Exists(p))
        {
            return ToolOutput.Of(string.Join("\n", Directory.EnumerateFileSystemEntries(p)
                .Select(x => Path.GetFileName(x) + (Directory.Exists(x) ? "/" : "")).Order(StringComparer.Ordinal)));
        }

        if (!File.Exists(p))
            throw new ToolException("Not a regular file.");
        var mtime = FileReadTracker.MtimeOf(p); // before the read: a change during it then fails the next edit instead of passing for this version
        var data = await Task.Run(() => RegularFileReader.ReadCapped(p, ImageBudget.MaxFile)).WaitAsync(ct) ?? throw new ToolException("File is over 5 MB. Use bash (head, tail, grep) to look at parts of it; "
            + "for an image, make a smaller copy.");
        seen.Record(p, mtime);
        if (ImageSniffer.Read(data) is { } image)
        {
            var (kind, w, h) = image;
            if (w == 0 || h == 0 || Math.Max(w, h) > ImageBudget.MaxPixels)
            {
                throw new ToolException($"Can't show this image ({(w != 0 && h != 0 ? $"{w}x{h} px" : "size unreadable")}): images go "
                    + $"to the model only up to {ImageBudget.MaxPixels} px on a side. Make a smaller copy with bash and read "
                    + $"that (macOS: sips -Z {ImageBudget.MaxPixels} <in> --out <out>; elsewhere ImageMagick or Pillow).");
            }

            if (kind == "image/png" && PngValidator.Damage(data) is { } why)
            {
                throw new ToolException($"This PNG is damaged ({why}), so the model's API would refuse it. Fix what wrote it, "
                    + "or convert the source again another way, and read that.");
            }

            var b64 = Convert.ToBase64String(data);
            if (held.WouldExceed(b64.Length)) // every request sends them all again
            {
                throw new ToolException($"This conversation already holds {(held.Held / 1e6).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} MB of images, and mini stops at "
                    + $"{ImageBudget.MaxHeld / 1_000_000} MB. Ask the user to /compact or start a new session, or make "
                    + "a smaller copy (fewer pixels, or JPEG) and read that.");
            }

            held.Add(b64.Length);
            return ToolOutput.OfBlocks([TextBlock.Create($"{Path.GetFileName(p)}: {kind}, {w}x{h} px"), ImageBlock.Create(kind, b64)]);
        }
        var lines = LineSplitter.Split(Lossy.GetString(data));
        long start = Math.Max(args.Offset, 1), size = 0;
        var shown = new List<string>();
        for (var i = start; i <= lines.Count && i < start + Math.Max(args.Limit, 1); i++)
        {
            var numbered = $"{i,5}\t{lines[(int)i - 1]}";
            if (shown.Count > 0 && size + numbered.Length > OutputLimit.Max - 200) // page here, never let the output cap cut the middle
                break;
            shown.Add(numbered);
            size += numbered.Length + 1;
        }
        var end = start + shown.Count - 1;
        var more = end < lines.Count ? $"\n[lines {start}-{end} of {lines.Count}; continue with offset={end + 1}]" : "";
        return ToolOutput.Of(string.Join("\n", shown) + more);
    }
}
