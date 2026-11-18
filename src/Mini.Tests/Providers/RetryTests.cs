using System.Diagnostics;
using System.Text.Json.Nodes;
using Anthropic.Exceptions;
using Anthropic.Models;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;
using static Mini.Tests.Providers.ProvidersHelpers;

namespace Mini.Tests.Providers;

/// <summary>The retry loop of RetryingModelClient (waits 2, 4 and 8 s, on the runtime's fake clock) and TransientErrorClassifier.IsTransient.</summary>
public class RetryTests
{
    // Python test: test_a_transient_failure_is_retried_and_leaves_no_trace
    // Python replaces time.sleep and checks waits == [2, 4, 8]; the C# wait is Task.Delay on the runtime's TimeProvider, so
    // the fake clock is moved on while the turn runs, and the waits are checked as the fake time that passed.
    [Fact]
    public async Task ATransientFailureIsRetriedAndLeavesNoTrace()
    {
        var ct = TestContext.Current.CancellationToken;
        using var ws = new Workspace();

        // three failures after the stream started (an error event, a cut-off, a dropped connection), then a whole answer
        var overloaded = StreamError(ErrorType.OverloadedError);
        var paused = ReplyWith([Text("searching"), Json.Obj(("type", "server_tool_use"), ("id", "srvtoolu_1"), ("name", "web_search"),
            ("input", Json.Obj(("query", "x"))))], "pause_turn");
        var (agent, fake) = ws.Make([paused, new Broken(Reply(Text("half an ans")), overloaded), new Broken(Reply(Text("half"))),
            new Broken(Reply(Text("hal")), new HttpRequestException("peer closed connection")), Reply(Text("whole"))]);
        agent.Transcript.PendingNotes.Add("note");
        string stop;
        TimeSpan elapsed;
        string warnings;
        using (var console = new ConsoleCapture())
        {
            (stop, elapsed) = await TurnWithClockAsync(ws, agent, ct);
            warnings = console.Err;
        }

        Assert.Equal("end_turn", stop);
        Assert.Equal("whole", agent.FinalText());
        Assert.InRange(elapsed, TimeSpan.FromSeconds(13.9), TimeSpan.FromSeconds(30)); // 2 + 4 + 8 s
        Assert.Equal(3, CountOf("retrying in", warnings));
        Assert.Contains("retrying in 2s", warnings);
        Assert.Contains("retrying in 4s", warnings);
        Assert.Contains("retrying in 8s", warnings);
        Assert.All(fake.Requests.Skip(2), r => Assert.Equal(fake.Requests[1].ToJsonString(), r.ToJsonString())); // each try sends exactly the same request
        Assert.Equal([Role.User, Role.System, Role.Assistant, Role.Assistant], agent.Messages.Select(m => m.Role)); // the pause, the answer
        Assert.Equal(["meta", "msg", "msg", "usage", "usage", "msgs"], agent.Session.Read().Select(e => e.Kind.Value));
        TranscriptAssert.AssertAppendOnly(fake.Requests);

        // three retries, then the turn ends as before
        (agent, fake) = ws.Make(Enumerable.Repeat<object>(new Broken(Reply(Text("x")), StatusError(500, ErrorType.ApiError)), 4));
        using (var console = new ConsoleCapture())
        {
            (stop, elapsed) = await TurnWithClockAsync(ws, agent, ct);
        }
        Assert.Equal("error", stop);
        Assert.Equal(4, fake.Requests.Count);
        Assert.InRange(elapsed, TimeSpan.FromSeconds(13.9), TimeSpan.FromSeconds(30));
        Assert.Equal([Role.User, Role.Assistant], agent.Messages.Select(m => m.Role)); // the placeholder
    }

