using System.Text;
using Mini.Tests.Fakes;
using static Mini.Tests.Tools.ToolsHelpers;

namespace Mini.Tests.Tools;

/// <summary>read, write, edit: the freshness rules, line-ending handling, paging and the special-file refusal.</summary>
public class FileToolsTests
{
    // Python test: test_edit_requires_fresh_read
    [Fact]
    public async Task EditRequiresFreshRead()
    {
        using var ws = new Workspace();
        var tools = Auto(ws);
        var f = ws.Write("f.txt", "one two");

        var e = await Assert.ThrowsAsync<ToolException>(() => tools.EditAsync("f.txt", "one", "1", ct: TestContext.Current.CancellationToken));
        Assert.Matches("Read the file", e.Message);

        await tools.ReadAsync("f.txt", ct: TestContext.Current.CancellationToken);
        Touch1(f);
        e = await Assert.ThrowsAsync<ToolException>(() => tools.EditAsync("f.txt", "one", "1", ct: TestContext.Current.CancellationToken));
        Assert.Matches("changed", e.Message);

        await tools.ReadAsync("f.txt", ct: TestContext.Current.CancellationToken);
        await tools.EditAsync("f.txt", "one", "1", ct: TestContext.Current.CancellationToken);
        Assert.Equal("1 two", File.ReadAllText(f));
    }

    // Python test: test_edit_rejects_ambiguous_match
    [Fact]
    public async Task EditRejectsAmbiguousMatch()
    {
        using var ws = new Workspace();
        var tools = Auto(ws);
        var f = ws.Write("f.txt", "a a");
        await tools.ReadAsync("f.txt", ct: TestContext.Current.CancellationToken);

        var e = await Assert.ThrowsAsync<ToolException>(() => tools.EditAsync("f.txt", "a", "b", ct: TestContext.Current.CancellationToken));
        Assert.Matches("2 times", e.Message);

        await tools.EditAsync("f.txt", "a", "b", replaceAll: true, ct: TestContext.Current.CancellationToken);
        Assert.Equal("b b", File.ReadAllText(f));
    }

    // Python test: test_write_will_not_clobber_unread_file
    [Fact]
    public async Task WriteWillNotClobberUnreadFile()
    {
        using var ws = new Workspace();
        var f = ws.Write("f.txt", "keep");

        var e = await Assert.ThrowsAsync<ToolException>(() => Auto(ws).WriteAsync("f.txt", "gone", TestContext.Current.CancellationToken));

        Assert.Matches("Read it first", e.Message);
        Assert.Equal("keep", File.ReadAllText(f));
    }

    // Python test: test_write_needs_current_version_and_edit_needs_old
    [Fact]
    public async Task WriteNeedsCurrentVersionAndEditNeedsOld()
    {
        using var ws = new Workspace();
        var tools = Auto(ws);
        var f = ws.Write("f.txt", "v1");
        await tools.ReadAsync("f.txt", ct: TestContext.Current.CancellationToken);
        Touch1(f);

        var e = await Assert.ThrowsAsync<ToolException>(() => tools.WriteAsync("f.txt", "v2", TestContext.Current.CancellationToken));
        Assert.Matches("current version", e.Message);

        await tools.ReadAsync("f.txt", ct: TestContext.Current.CancellationToken);
        e = await Assert.ThrowsAsync<ToolException>(() => tools.EditAsync("f.txt", "", "x", replaceAll: true, ct: TestContext.Current.CancellationToken));
        Assert.Matches("empty", e.Message);
    }

    // Python test: test_edit_error_says_bash_reads_dont_count
    [Fact]
    public async Task EditErrorSaysBashReadsDontCount()
    {
        using var ws = new Workspace();
        ws.Write("a.txt", "x");

        var e = await Assert.ThrowsAsync<ToolException>(() => Auto(ws).EditAsync("a.txt", "x", "y", ct: TestContext.Current.CancellationToken));

        Assert.Matches("read tool.*bash output", e.Message);
    }

    // Python test: test_edit_tolerates_mixed_endings_and_trailing_spaces
    [Fact]
    public async Task EditToleratesMixedEndingsAndTrailingSpaces()
    {
        using var ws = new Workspace();
        var f = ws.Path("m.txt");
        File.WriteAllBytes(f, "one\r\n  two  \nthree\r\nfour\n"u8.ToArray());
        var tools = Auto(ws);
        await tools.ReadAsync("m.txt", ct: TestContext.Current.CancellationToken);

        var result = await tools.EditAsync("m.txt", "  two\nthree", "  2\n3", ct: TestContext.Current.CancellationToken);
        Assert.Contains("ignoring", result);
        Assert.Equal("one\r\n  2\n3\r\nfour\n"u8.ToArray(), File.ReadAllBytes(f));

        await tools.EditAsync("m.txt", "  2 \n3", "", ct: TestContext.Current.CancellationToken); // deleting whole lines leaves no blank line
        Assert.Equal("one\r\nfour\n"u8.ToArray(), File.ReadAllBytes(f));
    }

