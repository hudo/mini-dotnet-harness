using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;
using static Mini.Tests.Providers.ProvidersHelpers;

namespace Mini.Tests.Providers;

/// <summary>C#-only: the decorator on its own, without an Agent: what it asks of the observer, the clock and the wrapped client.</summary>
public class RetryingModelClientTests
{
    sealed class Recorder : IStreamObserver
    {
        public List<string> Events { get; } = [];
        public void OnStarted() => Events.Add("started");
        public void OnText(string delta) => Events.Add($"text:{delta}");
        public void OnThinking(string delta) => Events.Add($"thinking:{delta}");
        public void OnRetrying(Exception error, int seconds) => Events.Add($"retrying:{seconds}");
    }

    static async Task<T> WithClockAsync<T>(FakeTimeProvider time, Task<T> task)
    {
        while (!task.IsCompleted)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }
        return await task;
    }

    // C#-only: three failures after the start, then a whole reply; the waits are 2, 4 and 8 s of the given clock
    [Fact]
    public async Task RetriesAfterTheStreamStartedWithGrowingWaits()
    {
        var fake = new FakeModelClient([new Broken(Reply(Text("a"))), new Broken(Reply(Text("b"))), new Broken(Reply(Text("c"))), Reply(Text("whole"))]);
        var time = new FakeTimeProvider();
        var observer = new Recorder();
        var start = time.GetUtcNow();

        var response = await WithClockAsync(time, new RetryingModelClient(fake, time).StreamAsync(Request(), observer, TestContext.Current.CancellationToken));

        Assert.Equal("whole", response.Content.OfType<TextBlock>().Single().Text);
        Assert.Equal(["started", "text:a", "retrying:2", "started", "text:b", "retrying:4", "started", "text:c", "retrying:8", "started", "text:whole"], observer.Events);
        Assert.True(time.GetUtcNow() - start >= TimeSpan.FromSeconds(14));
        Assert.Equal(4, fake.Requests.Count);
    }

    // C#-only: the fourth failure is the caller's
    [Fact]
    public async Task TheLastAttemptsFailureIsThrown()
    {
        var fake = new FakeModelClient(Enumerable.Repeat<object>(new Broken(Reply(Text("x"))), 4));
        var time = new FakeTimeProvider();

        await Assert.ThrowsAsync<StreamCutException>(() =>
            WithClockAsync(time, new RetryingModelClient(fake, time).StreamAsync(Request(), new Recorder(), TestContext.Current.CancellationToken)));

        Assert.Equal(4, fake.Requests.Count);
    }

    // C#-only: a failure before the stream started, or one that is not transient, is thrown at once; so is a cancelled call
    [Fact]
    public async Task NothingIsRetriedBeforeTheStartOrWhenNotTransient()
    {
        var time = new FakeTimeProvider();
        foreach (var failure in new object[] { new HttpRequestException("refused"), new Broken(Reply(Text("x")), StatusError(400, Anthropic.Models.ErrorType.InvalidRequestError)) })
        {
            var fake = new FakeModelClient([failure, Reply(Text("never"))]);
            var observer = new Recorder();

            await Assert.ThrowsAnyAsync<Exception>(() => new RetryingModelClient(fake, time).StreamAsync(Request(), observer, TestContext.Current.CancellationToken));

            Assert.Single(fake.Requests);
            Assert.DoesNotContain(observer.Events, e => e.StartsWith("retrying", StringComparison.Ordinal));
        }
    }

    // C#-only: only the stream is retried; a whole response and a token count go through once
    [Fact]
    public async Task CreateAndCountAreNotRetried()
    {
        var fake = new FakeModelClient([new HttpRequestException("down")]) { TokenCount = 7 };
        var client = new RetryingModelClient(fake, new FakeTimeProvider());

        await Assert.ThrowsAsync<HttpRequestException>(() => client.CreateAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Equal(7, await client.CountTokensAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Single(fake.Creates);
        Assert.Single(fake.Counts);
    }
}
