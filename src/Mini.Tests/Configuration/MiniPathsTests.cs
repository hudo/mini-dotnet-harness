using Mini.Tests.Fakes;

namespace Mini.Tests.Configuration;

/// <summary>Where the files are, from the options alone.</summary>
// C#-only: Python's HOME and project_dir read the environment at import
public class MiniPathsTests
{
    // C#-only: MINI_NET_HOME is the home
    [Fact]
    public void NetHomeWinsOverTheProfileFolder()
    {
        var paths = new MiniPaths(new MiniOptions { NetHome = "/custom", UserProfileFolder = "/profile", Home = "/home" });

        Assert.Equal("/custom", paths.Home);
        Assert.Equal(Path.Combine("/custom", "jobs"), paths.JobsDir);
    }

    // C#-only: without MINI_NET_HOME it is ~/.mini-net, from the OS's profile folder, else HOME, else the current folder
    [Fact]
    public void TheDefaultHomeIsDotMiniNetInTheProfile()
    {
        Assert.Equal(Path.Combine("/profile", ".mini-net"), new MiniPaths(new MiniOptions { UserProfileFolder = "/profile", Home = "/home" }).Home);
        Assert.Equal(Path.Combine("/home", ".mini-net"), new MiniPaths(new MiniOptions { Home = "/home" }).Home);
        Assert.Equal(Path.Combine(".", ".mini-net"), new MiniPaths(new MiniOptions()).Home);
    }

    // C#-only: `~` is USERPROFILE on Windows and HOME elsewhere, else the OS's profile folder
    [Fact]
    public void TheUserHomeIsTheOsVariableThenTheProfileFolder()
    {
        var options = new MiniOptions { Home = "/home", UserProfile = "C:\\u", UserProfileFolder = "/profile" };

        Assert.Equal(OperatingSystem.IsWindows() ? "C:\\u" : "/home", new MiniPaths(options).UserHome);
        Assert.Equal("/profile", new MiniPaths(options with { Home = "", UserProfile = "" }).UserHome);
    }

    // C#-only: one folder per working directory, named by the first 12 hex digits of the real path's SHA-256, created on demand
    [Fact]
    public void TheProjectFolderIsKeyedByTheRealPath()
    {
        using var ws = new Workspace();
        var paths = ws.Runtime.Paths;

        var dir = paths.ProjectDir(ws.Root);

        Assert.True(Directory.Exists(dir));
        Assert.Equal(Path.Combine(paths.Home, "sessions"), Path.GetDirectoryName(dir));
        Assert.Matches("^[0-9a-f]{12}$", Path.GetFileName(dir));
        Assert.Equal(dir, paths.ProjectDir(ws.Root));
    }
}