    // Python test: test_edit_miss_points_at_the_closest_line_and_loose_ties_refuse
    [Fact]
    public async Task EditMissPointsAtTheClosestLineAndLooseTiesRefuse()
    {
        using var ws = new Workspace();
        const string original = "def run(x):\n    return x\n\npad \npad\n";
        var f = ws.Write("c.py", original);
        var tools = Auto(ws);
        await tools.ReadAsync("c.py", ct: TestContext.Current.CancellationToken);

        var e = await Assert.ThrowsAsync<ToolException>(() => tools.EditAsync("c.py", "def run(y):", "z", ct: TestContext.Current.CancellationToken));
        Assert.Matches("Closest: line 1", e.Message);
        e = await Assert.ThrowsAsync<ToolException>(() => tools.EditAsync("c.py", "pad  ", "q", ct: TestContext.Current.CancellationToken));
        Assert.Matches("2 places", e.Message);

        Assert.Equal(original, File.ReadAllText(f));
    }

    // Python test: test_edit_keeps_windows_line_endings
    [Fact]
    public async Task EditKeepsWindowsLineEndings()
    {
        using var ws = new Workspace();
        var f = ws.Path("w.txt");
        File.WriteAllBytes(f, "a\r\nb\r\nc\r\n"u8.ToArray());
        var tools = Auto(ws);
        await tools.ReadAsync("w.txt", ct: TestContext.Current.CancellationToken);

        await tools.EditAsync("w.txt", "a\nb", "a\nB", ct: TestContext.Current.CancellationToken);

        Assert.Equal("a\r\nB\r\nc\r\n"u8.ToArray(), File.ReadAllBytes(f));
    }

    // Python test: test_overwriting_an_executable_needs_approval
    [Fact]
    public async Task OverwritingAnExecutableNeedsApproval()
    {
        using var ws = new Workspace();
        var name = OperatingSystem.IsWindows() ? "run.bat" : "run.sh"; // Windows has no execute bit: a file runs by its extension
        var f = ws.Write(name, "echo hi");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(f, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute); // 0o755
        }

        var tools = Headless(ws);
        await tools.ReadAsync(name, ct: TestContext.Current.CancellationToken);

        var e = await Assert.ThrowsAsync<ToolException>(() => tools.WriteAsync(name, "rm -rf ~", TestContext.Current.CancellationToken));

