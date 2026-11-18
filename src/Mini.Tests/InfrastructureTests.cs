using System.Text.Json.Nodes;
using Mini.Tests.Fakes;
using Xunit.Sdk;
using static Mini.Tests.Fakes.FakeModelClient;

namespace Mini.Tests;

/// <summary>Checks of the shared test helpers themselves (no Python counterpart).</summary>
public class InfrastructureTests
{
    /// <summary>Remembers that the stream started, and passes everything on.</summary>
    sealed class StartObserver(IStreamObserver next) : IStreamObserver
    {
        public bool Started { get; private set; }
        public void OnStarted() { Started = true; next.OnStarted(); }
        public void OnText(string delta) => next.OnText(delta);
        public void OnThinking(string delta) => next.OnThinking(delta);
        public void OnRetrying(Exception error, int seconds) => next.OnRetrying(error, seconds);
    }

    [Fact]
    public async Task BrokenStreamShowsTextThenThrowsStreamCut()
    {
        using var cap = new ConsoleCapture();
        var fake = new FakeModelClient([new Broken(Reply(Text("partial")))]);
        var observer = new StartObserver(new OutputStreamObserver(new ConsoleAgentOutput()));
        var ex = await Assert.ThrowsAsync<StreamCutException>(() =>
            fake.StreamAsync(Request(), observer, TestContext.Current.CancellationToken));
        Assert.True(observer.Started);
        Assert.Contains("partial", cap.Out);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task ExceptionIsThrownBeforeOnStartAndRunningOutOfScriptFails()
    {
        var fake = new FakeModelClient([new InvalidOperationException("boom"), (JsonObject r) => (object)Reply(Text(Json.Str(r["model"])!))]);
        var observer = new StartObserver(new OutputStreamObserver(new ConsoleAgentOutput(silent: true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fake.StreamAsync(Request(), observer, TestContext.Current.CancellationToken));
        Assert.False(observer.Started);
        var reply = await fake.StreamAsync(Request("echo"), new OutputStreamObserver(new ConsoleAgentOutput(silent: true)), TestContext.Current.CancellationToken);
        Assert.Equal("echo", Json.Str(reply.ToJson()["content"]![0]!["text"]));
        Assert.False(reply.ToJson().ContainsKey("stop_sequence")); // nulls stripped, like Api
        await Assert.ThrowsAsync<XunitException>(() => fake.CreateAsync(Request(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ReplyKeepsFakePyKeyOrder()
    {
        var r = ReplyWith([Text("x")], transformations: new JsonArray());
        Assert.Equal(["id", "type", "role", "model", "content", "stop_reason", "stop_sequence", "usage", "input_transformations"],
            r.Select(p => p.Key));
    }

    [Fact]
    public void ConsoleCaptureScriptsInputAndRestores()
    {
        var before = Console.Out;
        using (var cap = new ConsoleCapture("one", "two"))
        {
            Assert.Equal("one", Console.ReadLine());
            Assert.Equal("two", Console.ReadLine());
            Assert.Null(Console.ReadLine());
            Console.Write("out"); Console.Error.Write("err");
            Assert.Equal("out", cap.Out);
            Assert.Equal("err", cap.Err);
        }
        Assert.Same(before, Console.Out);
    }

    [Fact]
    public void EnvScopeSetsAndRestores()
    {
        Assert.Null(Environment.GetEnvironmentVariable("MINI_MODEL")); // cleared by TestEnvironment
        using (new EnvScope(("MINI_MODEL", "x"), ("MINI_NET_HOME", null)))
        {
            Assert.Equal("x", Environment.GetEnvironmentVariable("MINI_MODEL"));
            Assert.Null(Environment.GetEnvironmentVariable("MINI_NET_HOME"));
        }
        Assert.Null(Environment.GetEnvironmentVariable("MINI_MODEL"));
        Assert.Equal(TestEnvironment.Home, Environment.GetEnvironmentVariable("MINI_NET_HOME"));
    }

    [Fact]
    public void GoldenFileWritesThenComparesAndNamesTheFirstDifferingLine()
    {
        const string name = "_selftest.txt";
        var path = Path.Combine(AppContext.BaseDirectory, "../../../Golden", name); // the source tree, from bin/Debug/net10.0
        try
        {
            using (new EnvScope(("MINI_UPDATE_GOLDEN", "1")))
                GoldenFile.Assert(name, "a\nb\n");
            Assert.True(File.Exists(path));
            GoldenFile.Assert(name, "a\nb\n");
            var failure = Assert.ThrowsAny<Exception>(() => GoldenFile.Assert(name, "a\nc\n"));
            Assert.Contains("line 2", failure.Message);
            Assert.Contains("expected: b", failure.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WorkspaceIsTheCwdAndMakeBuildsAnAgent()
    {
        string root;
        using (var ws = new Workspace())
        {
            root = ws.Root;
            Assert.Equal(root, Directory.GetCurrentDirectory());
            var (agent, fake) = ws.Make([Reply(Text("x"))]);
            Assert.Equal(1, fake.Remaining);
            Assert.Equal("claude-opus-5-5", agent.Model);
        }
        Assert.False(Directory.Exists(root));
    }
}
