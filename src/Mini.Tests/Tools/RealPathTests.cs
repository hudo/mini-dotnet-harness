using Mini.Tests.Fakes;
using static Mini.Tests.Tools.ToolsHelpers;

namespace Mini.Tests.Tools;

/// <summary>Paths.RealPath against real symlinks, and what the tools do with the result. All C#-only: Python gets this
/// from Path.resolve(), so the Python suite never tests it separately.</summary>
public class RealPathTests
{
    // C#-only: a symlink inside the workspace that points outside is outside: writes through it ask.
    [Fact]
    public async Task ASymlinkInsideTheWorkspacePointingOutsideIsOutside()
    {
        using var ws = new Workspace();
        var outsideDir = OutsideDir();
        try
        {
            var target = System.IO.Path.Combine(outsideDir, "target.txt");
            File.WriteAllText(target, "private\n");
            File.CreateSymbolicLink(ws.Path("link.txt"), target);

            Assert.Equal(target, PathResolver.RealPath(ws.Path("link.txt")));

            var tools = Headless(ws);
            var e = await Assert.ThrowsAsync<ToolException>(() => tools.ReadAsync("link.txt", ct: TestContext.Current.CancellationToken));
            Assert.Matches("denied this read", e.Message);
            // the unread target can't be overwritten either: its mtime is keyed by the real path, and reading it was denied
            var w = await Assert.ThrowsAsync<ToolException>(() => tools.WriteAsync("link.txt", "x", TestContext.Current.CancellationToken));
            Assert.Matches("Read it first", w.Message);
            Assert.Equal("private\n", File.ReadAllText(target));
        }
        finally { Directory.Delete(outsideDir, recursive: true); }
    }

    // C#-only: a symlinked parent directory leads outside, so a new file "inside" it is outside.
    [Fact]
    public async Task ASymlinkedParentDirectoryLeadsOutside()
    {
        using var ws = new Workspace();
        var outsideDir = OutsideDir();
        try
        {
            Directory.CreateSymbolicLink(ws.Path("dirlink"), outsideDir);

            Assert.Equal(System.IO.Path.Combine(outsideDir, "new.txt"), PathResolver.RealPath(ws.Path("dirlink/new.txt")));

            var e = await Assert.ThrowsAsync<ToolException>(() => Headless(ws).WriteAsync("dirlink/new.txt", "x", TestContext.Current.CancellationToken));
            Assert.Matches("denied this write", e.Message);
            Assert.False(File.Exists(System.IO.Path.Combine(outsideDir, "new.txt")));
        }
        finally { Directory.Delete(outsideDir, recursive: true); }
    }

    // C#-only: a symlink to a folder inside the workspace is still inside (no false alarm), and resolves to the real name.
    [Fact]
    public async Task ASymlinkToAFolderInsideTheWorkspaceStaysInside()
    {
        using var ws = new Workspace();
        Directory.CreateDirectory(ws.Path("real"));
        Directory.CreateSymbolicLink(ws.Path("alias"), ws.Path("real"));

        Assert.Equal(ws.Path("real/f.txt"), PathResolver.RealPath(ws.Path("alias/f.txt")));
        await Headless(ws).WriteAsync("alias/f.txt", "x", TestContext.Current.CancellationToken);

        Assert.Equal("x", File.ReadAllText(ws.Path("real/f.txt")));
    }

    // C#-only: a dangling link keeps its (missing) target: it resolves, it does not throw, and a target outside asks.
    [Fact]
    public async Task ADanglingLinkResolvesToItsMissingTarget()
    {
        using var ws = new Workspace();
        var outsideDir = OutsideDir();
        try
        {
            var missing = System.IO.Path.Combine(outsideDir, "missing.txt");
            File.CreateSymbolicLink(ws.Path("dangling"), missing);
            File.CreateSymbolicLink(ws.Path("dangling-in"), ws.Path("not-yet.txt"));

            Assert.Equal(missing, PathResolver.RealPath(ws.Path("dangling")));
            Assert.Equal(ws.Path("not-yet.txt"), PathResolver.RealPath(ws.Path("dangling-in")));

            var e = await Assert.ThrowsAsync<ToolException>(() => Headless(ws).WriteAsync("dangling", "x", TestContext.Current.CancellationToken));
            Assert.Matches("denied this write", e.Message);
            Assert.False(File.Exists(missing));
            // a dangling link to a not-yet-existing file inside the project writes the target, not the link
            await Headless(ws).WriteAsync("dangling-in", "made", TestContext.Current.CancellationToken);
            Assert.Equal("made", File.ReadAllText(ws.Path("not-yet.txt")));
        }
        finally { Directory.Delete(outsideDir, recursive: true); }
    }

