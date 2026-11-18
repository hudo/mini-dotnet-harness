using Mini.Tests.Fakes;

namespace Mini.Tests.Security;

/// <summary>The paths a shell command names, and whether any of them needs the user's say.</summary>
// Python test helper: tests/test_mini.py (test_bash_reading_outside_the_project_asks and its siblings, run on the inspector directly)
public class CommandPathInspectorTests
{
    static CommandPathInspector Inspector(Workspace ws, TestRuntime? runtime = null) =>
        new(new WorkspacePolicy(ws.Root, (runtime ?? ws.Runtime).Paths));

    [Fact]
    public void WordsAreTheArgumentsThatAreNotFlagsResolvedAgainstTheWorkingDirectory()
    {
        using var ws = new Workspace();

        var words = Inspector(ws).Words("cat -n a.txt sub/b.txt");

        Assert.Equal([ws.Path("a.txt"), ws.Path("sub/b.txt")], words);
    }

    [Fact]
    public void TheCommandItselfIsNotAPath()
    {
        using var ws = new Workspace();

        Assert.Empty(Inspector(ws).Words("ls")!);
        Assert.Empty(Inspector(ws).Words("ls -la")!);
    }

    [Fact]
    public void QuotedWordsAreOneWord()
    {
        using var ws = new Workspace();

        Assert.Equal([ws.Path("my file.txt")], Inspector(ws).Words("cat 'my file.txt'"));
    }

    [Fact]
    public void BadQuotingCannotBeSplit()
    {
        using var ws = new Workspace();

        Assert.Null(Inspector(ws).Words("cat 'unterminated"));
        Assert.True(Inspector(ws).NamesPrivate("cat 'unterminated")); // can't tell what it names: ask
    }

    [Fact]
    public void ANulByteIsAPathThatCannotExist()
    {
        using var ws = new Workspace();

        Assert.Null(Inspector(ws).Words("cat a\0b"));
        Assert.True(Inspector(ws).NamesPrivate("cat a\0b"));
    }

    [Fact]
    public void ALoopOfLinksCannotBeResolved()
    {
        using var ws = new Workspace();
        File.CreateSymbolicLink(ws.Path("a"), ws.Path("b"));
        File.CreateSymbolicLink(ws.Path("b"), ws.Path("a"));

        Assert.Null(Inspector(ws).Words("cat a"));
        Assert.True(Inspector(ws).NamesPrivate("cat a"));
    }

    [Theory]
    [InlineData("cat notes.txt")]
    [InlineData("ls -la")]
    [InlineData("head -5 src/app.py")]
    [InlineData("cat .env.example")]
    public void ProjectFilesAreNotPrivate(string command)
    {
        using var ws = new Workspace();

        Assert.False(Inspector(ws).NamesPrivate(command));
    }

    [Theory]
    [InlineData("cat /etc/passwd")]
    [InlineData("cat ../outside.txt")]
    [InlineData("cat .env")]
    [InlineData("cat sub/id_rsa")]
    [InlineData("cat ~/.ssh/config")]
    [InlineData("cat ~root/x")]
    [InlineData("head -n 1 /etc/hosts")]
    public void OutsideAndSecretPathsArePrivate(string command)
    {
        using var ws = new Workspace();

        Assert.True(Inspector(ws).NamesPrivate(command));
    }

    [Theory]
    [InlineData("cat *")]
    [InlineData("cat .e?v")]
    [InlineData("cat [.]env")]
    public void AGlobCouldMatchASecret(string command)
    {
        using var ws = new Workspace();

        Assert.True(Inspector(ws).NamesPrivate(command));
    }

    [Fact]
    public void ALinkToASecretOutsideIsPrivate()
    {
        using var ws = new Workspace();
        var outside = ToolsOutside.Dir();
        try
        {
            File.WriteAllText(System.IO.Path.Combine(outside, "target.txt"), "x");
            File.CreateSymbolicLink(ws.Path("innocent.txt"), System.IO.Path.Combine(outside, "target.txt"));

            Assert.True(Inspector(ws).NamesPrivate("cat innocent.txt"));
        }
        finally { Directory.Delete(outside, recursive: true); }
    }

    [Fact]
    public void ATildeIsTheUsersHomeNotTheProject()
    {
        using var ws = new Workspace();
        var home = ToolsOutside.Dir();
        try
        {
            var started = new TestRuntime(o => o with { Home = home, UserProfile = home });

            Assert.Equal([System.IO.Path.Combine(home, ".ssh/id_rsa")], Inspector(ws, started).Words("cat ~/.ssh/id_rsa"));
        }
        finally { Directory.Delete(home, recursive: true); }
    }
}