        Assert.Matches("denied", e.Message);
        Assert.Equal("echo hi", File.ReadAllText(f));
    }

    // Python test: test_editing_a_plain_project_file_never_asks
    [Fact]
    public async Task EditingAPlainProjectFileNeverAsks()
    {
        using var ws = new Workspace();
        var f = ws.Write("app.js", "const a = 1;\n");
        var tools = Headless(ws);
        await tools.ReadAsync("app.js", ct: TestContext.Current.CancellationToken);

        await tools.EditAsync("app.js", "a = 1", "a = 2", ct: TestContext.Current.CancellationToken);

        Assert.Equal("const a = 2;\n", File.ReadAllText(f));
    }

    // Python test: test_read_refuses_huge_and_special_files
    [Fact]
    public async Task ReadRefusesHugeAndSpecialFiles()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "/dev/zero is POSIX");
        using var ws = new Workspace();

        var e = await Assert.ThrowsAsync<ToolException>(() => Auto(ws).ReadAsync("/dev/zero", ct: TestContext.Current.CancellationToken));

        Assert.Matches("regular file", e.Message);
    }

    // C#-only: the 5 MB cap is a separate branch in ReadAsync from the special-file refusal.
    [Fact]
    public async Task ReadRefusesAFileOverFiveMegabytes()
    {
        using var ws = new Workspace();
        File.WriteAllBytes(ws.Path("huge.bin"), new byte[5_000_001]);

        var e = await Assert.ThrowsAsync<ToolException>(() => Auto(ws).ReadAsync("huge.bin", ct: TestContext.Current.CancellationToken));

        Assert.Matches("File is over 5 MB", e.Message);
    }

    // Python test: test_read_pages_before_the_output_cap
    [Fact]
    public async Task ReadPagesBeforeTheOutputCap()
    {
        using var ws = new Workspace();
        ws.Write("big.py", string.Join("\n", Enumerable.Range(0, 600).Select(i => $"x_{i} = '{new string('a', 70)}'")) + "\n");
        var tools = Auto(ws);

        var first = ReadText(await tools.ReadAsync("big.py", ct: TestContext.Current.CancellationToken));
        Assert.True(first.Length < 30_000);
        Assert.DoesNotContain("[... ", first);
        var nxt = int.Parse(first[(first.LastIndexOf("offset=", StringComparison.Ordinal) + "offset=".Length)..].TrimEnd(']'));
        var second = ReadText(await tools.ReadAsync("big.py", nxt, ct: TestContext.Current.CancellationToken));

        Assert.StartsWith($"{nxt}\t", second.Split('\n')[0].Trim());
        var negative = ReadText(await tools.ReadAsync("big.py", limit: -5, ct: TestContext.Current.CancellationToken));
        Assert.True(negative.Count(c => c == '\n') >= 1);
    }

    // Python test: test_crlf_edge_cases
    [Fact]
    public async Task CrlfEdgeCases()
    {
        using var ws = new Workspace();
        var tools = Auto(ws);
        var f = ws.Path("m.txt");
        File.WriteAllBytes(f, "header\r\nb\nc\n"u8.ToArray());
        await tools.ReadAsync("m.txt", ct: TestContext.Current.CancellationToken);
        await tools.EditAsync("m.txt", "b\nc", "B\nC", ct: TestContext.Current.CancellationToken);
        Assert.Equal("header\r\nB\nC\n"u8.ToArray(), File.ReadAllBytes(f));

        var g = ws.Path("w.txt");
        File.WriteAllBytes(g, "a\r\nb\r\n"u8.ToArray());
        await tools.ReadAsync("w.txt", ct: TestContext.Current.CancellationToken);
        await tools.EditAsync("w.txt", "a\nb", "x\r\ny", ct: TestContext.Current.CancellationToken);
        Assert.Equal("x\r\ny\r\n"u8.ToArray(), File.ReadAllBytes(g));

        await tools.ReadAsync("w.txt", ct: TestContext.Current.CancellationToken);
        await tools.WriteAsync("w.txt", "one\ntwo\n", TestContext.Current.CancellationToken);
        Assert.Equal("one\r\ntwo\r\n"u8.ToArray(), File.ReadAllBytes(g));
    }

    // Python test: test_files_are_utf8_whatever_the_locale
    [Fact]
    public async Task FilesAreUtf8WhateverTheLocale()
    {
        using var ws = new Workspace();
        var tools = Auto(ws);

        await tools.WriteAsync("u.txt", "naïve → ✓\n", TestContext.Current.CancellationToken);
        Assert.Equal(Encoding.UTF8.GetBytes("naïve → ✓\n"), File.ReadAllBytes(ws.Path("u.txt")));
        Assert.Contains("naïve → ✓", ReadText(await tools.ReadAsync("u.txt", ct: TestContext.Current.CancellationToken)));

        await tools.EditAsync("u.txt", "→ ✓", "← ✗", ct: TestContext.Current.CancellationToken);
        Assert.Equal(Encoding.UTF8.GetBytes("naïve ← ✗\n"), File.ReadAllBytes(ws.Path("u.txt")));
        Assert.Equal("u.txt:1: naïve ← ✗", await tools.FindAsync(contains: "✗", ct: TestContext.Current.CancellationToken));
    }

    // C#-only: edit refuses to rewrite bytes it could not decode (Strict decoder); Python's edit does the same via utf-8 errors.
    [Fact]
    public async Task EditRefusesAFileThatIsNotValidUtf8()
    {
        using var ws = new Workspace();
        var f = ws.Path("latin1.txt");
        byte[] bytes = [(byte)'a', 0xE9, (byte)'b'];
        File.WriteAllBytes(f, bytes);
        var tools = Auto(ws);
        await tools.ReadAsync("latin1.txt", ct: TestContext.Current.CancellationToken);

        var e = await Assert.ThrowsAsync<ToolException>(() => tools.EditAsync("latin1.txt", "a", "z", ct: TestContext.Current.CancellationToken));

        Assert.Matches("not valid UTF-8", e.Message);
        Assert.Equal(bytes, File.ReadAllBytes(f));
    }

    // C#-only: bad argument shapes come back as ToolException("Bad arguments: …") from the dispatcher (strict schemas make them rare).
    [Fact]
    public async Task CallRejectsUnexpectedAndMistypedArguments()
    {
        using var ws = new Workspace();
        var tools = Auto(ws);

        var e = await Assert.ThrowsAsync<ToolException>(() => tools.CallAsync("read", Json.Obj(("path", "x"), ("bogus", 1)), TestContext.Current.CancellationToken));
        Assert.Equal("Bad arguments: unexpected `bogus`", e.Message);
        e = await Assert.ThrowsAsync<ToolException>(() => tools.CallAsync("read", Json.Obj(("offset", 1)), TestContext.Current.CancellationToken));
        Assert.Equal("Bad arguments: `path` is required and must be a string", e.Message);
        e = await Assert.ThrowsAsync<ToolException>(() => tools.CallAsync("edit", Json.Obj(("path", "x"), ("old", "a"), ("new", "b"), ("replace_all", "yes")), TestContext.Current.CancellationToken));
        Assert.Equal("Bad arguments: `replace_all` must be a boolean", e.Message);
        e = await Assert.ThrowsAsync<ToolException>(() => tools.CallAsync("nope", Json.Obj(), TestContext.Current.CancellationToken));
        Assert.Equal("Tool nope is not available in this session.", e.Message); // the old LocalTools-level "Unknown tool" is the executor's message now
    }
}
