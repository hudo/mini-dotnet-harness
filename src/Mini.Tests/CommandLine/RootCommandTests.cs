using Mini.Tests.Fakes;

namespace Mini.Tests.CommandLine;

/// <summary>The flags, as argparse had them: defaults, one value per <c>--allow</c>, "@x" taken literally, errors with exit code 2, no <c>--version</c>.</summary>
public class RootCommandTests
{
    static async Task<(int Exit, CliOptions? Options, string Err)> Parse(params string[] argv)
    {
        using var console = new ConsoleCapture();
        CliOptions? seen = null;
        var exit = await RootCommandFactory.InvokeAsync(argv, o =>
        {
            seen = o;
            return Task.FromResult(0);
        });
        return (exit, seen, console.Err);
    }

    // Python: cli.py main (the defaults of the parser)
    [Fact]
    public async Task TheDefaultsAreArgparses()
    {
        var (exit, o, _) = await Parse();

        Assert.Equal(0, exit);
        Assert.Equal(new CliOptions(null, false, null, null, "high", false, null, [], null, null, 25, null, false).Allow, o!.Allow);
        Assert.Equal(("high", 25, null, false, false), (o.Effort, o.MaxTurns, o.Budget, o.Json, o.Headless));
        Assert.Null(o.Prompt);
        Assert.Null(o.Permissions);
    }

    // Python: cli.py main (-p, -c, -r and the long names)
    [Fact]
    public async Task ShortAndLongFlagsBindTheSameOptions()
    {
        var (_, o, _) = await Parse("-p", "hi", "-c", "-r", "abc", "--model", "m", "--effort", "max", "--permissions", "yes",
            "--goal", "g", "--verify", "make", "--max-turns", "7", "--budget", "0", "--json", "--models");

        Assert.Equal(("hi", true, "abc", "m", "max", "yes"), (o!.Prompt, o.Continue, o.Resume, o.Model, o.Effort, o.Permissions));
        Assert.Equal(("g", "make", 7, 0.0, true, true), (o.Goal, o.Verify, o.MaxTurns, o.Budget, o.Json, o.ListModels));
        Assert.True(o.Headless);
    }

    // Python: cli.py main (--allow is action="append": one value per occurrence)
    [Fact]
    public async Task AllowIsRepeatable()
    {
        var (_, o, _) = await Parse("--allow", "git", "--allow", "make");

        Assert.Equal(["git", "make"], o!.Allow);
    }

    [Fact]
    public async Task AllowTakesOneValuePerOccurrence()
    {
        var (exit, o, err) = await Parse("--allow", "git", "make");

        Assert.Equal(2, exit);
        Assert.Null(o);
        Assert.StartsWith("mini: error: ", err);
    }

    // Python: cli.py main (argparse takes "@x" literally; System.CommandLine would read a response file)
    [Fact]
    public async Task AtSignsAreTakenLiterally()
    {
        var (exit, o, _) = await Parse("-p", "@x", "--goal", "@y");

        Assert.Equal(0, exit);
        Assert.Equal(("@x", "@y"), (o!.Prompt, o.Goal));
    }

    // Python: cli.py main (argparse: the message on stderr, exit 2)
    [Theory]
    [InlineData("--bogus")]
    [InlineData("--version")]
    [InlineData("--permissions", "maybe")]
    [InlineData("--max-turns", "many")]
    [InlineData("--budget", "lots")]
    [InlineData("--prompt")]
    public async Task AParseErrorPrintsTheMiniErrorLineAndExitsTwo(params string[] argv)
    {
        var (exit, o, err) = await Parse(argv);

        Assert.Equal(2, exit);
        Assert.Null(o);
        Assert.StartsWith("mini: error: ", err); // a message may run on over several lines (the choices of --permissions)
    }

    // Python: cli.py main (--permissions has choices: ask, judge, yes)
    [Fact]
    public async Task PermissionsTakesAskJudgeOrYes()
    {
        foreach (var mode in new[] { "ask", "judge", "yes" })
            Assert.Equal(mode, (await Parse("--permissions", mode)).Options!.Permissions);
    }
}
