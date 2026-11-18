using System.Text.Json.Nodes;
using Mini.Tests.Fakes;

namespace Mini.Tests.Approval;

/// <summary>Shared pieces of the approval lane. The judge is faked through <see cref="IApprovalJudge"/>.</summary>
internal static class ApprovalHelpers
{
    /// <summary>A judge that answers with whatever the test says, and records what it was asked.</summary>
    public sealed class CannedJudge(Func<ApprovalRequest, JudgeVerdict> answer) : IApprovalJudge
    {
        public Task<JudgeVerdict> JudgeAsync(ApprovalRequest request, IJudgeContext context, CancellationToken ct) =>
            Task.FromResult(answer(request));
    }

    /// <summary>
    /// Python's <c>judged()</c>, minus the HTTP layer: settings in judge mode whose judge returns a canned verdict, and a
    /// <see cref="ToolKit"/> over the workspace that uses it. <c>Calls</c> records every (kind, detail) the judge was asked.
    /// </summary>
    public static (ApprovalSettings Approver, ToolKit Local, List<(string Kind, string Detail)> Calls) Judged(
        Workspace ws, string verdict, string why = "", bool interactive = false)
    {
        var calls = new List<(string, string)>();
        var approver = new ApprovalSettings(interactive: interactive, useJudge: true);
        var judge = new CannedJudge(request =>
        {
            calls.Add((request.Kind.Wire(), request.Detail));
            return new JudgeVerdict(Enum.Parse<JudgeChoice>(verdict, ignoreCase: true), why.Length > 0 ? why : $"{verdict} with confidence 1.00");
        });
        return (approver, ws.Tools(approver, judge: judge), calls);
    }

    /// <summary>What the judge is shown of this agent's session for an action (the state text of the request).</summary>
    public static string JudgeState(Agent agent, string kind, string detail) => JevJudge.BuildState(agent.JudgeContext, kind, detail);

    /// <summary>The same size the approval prompt measures (80x24 when there is no terminal).</summary>
    public static (int Width, int Height) ScreenSize()
    {
        try { return (Console.WindowWidth > 0 ? Console.WindowWidth : 80, Console.WindowHeight > 0 ? Console.WindowHeight : 24); }
        catch (Exception e) when (e is IOException or PlatformNotSupportedException) { return (80, 24); }
    }

    /// <summary>The most rows the approval prompt shows before it denies instead.</summary>
    public static int RowLimit() => Math.Max(Math.Min(40, ScreenSize().Height - 3), 10);

    /// <summary>The prompt with its row markers removed, so long text that wrapped over rows reads as one piece.</summary>
    public static string Flat(string prompt) => prompt.Replace("\n  │ ", "");

    /// <summary>The content of the first tool_result in the last message of a request.</summary>
    public static string? LastResult(JsonObject request) =>
        Json.Str(request["messages"]!.AsArray().Last()!["content"]![0]!["content"]);
}
