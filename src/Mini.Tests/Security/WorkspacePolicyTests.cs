using Mini.Tests.Fakes;

namespace Mini.Tests.Security;

/// <summary>WorkspacePolicy over a real workspace (a temp directory that is the cwd), including real symlinks.</summary>
// Python test helper: tests/test_mini.py (the path-policy cases of test_files_that_run_later_need_approval and test_every_write_asks_when_started_in_home, run on the policy directly)
public class WorkspacePolicyTests
{
    static WorkspacePolicy Policy(Workspace ws, TestRuntime? runtime = null) => new(ws.Root, (runtime ?? ws.Runtime).Paths);

    [Fact]
    public void FilesInsideTheProjectAreInside()
    {
        using var ws = new Workspace();
        var policy = Policy(ws);

        Assert.True(policy.IsInside(ws.Path("notes.txt")));
        Assert.True(policy.IsInside(ws.Path("src/deep/app.py")));
        Assert.True(policy.IsInside(ws.Path("package.json.bak")));
    }

    [Theory]
    [InlineData(".git/config")]
    [InlineData(".GIT/hooks/pre-commit")]
    [InlineData("sub/.husky/pre-push")]
    [InlineData("mcp.json")]
    [InlineData("Makefile")]
    [InlineData("GNUmakefile")]
    [InlineData("deep/er/MCP.JSON")]
    [InlineData("docs/HEAD")]
    [InlineData("docs/config")]
    public void DotFoldersAndFilesThatRunLaterAreNotInside(string relative)
    {
        using var ws = new Workspace();

        Assert.False(Policy(ws).IsInside(ws.Path(relative)));
        Assert.True(Policy(ws).NeedsWriteApproval(ws.Path(relative)));
    }

    [Fact]
    public void ThePathsOutsideTheProjectAreNotInside()
    {
        using var ws = new Workspace();
        var policy = Policy(ws);

        Assert.False(policy.IsInside(System.IO.Path.GetDirectoryName(ws.Root)!));
        Assert.False(policy.IsInside(ws.Root)); // the folder itself has no parts below it: not a file to write
        Assert.False(policy.IsInside(ws.Root + "-sibling/x.txt")); // a shared prefix is not a parent: whole parts only
    }

    [Fact]
    public void NothingIsInsideWhenTheProjectIsTheHomeFolder()
    {
        using var ws = new Workspace();
        var started = new TestRuntime(o => o with { Home = ws.Root, UserProfile = ws.Root });

        Assert.False(Policy(ws, started).IsInside(ws.Path("notes.txt")));
    }

    [Fact]
    public void NothingIsInsideWhenTheHomeFolderIsUnknown()
    {
        using var ws = new Workspace();
        var homeless = new TestRuntime(o => o with { Home = null, UserProfile = null, UserProfileFolder = "" });

        Assert.False(Policy(ws, homeless).IsInside(ws.Path("notes.txt")));
    }

    [Fact]
    public void NothingIsInsideWhenTheHomeFolderIsInsideTheProject()
    {
        using var ws = new Workspace();
        Directory.CreateDirectory(ws.Path("fakehome"));
        var started = new TestRuntime(o => o with { Home = ws.Path("fakehome"), UserProfile = ws.Path("fakehome") });

        Assert.False(Policy(ws, started).IsInside(ws.Path("notes.txt")));
    }

    [Fact]
    public void ALinkInsideTheProjectThatLeadsOutsideIsOutside()
    {
        using var ws = new Workspace();
        var outside = ToolsOutside.Dir();
        try
        {
            File.WriteAllText(System.IO.Path.Combine(outside, "target.txt"), "x");
            File.CreateSymbolicLink(ws.Path("link.txt"), System.IO.Path.Combine(outside, "target.txt"));
            Directory.CreateSymbolicLink(ws.Path("dirlink"), outside);
            var policy = Policy(ws);

            Assert.False(policy.IsInside(policy.Resolve("link.txt")));
            Assert.False(policy.IsInside(policy.Resolve("dirlink/new.txt")));
            Assert.True(policy.IsPrivate(policy.Resolve("link.txt")));
        }
        finally { Directory.Delete(outside, recursive: true); }
    }

    [Fact]
    public void DotDotAfterALinkLeavesTheLinksTargetNotTheLinksFolder()
    {
        using var ws = new Workspace();
        var outside = ToolsOutside.Dir();
        try
        {
            var deep = System.IO.Path.Combine(outside, "a", "b");
            Directory.CreateDirectory(deep);
            Directory.CreateSymbolicLink(ws.Path("deeplink"), deep);
            var policy = Policy(ws);

            Assert.Equal(System.IO.Path.Combine(outside, "a", "x.txt"), policy.Resolve("deeplink/../x.txt"));
            Assert.False(policy.IsInside(policy.Resolve("deeplink/../x.txt")));
        }
        finally { Directory.Delete(outside, recursive: true); }
    }

