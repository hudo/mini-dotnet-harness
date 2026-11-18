using Mini.Tests.Fakes;

namespace Mini.Tests.Tools;

/// <summary>Each tool's argument record: the values it reads and the exact "Bad arguments: …" text of each failure.</summary>
// Python test helper: tests/test_mini.py (the TypeError text Agent.dispatch turns into "Bad arguments")
public class ToolArgsTests
{
    static System.Text.Json.Nodes.JsonObject Obj(params (string, System.Text.Json.Nodes.JsonNode?)[] kv) => Json.Obj(kv);

    static string Fails<T>(System.Text.Json.Nodes.JsonObject input) where T : IToolArgs<T> =>
        Assert.Throws<ToolException>(() => ToolArgs.Bind<T>(input)).Message;

    [Fact]
    public void ReadTakesAPathAndDefaultsTheRest()
    {
        Assert.Equal(new ReadArgs("a.txt", 1, 2000), ToolArgs.Bind<ReadArgs>(Obj(("path", "a.txt"))));
        Assert.Equal(new ReadArgs("a.txt", 5, 10), ToolArgs.Bind<ReadArgs>(Obj(("path", "a.txt"), ("offset", 5), ("limit", 10))));
    }

    [Fact]
    public void WriteEditFindBashDefaults()
    {
        Assert.Equal(new WriteArgs("a", "c"), ToolArgs.Bind<WriteArgs>(Obj(("path", "a"), ("content", "c"))));
        Assert.Equal(new EditArgs("a", "o", "n", false), ToolArgs.Bind<EditArgs>(Obj(("path", "a"), ("old", "o"), ("new", "n"))));
        Assert.Equal(new EditArgs("a", "o", "n", true), ToolArgs.Bind<EditArgs>(Obj(("path", "a"), ("old", "o"), ("new", "n"), ("replace_all", true))));
        Assert.Equal(new FindArgs("*", "", "."), ToolArgs.Bind<FindArgs>(Obj()));
        Assert.Equal(new BashArgs("ls", 120, false), ToolArgs.Bind<BashArgs>(Obj(("command", "ls"))));
        Assert.Equal(new BashArgs("ls", 5, true), ToolArgs.Bind<BashArgs>(Obj(("command", "ls"), ("timeout", 5), ("background", true))));
    }

    [Fact]
    public void AnUnexpectedKeyIsNamed()
    {
        Assert.Equal("Bad arguments: unexpected `bogus`", Fails<ReadArgs>(Obj(("path", "x"), ("bogus", 1))));
        Assert.Equal("Bad arguments: unexpected `file`", Fails<WriteArgs>(Obj(("path", "x"), ("content", "y"), ("file", "z"))));
        Assert.Equal("Bad arguments: unexpected `cwd`", Fails<BashArgs>(Obj(("command", "ls"), ("cwd", "/"))));
    }

    [Fact]
    public void TheFirstUnexpectedKeyInTheModelsOrderWinsAndComesBeforeAMissingValue()
    {
        Assert.Equal("Bad arguments: unexpected `b`", Fails<ReadArgs>(Obj(("b", 1), ("a", 2))));
        Assert.Equal("Bad arguments: unexpected `zzz`", Fails<ReadArgs>(Obj(("zzz", 1)))); // path is missing too, but unknown keys are checked first
    }

    [Fact]
    public void AMissingOrMistypedStringNamesTheKey()
    {
        Assert.Equal("Bad arguments: `path` is required and must be a string", Fails<ReadArgs>(Obj(("offset", 1))));
        Assert.Equal("Bad arguments: `path` is required and must be a string", Fails<ReadArgs>(Obj(("path", 5))));
        Assert.Equal("Bad arguments: `content` is required and must be a string", Fails<WriteArgs>(Obj(("path", "x"))));
        Assert.Equal("Bad arguments: `name` must be a string", Fails<FindArgs>(Obj(("name", 1))));
    }

    [Fact]
    public void AMistypedNumberOrFlagNamesTheKey()
    {
        Assert.Equal("Bad arguments: `offset` must be an integer", Fails<ReadArgs>(Obj(("path", "x"), ("offset", "1"))));
        Assert.Equal("Bad arguments: `limit` must be an integer", Fails<ReadArgs>(Obj(("path", "x"), ("limit", 1.5))));
        Assert.Equal("Bad arguments: `replace_all` must be a boolean", Fails<EditArgs>(Obj(("path", "x"), ("old", "a"), ("new", "b"), ("replace_all", "yes"))));
        Assert.Equal("Bad arguments: `background` must be a boolean", Fails<BashArgs>(Obj(("command", "ls"), ("background", 1))));
    }

    [Fact]
    public void ValuesAreCheckedInTheOrderTheToolReadsThem()
    {
        // path, then offset, then limit: with all three wrong, path is what is reported
        Assert.Equal("Bad arguments: `path` is required and must be a string", Fails<ReadArgs>(Obj(("offset", "x"), ("limit", "y"))));
        Assert.Equal("Bad arguments: `offset` must be an integer", Fails<ReadArgs>(Obj(("path", "p"), ("offset", "x"), ("limit", "y"))));
        // command, then timeout, then background
        Assert.Equal("Bad arguments: `timeout` must be an integer", Fails<BashArgs>(Obj(("command", "ls"), ("timeout", "x"), ("background", "y"))));
    }

    [Fact]
    public void TaskAskUserAndGoalDoneKeepTheirOwnMessagesAndIgnoreUnknownKeys()
    {
        Assert.Equal("Bad arguments: prompt", Fails<TaskArgs>(Obj()));
        Assert.Equal("Bad arguments: prompt", Fails<TaskArgs>(Obj(("prompt", 1))));
        Assert.Equal(new TaskArgs("p", null), ToolArgs.Bind<TaskArgs>(Obj(("prompt", "p"), ("extra", 1))));
        Assert.Equal(new TaskArgs("p", "m"), ToolArgs.Bind<TaskArgs>(Obj(("prompt", "p"), ("model", "m"))));
        Assert.Equal(new TaskArgs("p", null), ToolArgs.Bind<TaskArgs>(Obj(("prompt", "p"), ("model", 3))));

        Assert.Equal("Bad arguments: question", Fails<AskUserArgs>(Obj()));
        var ask = ToolArgs.Bind<AskUserArgs>(Obj(("question", "q"), ("options", new System.Text.Json.Nodes.JsonArray("a", 2, null))));
        Assert.Equal("q", ask.Question);
        Assert.Equal(["a", "2", "None"], ask.Options);
        Assert.Empty(ToolArgs.Bind<AskUserArgs>(Obj(("question", "q"), ("options", "not a list"))).Options);

        Assert.Equal(new GoalDoneArgs(""), ToolArgs.Bind<GoalDoneArgs>(Obj()));
        Assert.Equal(new GoalDoneArgs("ran tests"), ToolArgs.Bind<GoalDoneArgs>(Obj(("evidence", "ran tests"))));
    }
}
