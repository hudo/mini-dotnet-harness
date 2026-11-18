using Mini.Tests.Fakes;
using static Mini.Tests.Tools.ToolsHelpers;

namespace Mini.Tests.Tools;

/// <summary>find: names and contents without a shell, the result caps, and files that can't be read.</summary>
public class FindToolTests
{
    // Python test: test_find_lists_names_and_greps_contents_without_asking
    [Fact]
    public async Task FindListsNamesAndGrepsContentsWithoutAsking()
    {
        using var ws = new Workspace();
        ws.Write("src/a.py", "x = 1\nTIMEOUT = 5\n");
        ws.Write("src/b.txt", "TIMEOUT = 9\n");
        ws.Write(".git/c.py", "TIMEOUT = 0\n");
        File.WriteAllBytes(ws.Path("blob.py"), "TIMEOUT\0\u0001"u8.ToArray());
        var t = Headless(ws); // headless: anything gated would be denied

        Assert.Equal(["blob.py", "src/a.py"], (await t.FindAsync("*.py", ct: TestContext.Current.CancellationToken)).Split('\n'));
        Assert.Equal("src/a.py:2: TIMEOUT = 5", await t.FindAsync("*.py", "TIMEOUT", ct: TestContext.Current.CancellationToken));
        Assert.Equal(["src/a.py:2: TIMEOUT = 5", "src/b.txt:1: TIMEOUT = 9"],
            (await t.FindAsync(contains: @"TIMEOUT = \d", path: "src", ct: TestContext.Current.CancellationToken)).Split('\n'));
        Assert.Equal("src/b.txt:1: TIMEOUT = 9", await t.FindAsync(contains: "TIMEOUT", path: "src/b.txt", ct: TestContext.Current.CancellationToken)); // one file works too
        var e = await Assert.ThrowsAsync<ToolException>(() => t.FindAsync(path: "nope", ct: TestContext.Current.CancellationToken));
        Assert.Matches("No such path", e.Message);
        e = await Assert.ThrowsAsync<ToolException>(() => t.FindAsync("**/*.py", ct: TestContext.Current.CancellationToken));
        Assert.Matches("file names only", e.Message);
        Assert.Equal("[no matches]", await t.FindAsync("*.md", ct: TestContext.Current.CancellationToken));
        // Python raises re.error; C# turns a bad regex into a ToolException("error: …") the model sees (same text path in the loop).
        e = await Assert.ThrowsAsync<ToolException>(() => t.FindAsync(contains: "(", ct: TestContext.Current.CancellationToken));
        Assert.StartsWith("error:", e.Message);
    }

    // Python test: test_find_stops_at_500_results
    [Fact]
    public async Task FindStopsAt500Results()
    {
        using var ws = new Workspace();
        ws.Write("big.txt", string.Concat(Enumerable.Repeat("hit\n", 600)));

        var output = await Headless(ws).FindAsync(contains: "hit", ct: TestContext.Current.CancellationToken);

        Assert.Equal(500, output.Count(c => c == '\n'));
        Assert.EndsWith("narrow the search]", output);
    }

    // Python test: test_find_stops_before_the_output_cap_cuts_the_middle
    [Fact]
    public async Task FindStopsBeforeTheOutputCapCutsTheMiddle()
    {
        using var ws = new Workspace();
        ws.Write("wide.txt", string.Concat(Enumerable.Repeat("hit" + new string('x', 290) + "\n", 400)));

        var output = await Headless(ws).FindAsync(contains: "hit", ct: TestContext.Current.CancellationToken);

        Assert.True(output.Length < 30_000);
        Assert.EndsWith("narrow the search]", output);
    }

    // Python test: test_find_skips_an_unreadable_file_and_keeps_the_rest
    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")] // chmod 0 doesn't make a file unreadable on Windows
    public async Task FindSkipsAnUnreadableFileAndKeepsTheRest()
    {
        Assert.SkipWhen(Environment.UserName == "root", "root reads files whatever their mode");
        using var ws = new Workspace();
        var locked = ws.Write("locked.txt", "hit\n");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        ws.Write("open.txt", "hit\n");
        try
        {
            Assert.Equal("open.txt:1: hit", await Headless(ws).FindAsync(contains: "hit", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
    }

    // Python test: test_find_survives_a_broken_symlink
    [Fact]
    public async Task FindSurvivesABrokenSymlink()
    {
        using var ws = new Workspace();
        File.CreateSymbolicLink(ws.Path("dead.py"), ws.Path("missing.py"));
        ws.Write("ok.py", "hit\n");

        Assert.Equal("ok.py:1: hit", await Headless(ws).FindAsync("*.py", "hit", ct: TestContext.Current.CancellationToken));
    }

    // C#-only: find follows a symlink to judge the target, so a link inside the project to a secret outside never has its contents searched.
    [Fact]
    public async Task FindDoesNotSearchTheContentsOfALinkedSecret()
    {
        using var ws = new Workspace();
        var outsideDir = OutsideDir();
        try
        {
            var secret = System.IO.Path.Combine(outsideDir, "id_rsa");
            File.WriteAllText(secret, "TOKEN=hidden\n");
            File.CreateSymbolicLink(ws.Path("innocent.txt"), secret);
            ws.Write("plain.txt", "TOKEN=visible\n");

            var output = await Headless(ws).FindAsync(contains: "TOKEN", ct: TestContext.Current.CancellationToken);

            Assert.Equal("plain.txt:1: TOKEN=visible", output);
        }
        finally { Directory.Delete(outsideDir, recursive: true); }
    }

    // C#-only: find skips symlinked folders (os.walk's followlinks=False) and the dependency folders, whatever they hold.
    [Fact]
    public async Task FindSkipsSymlinkedFoldersAndDependencyFolders()
    {
        using var ws = new Workspace();
        ws.Write("real/a.txt", "hit\n");
        ws.Write("node_modules/b.txt", "hit\n");
        ws.Write("__pycache__/c.txt", "hit\n");
        Directory.CreateSymbolicLink(ws.Path("linked"), ws.Path("real"));

        Assert.Equal("real/a.txt:1: hit", await Headless(ws).FindAsync(contains: "hit", ct: TestContext.Current.CancellationToken));
    }
}
