using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;

namespace Mini.Tests.Interaction;

/// <summary>Mini.Core shows its work through <see cref="IAgentOutput"/> alone: these tests run it on a recording output, with no terminal.</summary>
public class OutputStreamObserverTests
{
    // C#-only: the observer is the adapter from the transport's events to the output
    [Fact]
    public void TheObserverPassesTextThinkingAndRetriesOn()
    {
        var output = new RecordingOutput();
        var observer = new OutputStreamObserver(output);

        observer.OnStarted();
        observer.OnText("a");
        observer.OnThinking("b");
        observer.OnRetrying(new IOException("down"), 2);

        Assert.Equal(["text: a", "thinking: b", "end: ", "warn: request failed (down); retrying in 2s"], output.Events);
    }

    // C#-only: a whole turn with a tool call, as the recording output saw it
    [Fact]
    public async Task ATurnWithAToolCallIsShownThroughTheOutputPort()
    {
        using var ws = new Workspace();
        File.WriteAllText(Path.Combine(ws.Root, "a.txt"), "contents of a");
        var output = new RecordingOutput();
        var (agent, _) = ws.Make([
            Reply("tool_use", Text("looking"), Tool("read", Json.Obj(("path", "a.txt")))), Reply(Text("done"))], ui: output);

        await agent.TurnAsync("read a.txt", ct: TestContext.Current.CancellationToken);

        Assert.Equal(["text: looking", "end: ", "tool: read", "result:     1\tcontents of a", "text: done", "end: "], output.Events);
    }

    // C#-only: a subagent's output is its lead's, one level in
    [Fact]
    public void AChildSharesTheLogOneLevelIn()
    {
        var output = new RecordingOutput();

        output.Child().Note("n");

        Assert.Equal(["child:note: n"], output.Events);
    }
}