    [Fact]
    public void ResolveExpandsTheTildeToTheHomeFolder()
    {
        using var ws = new Workspace();
        var home = ToolsOutside.Dir();
        try
        {
            var started = new TestRuntime(o => o with { Home = home, UserProfile = home });
            var policy = Policy(ws, started);

            Assert.Equal(home, policy.Resolve("~"));
            Assert.Equal(System.IO.Path.Combine(home, "x.txt"), policy.Resolve("~/x.txt"));
            Assert.True(policy.IsPrivate(policy.Resolve("~/x.txt")));
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    [Fact]
    public void ATildeWordOtherThanTheHomeLandsOutsideTheProject()
    {
        using var ws = new Workspace();
        var policy = Policy(ws);

        Assert.Equal("/~root/x", policy.Resolve("~root/x"));
        Assert.True(policy.IsPrivate(policy.Resolve("~root/x")));
        Assert.True(policy.IsPrivate(policy.Resolve("~+")));
    }

    [Fact]
    public void ARelativePathIsRelativeToTheWorkingDirectory()
    {
        using var ws = new Workspace();

        Assert.Equal(ws.Path("a/b.txt"), Policy(ws).Resolve("a/b.txt"));
        Assert.Equal(ws.Root, Policy(ws).Resolve("."));
    }

    [Fact]
    public void ReadingAsksOutsideTheProjectAndForSecretsAnywhere()
    {
        using var ws = new Workspace();
        var policy = Policy(ws);

        Assert.False(policy.IsPrivate(ws.Path("notes.txt")));
        Assert.False(policy.IsPrivate(ws.Root));
        Assert.False(policy.IsPrivate(ws.Path(".git/HEAD"))); // protected from writes, not from reads
        Assert.True(policy.IsPrivate(ws.Path(".env")));
        Assert.True(policy.IsPrivate(ws.Path("sub/id_rsa")));
        Assert.True(policy.IsPrivate(System.IO.Path.GetDirectoryName(ws.Root)!));
        Assert.False(policy.IsPrivate(ws.Path(".env.example")));
    }

    [Fact]
    public void BackgroundJobLogsAreReadableWithoutAsking()
    {
        using var ws = new Workspace();
        var policy = Policy(ws);
        var log = System.IO.Path.Combine(ws.Runtime.Paths.JobsDir, "20260101-000000-abc.log");

        Assert.False(policy.IsPrivate(PathResolver.RealPath(log)));
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void WritingOverAnExecutableNeedsApproval()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the execute bit is a POSIX mode");
        using var ws = new Workspace();
        var script = ws.Write("run.sh", "#!/bin/sh\n");
        var plain = ws.Write("plain.txt", "x");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(plain, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var policy = Policy(ws);

        Assert.True(policy.NeedsWriteApproval(script));
        Assert.False(policy.NeedsWriteApproval(plain));
        Assert.False(policy.NeedsWriteApproval(ws.Path("new.txt")));
    }

    // The Windows branches are gated by OperatingSystem.IsWindows(); their patterns are checked here on every platform.
    [Theory]
    [InlineData("/c/Users/me/x.txt", "c:/Users/me/x.txt")]
    [InlineData("/C/x", "C:/x")]
    [InlineData("/d", "d:/")]
    [InlineData("/d/", "d:/")]
    [InlineData("/etc/passwd", "/etc/passwd")] // a folder name longer than one letter is not a drive
    [InlineData("/c:/x", "/c:/x")]
    [InlineData("c/x", "c/x")]
    [InlineData("relative/x", "relative/x")]
    public void GitBashSpellsADriveAsASlashLetter(string path, string expected) =>
        Assert.Equal(expected, WorkspacePolicy.GitBashToWindows(path));

    [Theory]
    [InlineData(".env::$DATA")] // an NTFS stream
    [InlineData("PROGRA~1")] // an 8.3 short name
    [InlineData("config.")] // a trailing dot
    [InlineData("config ")] // a trailing space
    public void WindowsAliasesOfAFileAreSpotted(string part) => Assert.True(WorkspacePolicy.IsWindowsAlias(part));

    [Theory]
    [InlineData("config")]
    [InlineData("a.b")]
    [InlineData("a b")]
    [InlineData(".hidden")]
    public void OrdinaryNamesAreNotWindowsAliases(string part) => Assert.False(WorkspacePolicy.IsWindowsAlias(part));
}

/// <summary>A new directory outside the workspace, as the OS reports it (macOS's /var resolved).</summary>
static class ToolsOutside
{
    public static string Dir() => PathResolver.RealPath(Directory.CreateTempSubdirectory("mini-tests-out-").FullName);
}