    // Python test: test_a_transient_failure_is_retried_and_leaves_no_trace (second half: the failures that are never retried)
    // C# note: the fake throws a plain script item before onStart (the SDK has retried by then); a Broken one comes after it.
    [Fact]
    public async Task ARefusedRequestOrOneThatNeverStartedIsNotRetried()
    {
        var ct = TestContext.Current.CancellationToken;
        using var ws = new Workspace();
        object[] never =
        [
            StatusError(429, ErrorType.RateLimitError), // before the stream: the SDK already retried it
            new HttpRequestException("connection refused"),
            StatusError(529, ErrorType.OverloadedError),
            new Broken(Reply(Text("x")), StatusError(400, ErrorType.InvalidRequestError)),
            new Broken(Reply(Text("x")), StreamError(ErrorType.InvalidRequestError)),
            new Broken(Reply(Text("x")), new InvalidOperationException("529")),
            // what `mini -p ... | head -1` hits once head has its line: our own IO failure, not the network's
            new Broken(Reply(Text("x")), new IOException("Broken pipe")),
        ];
        var clock = Stopwatch.StartNew();
        using var console = new ConsoleCapture();
        foreach (var failure in never)
        {
            var (agent, fake) = ws.Make([failure, Reply(Text("never"))]);

            Assert.Equal("error", await agent.TurnAsync("hi", ct: ct));
            Assert.Single(fake.Requests); // one billed request, not four
        }
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1.5), $"no retry wait expected, took {clock.Elapsed}"); // Python: waits == []
        Assert.DoesNotContain("retrying in", console.Err);
    }

    // Python test: test_a_transient_failure_is_retried_and_leaves_no_trace (last part: Ctrl-C while it waits to retry)
    [Fact]
    public async Task CtrlCWhileWaitingToRetryInterruptsTheTurn()
    {
        using var ws = new Workspace();
        var (agent, fake) = ws.Make([new Broken(Reply(Text("x")), StreamError(ErrorType.OverloadedError)), Reply(Text("never"))]);
        using var cts = new CancellationTokenSource();
        using var console = new ConsoleCapture();

        var turn = agent.TurnAsync("hi", ct: cts.Token); // the fake clock stands still: the 2 s wait never ends by itself
        while (!console.Err.Contains("retrying in 2s", StringComparison.Ordinal))
        {
            Assert.False(turn.IsCompleted, "the turn ended before it waited to retry");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        cts.Cancel(); // inside the first wait
        var stop = await turn;

        Assert.Equal("interrupted", stop);
        Assert.Single(fake.Requests);
        Assert.Equal([Role.User, Role.Assistant], agent.Messages.Select(m => m.Role));
    }

    // Python test: test_with_the_real_sdk_only_a_failure_after_the_stream_starts_gets_retried
    // Adapted: the Python test runs the Python SDK against a local server. Here the classification TransientErrorClassifier.IsTransient makes
    // for each exception kind it handles is tested directly (what the SDK and our own callbacks raise).
    [Fact]
    public void WithTheRealSdkOnlyAFailureAfterTheStreamStartsGetsRetried_Transient()
    {
        Exception[] transient =
        [
            StatusError(429, ErrorType.RateLimitError),
            StatusError(500), StatusError(502), StatusError(503), StatusError(529, ErrorType.OverloadedError),
            new AnthropicRateLimitException(new HttpRequestException("x")) { StatusCode = System.Net.HttpStatusCode.TooManyRequests, ResponseBody = "{}" },
            new Anthropic5xxException(new HttpRequestException("x")) { StatusCode = System.Net.HttpStatusCode.InternalServerError, ResponseBody = "{}" },
            StreamError(ErrorType.OverloadedError), StreamError(ErrorType.RateLimitError),
            StreamError(ErrorType.ApiError), StreamError(ErrorType.TimeoutError),
            StatusError(200, ErrorType.OverloadedError), // an error type carried on an HTTP 200
            new AnthropicIOException("connection broke", new HttpRequestException("x")),
            new HttpRequestException("the connection broke while reading the response"),
            new StreamCutException("the stream ended before the response was complete"),
            new OperationCanceledException("the HTTP client's timeout", new TimeoutException()),
            new TaskCanceledException("timed out", new TimeoutException()),
        ];
        foreach (var e in transient)
            Assert.True(TransientErrorClassifier.IsTransient(e), $"{e.GetType().Name} ({e.Message}) should be retried");
    }

    // Python test: test_with_the_real_sdk_only_a_failure_after_the_stream_starts_gets_retried (what is never retried)
    [Fact]
    public void WithTheRealSdkOnlyAFailureAfterTheStreamStartsGetsRetried_NotTransient()
    {
        var own = new HttpRequestException("our callback failed");
        own.Data["mini.own"] = true; // OwnFailure.Ours marks what a UI write or onStart threw
        var ownCut = new StreamCutException("ours");
        ownCut.Data["mini.own"] = true;
        Exception[] never =
        [
            StatusError(400, ErrorType.InvalidRequestError), StatusError(401, ErrorType.AuthenticationError),
            StatusError(403, ErrorType.PermissionError), StatusError(404, ErrorType.NotFoundError),
            StatusError(413), StatusError(422), // request_too_large
            StatusError(200), // a status error with nothing in it
            StreamError(ErrorType.InvalidRequestError), StreamError(ErrorType.AuthenticationError),
            StreamError(ErrorType.PermissionError), StreamError(ErrorType.NotFoundError), StreamError(ErrorType.BillingError),
            StreamError(null),
            new IOException("Broken pipe"), // a bare IOException is ours: the request methods wrap the network's as HttpRequestException
            new InvalidOperationException("529"),
            new OperationCanceledException("cancelled"),
            new AnthropicInvalidDataException("bad data", new Exception("x")),
            new AnthropicException("anything else", new Exception("x")),
            own, ownCut,
        ];
        foreach (var e in never)
            Assert.False(TransientErrorClassifier.IsTransient(e), $"{e.GetType().Name} ({e.Message}) should not be retried");
    }

    /// <summary>Runs the turn and moves the runtime's fake clock on, a second at a time, so the retry waits end at once. The time that passed is the waits.</summary>
    static async Task<(string Stop, TimeSpan Waited)> TurnWithClockAsync(Workspace ws, Agent agent, CancellationToken ct)
    {
        var start = ws.Runtime.Time.GetUtcNow();
        var turn = agent.TurnAsync("hi", ct: ct);
        while (!turn.IsCompleted)
        {
            ws.Runtime.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(1, ct);
        }
        return (await turn, ws.Runtime.Time.GetUtcNow() - start);
    }

    static int CountOf(string needle, string haystack) =>
        (haystack.Length - haystack.Replace(needle, "").Length) / needle.Length;
}