    // C#-only: `..` is applied to the already-resolved directory, so `link/..` leaves the link's target, not the link's folder.
    [Fact]
    public void DotDotAfterASymlinkLeavesTheLinkTarget()
    {
        using var ws = new Workspace();
        var outsideDir = OutsideDir();
        try
        {
            var inner = Directory.CreateDirectory(System.IO.Path.Combine(outsideDir, "inner")).FullName;
            Directory.CreateSymbolicLink(ws.Path("link"), inner);

            Assert.Equal(outsideDir, PathResolver.RealPath(ws.Path("link/..")));
            Assert.Equal(System.IO.Path.Combine(outsideDir, "x.txt"), PathResolver.RealPath(ws.Path("link/../x.txt")));
        }
        finally { Directory.Delete(outsideDir, recursive: true); }
    }

    // C#-only: a plain `..` through real folders collapses, and one that climbs out of the workspace asks.
    [Fact]
    public async Task APathThroughDotDotIsResolvedBeforeJudging()
    {
        using var ws = new Workspace();
        Directory.CreateDirectory(ws.Path("a/b"));

        Assert.Equal(ws.Path("f.txt"), PathResolver.RealPath(ws.Path("a/b/../../f.txt")));
        await Headless(ws).WriteAsync("a/b/../../f.txt", "in", TestContext.Current.CancellationToken);
        Assert.Equal("in", File.ReadAllText(ws.Path("f.txt")));

        var e = await Assert.ThrowsAsync<ToolException>(() => Headless(ws).WriteAsync("a/../../escape.txt", "x", TestContext.Current.CancellationToken));
        Assert.Matches("denied this write", e.Message);
    }

    // C#-only: `..` at the root stays at the root, as on disk; a relative path is taken from the process cwd.
    [Fact]
    public void DotDotAtTheRootStaysAtTheRootAndRelativePathsStartAtTheCwd()
    {
        using var ws = new Workspace();

        Assert.Equal("/", PathResolver.RealPath("/../.."));
        Assert.Equal(ws.Path("sub/f"), PathResolver.RealPath("sub/f"));
        Assert.Equal(ws.Root, PathResolver.RealPath("."));
    }

    // C#-only: a loop of links fails closed with an IOException instead of hanging; the tools turn it into "ask".
    [Fact]
    public async Task ALoopOfLinksFailsClosed()
    {
        using var ws = new Workspace();
        File.CreateSymbolicLink(ws.Path("a"), ws.Path("b"));
        File.CreateSymbolicLink(ws.Path("b"), ws.Path("a"));

        Assert.Throws<IOException>(() => PathResolver.RealPath(ws.Path("a")));
        // for a command's words, an unresolvable path means "can't tell: ask", so a headless run denies it
        var e = await Assert.ThrowsAsync<ToolException>(() => Headless(ws).BashAsync("cat a", ct: TestContext.Current.CancellationToken));
        Assert.Matches("denied this command", e.Message);
    }

    // C#-only: the protected-name and secret checks look at the resolved path, so a link named innocently to a protected file asks.
    [Fact]
    public async Task ALinkToAProtectedFileInsideTheProjectAsks()
    {
        using var ws = new Workspace();
        ws.Write("Makefile", "all:\n");
        File.CreateSymbolicLink(ws.Path("notes.txt"), ws.Path("Makefile"));
        var tools = Headless(ws);
        await tools.ReadAsync("notes.txt", ct: TestContext.Current.CancellationToken);

        var e = await Assert.ThrowsAsync<ToolException>(() => tools.WriteAsync("notes.txt", "pwned", TestContext.Current.CancellationToken));

        Assert.Matches("denied this write", e.Message);
        Assert.Equal("all:\n", File.ReadAllText(ws.Path("Makefile")));
    }
}
